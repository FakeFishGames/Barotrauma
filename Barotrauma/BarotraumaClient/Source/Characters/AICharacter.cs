namespace Barotrauma
{
    partial class AICharacter : Character
    {
        partial void InitProjSpecific()
        {
            soundTimer = Rand.Range(0.0f, soundInterval);
            OnAttacked += OnAttackedProjSpecific;
        }

        //TODO: configure sound intervals per sound type?

        private void OnAttackedProjSpecific(Character attacker, AttackResult attackResult)
        {
            if (attackResult.Damage <= 0) { return; }
            soundTimer = Rand.Range(0.0f, soundInterval);
            if (soundTimer < soundInterval * 0.5f)
            {
                PlaySound(CharacterSound.SoundType.Attack);
                soundTimer = soundInterval;
            }
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
