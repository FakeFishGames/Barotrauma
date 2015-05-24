using System;
using System.Globalization;
using System.Xml.Linq;

namespace Subsurface.Items.Components
{
    class AndComponent : ItemComponent
    {
        protected string output;

        //an array to keep track of how long ago a non-zero signal was received on both inputs
        protected float[] timeSinceReceived;

        //the output is sent if both inputs have received a signal within the timeframe
        protected float timeFrame;
        
        [InGameEditable, HasDefaultValue(0.1f, true)]
        public float TimeFrame
        {
            get { return timeFrame; }
            set
            {
                timeFrame = Math.Max(0.0f, value);
            }
        }

        [InGameEditable, HasDefaultValue("1", true)]
        public string Output
        {
            get { return output; }
            set { output = value; }
        }

        public AndComponent(Item item, XElement element)
            : base (item, element)
        {
            timeSinceReceived = new float[] { timeFrame*2.0f, timeFrame*2.0f};

            //output = "1";
        }

        public override void Update(float deltaTime, Camera cam)
        {
            bool sendOutput = true;
            for (int i = 0; i<timeSinceReceived.Length; i++)
            {                
                if (timeSinceReceived[i] > timeFrame) sendOutput = false;
                timeSinceReceived[i] += deltaTime;
            }

            if (sendOutput)
            {
                item.SendSignal(output, "signal_out");
            }
        }

        public override void ReceiveSignal(string signal, Connection connection, Item sender)
        {
            switch (connection.name)
            {
                case "signal_in1":
                    if (signal == "0") return;
                    timeSinceReceived[0] = 0.0f;
                    isActive = true;
                    break;
                case "signal_in2":
                    if (signal == "0") return;
                    timeSinceReceived[1] = 0.0f;
                    isActive = true;
                    break;
                case "set_output":
                    output = signal;
                    break;
            }
        }
    }
}
