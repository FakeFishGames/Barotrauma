using System;
using System.Xml.Linq;
using Barotrauma.Sounds;

namespace Barotrauma
{
    class CharacterSound
    {
        public enum SoundType
        {
            Idle, Attack, Die
        }

        public readonly Sound Sound;

        public readonly SoundType Type;

        public readonly float Range;

        public CharacterSound(XElement element)
        {
            Sound = Submarine.LoadRoundSound(element.Attribute("file").Value);
            Range = element.GetAttributeFloat("range", 1000.0f);

            Enum.TryParse<SoundType>(element.GetAttributeString("state", "Idle"), true, out Type);
        }
    }
}
