using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    partial class ItemLabel : ItemComponent, IDrawableComponent
    {
        public Vector2 DrawSize
        {
            //use the extents of the item as the draw size
            get { return Vector2.Zero; }
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0, float signalStrength = 1)
        {
            switch (connection.Name)
            {
                case "set_text":
                    Text = signal;
                    break;
            }
        }
    }
}