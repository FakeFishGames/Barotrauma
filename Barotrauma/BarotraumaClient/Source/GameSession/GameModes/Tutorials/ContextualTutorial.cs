using System.Collections.Generic;
using System.Xml.Linq;
using System;
using Microsoft.Xna.Framework;
using Barotrauma.Items.Components;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Tutorials
{
    class ContextualTutorial : Tutorial
    {
        public static bool Selected = false;
        public static bool ContentRunning = false;
        public static bool Initialized = false;

        private enum ContentTypes { None = 0, Video = 1, TextOnly = 2 };

        private TutorialSegment activeSegment;
        private List<TutorialSegment> segments;
        
        private VideoPlayer videoPlayer;

        private Steering navConsole;
        private Reactor reactor;
        private Sonar sonar;
        private Vector2 subStartingPosition;
        private List<Character> crew;
        private List<Pair<Character, float>> characterTimeOnSonar;
        private float requiredTimeOnSonar = 5f;

        private bool started = false;
        private string playableContentPath;

        private float tutorialTimer;
        private float degrading2ActivationCountdown;

        private bool disableTutorialOnDeficiencyFound = true;

        private GUIFrame holderFrame, objectiveFrame;
        private bool objectivesOpen = false;
        private GUITextBlock objectiveTitle, objectiveText;
        private List<TutorialSegment> activeObjectives = new List<TutorialSegment>();

        private class TutorialSegment
        {
            public string Id;
            public string Objective;
            public ContentTypes ContentType;
            public XElement TextContent;
            public XElement VideoContent;
            public bool IsTriggered;

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
                        VideoContent = config.Element("Video");
                        TextContent = config.Element("Text");
                        break;
                    case ContentTypes.TextOnly:
                        TextContent = config.Element("Text");
                        break;
                }
            }
        }

        public ContextualTutorial(XElement element) : base(element)
        {
            playableContentPath = element.GetAttributeString("playablecontentpath", "");
            segments = new List<TutorialSegment>();

            foreach (var segment in element.Elements("Segment"))
            {
                segments.Add(new TutorialSegment(segment));
            }

            Name = "ContextualTutorial";
        }

        public override void Initialize()
        {
            if (Initialized) return;
            Initialized = true;

            base.Initialize();
            videoPlayer = new VideoPlayer();
            characterTimeOnSonar = new List<Pair<Character, float>>();

            for (int i = 0; i < segments.Count; i++)
            {
                segments[i].IsTriggered = false;
            }
        }

        public void LoadPartiallyComplete(XElement element)
        {
            int[] completedSegments = element.GetAttributeIntArray("completedsegments", null);

            if (completedSegments == null || completedSegments.Length == 0)
            {
                return;
            }

            if (completedSegments.Length == segments.Count) // Completed all segments
            {
                Stop();
                return;
            }

            for (int i = 0; i < completedSegments.Length; i++)
            {
                segments[completedSegments[i]].IsTriggered = true;
            }
        }

        private void PreloadVideoContent() // Have to see if needed with videos
        {
            /*for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].ContentType != ContentTypes.Video || segments[i].IsTriggered) continue;
                spriteSheetPlayer.PreloadContent(playableContentPath, "tutorial", segments[i].Id, segments[i].VideoContent);
            }*/
        }

        public void SavePartiallyComplete(XElement element)
        {
            XElement tutorialElement = new XElement("contextualtutorial");
            tutorialElement.Add(new XAttribute("completedsegments", GetCompletedSegments()));
            element.Add(tutorialElement);
        }

        private string GetCompletedSegments()
        {
            string completedSegments = string.Empty;

            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].IsTriggered)
                {
                    completedSegments += i + ",";
                }
            }

            if (completedSegments.Length > 0)
            {
                completedSegments = completedSegments.TrimEnd(',');
            }

            return completedSegments;
        }

        public override void Start()
        {
            if (!Initialized) return;

            PreloadVideoContent();

            base.Start();

            activeSegment = null;
            tutorialTimer = 0.0f;
            degrading2ActivationCountdown = -1;
            subStartingPosition = Vector2.Zero;
            characterTimeOnSonar.Clear();

            subStartingPosition = Submarine.MainSub.WorldPosition;
            navConsole = Item.ItemList.Find(i => i.HasTag("command"))?.GetComponent<Steering>();
            sonar = navConsole?.Item.GetComponent<Sonar>();
            reactor = Item.ItemList.Find(i => i.HasTag("reactor"))?.GetComponent<Reactor>();

#if DEBUG
            if (reactor == null || navConsole == null || sonar == null)
            {
                infoBox = CreateInfoFrame("Submarine not compatible with the tutorial:"
                    + "\nReactor - " + (reactor != null ? "OK" : "Tag 'reactor' not found")
                    + "\nNavigation Console - " + (navConsole != null ? "OK" : "Tag 'command' not found")
                    + "\nSonar - " + (sonar != null ? "OK" : "Not found under Navigation Console"), true);
                CoroutineManager.StartCoroutine(WaitForErrorClosed());
                return;
            }
#endif
            if (disableTutorialOnDeficiencyFound)
            {
                if (reactor == null || navConsole == null || sonar == null)
                {
                    Stop();
                    return;
                }
            }
            else
            {
                if (navConsole == null) segments[2].IsTriggered = true; // Disable navigation console usage tutorial
                if (reactor == null) segments[5].IsTriggered = true; // Disable reactor usage tutorial
                if (sonar == null) segments[6].IsTriggered = true; // Disable enemy on sonar tutorial
            }

            crew = GameMain.GameSession.CrewManager.GetCharacters().ToList();
            Completed = true; // Trigger completed at start to prevent the contextual tutorial from automatically activating on starting new campaigns after this one
            started = true;
        }

#if DEBUG
        private IEnumerable<object> WaitForErrorClosed()
        {
            while (infoBox != null) yield return null;
            Stop();
        }
#endif

        public void Stop()
        {
            started = ContentRunning = Initialized = false;
            videoPlayer.Remove();
            videoPlayer = null;
            characterTimeOnSonar = null;
        }

        public override void AddToGUIUpdateList()
        {
            base.AddToGUIUpdateList();
            if (videoPlayer != null)
            {
                videoPlayer.AddToGUIUpdateList();
            }
            if (objectiveFrame != null && activeObjectives.Count > 0)
            {
                objectiveFrame.AddToGUIUpdateList();
            }
        }

        public override void Update(float deltaTime)
        {
            if (!started || ContentRunning) return;

            deltaTime *= 0.5f;
                       
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].IsTriggered) continue;
                if (CheckContextualTutorials(i, deltaTime)) // Found a relevant tutorial, halt finding new ones
                {
                    break;
                }
            }

            for (int i = 0; i < activeObjectives.Count; i++)
            {
                CheckActiveObjectives(activeObjectives[i]);
            }
        }

        private void CurrentSegmentStopCallback()
        {
            if (!string.IsNullOrEmpty(activeSegment.Objective))
            {
                if (objectiveFrame == null)
                {
                    CreateObjectiveFrame();
                }

                objectiveText.Text = activeSegment.Objective;
            }

            activeSegment = null;
            ContentRunning = false;
        }

        private bool CheckContextualTutorials(int index, float deltaTime)
        {
            switch (index)
            {
                case 0: // Welcome: Game Start [Text]
                    if (tutorialTimer < 0.5f)
                    {
                        tutorialTimer += deltaTime;
                        return false;
                    }
                    break;
                case 1: // Command Reactor: 10 seconds after 'Welcome' dismissed and only if no command given to start reactor [Video]
                    if (tutorialTimer < 10.5f)
                    {
                        tutorialTimer += deltaTime;

                        if (HasOrder("operatereactor"))
                        {
                            segments[index].IsTriggered = true;
                            tutorialTimer = 10.5f;
                        }
                        return false;
                    }
                    break;
                case 2: // Nav Console: 20 seconds after 'Command Reactor' dismissed or if nav console is activated [Video]
                    if (Character.Controlled?.SelectedConstruction != navConsole.Item)
                    {
                        if (!segments[1].IsTriggered) return false; // Do not advance tutorial timer based on this segment if reactor has not been powered up
                        if (tutorialTimer < 30.5f)
                        {
                            tutorialTimer += deltaTime;
                            return false;
                        }
                    }
                    else
                    {
                        if (!segments[1].IsTriggered || !HasOrder("operatereactor")) // If reactor has not been powered up or ordered to be, default to that one first
                        {
                            if (tutorialTimer < 10.5f)
                            {
                                tutorialTimer = 10.5f;
                            }
                            return false;
                        }

                        tutorialTimer = 30.5f;
                    }

                    TriggerTutorialSegment(index, GameMain.GameSession.EndLocation.Name);
                    return true;
                case 3: // Objective: Travel ~150 meters and while sub is not flooding [Text]
                    if (Vector2.Distance(subStartingPosition, Submarine.MainSub.WorldPosition) < 12000f || IsFlooding())
                    {
                        return false;
                    }
                    else // Called earlier than others due to requiring specific args
                    {
                        TriggerTutorialSegment(index, GameMain.GameSession.EndLocation.Name);
                        return true;
                    }
                case 4: // Flood: Hull is breached and sub is taking on water [Video]
                    if (!IsFlooding())
                    {
                        return false;
                    }
                    break;
                case 5: // Reactor: Player uses reactor for the first time [Video]
                    if (Character.Controlled?.SelectedConstruction != reactor.Item)
                    {
                        return false;
                    }
                    break;
                case 6: // Enemy on Sonar:  Player witnesses creature signal on sonar for 5 seconds [Video]
                    if (!HasEnemyOnSonarForDuration(deltaTime))
                    {
                        return false;
                    }
                    break;
                case 7: // Degrading1: Any equipment degrades to 50% health or less and player has not assigned any crew to perform maintenance [Text]
                    bool degradedEquipmentFound = false;

                    foreach (Item item in Item.ItemList)
                    {
                        if (!item.Repairables.Any() || item.Condition > 50.0f) continue;
                        degradedEquipmentFound = true;
                        break;
                    }

                    if (degradedEquipmentFound)
                    {
                        degrading2ActivationCountdown = 5f;
                        if (HasOrder("repairsystems"))
                        {
                            segments[index].IsTriggered = true;
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                    break;
                case 8: // Degrading2: 5 seconds after 'Degrading1' dismissed, and only if player has not assigned any crew to perform maintenance [Video]
                    if (degrading2ActivationCountdown == -1f)
                    {
                        return false;
                    }
                    else if (degrading2ActivationCountdown > 0.0f)
                    {
                        degrading2ActivationCountdown -= deltaTime;
                        if (HasOrder("repairsystems"))
                        {
                            segments[index].IsTriggered = true;
                        }

                        return false;
                    }
                    break;
                case 9: // Medical: Crewmember is injured but not killed [Video]
                    bool injuredFound = false;
                    for (int i = 0; i < crew.Count; i++)
                    {
                        Character member = crew[i];
                        if (member.Vitality < member.MaxVitality && !member.IsDead)
                        {
                            injuredFound = true;
                            TriggerTutorialSegment(index, new string[] { member.DisplayName,
                                (member.Info.Gender == Gender.Male) ? TextManager.Get("PronounPossessiveMale").ToLower() : TextManager.Get("PronounPossessiveFemale").ToLower() });
                            return true;
                        }
                    }

                    if (!injuredFound) return false;
                    break;
                case 10: // Approach1: Destination is within ~100m [Video]
                    if (Vector2.Distance(Submarine.MainSub.WorldPosition, Level.Loaded.EndPosition) > 8000f)
                    {
                        return false;
                    }
                    else
                    {
                        TriggerTutorialSegment(index, GameMain.GameSession.EndLocation.Name);
                        return true;
                    }
                case 11: // Approach2: Sub is docked [Text]
                    if (!Submarine.MainSub.AtEndPosition || Submarine.MainSub.DockedTo.Count == 0)
                    {
                        return false;
                    }
                    break;
            }

            TriggerTutorialSegment(index);
            return true;
        }

        private void CheckActiveObjectives(TutorialSegment objective)
        {
            switch(objective.Id)
            {
                case "ReactorCommand": // Reactor up and running

                    break;
                case "NavConsole": // traveled 100 meters

                    break;
                case "Flood": // Hull breaches repaired
                    break;
                case "EnemyOnSonar":
                    break;
                case "Degrading2":
                    break;
                case "Approach1":
                    break;
            }

            activeObjectives.Remove(objective);
        }

        private bool HasOrder(string aiTag)
        {
            for (int i = 0; i < crew.Count; i++)
            {
                if (crew[i].CurrentOrder?.AITag == aiTag) return true;
            }

            return false;
        }

        private bool IsFlooding()
        {
            foreach (Gap gap in Gap.GapList)
            {
                if (gap.ConnectedWall == null) continue;
                if (gap.ConnectedDoor != null || gap.Open <= 0.0f) continue;
                if (gap.Submarine == null) continue;
                if (gap.Submarine != Submarine.MainSub) continue;
                return true;
            }

            return false;
        }

        private bool HasEnemyOnSonarForDuration(float deltaTime)
        {
            foreach (Character c in Character.CharacterList)
            {
                if (c.AnimController.CurrentHull != null || !c.Enabled || !(c.AIController is EnemyAIController)) continue;
                if (sonar.DetectSubmarineWalls && c.AnimController.CurrentHull == null && sonar.Item.CurrentHull != null) continue;
                if (Vector2.DistanceSquared(c.WorldPosition, sonar.Item.WorldPosition) > sonar.Range * sonar.Range)
                {
                    for (int i = 0; i < characterTimeOnSonar.Count; i++)
                    {
                        if (characterTimeOnSonar[i].First == c)
                        {
                            characterTimeOnSonar.RemoveAt(i);
                            break;
                        }
                    }

                    continue;
                }

                Pair<Character, float> pair = characterTimeOnSonar.Find(ct => ct.First == c);
                if (pair != null)
                {
                    pair.Second += deltaTime;
                }
                else
                {
                    characterTimeOnSonar.Add(new Pair<Character, float>(c, deltaTime));
                }
            }

            return characterTimeOnSonar.Find(ct => ct.Second >= requiredTimeOnSonar) != null;
        }

        private void TriggerTutorialSegment(int index, params object[] args)
        {
            ContentRunning = true;
            activeSegment = segments[index];
            activeSegment.IsTriggered = true;

            string tutorialText = TextManager.GetFormatted(activeSegment.TextContent.GetAttributeString("tag", ""), true, args);
            string objectiveText = string.Empty;

            if (!string.IsNullOrEmpty(activeSegment.Objective))
            {
                if (args.Length == 0)
                {
                    objectiveText = activeSegment.Objective;
                }
                else
                {
                    objectiveText = string.Format(activeSegment.Objective, args);
                }

                activeObjectives.Add(activeSegment);
            }

            switch (activeSegment.ContentType)
            {
                case ContentTypes.None:
                    break;
                case ContentTypes.Video:
                    string fileName = "1_CommandReactor/command_reactor_video.mp4";
                    videoPlayer.LoadContentWithObjective(playableContentPath + fileName, new VideoPlayer.VideoSettings(activeSegment.VideoContent), new VideoPlayer.TextSettings(activeSegment.TextContent, args), activeSegment.Id, true, objectiveText, CurrentSegmentStopCallback);
                    break;
                case ContentTypes.TextOnly:
                    infoBox = CreateInfoFrame(TextManager.Get(activeSegment.Id), tutorialText,
                                              activeSegment.TextContent.GetAttributeInt("width", 300),
                                              activeSegment.TextContent.GetAttributeInt("height", 80),
                                              activeSegment.TextContent.GetAttributeString("anchor", "Center"), true, CurrentSegmentStopCallback);
                    break;
            }

            for (int i = 0; i < segments.Count; i++)
            {
                if (!segments[i].IsTriggered) return;
            }

            CoroutineManager.StartCoroutine(WaitToStop()); // Completed
        }

        private void CreateObjectiveFrame()
        {
            holderFrame = new GUIFrame(new RectTransform(new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight), GUI.Canvas, Anchor.Center));
            objectiveFrame = new GUIFrame(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.ObjectiveArea, holderFrame.RectTransform), style: null);
            objectiveTitle = new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.3f), objectiveFrame.RectTransform, Anchor.TopCenter, Pivot.TopCenter), TextManager.Get("Objective"), textColor: Color.White, font: GUI.ObjectiveTitleFont, textAlignment: Alignment.BottomRight);
            objectiveText = new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.7f), objectiveFrame.RectTransform, Anchor.BottomCenter, Pivot.BottomCenter), "Repair Hull Breach", textColor: new Color(4, 180, 108), font: GUI.ObjectiveNameFont, textAlignment: Alignment.TopRight);

            int toggleButtonWidth = (int)(30 * GUI.Scale);
            var toggleButton = new GUIButton(new RectTransform(new Point(toggleButtonWidth, HUDLayoutSettings.ObjectiveArea.Height), objectiveFrame.RectTransform, Anchor.CenterRight) { AbsoluteOffset = new Point(0, -5) }, style: "UIToggleButton");
            toggleButton.OnClicked += (GUIButton btn, object userdata) =>
            {
                objectivesOpen = !objectivesOpen;
                foreach (GUIComponent child in btn.Children)
                {
                    child.SpriteEffects = objectivesOpen ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                }
                return true;
            };
        }

        private IEnumerable<object> WaitToStop()
        {
            while (ContentRunning) yield return null;
            Stop();
        }
    }
}
