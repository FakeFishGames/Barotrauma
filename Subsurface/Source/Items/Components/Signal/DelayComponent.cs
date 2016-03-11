using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class DelayComponent : ItemComponent
    {
        //the output is sent if both inputs have received a signal within the timeframe
        protected TimeSpan delay;

        private Queue<Tuple<string, DateTime>> signalQueue;
        
        [InGameEditable, HasDefaultValue(1.0f, true)]
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

                item.SendSignal(signalOut.Item1, "signal_out");
            }
        }

        public override void ReceiveSignal(string signal, Connection connection, Item sender, float power=0.0f)
        {
            switch (connection.Name)
            {
                case "signal_in":
                    signalQueue.Enqueue(new Tuple<string, DateTime>(signal, DateTime.Now));
                    break;
            }
        }
    }
}
