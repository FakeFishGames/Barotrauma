using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class HumanWalkParams : HumanGroundedParams
    {
        public static HumanWalkParams GetAnimParams()
        {
            return GetAnimParams<HumanWalkParams>("human", AnimationType.Walk);
        }
    }

    class HumanRunParams : HumanGroundedParams
    {
        public static HumanRunParams GetAnimParams()
        {
            return GetAnimParams<HumanRunParams>("human", AnimationType.Run);
        }
    }

    class HumanSwimFastParams: AnimationParams
    {
        public static HumanSwimFastParams GetAnimParams()
        {
            return GetAnimParams<HumanSwimFastParams>("human", AnimationType.SwimFast);
        }
    }

    class HumanSwimSlowParams : AnimationParams
    {
        public static HumanSwimSlowParams GetAnimParams()
        {
            return GetAnimParams<HumanSwimSlowParams>("human", AnimationType.SwimSlow);
        }
    }

    abstract class HumanGroundedParams : GroundedMovementParams
    {
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
