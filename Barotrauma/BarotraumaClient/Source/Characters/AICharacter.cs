namespace Barotrauma
{
    partial class AICharacter : Character
    {
        partial void InitProjSpecific()
        {
            soundTimer = Rand.Range(0.0f, soundInterval);
        }

        partial void SoundUpdate(float deltaTime)
        {
            if (soundTimer > 0)
            {
                soundTimer -= deltaTime;
            }
            else
            {
                switch (aiController.State)
                {
                    case AIController.AIState.Attack:
                        PlaySound(CharacterSound.SoundType.Attack);
                        break;
                    default:
                        PlaySound(CharacterSound.SoundType.Idle);
                        break;
                }
                soundTimer = soundInterval;
            }
        }

        public override void DrawFront(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch, Camera cam)
        {
            base.DrawFront(spriteBatch, cam);

            if (GameMain.DebugDraw && !IsDead) aiController.DebugDraw(spriteBatch);
        }

    }
}
