using Microsoft.Xna.Framework;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    abstract class StringComponent : ItemComponent
    {
        //an array to keep track of how long ago a signal was received on both inputs
        protected float[] timeSinceReceived;

        protected string[] receivedSignal;

        //the output is sent if both inputs have received a signal within the timeframe
        protected float timeFrame;


        [InGameEditable(DecimalCount = 2),
            Serialize(0.0f, true, description: "The item must have received signals to both inputs within this timeframe to output the result." +
            " If set to 0, the inputs must be received at the same time.", alwaysUseInstanceValues: true)]
        public float TimeFrame
        {
            get { return timeFrame; }
            set
            {
                timeFrame = Math.Max(0.0f, value);
            }
        }

        public StringComponent(Item item, XElement element)
            : base(item, element)
        {
            timeSinceReceived = new float[] { Math.Max(timeFrame * 2.0f, 0.1f), Math.Max(timeFrame * 2.0f, 0.1f) };
            receivedSignal = new string[2];
        }

        sealed public override void Update(float deltaTime, Camera cam)
        {
            for (int i = 0; i < timeSinceReceived.Length; i++)
            {
                if (timeSinceReceived[i] > timeFrame)
                {
                    IsActive = false;
                    return;
                }
                timeSinceReceived[i] += deltaTime;
            }
            string output = Calculate(receivedSignal[0], receivedSignal[1]);
            item.SendSignal(output, "signal_out");
        }

        protected abstract string Calculate(string signal1, string signal2);

        public override void ReceiveSignal(Signal signal)
        {
            switch (signal.connection.Name)
            {
                case "signal_in1":
                    receivedSignal[0] = signal.value;
                    timeSinceReceived[0] = 0.0f;
                    IsActive = true;
                    break;
                case "signal_in2":
                    receivedSignal[1] = signal.value;
                    timeSinceReceived[1] = 0.0f;
                    IsActive = true;
                    break;
            }
        }
    }
}
