using System.Xml.Linq;
using System;

namespace Barotrauma.Items.Components
{
    class ConcatComponent : StringComponent
    {
        private int maxOutputLength;

        [Editable, Serialize(256, false, description: "The maximum length of the output string. Warning: Large values can lead to large memory usage or networking load.")]
        public int MaxOutputLength
        {
            get { return maxOutputLength; }
            set
            {
                maxOutputLength = Math.Max(value, 0);
            }
        }


        public ConcatComponent(Item item, XElement element)
            : base(item, element)
        {
        }

        protected override string Calculate(string signal1, string signal2)
        {
            string output = signal1 + signal2;
            return output.Length <= maxOutputLength ? output : output.Substring(0, MaxOutputLength);
        }
    }
}
