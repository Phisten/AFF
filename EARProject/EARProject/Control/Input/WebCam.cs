using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.Structure;


namespace EARProject.IO
{
    public class WebCam
    {

        public delegate void FrameGrabbed(object sender, Image<Bgr, byte> frame, long frameElapsed);
        public event FrameGrabbed OnFrameGrabbed;
        private Capture _capture;
        public WebCam(int WebCamNumber)
        {
            _capture = new Capture(WebCamNumber);
            _capture.ImageGrabbed += ProcessFrame;
            
            FrameTimer.Start();
            _capture.Start();
        }

        Stopwatch FrameTimer = new Stopwatch();
        private void ProcessFrame(object sender, EventArgs arg)
        {
            Image<Bgr, byte> frame = _capture.RetrieveBgrFrame();
            long frameElapsed = FrameTimer.ElapsedMilliseconds;

            OnFrameGrabbed(this, frame, frameElapsed);

            FrameTimer.Restart();
        }
    }
}
