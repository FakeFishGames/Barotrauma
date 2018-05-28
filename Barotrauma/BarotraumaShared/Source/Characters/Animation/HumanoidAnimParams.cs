using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class HumanoidWalkAnimation : HumanoidGroundedAnimation
    {
        public static readonly HumanoidWalkAnimation Instance = new HumanoidWalkAnimation($"{CHARACTERS_FOLDER}/Humanoid/HumanoidWalk.xml");
        protected HumanoidWalkAnimation(string file) : base(file) { }
    }

    class HumanoidRunAnimation : HumanoidGroundedAnimation
    {
        public static readonly HumanoidRunAnimation Instance = new HumanoidRunAnimation($"{CHARACTERS_FOLDER}/Humanoid/HumanoidRun.xml");
        protected HumanoidRunAnimation(string file) : base(file) { }
    }

    class HumanoidSwimAnimation : SwimAnimation
    {
        public static readonly HumanoidSwimAnimation Instance = new HumanoidSwimAnimation($"{CHARACTERS_FOLDER}/Humanoid/HumanoidSwim.xml");
        protected HumanoidSwimAnimation(string file) : base(file) { }
    }

    abstract class HumanoidGroundedAnimation : WalkAnimation
    {
        protected HumanoidGroundedAnimation(string file) : base(file) { }

        [Serialize(0.3f, true), Editable]
        public float GetUpSpeed
        {
            get;
            set;
        }

        [Serialize(1.54f, true), Editable]
        public float HeadPosition
        {
            get;
            set;
        }

        [Serialize(1.15f, true), Editable]
        public float TorsoPosition
        {
            get;
            set;
        }

        [Serialize(0.25f, true), Editable]
        public float HeadLeanAmount
        {
            get;
            set;
        }

        [Serialize(0.25f, true), Editable]
        public float TorsoLeanAmount
        {
            get;
            set;
        }

        [Serialize(5.0f, true), Editable]
        public float CycleSpeed
        {
            get;
            set;
        }

        [Serialize(15.0f, true), Editable]
        public float FootMoveStrength
        {
            get;
            set;
        }

        [Serialize(20.0f, true), Editable]
        public float FootRotateStrength
        {
            get;
            set;
        }

        [Serialize("0.0, 0.0", true), Editable]
        public Vector2 FootMoveOffset
        {
            get;
            set;
        }

        [Serialize(10.0f, true), Editable]
        public float LegCorrectionTorque
        {
            get;
            set;
        }

        [Serialize(15.0f, true), Editable]
        public float ThighCorrectionTorque
        {
            get;
            set;
        }

        [Serialize("0.4, 0.15", true), Editable]
        public Vector2 HandMoveAmount
        {
            get;
            set;
        }

        [Serialize("-0.15, 0.0", true), Editable]
        public Vector2 HandMoveOffset
        {
            get;
            set;
        }

        [Serialize(0.7f, true), Editable]
        public float HandMoveStrength
        {
            get;
            set;
        }

        [Serialize(-1.0f, true), Editable]
        public float HandClampY
        {
            get;
            set;
        }
    }
}
