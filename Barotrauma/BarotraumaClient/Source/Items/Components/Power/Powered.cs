using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.Sounds;

namespace Barotrauma.Items.Components
{
    partial class Powered : ItemComponent
    {
        protected List<Sound> sparkSounds;

        private Sound powerOnSound;
        private bool powerOnSoundPlayed;
        
        partial void InitProjectSpecific(XElement element)
        {
            sparkSounds = new List<Sound>();
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
