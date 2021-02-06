using System;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class AndComponent : ItemComponent
    {
        protected string output, falseOutput;

        //an array to keep track of how long ago a non-zero signal was received on both inputs
        protected float[] timeSinceReceived;

        //the output is sent if both inputs have received a signal within the timeframe
        protected float timeFrame;
        
        [InGameEditable(DecimalCount = 2), Serialize(0.0f, true, description: "The item sends the output if both inputs have received a non-zero signal within the timeframe. If set to 0, the inputs must receive a signal at the same time.", alwaysUseInstanceValues: true)]
        public float TimeFrame
        {
            get { return timeFrame; }
            set
            {
                timeFrame = Math.Max(0.0f, value);
            }
        }

        [InGameEditable, Serialize("1", true, description: "The signal sent when the condition is met.", alwaysUseInstanceValues: true)]
        public string Output
        {
            get { return output; }
            set { output = value; }
        }

        [InGameEditable, Serialize("", true, description: "The signal sent when the condition is met (if empty, no signal is sent).", alwaysUseInstanceValues: true)]
        public string FalseOutput
        {
            get { return falseOutput; }
            set { falseOutput = value; }
        }

        public AndComponent(Item item, XElement element)
            : base(item, element)
        {
            timeSinceReceived = new float[] { Math.Max(timeFrame * 2.0f, 0.1f), Math.Max(timeFrame * 2.0f, 0.1f) };
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

            string signalOut = sendOutput ? output : falseOutput;
            if (string.IsNullOrEmpty(signalOut)) return;

            item.SendSignal(0, signalOut, "signal_out", null);
        }

        public override void ReceiveSignal([NotNull] Signal signal)
        {
            switch (signal.connection.Name)
            {
                case "signal_in1":
                    if (signal.value == "0") return;
                    timeSinceReceived[0] = 0.0f;
                    break;
                case "signal_in2":
                    if (signal.value == "0") return;
                    timeSinceReceived[1] = 0.0f;
                    break;
                case "set_output":
                    output = signal.value;
                    break;
            }
        }
    }
}
