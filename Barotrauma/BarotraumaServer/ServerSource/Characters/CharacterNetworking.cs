using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Character
    {
        public string OwnerClientEndPoint;
        public string OwnerClientName;
        public bool ClientDisconnected;
        public float KillDisconnectedTimer;

        private bool networkUpdateSent;

        private double LastInputTime;

        public float GetPositionUpdateInterval(Client recipient)
        {
            if (!Enabled) { return 1000.0f; }

            Vector2 comparePosition = recipient.SpectatePos == null ? recipient.Character.WorldPosition : recipient.SpectatePos.Value;

            float distance = Vector2.Distance(comparePosition, WorldPosition);
            float priority = 1.0f - MathUtils.InverseLerp(
                NetConfig.HighPrioCharacterPositionUpdateDistance, 
                NetConfig.LowPrioCharacterPositionUpdateDistance,
                distance);

            float interval = MathHelper.Lerp(
                NetConfig.LowPrioCharacterPositionUpdateInterval, 
                NetConfig.HighPrioCharacterPositionUpdateInterval,
                priority);

            if (IsDead)
            {
                interval = Math.Max(interval * 2, 0.1f);
            }

            return interval;
        }

        partial void UpdateNetInput()
        {
            if (!(this is AICharacter) || IsRemotePlayer)
            {
                if (!CanMove)
                {
                    AnimController.Frozen = false;
                    if (memInput.Count > 0)
                    {
                        prevDequeuedInput = dequeuedInput;
                        dequeuedInput = memInput[memInput.Count - 1].states & InputNetFlags.Ragdoll;
                        memInput.RemoveAt(memInput.Count - 1);
                    }
                }
                else if (memInput.Count == 0)
                {
                    AnimController.Frozen = true;
                    if (Timing.TotalTime > LastInputTime + 0.5)
                    {
                        //no inputs have been received in 0.5 seconds, reset input
                        //(if there's a temporary network hiccup that prevents us from receiving inputs, we assume the inputs haven't changed,
                        //but if it takes too long, for example due to a client crashing/disconnecting, we don't want to keep the character
                        //firing a welding tool or whatever else they were doing until the kill disconnect timer kicks in)
                        prevDequeuedInput = dequeuedInput =
                            dequeuedInput.HasFlag(InputNetFlags.FacingLeft) ? InputNetFlags.FacingLeft : InputNetFlags.None;
                    }
                }
                else
                {
                    AnimController.Frozen = false;
                    prevDequeuedInput = dequeuedInput;

                    LastProcessedID = memInput[memInput.Count - 1].networkUpdateID;
                    dequeuedInput = memInput[memInput.Count - 1].states;

                    double aimAngle = ((double)memInput[memInput.Count - 1].intAim / 65535.0) * 2.0 * Math.PI;
                    cursorPosition = AimRefPosition + new Vector2((float)Math.Cos(aimAngle), (float)Math.Sin(aimAngle)) * 500.0f;

                    //reset focus when attempting to use/select something
                    if (memInput[memInput.Count - 1].states.HasFlag(InputNetFlags.Use) ||
                        memInput[memInput.Count - 1].states.HasFlag(InputNetFlags.Select) ||
                        memInput[memInput.Count - 1].states.HasFlag(InputNetFlags.Deselect) ||
                        memInput[memInput.Count - 1].states.HasFlag(InputNetFlags.Health) ||
                        memInput[memInput.Count - 1].states.HasFlag(InputNetFlags.Grab))
                    {
                        focusedItem = null;
                        FocusedCharacter = null;
                    }
                    var closestEntity = FindEntityByID(memInput[memInput.Count - 1].interact);
                    if (closestEntity is Item)
                    {
                        if (CanInteractWith((Item)closestEntity))
                        {
                            focusedItem = (Item)closestEntity;
                            FocusedCharacter = null;
                        }
                    }
                    else if (closestEntity is Character)
                    {
                        if (CanInteractWith((Character)closestEntity))
                        {
                            FocusedCharacter = (Character)closestEntity;
                            focusedItem = null;
                        }
                    }

                    memInput.RemoveAt(memInput.Count - 1);

                    TransformCursorPos();

                    if ((dequeuedInput == InputNetFlags.None || dequeuedInput == InputNetFlags.FacingLeft) && Math.Abs(AnimController.Collider.LinearVelocity.X) < 0.005f && Math.Abs(AnimController.Collider.LinearVelocity.Y) < 0.2f)
                    {
                        while (memInput.Count > 5 && memInput[memInput.Count - 1].states == dequeuedInput)
                        {
                            //remove inputs where the player is not moving at all
                            //helps the server catch up, shouldn't affect final position
                            LastProcessedID = memInput[memInput.Count - 1].networkUpdateID;
                            memInput.RemoveAt(memInput.Count - 1);
                        }
                    }
                }
            }
            AnimController.Frozen = false;

            if (networkUpdateSent)
            {
                foreach (Key key in keys)
                {
                    key.DequeueHit();
                    key.DequeueHeld();
                }

                networkUpdateSent = false;
            }
        }

        public virtual void ServerRead(ClientNetObject type, IReadMessage msg, Client c)
        {
            if (GameMain.Server == null) return;

            switch (type)
            {
                case ClientNetObject.CHARACTER_INPUT:

                    if (c.Character != this)
                    {
#if DEBUG
                        DebugConsole.Log("Received a character update message from a client who's not controlling the character");
#endif
                        return;
                    }

                    UInt16 networkUpdateID = msg.ReadUInt16();
                    byte inputCount = msg.ReadByte();

                    if (AllowInput) { Enabled = true; }

                    for (int i = 0; i < inputCount; i++)
                    {
                        InputNetFlags newInput = (InputNetFlags)msg.ReadRangedInteger(0, (int)InputNetFlags.MaxVal);
                        UInt16 newAim = 0;
                        UInt16 newInteract = 0;

                        if (newInput != InputNetFlags.None && newInput != InputNetFlags.FacingLeft)
                        {
                            c.KickAFKTimer = 0.0f;
                        }
                        else if (AnimController.Dir < 0.0f != newInput.HasFlag(InputNetFlags.FacingLeft))
                        {
                            //character changed the direction they're facing
                            c.KickAFKTimer = 0.0f;
                        }
                        
                        newAim = msg.ReadUInt16();                        
                        if (newInput.HasFlag(InputNetFlags.Select) ||
                            newInput.HasFlag(InputNetFlags.Deselect) ||
                            newInput.HasFlag(InputNetFlags.Use) ||
                            newInput.HasFlag(InputNetFlags.Health) ||
                            newInput.HasFlag(InputNetFlags.Grab))
                        {
                            newInteract = msg.ReadUInt16();
                        }
                        
                        if (NetIdUtils.IdMoreRecent((ushort)(networkUpdateID - i), LastNetworkUpdateID) && (i < 60))
                        {
                            if ((i > 0 && memInput[i - 1].intAim != newAim))
                            {
                                c.KickAFKTimer = 0.0f;
                            }
                            NetInputMem newMem = new NetInputMem
                            {
                                states = newInput,
                                intAim = newAim,
                                interact = newInteract,
                                networkUpdateID = (ushort)(networkUpdateID - i)
                            };
                            memInput.Insert(i, newMem);
                            LastInputTime = Timing.TotalTime;
                        }
                    }

                    if (NetIdUtils.IdMoreRecent(networkUpdateID, LastNetworkUpdateID))
                    {
                        LastNetworkUpdateID = networkUpdateID;
                    }
                    if (memInput.Count > 60)
                    {
                        //deleting inputs from the queue here means the server is way behind and data needs to be dropped
                        //we'll make the server drop down to 30 inputs for good measure
                        memInput.RemoveRange(30, memInput.Count - 30);
                    }
                    break;

                case ClientNetObject.ENTITY_STATE:
                    int eventType = msg.ReadRangedInteger(0, 3);
                    switch (eventType)
                    {
                        case 0:
                            Inventory.ServerRead(type, msg, c);
                            break;
                        case 1:
                            bool doingCPR = msg.ReadBoolean();
                            if (c.Character != this)
                            {
#if DEBUG
                                DebugConsole.Log("Received a character update message from a client who's not controlling the character");
#endif
                                return;
                            }

                            AnimController.Anim = doingCPR ? AnimController.Animation.CPR : AnimController.Animation.None;
                            break;
                        case 2:
                            if (c.Character != this)
                            {
#if DEBUG
                                DebugConsole.Log("Received a character update message from a client who's not controlling the character");
#endif
                                return;
                            }

                            if (IsIncapacitated)
                            {
                                var causeOfDeath = CharacterHealth.GetCauseOfDeath();
                                Kill(causeOfDeath.First, causeOfDeath.Second);
                            }
                            break;
                    }
                    break;
            }
            msg.ReadPadBits();
        }

        public virtual void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            if (GameMain.Server == null) return;

            if (extraData != null)
            {
                switch ((NetEntityEvent.Type)extraData[0])
                {
                    case NetEntityEvent.Type.InventoryState:
                        msg.WriteRangedInteger(0, 0, 4);
                        msg.Write(GameMain.Server.EntityEventManager.Events.Last()?.ID ?? (ushort)0);
                        Inventory.ServerWrite(msg, c);
                        break;
                    case NetEntityEvent.Type.Control:
                        msg.WriteRangedInteger(1, 0, 4);
                        Client owner = (Client)extraData[1];
                        msg.Write(owner != null && owner.Character == this && GameMain.Server.ConnectedClients.Contains(owner) ? owner.ID : (byte)0);
                        break;
                    case NetEntityEvent.Type.Status:
                        msg.WriteRangedInteger(2, 0, 4);
                        WriteStatus(msg);
                        break;
                    case NetEntityEvent.Type.UpdateSkills:
                        msg.WriteRangedInteger(3, 0, 4);
                        if (Info?.Job == null)
                        {
                            msg.Write((byte)0);
                        }
                        else
                        {
                            msg.Write((byte)Info.Job.Skills.Count);
                            foreach (Skill skill in Info.Job.Skills)
                            {
                                msg.Write(skill.Identifier);
                                msg.Write(skill.Level);
                            }
                        }
                        break;
                    case NetEntityEvent.Type.ExecuteAttack:
                        Limb attackLimb = extraData[1] as Limb;
                        UInt16 targetEntityID = (UInt16)extraData[2];
                        int targetLimbIndex = extraData.Length > 3 ? (int)extraData[3] : 0;
                        msg.WriteRangedInteger(4, 0, 4);
                        msg.Write((byte)(Removed ? 255 : Array.IndexOf(AnimController.Limbs, attackLimb)));
                        msg.Write(targetEntityID);
                        msg.Write((byte)targetLimbIndex);
                        break;
                    default:
                        DebugConsole.ThrowError("Invalid NetworkEvent type for entity " + ToString() + " (" + (NetEntityEvent.Type)extraData[0] + ")");
                        break;
                }
                msg.WritePadBits();
            }
            else
            {
                msg.Write(ID);

                IWriteMessage tempBuffer = new WriteOnlyMessage();

                if (this == c.Character)
                {
                    tempBuffer.Write(true);
                    if (LastNetworkUpdateID < memInput.Count + 1)
                    {
                        tempBuffer.Write((UInt16)0);
                    }
                    else
                    {
                        tempBuffer.Write((UInt16)(LastNetworkUpdateID - memInput.Count - 1));
                    }
                }
                else
                {
                    tempBuffer.Write(false);

                    bool aiming = false;
                    bool use = false;
                    bool attack = false;
                    bool shoot = false;

                    if (IsRemotePlayer)
                    {
                        aiming  = dequeuedInput.HasFlag(InputNetFlags.Aim);
                        use     = dequeuedInput.HasFlag(InputNetFlags.Use);
                        attack  = dequeuedInput.HasFlag(InputNetFlags.Attack);
                        shoot   = dequeuedInput.HasFlag(InputNetFlags.Shoot);
                    }
                    else if (keys != null)
                    {
                        aiming  = keys[(int)InputType.Aim].GetHeldQueue;
                        use     = keys[(int)InputType.Use].GetHeldQueue;
                        attack  = keys[(int)InputType.Attack].GetHeldQueue;
                        shoot   = keys[(int)InputType.Shoot].GetHeldQueue;
                        networkUpdateSent = true;
                    }

                    tempBuffer.Write(aiming);
                    tempBuffer.Write(shoot);
                    tempBuffer.Write(use);
                    if (AnimController is HumanoidAnimController)
                    {
                        tempBuffer.Write(((HumanoidAnimController)AnimController).Crouching);
                    }
                    tempBuffer.Write(attack);
                    
                    Vector2 relativeCursorPos = cursorPosition - AimRefPosition;
                    tempBuffer.Write((UInt16)(65535.0 * Math.Atan2(relativeCursorPos.Y, relativeCursorPos.X) / (2.0 * Math.PI)));
                    
                    tempBuffer.Write(IsRagdolled || Stun > 0.0f || IsDead || IsIncapacitated);

                    tempBuffer.Write(AnimController.Dir > 0.0f);
                }

                if (SelectedCharacter != null || SelectedConstruction != null)
                {
                    tempBuffer.Write(true);
                    tempBuffer.Write(SelectedCharacter != null ? SelectedCharacter.ID : NullEntityID);
                    tempBuffer.Write(SelectedConstruction != null ? SelectedConstruction.ID : NullEntityID);
                    if (SelectedCharacter != null)
                    {
                        tempBuffer.Write(AnimController.Anim == AnimController.Animation.CPR);
                    }
                }
                else
                {
                    tempBuffer.Write(false);
                }

                tempBuffer.Write(SimPosition.X);
                tempBuffer.Write(SimPosition.Y);
                float MaxVel = NetConfig.MaxPhysicsBodyVelocity;
                AnimController.Collider.LinearVelocity = new Vector2(
                    MathHelper.Clamp(AnimController.Collider.LinearVelocity.X, -MaxVel, MaxVel),
                    MathHelper.Clamp(AnimController.Collider.LinearVelocity.Y, -MaxVel, MaxVel));
                tempBuffer.WriteRangedSingle(AnimController.Collider.LinearVelocity.X, -MaxVel, MaxVel, 12);
                tempBuffer.WriteRangedSingle(AnimController.Collider.LinearVelocity.Y, -MaxVel, MaxVel, 12);

                bool fixedRotation = AnimController.Collider.FarseerBody.FixedRotation || !AnimController.Collider.PhysEnabled;
                tempBuffer.Write(fixedRotation);
                if (!fixedRotation)
                {
                    tempBuffer.Write(AnimController.Collider.Rotation);
                    float MaxAngularVel = NetConfig.MaxPhysicsBodyAngularVelocity;
                    AnimController.Collider.AngularVelocity = NetConfig.Quantize(AnimController.Collider.AngularVelocity, -MaxAngularVel, MaxAngularVel, 8);
                    tempBuffer.WriteRangedSingle(MathHelper.Clamp(AnimController.Collider.AngularVelocity, -MaxAngularVel, MaxAngularVel), -MaxAngularVel, MaxAngularVel, 8);
                }

                bool writeStatus = healthUpdateTimer <= 0.0f;
                tempBuffer.Write(writeStatus);
                if (writeStatus)
                {
                    WriteStatus(tempBuffer);
                }

                tempBuffer.WritePadBits();

                msg.WriteVariableUInt32((uint)tempBuffer.LengthBytes);
                msg.Write(tempBuffer.Buffer, 0, tempBuffer.LengthBytes);
            }
        }

        private void WriteStatus(IWriteMessage msg)
        {
            msg.Write(IsDead);
            if (IsDead)
            {
                msg.WriteRangedInteger((int)CauseOfDeath.Type, 0, Enum.GetValues(typeof(CauseOfDeathType)).Length - 1);
                if (CauseOfDeath.Type == CauseOfDeathType.Affliction)
                {
                    msg.Write(CauseOfDeath.Affliction.Identifier);
                }

                if (AnimController?.LimbJoints == null)
                {
                    //0 limbs severed
                    msg.Write((byte)0);
                }
                else
                {
                    List<int> severedJointIndices = new List<int>();
                    for (int i = 0; i < AnimController.LimbJoints.Length; i++)
                    {
                        if (AnimController.LimbJoints[i] != null && AnimController.LimbJoints[i].IsSevered)
                        {
                            severedJointIndices.Add(i);
                        }
                    }
                    msg.Write((byte)severedJointIndices.Count);
                    foreach (int jointIndex in severedJointIndices)
                    {
                        msg.Write((byte)jointIndex);
                    }
                }
            }
            else
            {
                CharacterHealth.ServerWrite(msg);
            }
        }

        public void WriteSpawnData(IWriteMessage msg, UInt16 entityId, bool restrictMessageSize)
        {
            if (GameMain.Server == null) return;
            
            int msgLength = msg.LengthBytes;

            msg.Write(Info == null);
            msg.Write(entityId);
            msg.Write(SpeciesName);
            msg.Write(seed);

            if (Removed)
            {
                msg.Write(0.0f);
                msg.Write(0.0f);
            }
            else
            {
                msg.Write(WorldPosition.X);
                msg.Write(WorldPosition.Y);
            }

            msg.Write(Enabled);

            //character with no characterinfo (e.g. some monster)
            if (Info == null)
            {
                TryWriteStatus(msg);
                return;
            }

            Client ownerClient = GameMain.Server.ConnectedClients.Find(c => c.Character == this);
            if (ownerClient != null)
            {
                msg.Write(true);
                msg.Write(ownerClient.ID);
            }
            else if (GameMain.Server.Character == this)
            {
                msg.Write(true);
                msg.Write((byte)0);
            }
            else
            {
                msg.Write(false);
            }

            msg.Write((byte)TeamID);
            msg.Write(this is AICharacter);
            msg.Write(info.SpeciesName);
            info.ServerWrite(msg);

            // Current order
            if (info.CurrentOrder != null)
            {
                msg.Write(true);
                msg.Write((byte)Order.PrefabList.IndexOf(info.CurrentOrder.Prefab));
                msg.Write(info.CurrentOrder.TargetEntity == null ? (UInt16)0 :
                    info.CurrentOrder.TargetEntity.ID);
                if (info.CurrentOrder.OrderGiver != null)
                {
                    msg.Write(true);
                    msg.Write(info.CurrentOrder.OrderGiver.ID);
                }
                else
                {
                    msg.Write(false);
                }
                msg.Write((byte)(string.IsNullOrWhiteSpace(info.CurrentOrderOption) ? 0 :
                    Array.IndexOf(info.CurrentOrder.Prefab.Options, info.CurrentOrderOption)));
            }
            else
            {
                msg.Write(false);
            }

            TryWriteStatus(msg);

            void TryWriteStatus(IWriteMessage msg)
            {
                var tempBuffer = new ReadWriteMessage();
                WriteStatus(tempBuffer);
                if (msg.LengthBytes + tempBuffer.LengthBytes >= 255 && restrictMessageSize)
                { 
                    msg.Write(false);
                    DebugConsole.ThrowError($"Error when writing character spawn data: status data caused the length of the message to exceed 255 bytes ({msg.LengthBytes} + {tempBuffer.LengthBytes})");
                }
                else
                {
                    msg.Write(true);
                    WriteStatus(msg);
                }
            }

            DebugConsole.Log("Character spawn message length: " + (msg.LengthBytes - msgLength));
        }
    }
}
