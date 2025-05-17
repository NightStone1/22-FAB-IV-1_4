using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WpfApp14
{
    /// <summary>
    /// Логика взаимодействия для Chat.xaml
    /// </summary>
    // Класс multicast чата
    public partial class Chat : Window
    {
        // Поля класса
        private UdpClient udpClient; // UDP клиент
        private IPAddress multicastIP; // Multicast адрес
        private int localPort; // Локальный порт
        private int remotePort; // Порт получателей
        private bool isConnected = false; // Флаг подключения
        private Thread receiveThread; // Поток приема сообщений

        public Chat()
        {
            InitializeComponent(); // Инициализация компонентов WPF
        }

        // Обработчик подключения/отключения
        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isConnected)
            {
                try
                {
                    // Парсинг параметров из UI
                    localPort = int.Parse(localPortTextBox.Text);
                    remotePort = int.Parse(remotePortTextBox.Text);
                    multicastIP = IPAddress.Parse(multicastIpTextBox.Text);

                    // Настройка UDP клиента
                    udpClient = new UdpClient(localPort);
                    udpClient.JoinMulticastGroup(multicastIP); // Вступление в группу

                    // Запуск потока приема
                    receiveThread = new Thread(new ThreadStart(ReceiveMessages));
                    receiveThread.IsBackground = true; // Фоновый поток
                    receiveThread.Start();

                    isConnected = true;
                    connectButton.Content = "Disconnect";
                    AddMessageToChat("Connected to multicast group " + multicastIP);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error connecting: " + ex.Message);
                }
            }
            else
            {
                Disconnect(); // Отключение
            }
        }

        // Метод отключения
        private void Disconnect()
        {
            try
            {
                if (udpClient != null)
                {
                    udpClient.DropMulticastGroup(multicastIP); // Выход из группы
                    udpClient.Close();
                }
                if (receiveThread != null && receiveThread.IsAlive)
                {
                    receiveThread.Abort(); // Остановка потока
                }
            }
            catch { }

            isConnected = false;
            connectButton.Content = "Connect";
            AddMessageToChat("Disconnected from multicast group");
        }

        // Отправка сообщения
        private void SendMessage()
        {
            if (!isConnected)
            {
                MessageBox.Show("Please connect first");
                return;
            }

            string message = messageTextBox.Text;
            if (!string.IsNullOrEmpty(message))
            {
                try
                {
                    byte[] data = Encoding.UTF8.GetBytes(message); // Кодирование
                    IPEndPoint endPoint = new IPEndPoint(multicastIP, remotePort);
                    udpClient.Send(data, data.Length, endPoint); // Отправка
                    AddMessageToChat("You: " + message);
                    messageTextBox.Clear();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error sending message: " + ex.Message);
                }
            }
        }

        // Прием сообщений в отдельном потоке
        private void ReceiveMessages()
        {
            IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

            while (isConnected)
            {
                try
                {
                    byte[] data = udpClient.Receive(ref remoteEndPoint); // Блокирующий прием
                    string message = Encoding.UTF8.GetString(data);

                    // Обновление UI из потока
                    Dispatcher.Invoke(() =>
                    {
                        AddMessageToChat($"{remoteEndPoint.Address}: {message}");
                    });
                }
                catch (SocketException)
                {
                    // Ожидаемая ошибка при разрыве соединения
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        AddMessageToChat($"Error: {ex.Message}");
                    });
                }
            }
        }

        // Добавление сообщения в чат
        private void AddMessageToChat(string message)
        {
            chatTextBox.AppendText(message + Environment.NewLine);
            chatTextBox.ScrollToEnd(); // Автопрокрутка
        }

        // Очистка при закрытии окна
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Disconnect(); // Корректное отключение
        }
    }    
}
