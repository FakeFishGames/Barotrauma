using Lidgren.Network;
using Microsoft.Xna.Framework;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FarseerPhysics;

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

            if (isDead) return;

            if (Controlled == this) return;

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

            if (GameMain.DebugDraw && !isDead) aiController.DebugDraw(spriteBatch);
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
                    //foreach (Limb limb in AnimController.Limbs)
                    //{
                        //if (RefLimb.ignoreCollisions) continue;

                        //if ((AnimController.RefLimb.SimPosition - Submarine.Loaded.SimPosition).Length() > NetConfig.CharacterIgnoreDistance) return false;

                        message.Write(AnimController.RefLimb.SimPosition.X);
                        message.Write(AnimController.RefLimb.SimPosition.Y);


                        message.Write(AnimController.RefLimb.Rotation);
                    //    i++;
                    //}

                    message.WriteRangedSingle(MathHelper.Clamp(AnimController.StunTimer, 0.0f, 60.0f), 0.0f, 60.0f, 8);
                    message.Write((byte)((health / maxHealth) * 255.0f));

                    aiController.FillNetworkData(message);
                    return true;
                case NetworkEventType.EntityUpdate:
                    //if (Submarine == null)
                    //{
                    //    if ((AnimController.RefLimb.SimPosition - Submarine.Loaded.SimPosition).Length() > NetConfig.CharacterIgnoreDistance) return false;
                    
                    //}
                    //else
                    //{
                    //    if (AnimController.RefLimb.SimPosition.Length() > NetConfig.CharacterIgnoreDistance) return false;                    
                    //}

                    
                    message.Write(AnimController.TargetDir == Direction.Right);
                    message.WriteRangedSingle(MathHelper.Clamp(AnimController.TargetMovement.X, -1.0f, 1.0f), -1.0f, 1.0f, 8);
                    message.WriteRangedSingle(MathHelper.Clamp(AnimController.TargetMovement.X, -1.0f, 1.0f), -1.0f, 1.0f, 8);
                    
                    message.Write(Submarine != null);

                    message.Write(AnimController.RefLimb.SimPosition.X);
                    message.Write(AnimController.RefLimb.SimPosition.Y);

                    return true;                    
            }
            
            return true;
        }

        public override void ReadNetworkData(NetworkEventType type, NetIncomingMessage message, float sendingTime, out object data)
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
                            limbPos.X = message.ReadFloat();
                            limbPos.Y = message.ReadFloat();
                            
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
                    Vector2 targetMovement  = Vector2.Zero;
                    bool targetDir = false;
                                                  
                    if (sendingTime <= LastNetworkUpdate) return;

                    bool inSub = false;

                    Vector2 pos = Vector2.Zero, vel = Vector2.Zero;

                    try
                    {
                        targetDir = message.ReadBoolean();
                        targetMovement.X = message.ReadRangedSingle(-1.0f, 1.0f, 8);
                        targetMovement.Y = message.ReadRangedSingle(-1.0f, 1.0f, 8);

                        inSub = message.ReadBoolean();

                        pos.X = message.ReadFloat();
                        pos.Y = message.ReadFloat();

                        //vel.X = message.ReadFloat();
                        //vel.Y = message.ReadFloat();
                
                    }
                    catch
                    {
                        return;
                    }

                    AnimController.TargetDir = (targetDir) ? Direction.Right : Direction.Left;
                    AnimController.TargetMovement = targetMovement;
                        
                    AnimController.TargetMovement = AnimController.EstimateCurrPosition(pos, (float)(NetTime.Now) - sendingTime);
                            

                    if (inSub)
                    {
                        Hull newHull = Hull.FindHull(ConvertUnits.ToDisplayUnits(pos), AnimController.CurrentHull, false);
                        if (newHull != null)
                        {
                            AnimController.CurrentHull = newHull;
                            Submarine = newHull.Submarine;
                        }
                    }
                      
                    LastNetworkUpdate = sendingTime;
                    return;
            }
        }


    }
}
