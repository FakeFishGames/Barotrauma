using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    class Screen
    {
        private static Screen selected;
        
        public static Screen Selected
        {
            get { return selected; }
        }

        public virtual void Deselect()
        {
        }

        public virtual void Select()
        {
            if (selected != null && selected!=this) selected.Deselect();
            selected = this;
        }

        public virtual void Update(double deltaTime)
        {
        }

        public virtual void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
        }

    }
}
