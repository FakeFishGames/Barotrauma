using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    partial class ConnectionPanel : ItemComponent, IServerSerializable, IClientSerializable
    {
        public static Wire HighlightedWire;

        public override bool ShouldDrawHUD(Character character)
        {
            return character == Character.Controlled && character == user;
        }
        
        public override void AddToGUIUpdateList()
        {
            GuiFrame?.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character, float deltaTime)
        {
            if (character != Character.Controlled || character != user) return;

            GuiFrame?.UpdateManually(deltaTime);
            
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
            if (GuiFrame == null) return;

            GuiFrame.DrawManually(spriteBatch);

            HighlightedWire = null;
            Connection.DrawConnections(spriteBatch, this, character);

            foreach (UISprite sprite in GUI.Style.GetComponentStyle("ConnectionPanelFront").Sprites[GUIComponent.ComponentState.None])
            {
                sprite.Draw(spriteBatch, GuiFrame.Rect, Color.White, SpriteEffects.None);
            }
        }
    }
}
