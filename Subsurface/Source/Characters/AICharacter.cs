using Lidgren.Network;
using Microsoft.Xna.Framework;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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


            if (GameMain.Client != null && GameMain.Server == null) Enabled = false;
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

        public override void DrawFront(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            base.DrawFront(spriteBatch);

            if (GameMain.DebugDraw) aiController.DebugDraw(spriteBatch);
        }

        public override AttackResult AddDamage(IDamageable attacker, Vector2 position, Attack attack, float deltaTime, bool playSound = false)
        {
            AttackResult result = base.AddDamage(attacker, position, attack, deltaTime, playSound);

            aiController.OnAttacked(attacker, (result.Damage + result.Bleeding)/Math.Max(health,1.0f));

            return result;
        }

        public override bool FillNetworkData(NetworkEventType type, NetBuffer message, object data)
        {
            switch (type)
            {
                case NetworkEventType.KillCharacter:
                    return true;
                case NetworkEventType.ImportantEntityUpdate:
                    int i = 0;
                    //foreach (Limb limb in AnimController.Limbs)
                    //{
                        //if (RefLimb.ignoreCollisions) continue;

                        if (AnimController.RefLimb.SimPosition.Length() > NetConfig.CharacterIgnoreDistance) return false;

                        message.WriteRangedSingle(AnimController.RefLimb.SimPosition.X, -NetConfig.CharacterIgnoreDistance, NetConfig.CharacterIgnoreDistance, 16);
                        message.WriteRangedSingle(AnimController.RefLimb.SimPosition.Y, -NetConfig.CharacterIgnoreDistance, NetConfig.CharacterIgnoreDistance, 16);


                        message.Write(AnimController.RefLimb.Rotation);
                    //    i++;
                    //}

                    message.WriteRangedSingle(MathHelper.Clamp(AnimController.StunTimer, 0.0f, 60.0f), 0.0f, 60.0f, 8);
                    message.Write((byte)((health / maxHealth) * 255.0f));

                    aiController.FillNetworkData(message);
                    return true;
                case NetworkEventType.EntityUpdate:
                    if (AnimController.RefLimb.SimPosition.Length() > NetConfig.CharacterIgnoreDistance) return false;

                    message.Write((float)NetTime.Now);

                    message.Write(AnimController.TargetDir == Direction.Right);
                    message.WriteRangedSingle(MathHelper.Clamp(AnimController.TargetMovement.X, -1.0f, 1.0f), -1.0f, 1.0f, 8);
                    message.WriteRangedSingle(MathHelper.Clamp(AnimController.TargetMovement.X, -1.0f, 1.0f), -1.0f, 1.0f, 8);
            
                    message.WriteRangedSingle(AnimController.RefLimb.SimPosition.X, -NetConfig.CharacterIgnoreDistance, NetConfig.CharacterIgnoreDistance, 16);
                    message.WriteRangedSingle(AnimController.RefLimb.SimPosition.Y, -NetConfig.CharacterIgnoreDistance, NetConfig.CharacterIgnoreDistance, 16);

                    return true;                    
            }
            
            return true;
        }
        
        public override void ReadNetworkData(NetworkEventType type, NetIncomingMessage message, out object data)
        {
            data = null;
            Enabled = true;

            switch (type)
            {
                case NetworkEventType.KillCharacter:
                    Kill(CauseOfDeath.Damage, true);
                    return;
                case NetworkEventType.ImportantEntityUpdate:
                    //foreach (Limb limb in AnimController.Limbs)
                    //{
                    //    if (limb.ignoreCollisions) continue;

                        Vector2 limbPos = AnimController.RefLimb.SimPosition;
                        float rotation = AnimController.RefLimb.Rotation;

                        try
                        {
                            limbPos.X = message.ReadRangedSingle(-NetConfig.CharacterIgnoreDistance, NetConfig.CharacterIgnoreDistance, 16);
                            limbPos.Y = message.ReadRangedSingle(-NetConfig.CharacterIgnoreDistance, NetConfig.CharacterIgnoreDistance, 16);
                            
                            rotation = message.ReadFloat();
                        }
                        catch
                        {
                            return;
                        }

                        if (AnimController.RefLimb.body != null)
                        {
                            //AnimController.RefLimb.body.TargetVelocity = limb.body.LinearVelocity;
                            AnimController.RefLimb.body.TargetPosition = limbPos;// +vel * (float)(deltaTime / 60.0);
                            AnimController.RefLimb.body.TargetRotation = rotation;// +angularVel * (float)(deltaTime / 60.0);
                            //limb.body.TargetAngularVelocity = limb.body.AngularVelocity;
                        }
                    //}

                    float newStunTimer = 0.0f, newHealth = 0.0f;

                    try
                    {
                        newStunTimer = message.ReadRangedSingle(0.0f, 60.0f, 8);
                        newHealth = (message.ReadByte() / 255.0f) * maxHealth;
                    }
                    catch { return; }

                    AnimController.StunTimer = newStunTimer;
                    health = newHealth;

                    aiController.ReadNetworkData(message);
                    return;
                case NetworkEventType.EntityUpdate:
                    float sendingTime = 0.0f;
                    Vector2 targetMovement  = Vector2.Zero;
                    bool targetDir = false;
                                
                    sendingTime = message.ReadFloat();                    
                    if (sendingTime <= LastNetworkUpdate) return;

                    Vector2 pos = Vector2.Zero, vel = Vector2.Zero;

                    try
                    {
                        targetDir = message.ReadBoolean();
                        targetMovement.X = message.ReadRangedSingle(-1.0f, 1.0f, 8);
                        targetMovement.Y = message.ReadRangedSingle(-1.0f, 1.0f, 8);

                        pos.X = message.ReadRangedSingle(-NetConfig.CharacterIgnoreDistance, NetConfig.CharacterIgnoreDistance, 16);
                        pos.Y = message.ReadRangedSingle(-NetConfig.CharacterIgnoreDistance, NetConfig.CharacterIgnoreDistance, 16);

                        //vel.X = message.ReadFloat();
                        //vel.Y = message.ReadFloat();
                
                    }
                    catch (Exception e)
                    {
                        return;
                    }

                    AnimController.TargetDir = (targetDir) ? Direction.Right : Direction.Left;
                    AnimController.TargetMovement = targetMovement;
        
                    AnimController.RefLimb.body.TargetPosition = pos;
                    AnimController.RefLimb.body.TargetVelocity = vel;
                      
                    LastNetworkUpdate = sendingTime;
                    return;
            }
        }


    }
}
