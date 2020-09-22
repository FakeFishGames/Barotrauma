using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class ConcatComponent : StringComponent
    {
        public ConcatComponent(Item item, XElement element)
            : base(item, element)
        {
        }

        protected override string Calculate(string signal1, string signal2)
        {
            return signal1 + signal2;
        }
    }
}
