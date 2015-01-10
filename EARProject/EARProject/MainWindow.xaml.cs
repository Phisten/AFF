using System;
using System.Collections.Generic;
using System.Linq;
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
using EARProject.IO;

namespace EARProject
{
    /// <summary>
    /// MainWindow.xaml 的互動邏輯
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }


        List<WebCam> webCamList = new List<WebCam>();
        WebCam webCam;

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            webCam = new WebCam(0);
            webCam.OnFrameGrabbed += webCam_OnFrameGrabbed;
        }

        void webCam_OnFrameGrabbed(object sender, Emgu.CV.Image<Emgu.CV.Structure.Bgr, byte> frame, long frameElapsed)
        {
            VideoBox.Source = new BitmapImage();
        }


    }
}
