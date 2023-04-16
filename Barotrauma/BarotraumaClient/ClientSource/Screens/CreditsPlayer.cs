using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class CreditsPlayer : GUIComponent
    {
        private GUIListBox listBox;

        private readonly ContentXElement configElement;

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

        public readonly GUIButton CloseButton;


        public CreditsPlayer(RectTransform rectT, string configFile) : base(null, rectT)
        {
            GameMain.Instance.ResolutionChanged += () =>
            {
                ClearChildren();
                Load();
            };

            var doc = XMLExtensions.TryLoadXml(configFile);
            if (doc == null) { return; }
            configElement = doc.Root.FromPackage(ContentPackageManager.VanillaCorePackage);

            Load();

            CloseButton = new GUIButton(new RectTransform(new Vector2(0.1f), RectTransform, Anchor.BottomRight, maxSize: new Point(GUI.IntScale(300), GUI.IntScale(50))) 
                { AbsoluteOffset = new Point(GUI.IntScale(20), GUI.IntScale(20) + (Rect.Bottom - GameMain.GraphicsHeight)) },
                TextManager.Get("close"));
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
