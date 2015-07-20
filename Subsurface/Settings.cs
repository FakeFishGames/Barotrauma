using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Subsurface
{
    public class GameSettings
    {
        public int GraphicsWidth
        {
            get;
            set;
        }
        public int GraphicsHeight
        {
            get;
            set;
        }

        public bool FullScreenEnabled
        {
            get;
            set;
        }

        public GameSettings(string filePath)
        {
            Load(filePath);
        }

        public void Load(string filePath)
        {
            XDocument doc = ToolBox.TryLoadXml(filePath);
            try
            {
                XElement graphicsMode = doc.Root.Element("graphicsmode");
                GraphicsWidth = int.Parse(graphicsMode.Attribute("width").Value);
                GraphicsHeight = int.Parse(graphicsMode.Attribute("height").Value);
                
                FullScreenEnabled = graphicsMode.Attribute("fullscreen").Value == "true";
            }
            catch
            {
                GraphicsWidth = 1024;
                GraphicsHeight = 768;
                return;
            }

        }

        public void Save(string filePath)
        {
            XDocument doc = null;
            try
            {
                doc = XDocument.Load(filePath);
            }
            catch
            {
                doc = new XDocument();
            }

            if (doc.Root == null)
            {
                doc.Add(new XElement("config"));
            }

            XElement gMode = doc.Root.Element("graphicsmode");
            if (gMode == null)
            {
                gMode = new XElement("graphicsmode");
                doc.Root.Add(gMode);
            }

            gMode.ReplaceAttributes(
                new XAttribute("width", GraphicsWidth),
                new XAttribute("height", GraphicsHeight),
                new XAttribute("fullscreen", FullScreenEnabled ? "true" : "false"));

            doc.Save(filePath);
        }
    }
}
