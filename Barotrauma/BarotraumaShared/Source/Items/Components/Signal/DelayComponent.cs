using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class DelayComponent : ItemComponent
    {
        class DelayedSignal
        {
            public readonly string Signal;
            public readonly float SignalStrength;
            public float SendTimer;

            public DelayedSignal(string signal, float signalStrength, float sendTimer)
            {
                Signal = signal;
                SignalStrength = signalStrength;
                SendTimer = sendTimer;
            }
        }

        const int SignalQueueSize = 500;

        private Queue<DelayedSignal> signalQueue;
        
        [InGameEditable(MinValueFloat = 0.0f, MaxValueFloat = 60.0f, DecimalCount = 2), Serialize(1.0f, true, description: "How long the item delays the signals (in seconds).")]
        public float Delay
        {
            get;
            set;
        }

        [InGameEditable, Serialize(false, true, description: "Should the component discard previously received signals when a new one is received.")]
        public bool ResetWhenSignalReceived
        {
            get;
            set;
        }

        [InGameEditable, Serialize(false, true, description: "Should the component discard previously received signals when the incoming signal changes.")]
        public bool ResetWhenDifferentSignalReceived
        {
            get;
            set;
        }

        public DelayComponent(Item item, XElement element)
            : base (item, element)
        {
            signalQueue = new Queue<DelayedSignal>();
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            foreach (var val in signalQueue)
            {
                val.SendTimer -= deltaTime;
            }

            while (signalQueue.Count > 0 && signalQueue.Peek().SendTimer <= 0.0f)
            {
                var signalOut = signalQueue.Dequeue();
                item.SendSignal(0, signalOut.Signal, "signal_out", null, signalStrength: signalOut.SignalStrength);
            }
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            switch (connection.Name)
            {
                case "signal_in":
                    if (signalQueue.Count >= SignalQueueSize) return;
                    if (ResetWhenSignalReceived) signalQueue.Clear();
                    if (ResetWhenDifferentSignalReceived && signalQueue.Count > 0 && signalQueue.Peek().Signal != signal)
                    {
                        signalQueue.Clear();
                    }
                    signalQueue.Enqueue(new DelayedSignal(signal, signalStrength, Delay));
                    break;
            }
        }
    }
}
