using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class FishWalkParams : FishGroundedParams
    {
        public static FishWalkParams GetAnimParams(Character character)
        {
            if (!character.AnimController.CanWalk)
            {
                DebugConsole.ThrowError($"{character.SpeciesName} cannot use walk animations!");
                return Empty;
            }
            return GetAnimParams<FishWalkParams>(character, AnimationType.Walk);
        }

        protected static FishWalkParams Empty = new FishWalkParams();
    }

    class FishRunParams : FishGroundedParams
    {
        public static FishRunParams GetAnimParams(Character character)
        {
            if (!character.AnimController.CanWalk)
            {
                DebugConsole.ThrowError($"{character.SpeciesName} cannot use run animations!");
                return Empty;
            }
            return GetAnimParams<FishRunParams>(character, AnimationType.Run);
        }

        protected static FishRunParams Empty = new FishRunParams();
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

    abstract class FishGroundedParams : GroundedMovementParams, IFishAnimation
    {
        [Serialize(true, true), Editable]
        public bool Flip { get; set; }

        [Serialize(0.0f, true), Editable]
        public float LegTorque { get; set; }

        [Serialize(1f, true), Editable]
        public float WaveLength { get; set; }

        /// <summary>
        /// The angle of the collider when standing (i.e. out of water).
        /// In degrees.
        /// </summary>
        [Serialize(0f, true), Editable]
        public float ColliderStandAngle
        {
            get => MathHelper.ToDegrees(ColliderStandAngleInRadians);
            set => ColliderStandAngleInRadians = MathHelper.ToRadians(value);
        }
        public float ColliderStandAngleInRadians { get; private set; }

        /// <summary>
        /// In degrees.
        /// </summary>
        [Serialize(float.NaN, true), Editable]
        public float FootRotation
        {
            get => float.IsNaN(FootRotationInRadians) ? float.NaN : MathHelper.ToDegrees(FootRotationInRadians);
            set
            {
                if (!float.IsNaN(value))
                {
                    FootRotationInRadians = MathHelper.ToRadians(value);
                }
            }
        }
        public float FootRotationInRadians { get; private set; } = float.NaN;
    }

    abstract class FishSwimParams : AnimationParams, IFishAnimation
    {
        [Serialize(true, true), Editable]
        public bool Flip { get; set; }

        [Serialize(false, true), Editable]
        public bool Mirror { get; set; }

        [Serialize(1f, true), Editable]
        public float WaveAmplitude { get; set; }

        [Serialize(1f, true), Editable]
        public float WaveLength { get; set; }

        [Serialize(25.0f, true), Editable]
        public float SteerTorque { get; set; }

        [Serialize(true, true), Editable]
        public bool RotateTowardsMovement { get; set; }
    }

    interface IFishAnimation
    {
        bool Flip { get; set; }
    }
}
