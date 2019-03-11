using System;
using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class ColorComponent : ItemComponent
    {
        protected float[] receivedSignal;

        private string output = "0,0,0,0";

        public ColorComponent(Item item, XElement element)
            : base(item, element)
        {
            receivedSignal = new float[4];
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            item.SendSignal(0, output, "signal_out", null);
        }

        private void UpdateOutput()
        {
            output = receivedSignal[0].ToString("G", CultureInfo.InvariantCulture);
            output += "," + receivedSignal[1].ToString("G", CultureInfo.InvariantCulture);
            output += "," + receivedSignal[2].ToString("G", CultureInfo.InvariantCulture);
            output += "," + receivedSignal[3].ToString("G", CultureInfo.InvariantCulture);
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            switch (connection.Name)
            {
                case "signal_r":
                    float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out receivedSignal[0]);
                    UpdateOutput();
                    break;
                case "signal_g":
                    float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out receivedSignal[1]);
                    UpdateOutput();
                    break;
                case "signal_b":
                    float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out receivedSignal[2]);
                    UpdateOutput();
                    break;
                case "signal_a":
                    float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out receivedSignal[3]);
                    UpdateOutput();
                    break;
            }
        }
    }
}
