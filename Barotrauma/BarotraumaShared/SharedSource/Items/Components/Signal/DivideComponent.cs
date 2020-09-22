using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class DivideComponent : ArithmeticComponent
    {
        public DivideComponent(Item item, XElement element)
            : base(item, element)
        {
        }

        protected override float Calculate(float signal1, float signal2)
        {
            if (MathUtils.NearlyEqual(signal2, 0)) { return float.NaN; }
            return signal1 / signal2;
        }
    }
}
