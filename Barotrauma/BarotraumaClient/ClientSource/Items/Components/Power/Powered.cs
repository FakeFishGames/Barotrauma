using Barotrauma.Sounds;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Powered : ItemComponent
    {
        private RoundSound powerOnSound;
        private bool powerOnSoundPlayed;
        
        partial void InitProjectSpecific(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "poweronsound":
                        powerOnSound = Submarine.LoadRoundSound(subElement, false);
                        break;
                }
            }
        }
    }
}
