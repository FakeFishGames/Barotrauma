using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    public class GUIStyle
    {
        private Dictionary<string, GUIComponentStyle> componentStyles;

        public ScalableFont Font { get; private set; }
        public ScalableFont SmallFont { get; private set; }
        public ScalableFont LargeFont { get; private set; }

        public Sprite CursorSprite { get; private set; }
            
        public GUIStyle(string file)
        {
            componentStyles = new Dictionary<string, GUIComponentStyle>();

            XDocument doc;
            try
            {
                ToolBox.IsProperFilenameCase(file);
                doc = XDocument.Load(file);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Loading style \"" + file + "\" failed", e);
                return;
            }

            foreach (XElement subElement in doc.Root.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "font":
                        Font = new ScalableFont(subElement, GameMain.Instance.GraphicsDevice);
                        break;
                    case "smallfont":
                        SmallFont = new ScalableFont(subElement, GameMain.Instance.GraphicsDevice);
                        break;
                    case "largefont":
                        LargeFont = new ScalableFont(subElement, GameMain.Instance.GraphicsDevice);
                        break;
                    case "cursor":
                        CursorSprite = new Sprite(subElement);
                        break;
                    default:
                        GUIComponentStyle componentStyle = new GUIComponentStyle(subElement);
                        componentStyles.Add(subElement.Name.ToString().ToLowerInvariant(), componentStyle);
                        break;
                }
            }
        }

        public void Apply(GUIComponent targetComponent, string styleName = "", GUIComponent parent = null)
        {
            GUIComponentStyle componentStyle = null;  
            if (parent != null)
            {

                GUIComponentStyle parentStyle = parent.Style;

                if (parent.Style == null)
                {
                    string parentStyleName = parent.GetType().Name.ToLowerInvariant();

                    if (!componentStyles.TryGetValue(parentStyleName, out parentStyle))
                    {
                        DebugConsole.ThrowError("Couldn't find a GUI style \""+ parentStyleName + "\"");
                        return;
                    }
                }


                string childStyleName = string.IsNullOrEmpty(styleName) ? targetComponent.GetType().Name : styleName;
                parentStyle.ChildStyles.TryGetValue(childStyleName.ToLowerInvariant(), out componentStyle);
            }
            else
            {
                if (string.IsNullOrEmpty(styleName))
                {
                    styleName = targetComponent.GetType().Name;
                }
                if (!componentStyles.TryGetValue(styleName.ToLowerInvariant(), out componentStyle))
                {
                    DebugConsole.ThrowError("Couldn't find a GUI style \""+ styleName+"\"");
                    return;
                }
            }
            
            targetComponent.ApplyStyle(componentStyle);            
        }
    }
}
