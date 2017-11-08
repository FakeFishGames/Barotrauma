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

        //the output is sent if both inputs have received a signal within the timeframe
        private TimeSpan delay;

        private Queue<Tuple<string, DateTime>> signalQueue;
        
        [InGameEditable, Serialize(1.0f, true)]
        public float Delay
        {
            get { return (float)delay.TotalSeconds; }
            set
            {
                float seconds = MathHelper.Clamp(value, 0.0f, 60.0f);

                delay = new TimeSpan(0,0,0,0, (int)(seconds*1000.0f));
            }
        }
        
        public DelayComponent(Item item, XElement element)
            : base (item, element)
        {
            signalQueue = new Queue<Tuple<string, DateTime>>();

            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            while (signalQueue.Any() && signalQueue.Peek().Item2 + delay <= DateTime.Now)
            {
                var signalOut = signalQueue.Dequeue();

                item.SendSignal(0, signalOut.Item1, "signal_out", null);
            }
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power=0.0f)
        {
            switch (connection.Name)
            {
                case "signal_in":
                    if (signalQueue.Count >= SignalQueueSize) return;

                    signalQueue.Enqueue(new Tuple<string, DateTime>(signal, DateTime.Now));
                    break;
            }
        }
    }
}
