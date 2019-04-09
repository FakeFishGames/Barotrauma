using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma.Tutorials
{
    abstract class Tutorial
    {
        #region Tutorial variables
        public static bool Initialized = false;
        public static bool ContentRunning = false;
        public static List<Tutorial> Tutorials;

        protected bool started = false;
        protected GUIComponent infoBox;
        private Action infoBoxClosedCallback;
        protected XElement configElement;

        private enum TutorialType { None, Scenario, Contextual };
        private TutorialType tutorialType = TutorialType.None;

        protected VideoPlayer videoPlayer;
        protected enum ContentTypes { None = 0, Video = 1, ManualVideo = 2, TextOnly = 3 };
        protected string playableContentPath;
        protected Point screenResolution;
        protected float prevUIScale;

        private GUIFrame holderFrame, objectiveFrame;
        private List<TutorialSegment> activeObjectives = new List<TutorialSegment>();
        private string objectiveTranslated;

        protected TutorialSegment activeContentSegment;
        protected List<TutorialSegment> segments;

        protected class TutorialSegment
        {
            public string Id;
            public string Objective;
            public ContentTypes ContentType;
            public XElement TextContent;
            public XElement VideoContent;
            public bool IsTriggered;
            public GUIButton ReplayButton;
            public GUITextBlock LinkedTitle, LinkedText;

            public TutorialSegment(XElement config)
            {
                Id = config.GetAttributeString("id", "Missing ID");
                Objective = TextManager.Get(config.GetAttributeString("objective", string.Empty), true);
                Enum.TryParse(config.GetAttributeString("contenttype", "None"), true, out ContentType);
                IsTriggered = config.GetAttributeBool("istriggered", false);

                switch (ContentType)
                {
                    case ContentTypes.None:
                        break;
                    case ContentTypes.Video:
                    case ContentTypes.ManualVideo:
                        VideoContent = config.Element("Video");
                        TextContent = config.Element("Text");
                        break;
                    case ContentTypes.TextOnly:
                        TextContent = config.Element("Text");
                        break;
                }
            }
        }

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
        #endregion

        #region Tutorial Controls
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
            completed = GameMain.Config.CompletedTutorialNames.Contains(Name);
            Enum.TryParse(element.GetAttributeString("tutorialtype", "Scenario"), true, out tutorialType);
            playableContentPath = element.GetAttributeString("playablecontentpath", "");

            segments = new List<TutorialSegment>();

            foreach (var segment in element.Elements("Segment"))
            {
                segments.Add(new TutorialSegment(segment));
            }
        }
        
        public virtual void Initialize()
        {
            if (Initialized) return;
            Initialized = true;

            videoPlayer = new VideoPlayer();
        }

        public virtual void Start()
        {
            activeObjectives.Clear();
            objectiveTranslated = TextManager.Get("Objective");
            CreateObjectiveFrame();
        }

        public virtual void AddToGUIUpdateList()
        {
            if (GameMain.GraphicsWidth != screenResolution.X || GameMain.GraphicsHeight != screenResolution.Y || prevUIScale != GUI.Scale)
            {
                CreateObjectiveFrame();
            }

            if (objectiveFrame != null && activeObjectives.Count > 0)
            {
                objectiveFrame.AddToGUIUpdateList(order: -1);
            }

            if (infoBox != null) infoBox.AddToGUIUpdateList(order: 100);
            if (videoPlayer != null) videoPlayer.AddToGUIUpdateList(order: 100);
        }

        public virtual void Update(float deltaTime)
        {
            if (videoPlayer != null)
            {
                videoPlayer.Update();
            }

            if (activeObjectives != null)
            {
                for (int i = 0; i < activeObjectives.Count; i++)
                {
                    CheckActiveObjectives(activeObjectives[i], deltaTime);
                }
            }

            if (infoBox != null)
            {
                if (PlayerInput.KeyHit(Keys.Enter) || PlayerInput.KeyHit(Keys.Escape))
                {
                    CloseInfoFrame(null, null);
                }
            }
        }

        public virtual IEnumerable<object> UpdateState()
        {
            yield return CoroutineStatus.Success;
        }

        protected bool Restart(GUIButton button, object obj)
        {
            TutorialMode.StartTutorial(this);
            return true;
        }

        protected virtual void TriggerTutorialSegment(int index, params object[] args)
        {
            Inventory.draggingItem = null;
            ContentRunning = true;
            activeContentSegment = segments[index];

            string tutorialText = TextManager.GetFormatted(activeContentSegment.TextContent.GetAttributeString("tag", ""), true, args);
            string objectiveText = string.Empty;

            if (!string.IsNullOrEmpty(activeContentSegment.Objective))
            {
                if (args.Length == 0)
                {
                    objectiveText = activeContentSegment.Objective;
                }
                else
                {
                    objectiveText = string.Format(activeContentSegment.Objective, args);
                }

                activeContentSegment.Objective = objectiveText;
            }
            else
            {
                activeContentSegment.IsTriggered = true; // Complete at this stage only if no related objective
            }

            switch (activeContentSegment.ContentType)
            {
                case ContentTypes.None:
                    break;
                case ContentTypes.Video:
                    infoBox = CreateInfoFrame(TextManager.Get(activeContentSegment.Id), tutorialText,
                          activeContentSegment.TextContent.GetAttributeInt("width", 300),
                          activeContentSegment.TextContent.GetAttributeInt("height", 80),
                          activeContentSegment.TextContent.GetAttributeString("anchor", "Center"), true, () => LoadVideo(activeContentSegment));
                    break;
                case ContentTypes.ManualVideo:
                    infoBox = CreateInfoFrame(TextManager.Get(activeContentSegment.Id), tutorialText,
                        activeContentSegment.TextContent.GetAttributeInt("width", 300),
                        activeContentSegment.TextContent.GetAttributeInt("height", 80),
                        activeContentSegment.TextContent.GetAttributeString("anchor", "Center"), true, StopCurrentContentSegment);
                    break;
                case ContentTypes.TextOnly:
                    infoBox = CreateInfoFrame(TextManager.Get(activeContentSegment.Id), tutorialText,
                                              activeContentSegment.TextContent.GetAttributeInt("width", 300),
                                              activeContentSegment.TextContent.GetAttributeInt("height", 80),
                                              activeContentSegment.TextContent.GetAttributeString("anchor", "Center"), true, StopCurrentContentSegment);
                    break;
            }
        }

        public virtual void Stop()
        {
            if (videoPlayer != null)
            {
                videoPlayer.Remove();
                videoPlayer = null;
            }
        }
        #endregion

        #region Objectives
        private void CreateObjectiveFrame()
        {
            holderFrame = new GUIFrame(new RectTransform(new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight), GUI.Canvas, Anchor.Center));
            objectiveFrame = new GUIFrame(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.ObjectiveAnchor, holderFrame.RectTransform), style: null);

            for (int i = 0; i < activeObjectives.Count; i++)
            {
                CreateObjectiveGUI(activeObjectives[i], i);
            }

            screenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            prevUIScale = GUI.Scale;
        }

        protected void StopCurrentContentSegment()
        {
            if (!string.IsNullOrEmpty(activeContentSegment.Objective))
            {
                AddNewObjective(activeContentSegment);
            }

            activeContentSegment = null;
            ContentRunning = false;
        }

        protected virtual void CheckActiveObjectives(TutorialSegment objective, float deltaTime)
        {

        }

        protected bool HasObjective(TutorialSegment segment)
        {
            return activeObjectives.Contains(segment);
        }

        protected void AddNewObjective(TutorialSegment segment)
        {
            activeObjectives.Add(segment);
            CreateObjectiveGUI(segment, activeObjectives.Count - 1);
        }

        private void CreateObjectiveGUI(TutorialSegment segment, int index)
        {
            Point replayButtonSize = new Point((int)(GUI.ObjectiveNameFont.MeasureString(segment.Objective).X * GUI.Scale), (int)(GUI.ObjectiveNameFont.MeasureString(segment.Objective).Y * 1.45f * GUI.Scale));

            segment.ReplayButton = new GUIButton(new RectTransform(replayButtonSize, objectiveFrame.RectTransform, Anchor.TopRight, Pivot.TopRight) { AbsoluteOffset = new Point(0, (replayButtonSize.Y + (int)(20f * GUI.Scale)) * index) }, style: null);
            segment.ReplayButton.OnClicked += (GUIButton btn, object userdata) =>
            {
                ReplaySegmentVideo(segment);
                return true;
            };

            int yOffset = (int)((GUI.ObjectiveNameFont.MeasureString(objectiveTranslated).Y / 2f + 5) * GUI.Scale);
            segment.LinkedTitle = new GUITextBlock(new RectTransform(new Point(replayButtonSize.X, yOffset), segment.ReplayButton.RectTransform, Anchor.Center, Pivot.BottomCenter) { AbsoluteOffset = new Point((int)(10 * GUI.Scale), 0) }, objectiveTranslated, textColor: Color.White, font: GUI.ObjectiveTitleFont, textAlignment: Alignment.CenterRight);
            segment.LinkedText = new GUITextBlock(new RectTransform(new Point(replayButtonSize.X, yOffset), segment.ReplayButton.RectTransform, Anchor.Center, Pivot.TopCenter) { AbsoluteOffset = new Point((int)(10 * GUI.Scale), 0) }, segment.Objective, textColor: new Color(4, 180, 108), font: GUI.ObjectiveNameFont, textAlignment: Alignment.CenterRight);

            segment.LinkedTitle.TextScale = segment.LinkedText.TextScale = GUI.Scale;

            segment.LinkedTitle.Color = segment.LinkedTitle.HoverColor = segment.LinkedTitle.PressedColor = segment.LinkedTitle.SelectedColor = Color.Transparent;
            segment.LinkedText.Color = segment.LinkedText.HoverColor = segment.LinkedText.PressedColor = segment.LinkedText.SelectedColor = Color.Transparent;
            segment.ReplayButton.Color = segment.ReplayButton.HoverColor = segment.ReplayButton.PressedColor = segment.ReplayButton.SelectedColor = Color.Transparent;
        }

        private void ReplaySegmentVideo(TutorialSegment segment)
        {
            if (ContentRunning) return;
            ContentRunning = true;
            videoPlayer.LoadContent(playableContentPath, new VideoPlayer.VideoSettings(segment.VideoContent), new VideoPlayer.TextSettings(segment.VideoContent), segment.Id, true, callback: () => ContentRunning = false);
        }

        protected void RemoveCompletedObjective(TutorialSegment objective)
        {
            if (!HasObjective(objective)) return;
            objective.IsTriggered = true;

            int checkMarkHeight = (int)(objective.ReplayButton.Rect.Height * 1.2f);
            int checkMarkWidth = (int)(checkMarkHeight * 0.93f);

            Color color = new Color(4, 180, 108);
            RectTransform rectTA = new RectTransform(new Point(checkMarkWidth, checkMarkHeight), objective.ReplayButton.RectTransform, Anchor.BottomLeft, Pivot.BottomLeft);
            rectTA.AbsoluteOffset = new Point(-rectTA.Rect.Width - 5, 0);
            GUIImage checkmark = new GUIImage(rectTA, "CheckMark");
            checkmark.Color = color;

            RectTransform rectTB = new RectTransform(new Vector2(1.1f, .8f), objective.LinkedText.RectTransform, Anchor.Center, Pivot.Center);
            GUIImage stroke = new GUIImage(rectTB, "Stroke");
            stroke.Color = color;

            CoroutineManager.StartCoroutine(WaitForObjectiveEnd(objective));
        }

        private IEnumerable<object> WaitForObjectiveEnd(TutorialSegment objective)
        {
            yield return new WaitForSeconds(2.0f);
            objectiveFrame.RemoveChild(objective.ReplayButton);
            activeObjectives.Remove(objective);

            for (int i = 0; i < activeObjectives.Count; i++)
            {
                activeObjectives[i].ReplayButton.RectTransform.AbsoluteOffset = new Point(0, (activeObjectives[i].ReplayButton.Rect.Height + 20) * i);
            }
        }
        #endregion

        #region InfoFrame
        protected bool CloseInfoFrame(GUIButton button, object userData)
        {
            infoBox = null;
            infoBoxClosedCallback?.Invoke();
            return true;
        }

        protected GUIComponent CreateInfoFrame(string title, string text, int width = 300, int height = 80, string anchorStr = "", bool hasButton = false, Action callback = null)
        {
            if (hasButton) height += 30;

            string wrappedText = ToolBox.WrapText(text, width, GUI.Font);          

            height += wrappedText.Split('\n').Length * 25;

            if (title.Length > 0)
            {
                height += 35;
            }

            Anchor anchor = Anchor.TopRight;

            if (anchorStr != string.Empty)
            {
                Enum.TryParse(anchorStr, out anchor);
            }

            var infoBlock = new GUIFrame(new RectTransform(new Point((int)(width * GUI.Scale), (int)(height * GUI.Scale)), GUI.Canvas, anchor) { AbsoluteOffset = new Point(20) });
            infoBlock.Flash(Color.Green);

            if (title.Length > 0)
            {
                var titleBlock = new GUITextBlock(new RectTransform(new Vector2(1f, .35f), infoBlock.RectTransform, Anchor.TopCenter,
                Pivot.TopCenter), title, font: GUI.VideoTitleFont, textAlignment: Alignment.Center, textColor: new Color(253, 174, 0));
                titleBlock.TextScale = GUI.Scale;
            }

            var textBlock = new GUITextBlock(new RectTransform(new Vector2(0.9f, 1f), infoBlock.RectTransform, Anchor.BottomCenter),
                text, wrap: true);
            textBlock.TextScale = GUI.Scale;

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
        #endregion

        #region Video
        protected void LoadVideo(TutorialSegment segment)
        {
            videoPlayer.LoadContent(playableContentPath, new VideoPlayer.VideoSettings(segment.VideoContent), new VideoPlayer.TextSettings(segment.VideoContent), segment.Id, true, segment.Objective, StopCurrentContentSegment);
        }
        #endregion
    }
}
