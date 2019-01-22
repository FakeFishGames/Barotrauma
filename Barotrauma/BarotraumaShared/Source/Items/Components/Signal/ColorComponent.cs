using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class ColorComponent : ItemComponent
    {
        //an array to keep track of how long ago a signal was received on both inputs
        protected float[] timeSinceReceived;

        protected float[] receivedSignal;


        //the output is sent if both inputs have received a signal within the timeframe
        protected float timeFrame;

        [InGameEditable, Serialize(0.0f, true)]
        public float TimeFrame
        {
            get { return timeFrame; }
            set
            {
                timeFrame = Math.Max(0.0f, value);
            }
        }

        public ColorComponent(Item item, XElement element)
            : base(item, element)
        {
            timeSinceReceived = new float[] { Math.Max(timeFrame * 2.0f, 0.1f), Math.Max(timeFrame * 2.0f, 0.1f), Math.Max(timeFrame * 2.0f, 0.1f), Math.Max(timeFrame * 2.0f, 0.1f) };
            receivedSignal = new float[4];
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            bool sendOutput = true;
            for (int i = 0; i < timeSinceReceived.Length; i++)
            {
                if (timeSinceReceived[i] > timeFrame) sendOutput = false;
                timeSinceReceived[i] += deltaTime;
            }
            if (sendOutput)
            {
                string output = receivedSignal[0].ToString();
                output += "," + receivedSignal[1].ToString();
                output += "," + receivedSignal[2].ToString();
                output += "," + receivedSignal[3].ToString();

                item.SendSignal(0, output, "signal_out", null);
            }
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f)
        {
            switch (connection.Name)
            {
                case "signal_r":
                    float.TryParse(signal, out receivedSignal[0]);
                    timeSinceReceived[0] = 0.0f;
                    break;
                case "signal_g":
                    float.TryParse(signal, out receivedSignal[1]);
                    timeSinceReceived[1] = 0.0f;
                    break;
                case "signal_b":
                    float.TryParse(signal, out receivedSignal[2]);
                    timeSinceReceived[2] = 0.0f;
                    break;
                case "signal_a":
                    float.TryParse(signal, out receivedSignal[3]);
                    timeSinceReceived[3] = 0.0f;
                    break;
            }
        }
    }
}
