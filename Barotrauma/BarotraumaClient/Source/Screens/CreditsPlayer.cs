using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma
{
    class CreditsPlayer : GUIComponent
    {
        private GUIListBox listBox;

        private XElement configElement;

        private float scrollSpeed;

        public CreditsPlayer(RectTransform rectT, string configFile) : base(null, rectT)
        {
            GameMain.Instance.OnResolutionChanged += () => { ClearChildren(); Load(); };

            var doc = XMLExtensions.TryLoadXml(configFile);
            if (doc == null) { return; }
            configElement = doc.Root;

            Load();
        }

        private void Load()
        {
            scrollSpeed = configElement.GetAttributeFloat("scrollspeed", 100.0f);
            int spacing = configElement.GetAttributeInt("spacing", 0);

            listBox = new GUIListBox(new RectTransform(Vector2.One, RectTransform), style: null)
            {
                Spacing = spacing
            };

            foreach (XElement subElement in configElement.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "text":
                        AddTextElement(subElement, listBox.Content.RectTransform);
                        break;
                    case "gridtext":
                        AddGridTextElement(subElement, listBox.Content.RectTransform);
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

        private GUIComponent AddTextElement(XElement element, RectTransform parent, string overrideText = null, Anchor anchor = Anchor.Center)
        {
            var text = overrideText ?? element.ElementInnerText().Replace(@"\n", "\n");
            Color color = element.GetAttributeColor("color", Color.White);
            float scale = element.GetAttributeFloat("scale", 1.0f);
            Alignment alignment = Alignment.Center;
            Enum.TryParse(element.GetAttributeString("alignment", "Center"), out alignment);
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
            var textBlock = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), textHolder.RectTransform, anchor),
                text,
                color,
                font,
                alignment,
                wrap: true)
            {
                TextScale = scale
            };
            textBlock.RectTransform.IsFixedSize = textHolder.RectTransform.IsFixedSize = true;
            textBlock.RectTransform.NonScaledSize = new Point(textBlock.Rect.Width, textBlock.Rect.Height);
            textHolder.RectTransform.NonScaledSize = new Point(textHolder.Rect.Width, textBlock.Rect.Height);
            return textHolder;
        }

        private void AddGridTextElement(XElement element, RectTransform parent)
        {
            var text = element.ElementInnerText().Replace(@"\n", "\n");
            string[] elements = text.Split(',');
            RectTransform lineContainer = null;
            for (int i = 0; i < elements.Length; i++)
            {
                switch (i % 3)
                {
                    case 0:
                        lineContainer = AddTextElement(element, parent, elements[i], Anchor.CenterLeft).RectTransform;
                        lineContainer.Anchor = Anchor.TopCenter;
                        lineContainer.Pivot = Pivot.TopCenter;
                        lineContainer.NonScaledSize = new Point((int)(parent.NonScaledSize.X * 0.7f), lineContainer.NonScaledSize.Y);
                        break;
                    case 1:
                        AddTextElement(element, lineContainer, elements[i], Anchor.Center).GetChild<GUITextBlock>().TextAlignment = Alignment.Center;
                        break;
                    case 2:
                        AddTextElement(element, lineContainer, elements[i], Anchor.CenterRight).GetChild<GUITextBlock>().TextAlignment = Alignment.CenterRight;
                        break;
                }
            }
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

        public void Restart()
        {
            listBox.BarScroll = 0.0f;
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
