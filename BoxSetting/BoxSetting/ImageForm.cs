using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BoxSetting
{
    public partial class ImageForm : Form
    {
        public ImageForm()
        {
            InitializeComponent();
            captureImageBox.Left = 0;
            captureImageBox.Top = 0;
        }
        public ImageForm(Size ImageSize)
        {
            InitializeComponent();
            captureImageBox.Left = 0;
            captureImageBox.Top = 0;
            this.ClientSize = ImageSize;
            this.captureImageBox.Size = ImageSize;
        }

        public void SetNewImage(Emgu.CV.IImage newImage)
        {
            captureImageBox.Image = newImage;
            if (newImage.Size != this.captureImageBox.Size)
            {
                this.ClientSize = newImage.Size;
                this.captureImageBox.Size = newImage.Size;
            }
        }
    }
}
