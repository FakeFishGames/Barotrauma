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

        public CharacterStateInfo(Vector2 pos, float rotation, float time, Direction dir, Entity interact, AnimController.Animation animation = AnimController.Animation.None)
            : this(pos, rotation, 0, time, dir, interact, animation)
        {
        }

        public CharacterStateInfo(Vector2 pos, float rotation, UInt16 ID, Direction dir, Entity interact, AnimController.Animation animation = AnimController.Animation.None)
            : this(pos, rotation, ID, 0.0f, dir, interact, animation)
        {
        }

        protected CharacterStateInfo(Vector2 pos, float rotation, UInt16 ID, float time, Direction dir, Entity interact, AnimController.Animation animation = AnimController.Animation.None)
            : base(pos, rotation, ID, time)
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
            Ragdoll = 0x800,
            Health = 0x1000,

            MaxVal = 0x1FFF
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

        public string OwnerClientIP;
        public string OwnerClientName;
        public bool ClientDisconnected;
        public float KillDisconnectedTimer;

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
#if SERVER
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
            AnimController.Frozen = false;
#endif
#if CLIENT
            if (GameMain.Client != null)
            {
                if (this != Controlled)
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
                else
                {
                    var posInfo = new CharacterStateInfo(
                    SimPosition,
                    AnimController.Collider.Rotation,
                    LastNetworkUpdateID,
                    AnimController.TargetDir,
                    SelectedCharacter == null ? (Entity)SelectedConstruction : (Entity)SelectedCharacter,
                    AnimController.Anim);

                    memLocalState.Add(posInfo);

                    InputNetFlags newInput = InputNetFlags.None;
                    if (IsKeyDown(InputType.Left)) newInput |= InputNetFlags.Left;
                    if (IsKeyDown(InputType.Right)) newInput |= InputNetFlags.Right;
                    if (IsKeyDown(InputType.Up)) newInput |= InputNetFlags.Up;
                    if (IsKeyDown(InputType.Down)) newInput |= InputNetFlags.Down;
                    if (IsKeyDown(InputType.Run)) newInput |= InputNetFlags.Run;
                    if (IsKeyDown(InputType.Crouch)) newInput |= InputNetFlags.Crouch;
                    if (IsKeyHit(InputType.Select)) newInput |= InputNetFlags.Select; //TODO: clean up the way this input is registered
                    if (IsKeyHit(InputType.Health)) newInput |= InputNetFlags.Health;
                    if (IsKeyDown(InputType.Use)) newInput |= InputNetFlags.Use;
                    if (IsKeyDown(InputType.Aim)) newInput |= InputNetFlags.Aim;
                    if (IsKeyDown(InputType.Attack)) newInput |= InputNetFlags.Attack;
                    if (IsKeyDown(InputType.Ragdoll)) newInput |= InputNetFlags.Ragdoll;

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
            }
            else //this == Character.Controlled && GameMain.Client == null
            {                
                AnimController.Frozen = false;
            }
#endif

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
    }
}
