using Melfa.Robot;
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

namespace Communication
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            connect();
        }


        public void connect()
        {
            using var robot = new MelfaRobot(
                "127.0.0.1",
                port: 10008
            );

            try
            {
                // 1. Kết nối TCP/IP
                robot.Connect();

              //  robot.OperationEnabled = true;
              //  robot.ServoOn = true;

             //   var p =  robot.CurrentPositionP;
                while (true)
                {
                    robot.ExecuteBlocking("MOV P1");
                    robot.ExecuteBlocking("MOV P2,50");
                }

             //   MessageBox.Show(" current point: " +p.ToString() );
              //  MessageBox.Show("Lỗi kết nối robot");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi kết nối robot: {ex.Message}");
            }
        }
    }
}