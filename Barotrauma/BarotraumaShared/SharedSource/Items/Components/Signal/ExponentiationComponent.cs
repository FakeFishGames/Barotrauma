using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class ExponentiationComponent : ItemComponent
    {
        private float exponent;
        [InGameEditable, Serialize(1.0f, false, description: "The exponent of the operation.", alwaysUseInstanceValues: true)]
        public float Exponent
        {
            get
            {
                return exponent;
            }
            set
            {
                exponent = value;
            }
        }

        public ExponentiationComponent(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
        }

        public override void ReceiveSignal(Signal signal)
        {
            switch (signal.connection.Name)
            {
                case "set_exponent":
                case "exponent":
                    float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out exponent);
                    break;
                case "signal_in":
                    float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value);
                    item.SendSignal(MathUtils.Pow(value, Exponent).ToString("G", CultureInfo.InvariantCulture), "signal_out");
                    break;
            }
        }
    }
}