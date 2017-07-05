using Barotrauma.Networking;
using Microsoft.Xna.Framework.Graphics;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class ConnectionPanel : ItemComponent, IServerSerializable, IClientSerializable
    {
        public override void UpdateHUD(Character character)
        {
            if (character != Character.Controlled || character != user) return;

            if (Screen.Selected != GameMain.EditMapScreen &&
                character.IsKeyHit(InputType.Select) &&
                character.SelectedConstruction == this.item) character.SelectedConstruction = null;

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

        public override XElement Save(XElement parentElement)
        {
            XElement componentElement = base.Save(parentElement);

            foreach (Connection c in Connections)
            {
                c.Save(componentElement);
            }

            return componentElement;
        }
    }
}
