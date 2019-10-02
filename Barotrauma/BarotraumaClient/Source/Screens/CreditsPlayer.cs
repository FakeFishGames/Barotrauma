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
                GUIComponent.FromXML(subElement, listBox.Content.RectTransform);
            }
            foreach (GUIComponent child in listBox.Children)
            {
                child.CanBeFocused = false;
            }

            listBox.RecalculateChildren();
            listBox.UpdateScrollBarSize();
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
