using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    class CharacterStateInfo : PosInfo
    {
        public readonly Direction Direction;

        public readonly Character SelectedCharacter;
        public readonly Item SelectedItem;

        public readonly AnimController.Animation Animation;

        public CharacterStateInfo(Vector2 pos, float? rotation, Vector2 velocity, float? angularVelocity, float time, Direction dir, Character selectedCharacter, Item selectedItem, AnimController.Animation animation = AnimController.Animation.None)
            : this(pos, rotation, velocity, angularVelocity, 0, time, dir, selectedCharacter, selectedItem, animation)
        {
        }

        public CharacterStateInfo(Vector2 pos, float? rotation, UInt16 ID, Direction dir, Character selectedCharacter, Item selectedItem, AnimController.Animation animation = AnimController.Animation.None)
            : this(pos, rotation, Vector2.Zero, 0.0f, ID, 0.0f, dir, selectedCharacter, selectedItem, animation)
        {
        }

        protected CharacterStateInfo(Vector2 pos, float? rotation, Vector2 velocity, float? angularVelocity, UInt16 ID, float time, Direction dir, Character selectedCharacter, Item selectedItem, AnimController.Animation animation = AnimController.Animation.None)
            : base(pos, rotation, velocity, angularVelocity, ID, time)
        {
            Direction = dir;
            SelectedCharacter = selectedCharacter;
            SelectedItem = selectedItem;

            Animation = animation;
        }
    }

    partial class Character
    {
        [Flags]
        private enum InputNetFlags : uint
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
            Grab = 0x2000,
            Deselect = 0x4000, // 16384
            Shoot = 0x8000, // 32768
            Reload = 0x10000, // 65536

            MaxVal = 0x1FFFF // 131071
            //MaxVal = 0xFFFF // 65535
            //MaxVal = 0x7FFF   // 32767
            //MaxVal = 0x3FFF // 16383
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

        public float healthUpdateTimer;

        private float healthUpdateInterval;
        public float HealthUpdateInterval
        {
            get { return healthUpdateInterval; }
            set
            {
                healthUpdateInterval = MathHelper.Clamp(value, 0.0f, IsDead ? NetConfig.MaxHealthUpdateIntervalDead : NetConfig.MaxHealthUpdateInterval);
                healthUpdateTimer = Math.Min(healthUpdateTimer, healthUpdateInterval);
            }
        }
        
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

        partial void UpdateNetInput();
    }
}
