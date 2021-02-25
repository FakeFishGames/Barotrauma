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
                item.SendSignal(new Signal("1", power: 0.0f), "signal_out");
            }
            signalReceived = false;
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            if (connection.Name != "signal_in") { return; }

            signal.value = signal.value == "0" || string.IsNullOrEmpty(signal.value) ? "1" : "0";
            signal.power = 0.0f;
            item.SendSignal(signal, "signal_out");
            signalReceived = true;
        }
    }
}
