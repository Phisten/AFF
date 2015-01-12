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
using System.Xml;
using System.IO;
using System.IO.Ports;
using System.Threading;

namespace BoxSetting
{
    public partial class BoxSettingForm : Form
    {
        //20150112 : 餐盤位置絕對座標模式
        bool DirectPositionMode = true;


        bool SettingMode = false;
        bool AutoDetectSubRoi = false;
        double templetPlateWidth = 1;
        double templetPlateHeight = 1;

        //Emgu.CV.CvEnum.THRESH WhiteOrBlack = Emgu.CV.CvEnum.THRESH.CV_THRESH_BINARY_INV;
        Emgu.CV.CvEnum.THRESH WhiteOrBlack = Emgu.CV.CvEnum.THRESH.CV_THRESH_BINARY;
        Emgu.CV.CvEnum.THRESH SubWhiteOrBlack = Emgu.CV.CvEnum.THRESH.CV_THRESH_BINARY_INV;

        //int DefaultSubThreshold = 115;
        int DefaultSubThreshold = 115;

        //delegate void pri 
        delegate void RefreshListBox(BoxSettingForm thisForm);
        RefreshListBox refreshListBox = ReViewListBoxStatic;

        //SquarePlateROI = Blue
        Rectangle SearchPlateROI = new Rectangle(20, 20, 620, 460);
        Bgr SearchPlateColor = new Bgr(255, 0, 0);

        //MainRoi = Block
        Rectangle MainRoi = new Rectangle(0, 0, 0, 0);
        Bgr MainRoiColor = new Bgr(0, 0, 0);

        //SubRoi = Green
        int SubROICount = 3;
        double SubRoiScaleRate = 0.9d;
        List<Rectangle> subRoiList = new List<Rectangle>();
        List<List<Rectangle>> foodRoiList = new List<List<Rectangle>>();
        Bgr SubRoiColor = new Bgr(0, 255, 0);
        /// <summary>每個餐盤子區塊分割區的食物所含面積</summary>
        List<int> partitionFoodArea = new List<int>();
        List<double> partitionFoodAreaRate = new List<double>();
        MCvFont foodrateFont = new MCvFont(Emgu.CV.CvEnum.FONT.CV_FONT_HERSHEY_COMPLEX_SMALL, 1d, 1d);
        double foodDetectorRate = 0.20d;


        //curBox = Red
        Rectangle curROI = new Rectangle();
        Bgr curRoiColor = new Bgr(0, 0, 255);

        TestType curType = (TestType)0;
        int blobSizeThreshold = 200;
        ImageForm ThreshForm = new ImageForm();

        //Debug
        int DebugLevel = 1;
        int DebugThresholdMode = 1;
        Image<Rgb, byte>[] debugImage = new Image<Rgb, byte>[8];

        //int SquarePlateThreshold = 120;
        int WebCamIndex = 0;
        private Capture _capture = null;
        private bool _captureInProgress;
        int imgWidth = 640;
        int imgHeight = 480;
        public BoxSettingForm()
        {
            InitializeComponent();
            try
            {
                if (0 == (int)curType)
                {
                    _capture = new Capture(WebCamIndex);
                    _capture.ImageGrabbed += ProcessFrame;

                    _capture.Start();
                    _captureInProgress = !_captureInProgress;
                    captureImageBox.Size = new Size(imgWidth, imgHeight);
                    captureImageBox.FunctionalMode = Emgu.CV.UI.ImageBox.FunctionalModeOption.Minimum;
                }
                else if (1 == (int)curType)
                {
                    string TestImgPath = @"C:\Users\Administrator\Desktop\food det\pla_640x480.jpg";
                    frame = new Image<Bgr, byte>(TestImgPath);
                    captureImageBox.Size = new Size(frame.Width, frame.Height);
                    captureImageBox.FunctionalMode = Emgu.CV.UI.ImageBox.FunctionalModeOption.Minimum;
                    ProcessFrame(null, null);
                }
            }
            catch (NullReferenceException excpt)
            {
                MessageBox.Show(excpt.Message);
            }

        }

        MCvFont cvFontFoodPos = new MCvFont(Emgu.CV.CvEnum.FONT.CV_FONT_HERSHEY_SIMPLEX, 0.3d, 0.3d);
        MCvFont cvFontBack = new MCvFont(Emgu.CV.CvEnum.FONT.CV_FONT_HERSHEY_COMPLEX, 0.5d, 0.5d);
        Image<Bgr, Byte> frame;
        int firstFrame = 0;
        int ImageSaveElapsed = 5000; //存餐盤照片時間間隔(ms
        Stopwatch swImageSaveTimer = new Stopwatch();
        private void ProcessFrame(object sender, EventArgs arg)
        {

            if (0 == (int)curType)
                frame = _capture.RetrieveBgrFrame();
            Image<Bgr, Byte> srcImage = frame.Resize(captureImageBox.Size.Width, captureImageBox.Size.Height, Emgu.CV.CvEnum.INTER.CV_INTER_LINEAR);
            srcImage._SmoothGaussian(3);
            Image<Bgr, Byte> outputImage = srcImage.Copy();

            Image<Gray, byte> threshImage = null;
            Image<Gray, byte> subRoiThreshImage = null;
            List<Image<Gray, byte>> foodThreshImageList = new List<Image<Gray, byte>>(); //餐盤子區域二值化影像
            List<Image<Gray, byte>> foodThreshCenterImageList = new List<Image<Gray, byte>>(); //餐盤子區域中心處(食物抓取區域)二值化影像
            if (SettingMode == false)
            {
                if (DirectPositionMode)
                {
                    PlateDetector_ByPass(srcImage, ref outputImage, out threshImage);
                }
                else
                {
                    //尋找餐盤位置
                    PlateDetector(srcImage, ref outputImage, out threshImage);
                }

            }

            partitionFoodArea = new List<int>();
            partitionFoodAreaRate = new List<double>();
            foodRoiList = new List<List<Rectangle>>(); //所有餐盤子區域檢測出的食物ROI (座標用於攝影機原始影像)
            if (SettingMode == false)
            {
                if (AutoDetectSubRoi == true)
                {
                    //尋找餐盤子區塊
                    SubRoiDetector(srcImage, ref outputImage, out subRoiThreshImage);

                    //去除右上角區塊並排序其他區塊
                    SubRoiSort(ref subRoiList);
                }

                for (int i = 0; i < subRoiList.Count; i++)
                {
                    List<Rectangle> curFoodRoiList;
                    Image<Gray, byte> curThresholdImage;
                    //Image<Gray, byte> curThresholdCenterImage;
                    //判斷各個子區域是否仍有食物 以及回傳中心食物區塊大小


                    //縮小子區域範圍    SubRoiScaleRate
                    Rectangle curSubROI = RectangleCenterScale(subRoiList[i], SubRoiScaleRate, SubRoiScaleRate);
                    if (curSubROI.Height == 0 || curSubROI.Width == 0)
                    {
                        continue;
                    }


                    if (firstFrame <= i && i < SubROICount && swImageSaveTimer.ElapsedMilliseconds > ImageSaveElapsed)
                    {
                        Image<Bgr, byte> viewImage = srcImage.Copy(curSubROI).Resize(2d, Emgu.CV.CvEnum.INTER.CV_INTER_LINEAR);
                        viewImage.Save(@"C:\img\" + (i + 1).ToString() + ".jpg");
                        // Image<Bgr, byte> viewImage2 = srcImage.Copy(curSubROI);
                        // viewImage2.Save(@"C:\img\" + (i + 1).ToString() + "src.jpg");
                        firstFrame++;

                    }

                    int curCenterFoodArea; //已無效
                    //檢測指定區域內的食物面積,會回傳食物blob與轉換後的檢測影像與食物所含面積
                    FoodDetector(srcImage, curSubROI, ref outputImage, out curThresholdImage, out curFoodRoiList, out curCenterFoodArea);

                    int foodarea;
                    Rectangle curSubRoiThresholdRect = curThresholdImage.ROI;

                    //子區域食物面積計算
                    Rectangle curSubROIpartition1 = curSubRoiThresholdRect;
                    foodarea = ValueHitTest(curThresholdImage, curSubROIpartition1);
                    partitionFoodArea.Add(foodarea);
                    double foodareaRate = foodarea / (double)(curSubROIpartition1.Width * curSubROIpartition1.Height);
                    partitionFoodAreaRate.Add(foodareaRate);
                    outputImage.Draw(Math.Round(foodareaRate*100d).ToString() + "%", ref foodrateFont, new Point(curSubROIpartition1.X + curSubROI.X, curSubROIpartition1.Y + curSubROI.Y), new Bgr(0, 0, 255));
                    outputImage.Draw(new Rectangle(curSubROIpartition1.X + curSubROI.X, curSubROIpartition1.Y + curSubROI.Y, curSubROIpartition1.Width, curSubROIpartition1.Height), new Bgr(255, 200, 100), 1);

                    ////右下角分割區
                    //Rectangle curSubROIpartition1 = RectangleCenterScale(curSubRoiThresholdRect, 0.5d, 0.5d);
                    //curSubROIpartition1.Offset(curSubRoiThresholdRect.Width / 4, curSubRoiThresholdRect.Height / 4);
                    //foodarea = ValueHitTest(curThresholdImage, curSubROIpartition1);
                    //partitionFoodArea.Add(foodarea);
                    //partitionFoodAreaRate.Add(foodarea / (double)(curSubROIpartition1.Width * curSubROIpartition1.Height));
                    //outputImage.Draw(new Rectangle(curSubROIpartition1.X + curSubROI.X, curSubROIpartition1.Y + curSubROI.Y, curSubROIpartition1.Width, curSubROIpartition1.Height), new Bgr(255, 200, 100), 1);

                    ////右上角分割區
                    //Rectangle curSubROIpartition2 = RectangleCenterScale(curSubRoiThresholdRect, 0.5d, 0.5d);
                    //curSubROIpartition2.Offset(curSubRoiThresholdRect.Width / 4, -curSubRoiThresholdRect.Height / 4);
                    //foodarea = ValueHitTest(curThresholdImage, curSubROIpartition2);
                    //partitionFoodArea.Add(foodarea);
                    //partitionFoodAreaRate.Add(foodarea / (double)(curSubROIpartition2.Width * curSubROIpartition2.Height));
                    //outputImage.Draw(new Rectangle(curSubROIpartition2.X + curSubROI.X, curSubROIpartition2.Y + curSubROI.Y, curSubROIpartition2.Width, curSubROIpartition2.Height), new Bgr(255, 200, 100), 1);

                    ////左邊分割區
                    //Rectangle curSubROIpartition3 = RectangleCenterScale(curSubRoiThresholdRect, 0.5d, 1d);
                    //curSubROIpartition3.Offset(-curSubRoiThresholdRect.Width / 4, 0);
                    //foodarea = ValueHitTest(curThresholdImage, curSubROIpartition3);
                    //partitionFoodArea.Add(foodarea);
                    //partitionFoodAreaRate.Add(foodarea / (double)(curSubROIpartition3.Width * curSubROIpartition3.Height));
                    //outputImage.Draw(new Rectangle(curSubROIpartition3.X + curSubROI.X, curSubROIpartition3.Y + curSubROI.Y, curSubROIpartition3.Width, curSubROIpartition3.Height), new Bgr(255, 200, 100), 1);



                    //搜尋各子區塊分割區內的食物面積

                    foodThreshImageList.Add(curThresholdImage);
                    foodRoiList.Add(curFoodRoiList);
                }


                

                //刷新存照片時間
                if (swImageSaveTimer.IsRunning == false || swImageSaveTimer.ElapsedMilliseconds > ImageSaveElapsed)
                {
                    swImageSaveTimer.Restart();
                    firstFrame = 0;
                }
            }
            List<double> foodAreaRate = new List<double>();


            //更新食物資訊  內含RS232傳送
            UpdateFoodInfo(foodAreaRate);


            //原始影像顯示設定

            //SquarePlateROI = Blue
            outputImage.Draw(SearchPlateROI, SearchPlateColor, 1);

            //MainRoi = Block
            outputImage.Draw(MainRoi, MainRoiColor, 1);

            //sub ROI
            Rectangle curRoi;
            double scaleWidth = MainRoi.Width / templetPlateWidth;
            double scaleHeight = MainRoi.Height / templetPlateHeight;

            int subRoiListIndex = 1;
            foreach (var item in subRoiList)
            {
                curRoi = new Rectangle(
                    (int)(item.X + item.X * (scaleWidth - 1)),
                    (int)(item.Y + item.Y * (scaleHeight - 1)),
                    (int)(item.Width * scaleWidth),
                    (int)(item.Height * scaleHeight));


                outputImage.Draw(curRoi, SubRoiColor, 1);
                outputImage.Draw("sub" + (subRoiListIndex++).ToString(), ref cvFontBack, new Point(curRoi.X + 5, curRoi.Y + curRoi.Height - 10), new Bgr(255, 200, 0));
            }

            //food locatDraw
            //foreach (var roilist in foodRoiList)
            //{
            //    foreach (Rectangle item in roilist)
            //    {
            //        int x = item.X;
            //        int y = item.Y;
            //        int fontX = x;
            //        int fontY = y + item.Height + 10;
            //        outputImage.Draw("(" + x.ToString() + "," + y.ToString() + ")", ref cvFontFoodPos, new Point(fontX, fontY), new Bgr(0, 0, 255));
            //    }
            //}

            int curRoiX, curRoiY;
            //cur ROI
            switch (curMouseMode)
            {
                case MouseMode.Default:
                    break;
                case MouseMode.WaitDrop:
                    outputImage.Draw(curROI, curRoiColor, 1);
                    break;
                case MouseMode.DropStart:
                    curRoiX = Math.Min(StartDrupPoint.X, mousePos.X);
                    curRoiY = Math.Min(StartDrupPoint.Y, mousePos.Y);
                    curROI = new Rectangle(
                        curRoiX,
                        curRoiY,
                        Math.Max(StartDrupPoint.X, mousePos.X) - curRoiX,
                        Math.Max(StartDrupPoint.Y, mousePos.Y) - curRoiY
                        );
                    outputImage.Draw(curROI, curRoiColor, 1);
                    break;
                case MouseMode.DropEndAndWaitDrop:

                    curRoiX = Math.Min(StartDrupPoint.X, EndDrupPoint.X);
                    curRoiY = Math.Min(StartDrupPoint.Y, EndDrupPoint.Y);
                    curROI = new Rectangle(
                        curRoiX,
                        curRoiY,
                        Math.Max(StartDrupPoint.X, EndDrupPoint.X) - curRoiX,
                        Math.Max(StartDrupPoint.Y, EndDrupPoint.Y) - curRoiY
                        );
                    outputImage.Draw(curROI, curRoiColor, 1);
                    break;
                default:
                    break;
            }


            string strMousePos =
                "(" + mousePos.X + "," + mousePos.Y + ")";
            outputImage.Draw(strMousePos, ref cvFontBack, new Point(mousePos.X, mousePos.Y + 30), new Bgr(255, 255, 255));




            var tempYccImg = outputImage.Convert<Ycc, byte>();
            switch (showMode)
            {
                case 1:
                    captureImageBox.Image = outputImage;
                    break;
                case 2:  //攝影機二值化影像顯示設定
                    if (threshImage == null)
                        threshImage = outputImage.Convert<Gray, byte>();
                    captureImageBox.Image = threshImage;
                    break;
                case 3:  //餐盤二值化影像顯示設定
                    if (subRoiThreshImage == null)
                        subRoiThreshImage = outputImage.Convert<Gray, byte>();
                    captureImageBox.Image = subRoiThreshImage;
                    break;
                //4567 餐盤子區塊1234完整區域 二值化影像顯示設定
                case 4:
                case 5:
                case 6:
                case 7:
                    if (showMode - 4 >= foodThreshImageList.Count)
                        foodThreshImageList.Add(outputImage.Convert<Gray, byte>());
                    if (showMode >= 4 && showMode <= 7 && foodThreshImageList[showMode - 4] == null)
                        foodThreshImageList[showMode - 4] = outputImage.Convert<Gray, byte>();
                    captureImageBox.Image = foodThreshImageList[showMode - 4];
                    break;
                //FGHJ 餐盤子區塊1234中心區域 二值化影像顯示設定
                case 102:
                case 103:
                case 104:
                case 106:
                    int mappingIndex = showMode != 106 ? showMode - 102 : 3;
                    captureImageBox.Image = foodThreshCenterImageList[mappingIndex];
                    break;
                case 8:
                    break;
                case 113: //q   原始影像
                    captureImageBox.Image = outputImage;
                    break;
                case 119: //w   YCbCr色彩
                    captureImageBox.Image = new Image<Rgb, byte>(new Image<Gray, byte>[] { tempYccImg[0], tempYccImg[1], tempYccImg[2] });
                    break;
                case 101: //e   Y通道(灰階)
                    captureImageBox.Image = tempYccImg[0];
                    break;
                case 114: //r   Cb通道
                    captureImageBox.Image = tempYccImg[1];
                    break;
                case 116: //r   Cr通道
                    captureImageBox.Image = tempYccImg[2];
                    break;
                default:
                    captureImageBox.Image = outputImage;
                    break;
            }


        }

        /// <summary>將子區域按照由上方為第1個 右下方為第2 左下方為第3的順序排序處理. 子區塊剛好為4個時才排序</summary>
        private void SubRoiSort(ref List<Rectangle> subRoiList)
        {
            subRoiList.Sort(new Comparison<Rectangle>((r1, r2) => r1.Y - r2.Y));
            //if (subRoiList.Count == 4)
            //{
            ////將X+Y後由大到小排序
            // 改為由Y小至大排序
            //subRoiList.Sort(new Comparison<Rectangle>((r1, r2) => r1.X + r1.Y - r2.X - r2.Y));


            ////去除左上角子區塊
            //subRoiList.RemoveAt(0);
            ////右下與左下對換
            //Rectangle tmp = subRoiList[1];
            //subRoiList[1] = subRoiList[2];
            //subRoiList[2] = tmp;
            //}
        }


        int RS232UpdateTime = 500; //食物狀態更頻率與RS232傳送頻率(ms)
        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();//引用stopwatch物件
        /// <summary>更新食物狀況與傳送RS232資料</summary>
        private void UpdateFoodInfo(List<double> foodAreaRate)
        {

            Func<int, int, RichTextBox, string> centerInfoStr = (centerFoodAreaTmp, allFoodCount, richBox) => richBox.Text = centerFoodAreaTmp > 0 ? "中心有食物" : allFoodCount > 0 ? "周邊有食物" : "完全沒食物";
            Func<int, List<string>, RichTextBox, string> centerInfoStrByIndex = (func_curStateIndex, func_stateMsgList, richBox) => richBox.Text = func_stateMsgList[func_curStateIndex];
            Func<string, RichTextBox, string> UpdateRS232Data = (RS232Data, richBox) => richBox.Text = RS232Data;


            List<string> stateMsg = new List<string>() { "完全沒食物", "中心有食物", "周邊有食物", };
            List<char> stateCodeList = new List<char>(6);
            List<int> stateIndex = new List<int>() { 0, 1, 2 };

            List<RichTextBox> subRectTextBoxList = new List<RichTextBox>() { richTextBox2, richTextBox3, richTextBox4, richTextBox5 };
            double foodAreaRateTemp = 0d;
            for (int i = 0; i < partitionFoodAreaRate.Count; i++)
            {
                int curStateIndex = 0;
                int centerFoodAreaRate = 0;


                if (i < SubROICount)
                    //if (i % 3 == 0 && i < 9)
                {
                    //更新食物訊息於訊息
                    //int curfoodareaTest = partitionFoodArea[i] + partitionFoodArea[i + 1] + partitionFoodArea[i + 2];
                    int curfoodareaTest = partitionFoodArea[i];
                    curStateIndex = curfoodareaTest > 0 ? 1 : foodRoiList[i].Count > 0 ? 2 : 0;
                    //subRectTextBoxList[i / 3].Invoke(centerInfoStrByIndex, curStateIndex, stateMsg, subRectTextBoxList[i / 3]);

                    //20140603 整合組要求修改,原本SUB1=01 SUB2=10 SUB3=11
                    //          改為SUB1=A SUB2=B SUB3=C
                    ////串上
                    //stateCodeList.Add(i / 3 == 0 ? '0' : '1');
                    //stateCodeList.Add(i / 3 == 1 ? '0' : '1');
                    stateCodeList.Add((new char[] { 'A', 'B', 'C' })[i]);
                    stateCodeList.Add(partitionFoodAreaRate[i] > foodDetectorRate ? '1' : '0');

                }


                ////串上目前分割區的食物百分比
                //char foodPsASC = ((int)(partitionFoodAreaRate[i] * 10)).ToString()[0];
                //20140603 整合組要求修改,原本輸出食物百分比0~9 改為0與1 (閥值為30%)
                //char foodPsASC = partitionFoodAreaRate[i] > 0.3d ? '1' : '0';

                //20150105 輸出格式變更  由 a111b111c111  改為  a1b1c1 (不分為三個子區塊)
                //stateCodeList.Add(foodPsASC); 
                //foodAreaRateTemp += partitionFoodAreaRate[i];
                //if (i % 3 == 2 && i < 9)
                //{
                //    stateCodeList.Add(foodAreaRateTemp > 3d * 0.3d ? '1' : '0');
                //    foodAreaRateTemp = 0d;
                //}

            }


            //發送RS232訊號
            if (sw.IsRunning == false) sw.Start();
            if (serialport.IsOpen == true && sw.ElapsedMilliseconds >= RS232UpdateTime)
            {
                sw.Reset();
                serialport.Write(stateCodeList.ToArray(), 0, stateCodeList.Count);
                richTextBox1.Invoke(UpdateRS232Data, string.Join<char>(" ", stateCodeList), richTextBox1);
            }
        }

        private void ReleaseData()
        {
            if (_capture != null)
                _capture.Dispose();
        }

        //RS232宣告
        System.IO.Ports.SerialPort serialport = new System.IO.Ports.SerialPort();//宣告連接埠
        Boolean light = false, serialportopen = false;
        private Color[] MsgTypeColor = { Color.Blue, Color.Green, Color.Black, Color.Orange, Color.Red };
        public enum MsgType { System, User, Normal, Warning, Error };
        //第二組RS232宣告
        System.IO.Ports.SerialPort serialport2 = new System.IO.Ports.SerialPort();//宣告連接埠
        Boolean light2 = false, serialport2open = false;
        //private Color[] MsgTypeColor = { Color.Blue, Color.Green, Color.Black, Color.Orange, Color.Red };
        //public enum MsgType { System, User, Normal, Warning, Error };

        private void Form1_Load(object sender, EventArgs e)
        {
            //button1.Text = "設定子區域";
           
            //button5.Text = "設定完成";
            //button2.Text = "儲存餐盤搜尋區域";
            button5.Visible = false;
            button6.Visible = false;
            button7.Visible = false;
            listBox1.Visible = false;
            trackBar1.Visible = false;
            //button2.Visible = false;
            //button3.Visible = false;
            //button4.Visible = false;

            //餐盤狀態檢視
            richTextBox2.Visible = false;
            richTextBox3.Visible = false;
            richTextBox4.Visible = false;
            richTextBox5.Visible = false;
            label3.Visible = false;
            label4.Visible = false;
            label5.Visible = false;
            label6.Visible = false;
            label7.Visible = false;
            button1.Visible = false;
            button8.Visible = false;
            button9.Visible = false;
            button10.Visible = false;
            button11.Visible = false;



            button3_Click(null, null);
            button3_Click(null, null);
            button3_Click(null, null);
            button3_Click(null, null);

            //RS232用
            //設定ovalShape1的FillStyle屬性為Soild     
            button12.Enabled = false;
            textBox1.Enabled = false;
            backgroundWorker1.WorkerSupportsCancellation = true;
            timer1.Interval = 500;
            comboBox1.Items.Clear();
            foreach (string com in System.IO.Ports.SerialPort.GetPortNames())//取得所有可用的連接埠
            {
                comboBox1.Items.Add(com);
            }
            //-----------第二組RS232用
            backgroundWorker2.WorkerSupportsCancellation = true;
            timer1.Interval = 500;
            comboBox2.Items.Clear();
            foreach (string com in System.IO.Ports.SerialPort.GetPortNames())//取得所有可用的連接埠
            {
                comboBox2.Items.Add(com);
            }
            //-----------------------------------------------

            Plate.XmlLoad(templetPlateWidth, templetPlateHeight, ref SearchPlateROI, ref subRoiList);
            curROI = SearchPlateROI;

            ReViewListBox();
            OkCancelButton(false);

        }



        //------------------------------以下新建RS232-------------------------------------------
        private void button3_Click_1(object sender, EventArgs e)
        {
            if (serialportopen == false && !serialport.IsOpen)
            {
                try
                {
                    //設定連接埠為9600、n、8、1、n
                    serialport.PortName = comboBox1.Text;
                    serialport.BaudRate = 9600;
                    serialport.DataBits = 8;
                    serialport.Parity = System.IO.Ports.Parity.None;
                    serialport.StopBits = System.IO.Ports.StopBits.One;
                    serialport.Encoding = Encoding.Default;//傳輸編碼方式
                    serialport.Open();
                    timer1.Enabled = true;
                    serialportopen = true;
                    button12.Enabled = true;
                    textBox1.Enabled = true;
                    if (this.backgroundWorker1.IsBusy != true)
                    {
                        this.backgroundWorker1.WorkerReportsProgress = true;
                        this.backgroundWorker1.RunWorkerAsync();
                    }
                    button3.Text = "中斷";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }
            }
            else if (serialportopen == true && serialport.IsOpen)
            {
                try
                {
                    serialport.Close();
                    if (!serialport.IsOpen)
                    {
                        serialportopen = false;
                        timer1.Enabled = false;
                        button12.Enabled = false;
                        textBox1.Enabled = false;
                        this.backgroundWorker1.WorkerReportsProgress = false;
                        this.backgroundWorker1.CancelAsync();
                        this.backgroundWorker1.Dispose();
                        //ovalShape1.FillColor = Color.Red;

                        button3.Text = "連線";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }
            }
        }
        //------------------------------以下新建第二組RS232-------------------------------------------
        private void button13_Click_1(object sender, EventArgs e)
        {
            if (serialportopen == false && !serialport2.IsOpen)
            {
                try
                {
                    //設定連接埠為9600、n、8、1、n
                    serialport2.PortName = comboBox2.Text;
                    serialport2.BaudRate = 9600;
                    serialport2.DataBits = 8;
                    serialport2.Parity = System.IO.Ports.Parity.None;
                    serialport2.StopBits = System.IO.Ports.StopBits.One;
                    serialport2.Encoding = Encoding.Default;//傳輸編碼方式
                    serialport2.Open();
                    timer1.Enabled = true;
                    serialport2open = true;
                    button12.Enabled = true;
                    textBox1.Enabled = true;
                    if (this.backgroundWorker1.IsBusy != true)
                    {
                        this.backgroundWorker1.WorkerReportsProgress = true;
                        this.backgroundWorker1.RunWorkerAsync();
                    }
                    button13.Text = "中斷";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }
            }
            else if (serialport2open == true && serialport2.IsOpen)
            {
                try
                {
                    serialport.Close();
                    if (!serialport2.IsOpen)
                    {
                        serialport2open = false;
                        // timer2.Enabled = false;
                        button12.Enabled = false;
                        //textBox2.Enabled = false;
                        this.backgroundWorker2.WorkerReportsProgress = false;
                        this.backgroundWorker2.CancelAsync();
                        this.backgroundWorker2.Dispose();
                        //ovalShape1.FillColor = Color.Red;

                        button13.Text = "連線";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }
            }
        }
        private void timer1_Tick(object sender, EventArgs e)
        {
            if (light == false)
            {
                //ovalShape1.FillColor = Color.Green;
                light = true;
            }
            else if (light == true)
            {
                //ovalShape1.FillColor = Color.Gray;
                light = false;
            }
        }

        private void button12_Click_1(object sender, EventArgs e)
        {
            try
            {
                serialport.Write(textBox1.Text);
                AddText(MsgType.Error, "傳送：" + textBox1.Text + "\r\n");
                textBox1.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private void textBox2_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (Char)13)
            {
                try
                {
                    serialport.Write(textBox1.Text);
                    AddText(MsgType.Error, "傳送：" + textBox1.Text + "\r\n");
                    textBox1.Clear();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }
            }
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            for (; ; )
            {
                if (backgroundWorker1.CancellationPending == true)
                {
                    e.Cancel = true;
                    break;
                }
                else
                {
                    try
                    {
                        backgroundWorker1.ReportProgress(0);
                        System.Threading.Thread.Sleep(1000);
                    }
                    catch (Exception)
                    {
                        e.Cancel = true;
                        break;
                    }
                }
            }
        }
        private void backgroundWorker2_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            try
            {
                if (serialport.BytesToRead != 0)
                {
                    label9.Text = "緩衝區：" + serialport.BytesToRead.ToString();
                    AddText(MsgType.System, "接收：" + serialport2.ReadExisting() + "\r\n");
                    serialport2.DiscardInBuffer();
                }
            }
            catch (Exception)
            { }
        }

        private void AddText(MsgType msgtype, string msg)
        {
            richTextBox1.Invoke(new EventHandler(delegate
            {
                richTextBox7.SelectedText = string.Empty;
                richTextBox7.SelectionFont = new Font(richTextBox7.SelectionFont, FontStyle.Bold);
                richTextBox7.SelectionColor = MsgTypeColor[(int)msgtype];
                richTextBox7.AppendText(msg);
                richTextBox7.ScrollToCaret();
            }));
        }

        private void ReViewListBox()
        {
            listBox1.Items.Clear();
            listBox1.Items.Add("餐盤移動範圍(藍)" + SearchPlateROI.ToString());
            listBox1.Items.Add("餐盤實際區域(黑)" + MainRoi.ToString());
            for (int i = 0; i < subRoiList.Count(); i++)
            {
                listBox1.Items.Add("餐盤子區域" + (i + 1) + "(綠)" + subRoiList[i].ToString());
            }
        }
        private static void ReViewListBoxStatic(BoxSettingForm thisForm)
        {
            string blueStr = thisForm.SearchPlateROI.ToString();
            string blackStr = thisForm.MainRoi.ToString();
            thisForm.listBox1.Items.Clear();
            thisForm.listBox1.Items.Add("餐盤移動範圍(藍)" + blueStr);
            thisForm.listBox1.Items.Add("餐盤實際區域(黑)" + blackStr);
            for (int i = 0; i < thisForm.subRoiList.Count(); i++)
            {
                string greenStr = thisForm.subRoiList[i].ToString();
                thisForm.listBox1.Items.Add("餐盤子區域" + (i + 1) + "(綠)" + greenStr);
            }
        }

        private void captureImageBox_Click(object sender, EventArgs e)
        {

        }

        int greyThreshValue;
        int SubRoiThreshValue;

        /// <summary>檢測餐盤，會設定MainRoi</summary>
        private void PlateDetector(Image<Bgr, Byte> srcImage, ref Image<Bgr, Byte> outputImage, out Image<Gray, byte> threshImage)
        {
            Image<Gray, Byte> greyImg = srcImage.Convert<Gray, Byte>();

            Rectangle roi_1 = SearchPlateROI;
            greyImg.ROI = roi_1;

            Image<Gray, Byte> greyThreshImg;

            //greyThreshImg = greyImg.ThresholdBinaryInv(new Gray(greyThreshValue), new Gray(255));
            greyThreshImg = greyImg.CopyBlank();
            greyThreshValue = (int)Emgu.CV.CvInvoke.cvThreshold(greyImg.Ptr, greyThreshImg.Ptr, greyThreshValue, 255d,
                WhiteOrBlack | Emgu.CV.CvEnum.THRESH.CV_THRESH_OTSU);

            outputImage.Draw("thValue=" + greyThreshValue.ToString(), ref cvFontBack, new Point(0, 50), new Bgr(255, 0, 0));

            Emgu.CV.Cvb.CvBlobs resultingImgBlobs = new Emgu.CV.Cvb.CvBlobs();
            Emgu.CV.Cvb.CvBlobDetector bDetect = new Emgu.CV.Cvb.CvBlobDetector();
            //Emgu.CV.Cvb.CvTracks blobTracks = new Emgu.CV.Cvb.CvTracks();
            uint numWebcamBlobsFound = bDetect.Detect(greyThreshImg, resultingImgBlobs);

            Image<Bgr, Byte> blobImg = greyThreshImg.Convert<Bgr, Byte>();
            Bgr red = new Bgr(200, 100, 0);
            int _blobSizeThreshold = blobSizeThreshold;

            //find MaxBlob
            int maxIndex = -1;
            int maxArea = 0;
            List<Emgu.CV.Cvb.CvBlob> blob = resultingImgBlobs.Values.ToList<Emgu.CV.Cvb.CvBlob>();
            for (int i = 0; i < resultingImgBlobs.Count; i++)
            {
                if (blob[i].Area > maxArea)
                {
                    maxArea = blob[i].Area;
                    maxIndex = i;
                }
            }

            if (-1 != maxIndex)
            {
                Rectangle bBox = blob[maxIndex].BoundingBox;
                greyThreshImg.Draw(bBox, new Gray(155), 1);
                bBox.X += roi_1.X;
                bBox.Y += roi_1.Y;
                outputImage.Draw(bBox, red, 1);
                MainRoi = bBox;
                listBox1.Invoke(refreshListBox, this);
            }

            templetPlateWidth = MainRoi.Width == 0 ? 1 : MainRoi.Width;
            templetPlateHeight = MainRoi.Height == 0 ? 1 : MainRoi.Height;

            threshImage = greyThreshImg;
        }

        /// <summary>取消餐盤自動檢測，直接設定MainRoi</summary>
        private void PlateDetector_ByPass(Image<Bgr, Byte> srcImage, ref Image<Bgr, Byte> outputImage, out Image<Gray, byte> threshImage)
        {
            Image<Gray, Byte> greyImg = srcImage.Convert<Gray, Byte>();

            Rectangle roi_1 = SearchPlateROI;
            greyImg.ROI = roi_1;

            Image<Gray, Byte> greyThreshImg;

            greyThreshImg = greyImg.CopyBlank();

            Bgr red = new Bgr(200, 100, 0);
            Rectangle bBox = new Rectangle(0, 0, roi_1.Width, roi_1.Height);
            greyThreshImg.Draw(bBox, new Gray(155), 1);
            bBox.X += roi_1.X;
            bBox.Y += roi_1.Y;
            outputImage.Draw(bBox, red, 1);
            MainRoi = bBox;
            listBox1.Invoke(refreshListBox, this);

            templetPlateWidth = MainRoi.Width == 0 ? 1 : MainRoi.Width;
            templetPlateHeight = MainRoi.Height == 0 ? 1 : MainRoi.Height;

            threshImage = greyThreshImg;
        }

        /// <summary>搜尋餐盤子區域</summary>
        private void SubRoiDetector(Image<Bgr, Byte> srcPlateImage, ref Image<Bgr, Byte> outputImage, out Image<Gray, Byte> subRoiThreshImage)
        {
            Rectangle PlateRoi = MainRoi;
            Image<Ycc, byte> yccImage = srcPlateImage.Convert<Ycc, byte>();
            yccImage.ROI = PlateRoi;
            Image<Gray, byte> cbImage = yccImage[1];
            //Image<Gray, byte> cbThreshImage = yccImage[1];
            //Image<Gray, byte> cbImage = srcPlateImage.Convert<Gray, byte>();
            Image<Gray, byte> cbThreshImage = cbImage;


            if (DefaultSubThreshold == 0)
            {
                SubRoiThreshValue = (int)Emgu.CV.CvInvoke.cvThreshold(cbImage.Ptr, cbThreshImage.Ptr, SubRoiThreshValue, 255d,
                    SubWhiteOrBlack | Emgu.CV.CvEnum.THRESH.CV_THRESH_OTSU);
            }
            else
            {
                SubRoiThreshValue = (int)Emgu.CV.CvInvoke.cvThreshold(cbImage.Ptr, cbThreshImage.Ptr, DefaultSubThreshold, 255d,
                    SubWhiteOrBlack);
            }


            outputImage.Draw("subRoiThValue=" + greyThreshValue.ToString(), ref cvFontBack, new Point(0, 80), new Bgr(255, 0, 0));

            Emgu.CV.Cvb.CvBlobs resultingImgBlobs = new Emgu.CV.Cvb.CvBlobs();
            Emgu.CV.Cvb.CvBlobDetector bDetect = new Emgu.CV.Cvb.CvBlobDetector();
            //Emgu.CV.Cvb.CvTracks blobTracks = new Emgu.CV.Cvb.CvTracks();
            uint numWebcamBlobsFound = bDetect.Detect(cbThreshImage, resultingImgBlobs);

            Image<Bgr, Byte> blobImg = cbThreshImage.Convert<Bgr, Byte>();
            Bgr red = new Bgr(200, 100, 0);
            int _blobSizeThreshold = 200;

            //find MaxBlob
            int maxLength = -1;
            //int maxArea = 0;
            List<Emgu.CV.Cvb.CvBlob> blob = resultingImgBlobs.Values.ToList<Emgu.CV.Cvb.CvBlob>();
            blob = blob.OrderByDescending<Emgu.CV.Cvb.CvBlob, int>(blobCompeTemp => blobCompeTemp.Area).ToList<Emgu.CV.Cvb.CvBlob>();

            maxLength = Math.Min(blob.Count, SubROICount);
            subRoiList = new List<Rectangle>();
            for (int i = 0; i < maxLength; i++)
            {
                if (blob[i].Area < _blobSizeThreshold)
                    continue;

                Rectangle bBox = blob[i].BoundingBox;
                //srcPlateImage.Draw(bBox, red, 1);
                cbThreshImage.Draw(bBox, new Gray(215), 1);

                bBox.X += PlateRoi.X;
                bBox.Y += PlateRoi.Y;

                //disable --- 找到的子區域稍微縮小 濾除邊界 RectangleCenterScale(bBox, 0.8d);
                subRoiList.Add(bBox);
                //subRoiList[i] = ;


            }

            //listBox1.Invoke(refreshListBox, this);
            subRoiThreshImage = cbThreshImage;

        }

        /// <summary>將影像於YCbCr色彩空間抽取Cb層用於食物檢測,回傳檢測結果食物blobs以及轉換後的檢測影像</summary>
        private void FoodDetector(Image<Bgr, Byte> srcImage, Rectangle curSubRoi, ref Image<Bgr, Byte> outputImage, out Image<Gray, byte> foodThreshImage, out List<Rectangle> foodRoiList, out int curCenterFoodArea) //, out Emgu.CV.Cvb.CvBlobs foodBlobList
        {
            //Image<Gray, Byte> greyImg = srcImage.Convert<Gray, Byte>();
            Image<Gray, Byte> greyImg = srcImage.Convert<Ycc, Byte>()[2];
            greyImg.ROI = curSubRoi;

            Image<Gray, Byte> greyThreshImg;

            //greyThreshImg = greyImg.ThresholdBinaryInv(new Gray(greyThreshValue), new Gray(255));
            greyThreshImg = greyImg.CopyBlank();


            //Emgu.CV.CvEnum.THRESH FoodThresType = Emgu.CV.CvEnum.THRESH.CV_THRESH_BINARY;
            Emgu.CV.CvEnum.THRESH FoodThresType = Emgu.CV.CvEnum.THRESH.CV_THRESH_BINARY_INV;
            if (DebugThresholdMode >= 1 && debugThreshold > 0)
            {
                greyThreshValue = (int)Emgu.CV.CvInvoke.cvThreshold(greyImg.Ptr, greyThreshImg.Ptr, debugThreshold, 255d, FoodThresType);
            }
            else
            {
                greyThreshValue = (int)Emgu.CV.CvInvoke.cvThreshold(greyImg.Ptr, greyThreshImg.Ptr, greyThreshValue, 255d, FoodThresType | Emgu.CV.CvEnum.THRESH.CV_THRESH_OTSU);
            }

            outputImage.Draw("foodThValue=" + greyThreshValue.ToString(), ref cvFontBack, new Point(0, 110), new Bgr(255, 0, 0));

            Emgu.CV.Cvb.CvBlobs resultingImgBlobs = new Emgu.CV.Cvb.CvBlobs(); //檢測出的blobs
            Emgu.CV.Cvb.CvBlobDetector bDetect = new Emgu.CV.Cvb.CvBlobDetector();
            uint numWebcamBlobsFound = bDetect.Detect(greyThreshImg, resultingImgBlobs);

            //SubRoiCornerFilter(curSubRoi, ref resultingImgBlobs);

            Bgr red = new Bgr(0, 0, 255);
            int _blobSizeThreshold = 150;

            curCenterFoodArea = 0;
            //foodBolbList = new Emgu.CV.Cvb.CvBlobs(); //過濾後的blobs(被視為食物的blob)
            foodRoiList = new List<Rectangle>();

            //餐盤子區域中心處(湯匙撈食物處)
            Rectangle centerTemp = RectangleCenterScale(curSubRoi, 0.4d, 1d);
            foreach (Emgu.CV.Cvb.CvBlob targetBlob in resultingImgBlobs.Values)
            {
                Rectangle bBox = targetBlob.BoundingBox;
                if (targetBlob.Area > _blobSizeThreshold)
                //20150112 :已取消食物與餐盤邊緣觸碰的偵測 (因餐盤架構改變)
                    //if (targetBlob.Area > _blobSizeThreshold && !IsSubRoiCorner(curSubRoi.Width, curSubRoi.Height, bBox))
                {
                    greyThreshImg.Draw(targetBlob.BoundingBox, new Gray(130), 1);
                    bBox.X += curSubRoi.X;
                    bBox.Y += curSubRoi.Y;
                    foodRoiList.Add(bBox);

                    //outputImage.Draw(bBox, red, 1);


                    //outputImage.Draw(centerTemp, new Bgr(255,255,0), 1);

                    //判斷是否有食物  已無效
                    if (HitTest(bBox, centerTemp))
                    {
                        curCenterFoodArea += targetBlob.Area;
                    }
                    //curCenterFoodArea += targetBlob.Area;

                }
            }
            foodThreshImage = greyThreshImg;

        }

        //矩形碰撞偵測,用於檢查食物是否存在於子區域中心處
        private bool HitTest(Rectangle foodRect, Rectangle CenterRect)
        {
            return foodRect.IntersectsWith(CenterRect);
        }
        //矩形碰撞偵測,計算矩陣內指定數值的面積
        private int ValueHitTest(Image<Gray, byte> srcImage, Rectangle Rect, int value = 255)
        {
            int area = 0;
            for (int i = Rect.Left, width = Rect.Right; i < width; i++)
            {
                for (int j = Rect.Top, height = Rect.Bottom; j < height; j++)
                {
                    //byte color = srcImage.Data[i,j,0];
                    if (srcImage.Data[j, i, 0] == value)
                    {
                        srcImage.Data[j, i, 0] = 128;
                        area++;
                    }
                }
            }
            return area;
        }
        /// <summary>檢測餐盤子區域的中心處是否有</summary>
        private void CenterFoodDetector()
        {

        }



        // 矩形框固定中心 指定長寬比縮小
        private Rectangle RectangleCenterScale(Rectangle srcRect, double dScaleX, double dScaleY)
        {
            Rectangle tarRect = srcRect;
            int width = (int)(srcRect.Width * dScaleX);
            int heigth = (int)(srcRect.Height * dScaleY);
            tarRect = new Rectangle(srcRect.Left + srcRect.Width / 2 - width / 2, srcRect.Top + srcRect.Height / 2 - heigth / 2, width, heigth);
            return tarRect;
        }



        /// <summary>過濾餐盤子區域邊角誤判的食物</summary>
        private bool IsSubRoiCorner(int SubRoiWidth, int SubRoiHeight, Rectangle foodRoi)
        {
            int limit = 3;
            Rectangle curROI = foodRoi;
            if (curROI.X < limit && curROI.Y < limit) //左上邊界
                return true;
            else if (curROI.Right > SubRoiWidth - limit && curROI.Y < limit) //右上邊界
                return true;
            else if (curROI.Right > SubRoiWidth - limit && curROI.Bottom > SubRoiHeight - limit) //右下邊界
                return true;
            else if (curROI.X < limit && curROI.Bottom > SubRoiHeight - limit) //左下邊界
                return true;

            return false;
        }
        private bool IsSubRoiCorner(Rectangle SearchRoi, Rectangle foodRoi)
        {
            int sroiWidth = SearchRoi.Width;
            int sroiHeight = SearchRoi.Height;

            Rectangle curROI = foodRoi;
            if (curROI.X < SearchRoi.X + 10 &&
                        curROI.Y < SearchRoi.Y + 10)
                return true;
            else if (curROI.X > SearchRoi.X + sroiWidth - 10 &&
                        curROI.Y < SearchRoi.Y + 10)
                return true;
            else if (curROI.X > SearchRoi.X + sroiWidth - 10 &&
                        curROI.Y > SearchRoi.Y + sroiHeight - 10)
                return true;
            else if (curROI.X < SearchRoi.X + 10 &&
                        curROI.Y > SearchRoi.Y + sroiHeight - 10)
                return true;

            return false;
        }
        private void SubRoiCornerFilter(Rectangle SearchRoi, ref Emgu.CV.Cvb.CvBlobs foodBlobs)
        {
            List<uint> removeKeyList = new List<uint>();
            //Emgu.CV.Cvb.CvBlobs tmpFoodBlobs = foodBlobs;
            int sroiWidth = SearchRoi.Width;
            int sroiHeight = SearchRoi.Height;

            for (int i = foodBlobs.Count - 1; i >= 0; i--)
            {
                Rectangle curROI = foodBlobs[(uint)i].BoundingBox;
                if (curROI.X < SearchRoi.X + 5 &&
                            curROI.Y < SearchRoi.Y + 5)
                    removeKeyList.Add((uint)i);
                else if (curROI.X > SearchRoi.X + sroiWidth - 5 &&
                            curROI.Y < SearchRoi.Y + 5)
                    removeKeyList.Add((uint)i);
                else if (curROI.X > SearchRoi.X + sroiWidth - 5 &&
                            curROI.Y > SearchRoi.Y + sroiHeight - 5)
                    removeKeyList.Add((uint)i);
                else if (curROI.X < SearchRoi.X + 5 &&
                            curROI.Y > SearchRoi.Y + sroiHeight - 5)
                    removeKeyList.Add((uint)i);
            }

            foreach (uint item in removeKeyList)
            {
                foodBlobs.Remove(item);
            }
        }

        Point mousePos = new Point();
        MouseMode curMouseMode = MouseMode.Default;
        Point StartDrupPoint = new Point();
        Point EndDrupPoint = new Point();
        private void captureImageBoxRefreshCheck()
        {
            if ((int)curType == 1)
                ProcessFrame(null, null);
        }
        private void captureImageBox_MouseMove(object sender, MouseEventArgs e)
        {
            mousePos = new Point(e.X, e.Y);
            captureImageBoxRefreshCheck();
        }
        private void captureImageBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (curMouseMode == MouseMode.WaitDrop ||
                curMouseMode == MouseMode.DropEndAndWaitDrop)
            {
                StartDrupPoint = new Point(e.X, e.Y);


                curMouseMode = MouseMode.DropStart;
            }
            captureImageBoxRefreshCheck();
        }
        private void captureImageBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (curMouseMode == MouseMode.DropStart)
            {
                EndDrupPoint = new Point(e.X, e.Y);


                curMouseMode = MouseMode.DropEndAndWaitDrop;
            }
            captureImageBoxRefreshCheck();
        }
        private void captureImageBox_MouseLeave(object sender, EventArgs e)
        {
            if (curMouseMode == MouseMode.DropStart)
            {
                curMouseMode = MouseMode.DropEndAndWaitDrop;
            }
            captureImageBoxRefreshCheck();
        }

        private void OkCancelButton(bool enable)
        {
            button6.Enabled = enable;
            button7.Enabled = enable;
            captureImageBoxRefreshCheck();
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (curMouseMode != MouseMode.Default)
            {
                button7_Click(null, null);
            }
            switch (listBox1.SelectedIndex)
            {
                case -1:
                    button7_Click(null, null);
                    break;
                case 0:
                    curROI = SearchPlateROI;
                    curMouseMode = MouseMode.WaitDrop;
                    OkCancelButton(true);
                    break;
                case 1:
                    curROI = MainRoi;
                    curMouseMode = MouseMode.WaitDrop;
                    OkCancelButton(true);
                    break;
                default:
                    curROI = subRoiList[listBox1.SelectedIndex - 2];
                    curMouseMode = MouseMode.WaitDrop;
                    OkCancelButton(true);
                    break;
            }

        }

        //OK
        private void button6_Click(object sender, EventArgs e)
        {
            switch (listBox1.SelectedIndex)
            {
                case -1:
                    break;
                case 0:
                    SearchPlateROI = curROI;
                    break;
                case 1:
                    MainRoi = curROI;
                    break;
                default:
                    curROI.Offset(-MainRoi.Left, -MainRoi.Top);
                    subRoiList[listBox1.SelectedIndex - 2] = curROI;
                    break;
            }
            ReViewListBox();
            curMouseMode = MouseMode.Default;
            OkCancelButton(false);
            curROI = new Rectangle();
        }

        //Cancel
        private void button7_Click(object sender, EventArgs e)
        {
            curMouseMode = MouseMode.Default;
            curROI = new Rectangle();
            OkCancelButton(false);
        }

        //add SubRoi
        private void button3_Click(object sender, EventArgs e)
        {
            curROI = new Rectangle();
            subRoiList.Add(curROI);
            ReViewListBox();
            listBox1.SelectedIndex = listBox1.Items.Count - 1;
            OkCancelButton(true);
        }

        private void button4_Click(object sender, EventArgs e)
        {
            switch (listBox1.SelectedIndex)
            {
                case -1:
                    break;
                case 0:
                    SearchPlateROI = new Rectangle();
                    break;
                case 1:
                    MainRoi = new Rectangle();
                    break;
                default:
                    subRoiList.RemoveAt(listBox1.SelectedIndex - 2);
                    break;
            }

            listBox1.SelectedIndex = -1;
            ReViewListBox();
        }


        enum MouseMode
        {
            Default, WaitDrop, DropStart, DropEndAndWaitDrop
        }

        enum TestType
        {
            Default, Simple
        }
        int showMode = 1;
        private void BoxSettingForm_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar >= '1' && e.KeyChar <= '8')
            {
                showMode = int.Parse(e.KeyChar.ToString());
            }
            else if (e.KeyChar == '9')
            {
                if (WhiteOrBlack == Emgu.CV.CvEnum.THRESH.CV_THRESH_BINARY_INV)
                {
                    WhiteOrBlack = Emgu.CV.CvEnum.THRESH.CV_THRESH_BINARY;
                }
                else
                {
                    WhiteOrBlack = Emgu.CV.CvEnum.THRESH.CV_THRESH_BINARY_INV;
                }
            }
            else
            {
                showMode = e.KeyChar;
            }
        }


        //編輯餐盤搜索區域
        private void button1_Click(object sender, EventArgs e)
        {
            //SettingMode = true;
            //templetPlateWidth = MainRoi.Width == 0 ? 1 : MainRoi.Width;
            //templetPlateHeight = MainRoi.Height == 0 ? 1 : MainRoi.Height;

            ////選擇餐盤搜尋區域
            //listBox1.SelectedIndex = 0;
            //curROI = SearchPlateROI;
            //curMouseMode = MouseMode.WaitDrop;
            //button2.Enabled = true;
            //button1.Enabled = false;
        }


        private void button5_Click(object sender, EventArgs e)
        {
            SettingMode = false;
            templetPlateWidth = MainRoi.Width == 0 ? 1 : MainRoi.Width;
            templetPlateHeight = MainRoi.Height == 0 ? 1 : MainRoi.Height;
        }


        //儲存餐盤搜索區域
        private void button2_Click(object sender, EventArgs e)
        {
            //確認餐盤搜尋區域
            //button2.Enabled = false;
            //button1.Enabled = true;
            SearchPlateROI = curROI;
            ReViewListBox();
            curMouseMode = MouseMode.WaitDrop;
            curROI = new Rectangle();

            //修改結束
            SettingMode = false;
            templetPlateWidth = MainRoi.Width == 0 ? 1 : MainRoi.Width;
            templetPlateHeight = MainRoi.Height == 0 ? 1 : MainRoi.Height;

            Plate.XmlSave(templetPlateWidth, templetPlateHeight, SearchPlateROI, subRoiList);
        }

        int debugThreshold = 0;
        private void trackBar1_Scroll(object sender, EventArgs e)
        {
            debugThreshold = trackBar1.Value;
        }

        private void backgroundWorker1_DoWork_1(object sender, DoWorkEventArgs e)
        {

        }

        private void button13_Click(object sender, EventArgs e)
        {

        }

        private void button14_Click(object sender, EventArgs e)
        {
            Image<Bgr, byte> testImg = new Image<Bgr, byte>(@"F:\餐盤影像\test.jpg");
            Image<Bgr, byte> outputImg = new Image<Bgr, byte>(@"F:\餐盤影像\test.jpg");
            Image<Gray, byte> threshImage = new Image<Gray, byte>(@"F:\餐盤影像\test.jpg");
            SearchPlateROI = new Rectangle();
            //PlateDetector(testImg, ref outputImg, out threshImage);
            SubRoiDetector(testImg, ref outputImg, out threshImage);
            outputImg.Save(@"F:\餐盤影像\test_outputImg.jpg");
            threshImage.Save(@"F:\餐盤影像\test_threshImage.jpg");
        }

        private void button15_Click(object sender, EventArgs e)
        {
            if (subRoiList.Count >= 1)
            {
                subRoiList[0] = curROI;
            }
            Plate.XmlSave(templetPlateWidth, templetPlateHeight, SearchPlateROI, subRoiList);

        }

        private void button16_Click(object sender, EventArgs e)
        {
            if (subRoiList.Count >= 2)
            {
                subRoiList[1] = curROI;
            }
            Plate.XmlSave(templetPlateWidth, templetPlateHeight, SearchPlateROI, subRoiList);
        }

        private void button17_Click(object sender, EventArgs e)
        {
            if (subRoiList.Count >= 3)
            {
                subRoiList[2] = curROI;
            }
            Plate.XmlSave(templetPlateWidth, templetPlateHeight, SearchPlateROI, subRoiList);
        }





    }

}
