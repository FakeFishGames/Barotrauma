using System;
using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class AdderComponent : ItemComponent
    {
        //an array to keep track of how long ago a signal was received on both inputs
        protected float[] timeSinceReceived;

        protected float[] receivedSignal;


        //the output is sent if both inputs have received a signal within the timeframe
        protected float timeFrame;
        
        [InGameEditable(DecimalCount = 2), Serialize(0.0f, true)]
        public float TimeFrame
        {
            get { return timeFrame; }
            set
            {
                timeFrame = Math.Max(0.0f, value);
            }
        }

        public AdderComponent(Item item, XElement element)
            : base(item, element)
        {
            timeSinceReceived = new float[] { Math.Max(timeFrame * 2.0f, 0.1f), Math.Max(timeFrame * 2.0f, 0.1f) };
            receivedSignal = new float[2];
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
                item.SendSignal(0, (receivedSignal[0] + receivedSignal[1]).ToString(), "signal_out", null);
            }
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            switch (connection.Name)
            {
                case "signal_in1":
                    float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out receivedSignal[0]);
                    timeSinceReceived[0] = 0.0f;
                    break;
                case "signal_in2":
                    float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out receivedSignal[1]);
                    timeSinceReceived[1] = 0.0f;
                    break;
            }
        }
    }
}
