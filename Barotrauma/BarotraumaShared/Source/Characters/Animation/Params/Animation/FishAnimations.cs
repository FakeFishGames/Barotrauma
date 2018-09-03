using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class FishWalkParams : FishGroundedParams
    {
        public static FishWalkParams GetDefaultAnimParams(Character character)
        {
            return Check(character) ? GetDefaultAnimParams<FishWalkParams>(character.SpeciesName, AnimationType.Walk) : Empty;
        }
        public static FishWalkParams GetAnimParams(Character character, string fileName = null)
        {
            return Check(character) ? GetAnimParams<FishWalkParams>(character.SpeciesName, AnimationType.Walk, fileName) : Empty;
        }

        protected static FishWalkParams Empty = new FishWalkParams();
    }

    class FishRunParams : FishGroundedParams
    {
        public static FishRunParams GetDefaultAnimParams(Character character)
        {
            return Check(character) ? GetDefaultAnimParams<FishRunParams>(character.SpeciesName, AnimationType.Run) : Empty;
        }
        public static FishRunParams GetAnimParams(Character character, string fileName = null)
        {
            return Check(character) ? GetAnimParams<FishRunParams>(character.SpeciesName, AnimationType.Run, fileName) : Empty;
        }

        protected static FishRunParams Empty = new FishRunParams();
    }

    class FishSwimFastParams : FishSwimParams
    {
        public static FishSwimFastParams GetDefaultAnimParams(Character character) => GetDefaultAnimParams<FishSwimFastParams>(character.SpeciesName, AnimationType.SwimFast);
        public static FishSwimFastParams GetAnimParams(Character character, string fileName = null)
        {
            return GetAnimParams<FishSwimFastParams>(character.SpeciesName, AnimationType.SwimFast, fileName);
        }
    }

    class FishSwimSlowParams : FishSwimParams
    {
        public static FishSwimSlowParams GetDefaultAnimParams(Character character) => GetDefaultAnimParams<FishSwimSlowParams>(character.SpeciesName, AnimationType.SwimSlow);
        public static FishSwimSlowParams GetAnimParams(Character character, string fileName = null)
        {
            return GetAnimParams<FishSwimSlowParams>(character.SpeciesName, AnimationType.SwimSlow, fileName);
        }
    }

    abstract class FishGroundedParams : GroundedMovementParams, IFishAnimation
    {
        protected static bool Check(Character character)
        {
            if (!character.AnimController.CanWalk)
            {
                DebugConsole.ThrowError($"{character.SpeciesName} cannot use run animations!");
                return false;
            }
            return true;
        }

        [Serialize(true, true), Editable]
        public bool Flip { get; set; }

        [Serialize(0.0f, true), Editable]
        public float LegTorque { get; set; }

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
        public float FootAngle
        {
            get => float.IsNaN(FootAngleInRadians) ? float.NaN : MathHelper.ToDegrees(FootAngleInRadians);
            set
            {
                if (!float.IsNaN(value))
                {
                    FootAngleInRadians = MathHelper.ToRadians(value);
                }
            }
        }
        public float FootAngleInRadians { get; private set; } = float.NaN;
    }

    abstract class FishSwimParams : SwimParams, IFishAnimation
    {
        [Serialize(true, true), Editable]
        public bool Flip { get; set; }

        [Serialize(false, true), Editable]
        public bool Mirror { get; set; }

        [Serialize(1f, true), Editable]
        public float WaveAmplitude { get; set; }

        [Serialize(1f, true), Editable]
        public float WaveLength { get; set; }

        [Serialize(true, true), Editable]
        public bool RotateTowardsMovement { get; set; }

        /// <summary>
        /// In degrees.
        /// </summary>
        [Serialize(float.NaN, true), Editable]
        public float FootAngle
        {
            get => float.IsNaN(FootAngleInRadians) ? float.NaN : MathHelper.ToDegrees(FootAngleInRadians);
            set
            {
                if (!float.IsNaN(value))
                {
                    FootAngleInRadians = MathHelper.ToRadians(value);
                }
            }
        }
        public float FootAngleInRadians { get; private set; } = float.NaN;
    }

    interface IFishAnimation
    {
        bool Flip { get; set; }
        float FootAngle { get; set; }
        float FootAngleInRadians { get; }
    }
}
