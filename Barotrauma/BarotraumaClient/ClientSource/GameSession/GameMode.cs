using Microsoft.Xna.Framework.Graphics;
namespace Barotrauma
{
    partial class GameMode
    {
        public virtual void HUDScaleChanged() { }

        public void Draw(SpriteBatch spriteBatch)
        {
            DrawGameModeSpecific(spriteBatch);
            foreach (var hook in PluginData.GetHooks<PluginGameModeDrawDelegate>())
            {
                hook(this, spriteBatch);
            }
        }

        protected virtual void DrawGameModeSpecific(SpriteBatch spriteBatch)
        {
        }
    }
}
