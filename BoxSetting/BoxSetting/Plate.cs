using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace BoxSetting
{
    public class Plate
    {
        internal static void XmlSave(double templetPlateWidth, double templetPlateHeight, Rectangle SearchPlateROI, List<Rectangle> subRoiList, int foodThreshold)
        {
            XmlDocument doc = new XmlDocument();
            //建立根節點
            XmlElement elePlate = doc.CreateElement("Plate");
            elePlate.SetAttribute("templetPlateWidth", templetPlateWidth.ToString());
            elePlate.SetAttribute("templetPlateHeight", templetPlateHeight.ToString());
            doc.AppendChild(elePlate);

            //建立子節點
            XmlElement elePlateSearchROI;
            elePlateSearchROI = doc.CreateElement("SearchPlateROI");
            elePlateSearchROI.SetAttribute("X", SearchPlateROI.X.ToString());
            elePlateSearchROI.SetAttribute("Y", SearchPlateROI.Y.ToString());
            elePlateSearchROI.SetAttribute("Width", SearchPlateROI.Width.ToString());
            elePlateSearchROI.SetAttribute("Height", SearchPlateROI.Height.ToString());
            elePlate.AppendChild(elePlateSearchROI);

            XmlElement eleSubROI;
            for (int i = 0; i < subRoiList.Count; i++)
            {
                eleSubROI = doc.CreateElement("SubROI" + (i + 1).ToString());
                eleSubROI.SetAttribute("X", subRoiList[i].X.ToString());
                eleSubROI.SetAttribute("Y", subRoiList[i].Y.ToString());
                eleSubROI.SetAttribute("Width", subRoiList[i].Width.ToString());
                eleSubROI.SetAttribute("Height", subRoiList[i].Height.ToString());
                elePlate.AppendChild(eleSubROI);
            }

            XmlElement eleThreshold;
            eleThreshold = doc.CreateElement("Threshold");
            eleThreshold.SetAttribute("Food", foodThreshold.ToString());
            elePlate.AppendChild(eleThreshold);

            doc.Save("Plate.xml");
        }

        internal static void XmlLoad(double templetPlateWidth, double templetPlateHeight, ref Rectangle SearchPlateROI, ref List<Rectangle> subRoiList, out int foodThreshold)
        {
            XmlDocument doc = new XmlDocument();
            foodThreshold = 0;
            doc.Load("Plate.xml");
            if (doc != null)
            {
                XmlElement elePlate = doc.SelectSingleNode("Plate") as XmlElement;
                if (elePlate != null)
                {
                    templetPlateWidth = double.Parse(elePlate.GetAttribute("templetPlateWidth"));
                    templetPlateHeight = double.Parse(elePlate.GetAttribute("templetPlateHeight"));
                }
                XmlElement eleSearchPlateROI;
                eleSearchPlateROI = doc.SelectSingleNode("Plate/SearchPlateROI") as XmlElement;
                if (eleSearchPlateROI != null)
                {
                    int x = int.Parse(eleSearchPlateROI.GetAttribute("X")),
                        y = int.Parse(eleSearchPlateROI.GetAttribute("Y")),
                        width = int.Parse(eleSearchPlateROI.GetAttribute("Width")),
                        height = int.Parse(eleSearchPlateROI.GetAttribute("Height"));
                    SearchPlateROI = new Rectangle(x, y, width, height);
                }

                XmlElement eleSubROI;
                for (int i = 0; i < subRoiList.Count; i++)
                {
                    eleSubROI = doc.SelectSingleNode("Plate/SubROI" + (i + 1).ToString()) as XmlElement;
                    if (eleSubROI != null)
                    {
                        int x = int.Parse(eleSubROI.GetAttribute("X")),
                            y = int.Parse(eleSubROI.GetAttribute("Y")),
                            width = int.Parse(eleSubROI.GetAttribute("Width")),
                            height = int.Parse(eleSubROI.GetAttribute("Height"));
                        subRoiList[i] = new Rectangle(x, y, width, height);
                    }
                }

                XmlElement eleThreshold;
                eleThreshold = doc.SelectSingleNode("Plate/Threshold") as XmlElement;
                if (eleThreshold != null)
                {
                    foodThreshold = int.Parse(eleThreshold.GetAttribute("Food"));
                }
            }
        }
    }
}
