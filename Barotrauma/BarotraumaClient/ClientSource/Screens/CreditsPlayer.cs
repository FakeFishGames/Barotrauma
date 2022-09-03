using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma
{
    class CreditsPlayer : GUIComponent
    {
        private GUIListBox listBox;

        private ContentXElement configElement;

        private float scrollSpeed;

        public bool AutoRestart = true;

        public bool Finished
        {
            get { return listBox.BarScroll >= 1.0f; }
        }

        public bool ScrollBarEnabled
        {
            get { return listBox.ScrollBarEnabled; }
            set { listBox.ScrollBarEnabled = value; }
        }

        public bool AllowMouseWheelScroll
        {
            get { return listBox.AllowMouseWheelScroll; }
            set { listBox.AllowMouseWheelScroll = value; }
        }

        public float Scroll
        {
            get { return listBox.BarScroll; }
            set { listBox.BarScroll = value; }
        }


        public CreditsPlayer(RectTransform rectT, string configFile) : base(null, rectT)
        {
            GameMain.Instance.ResolutionChanged += () =>
            {
                ClearChildren();
                Load();
            };

            var doc = XMLExtensions.TryLoadXml(configFile);
            if (doc == null) { return; }
            configElement = doc.Root.FromContent(ContentPath.FromRaw(configFile));

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

            foreach (var subElement in configElement.Elements())
            {
                FromXML(subElement, listBox.Content.RectTransform);
            }
            foreach (GUIComponent child in listBox.Content.Children)
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
            if (!Visible) { return; }

            listBox.BarScroll += scrollSpeed / listBox.TotalSize * deltaTime;

            if (AutoRestart && listBox.BarScroll >= 1.0f)
            {
                listBox.BarScroll = 0.0f;
            }
        }
    }
}
