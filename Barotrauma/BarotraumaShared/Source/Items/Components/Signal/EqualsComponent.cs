using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class EqualsComponent : ItemComponent
    {
        protected string output, falseOutput;

        //an array to keep track of how long ago a signal was received on both inputs
        protected float[] timeSinceReceived;

        protected float[] receivedSignal;


        //the output is sent if both inputs have received a signal within the timeframe
        protected float timeFrame;

        [InGameEditable, Serialize("1", true)]
        public string Output
        {
            get { return output; }
            set { output = value; }
        }

        [InGameEditable, Serialize("", true)]
        public string FalseOutput
        {
            get { return falseOutput; }
            set { falseOutput = value; }
        }

        public EqualsComponent(Item item, XElement element)
            : base(item, element)
        {
            timeSinceReceived = new float[] { Math.Max(timeFrame * 2.0f, 0.1f), Math.Max(timeFrame * 2.0f, 0.1f) };
            receivedSignal = new float[2];
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            bool sendOutput = false;
            for (int i = 0; i < timeSinceReceived.Length; i++)
            {
                if (timeSinceReceived[i] <= timeFrame) sendOutput = true;
                timeSinceReceived[i] += deltaTime;
            }

            if (sendOutput)
            {
                string signalOut = receivedSignal[0] == receivedSignal[1] ? output : falseOutput;
                if (string.IsNullOrEmpty(signalOut)) return;

                item.SendSignal(0, signalOut, "signal_out", null);
            }
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f)
        {
            switch (connection.Name)
            {
                case "signal_in1":
                    float.TryParse(signal, out receivedSignal[0]);
                    timeSinceReceived[0] = 0.0f;
                    break;
                case "signal_in2":
                    float.TryParse(signal, out receivedSignal[1]);
                    timeSinceReceived[1] = 0.0f;
                    break;
            }
        }
    }
}
