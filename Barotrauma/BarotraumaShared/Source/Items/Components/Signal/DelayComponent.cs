using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class DelayComponent : ItemComponent
    {
        const int SignalQueueSize = 500;

        private Queue<Pair<string, float>> signalQueue;

        [InGameEditable(MinValueFloat = 0.0f, MaxValueFloat = 60.0f), Serialize(1.0f, true)]
        public float Delay
        {
            get;
            set;
        }

        [InGameEditable(ToolTip = "Should the component discard previously received signals when a new one is received."), Serialize(false, true)]
        public bool ResetWhenSignalReceived
        {
            get;
            set;
        }


        public DelayComponent(Item item, XElement element)
            : base (item, element)
        {
            signalQueue = new Queue<Pair<string, float>>();

            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            foreach (var val in signalQueue)
            {
                val.Second -= deltaTime;
            }

            while (signalQueue.Count > 0 && signalQueue.Peek().Second <= 0.0f)
            {
                var signalOut = signalQueue.Dequeue();
                item.SendSignal(0, signalOut.First, "signal_out", null);
            }
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f)
        {
            switch (connection.Name)
            {
                case "signal_in":
                    if (signalQueue.Count >= SignalQueueSize) return;
                    if (ResetWhenSignalReceived) signalQueue.Clear();
                    signalQueue.Enqueue(Pair<string, float>.Create(signal, Delay));
                    break;
            }
        }
    }
}
