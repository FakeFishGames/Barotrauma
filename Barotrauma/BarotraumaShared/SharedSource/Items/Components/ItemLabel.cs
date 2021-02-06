using System.Diagnostics.CodeAnalysis;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    partial class ItemLabel : ItemComponent, IDrawableComponent, IServerSerializable
    {
        public Vector2 DrawSize
        {
            //use the extents of the item as the draw size
            get { return Vector2.Zero; }
        }

        partial void OnStateChanged();

        public override void ReceiveSignal(Signal signal)
        {
            switch (signal.connection.Name)
            {
                case "set_text":
                    if (Text == signal.value) { return; }
                    Text = signal.value;
                    OnStateChanged();
                    break;
            }
        }
    }
}