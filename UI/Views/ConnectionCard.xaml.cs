using System.Windows;
using System.Windows.Controls;


namespace UI.Views
{
    /// <summary>
    /// Thẻ kết nối dùng lại cho mọi thiết bị. Các thuộc tính dưới đây cho phép khai báo trong XAML:
    /// &lt;views:ConnectionCard Title="Robot" Protocol="TCP/IP" Badge="RB" Address="192.168.1.10" Port="9001" /&gt;
    /// </summary>
    public partial class ConnectionCard : UserControl
    {
        public static readonly DependencyProperty TitleProperty =
                   DependencyProperty.Register(nameof(Title), typeof(string), typeof(ConnectionCard), new PropertyMetadata(""));

        public static readonly DependencyProperty ProtocolProperty =
            DependencyProperty.Register(nameof(Protocol), typeof(string), typeof(ConnectionCard), new PropertyMetadata(""));

        public static readonly DependencyProperty BadgeProperty =
            DependencyProperty.Register(nameof(Badge), typeof(string), typeof(ConnectionCard), new PropertyMetadata(""));

        public static readonly DependencyProperty AddressProperty =
            DependencyProperty.Register(nameof(Address), typeof(string), typeof(ConnectionCard), new PropertyMetadata(""));

        public static readonly DependencyProperty PortProperty =
            DependencyProperty.Register(nameof(Port), typeof(string), typeof(ConnectionCard), new PropertyMetadata(""));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public string Protocol
        {
            get { return (string)GetValue(ProtocolProperty); }
            set { SetValue(ProtocolProperty, value); }
        }

        public string Badge
        {
            get { return (string)GetValue(BadgeProperty); }
            set { SetValue(BadgeProperty, value); }
        }

        public string Address
        {
            get { return (string)GetValue(AddressProperty); }
            set { SetValue(AddressProperty, value); }
        }

        public string Port
        {
            get { return (string)GetValue(PortProperty); }
            set { SetValue(PortProperty, value); }
        }

        public ConnectionCard()
        {
            InitializeComponent();
        }
    }
}
