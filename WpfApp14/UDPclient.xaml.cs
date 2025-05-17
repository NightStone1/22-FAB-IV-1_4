using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WpfApp14
{
    /// <summary>
    /// Логика взаимодействия для UDPclient.xaml
    /// </summary>
    // Класс для отправки файлов по UDP
    public class FileTransferClientUDP
    {
        private string _serverAddress; // Адрес сервера
        private int _port; // Порт сервера
        private UdpClient _udpClient; // UDP клиент для передачи
        private const int MaxUdpPacketSize = 200000; // Максимальный размер UDP пакета

        public FileTransferClientUDP(string serverAddress, int port, UdpClient udpClient)
        {
            _serverAddress = serverAddress;
            _port = port;
            _udpClient = udpClient;
        }

        // Объединение двух байтовых массивов в один
        private byte[] Combine(byte[] first, byte[] second)
        {
            byte[] combined = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, combined, 0, first.Length);
            Buffer.BlockCopy(second, 0, combined, first.Length, second.Length);
            return combined;
        }

        // Основной метод отправки файла
        public async Task SendFile(string filePath)
        {
            try
            {
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Parse(_serverAddress), _port);
                string fileName = System.IO.Path.GetFileName(filePath);
                byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileName);

                // Проверка длины имени файла
                if (fileNameBytes.Length > 255)
                {
                    Console.WriteLine($"Error: file name exceeds 255 char limit.");
                    return;
                }

                // Формирование метаданных файла
                byte[] fileNameLengthBytes = new[] { (byte)fileNameBytes.Length };
                byte[] fileLengthBytes = BitConverter.GetBytes(new FileInfo(filePath).Length);
                byte[] metadata = Combine(fileNameLengthBytes, fileNameBytes);
                metadata = Combine(metadata, fileLengthBytes);

                // Отправка метаданных
                await _udpClient.SendAsync(metadata, metadata.Length, remoteEndPoint);

                // Чтение и отправка файла частями
                using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] buffer = new byte[MaxUdpPacketSize - 100]; // Буфер с запасом для заголовков
                    int bytesRead;
                    long totalBytesSent = 0;
                    int seqNumber = 0;

                    do
                    {
                        bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            // Добавление порядкового номера к данным
                            byte[] seqBytes = BitConverter.GetBytes(seqNumber++);
                            byte[] dataToSend = new byte[seqBytes.Length + bytesRead];
                            Buffer.BlockCopy(seqBytes, 0, dataToSend, 0, seqBytes.Length);
                            Buffer.BlockCopy(buffer, 0, dataToSend, seqBytes.Length, bytesRead);

                            await _udpClient.SendAsync(dataToSend, dataToSend.Length, remoteEndPoint);
                            totalBytesSent += bytesRead;
                            Console.WriteLine($"Sent: {totalBytesSent}/{fileStream.Length} bytes");
                        }
                    } while (bytesRead > 0);

                    // Отправка маркера конца файла
                    byte[] endOfFile = BitConverter.GetBytes(-1);
                    await _udpClient.SendAsync(endOfFile, endOfFile.Length, remoteEndPoint);
                }
                Console.WriteLine($"File '{fileName}' sent successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }

    // Окно UDP клиента (WPF)
    public partial class UDPclient : Window
    {
        private int port; // Порт для подключения
        private string savePath; // Путь для сохранения файлов
        private UdpClient udpServer; // UDP сервер
        private volatile bool isStarted = false; // Флаг работы сервера
        private string serverAddress; // Адрес сервера

        public UDPclient()
        {
            InitializeComponent();
            serverAddress = "127.0.0.1"; // Локальный адрес по умолчанию
            statusBar.Content = "Сервер не включен";
            port = 8888; // Порт по умолчанию
            savePath = "received_files";
            Directory.CreateDirectory(savePath); // Создание папки для файлов
        }

        // Прием данных от клиента
        private async Task ReceiveData()
        {
            try
            {
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, port);
                while (isStarted)
                {
                    var result = await udpServer.ReceiveAsync();
                    byte[] data = result.Buffer;
                    if (data.Length > 0)
                    {
                        await Task.Run(() => HandleData(data, remoteEP));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
            }
        }

        // Обработка полученных данных
        private async Task HandleData(byte[] data, IPEndPoint remoteEP)
        {
            try
            {
                // Проверка минимального размера пакета
                if (data.Length < 5) return;

                int fileNameLength = data[0]; // Длина имени файла
                if (data.Length < 1 + fileNameLength + 8) return;

                // Извлечение метаданных
                string fileName = Encoding.UTF8.GetString(data, 1, fileNameLength);
                long fileLength = BitConverter.ToInt64(data, 1 + fileNameLength);
                if (fileName == "") return;

                // Создание файла
                string filePath = System.IO.Path.Combine(savePath, fileName);
                Dispatcher.Invoke(() => {
                    statusBar.Content = ($"Приём файла '{fileName}'...");
                });

                using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                {
                    var receivedData = new SortedDictionary<int, byte[]>(); // Для упорядочивания пакетов
                    bool receivingFile = true;

                    while (receivingFile)
                    {
                        var result = await udpServer.ReceiveAsync();
                        byte[] filedata = result.Buffer;

                        // Проверка маркера конца файла
                        if (filedata.Length < 8)
                        {
                            int endOfFileMark = BitConverter.ToInt32(filedata, 0);
                            if (endOfFileMark == -1)
                            {
                                receivingFile = false;
                                continue;
                            }
                            continue;
                        }

                        // Извлечение порядкового номера
                        int seqNumber = BitConverter.ToInt32(filedata, 0);
                        byte[] fileDataSegment = new byte[filedata.Length - 4];
                        Buffer.BlockCopy(filedata, 4, fileDataSegment, 0, fileDataSegment.Length);
                        receivedData[seqNumber] = fileDataSegment;
                    }

                    // Запись данных в файл в правильном порядке
                    foreach (var pair in receivedData)
                    {
                        await fileStream.WriteAsync(pair.Value, 0, pair.Value.Length);
                    }

                    Dispatcher.Invoke(() =>
                    {
                        statusBar.Content = ($"Файл '{fileName}' получен успешно: {fileLength} байт.");
                    });
                }
            }
            catch (Exception ex)
            {
                // Обработка ошибок с обновлением UI
                Dispatcher.Invoke(() =>
                {
                    statusBar.Content = $"Ошибка обработки файла: {ex.Message}";
                });
                Console.WriteLine($"Ошибка обработки данных: {ex.Message}");
            }
        }

        // Обработчик кнопки запуска/останова сервера
        private async void btn_DisConnect_Click(object sender, RoutedEventArgs e)
        {
            if (!isStarted)
            {
                isStarted = true;
                udpServer = new UdpClient(port); // Создаем UDP-клиент
                statusBar.Content = ($"Сервер запущен на порту {port}. Ожидание данных...");
                await ReceiveData(); // Запускаем прием
            }
            else
            {
                isStarted = false;
                udpServer.Close(); // Останавливаем сервер
                statusBar.Content = ($"Сервер остановлен");
            }
        }

        // Открытие папки с полученными файлами
        private void btn_openDir_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer.exe", savePath); // Запуск проводника
        }

        // Метод отправки файла (обертка для FileTransferClientUDP)
        private async Task SendFile(string filePath)
        {
            try
            {
                FileTransferClientUDP client = new FileTransferClientUDP(serverAddress, port, udpServer);
                await client.SendFile(filePath); // Делегируем отправку
                Dispatcher.Invoke(() =>
                {
                    statusBar.Content = "Файл успешно отправлен";
                });
            }
            catch (Exception ex)
            {
                // Обработка ошибок отправки
                Dispatcher.Invoke(() =>
                {
                    statusBar.Content = "Ошибка при отправке файла";
                });
                MessageBox.Show($"Ошибка: {ex.Message}");
            }
        }

        // Обработчик массовой отправки файлов
        private async void btn_sendFiles_Click(object sender, RoutedEventArgs e)
        {
            if (isStarted)
            {
                if (sendList.Items.Count > 0)
                {
                    // Отправка всех файлов из списка
                    foreach (string file in sendList.Items)
                    {
                        statusBar.Content = $"Отправка файла: {System.IO.Path.GetFileName(file)}...";
                        await SendFile(file);
                    }
                    sendList.Items.Clear(); // Очистка списка
                }
                else
                {
                    MessageBox.Show("Список файлов для отправки пуст.");
                }
            }
        }

        // Выбор файлов для отправки через диалог
        private void btn_chooseFiles_Click(object sender, RoutedEventArgs e)
        {
            sendList.Visibility = Visibility.Visible;
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Multiselect = true; // Множественный выбор
            openFileDialog.Filter = "All files (*.*)|*.*"; // Все типы файлов
            openFileDialog.Title = "Выберите файлы";
            if (openFileDialog.ShowDialog() == true)
            {
                // Добавление выбранных файлов в список
                foreach (string fileName in openFileDialog.FileNames)
                {
                    sendList.Items.Add(fileName);
                }
            }
        }

        // Удаление файла из списка двойным кликом
        private void sendList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sendList.SelectedItem == null) return;
            sendList.Items.Remove(sendList.SelectedItem);
        }

        // Очистка ресурсов при закрытии окна
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            udpServer.Close();  // Корректное закрытие соединения
            udpServer.Dispose();
        }
    }
}
