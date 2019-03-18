using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.Sounds;

namespace Barotrauma.Items.Components
{
    partial class Powered : ItemComponent
    {
        protected List<RoundSound> sparkSounds;

        private RoundSound powerOnSound;
        private bool powerOnSoundPlayed;
        
        partial void InitProjectSpecific(XElement element)
        {
            sparkSounds = new List<RoundSound>();
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "poweronsound":
                        powerOnSound = Submarine.LoadRoundSound(subElement, false);
                        break;
                    case "sparksound":
                        sparkSounds.Add(Submarine.LoadRoundSound(subElement, false));
                        break;
                }
            }
        }
    }
}
