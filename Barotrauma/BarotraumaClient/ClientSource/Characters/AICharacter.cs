namespace Barotrauma
{
    public partial class AICharacter : Character
    {
        public override void DrawFront(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch, Camera cam)
        {
            base.DrawFront(spriteBatch, cam);
            if (GameMain.DebugDraw && !IsDead)
            {
                aiController.DebugDraw(spriteBatch);
                if (HumanAIController.DebugAI && aiController is HumanAIController { ShipCommandManager: ShipCommandManager shipCommandManager } &&
                    shipCommandManager.Commander == this)
                {
                    shipCommandManager.DebugDraw(spriteBatch);
                }
            }
        }
    }
}
