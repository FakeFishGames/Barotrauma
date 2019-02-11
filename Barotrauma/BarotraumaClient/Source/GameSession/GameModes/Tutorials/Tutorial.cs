using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma.Tutorials
{
    abstract class Tutorial
    {
        public static List<Tutorial> Tutorials;

        protected GUIComponent infoBox;
        private Action infoBoxClosedCallback;
        protected XElement configElement;

        private enum TutorialType { None, Scenario, Contextual };
        private TutorialType tutorialType = TutorialType.None;

        public string Name
        {
            get;
            protected set;
        }

        private bool completed;
        public bool Completed
        {
            get { return completed; }
            protected set
            {
                if (completed == value) return;
                completed = value;
                GameMain.Config.SaveNewPlayerConfig();
            }
        }

        public static void Init()
        {
            Tutorials = new List<Tutorial>();
            foreach (string file in GameMain.Instance.GetFilesOfType(ContentType.Tutorials))
            {
                XDocument doc = XMLExtensions.TryLoadXml(file);
                if (doc?.Root == null) continue;

                foreach (XElement element in doc.Root.Elements())
                {
                    Tutorial newTutorial = Load(element);
                    if (newTutorial != null) Tutorials.Add(newTutorial);
                }
            }
        }

        private static Tutorial Load(XElement element)
        {
            Type t;
            string type = element.Name.ToString().ToLowerInvariant();
            try
            {
                // Get the type of a specified class.                
                t = Type.GetType("Barotrauma.Tutorials." + type + "", false, true);
                if (t == null)
                {
                    DebugConsole.ThrowError("Could not find tutorial type \"" + type + "\"");
                    return null;
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Could not find tutorial type \"" + type + "\"", e);
                return null;
            }

            ConstructorInfo constructor;
            try
            {
                if (!t.IsSubclassOf(typeof(Tutorial))) return null;
                constructor = t.GetConstructor(new Type[] { typeof(XElement) });
                if (constructor == null)
                {
                    DebugConsole.ThrowError("Could not find the constructor of tutorial type \"" + type + "\"");
                    return null;
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Could not find the constructor of tutorial type \"" + type + "\"", e);
                return null;
            }
            Tutorial tutorial = null;
            try
            {
                object component = constructor.Invoke(new object[] { element });
                tutorial = (Tutorial)component;
            }
            catch (TargetInvocationException e)
            {
                DebugConsole.ThrowError("Error while loading tutorial of the type " + t + ".", e.InnerException);
            }

            return tutorial;
        }

        public Tutorial(XElement element)
        {
            configElement = element;
            Name = element.GetAttributeString("name", "Unnamed");
            Completed = GameMain.Config.CompletedTutorialNames.Contains(Name);
            Enum.TryParse(element.GetAttributeString("tutorialtype", "Scenario"), true, out tutorialType);
        }
        
        public virtual void Initialize()
        {

        }

        public virtual void Start()
        {
           
        }

        public virtual void AddToGUIUpdateList()
        {
            if (infoBox != null) infoBox.AddToGUIUpdateList();
        }

        public virtual void Update(float deltaTime)
        {
           
        }

        public virtual IEnumerable<object> UpdateState()
        {
            yield return CoroutineStatus.Success;
        }

        protected bool CloseInfoFrame(GUIButton button, object userData)
        {
            infoBox = null;
            infoBoxClosedCallback?.Invoke();
            return true;
        }

        protected GUIComponent CreateInfoFrame(string text, bool hasButton = false, Action callback = null)
        {
            int width = 300;
            int height = hasButton ? 110 : 80;

            string wrappedText = ToolBox.WrapText(text, width, GUI.Font);

            height += wrappedText.Split('\n').Length * 25;

            var infoBlock = new GUIFrame(new RectTransform(new Point(width, height), GUI.Canvas, Anchor.TopRight) { AbsoluteOffset = new Point(20) });
            infoBlock.Flash(Color.Green);

            var textBlock = new GUITextBlock(new RectTransform(new Vector2(0.9f, 0.7f), infoBlock.RectTransform, Anchor.Center),
                text, wrap: true);

            infoBoxClosedCallback = callback;

            if (hasButton)
            {
                var okButton = new GUIButton(new RectTransform(new Point(160, 50), infoBlock.RectTransform, Anchor.BottomCenter, Pivot.TopCenter) { AbsoluteOffset = new Point(0, -10) },
                    TextManager.Get("OK"))
                {
                    OnClicked = CloseInfoFrame
                };
            }
            
            GUI.PlayUISound(GUISoundType.UIMessage);

            return infoBlock;
        }

        protected GUIComponent CreateInfoFrame(string title, string text, int width, int height, string anchorStr, bool hasButton = false, Action callback = null)
        {
            if (hasButton) height += 30;

            string wrappedText = ToolBox.WrapText(text, width, GUI.Font);          

            height += wrappedText.Split('\n').Length * 25;

            if (title.Length > 0)
            {
                height += 35;
            }

            Anchor anchor = Anchor.TopRight;
            Enum.TryParse(anchorStr, out anchor);

            var infoBlock = new GUIFrame(new RectTransform(new Point(width, height), GUI.Canvas, anchor) { AbsoluteOffset = new Point(20) });
            infoBlock.Flash(Color.Green);

            if (title.Length > 0)
            {
                var titleBlock = new GUITextBlock(new RectTransform(new Vector2(1f, .35f), infoBlock.RectTransform, Anchor.TopCenter,
                Pivot.TopCenter), title, font: GUI.LargeFont, textAlignment: Alignment.Center);
            }

            var textBlock = new GUITextBlock(new RectTransform(new Vector2(0.9f, 1f), infoBlock.RectTransform, Anchor.BottomCenter),
                text, wrap: true);

            infoBoxClosedCallback = callback;

            if (hasButton)
            {
                var okButton = new GUIButton(new RectTransform(new Point(160, 50), infoBlock.RectTransform, Anchor.BottomCenter, Pivot.TopCenter) { AbsoluteOffset = new Point(0, -10) },
                    TextManager.Get("OK"))
                {
                    OnClicked = CloseInfoFrame
                };
            }

            GUI.PlayUISound(GUISoundType.UIMessage);

            return infoBlock;
        }

        protected bool Restart(GUIButton button, object obj)
        {
            TutorialMode.StartTutorial(this);
            return true;
        }
    }
}
