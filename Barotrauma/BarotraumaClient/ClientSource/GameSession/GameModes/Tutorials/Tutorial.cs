using Barotrauma.Extensions;
using Barotrauma.IO;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Tutorials
{
    enum AutoPlayVideo { Yes, No };

    enum TutorialSegmentType { MessageBox, InfoBox, Objective };

    sealed class Tutorial
    {
        #region Constants

        private const SpawnType SpawnPointType = SpawnType.Human;
        private const float FadeOutTime = 3f;
        private const float WaitBeforeFade = 4f;

        #endregion

        #region Tutorial variables

        public readonly Identifier Identifier;

        public LocalizedString DisplayName { get; }

        public bool ContentRunning { get; private set; }

        private GUIComponent infoBox;
        private Action infoBoxClosedCallback;

        private VideoPlayer videoPlayer;
        private Point screenResolution;
        private WindowMode windowMode;
        private float prevUIScale;

        private GUILayoutGroup objectiveGroup;
        private readonly LocalizedString objectiveTextTranslated;

        private readonly List<Segment> ActiveObjectives = new List<Segment>();
        private const float ObjectiveComponentAnimationTime = 1.5f;
        private Segment ActiveContentSegment { get; set; }

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

        private bool completed;
        public bool Completed
        {
            get
            {
                return completed;
            }
            private set
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

        public readonly TutorialPrefab TutorialPrefab;
        private readonly EventPrefab eventPrefab;

        private CoroutineHandle tutorialCoroutine;
        private CoroutineHandle completedCoroutine;

        private Character character;

        private string SubmarinePath => TutorialPrefab.SubmarinePath.Value;
        private string StartOutpostPath => TutorialPrefab.OutpostPath.Value;
        private string LevelSeed => TutorialPrefab.LevelSeed;
        private string LevelParams => TutorialPrefab.LevelParams;

        private SubmarineInfo startOutpost = null;

        public readonly List<(Entity entity, Identifier iconStyle)> Icons = new List<(Entity entity, Identifier iconStyle)>();

        #endregion

        #region Tutorial Controls

        public Tutorial(TutorialPrefab prefab)
        {
            Identifier = $"tutorial.{prefab.Identifier}".ToIdentifier();
            DisplayName = TextManager.Get(Identifier);
            objectiveTextTranslated = TextManager.Get("Tutorial.Objective");

            TutorialPrefab = prefab;
            eventPrefab = EventSet.GetEventPrefab(prefab.EventIdentifier);
        }

        private IEnumerable<CoroutineStatus> Loading()
        {
            SubmarineInfo subInfo = new SubmarineInfo(SubmarinePath);

            LevelGenerationParams.LevelParams.TryGet(LevelParams, out LevelGenerationParams generationParams);

            yield return CoroutineStatus.Running;

            GameMain.GameSession = new GameSession(subInfo, GameModePreset.Tutorial, missionPrefabs: null);
            (GameMain.GameSession.GameMode as TutorialMode).Tutorial = this;

            if (generationParams is not null)
            {
                Biome biome =
                    Biome.Prefabs.FirstOrDefault(b => generationParams.AllowedBiomeIdentifiers.Contains(b.Identifier)) ??
                    Biome.Prefabs.First();

                if (!string.IsNullOrEmpty(StartOutpostPath))
                {
                    startOutpost = new SubmarineInfo(StartOutpostPath);
                }

                LevelData tutorialLevel = new LevelData(LevelSeed, 0, 0, generationParams, biome);
                GameMain.GameSession.StartRound(tutorialLevel, startOutpost: startOutpost);
            }
            else
            {
                GameMain.GameSession.StartRound(LevelSeed);
            }

            GameMain.GameSession.EventManager.ActiveEvents.Clear();
            GameMain.GameSession.EventManager.Enabled = true;
            GameMain.GameScreen.Select();

            if (Submarine.MainSub != null)
            {
                Submarine.MainSub.GodMode = true;
            }
            foreach (Structure wall in Structure.WallList)
            {
                if (wall.Submarine != null && wall.Submarine.Info.IsOutpost)
                {
                    wall.Indestructible = true;
                }
            }

            var charInfo = TutorialPrefab.GetTutorialCharacterInfo();

            var wayPoint = WayPoint.GetRandom(SpawnPointType, charInfo.Job?.Prefab, Level.Loaded.StartOutpost);

            if (wayPoint == null)
            {
                DebugConsole.ThrowError("A waypoint with the spawntype \"" + SpawnPointType + "\" is required for the tutorial event");
                yield return CoroutineStatus.Failure;
                yield break;
            }

            character = Character.Create(charInfo, wayPoint.WorldPosition, "", isRemotePlayer: false, hasAi: false);
            character.TeamID = CharacterTeamType.Team1;
            Character.Controlled = character;
            character.GiveJobItems(null);

            var idCard = character.Inventory.FindItemByTag("identitycard".ToIdentifier());
            if (idCard == null)
            {
                DebugConsole.ThrowError("Item prefab \"ID Card\" not found!");
                yield return CoroutineStatus.Failure;
                yield break;
            }
            idCard.AddTag("com");
            idCard.AddTag("eng");

            foreach (Item item in Item.ItemList)
            {
                Door door = item.GetComponent<Door>();
                if (door != null)
                {
                    door.CanBeWelded = false;
                }
            }

            tutorialCoroutine = CoroutineManager.StartCoroutine(UpdateState());

            Initialize();

            yield return CoroutineStatus.Success;
        }

        private void Initialize()
        {
            GameMain.GameSession.CrewManager.AllowCharacterSwitch = TutorialPrefab.AllowCharacterSwitch;
            GameMain.GameSession.CrewManager.AutoHideCrewList();

            if (Character.Controlled is Character character)
            {
                foreach (Item item in character.Inventory.AllItemsMod)
                {
                    if (item.HasTag(TutorialPrefab.StartingItemTags)) { continue; }
                    item.Unequip(character);
                    character.Inventory.RemoveItem(item);
                }
            }
        }

        public void Start()
        {
            videoPlayer = new VideoPlayer();
            GameMain.Instance.ShowLoading(Loading());
            ActiveObjectives.Clear();
            ActiveContentSegment = null;

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

        public void AddToGUIUpdateList()
        {
            if (GameMain.GraphicsWidth != screenResolution.X || GameMain.GraphicsHeight != screenResolution.Y || prevUIScale != GUI.Scale || GameSettings.CurrentConfig.Graphics.DisplayMode != windowMode)
            {
                CreateObjectiveFrame();
            }
            if (ActiveObjectives.Count > 0)
            {
                objectiveGroup?.AddToGUIUpdateList(order: -1);
            }
            infoBox?.AddToGUIUpdateList(order: 100);
            videoPlayer?.AddToGUIUpdateList(order: 100);
        }

        public void Update()
        {
            videoPlayer?.Update();

            if (character != null)
            {
                if (character.Oxygen < 1)
                {
                    character.Oxygen = 1;
                }
                if (character.IsDead)
                {
                    CoroutineManager.StartCoroutine(Dead());
                }
                else if (Character.Controlled == null)
                {
                    if (tutorialCoroutine != null)
                    {
                        CoroutineManager.StopCoroutines(tutorialCoroutine);
                    }
                    if (completedCoroutine == null && !CoroutineManager.IsCoroutineRunning(completedCoroutine))
                    {
                        GUI.PreventPauseMenuToggle = false;
                    }
                    ContentRunning = false;
                    infoBox = null;
                }
                else
                {
                    character = Character.Controlled;
                }
            }
        }

        private IEnumerable<CoroutineStatus> Dead()
        {
            GUI.PreventPauseMenuToggle = true;
            Character.Controlled = character = null;
            Stop();

            GameAnalyticsManager.AddDesignEvent("Tutorial:Died");

            yield return new WaitForSeconds(3.0f);

            var messageBox = new GUIMessageBox(TextManager.Get("Tutorial.TryAgainHeader"), TextManager.Get("Tutorial.TryAgain"), new LocalizedString[] { TextManager.Get("Yes"), TextManager.Get("No") });

            messageBox.Buttons[0].OnClicked += Restart;
            messageBox.Buttons[0].OnClicked += messageBox.Close;


            messageBox.Buttons[1].OnClicked = GameMain.MainMenuScreen.ReturnToMainMenu;
            messageBox.Buttons[1].OnClicked += messageBox.Close;

            yield return CoroutineStatus.Success;
        }

        public void CloseActiveContentGUI()
        {
            if (videoPlayer.IsPlaying)
            {
                videoPlayer.Stop();
            }
            else if (infoBox != null)
            {
                CloseInfoFrame();
            }
        }

        public IEnumerable<CoroutineStatus> UpdateState()
        {
            while (GameMain.Instance.LoadingScreenOpen || Level.Loaded == null || Level.Loaded.Generating)
            {
                yield return new WaitForSeconds(0.1f);
            }

            if (eventPrefab == null)
            {
                DebugConsole.LogError($"No tutorial event defined for the tutorial (identifier: \"{TutorialPrefab?.Identifier.ToString() ?? "null"})\"");
                yield return CoroutineStatus.Failure;
            }

            if (eventPrefab.CreateInstance() is Event eventInstance)
            {
                GameMain.GameSession.EventManager.QueuedEvents.Enqueue(eventInstance);
                while (!eventInstance.IsFinished)
                {
                    yield return CoroutineStatus.Running;
                }
            }
            else
            {
                DebugConsole.LogError($"Failed to create an instance for a tutorial event (identifier: \"{eventPrefab.Identifier}\"");
                yield return CoroutineStatus.Failure;
            }

            yield return CoroutineStatus.Success;
        }

        public void Complete()
        {
            GameAnalyticsManager.AddDesignEvent($"Tutorial:{Identifier}:Completed");
            completedCoroutine = CoroutineManager.StartCoroutine(TutorialCompleted());

            IEnumerable<CoroutineStatus> TutorialCompleted()
            {
                while (GUI.PauseMenuOpen) { yield return CoroutineStatus.Running; }

                GUI.PreventPauseMenuToggle = true;
                Character.Controlled.ClearInputs();
                Character.Controlled = null;
                GameAnalyticsManager.AddDesignEvent("Tutorial:Completed");

                yield return new WaitForSeconds(WaitBeforeFade);

                var endCinematic = new CameraTransition(Submarine.MainSub, GameMain.GameScreen.Cam, null, Alignment.Center, panDuration: FadeOutTime);
                Completed = true;

                while (endCinematic.Running) { yield return CoroutineStatus.Running; }

                Stop();
                GameMain.MainMenuScreen.ReturnToMainMenu(null, null);
            }
        }

        private bool Restart(GUIButton button, object obj)
        {
            GUIMessageBox.MessageBoxes.Clear();
            GameMain.MainMenuScreen.ReturnToMainMenu(button, obj);
            Start();
            return true;
        }

        public void TriggerTutorialSegment(Segment segment, bool connectObjective = false)
        {
            if (segment.SegmentType != TutorialSegmentType.InfoBox)
            {
                ActiveObjectives.Add(segment);
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

        public void CompleteTutorialSegment(Identifier segmentId)
        {
            if (GetActiveObjective(segmentId) is not Segment segment)
            {
                DebugConsole.AddWarning($"Warning: tried to complete the tutorial segment \"{segmentId}\" in tutorial \"{Identifier}\" but it isn't active!");
                return;
            }
            if (GUIStyle.GetComponentStyle("ObjectiveIndicatorCompleted") is GUIComponentStyle style)
            {
                //return if already completed
                if (segment.ObjectiveStateIndicator.Style == style) { return; }
                segment.ObjectiveStateIndicator.ApplyStyle(style);
            }
            segment.ObjectiveStateIndicator.Parent.Flash(color: GUIStyle.Green, flashDuration: 0.35f, useRectangleFlash: true);
            segment.ObjectiveButton.OnClicked = null;
            segment.ObjectiveButton.CanBeFocused = false;
            GameAnalyticsManager.AddDesignEvent($"Tutorial:{Identifier}:{segmentId}:Completed");
        }

        public void RemoveTutorialSegment(Identifier segmentId)
        {
            if (GetActiveObjective(segmentId) is not Segment segment)
            {
                DebugConsole.AddWarning($"Warning: tried to remove the tutorial segment \"{segmentId}\" in tutorial \"{Identifier}\" but it isn't active!");
                return;
            }
            segment.ObjectiveStateIndicator.FadeOut(ObjectiveComponentAnimationTime, false);
            segment.LinkedTextBlock.FadeOut(ObjectiveComponentAnimationTime, false);
            var parent = segment.LinkedTextBlock.Parent;
            parent.FadeOut(ObjectiveComponentAnimationTime, true, onRemove: () =>
            {
                ActiveObjectives.Remove(segment);
                objectiveGroup?.Recalculate();
            });
            parent.RectTransform.MoveOverTime(GetObjectiveHiddenPosition(parent.RectTransform), ObjectiveComponentAnimationTime);
            segment.ObjectiveButton.OnClicked = null;
            segment.ObjectiveButton.CanBeFocused = false;
        }

        private Segment GetActiveObjective(Identifier id) => ActiveObjectives.FirstOrDefault(s => s.Id == id);

        public void Stop()
        {
            if (tutorialCoroutine != null)
            {
                CoroutineManager.StopCoroutines(tutorialCoroutine);
            }
            ContentRunning = false;
            infoBox = null;
            videoPlayer?.Remove();
        }

        #endregion

        #region Objectives

        /// <summary>
        /// Create the objective list that holds the objectives (called on start and on resolution change)
        /// </summary>
        private void CreateObjectiveFrame()
        {
            var objectiveListFrame = new GUIFrame(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.TutorialObjectiveListArea, GUI.Canvas), style: null);
            objectiveGroup = new GUILayoutGroup(new RectTransform(Vector2.One, objectiveListFrame.RectTransform))
            {
                AbsoluteSpacing = (int)GUIStyle.Font.LineHeight
            };
            for (int i = 0; i < ActiveObjectives.Count; i++)
            {
                AddToObjectiveList(ActiveObjectives[i]);
            }
            screenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            windowMode = GameSettings.CurrentConfig.Graphics.DisplayMode;
            prevUIScale = GUI.Scale;
        }

        /// <summary>
        /// Stops content running and adds the active segment to the objective list
        /// </summary>
        private void StopCurrentContentSegment()
        {
            if (!ActiveContentSegment.ObjectiveText.IsNullOrEmpty())
            {
                ActiveObjectives.Add(ActiveContentSegment);
                AddToObjectiveList(ActiveContentSegment);
            }
            ContentRunning = false;
            ActiveContentSegment = null;
        }

        /// <summary>
        /// Adds the segment to the objective list
        /// </summary>
        private void AddToObjectiveList(Segment segment, bool connectExisting = false)
        {
            if (connectExisting)
            {
                if (ActiveObjectives.Find(o => o.Id == segment.Id) is { } existingSegment)
                {
                    existingSegment.ConnectMessageBox(segment);
                    SetButtonBehavior(existingSegment);   
                }
                return;
            }

            var frameRt = new RectTransform(new Vector2(1.0f, 0.1f), objectiveGroup.RectTransform)
            {
                AbsoluteOffset = GetObjectiveHiddenPosition(),
                MinSize = new Point(0, objectiveGroup.AbsoluteSpacing)
            };
            var frame = new GUIFrame(frameRt, style: null)
            {
                CanBeFocused = true
            };
            objectiveGroup.Recalculate();

            segment.LinkedTextBlock = new GUITextBlock(
                new RectTransform(new Point(frameRt.Rect.Width - objectiveGroup.AbsoluteSpacing, 0), frame.RectTransform, anchor: Anchor.TopRight),
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
            segment.ObjectiveStateIndicator = new GUIImage(indicatorRt, "ObjectiveIndicatorIncomplete");

            SetTransparent(segment.LinkedTextBlock);

            segment.ObjectiveButton = new GUIButton(new RectTransform(Vector2.One, segment.LinkedTextBlock.RectTransform, Anchor.TopLeft, Pivot.TopLeft), style: null)
            {
                ToolTip = objectiveTextTranslated
            };
            SetButtonBehavior(segment);
            SetTransparent(segment.ObjectiveButton);

            frameRt.MoveOverTime(new Point(0, frameRt.AbsoluteOffset.Y), ObjectiveComponentAnimationTime, onDoneMoving: () => objectiveGroup?.Recalculate());

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

        private void ReplaySegmentVideo(Segment segment)
        {
            if (ContentRunning) { return; }
            Inventory.DraggingItems.Clear();
            ContentRunning = true;
            LoadVideo(segment);
        }

        private void ShowSegmentText(Segment segment)
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

        private Point GetObjectiveHiddenPosition(RectTransform rt = null)
        {
            return new Point(GameMain.GraphicsWidth - objectiveGroup.Rect.X, rt?.AbsoluteOffset.Y ?? 0);
        }

        #endregion

        #region InfoFrame

        private void CloseInfoFrame() => CloseInfoFrame(null, null);

        private bool CloseInfoFrame(GUIButton button, object userData)
        {
            infoBox = null;
            infoBoxClosedCallback?.Invoke();
            return true;
        }

        /// <summary>
        //  Creates and displays a tutorial info box
        /// </summary>
        private GUIComponent CreateInfoFrame(LocalizedString title, LocalizedString text, int width = 300, int height = 80, Anchor anchor = Anchor.TopRight, bool hasButton = false, Action onInfoBoxClosed = null, Action onVideoButtonClicked = null)
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

        private void LoadVideo(Segment segment)
        {
            videoPlayer ??= new VideoPlayer();
            if (segment.AutoPlayVideo == AutoPlayVideo.Yes)
            {
                videoPlayer.LoadContent(
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
                videoPlayer.LoadContent(
                    contentPath: segment.VideoContent.ContentPath,
                    videoSettings: new VideoPlayer.VideoSettings(segment.VideoContent.FileName),
                    textSettings: null,
                    contentId: segment.Id,
                    startPlayback: true,
                    objective: string.Empty);
            }
        }

        private void LoadActiveContentVideo() => LoadVideo(ActiveContentSegment);

        #endregion
    }
}
