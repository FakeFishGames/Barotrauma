using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    partial class Controller : ItemComponent
    {
        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            var focusTarget = GetFocusTarget();
            if (focusTarget == null) return;
            if (character.ViewTarget == focusTarget)
            {
                foreach (ItemComponent ic in focusTarget.components)
                {
                    ic.DrawHUD(spriteBatch, character);
                }
            }
        }
    }
}
