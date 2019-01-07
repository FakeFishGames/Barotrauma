using Barotrauma.Source.GUI;
using Barotrauma.Tutorials;
using System.Xml.Linq;

namespace Barotrauma.Source.GameSession.GameModes.Tutorials
{
    class ContextualTutorial : Tutorial
    {
        private SpriteSheetPlayer spriteSheetPlayer;
        private XElement[] videos;
        private string playableContentPath;

        private bool[] segmentStates;

        public ContextualTutorial(XElement element) : base(element)
        {
            playableContentPath = element.GetAttributeString("playablecontentpath", "");
            videos = element.Elements("Video") as XElement[];
            spriteSheetPlayer = new SpriteSheetPlayer();

            XElement[] segments = element.Elements("Segment") as XElement[];
            segmentStates = new bool[segments.Length];

            for (int i = 0; i < segments.Length; i++)
            {
                segmentStates[i] = segments[i].GetAttributeBool("istriggered", false);
            }
        }

        public override void Update(float deltaTime)
        {
            if (spriteSheetPlayer.IsPlaying) return;

            for(int i = 0; i < segmentStates.Length; i++)
            {
                if (segmentStates[i]) continue;
                CheckContextualTutorials(i);
            }
        }

        private void CheckContextualTutorials(int index)
        {
            switch(index)
            {
                case 0: // 5 seconds after the game has started
                    if(false)
                    {
                        return;
                    }
                    break;
                case 1: // Open interface X
                    if (false)
                    {
                        return;
                    }
                    break;
            }

            StartPlaybackAndSaveStatus(index);
        }

        private void StartPlaybackAndSaveStatus(int index)
        {
            segmentStates[index] = true;
            spriteSheetPlayer.SetContent(playableContentPath, videos[index], true);
            // TODO: Save to XML
        }
    }
}
