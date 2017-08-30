using Barotrauma.Networking;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    partial class ConnectionPanel : ItemComponent, IServerSerializable, IClientSerializable
    {
        public override void UpdateHUD(Character character)
        {
            if (character != Character.Controlled || character != user) return;
            
            if (HighlightedWire != null)
            {
                HighlightedWire.Item.IsHighlighted = true;
                if (HighlightedWire.Connections[0] != null && HighlightedWire.Connections[0].Item != null) HighlightedWire.Connections[0].Item.IsHighlighted = true;
                if (HighlightedWire.Connections[1] != null && HighlightedWire.Connections[1].Item != null) HighlightedWire.Connections[1].Item.IsHighlighted = true;
            }
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (character != Character.Controlled || character != user) return;

            HighlightedWire = null;
            Connection.DrawConnections(spriteBatch, this, character);

        }
    }
}
