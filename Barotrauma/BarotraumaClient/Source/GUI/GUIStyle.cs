using Microsoft.Xna.Framework.Graphics;
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

        public UISprite UIGlow { get; private set; }

        public SpriteSheet FocusIndicator { get; private set; }
            
        public GUIStyle(string file, GraphicsDevice graphicsDevice)
        {
            componentStyles = new Dictionary<string, GUIComponentStyle>();

            XDocument doc;
            try
            {
                ToolBox.IsProperFilenameCase(file);
                doc = XDocument.Load(file, LoadOptions.SetBaseUri);
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
                        Font = new ScalableFont(subElement, graphicsDevice);
                        break;
                    case "smallfont":
                        SmallFont = new ScalableFont(subElement, graphicsDevice);
                        break;
                    case "largefont":
                        LargeFont = new ScalableFont(subElement, graphicsDevice);
                        break;
                    case "cursor":
                        CursorSprite = new Sprite(subElement);
                        break;
                    case "uiglow":
                        UIGlow = new UISprite(subElement);
                        break;
                    case "focusindicator":
                        FocusIndicator = new SpriteSheet(subElement);
                        break;
                    default:
                        GUIComponentStyle componentStyle = new GUIComponentStyle(subElement);
                        componentStyles.Add(subElement.Name.ToString().ToLowerInvariant(), componentStyle);
                        break;
                }
            }
        }

        public GUIComponentStyle GetComponentStyle(string name)
        {
            componentStyles.TryGetValue(name.ToLowerInvariant(), out GUIComponentStyle style);
            return style;
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
