using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class ModuloComponent : ItemComponent
    {
        private float modulus;
        [InGameEditable, Serialize(1.0f, IsPropertySaveable.No, description: "The modulus of the operation. Must be non-zero.", alwaysUseInstanceValues: true)]
        public float Modulus
        {
            get { return modulus; }
            set
            {
                modulus = MathUtils.NearlyEqual(value, 0.0f) ? 1.0f : value; 
            }
        }

        public ModuloComponent(Item item, ContentXElement element) : base(item, element)
        {
            IsActive = true;
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            switch (connection.Name)
            {
                case "set_modulus":
                case "modulus":
                    float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float newModulus);
                    Modulus = newModulus;
                    break;
                case "signal_in":
                    float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value);
                    signal.value = (value % modulus).ToString("G", CultureInfo.InvariantCulture);
                    item.SendSignal(signal, "signal_out");
                    break;
            }
                
        }
    }
}
