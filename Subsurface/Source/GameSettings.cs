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

        public ContentPackage SelectedContentPackage
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
            }

            foreach (XElement subElement in doc.Root.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "contentpackage":
                        string path = ToolBox.GetAttributeString(subElement, "path", "");
                        SelectedContentPackage = ContentPackage.list.Find(cp => cp.Path == path);

                        if (SelectedContentPackage == null) SelectedContentPackage = new ContentPackage(path);
                        break;
                }
            }

        }

        public void Save(string filePath)
        {
            XDocument doc = new XDocument();            

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

            if (SelectedContentPackage != null)
            {
                doc.Root.Add(new XElement("contentpackage", 
                    new XAttribute("path", SelectedContentPackage.Path)));
            }


            doc.Save(filePath);
        }
    }
}
