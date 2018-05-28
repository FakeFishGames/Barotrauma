namespace Barotrauma
{
    class FishWalkParams : FishGroundedParams
    {
        public static FishWalkParams GetAnimParams(string speciesName)
        {
            return GetAnimParams<FishWalkParams>(speciesName, AnimationType.Walk);
        }
    }

    class FishRunParams : FishGroundedParams
    {
        public static FishRunParams GetAnimParams(string speciesName)
        {
            return GetAnimParams<FishRunParams>(speciesName, AnimationType.Run);
        }
    }

    class FishSwimFastParams : FishSwimParams
    {
        public static FishSwimFastParams GetAnimParams(string speciesName)
        {
            return GetAnimParams<FishSwimFastParams>(speciesName, AnimationType.SwimFast);
        }
    }

    class FishSwimSlowParams : FishSwimParams
    {
        public static FishSwimSlowParams GetAnimParams(string speciesName)
        {
            return GetAnimParams<FishSwimSlowParams>(speciesName, AnimationType.SwimSlow);
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
