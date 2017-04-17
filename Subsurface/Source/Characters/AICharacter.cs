using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    class AICharacter : Character
    {
        const float AttackBackPriority = 1.0f;

        private AIController aiController;
        
        public override AIController AIController
        {
            get { return aiController; }
        }
        
        public AICharacter(string file, Vector2 position, CharacterInfo characterInfo = null, bool isNetworkPlayer = false)
            : base(file, position, characterInfo, isNetworkPlayer)
        {
            soundTimer = Rand.Range(0.0f, soundInterval);
        }

        public void SetAI(AIController aiController)
        {
            this.aiController = aiController;
        }

        public override void Update(Camera cam, float deltaTime)
        {
            base.Update(cam, deltaTime);

            if (!Enabled) return;
            
            float dist = Vector2.DistanceSquared(cam.WorldViewCenter, WorldPosition);
            if (dist > 8000.0f * 8000.0f)
            {
                AnimController.SimplePhysicsEnabled = true;
            }
            else if (dist < 7000.0f * 7000.0f)
            {
                AnimController.SimplePhysicsEnabled = false;
            }

            if (IsDead || Health <= 0.0f || IsUnconscious || Stun > 0.0f) return;

            if (Controlled == this || !aiController.Enabled) return;

            if (soundTimer > 0)
            {
                soundTimer -= deltaTime;
            }
            else
            {
                switch (aiController.State)
                {
                    case AIController.AiState.Attack:
                        PlaySound(CharacterSound.SoundType.Attack);
                        break;
                    default:
                        PlaySound(CharacterSound.SoundType.Idle);
                        break;
                }
                soundTimer = soundInterval;
            }

            aiController.Update(deltaTime);
        }

        public override void DrawFront(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch,Camera cam)
        {
            base.DrawFront(spriteBatch,cam);

            if (GameMain.DebugDraw && !IsDead) aiController.DebugDraw(spriteBatch);
        }

        public override void AddDamage(CauseOfDeath causeOfDeath, float amount, IDamageable attacker)
        {
            base.AddDamage(causeOfDeath, amount, attacker);

            if (attacker!=null) aiController.OnAttacked(attacker, amount);
        }

        public override AttackResult AddDamage(IDamageable attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound = false)
        {
            AttackResult result = base.AddDamage(attacker, worldPosition, attack, deltaTime, playSound);

            aiController.OnAttacked(attacker, (result.Damage + result.Bleeding) / Math.Max(Health, 1.0f));

            return result;
        }
    }
}
