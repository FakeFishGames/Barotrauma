using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using Barotrauma.Extensions;
using System.Collections.Immutable;

namespace Barotrauma.Tutorials
{
    enum TutorialContentType { None = 0, Video = 1, ManualVideo = 2, TextOnly = 3 };

    /// <summary>
    /// If you're seeing this and are currently working on improving the tutorials, consider
    /// deleting this class and all that derive from it, and starting from scratch.
    /// </summary>
    abstract class Tutorial
    {
        #region Constants
        public const string PlayableContentPath = "Content/Tutorials/TutorialVideos/";
        #endregion

        #region Tutorial variables
        public static ImmutableHashSet<Type> Types;
        static Tutorial()
        {
            Types = ReflectionUtils.GetDerivedNonAbstract<Tutorial>()
                .ToImmutableHashSet();
        }

        public readonly Identifier Identifier;

        public LocalizedString DisplayName { get; }

        public bool ContentRunning { get; protected set; }

        protected GUIComponent infoBox;
        private Action infoBoxClosedCallback;

        protected VideoPlayer videoPlayer;
        protected Point screenResolution;
        protected WindowMode windowMode;
        protected float prevUIScale;

        private GUIFrame holderFrame, objectiveFrame;
        private readonly List<Index> activeObjectives;
        private readonly LocalizedString objectiveTranslated;

        protected readonly ImmutableArray<Segment> segments;
        protected Index activeContentSegmentIndex;
        protected Segment activeContentSegment => segments[activeContentSegmentIndex];

        protected class Segment
        {
            public struct Text
            {
                public Identifier Tag;
                public int Width;
                public int Height;
                public Anchor Anchor;
            }

            public struct Video
            {
                public string File;
                public Identifier TextTag;
                public int Width;
                public int Height;
            }

            public bool IsTriggered;
            public GUIButton ReplayButton;
            public GUITextBlock LinkedTitle, LinkedText;
            public object[] Args;
            public LocalizedString Objective;

            public readonly Identifier Id;
            public readonly Text? TextContent;
            public readonly Video? VideoContent;
            public readonly TutorialContentType ContentType;

            public Segment(Identifier id, Identifier objectiveTextTag, TutorialContentType contentType, Text? textContent = null, Video? videoContent = null)
            {
                Id = id;
                Objective = TextManager.ParseInputTypes(TextManager.Get(objectiveTextTag));
                ContentType = contentType;
                TextContent = textContent;
                VideoContent = videoContent;

                IsTriggered = false;
            }
        }

        private bool completed;
        public bool Completed
        {
            get { return completed; }
            protected set
            {
                if (completed == value) { return; }
                completed = value;
                if (value)
                {
                    CompletedTutorials.Instance.Add(Identifier);
                }
                GameSettings.SaveCurrentConfig();
            }
        }
        #endregion

        #region Tutorial Controls
        protected Tutorial(Identifier identifier, params Segment[] segments)
        {
            Identifier = identifier;
            this.segments = segments.ToImmutableArray();
            DisplayName = TextManager.Get(Identifier);
            activeObjectives = new List<Index>();
            objectiveTranslated = TextManager.Get("Tutorial.Objective");
        }

        protected abstract IEnumerable<CoroutineStatus> Loading();

        public void Start()
        {
            videoPlayer = new VideoPlayer();
            GameMain.Instance.ShowLoading(Loading());
            
            activeObjectives.Clear();
            CreateObjectiveFrame();

            // Setup doors:  Clear all requirements, unless the door is setup as locked.
            foreach (var item in Item.ItemList)
            {
                var door = item.GetComponent<Door>();
                if (door != null)
                {
                    if (door.requiredItems.Values.None(ris => ris.None(ri => ri.Identifiers.None(i => i == "locked"))))
                    {
                        door.requiredItems.Clear();
                    }
                }
            }
        }

        public virtual void AddToGUIUpdateList()
        {
            if (GameMain.GraphicsWidth != screenResolution.X || GameMain.GraphicsHeight != screenResolution.Y || prevUIScale != GUI.Scale || GameSettings.CurrentConfig.Graphics.DisplayMode != windowMode)
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
            videoPlayer?.Update();

            if (activeObjectives != null)
            {
                for (int i = 0; i < activeObjectives.Count; i++)
                {
                    CheckActiveObjectives(activeObjectives[i], deltaTime);
                }
            }
        }

        public void CloseActiveContentGUI()
        {
            if (videoPlayer.IsPlaying)
            {
                videoPlayer.Stop();
            }
            else if (infoBox != null)
            {
                CloseInfoFrame(null, null);
            }
        }

        public virtual IEnumerable<CoroutineStatus> UpdateState()
        {
            yield return CoroutineStatus.Success;
        }

        protected bool Restart(GUIButton button, object obj)
        {
            GUI.PreventPauseMenuToggle = false;
            return true;
        }

        protected virtual void TriggerTutorialSegment(Index index, params object[] args)
        {
            Inventory.DraggingItems.Clear();
            ContentRunning = true;
            activeContentSegmentIndex = index;
            segments[index].Args = args;

            LocalizedString tutorialText = TextManager.GetFormatted(segments[index].TextContent.Value.Tag, args);
            tutorialText = TextManager.ParseInputTypes(tutorialText);
            LocalizedString objectiveText = string.Empty;

            if (!segments[index].Objective.IsNullOrEmpty())
            {
                if (args.Length == 0)
                {
                    objectiveText = segments[index].Objective;
                }
                else
                {
                    objectiveText = TextManager.GetFormatted(segments[index].Objective, args);
                }
                objectiveText = TextManager.ParseInputTypes(objectiveText);
                segments[index].Objective = objectiveText;
            }
            else
            {
                segments[index].IsTriggered = true; // Complete at this stage only if no related objective
            }


            switch (segments[index].ContentType)
            {
                case TutorialContentType.None:
                    break;
                case TutorialContentType.Video:
                    infoBox = CreateInfoFrame(TextManager.Get(activeContentSegment.Id), tutorialText,
                              activeContentSegment.TextContent.Value.Width,
                              activeContentSegment.TextContent.Value.Height,
                              activeContentSegment.TextContent.Value.Anchor, true, () => LoadVideo(activeContentSegment));
                    break;
                case TutorialContentType.ManualVideo:
                    infoBox = CreateInfoFrame(TextManager.Get(activeContentSegment.Id), tutorialText,
                            activeContentSegment.TextContent.Value.Width,
                            activeContentSegment.TextContent.Value.Height,
                        activeContentSegment.TextContent.Value.Anchor, true, StopCurrentContentSegment, () => LoadVideo(activeContentSegment));
                    break;
                case TutorialContentType.TextOnly:
                    infoBox = CreateInfoFrame(TextManager.Get(activeContentSegment.Id), tutorialText,
                            activeContentSegment.TextContent.Value.Width,
                            activeContentSegment.TextContent.Value.Height,
                            activeContentSegment.TextContent.Value.Anchor, true, StopCurrentContentSegment);
                    break;
            }
        }

        public virtual void Stop()
        {
            ContentRunning = false;
            infoBox = null;
            videoPlayer.Remove();
        }
        #endregion

        #region Objectives
        private void CreateObjectiveFrame()
        {
            holderFrame = new GUIFrame(new RectTransform(new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight), GUI.Canvas, Anchor.Center));
            objectiveFrame = new GUIFrame(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.ObjectiveAnchor, holderFrame.RectTransform), style: null);

            for (int i = 0; i < activeObjectives.Count; i++)
            {
                CreateObjectiveGUI(activeObjectives[i], i, segments[activeObjectives[i]].ContentType);
            }

            screenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            windowMode = GameSettings.CurrentConfig.Graphics.DisplayMode;
            prevUIScale = GUI.Scale;
        }

        protected void StopCurrentContentSegment()
        {
            if (!activeContentSegment.Objective.IsNullOrEmpty())
            {
                AddNewObjective(activeContentSegmentIndex, activeContentSegment.ContentType);
            }

            ContentRunning = false;
            activeContentSegmentIndex = Index.End;
        }

        protected virtual void CheckActiveObjectives(Index objective, float deltaTime)
        {

        }

        protected bool HasObjective(Index segment)
        {
            return activeObjectives.Contains(segment);
        }

        protected void AddNewObjective(Index segment, TutorialContentType type)
        {
            activeObjectives.Add(segment);
            CreateObjectiveGUI(segment, activeObjectives.Count - 1, type);
        }

        private void CreateObjectiveGUI(Index segmentIndex, int index, TutorialContentType type)
        {
            var segment = segments[segmentIndex];
            LocalizedString objectiveText = TextManager.ParseInputTypes(segment.Objective);
            Point replayButtonSize = new Point((int)(GUIStyle.LargeFont.MeasureString(objectiveText).X), (int)(GUIStyle.LargeFont.MeasureString(objectiveText).Y * 1.45f));

            segment.ReplayButton = new GUIButton(new RectTransform(replayButtonSize, objectiveFrame.RectTransform, Anchor.TopLeft, Pivot.TopLeft) { AbsoluteOffset = new Point(0, (replayButtonSize.Y + (int)(20f * GUI.Scale)) * index) }, style: null);
            segment.ReplayButton.OnClicked += (GUIButton btn, object userdata) =>
            {
                if (type == TutorialContentType.Video)
                {
                    ReplaySegmentVideo(segment);
                }
                else
                {
                    ShowSegmentText(segment);
                }
                return true;
            };

            LocalizedString objectiveTitleText = TextManager.ParseInputTypes(objectiveTranslated);
            int yOffset = (int)((GUIStyle.SubHeadingFont.MeasureString(objectiveTitleText).Y + 5));
            segment.LinkedTitle = new GUITextBlock(new RectTransform(new Point((int)GUIStyle.SubHeadingFont.MeasureString(objectiveTitleText).X, yOffset), segment.ReplayButton.RectTransform, Anchor.CenterLeft, Pivot.BottomLeft) /*{ AbsoluteOffset = new Point((int)(-10 * GUI.Scale), 0) }*/,
                objectiveTitleText, textColor: Color.White, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft)
            {
                ForceUpperCase = ForceUpperCase.Yes
            };

            segment.LinkedText = new GUITextBlock(new RectTransform(new Point((int)GUIStyle.LargeFont.MeasureString(objectiveText).X, yOffset), segment.ReplayButton.RectTransform, Anchor.CenterLeft, Pivot.TopLeft) /*{ AbsoluteOffset = new Point((int)(10 * GUI.Scale), 0) }*/,
                objectiveText, textColor: new Color(4, 180, 108), font: GUIStyle.LargeFont, textAlignment: Alignment.CenterLeft);
            
            segment.LinkedTitle.Color = segment.LinkedTitle.HoverColor = segment.LinkedTitle.PressedColor = segment.LinkedTitle.SelectedColor = Color.Transparent;
            segment.LinkedText.Color = segment.LinkedText.HoverColor = segment.LinkedText.PressedColor = segment.LinkedText.SelectedColor = Color.Transparent;
            segment.ReplayButton.Color = segment.ReplayButton.HoverColor = segment.ReplayButton.PressedColor = segment.ReplayButton.SelectedColor = Color.Transparent;
        }

        private void ReplaySegmentVideo(Segment segment)
        {
            if (ContentRunning) return;
            Inventory.DraggingItems.Clear();
            ContentRunning = true;
            LoadVideo(segment);
            //videoPlayer.LoadContent(playableContentPath, new VideoPlayer.VideoSettings(segment.VideoContent), new VideoPlayer.TextSettings(segment.VideoContent), segment.Id, true, callback: () => ContentRunning = false);
        }

        private void ShowSegmentText(Segment segment)
        {
            if (ContentRunning) return;
            Inventory.DraggingItems.Clear();
            ContentRunning = true;

            LocalizedString tutorialText = TextManager.GetFormatted(segment.TextContent.Value.Tag, segment.Args);

            Action videoAction = null;

            if (segment.ContentType != TutorialContentType.TextOnly)
            {
                videoAction = () => LoadVideo(segment);
            }

            infoBox = CreateInfoFrame(TextManager.Get(segment.Id), tutorialText,
            segment.TextContent.Value.Width,
            segment.TextContent.Value.Height,
            segment.TextContent.Value.Anchor, true, () => ContentRunning = false, videoAction);
        }

        protected void RemoveCompletedObjective(Index segmentIndex)
        {
            if (!HasObjective(segmentIndex)) return;
            var segment = segments[segmentIndex];
            segment.IsTriggered = true;
            segment.ReplayButton.OnClicked = null;

            int checkMarkHeight = (int)(segment.ReplayButton.Rect.Height * 1.2f);
            int checkMarkWidth = (int)(checkMarkHeight * 0.93f);

            Color color = new Color(4, 180, 108);

            int objectiveTextWidth = segment.LinkedText.Rect.Width;
            int objectiveTitleWidth = segment.LinkedTitle.Rect.Width;

            RectTransform rectTA;
            if (objectiveTextWidth > objectiveTitleWidth)
            {
                rectTA = new RectTransform(new Point(checkMarkWidth, checkMarkHeight), segment.ReplayButton.RectTransform, Anchor.BottomRight, Pivot.BottomRight);
                rectTA.AbsoluteOffset = new Point(-rectTA.Rect.Width - (int)(25 * GUI.Scale), 0);
            }
            else
            {
                rectTA = new RectTransform(new Point(checkMarkWidth, checkMarkHeight), segment.ReplayButton.RectTransform, Anchor.BottomRight, Pivot.BottomRight);
                rectTA.AbsoluteOffset = new Point(-rectTA.Rect.Width - (int)(25 * GUI.Scale) - (objectiveTitleWidth - objectiveTextWidth), 0);
            }

            GUIImage checkmark = new GUIImage(rectTA, "CheckMark");
            checkmark.Color = checkmark.SelectedColor = checkmark.HoverColor = checkmark.PressedColor = color;  

            RectTransform rectTB = new RectTransform(new Vector2(1.0f, .8f), segment.LinkedText.RectTransform, Anchor.Center, Pivot.Center);
            GUIImage stroke = new GUIImage(rectTB, "Stroke");
            stroke.Color = stroke.SelectedColor = stroke.HoverColor = stroke.PressedColor = color;

            CoroutineManager.StartCoroutine(WaitForObjectiveEnd(segmentIndex));
        }

        private IEnumerable<CoroutineStatus> WaitForObjectiveEnd(Index objectiveIndex)
        {
            var objective = segments[objectiveIndex];
            yield return new WaitForSeconds(2.0f);
            objectiveFrame.RemoveChild(objective.ReplayButton);
            activeObjectives.Remove(objectiveIndex);

            for (int i = 0; i < activeObjectives.Count; i++)
            {
                var activeObjective = segments[activeObjectives[i]];
                activeObjective.ReplayButton.RectTransform.AbsoluteOffset = new Point(0, (activeObjective.ReplayButton.Rect.Height + 20) * i);
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

        protected GUIComponent CreateInfoFrame(LocalizedString title, LocalizedString text, int width = 300, int height = 80, Anchor anchor = Anchor.TopRight, bool hasButton = false, Action callback = null, Action showVideo = null)
        {
            if (hasButton) height += 60;

            width = (int)(width * GUI.Scale);
            height = (int)(height * GUI.Scale);

            LocalizedString wrappedText = ToolBox.WrapText(text, width, GUIStyle.Font);
            height += (int)GUIStyle.Font.MeasureString(wrappedText).Y;

            if (title.Length > 0)
            {
                height += (int)GUIStyle.Font.MeasureString(title).Y + (int)(150 * GUI.Scale);
            }

            var background = new GUIFrame(new RectTransform(new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight), GUI.Canvas, Anchor.Center), style: "GUIBackgroundBlocker");

            var infoBlock = new GUIFrame(new RectTransform(new Point(width, height), background.RectTransform, anchor));
            infoBlock.Flash(GUIStyle.Green);

            var infoContent = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), infoBlock.RectTransform, Anchor.Center))
            {
                Stretch = true,
                AbsoluteSpacing = 5
            };

            if (title.Length > 0)
            {
                var titleBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), infoContent.RectTransform), 
                    title, font: GUIStyle.LargeFont, textAlignment: Alignment.Center, textColor: new Color(253, 174, 0));
                titleBlock.RectTransform.IsFixedSize = true;
            }

            text = RichString.Rich(text);
            GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), infoContent.RectTransform), text, wrap: true);

            textBlock.RectTransform.IsFixedSize = true;
            infoBoxClosedCallback = callback;

            if (hasButton)
            {
                var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), infoContent.RectTransform), isHorizontal: true)
                {
                    RelativeSpacing = 0.1f
                };
                buttonContainer.RectTransform.IsFixedSize = true;

                if (showVideo != null)
                {
                    buttonContainer.Stretch = true;
                    var videoButton = new GUIButton(new RectTransform(new Vector2(0.4f, 1.0f), buttonContainer.RectTransform),
                        TextManager.Get("Video"), style: "GUIButtonLarge")
                    {
                        OnClicked = (GUIButton button, object obj) =>
                        {
                            showVideo();
                            return true;
                        }
                    };
                }
                else
                {
                    buttonContainer.Stretch = false;
                    buttonContainer.ChildAnchor = Anchor.Center;
                }

                var okButton = new GUIButton(new RectTransform(new Vector2(0.4f, 1.0f), buttonContainer.RectTransform),
                    TextManager.Get("OK"), style: "GUIButtonLarge")
                {
                    OnClicked = CloseInfoFrame
                };
            }

            infoBlock.RectTransform.NonScaledSize = new Point(infoBlock.Rect.Width, (int)(infoContent.Children.Sum(c => c.Rect.Height + infoContent.AbsoluteSpacing) / infoContent.RectTransform.RelativeSize.Y));

            SoundPlayer.PlayUISound(GUISoundType.UIMessage);

            return background;
        }
        #endregion

        #region Video
        protected void LoadVideo(Segment segment)
        {
            if (videoPlayer == null) videoPlayer = new VideoPlayer();
            if (segment.ContentType != TutorialContentType.ManualVideo)
            {
                videoPlayer.LoadContent(
                    PlayableContentPath,
                    new VideoPlayer.VideoSettings(segment.VideoContent.Value.File),
                    new VideoPlayer.TextSettings(segment.VideoContent.Value.TextTag, segment.VideoContent.Value.Width),
                    segment.Id, true, segment.Objective, StopCurrentContentSegment);
            }
            else
            {
                videoPlayer.LoadContent(PlayableContentPath, new VideoPlayer.VideoSettings(segment.VideoContent.Value.File), null, segment.Id, true, string.Empty, null);
            }
        }
        #endregion

        #region Highlights
        protected void HighlightInventorySlot(Inventory inventory, Identifier identifier, Color color, float fadeInDuration, float fadeOutDuration, float scaleUpAmount)
        {
            if (inventory.visualSlots == null) { return; }
            for (int i = 0; i < inventory.Capacity; i++)
            {
                if (inventory.GetItemAt(i)?.Prefab.Identifier == identifier)
                {
                    HighlightInventorySlot(inventory, i, color, fadeInDuration, fadeOutDuration, scaleUpAmount);
                }
            }
        }

        protected void HighlightInventorySlotWithTag(Inventory inventory, Identifier tag, Color color, float fadeInDuration, float fadeOutDuration, float scaleUpAmount)
        {
            if (inventory.visualSlots == null) { return; }
            for (int i = 0; i < inventory.Capacity; i++)
            {
                if (inventory.GetItemAt(i)?.HasTag(tag) ?? false)
                {
                    HighlightInventorySlot(inventory, i, color, fadeInDuration, fadeOutDuration, scaleUpAmount);
                }
            }
        }

        protected void HighlightInventorySlot(Inventory inventory, int index, Color color, float fadeInDuration, float fadeOutDuration, float scaleUpAmount)
        {
            if (inventory.visualSlots == null || index < 0 || inventory.visualSlots[index].HighlightTimer > 0) { return; }
            inventory.visualSlots[index].ShowBorderHighlight(color, fadeInDuration, fadeOutDuration, scaleUpAmount);
        }
        #endregion
    }
}
