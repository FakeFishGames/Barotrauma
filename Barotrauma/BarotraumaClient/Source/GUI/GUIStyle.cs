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
        public ScalableFont VideoTitleFont { get; private set; }
        public ScalableFont ObjectiveTitleFont { get; private set; }
        public ScalableFont ObjectiveNameFont { get; private set; }

        public Sprite CursorSprite { get; private set; }

        public UISprite UIGlow { get; private set; }

        public SpriteSheet FocusIndicator { get; private set; }
            
        public GUIStyle(string file, GraphicsDevice graphicsDevice)
        {
            this.graphicsDevice = graphicsDevice;
            componentStyles = new Dictionary<string, GUIComponentStyle>();

            GameMain.Instance.OnResolutionChanged += () => { RescaleFonts(); };

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
            configElement = doc.Root;
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
                    case "objectivetitle":
                        ObjectiveTitleFont = new ScalableFont(subElement, graphicsDevice);
                        break;
                    case "objectivename":
                        ObjectiveNameFont = new ScalableFont(subElement, graphicsDevice);
                        break;
                    case "videotitle":
                        VideoTitleFont = new ScalableFont(subElement, graphicsDevice);
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
            return defaultFont;
        }


        private void RescaleFonts()
        {
            foreach (XElement subElement in configElement.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "font":
                        Font.Size = GetFontSize(subElement);
                        break;
                    case "smallfont":
                        SmallFont.Size = GetFontSize(subElement);
                        break;
                    case "largefont":
                        LargeFont.Size = GetFontSize(subElement);
                        break;
                    case "objectivetitle":
                        ObjectiveTitleFont.Size = GetFontSize(subElement);
                        break;
                    case "objectivename":
                        ObjectiveNameFont.Size = GetFontSize(subElement);
                        break;
                    case "videotitle":
                        VideoTitleFont.Size = GetFontSize(subElement);
                        break;
                }
            }
        }

        private ScalableFont LoadFont(XElement element, GraphicsDevice graphicsDevice)
        {
            string file         = GetFontFilePath(element);
            uint size           = GetFontSize(element);
            bool dynamicLoading = GetFontDynamicLoading(element);
            return new ScalableFont(file, size, graphicsDevice, dynamicLoading);
        }

        private uint GetFontSize(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "size") { continue; }
                Point maxResolution = subElement.GetAttributePoint("maxresolution", new Point(int.MaxValue, int.MaxValue));
                if (GameMain.GraphicsWidth <= maxResolution.X && GameMain.GraphicsHeight <= maxResolution.Y)
                {
                    return (uint)subElement.GetAttributeInt("size", 14);
                }
            }
            return 14;
        }

        private string GetFontFilePath(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "override") { continue; }
                string language = subElement.GetAttributeString("language", "").ToLowerInvariant();
                if (GameMain.Config.Language.ToLowerInvariant() == language)
                {
                    return subElement.GetAttributeString("file", "");
                }
            }
            return element.GetAttributeString("file", "");
        }

        private bool GetFontDynamicLoading(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "override") { continue; }
                string language = subElement.GetAttributeString("language", "").ToLowerInvariant();
                if (GameMain.Config.Language.ToLowerInvariant() == language)
                {
                    return subElement.GetAttributeBool("dynamicloading", false);
                }
            }
            return element.GetAttributeBool("dynamicloading", false);
        }

        public GUIComponentStyle GetComponentStyle(string name)
        {
            componentStyles.TryGetValue(name.ToLowerInvariant(), out GUIComponentStyle style);
            return style;
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
