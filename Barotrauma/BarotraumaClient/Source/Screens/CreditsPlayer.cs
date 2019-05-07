using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class CreditsPlayer : GUIComponent
    {
        private GUIListBox listBox;

        private float scrollSpeed;

        public CreditsPlayer(RectTransform rectT, string configFile) : base(null, rectT)
        {
            var doc = XMLExtensions.TryLoadXml(configFile);
            scrollSpeed = doc.Root.GetAttributeFloat("scrollspeed", 100.0f);
            int spacing = doc.Root.GetAttributeInt("spacing", 0);

            listBox = new GUIListBox(new RectTransform(Vector2.One, rectT), style: null)
            {
                Spacing = spacing
            };

            foreach (XElement subElement in doc.Root.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "text":
                        AddTextElement(subElement, listBox.Content.RectTransform);
                        break;
                    case "spacing":
                        AddSpacingElement(subElement, listBox.Content.RectTransform);
                        break;
                    case "image":
                        AddImageElement(subElement, listBox.Content.RectTransform);
                        break;
                }
            }
            listBox.UpdateScrollBarSize();
        }

        private void AddTextElement(XElement element, RectTransform parent)
        {
            var text = element.ElementInnerText().Replace(@"\n", "\n");
            Color color = element.GetAttributeColor("color", Color.White);
            ScalableFont font = GUI.Font;
            switch (element.GetAttributeString("font", "Font").ToLowerInvariant())
            {
                case "font":
                    font = GUI.Font;
                    break;
                case "smallfont":
                    font = GUI.SmallFont;
                    break;
                case "largefont":
                    font = GUI.LargeFont;
                    break;
                case "videotitlefont":
                    font = GUI.VideoTitleFont;
                    break;
                case "objectivetitlefont":
                    font = GUI.ObjectiveTitleFont;
                    break;
                case "objectivenamefont":
                    font = GUI.ObjectiveNameFont;
                    break;
            }

            var textHolder = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.0f), parent), style: null);
            var textBlock = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), textHolder.RectTransform, Anchor.Center), 
                text, 
                color,
                font,
                Alignment.Center,
                wrap: true);
            textBlock.RectTransform.IsFixedSize = textHolder.RectTransform.IsFixedSize = true;
            textBlock.RectTransform.NonScaledSize = new Point(textBlock.Rect.Width, textBlock.Rect.Height);
            textHolder.RectTransform.NonScaledSize = new Point(textHolder.Rect.Width, textBlock.Rect.Height);
        }

        private void AddSpacingElement(XElement element, RectTransform parent)
        {
            if (element.Attribute("absoluteheight") != null)
            {
                int absoluteHeight = element.GetAttributeInt("absoluteheight", 10);
                var textHolder = new GUIFrame(new RectTransform(new Point(parent.NonScaledSize.X, absoluteHeight), parent), style: null);
            }
            else
            {
                float relativeHeight = element.GetAttributeFloat("relativeheight", 0.0f);
                var textHolder = new GUIFrame(new RectTransform(new Vector2(1.0f, relativeHeight), parent), style: null);
            }
        }

        private void AddImageElement(XElement element, RectTransform parent)
        {
            Sprite sprite = new Sprite(element);

            if (element.Attribute("absoluteheight") != null)
            {
                int absoluteHeight = element.GetAttributeInt("absoluteheight", 10);
                new GUIImage(new RectTransform(new Point(parent.NonScaledSize.X, absoluteHeight), parent), sprite, scaleToFit: true);
            }
            else
            {
                float relativeHeight = element.GetAttributeFloat("relativeheight", 0.0f);
                new GUIImage(new RectTransform(new Vector2(1.0f, relativeHeight), parent), sprite, scaleToFit: true);
            }
        }

        protected override void Update(float deltaTime)
        {
            listBox.BarScroll += scrollSpeed / listBox.TotalSize * deltaTime;

            if (listBox.BarScroll >= 1.0f)
            {
                listBox.BarScroll = 0.0f;
            }
        }
    }
}
