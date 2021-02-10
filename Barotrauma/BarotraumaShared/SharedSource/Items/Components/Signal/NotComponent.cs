using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class NotComponent : ItemComponent
    {
        private bool signalReceived;

        private bool continuousOutput;
        [Editable, Serialize(false, true, description: "When enabled, the component continuously outputs \"1\" when it's not receiving a signal.", alwaysUseInstanceValues: true)]
        public bool ContinuousOutput
        {
            get { return continuousOutput; }
            set { continuousOutput = IsActive = value; }
        }

        public NotComponent(Item item, XElement element)
            : base (item, element)
        {
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);
            if (!signalReceived)
            {
                item.SendSignal(0, "1", "signal_out", null, 0.0f);
            }
            signalReceived = false;
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            if (connection.Name != "signal_in") { return; }
            item.SendSignal(stepsTaken, signal == "0" || signal == string.Empty ? "1" : "0", "signal_out", sender, 0.0f, source, signalStrength);
            signalReceived = true;
        }
    }
}
