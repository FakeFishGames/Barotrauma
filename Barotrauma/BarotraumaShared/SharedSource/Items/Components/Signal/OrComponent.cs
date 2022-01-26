using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class OrComponent : AndComponent
    {
        public OrComponent(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            bool sendOutput = false;
            for (int i = 0; i < timeSinceReceived.Length; i++)
            {
                if (timeSinceReceived[i] <= timeFrame) { sendOutput = true; }
                timeSinceReceived[i] += deltaTime;
            }

            string signalOut = sendOutput ? output : falseOutput;
            if (string.IsNullOrEmpty(signalOut))
            {
                IsActive = false;
                return;
            }

            item.SendSignal(new Signal(signalOut, sender: signalSender[0] ?? signalSender[1]), "signal_out");
        }
    }
}
