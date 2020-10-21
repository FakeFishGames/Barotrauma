using Microsoft.Xna.Framework;
using System;
using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    abstract class ArithmeticComponent : ItemComponent
    {
        //an array to keep track of how long ago a signal was received on both inputs
        protected float[] timeSinceReceived;

        protected float[] receivedSignal;

        //the output is sent if both inputs have received a signal within the timeframe
        protected float timeFrame;

        [Serialize(999999.0f, true, description: "The output of the item is restricted below this value.", alwaysUseInstanceValues: true),
            InGameEditable(MinValueFloat = -999999.0f, MaxValueFloat = 999999.0f)]
        public float ClampMax
        {
            get;
            set;
        }

        [Serialize(-999999.0f, true, description: "The output of the item is restricted above this value.", alwaysUseInstanceValues: true),
            InGameEditable(MinValueFloat = -999999.0f, MaxValueFloat = 999999.0f)]
        public float ClampMin
        {
            get;
            set;
        }

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

        public ArithmeticComponent(Item item, XElement element)
            : base(item, element)
        {
            timeSinceReceived = new float[] { Math.Max(timeFrame * 2.0f, 0.1f), Math.Max(timeFrame * 2.0f, 0.1f) };
            receivedSignal = new float[2];
        }

        sealed public override void Update(float deltaTime, Camera cam)
        {
            for (int i = 0; i < timeSinceReceived.Length; i++) { timeSinceReceived[i] += deltaTime; }
            if (timeSinceReceived[0] > timeFrame && timeSinceReceived[1] > timeFrame) 
            {
                IsActive = false;
                return;
            }
            if timeSinceReceived[0] > timeFrame || timeSinceReceived[1] > timeFrame) { return; }
            float output = Calculate(receivedSignal[0], receivedSignal[1]);
            if (MathUtils.IsValid(output))
            {
                item.SendSignal(0, MathHelper.Clamp(output, ClampMin, ClampMax).ToString("G", CultureInfo.InvariantCulture), "signal_out", null);
            }           
        }

        protected abstract float Calculate(float signal1, float signal2);

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            switch (connection.Name)
            {
                case "signal_in1":
                    float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out receivedSignal[0]);
                    timeSinceReceived[0] = 0.0f;
                    IsActive = true;
                    break;
                case "signal_in2":
                    float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out receivedSignal[1]);
                    timeSinceReceived[1] = 0.0f;
                    IsActive = true;
                    break;
            }
        }
    }
}
