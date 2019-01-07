using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Character
    {
        partial void UpdateNetInput()
        {
            if (!(this is AICharacter) || IsRemotePlayer)
            {
                if (!AllowInput)
                {
                    AnimController.Frozen = false;
                    if (memInput.Count > 0)
                    {
                        prevDequeuedInput = dequeuedInput;
                        dequeuedInput = memInput[memInput.Count - 1].states;
                        memInput.RemoveAt(memInput.Count - 1);
                    }
                }
                else if (memInput.Count == 0)
                {
                    AnimController.Frozen = true;
                }
                else
                {
                    AnimController.Frozen = false;
                    prevDequeuedInput = dequeuedInput;

                    LastProcessedID = memInput[memInput.Count - 1].networkUpdateID;
                    dequeuedInput = memInput[memInput.Count - 1].states;

                    double aimAngle = ((double)memInput[memInput.Count - 1].intAim / 65535.0) * 2.0 * Math.PI;
                    cursorPosition = AimRefPosition + new Vector2((float)Math.Cos(aimAngle), (float)Math.Sin(aimAngle)) * 60.0f;

                    //reset focus when attempting to use/select something
                    if (memInput[memInput.Count - 1].states.HasFlag(InputNetFlags.Use) ||
                        memInput[memInput.Count - 1].states.HasFlag(InputNetFlags.Select) ||
                        memInput[memInput.Count - 1].states.HasFlag(InputNetFlags.Health) ||
                        memInput[memInput.Count - 1].states.HasFlag(InputNetFlags.Grab))
                    {
                        focusedItem = null;
                        focusedCharacter = null;
                    }
                    var closestEntity = FindEntityByID(memInput[memInput.Count - 1].interact);
                    if (closestEntity is Item)
                    {
                        if (CanInteractWith((Item)closestEntity))
                        {
                            focusedItem = (Item)closestEntity;
                            focusedCharacter = null;
                        }
                    }
                    else if (closestEntity is Character)
                    {
                        if (CanInteractWith((Character)closestEntity))
                        {
                            focusedCharacter = (Character)closestEntity;
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

        public virtual void ServerRead(ClientNetObject type, NetBuffer msg, Client c)
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

                    if (AllowInput) Enabled = true;

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

                        if (newInput.HasFlag(InputNetFlags.Aim))
                        {
                            newAim = msg.ReadUInt16();
                        }
                        if (newInput.HasFlag(InputNetFlags.Select) ||
                            newInput.HasFlag(InputNetFlags.Use) ||
                            newInput.HasFlag(InputNetFlags.Health) ||
                            newInput.HasFlag(InputNetFlags.Grab))
                        {
                            newInteract = msg.ReadUInt16();
                        }

                        //if (AllowInput)
                        //{
                        if (NetIdUtils.IdMoreRecent((ushort)(networkUpdateID - i), LastNetworkUpdateID) && (i < 60))
                        {
                            NetInputMem newMem = new NetInputMem();
                            newMem.states = newInput;
                            newMem.intAim = newAim;
                            newMem.interact = newInteract;

                            newMem.networkUpdateID = (ushort)(networkUpdateID - i);

                            memInput.Insert(i, newMem);
                        }
                        //}
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

                            if (IsUnconscious)
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

        public virtual void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            if (GameMain.Server == null) return;

            if (extraData != null)
            {
                switch ((NetEntityEvent.Type)extraData[0])
                {
                    case NetEntityEvent.Type.InventoryState:
                        msg.WriteRangedInteger(0, 3, 0);
                        Inventory.ClientWrite(msg, extraData);
                        break;
                    case NetEntityEvent.Type.Control:
                        msg.WriteRangedInteger(0, 3, 1);
                        Client owner = ((Client)extraData[1]);
                        msg.Write(owner == null ? (byte)0 : owner.ID);
                        break;
                    case NetEntityEvent.Type.Status:
                        msg.WriteRangedInteger(0, 3, 2);
                        WriteStatus(msg);
                        break;
                    case NetEntityEvent.Type.UpdateSkills:
                        msg.WriteRangedInteger(0, 3, 3);
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
                    default:
                        DebugConsole.ThrowError("Invalid NetworkEvent type for entity " + ToString() + " (" + (NetEntityEvent.Type)extraData[0] + ")");
                        break;
                }
                msg.WritePadBits();
            }
            else
            {
                msg.Write(ID);

                NetBuffer tempBuffer = new NetBuffer();

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

                    if (IsRemotePlayer)
                    {
                        aiming = dequeuedInput.HasFlag(InputNetFlags.Aim);
                        use = dequeuedInput.HasFlag(InputNetFlags.Use);

                        attack = dequeuedInput.HasFlag(InputNetFlags.Attack);
                    }
                    else if (keys != null)
                    {
                        aiming = keys[(int)InputType.Aim].GetHeldQueue;
                        use = keys[(int)InputType.Use].GetHeldQueue;

                        attack = keys[(int)InputType.Attack].GetHeldQueue;

                        networkUpdateSent = true;
                    }

                    tempBuffer.Write(aiming);
                    tempBuffer.Write(use);
                    if (AnimController is HumanoidAnimController)
                    {
                        tempBuffer.Write(((HumanoidAnimController)AnimController).Crouching);
                    }

                    bool hasAttackLimb = AnimController.Limbs.Any(l => l != null && l.attack != null);
                    tempBuffer.Write(hasAttackLimb);
                    if (hasAttackLimb) tempBuffer.Write(attack);

                    if (aiming)
                    {
                        Vector2 relativeCursorPos = cursorPosition - AimRefPosition;
                        tempBuffer.Write((UInt16)(65535.0 * Math.Atan2(relativeCursorPos.Y, relativeCursorPos.X) / (2.0 * Math.PI)));
                    }
                    tempBuffer.Write(IsRagdolled);

                    tempBuffer.Write(AnimController.TargetDir == Direction.Right);
                }

                if (SelectedCharacter != null || SelectedConstruction != null)
                {
                    tempBuffer.Write(true);
                    tempBuffer.Write(SelectedCharacter != null ? SelectedCharacter.ID : SelectedConstruction.ID);
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
                tempBuffer.Write(AnimController.Collider.Rotation);

                WriteStatus(tempBuffer);

                tempBuffer.WritePadBits();

                msg.Write((byte)tempBuffer.LengthBytes);
                msg.Write(tempBuffer);
            }
        }

        private void WriteStatus(NetBuffer msg)
        {
            msg.Write(IsDead);
            if (IsDead)
            {
                msg.WriteRangedInteger(0, Enum.GetValues(typeof(CauseOfDeathType)).Length - 1, (int)CauseOfDeath.Type);
                if (CauseOfDeath.Type == CauseOfDeathType.Affliction)
                {
                    msg.WriteRangedInteger(0, AfflictionPrefab.List.Count - 1, AfflictionPrefab.List.IndexOf(CauseOfDeath.Affliction));
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
                msg.Write(IsRagdolled);
            }
        }

        public void WriteSpawnData(NetBuffer msg)
        {
            if (GameMain.Server == null) return;

            msg.Write(Info == null);
            msg.Write(ID);
            msg.Write(ConfigPath);
            msg.Write(seed);

            msg.Write(WorldPosition.X);
            msg.Write(WorldPosition.Y);

            msg.Write(Enabled);

            //character with no characterinfo (e.g. some monster)
            if (Info == null) return;

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

            msg.Write(TeamID);
            msg.Write(this is AICharacter);
            info.ServerWrite(msg);
        }
    }
}