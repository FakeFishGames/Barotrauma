using System.Collections.Generic;
using System.Xml.Linq;
using System;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework;
using Barotrauma.Items.Components;

namespace Barotrauma.Tutorials
{
    class ContextualTutorial : Tutorial
    {
        public static bool ContentRunning = false;
        public SinglePlayerCampaign Campaign;

        private enum ContentTypes { None = 0, Video = 1, Text = 2 };

        private TutorialSegment activeSegment;
        private List<TutorialSegment> segments;
        private SpriteSheetPlayer spriteSheetPlayer;
        private Steering navConsole;
        private Reactor reactor;
        private Vector2 subStartingPosition;
        private List<Character> crew;

        private bool active = false;

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
            spriteSheetPlayer = new SpriteSheetPlayer();
            segments = new List<TutorialSegment>();

            foreach (var segment in element.Elements("Segment"))
            {
                segments.Add(new TutorialSegment(segment));
            }
        }

        public override void Initialize()
        {
            base.Initialize();

            activeSegment = null;
            tutorialTimer = 0.0f;
            inputGracePeriodTimer = 0.0f;
            degrading2ActivationCountdown = -1;
            subStartingPosition = Vector2.Zero;
        }

        public override void Start()
        {
            base.Start();
            subStartingPosition = Submarine.MainSub.WorldPosition;
            navConsole = Item.ItemList.Find(i => i.HasTag("steering")).GetComponent<Steering>();
            reactor = Item.ItemList.Find(i => i.HasTag("reactor")).GetComponent<Reactor>();
            crew = GameMain.GameSession.CrewManager.GetCharacters();
            active = true;
        }

        public void Stop()
        {
            active = ContentRunning = false;
        }

        public override void AddToGUIUpdateList()
        {
            base.AddToGUIUpdateList();
            spriteSheetPlayer.AddToGUIUpdateList();
        }

        public override void Update(float deltaTime)
        {
            if (!active) return;

            if (ContentRunning)
            {
                if (inputGracePeriodTimer < inputGracePeriod)
                {
                    inputGracePeriodTimer += deltaTime;
                }
                else if (Keyboard.GetState().GetPressedKeys().Length > 0)
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
                case 0: // Welcome:         Game Start [Text]
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
                case 2: // Nav Console:     20 seconds after 'Command Reactor' dismissed or if nav console is activated [Video]
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
                case 3: // Objective:       Travel 200 meters and while sub is not flooding [Text]
                    if (Vector2.Distance(subStartingPosition, Submarine.MainSub.WorldPosition) < 200000f || IsFlooding())
                    {
                        return false;
                    }
                    break;
                case 4: // Flood:           Hull is breached and sub is taking on water [Video]
                    if (!IsFlooding())
                    {
                        return false;
                    }
                    break;
                case 5: // Reactor:         Player uses reactor for the first time [Video]
                    if (Character.Controlled.SelectedConstruction != reactor.Item)
                    {
                        return false;
                    }
                    break;
                case 6: // Enemy on Sonar:  Player witnesses creature signal on sonar for 5 seconds [Video]
                    return false;
                    break;
                case 7: // Degrading1:      Any equipment degrades to 50% health or less and player has not assigned any crew to perform maintenance [Text]
                    bool degradedEquipmentFound = false;

                    foreach (Item item in Item.ItemList)
                    {
                        if (item.Condition > 50.0f) continue;
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
                case 8: // Degrading2:      5 seconds after 'Degrading1' dismissed, and only if player has not assigned any crew to perform maintenance [Video]
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
                case 9: // Medical:         Crewmember is injured but not killed [Video]

                    bool injuredFound = false;
                    for (int i = 0; i < crew.Count; i++)
                    {
                        Character member = crew[i];
                        if (member.Vitality < member.MaxVitality / 2f && !member.IsDead)
                        {
                            injuredFound = true;
                            break;
                        }
                    }
                    
                    if (!injuredFound) return false;
                    break;
                case 10: // Approach1:      Destination is within 100m [Video]
                    if (Vector2.Distance(Submarine.MainSub.WorldPosition, Level.Loaded.EndPosition) > 100000f)
                    {
                        return false;
                    }
                    break;
                case 11: // Approach2:      Sub is docked [Text]
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
            for(int i = 0; i < crew.Count; i++)
            {
                if (crew[i].CurrentOrder.AITag == aiTag) return true;
            }

            return false;
        }

        private bool IsFlooding()
        {
            float floodingAmount = 0.0f;
            foreach (Hull hull in Hull.hullList)
            {
                floodingAmount += hull.WaterVolume / hull.Volume / Hull.hullList.Count;
            }

            return floodingAmount >= 0.1f; // Ignore ballast
        }

        private void TriggerTutorialSegment(int index)
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
                    infoBox = CreateInfoFrame(activeSegment.Content.Value);
                    break;
            }

            // TODO: Save triggered to XML
        }
    }
}
