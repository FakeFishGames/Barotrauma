namespace Barotrauma
{
    class FishWalkParams : FishGroundedParams
    {
        public static FishWalkParams GetAnimParams(Character character)
        {
            return GetAnimParams<FishWalkParams>(character, AnimationType.Walk);
        }
    }

    class FishRunParams : FishGroundedParams
    {
        public static FishRunParams GetAnimParams(Character character)
        {
            return GetAnimParams<FishRunParams>(character, AnimationType.Run);
        }
    }

    class FishSwimFastParams : FishSwimParams
    {
        public static FishSwimFastParams GetAnimParams(Character character)
        {
            return GetAnimParams<FishSwimFastParams>(character, AnimationType.SwimFast);
        }
    }

    class FishSwimSlowParams : FishSwimParams
    {
        public static FishSwimSlowParams GetAnimParams(Character character)
        {
            return GetAnimParams<FishSwimSlowParams>(character, AnimationType.SwimSlow);
        }
    }

    abstract class FishGroundedParams : GroundedMovementParams
    {
        [Serialize(0.0f, true), Editable]
        public float LegTorque
        {
            get;
            set;
        }
    }

    abstract class FishSwimParams : AnimationParams
    {
        [Serialize(1f, true), Editable]
        public float WaveAmplitude
        {
            get;
            set;
        }

        [Serialize(1f, true), Editable]
        public float WaveLength
        {
            get;
            set;
        }

        [Serialize(25.0f, true), Editable]
        public float SteerTorque
        {
            get;
            set;
        }
    }
}
