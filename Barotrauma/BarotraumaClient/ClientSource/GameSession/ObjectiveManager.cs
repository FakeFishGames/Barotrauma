using Barotrauma.Extensions;
using Barotrauma.IO;
using Barotrauma.Tutorials;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma;

static class ObjectiveManager
{
    public class Segment
    {
        public readonly record struct Text(
            Identifier Tag,
            int Width = DefaultWidth,
            int Height = DefaultHeight,
            Anchor Anchor = Anchor.Center);

        public readonly record struct Video(
            string FullPath,
            Identifier TextTag,
            int Width = DefaultWidth,
            int Height = DefaultHeight)
        {
            public string FileName => Path.GetFileName(FullPath.CleanUpPath());
            public string ContentPath => Path.GetDirectoryName(FullPath.CleanUpPath());
        }

        private const int DefaultWidth = 450;
        private const int DefaultHeight = 80;

        public GUIImage ObjectiveStateIndicator;
        public GUIButton ObjectiveButton;
        public GUITextBlock LinkedTextBlock;
        public LocalizedString ObjectiveText;

        public readonly Identifier Id;
        public readonly Text TextContent;
        public readonly Video VideoContent;
        public readonly AutoPlayVideo AutoPlayVideo;

        public Action OnClickObjective;

        public bool IsCompleted { get; set; }

        public bool CanBeCompleted { get; set; }

        public Identifier ParentId { get; set; }

        public TutorialSegmentType SegmentType { get; private set; }

        public static Segment CreateInfoBoxSegment(Identifier id, Identifier objectiveTextTag, AutoPlayVideo autoPlayVideo, Text textContent = default, Video videoContent = default)
        {
            return new Segment(id, objectiveTextTag, autoPlayVideo, textContent, videoContent);
        }

        public static Segment CreateMessageBoxSegment(Identifier id, Identifier objectiveTextTag, Action onClickObjective)
        {
            return new Segment(id, objectiveTextTag, onClickObjective);
        }

        public static Segment CreateObjectiveSegment(Identifier id, Identifier objectiveTextTag)
        {
            return new Segment(id, objectiveTextTag);
        }

        private Segment(Identifier id, Identifier objectiveTextTag, AutoPlayVideo autoPlayVideo, Text textContent = default, Video videoContent = default)
        {
            Id = id;
            ObjectiveText = TextManager.ParseInputTypes(TextManager.Get(objectiveTextTag));
            AutoPlayVideo = autoPlayVideo;
            TextContent = textContent;
            VideoContent = videoContent;
            SegmentType = TutorialSegmentType.InfoBox;
        }

        private Segment(Identifier id, Identifier objectiveTextTag, Action onClickObjective)
        {
            Id = id;
            ObjectiveText = TextManager.ParseInputTypes(TextManager.Get(objectiveTextTag));
            OnClickObjective = onClickObjective;
            SegmentType = TutorialSegmentType.MessageBox;
        }

        private Segment(Identifier id, Identifier objectiveTextTag)
        {
            Id = id;
            ObjectiveText = TextManager.ParseInputTypes(TextManager.Get(objectiveTextTag));
            SegmentType = TutorialSegmentType.Objective;
        }

        public void ConnectMessageBox(Segment messageBoxSegment)
        {
            SegmentType = TutorialSegmentType.MessageBox;
            OnClickObjective = messageBoxSegment.OnClickObjective;
        }
    }

    private readonly record struct ScreenSettings(
        Point ScreenResolution = default,
        float UiScale = default,
        WindowMode WindowMode = default)
    {
        public bool HaveChanged() =>
            GameMain.GraphicsWidth != ScreenResolution.X ||
            GameMain.GraphicsHeight != ScreenResolution.Y ||
            GUI.Scale != UiScale ||
            GameSettings.CurrentConfig.Graphics.DisplayMode != WindowMode;
    };

    private const float ObjectiveComponentAnimationTime = 1.5f;

    public static bool ContentRunning { get; private set; }

    public static VideoPlayer VideoPlayer { get; } = new VideoPlayer();

    private static Segment ActiveContentSegment { get; set; }

    private readonly static List<Segment> activeObjectives = new List<Segment>();
    private static GUIComponent infoBox;
    private static Action infoBoxClosedCallback;
    private static ScreenSettings screenSettings;
    private static GUILayoutGroup objectiveGroup;
    private static LocalizedString objectiveTextTranslated;

    public static void AddToGUIUpdateList()
    {
        if (screenSettings.HaveChanged())
        {
            CreateObjectiveFrame();
        }
        if (activeObjectives.Count > 0 && GameMain.GameSession?.Campaign is not { ShowCampaignUI: true })
        {
            objectiveGroup?.AddToGUIUpdateList(order: -1);
        }
        infoBox?.AddToGUIUpdateList(order: 100);
        VideoPlayer.AddToGUIUpdateList(order: 100);
    }

    public static void TriggerTutorialSegment(Segment segment, bool connectObjective = false)
    {
        if (segment.SegmentType != TutorialSegmentType.InfoBox)
        {
            activeObjectives.Add(segment);
            AddToObjectiveList(segment, connectObjective);
            return;
        }

        Inventory.DraggingItems.Clear();
        ContentRunning = true;
        ActiveContentSegment = segment;

        var title = TextManager.Get(segment.Id);
        LocalizedString tutorialText = TextManager.GetFormatted(segment.TextContent.Tag);
        tutorialText = TextManager.ParseInputTypes(tutorialText);

        switch (segment.AutoPlayVideo)
        {
            case AutoPlayVideo.Yes:
                infoBox = CreateInfoFrame(
                    title,
                    tutorialText,
                    segment.TextContent.Width,
                    segment.TextContent.Height,
                    segment.TextContent.Anchor,
                    hasButton: true,
                    onInfoBoxClosed: LoadActiveContentVideo);
                break;
            case AutoPlayVideo.No:
                infoBox = CreateInfoFrame(
                    title,
                    tutorialText,
                    segment.TextContent.Width,
                    segment.TextContent.Height,
                    segment.TextContent.Anchor,
                    hasButton: true,
                    onInfoBoxClosed: StopCurrentContentSegment,
                    onVideoButtonClicked: LoadActiveContentVideo);
                break;
        }
    }

    public static void CompleteTutorialSegment(Identifier segmentId)
    {
        if (GetActiveObjective(segmentId) is not Segment segment || !segment.CanBeCompleted || segment.IsCompleted)
        {
            return;
        }
        if (!MarkSegmentCompleted(segment))
        {
            return;
        }
        if (GameMain.GameSession?.GameMode is TutorialMode tutorialMode)
        {
            GameAnalyticsManager.AddDesignEvent($"Tutorial:{tutorialMode.Tutorial?.Identifier}:{segmentId}:Completed");
        }
        else if (GameMain.GameSession?.GameMode is CampaignMode campaign)
        {
            GameAnalyticsManager.AddDesignEvent($"Tutorial:CampaignMode:{segmentId}:Completed");
            campaign?.CampaignMetadata?.SetValue(segmentId, true);
        }
    }

    public static bool MarkSegmentCompleted(Segment segment, bool flash = true)
    {
        segment.IsCompleted = true;
        if (GUIStyle.GetComponentStyle("ObjectiveIndicatorCompleted") is GUIComponentStyle style)
        {
            if (segment.ObjectiveStateIndicator.Style == style)
            {
                return false;
            }
            segment.ObjectiveStateIndicator.ApplyStyle(style);
        }
        if (flash)
        {
            segment.ObjectiveStateIndicator.Parent.Flash(color: GUIStyle.Green, flashDuration: 0.35f, useRectangleFlash: true);
        }
        segment.ObjectiveButton.OnClicked = null;
        segment.ObjectiveButton.CanBeFocused = false;
        return true;
    }

    public static void RemoveTutorialSegment(Identifier segmentId)
    {
        if (GetActiveObjective(segmentId) is not Segment segment)
        {
            if (GameMain.GameSession?.GameMode is TutorialMode tutorialMode)
            {
                DebugConsole.AddWarning($"Warning: tried to remove the tutorial segment \"{segmentId}\" in tutorial \"{tutorialMode.Tutorial?.Identifier}\" but it isn't active!");
            }
            return;
        }
        segment.ObjectiveStateIndicator.FadeOut(ObjectiveComponentAnimationTime, false);
        segment.LinkedTextBlock.FadeOut(ObjectiveComponentAnimationTime, false);
        var parent = segment.LinkedTextBlock.Parent;
        parent.FadeOut(ObjectiveComponentAnimationTime, true, onRemove: () =>
        {
            activeObjectives.Remove(segment);
            objectiveGroup?.Recalculate();
        });
        parent.RectTransform.MoveOverTime(GetObjectiveHiddenPosition(parent.RectTransform), ObjectiveComponentAnimationTime);
        segment.ObjectiveButton.OnClicked = null;
        segment.ObjectiveButton.CanBeFocused = false;
    }

    public static void CloseActiveContentGUI()
    {
        if (VideoPlayer.IsPlaying)
        {
            VideoPlayer.Stop();
        }
        else if (infoBox != null)
        {
            CloseInfoFrame();
        }
    }

    public static void ClearContent()
    {
        ContentRunning = false;
        infoBox = null;
    }

    public static void ResetUI()
    {
        ContentRunning = false;
        infoBox = null;
        VideoPlayer.Remove();
    }

    #region Objectives
    private static Segment GetActiveObjective(Identifier id) => activeObjectives.FirstOrDefault(s => s.Id == id);

    public static void ResetObjectives()
    {
        activeObjectives.Clear();
        ActiveContentSegment = null;
        CreateObjectiveFrame();
    }

    /// <summary>
    /// Create the objective list that holds the objectives (called on start and on resolution change)
    /// </summary>
    private static void CreateObjectiveFrame()
    {
        var objectiveListFrame = new GUIFrame(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.TutorialObjectiveListArea, GUI.Canvas), style: null)
        {
            CanBeFocused = false
        };
        objectiveGroup = new GUILayoutGroup(new RectTransform(Vector2.One, objectiveListFrame.RectTransform))
        {
            AbsoluteSpacing = (int)GUIStyle.Font.LineHeight
        };
        for (int i = 0; i < activeObjectives.Count; i++)
        {
            AddToObjectiveList(activeObjectives[i]);
        }
        screenSettings = new ScreenSettings(new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight), GUI.Scale, GameSettings.CurrentConfig.Graphics.DisplayMode);
    }

    /// <summary>
    /// Stops content running and adds the active segment to the objective list
    /// </summary>
    private static void StopCurrentContentSegment()
    {
        if (!ActiveContentSegment.ObjectiveText.IsNullOrEmpty())
        {
            activeObjectives.Add(ActiveContentSegment);
            AddToObjectiveList(ActiveContentSegment);
        }
        ContentRunning = false;
        ActiveContentSegment = null;
    }

    /// <summary>
    /// Adds the segment to the objective list
    /// </summary>
    private static void AddToObjectiveList(Segment segment, bool connectExisting = false)
    {
        if (connectExisting)
        {
            if (activeObjectives.Find(o => o.Id == segment.Id) is { } existingSegment)
            {
                existingSegment.ConnectMessageBox(segment);
                SetButtonBehavior(existingSegment);
            }
            return;
        }

        var frameRt = new RectTransform(new Vector2(1.0f, 0.1f), objectiveGroup.RectTransform)
        {
            MinSize = new Point(0, objectiveGroup.AbsoluteSpacing)
        };
        Segment parentSegment = activeObjectives.FirstOrDefault(s => s.Id == segment.ParentId);
        if (parentSegment is not null)
        {
            // Add this child as the last child in case there are other existing children already
            int totalChildren = activeObjectives.Count(s => s.ParentId == segment.ParentId);
            int childIndex = activeObjectives.IndexOf(parentSegment) + totalChildren;
            if (objectiveGroup.RectTransform.GetChildIndex(frameRt) != childIndex)
            {
                frameRt.RepositionChildInHierarchy(childIndex);
                activeObjectives.Remove(segment);
                activeObjectives.Insert(childIndex, segment);
            }
        }
        frameRt.AbsoluteOffset = GetObjectiveHiddenPosition();

        var frame = new GUIFrame(frameRt, style: null)
        {
            CanBeFocused = true
        };

        objectiveGroup.Recalculate();

        int textWidth = parentSegment is null ? frameRt.Rect.Width - objectiveGroup.AbsoluteSpacing
            : frameRt.Rect.Width - 2 * objectiveGroup.AbsoluteSpacing;
        segment.LinkedTextBlock = new GUITextBlock(
            new RectTransform(new Point(textWidth, 0), frame.RectTransform, anchor: Anchor.TopRight),
            TextManager.ParseInputTypes(segment.ObjectiveText),
            wrap: true);

        var size = new Point(segment.LinkedTextBlock.Rect.Width, segment.LinkedTextBlock.Rect.Height);
        segment.LinkedTextBlock.RectTransform.NonScaledSize = size;
        segment.LinkedTextBlock.RectTransform.MinSize = size;
        segment.LinkedTextBlock.RectTransform.MaxSize = size;
        segment.LinkedTextBlock.RectTransform.IsFixedSize = true;
        frame.RectTransform.Resize(new Point(frame.Rect.Width, segment.LinkedTextBlock.RectTransform.Rect.Height), resizeChildren: false);
        frame.RectTransform.IsFixedSize = true;

        var indicatorRt = new RectTransform(new Point(objectiveGroup.AbsoluteSpacing), frame.RectTransform, isFixedSize: true);
        if (parentSegment is not null)
        {
            indicatorRt.AbsoluteOffset = new Point(objectiveGroup.AbsoluteSpacing, 0);
        }
        segment.ObjectiveStateIndicator = new GUIImage(indicatorRt, "ObjectiveIndicatorIncomplete");

        SetTransparent(segment.LinkedTextBlock);

        objectiveTextTranslated ??= TextManager.Get("Tutorial.Objective");
        segment.ObjectiveButton = new GUIButton(new RectTransform(Vector2.One, segment.LinkedTextBlock.RectTransform, Anchor.TopLeft, Pivot.TopLeft), style: null)
        {
            ToolTip = objectiveTextTranslated
        };
        SetButtonBehavior(segment);
        SetTransparent(segment.ObjectiveButton);

        frameRt.MoveOverTime(new Point(0, frameRt.AbsoluteOffset.Y), ObjectiveComponentAnimationTime, onDoneMoving: () => objectiveGroup?.Recalculate());

        // Check if the objective has already been completed in the campaign
        if (!segment.IsCompleted && GameMain.GameSession?.Campaign?.CampaignMetadata is CampaignMetadata data && data.GetBoolean(segment.Id))
        {
            MarkSegmentCompleted(segment, flash: false);
        }

        static void SetTransparent(GUIComponent component) => component.Color = component.HoverColor = component.PressedColor = component.SelectedColor = Color.Transparent;

        void SetButtonBehavior(Segment segment)
        {
            segment.ObjectiveButton.CanBeFocused = segment.SegmentType != TutorialSegmentType.Objective;
            segment.ObjectiveButton.OnClicked = (GUIButton btn, object userdata) =>
            {
                if (segment.SegmentType == TutorialSegmentType.InfoBox)
                {
                    if (segment.AutoPlayVideo == AutoPlayVideo.Yes)
                    {
                        ReplaySegmentVideo(segment);
                    }
                    else
                    {
                        ShowSegmentText(segment);
                    }
                }
                else if (segment.SegmentType == TutorialSegmentType.MessageBox)
                {
                    segment.OnClickObjective?.Invoke();
                }
                return true;
            };
        }
    }

    private static void ReplaySegmentVideo(Segment segment)
    {
        if (ContentRunning) { return; }
        Inventory.DraggingItems.Clear();
        ContentRunning = true;
        LoadVideo(segment);
    }

    private static void ShowSegmentText(Segment segment)
    {
        if (ContentRunning) { return; }
        Inventory.DraggingItems.Clear();
        ContentRunning = true;
        ActiveContentSegment = segment;
        infoBox = CreateInfoFrame(
            TextManager.Get(segment.Id),
            TextManager.Get(segment.TextContent.Tag),
            segment.TextContent.Width,
            segment.TextContent.Height,
            segment.TextContent.Anchor,
            hasButton: true,
            onInfoBoxClosed: () => ContentRunning = false,
            onVideoButtonClicked: () => LoadVideo(segment));
    }

    private static Point GetObjectiveHiddenPosition(RectTransform rt = null)
    {
        return new Point(GameMain.GraphicsWidth - objectiveGroup.Rect.X, rt?.AbsoluteOffset.Y ?? 0);
    }

    public static Segment GetObjective(Identifier identifier)
    {
        return activeObjectives.FirstOrDefault(o => o.Id == identifier);
    }

    public static bool AllActiveObjectivesCompleted()
    {
        return activeObjectives.None() || activeObjectives.All(o => !o.CanBeCompleted || o.IsCompleted);
    }

    public static bool AnyObjectives => activeObjectives.Any();

    #endregion

    #region InfoFrame

    private static void CloseInfoFrame() => CloseInfoFrame(null, null);

    private static bool CloseInfoFrame(GUIButton button, object userData)
    {
        infoBox = null;
        infoBoxClosedCallback?.Invoke();
        return true;
    }

    /// <summary>
    //  Creates and displays a tutorial info box
    /// </summary>
    private static GUIComponent CreateInfoFrame(LocalizedString title, LocalizedString text, int width = 300, int height = 80, Anchor anchor = Anchor.TopRight, bool hasButton = false, Action onInfoBoxClosed = null, Action onVideoButtonClicked = null)
    {
        if (hasButton)
        {
            height += 60;
        }

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
        infoBoxClosedCallback = onInfoBoxClosed;

        if (hasButton)
        {
            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), infoContent.RectTransform), isHorizontal: true)
            {
                RelativeSpacing = 0.1f
            };
            buttonContainer.RectTransform.IsFixedSize = true;

            if (onVideoButtonClicked != null)
            {
                buttonContainer.Stretch = true;
                var videoButton = new GUIButton(new RectTransform(new Vector2(0.4f, 1.0f), buttonContainer.RectTransform),
                    TextManager.Get("Video"), style: "GUIButtonLarge")
                {
                    OnClicked = (GUIButton button, object obj) =>
                    {
                        onVideoButtonClicked();
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

    private static void LoadVideo(Segment segment)
    {
        if (segment.AutoPlayVideo == AutoPlayVideo.Yes)
        {
            VideoPlayer.LoadContent(
                contentPath: segment.VideoContent.ContentPath,
                videoSettings: new VideoPlayer.VideoSettings(segment.VideoContent.FileName),
                textSettings: new VideoPlayer.TextSettings(segment.VideoContent.TextTag, segment.VideoContent.Width),
                contentId: segment.Id,
                startPlayback: true,
                objective: segment.ObjectiveText,
                onStop: StopCurrentContentSegment);
        }
        else
        {
            VideoPlayer.LoadContent(
                contentPath: segment.VideoContent.ContentPath,
                videoSettings: new VideoPlayer.VideoSettings(segment.VideoContent.FileName),
                textSettings: null,
                contentId: segment.Id,
                startPlayback: true,
                objective: string.Empty);
        }
    }

    private static void LoadActiveContentVideo() => LoadVideo(ActiveContentSegment);

    #endregion
}