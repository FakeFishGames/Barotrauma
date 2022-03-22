using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class XorComponent : AndComponent
    {
        public XorComponent(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            int receivedInputs = 0;
            for (int i = 0; i < timeSinceReceived.Length; i++)
            {
                if (timeSinceReceived[i] <= timeFrame) { receivedInputs += 1; }
                timeSinceReceived[i] += deltaTime;
            }

            bool state = receivedInputs == 1;
            string signalOut = state ? output : falseOutput;
            if (string.IsNullOrEmpty(signalOut))
            {
                //deactivate the component if state is false and there's no false output (will be woken up by non-zero signals in ReceiveSignal)
                if (!state) { IsActive = false; }
                return;
            }

            item.SendSignal(new Signal(signalOut, sender: signalSender[0] ?? signalSender[1]), "signal_out");
        }
    }
}
