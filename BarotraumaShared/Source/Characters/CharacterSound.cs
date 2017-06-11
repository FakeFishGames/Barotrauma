using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

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
            Sound = Sound.Load(element.Attribute("file").Value);
            Range = ToolBox.GetAttributeFloat(element, "range", 1000.0f);

            Enum.TryParse<SoundType>(ToolBox.GetAttributeString(element, "state", "Idle"), true, out Type);
        }
    }
}
