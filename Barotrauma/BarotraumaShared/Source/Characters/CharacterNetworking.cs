using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class CharacterStateInfo : PosInfo
    {
        public readonly Direction Direction;

        public readonly Entity Interact; //the entity being interacted with

        public readonly AnimController.Animation Animation;

        public CharacterStateInfo(Vector2 pos, float time, Direction dir, Entity interact, AnimController.Animation animation = AnimController.Animation.None)
            : this(pos, 0, time, dir, interact, animation)
        {
        }

        public CharacterStateInfo(Vector2 pos, UInt16 ID, Direction dir, Entity interact, AnimController.Animation animation = AnimController.Animation.None)
            : this(pos, ID, 0.0f, dir, interact, animation)
        {
        }

        protected CharacterStateInfo(Vector2 pos, UInt16 ID, float time, Direction dir, Entity interact, AnimController.Animation animation = AnimController.Animation.None)
            : base(pos, ID, time)
        {
            Direction = dir;
            Interact = interact;

            Animation = animation;
        }
    }

    partial class Character
    {
        [Flags]
        private enum InputNetFlags : ushort
        {
            None = 0x0,
            Left = 0x1,
            Right = 0x2,
            Up = 0x4,
            Down = 0x8,
            FacingLeft = 0x10,
            Run = 0x20,
            Crouch = 0x40,
            Select = 0x80,
            Use = 0x100,
            Aim = 0x200,
            Attack = 0x400,

            MaxVal = 0x7FF
        }
        private InputNetFlags dequeuedInput = 0;
        private InputNetFlags prevDequeuedInput = 0;

        public UInt16 LastNetworkUpdateID = 0;

        /// <summary>
        /// ID of the last inputs the server has processed
        /// </summary>
        public UInt16 LastProcessedID;

        private struct NetInputMem
        {
            public InputNetFlags states; //keys pressed/other boolean states at this step
            public UInt16 intAim; //aim angle, represented as an unsigned short where 0=0º, 65535=just a bit under 360º
            public UInt16 interact; //id of the entity being interacted with

            public UInt16 networkUpdateID;
        }

        private List<NetInputMem> memInput  = new List<NetInputMem>();

        private List<CharacterStateInfo> memState        = new List<CharacterStateInfo>();
        private List<CharacterStateInfo> memLocalState   = new List<CharacterStateInfo>();

        private bool networkUpdateSent;

        public bool isSynced = false;
        
        public List<CharacterStateInfo> MemState
        {
            get { return memState; }
        }

        public List<CharacterStateInfo> MemLocalState
        {
            get { return memLocalState; }
        }

        public void ResetNetState()
        {
            memInput.Clear();
            memState.Clear();
            memLocalState.Clear();

            LastNetworkUpdateID = 0;
            LastProcessedID = 0;
        }

        private void UpdateNetInput()
        {
            if (this != Character.Controlled)
            {
                if (GameMain.Client != null)
                {
                    //freeze AI characters if more than 1 seconds have passed since last update from the server
                    if (lastRecvPositionUpdateTime < NetTime.Now - 1.0f)
                    {
                        AnimController.Frozen = true;
                        memState.Clear();
                        //hide after 2 seconds
                        if (lastRecvPositionUpdateTime < NetTime.Now - 2.0f)
                        {
                            Enabled = false;
                            return;
                        }
                    }
                }
                else if (GameMain.Server != null && (!(this is AICharacter) || IsRemotePlayer))
                {
                    if (!AllowInput)
                    {
                        AnimController.Frozen = false;
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
                        cursorPosition = (ViewTarget == null ? AnimController.AimSourcePos : ViewTarget.Position)
                            + new Vector2((float)Math.Cos(aimAngle), (float)Math.Sin(aimAngle)) * 60.0f;

                        //reset focus when attempting to use/select something
                        if (memInput[memInput.Count - 1].states.HasFlag(InputNetFlags.Use) ||
                            memInput[memInput.Count - 1].states.HasFlag(InputNetFlags.Select))
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
            }
            else if (GameMain.Client != null)
            {
                var posInfo = new CharacterStateInfo(
                    SimPosition,
                    LastNetworkUpdateID, 
                    AnimController.TargetDir, 
                    selectedCharacter == null ? (Entity)selectedConstruction : (Entity)selectedCharacter,
                    AnimController.Anim);

                memLocalState.Add(posInfo);

                InputNetFlags newInput = InputNetFlags.None;
                if (IsKeyDown(InputType.Left))      newInput |= InputNetFlags.Left;
                if (IsKeyDown(InputType.Right))     newInput |= InputNetFlags.Right;
                if (IsKeyDown(InputType.Up))        newInput |= InputNetFlags.Up;
                if (IsKeyDown(InputType.Down))      newInput |= InputNetFlags.Down;
                if (IsKeyDown(InputType.Run))       newInput |= InputNetFlags.Run;
                if (IsKeyDown(InputType.Crouch))    newInput |= InputNetFlags.Crouch;
                if (IsKeyHit(InputType.Select))     newInput |= InputNetFlags.Select; //TODO: clean up the way this input is registered
                if (IsKeyDown(InputType.Use))       newInput |= InputNetFlags.Use;
                if (IsKeyDown(InputType.Aim))       newInput |= InputNetFlags.Aim;
                if (IsKeyDown(InputType.Attack))    newInput |= InputNetFlags.Attack;

                if (AnimController.TargetDir == Direction.Left) newInput |= InputNetFlags.FacingLeft;

                Vector2 relativeCursorPos = cursorPosition - (ViewTarget == null ? AnimController.AimSourcePos : ViewTarget.Position);
                relativeCursorPos.Normalize();
                UInt16 intAngle = (UInt16)(65535.0 * Math.Atan2(relativeCursorPos.Y, relativeCursorPos.X) / (2.0 * Math.PI));

                NetInputMem newMem = new NetInputMem();
                newMem.states = newInput;
                newMem.intAim = intAngle;
                if (focusedItem != null)
                {
                    newMem.interact = focusedItem.ID;
                }
                else if (focusedCharacter != null)
                {
                    newMem.interact = focusedCharacter.ID;
                }

                memInput.Insert(0, newMem);
                LastNetworkUpdateID++;
                if (memInput.Count > 60)
                {
                    memInput.RemoveRange(60, memInput.Count - 60);
                }
            }
            else //this == Character.Controlled && GameMain.Client == null
            {                
                AnimController.Frozen = false;
            }

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

                        if (newInput.HasFlag(InputNetFlags.Aim))
                        {
                            newAim = msg.ReadUInt16();
                        }
                        if (newInput.HasFlag(InputNetFlags.Select) || newInput.HasFlag(InputNetFlags.Use))
                        {
                            newInteract = msg.ReadUInt16();                 
                        }

                        if (AllowInput)
                        {
                            if (NetIdUtils.IdMoreRecent((ushort)(networkUpdateID - i), LastNetworkUpdateID) && (i < 60))
                            {
                                NetInputMem newMem = new NetInputMem();
                                newMem.states = newInput;
                                newMem.intAim = newAim;
                                newMem.interact = newInteract;

                                newMem.networkUpdateID = (ushort)(networkUpdateID - i);

                                memInput.Insert(i, newMem);
                            }
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
                    int eventType = msg.ReadRangedInteger(0,2);
                    switch (eventType)
                    {
                        case 0:
                            inventory.ServerRead(type, msg, c);
                            break;
                        case 1:
                            if (c.Character != this)
                            {
#if DEBUG
                                DebugConsole.Log("Received a character update message from a client who's not controlling the character");
#endif
                                return;
                            }

                            bool doingCPR = msg.ReadBoolean();
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
                                Kill(lastAttackCauseOfDeath);
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
                        msg.WriteRangedInteger(0, 2, 0);
                        inventory.ClientWrite(msg, extraData);
                        break;
                    case NetEntityEvent.Type.Control:
                        msg.WriteRangedInteger(0, 2, 1);
                        Client owner = ((Client)extraData[1]);
                        msg.Write(owner == null ? (byte)0 : owner.ID);
                        break;
                    case NetEntityEvent.Type.Status:
                        msg.WriteRangedInteger(0, 2, 2);
                        WriteStatus(msg);
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
                        Vector2 relativeCursorPos = cursorPosition - (ViewTarget == null ? AnimController.AimSourcePos : ViewTarget.Position);
                        tempBuffer.Write((UInt16)(65535.0 * Math.Atan2(relativeCursorPos.Y, relativeCursorPos.X) / (2.0 * Math.PI)));
                    }

                    tempBuffer.Write(AnimController.TargetDir == Direction.Right);
                }

                if (selectedCharacter != null || selectedConstruction != null)
                {
                    tempBuffer.Write(true);
                    tempBuffer.Write(selectedCharacter != null ? selectedCharacter.ID : selectedConstruction.ID);
                    if (selectedCharacter != null)
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

                tempBuffer.WritePadBits();

                msg.Write((byte)tempBuffer.LengthBytes);
                msg.Write(tempBuffer);
            }
        }

        private void WriteStatus(NetBuffer msg)
        {
            if (GameMain.Client != null)
            {
                DebugConsole.ThrowError("Client attempted to write character status to a networked message");
                return;
            }

            msg.Write(isDead);
            if (isDead)
            {
                msg.Write((byte)causeOfDeath);

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
                msg.WriteRangedSingle(health, minHealth, maxHealth, 8);

                msg.Write(oxygen < 100.0f);
                if (oxygen < 100.0f)
                {
                    msg.WriteRangedSingle(oxygen, -100.0f, 100.0f, 8);
                }

                msg.Write(bleeding > 0.0f);
                if (bleeding > 0.0f)
                {
                    msg.WriteRangedSingle(bleeding, 0.0f, 5.0f, 8);
                }

                msg.Write(Stun > 0.0f);
                if (Stun > 0.0f)
                {
                    msg.WriteRangedSingle(MathHelper.Clamp(Stun, 0.0f, MaxStun), 0.0f, MaxStun, 8);
                }

                msg.Write(HuskInfectionState > 0.0f);
            }
        }

        public void WriteSpawnData(NetBuffer msg)
        {
            if (GameMain.Server == null) return;

            msg.Write(Info == null);
            msg.Write(ID);
            msg.Write(ConfigPath);

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

            msg.Write(Info.Name);
            msg.Write(TeamID);

            msg.Write(this is AICharacter);
            msg.Write(Info.Gender == Gender.Female);
            msg.Write((byte)Info.HeadSpriteId);
            if (info.Job != null)
            {
                msg.Write(Info.Job.Name);
                msg.Write((byte)info.Job.Skills.Count);
                foreach (Skill skill in info.Job.Skills)
                {
                    msg.Write(skill.Name);
                    msg.WriteRangedInteger(0, 100, MathHelper.Clamp(skill.Level, 0, 100));
                }
            }
            else
            {
                msg.Write("");
            }
        }
        
    }
}
