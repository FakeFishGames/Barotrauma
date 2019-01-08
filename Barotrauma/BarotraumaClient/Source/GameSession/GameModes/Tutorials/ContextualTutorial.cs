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
                case 0: // 5 seconds after the game has started
                    if (false)
                    {
                        return false;
                    }
                    break;
                case 1: // Open interface X
                    if (false)
                    {
                        return false;
                    }
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
