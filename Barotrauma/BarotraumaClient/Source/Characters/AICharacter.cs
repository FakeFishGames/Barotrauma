namespace Barotrauma
{
    partial class AICharacter : Character
    {
        public override void DrawFront(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch, Camera cam)
        {
            base.DrawFront(spriteBatch, cam);
            if (GameMain.DebugDraw && !IsDead) aiController.DebugDraw(spriteBatch);
        }
    }
}
