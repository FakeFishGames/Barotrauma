using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class MultiplyComponent : ArithmeticComponent
    {
        public MultiplyComponent(Item item, XElement element)
            : base(item, element)
        {
        }

        protected override float Calculate(float signal1, float signal2)
        {
            return signal1 * signal2;
        }
    }
}
