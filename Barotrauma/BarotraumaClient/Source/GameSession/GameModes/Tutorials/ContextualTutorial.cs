using System.Collections.Generic;
using System.Xml.Linq;
using System;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework;
using Barotrauma.Items.Components;
using System.Linq;

namespace Barotrauma.Tutorials
{
    class ContextualTutorial : Tutorial
    {
        public static bool Selected = false;
        public static bool ContentRunning = false;
        public static bool Initialized = false;

        private enum ContentTypes { None = 0, Video = 1, Text = 2 };

        private TutorialSegment activeSegment;
        private List<TutorialSegment> segments;

        private SpriteSheetPlayer spriteSheetPlayer;
        private Steering navConsole;
        private Reactor reactor;
        private Sonar sonar;
        private Vector2 subStartingPosition;
        private List<Character> crew;
        private List<Pair<Character, float>> characterTimeOnSonar;
        private float requiredTimeOnSonar = 5f;

        private bool started = false;

        private string playableContentPath;

        private float inputGracePeriodTimer;
        private const float inputGracePeriod = 1f;
        private float tutorialTimer;
        private float degrading2ActivationCountdown;

        private class TutorialSegment
        {
            public string Name;
            public ContentTypes ContentType;
            public XElement Content;
            public bool IsTriggered;

            public TutorialSegment(XElement config)
            {
                Name = config.GetAttributeString("name", "Missing Name");
                Enum.TryParse(config.GetAttributeString("contenttype", "None"), true, out ContentType);
                IsTriggered = config.GetAttributeBool("istriggered", false);

                switch (ContentType)
                {
                    case ContentTypes.None:
                        break;
                    case ContentTypes.Video:
                        Content = config.Element("Video");
                        break;
                    case ContentTypes.Text:
                        Content = config.Element("Text");
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
        }

        public void LoadPartiallyComplete(XElement element)
        {
            int[] completedSegments = element.GetAttributeIntArray("completedsegments", null);

            if (completedSegments == null || completedSegments.Length == 0)
            {
                return;
            }

            for (int i = 0; i < completedSegments.Length; i++)
            {
                segments[completedSegments[i]].IsTriggered = true;
            }
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

        public override void Initialize()
        {
            if (Initialized) return;

            Initialized = true;
            base.Initialize();
            spriteSheetPlayer = new SpriteSheetPlayer();
            characterTimeOnSonar = new List<Pair<Character, float>>();

            for (int i = 0; i < segments.Count; i++)
            {
                segments[i].IsTriggered = false;
            }
        }

        public override void Start()
        {
            if (!Initialized) return;

            base.Start();

            activeSegment = null;
            tutorialTimer = 0.0f;
            inputGracePeriodTimer = 0.0f;
            degrading2ActivationCountdown = -1;
            subStartingPosition = Vector2.Zero;
            characterTimeOnSonar.Clear();

            subStartingPosition = Submarine.MainSub.WorldPosition;
            navConsole = Item.ItemList.Find(i => i.HasTag("command"))?.GetComponent<Steering>();
            sonar = navConsole?.Item.GetComponent<Sonar>();
            reactor = Item.ItemList.Find(i => i.HasTag("reactor"))?.GetComponent<Reactor>();

            if (reactor == null || navConsole == null || sonar == null)
            {
                infoBox = CreateInfoFrame("Submarine not compatible with the tutorial:"
                    + "\nReactor - " + (reactor != null ? "OK" : "Tag 'reactor' not found")
                    + "\nNavigation Console - " + (navConsole != null ? "OK" : "Tag 'command' not found")
                    + "\nSonar - " + (sonar != null ? "OK" : "Not found under Navigation Console"), true);
                CoroutineManager.StartCoroutine(WaitForErrorClosed());
                return;
            }

            crew = GameMain.GameSession.CrewManager.GetCharacters();
            started = true;
        }

        private IEnumerable<object> WaitForErrorClosed()
        {
            while (infoBox != null) yield return null;
            Stop();
        }

        public void Stop()
        {
            started = ContentRunning = Initialized = false;
            spriteSheetPlayer = null;
            characterTimeOnSonar = null;
        }

        public override void AddToGUIUpdateList()
        {
            base.AddToGUIUpdateList();
            if (spriteSheetPlayer != null)
            {
                spriteSheetPlayer.AddToGUIUpdateList();
            }
        }

        public override void Update(float deltaTime)
        {
            if (!started) return;
            deltaTime *= 0.5f;

            if (ContentRunning) // Content is running, wait until dismissed
            {
                if (inputGracePeriodTimer < inputGracePeriod)
                {
                    inputGracePeriodTimer += deltaTime;
                }
                else if (Keyboard.GetState().GetPressedKeys().Length > 0 || Mouse.GetState().LeftButton == ButtonState.Pressed || Mouse.GetState().RightButton == ButtonState.Pressed)
                {
                    switch (activeSegment.ContentType)
                    {
                        case ContentTypes.None:
                            break;
                        case ContentTypes.Video:
                            spriteSheetPlayer.Stop();
                            break;
                        case ContentTypes.Text:
                            infoBox = null;
                            break;
                    }

                    activeSegment = null;
                    ContentRunning = false;
                    inputGracePeriodTimer = 0.0f;
                }
                return;
            }

            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].IsTriggered) continue;
                if (CheckContextualTutorials(i, deltaTime)) // Found a relevant tutorial, halt finding new ones
                {
                    break;
                }
            }
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
                    if (Character.Controlled.SelectedConstruction != navConsole.Item)
                    {
                        if (tutorialTimer < 30.5f)
                        {
                            tutorialTimer += deltaTime;
                            return false;
                        }
                    }
                    else
                    {
                        tutorialTimer = 30.5f;
                    }
                    break;
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
                    if (Character.Controlled.SelectedConstruction != reactor.Item)
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
                            break;
                        }
                    }

                    if (!injuredFound) return false;
                    break;
                case 10: // Approach1: Destination is within ~100m [Video]
                    if (Vector2.Distance(Submarine.MainSub.WorldPosition, Level.Loaded.EndPosition) > 7500f)
                    {
                        return false;
                    }
                    break;
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

            switch (activeSegment.ContentType)
            {
                case ContentTypes.None:
                    break;
                case ContentTypes.Video:
                    spriteSheetPlayer.SetContent(playableContentPath, activeSegment.Content, true);
                    break;
                case ContentTypes.Text:
                    infoBox = CreateInfoFrame(TextManager.Get(activeSegment.Content.GetAttributeString("tag", ""), false, args),
                                              activeSegment.Content.GetAttributeInt("width", 300),
                                              activeSegment.Content.GetAttributeInt("height", 80),
                                              activeSegment.Content.GetAttributeString("anchor", "Center"), false);
                    break;
            }

            for (int i = 0; i < segments.Count; i++)
            {
                if (!segments[i].IsTriggered) return;
            }

            Completed = true;
        }
    }
}
