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
            if (!Enabled) return;

            base.Update(cam, deltaTime);
            
            float dist = Vector2.Distance(cam.WorldViewCenter, WorldPosition);
            if (dist > 8000.0f)
            {
                AnimController.SimplePhysicsEnabled = true;
            }
            else if (dist < 7000.0f)
            {
                AnimController.SimplePhysicsEnabled = false;
            }

            if (isDead || health <= 0.0f) return;

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

        public override void AddDamage(CauseOfDeath causeOfDeath, float amount, IDamageable attacker)
        {
            base.AddDamage(causeOfDeath, amount, attacker);

            if (attacker!=null) aiController.OnAttacked(attacker, amount);
        }

        public override AttackResult AddDamage(IDamageable attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound = false)
        {
            AttackResult result = base.AddDamage(attacker, worldPosition, attack, deltaTime, playSound);

            aiController.OnAttacked(attacker, (result.Damage + result.Bleeding) / Math.Max(health,1.0f));

            return result;
        }

        public override bool FillNetworkData(NetworkEventType type, NetBuffer message, object data)
        {
            switch (type)
            {
                case NetworkEventType.KillCharacter:
                    return true;
                case NetworkEventType.ImportantEntityUpdate:

                    message.Write(AnimController.RefLimb.SimPosition.X);
                    message.Write(AnimController.RefLimb.SimPosition.Y);

                    //message.Write(AnimController.RefLimb.Rotation);

                    message.Write((byte)((health / maxHealth) * 255.0f));

                    message.Write(AnimController.StunTimer > 0.0f);
                    if (AnimController.StunTimer > 0.0f)
                    {
                        message.WriteRangedSingle(MathHelper.Clamp(AnimController.StunTimer, 0.0f, 60.0f), 0.0f, 60.0f, 8);
                    }                   

                    if (DoesBleed)
                    {
                        Bleeding = MathHelper.Clamp(Bleeding, 0.0f, 5.0f);
                        message.WriteRangedSingle(Bleeding, 0.0f, 5.0f, 8);
                    }

                    aiController.FillNetworkData(message);
                    return true;
                case NetworkEventType.EntityUpdate:

                    message.Write(AnimController.Dir > 0.0f);
                    message.WriteRangedSingle(MathHelper.Clamp(AnimController.TargetMovement.X, -1.0f, 1.0f), -1.0f, 1.0f, 4);
                    message.WriteRangedSingle(MathHelper.Clamp(AnimController.TargetMovement.Y, -1.0f, 1.0f), -1.0f, 1.0f, 4);
                    
                    if (AnimController.CanEnterSubmarine) message.Write(Submarine != null);
                                        
                    message.Write(AnimController.RefLimb.SimPosition.X);
                    message.Write(AnimController.RefLimb.SimPosition.Y);

                    return true;
                case NetworkEventType.InventoryUpdate:
                    return base.FillNetworkData(type, message, data);
                default:
#if DEBUG
                    DebugConsole.ThrowError("AICharacter network event had a wrong type ("+type+")");
#endif
                    return false;
            }
        }

        public override bool ReadNetworkData(NetworkEventType type, NetIncomingMessage message, float sendingTime, out object data)
        {
            data = null;
            Enabled = true;

            //server doesn't accept AICharacter updates from the clients
            if (GameMain.Server != null) return false;

            switch (type)
            {
                case NetworkEventType.KillCharacter:

                    Kill(CauseOfDeath.Damage, true);
                    break;
                case NetworkEventType.InventoryUpdate:
                    return base.ReadNetworkData(type, message, sendingTime, out data);
                case NetworkEventType.ImportantEntityUpdate:

                    Vector2 limbPos = AnimController.RefLimb.SimPosition;
                    float rotation = AnimController.RefLimb.Rotation;

                    try
                    {
                        limbPos.X = message.ReadFloat();
                        limbPos.Y = message.ReadFloat();
                            
                        //rotation = message.ReadFloat();
                    }
                    catch (Exception e)
                    {
#if DEBUG
                        DebugConsole.ThrowError("Failed to read AICharacter update message", e);
#endif
                        return false;
                    }

                    if (AnimController.RefLimb.body != null)
                    {
                        AnimController.RefLimb.body.TargetPosition = limbPos;
                        //AnimController.RefLimb.body.TargetRotation = rotation;
                    }

                    float newStunTimer = 0.0f, newHealth = 0.0f, newBleeding = 0.0f;

                    try
                    {
                        newHealth = (message.ReadByte() / 255.0f) * maxHealth;

                        if (message.ReadBoolean())
                        {
                            newStunTimer = message.ReadRangedSingle(0.0f, 60.0f, 8);
                        }

                        if (DoesBleed)
                        {
                            newBleeding = message.ReadRangedSingle(0.0f, 5.0f, 8);
                        }
                    }
                    catch (Exception e)
                    {
#if DEBUG
                        DebugConsole.ThrowError("Failed to read AICharacter update message", e);
#endif

                        return false;
                    }

                    AnimController.StunTimer = newStunTimer;
                    health = newHealth;

                    Bleeding = newBleeding;

                    aiController.ReadNetworkData(message);
                    break;
                case NetworkEventType.EntityUpdate:
                    if (sendingTime <= LastNetworkUpdate) return false;

                    Vector2 targetMovement  = Vector2.Zero, pos = Vector2.Zero;
                    bool targetDir = false,inSub = false;
                    
                    try
                    {
                        targetDir = message.ReadBoolean();
                        targetMovement.X = message.ReadRangedSingle(-1.0f, 1.0f, 4);
                        targetMovement.Y = message.ReadRangedSingle(-1.0f, 1.0f, 4);

                        if (AnimController.CanEnterSubmarine) inSub = message.ReadBoolean();

                        pos.X = message.ReadFloat();
                        pos.Y = message.ReadFloat();                
                    }
                    catch (Exception e)
                    {
#if DEBUG
                        DebugConsole.ThrowError("Failed to read AICharacter update message", e);
#endif
                        return false;
                    }

                    AnimController.TargetDir = (targetDir) ? Direction.Right : Direction.Left;
                    AnimController.TargetMovement = targetMovement;

                    AnimController.RefLimb.body.TargetPosition = pos;
                        //AnimController.EstimateCurrPosition(pos, (float)(NetTime.Now) - sendingTime);                            

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
                    break;
            }

            return true;
        }


    }
}
