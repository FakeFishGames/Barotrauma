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
            //in number of frames
            public int SendTimer;
            //in number of frames
            public int SendDuration;

            public DelayedSignal(string signal, float signalStrength, int sendTimer)
            {
                Signal = signal;
                SignalStrength = signalStrength;
                SendTimer = sendTimer;
            }
        }

        private int signalQueueSize;
        private int delayTicks;

        private Queue<DelayedSignal> signalQueue;

        private DelayedSignal prevQueuedSignal;
        
        private float delay;
        [InGameEditable(MinValueFloat = 0.0f, MaxValueFloat = 60.0f, DecimalCount = 2), Serialize(1.0f, true, description: "How long the item delays the signals (in seconds).", alwaysUseInstanceValues: true)]
        public float Delay
        {
            get { return delay; }
            set
            {
                if (value == delay) { return; }
                delay = value;
                delayTicks = (int)(delay / Timing.Step);
                signalQueueSize = delayTicks * 2;
            }
        }

        [InGameEditable, Serialize(false, true, description: "Should the component discard previously received signals when a new one is received.", alwaysUseInstanceValues: true)]
        public bool ResetWhenSignalReceived
        {
            get;
            set;
        }

        [InGameEditable, Serialize(false, true, description: "Should the component discard previously received signals when the incoming signal changes.", alwaysUseInstanceValues: true)]
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
                val.SendTimer -= 1;
            }

            while (signalQueue.Count > 0 && signalQueue.Peek().SendTimer <= 0)
            {
                var signalOut = signalQueue.Peek();
                signalOut.SendDuration -= 1;
                item.SendSignal(0, signalOut.Signal, "signal_out", null, signalStrength: signalOut.SignalStrength);
                if (signalOut.SendDuration <= 0) { signalQueue.Dequeue(); } else { break; }
            }
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            switch (connection.Name)
            {
                case "signal_in":
                    if (signalQueue.Count >= signalQueueSize) { return; }
                    if (ResetWhenSignalReceived) { prevQueuedSignal = null; signalQueue.Clear(); }
                    if (ResetWhenDifferentSignalReceived && signalQueue.Count > 0 && signalQueue.Peek().Signal != signal)
                    {
                        prevQueuedSignal = null;
                        signalQueue.Clear();
                    }

                    if (prevQueuedSignal != null && 
                        prevQueuedSignal.Signal == signal && 
                        MathUtils.NearlyEqual(prevQueuedSignal.SignalStrength, signalStrength) &&
                        ((prevQueuedSignal.SendTimer + prevQueuedSignal.SendDuration == delayTicks) || (prevQueuedSignal.SendTimer <= 0 && prevQueuedSignal.SendDuration > 0)))
                    {
                        prevQueuedSignal.SendDuration += 1;
                        return;
                    }

                    prevQueuedSignal = new DelayedSignal(signal, signalStrength, delayTicks)
                    {
                        SendDuration = 1
                    };
                    signalQueue.Enqueue(prevQueuedSignal);
                    break;
            }
        }
    }
}
