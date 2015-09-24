using Lidgren.Network;
using Microsoft.Xna.Framework;
using Subsurface.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Subsurface
{
    class AICharacter : Character
    {
        const float AttackBackPriority = 1.0f;

        private AIController aiController;

        public AICharacter(string file) : this(file, Vector2.Zero, null)
        {
        }

        public AICharacter(string file, Vector2 position)
            : this(file, position, null)
        {
        }

        public AICharacter(CharacterInfo characterInfo, WayPoint spawnPoint, bool isNetworkPlayer = false)
            : this(characterInfo.File, spawnPoint.SimPosition, characterInfo, isNetworkPlayer)
        {

        }

        public AICharacter(CharacterInfo characterInfo, Vector2 position, bool isNetworkPlayer = false)
            : this(characterInfo.File, position, characterInfo, isNetworkPlayer)
        {
        }

        public AICharacter(string file, Vector2 position, CharacterInfo characterInfo = null, bool isNetworkPlayer = false)
            : base(file, position, characterInfo, isNetworkPlayer)
        {
            aiController = new EnemyAIController(this, file);            
        }

        public override void Update(Camera cam, float deltaTime)
        {
            base.Update(cam, deltaTime);

            if (isDead) return;

            if (soundTimer > 0)
            {
                soundTimer -= deltaTime;
            }
            else
            {
                PlaySound((aiController == null) ? AIController.AiState.None : aiController.State);
                soundTimer = soundInterval;
            }

            aiController.Update(deltaTime);
        }

        public override AttackResult AddDamage(IDamageable attacker, Vector2 position, Attack attack, bool playSound = false)
        {
            AttackResult result = base.AddDamage(attacker, position, attack, playSound);

            aiController.OnAttacked(attacker, (result.Damage + result.Bleeding)/Math.Max(health,1.0f));

            return result;
        }

        public override void FillNetworkData(NetworkEventType type, NetOutgoingMessage message, object data)
        {
            if (type == NetworkEventType.KillCharacter)
            {
                return;
            }

            message.Write((float)NetTime.Now);

            message.Write(LargeUpdateTimer <= 0);
            
            message.Write(AnimController.TargetDir == Direction.Right);
            message.Write(AnimController.TargetMovement.X);
            message.Write(AnimController.TargetMovement.Y);
            
            if (LargeUpdateTimer <= 0)
            {
                int i = 0;
                foreach (Limb limb in AnimController.Limbs)
                {
                    message.Write(limb.body.SimPosition.X);
                    message.Write(limb.body.SimPosition.Y);

                    message.Write(limb.body.Rotation);
                    i++;
                }

                message.WriteRangedSingle(MathHelper.Clamp(AnimController.StunTimer, 0.0f, 60.0f), 0.0f, 60.0f, 8);
                message.Write((byte)((health / maxHealth) * 255.0f));

                aiController.FillNetworkData(message);

                LargeUpdateTimer = 10;
            }
            else
            {
                message.Write(AnimController.RefLimb.SimPosition.X);
                message.Write(AnimController.RefLimb.SimPosition.Y);

                LargeUpdateTimer = Math.Max(0, LargeUpdateTimer - 1);
            }
        }

        public override void ReadNetworkData(NetworkEventType type, NetIncomingMessage message)
        {
            if (type == NetworkEventType.KillCharacter)
            {
                Kill(true);
                return;
            }

            float sendingTime = 0.0f;
            Vector2 targetMovement  = Vector2.Zero;
            bool targetDir = false;

            bool isLargeUpdate;

            
            try
            {
                sendingTime = message.ReadFloat();
                isLargeUpdate = message.ReadBoolean();
            }

            catch
            {
                return;
            }

            if (sendingTime <= LastNetworkUpdate) return;

            try
            {
                targetDir = message.ReadBoolean();
                targetMovement.X = message.ReadFloat();
                targetMovement.Y = message.ReadFloat();
                
            }
            catch
            {
                return;
            }

            AnimController.TargetDir = (targetDir) ? Direction.Right : Direction.Left;
            AnimController.TargetMovement = targetMovement;


            if (isLargeUpdate)
            {
                foreach (Limb limb in AnimController.Limbs)
                {
                    Vector2 pos = Vector2.Zero, vel = Vector2.Zero;
                    float rotation = 0.0f;

                    try
                    {
                        pos.X = message.ReadFloat();
                        pos.Y = message.ReadFloat();

                        rotation = message.ReadFloat();
                    }
                    catch
                    {
                        return;
                    }

                    if (limb.body != null)
                    {
                        limb.body.TargetVelocity = limb.body.LinearVelocity;
                        limb.body.TargetPosition = pos;// +vel * (float)(deltaTime / 60.0);
                        limb.body.TargetRotation = rotation;// +angularVel * (float)(deltaTime / 60.0);
                        limb.body.TargetAngularVelocity = limb.body.AngularVelocity;
                    }
                }

                float newStunTimer = 0.0f, newHealth = 0.0f;

                try
                {
                    newStunTimer = message.ReadRangedSingle(0.0f, 60.0f, 8);
                    newHealth = (message.ReadByte() / 255.0f) * maxHealth;
                }
                catch { return; }

                AnimController.StunTimer = newStunTimer;
                Health = newHealth;

                LargeUpdateTimer = 1;

                aiController.ReadNetworkData(message);
            }
            else
            {
                Vector2 pos = Vector2.Zero;
                try
                {
                    pos.X = message.ReadFloat();
                    pos.Y = message.ReadFloat();
                }

                catch { return; }


                Limb torso = AnimController.GetLimb(LimbType.Torso);
                if (torso == null) torso = AnimController.GetLimb(LimbType.Head);
                torso.body.TargetPosition = pos;

                LargeUpdateTimer = 0;
            }

            LastNetworkUpdate = sendingTime;

        }


    }
}
