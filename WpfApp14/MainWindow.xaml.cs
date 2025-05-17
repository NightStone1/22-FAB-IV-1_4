using System.Text;
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
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    // Главное окно приложения
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent(); // Инициализация компонентов WPF
        }

        // Открытие окна чата
        private void btn_chat_Click(object sender, RoutedEventArgs e)
        {
            Chat chat = new Chat();
            chat.Show();
        }

        // Открытие окна UDP-клиента
        private void btn_udp_Click(object sender, RoutedEventArgs e)
        {
            UDPclient client = new UDPclient();
            client.Show();
        }
    }
}