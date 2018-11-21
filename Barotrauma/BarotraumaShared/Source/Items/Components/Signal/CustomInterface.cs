using System;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    class CustomInterface : ItemComponent
    {
        private string buttonText, signalOut, connection;

        [InGameEditable, Serialize("Default button", true)]
        public string ButtonText
        {
            get { return buttonText; }
            set { buttonText = value; }
        }

        [InGameEditable, Serialize("1", true)]
        public string SignalOut
        {
            get { return signalOut; }
            set { signalOut = value; }
        }

        [InGameEditable, Serialize("signal_out", true)]
        public string Connection
        {
            get { return connection; }
            set { connection = value; }
        }

        public CustomInterface(Item item, XElement element)
            : base(item, element)
        {
            // TODO: initialization
        }

        public void SendSignal()
        {
            Item.SendSignal(0, signalOut, connection, null);
        }
    }
}
