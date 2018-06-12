using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class HumanWalkParams : HumanGroundedParams
    {
        public static HumanWalkParams GetAnimParams(Character character)
        {
            return GetAnimParams<HumanWalkParams>(character, AnimationType.Walk);
        }
    }

    class HumanRunParams : HumanGroundedParams
    {
        public static HumanRunParams GetAnimParams(Character character)
        {
            return GetAnimParams<HumanRunParams>(character, AnimationType.Run);
        }
    }

    class HumanSwimFastParams: HumanSwimParams
    {
        public static HumanSwimFastParams GetAnimParams(Character character)
        {
            return GetAnimParams<HumanSwimFastParams>(character, AnimationType.SwimFast);
        }
    }

    class HumanSwimSlowParams : HumanSwimParams
    {
        public static HumanSwimSlowParams GetAnimParams(Character character)
        {
            return GetAnimParams<HumanSwimSlowParams>(character, AnimationType.SwimSlow);
        }
    }

    abstract class HumanSwimParams : SwimParams
    {
        [Serialize(0.5f, true), Editable]
        public float LegMovementAmount
        {
            get;
            set;
        }

        [Serialize("0.5, 0.5", true), Editable]
        public Vector2 HandMovementAmount
        {
            get;
            set;
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
