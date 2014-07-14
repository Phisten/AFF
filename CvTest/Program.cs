using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using Emgu.CV;
using Emgu.CV.UI;
using Emgu.Util;
//using Emgu.CV.GPU;
using Emgu.CV.Structure;

namespace CvTest
{
    class Program
    {
        static void Main(string[] args)
        {
            
            Emgu.CV.Image<Ycc, byte> srcImg = new Image<Ycc, byte>(@"D:\github\AFF\0104.jpg");
            ImageViewer view = new ImageViewer(srcImg);
            view.Show();

            Console.ReadLine();

        }
    }
}
