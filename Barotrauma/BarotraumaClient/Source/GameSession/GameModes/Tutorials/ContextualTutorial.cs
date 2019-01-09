using System.Collections.Generic;
using System.Xml.Linq;
using System;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma.Tutorials
{
    class ContextualTutorial : Tutorial
    {
        public static bool ContentRunning = false;

        private enum ContentTypes { None = 0, Video = 1, Text = 2 };
        private SpriteSheetPlayer spriteSheetPlayer;
        private string playableContentPath;

        private TutorialSegment activeSegment = null;
        private float inputGracePeriodTimer;
        private const float inputGracePeriod = 1f;

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

        private List<TutorialSegment> segments;

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

        public override void AddToGUIUpdateList()
        {
            base.AddToGUIUpdateList();
            spriteSheetPlayer.AddToGUIUpdateList();
        }

        public override void Update(float deltaTime)
        {
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
                if (CheckContextualTutorials(i)) // Found a relevant tutorial, halt finding new ones
                {
                    break;
                }
            }
        }

        private bool CheckContextualTutorials(int index)
        {
            switch (index)
            {
                case 0: // Welcome:         Game Start [Text]
                    return false;
                    break;
                case 1: // Command Reactor: 10 seconds after 'Welcome' dismissed and only if no command given to start reactor [Video]
                    return false;
                    break;
                case 2: // Nav Console:     20 seconds after 'Command Reactor' dismissed or if nav console is activated [Video]
                    return false;
                    break;
                case 3: // Objective:       Travel 200 meters and while sub is not flooding [Text]
                    return false;
                    break;
                case 4: // Flood:           Hull is breached and sub is taking on water [Video]
                    return false;
                    break;
                case 5: // Reactor:         Player uses reactor for the first time [Video]
                    return false;
                    break;
                case 6: // Enemy on Sonar:  Player witnesses creature signal on sonar for 5 seconds [Video]
                    return false;
                    break;
                case 7: // Degrading1:      Any equipment degrades to 50% health or less and player has not assigned any crew to perform maintenance [Text]
                    return false;
                    break;
                case 8: // Degrading2:      5 seconds after 'Degrading1' dismissed, and only if player has not assigned any crew to perform maintenance [Video]
                    return false;
                    break;
                case 9: // Medical:         Crewmember is injured but not killed [Video]
                    return false;
                    break;
                case 10: // Approach1:      Destination is within 100m [Video]
                    return false;
                    break;
                case 11: // Approach2:      Sub is docked [Text]
                    return false;
                    break;                   
            }

            TriggerTutorialSegment(index);
            return true;
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
