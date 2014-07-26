using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.Util;
using System.Diagnostics;
using System.Drawing.Imaging;

namespace LipsLocate
{
    public partial class Form1 : Form
    {
        //RS232
        rs232Form rs232form = new rs232Form();


        //640*480
        int DefaultWebCamIndex = 0;
        Rectangle roi_1 = new Rectangle(200, 200, 200, 200);

        internal Capture _capture = null;
        private bool _captureInProgress;
        CascadeClassifier faceHaar = new CascadeClassifier("haarcascades\\haarcascade_frontalface_default.xml");
        CascadeClassifier mouthHaar =new CascadeClassifier("haarcascades\\haarcascade_mcs_mouth.xml");
        Stopwatch watch;


        //teaNB webCam Size=640 480
        int imgWidth = 640;
        int imgHeight = 480;

        public Form1()
        {
            InitializeComponent();
            try
            {
                rs232form.mainForm = this;

                _capture = new Capture(DefaultWebCamIndex);
                _capture.ImageGrabbed += ProcessFrame;

                _capture.Start();
                _captureInProgress = !_captureInProgress;
                captureImageBox.Left = 0;
                captureImageBox.Top = 0;
                captureImageBox.Size = new Size(imgWidth, imgHeight);
                this.ClientSize = captureImageBox.Size;

                captureImageBox.FunctionalMode = Emgu.CV.UI.ImageBox.FunctionalModeOption.Minimum;
            }
            catch (NullReferenceException excpt)
            {
                MessageBox.Show(excpt.Message);
            }
        }

        static long FpsTest = 0;
        MCvFont cvFont = new MCvFont(Emgu.CV.CvEnum.FONT.CV_FONT_HERSHEY_COMPLEX, 1, 1);
        MCvFont cvFontBack = new MCvFont(Emgu.CV.CvEnum.FONT.CV_FONT_HERSHEY_COMPLEX, 0.5, 0.5);
        internal void ProcessFrame(object sender, EventArgs arg)
        {
            watch = Stopwatch.StartNew();

            Image<Bgr, Byte> frame = _capture.RetrieveBgrFrame();
            Image<Bgr, Byte> image = frame.Resize(captureImageBox.Size.Width, captureImageBox.Size.Height, Emgu.CV.CvEnum.INTER.CV_INTER_LINEAR);
            Image<Bgr, Byte> srcImage = image.Copy();


            //face Detector
            Rectangle faceROI, mouthROI;
            mouthDetector(image, out faceROI, out mouthROI);
            //20140603 追加嘴唇高度分界線
            for (int i = 0, length = image.Height; i < length; i += length / 10)
            {
                image.Draw(new LineSegment2D(new Point(0, i), new Point(image.Width, i)), new Bgr(100, 100, 100), 1);
            }


            //Blue Detector
            //BlueDetector(image);
            Image<Bgr, Byte> blueDetectImage = BlueDetector(srcImage,image, faceROI, mouthROI); 
            //captureImageBox.Image = image;
            captureImageBox.Image = image;


            if (FpsTest >= 1000)
            {
                FpsTest -= 1000;
            }


            watch.Stop();
            image.Draw("FPS=" + Math.Round(1000.0 / watch.ElapsedMilliseconds, 2).ToString(), ref cvFont, new Point(0, 30), new Bgr());
            string strMousePos =
                "(" + mousePos.X + "," + mousePos.Y + ")";
            image.Draw(strMousePos, ref cvFontBack, new Point(mousePos.X, mousePos.Y + 30), new Bgr(255, 255, 255));
            FpsTest += watch.ElapsedMilliseconds;


            rs232form.RS232_DataSend();
        }

        private Image<Bgr, byte> BlueDetector(Image<Bgr, byte> image, Image<Bgr, byte> drawImage, Rectangle faceROI, Rectangle mouthROI)
        {
            List<Rectangle> blues = new List<Rectangle>();
            Image<Ycc, byte> yccImg = image.Convert<Ycc, byte>();
            Image<Gray, byte> cbImg = yccImg[1];

            Rectangle ThresROI = faceROI;
            ThresROI.Height += 50;
            cbImg.ROI = ThresROI;
            Image<Gray, byte> greyThreshImg = cbImg.CopyBlank();
            int greyThreshValue = (int)Emgu.CV.CvInvoke.cvThreshold(cbImg.Ptr, greyThreshImg.Ptr, 0d, 255d,
                Emgu.CV.CvEnum.THRESH.CV_THRESH_BINARY_INV | Emgu.CV.CvEnum.THRESH.CV_THRESH_OTSU);

            //center blue Detect
            int widthStep = 20;
            int HeightOffset = 50;

            Rectangle centerROI = faceROI;
            if (faceROI.Width != 0)
            {
                centerROI = new Rectangle(faceROI.Width / 2 - widthStep, HeightOffset, widthStep * 2, faceROI.Height);
            }
            greyThreshImg.ROI = centerROI;

            Rectangle drawCenterROI = centerROI;
            drawCenterROI.Offset(faceROI.Location);
            drawImage.Draw(drawCenterROI, new Bgr(200, 150, 0), 2);

            int blobMaxHeight = 10;
            Rectangle blobMaxRectangle = new Rectangle(0,0,0,0);
            Emgu.CV.Cvb.CvBlobs resultingImgBlobs = new Emgu.CV.Cvb.CvBlobs();
            Emgu.CV.Cvb.CvBlobDetector bDetect = new Emgu.CV.Cvb.CvBlobDetector();
            uint numWebcamBlobsFound = bDetect.Detect(greyThreshImg, resultingImgBlobs);
            greyThreshImg.ROI = new Rectangle(0, 0, 0, 0);
            //取得最高的BLUB
            foreach (Emgu.CV.Cvb.CvBlob item in resultingImgBlobs.Values)
            {
                if (item.BoundingBox.Height > blobMaxHeight)
                {
                    blobMaxHeight = item.BoundingBox.Height;
                    blobMaxRectangle = item.BoundingBox;
                }   
            }


            //TEST HIT
            if (blobMaxRectangle.IntersectsWith(mouthROI))
            {
                //spoon Height OK
                rs232form.spoonState = '0';
            }
            else if (blobMaxRectangle.Top < mouthROI.Top)
            {
                //too high
                rs232form.spoonState = '1';
            }
            else
            {
                //too low
                rs232form.spoonState = '2';
            }

            //draw spoon
            blobMaxRectangle.Offset(drawCenterROI.Location);
            drawImage.Draw(blobMaxRectangle, new Bgr(0, 255, 0), 2);


            return greyThreshImg.Convert<Bgr, byte>();
        }

        private void mouthDetector(Image<Bgr, Byte> image, out Rectangle faceROI, out Rectangle mouthROI)
        {
            List<Rectangle> faces = new List<Rectangle>();
            List<Rectangle> mouths = new List<Rectangle>();

            faceROI = new Rectangle(0, 0, 0, 0);
            mouthROI = new Rectangle(0, 0, 0, 0);

            DetectFace(image, faces, mouths);
            //FaceDetection.DetectFace.Detect(image, "haarcascades\\haarcascade_frontalface_default.xml", "haarcascades\\haarcascade_eye.xml", faces, eyes, out detectionTime);
            foreach (Rectangle face in faces)
            {
                if (rs232form.FaceMinWidth < face.Width && face.Width < rs232form.FaceMaxWidth &&
                  rs232form.FaceMinHeight < face.Height && face.Height < rs232form.FaceMaxHeight)
                {
                    image.Draw(face, new Bgr(Color.Red), 2);
                    faceROI = face;
                }
                else
                {
                    image.Draw(face, new Bgr(Color.Black), 1);
                }
            }
            int curMouthNumber = 1;
            foreach (Rectangle mouth in mouths)
            {
                image.Draw(mouth, new Bgr(Color.Blue), 1);
                mouthROI = mouth;

                string strPos =
                    "pos=(" + (mouth.Left + (mouth.Width / 2.0)) + "," +
                                (mouth.Top + (mouth.Height / 2.0)) + ")";

                //20140603 追加, 嘴唇高度(分為10級高度)
                int mouthLevel = (int)(10 * (mouth.Top + (mouth.Height / 2.0)) / (double)image.Height);
                strPos += ", Level = " + mouthLevel;
                rs232form.mouthHeight = mouthLevel.ToString()[0];

                //image.Draw(strPos, ref cvFont, new Point(0, 30 + curMouthNumber * 30), new Bgr(0, 0, 0));
                image.Draw(strPos, ref cvFontBack, new Point(0, 30 + curMouthNumber * 30), new Bgr(255, 255, 255));

                curMouthNumber++;
            }
        }

        private void DetectFace(Image<Bgr, Byte> image, List<Rectangle> faces, List<Rectangle> mouths)
        {
            //Read the HaarCascade objects
            using (Image<Gray, Byte> gray = image.Convert<Gray, Byte>()) //Convert it to Grayscale
            {
                //normalizes brightness and increases contrast of the image
                gray._EqualizeHist();

                //Detect the faces  from the gray scale image and store the locations as rectangle
                //The first dimensional is the channel
                //The second dimension is the index of the rectangle in the specific channel
                Rectangle[] facesDetected = faceHaar.DetectMultiScale(
                   gray,
                   1.1,
                   10,
                   new Size(20, 20),
                   Size.Empty);
                faces.AddRange(facesDetected);

                foreach (Rectangle f in facesDetected)
                {
                    //Test curFace Size
                    if (rs232form.FaceMinWidth < f.Width && f.Width < rs232form.FaceMaxWidth &&
                       rs232form.FaceMinHeight < f.Height && f.Height < rs232form.FaceMaxHeight)
                    {
                        //bypass
                    }
                    else
                    {
                        //next
                        continue;
                    }


                    //mouthsDetected
                    int helfHeight = (int)Math.Round(f.Height / 2.0);
                    gray.ROI = new Rectangle(f.Left, f.Top + helfHeight, f.Width, helfHeight);
                    Rectangle[] mouthsDetected = mouthHaar.DetectMultiScale(
                       gray,
                       1.1,
                       10,
                       new Size(20, 20),
                       Size.Empty);
                    gray.ROI = Rectangle.Empty;

                    Rectangle mouthRectLast;
                    if (mouthsDetected.Count<Rectangle>() > 0)
                    {
                        mouthRectLast = mouthsDetected.Last<Rectangle>();

                        foreach (Rectangle e in mouthsDetected)
                        {
                            Rectangle mouthRect = e;
                            if (mouthRectLast.Top < mouthRect.Top)
                                mouthRectLast = mouthRect;
                            //mouthRect.Offset(f.X, f.Y);
                            //mouths.Add(mouthRect);
                        }

                        mouthRectLast.Offset(f.X, f.Y + helfHeight);
                        mouths.Add(mouthRectLast);

                        //if get mouth then return
                        return;
                    }
                }
            }
        }

        int showMode = 1;
        private void Form1_Load(object sender, EventArgs e)
        {
            this.Top = 0;
            rs232form.Show();

            
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Button target = sender as Button;

            if (_capture != null)
            {
                if (_captureInProgress)
                {  //stop the capture
                    target.Text = "Start Capture";
                    _capture.Pause();
                }
                else
                {
                    //start the capture
                    target.Text = "Stop";
                    _capture.Start();
                }

                _captureInProgress = !_captureInProgress;
            }
        }

        private void ReleaseData()
        {
            if (_capture != null)
                _capture.Dispose();
        }

        private void captureImageBox_Paint(object sender, PaintEventArgs e)
        {

        }

        Point mousePos = new Point();
        private void captureImageBox_MouseMove(object sender, MouseEventArgs e)
        {
            mousePos = new Point(e.X, e.Y);
        }

        private void Form1_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar >= '1' && e.KeyChar <= '9')
            {
                showMode = int.Parse(e.KeyChar.ToString());
            }
        }

    }

}
