using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class HumanWalkParams : HumanGroundedParams
    {
        public static HumanWalkParams GetDefaultAnimParams(Character character) => GetDefaultAnimParams<HumanWalkParams>(character, AnimationType.Walk);
        public static HumanWalkParams GetAnimParams(Character character, string fileName = null)
        {
            return GetAnimParams<HumanWalkParams>(character.SpeciesName, AnimationType.Walk, fileName);
        }

        public override void StoreSnapshot() => StoreSnapshot<HumanWalkParams>();
    }

    class HumanRunParams : HumanGroundedParams
    {
        public static HumanRunParams GetDefaultAnimParams(Character character) => GetDefaultAnimParams<HumanRunParams>(character, AnimationType.Run);
        public static HumanRunParams GetAnimParams(Character character, string fileName = null)
        {
            return GetAnimParams<HumanRunParams>(character.SpeciesName, AnimationType.Run, fileName);
        }

        public override void StoreSnapshot() => StoreSnapshot<HumanRunParams>();
    }

    class HumanCrouchParams : HumanGroundedParams
    {
        [Serialize(0.0f, true, description: "How much lower the character's head and torso move when stationary."), Editable(MinValueFloat = 0, MaxValueFloat = 2, DecimalCount = 2)]
        public float MoveDownAmountWhenStationary { get; set; }

        [Serialize(0.0f, true), Editable(-360f, 360f)]
        public float ExtraHeadAngleWhenStationary { get; set; }

        [Serialize(0.0f, true), Editable(-360f, 360f)]
        public float ExtraTorsoAngleWhenStationary { get; set; }

        public static HumanCrouchParams GetDefaultAnimParams(Character character) => GetDefaultAnimParams<HumanCrouchParams>(character, AnimationType.Crouch);
        public static HumanCrouchParams GetAnimParams(Character character, string fileName = null)
        {
            return GetAnimParams<HumanCrouchParams>(character.SpeciesName, AnimationType.Crouch, fileName);
        }

        public override void StoreSnapshot() => StoreSnapshot<HumanCrouchParams>();
    }

    class HumanSwimFastParams: HumanSwimParams
    {
        public static HumanSwimFastParams GetDefaultAnimParams(Character character) => GetDefaultAnimParams<HumanSwimFastParams>(character, AnimationType.SwimFast);
        public static HumanSwimFastParams GetAnimParams(Character character, string fileName = null)
        {
            return GetAnimParams<HumanSwimFastParams>(character.SpeciesName, AnimationType.SwimFast, fileName);
        }


        public override void StoreSnapshot() => StoreSnapshot<HumanSwimFastParams>();
    }

    class HumanSwimSlowParams : HumanSwimParams
    {
        public static HumanSwimSlowParams GetDefaultAnimParams(Character character) => GetDefaultAnimParams<HumanSwimSlowParams>(character, AnimationType.SwimSlow);
        public static HumanSwimSlowParams GetAnimParams(Character character, string fileName = null)
        {
            return GetAnimParams<HumanSwimSlowParams>(character.SpeciesName, AnimationType.SwimSlow, fileName);
        }

        public override void StoreSnapshot() => StoreSnapshot<HumanSwimSlowParams>();
    }

    abstract class HumanSwimParams : SwimParams, IHumanAnimation
    {
        [Serialize(0.5f, true), Editable(DecimalCount = 2)]
        public float LegMoveAmount { get; set; }

        [Serialize(5.0f, true), Editable]
        public float LegCycleLength { get; set; }

        [Serialize("0.5, 0.1", true), Editable(DecimalCount = 2)]
        public Vector2 HandMoveAmount { get; set; }

        [Serialize(5.0f, true), Editable]
        public float HandCycleSpeed { get; set; }

        [Serialize("0.0, 0.0", true), Editable(DecimalCount = 2)]
        public Vector2 HandMoveOffset { get; set; }

        /// <summary>
        /// In degrees.
        /// </summary>
        [Serialize(0.0f, true), Editable(-360f, 360f)]
        public float FootAngle
        {
            get => MathHelper.ToDegrees(FootAngleInRadians);
            set
            {
                FootAngleInRadians = MathHelper.ToRadians(value);
            }
        }
        public float FootAngleInRadians { get; private set; }

        [Serialize(1f, true, description: "How much force is used to move the arms."), Editable(MinValueFloat = 0, MaxValueFloat = 20, DecimalCount = 2)]
        public float ArmMoveStrength { get; set; }

        [Serialize(1f, true, description: "How much force is used to move the hands."), Editable(MinValueFloat = 0, MaxValueFloat = 10, DecimalCount = 2)]
        public float HandMoveStrength { get; set; }
    }

    abstract class HumanGroundedParams : GroundedMovementParams, IHumanAnimation
    {
        [Serialize(0.3f, true, description: "How much force is used to force the character upright."), Editable(MinValueFloat = 0, MaxValueFloat = 1, DecimalCount = 2)]
        public float GetUpForce { get; set; }

        [Serialize(0.25f, true, description: "How much the character's head leans forwards when moving."), Editable(DecimalCount = 2)]
        public float HeadLeanAmount { get; set; }

        [Serialize(0.25f, true, description: "How much the character's torso leans forwards when moving."), Editable(DecimalCount = 2)]
        public float TorsoLeanAmount { get; set; }

        [Serialize(15.0f, true, description: "How much force is used to move the feet to the correct position."), Editable(MinValueFloat = 0, MaxValueFloat = 100)]
        public float FootMoveStrength { get; set; }

        [Serialize(0f, true, description: "How much the horizontal difference of waist and the foot positions has an effect to lifting the foot."), Editable(DecimalCount = 2, ValueStep = 0.1f, MinValueFloat = 0f, MaxValueFloat = 1f)]
        public float FootLiftHorizontalFactor { get; set; }

        /// <summary>
        /// In degrees.
        /// </summary>
        [Serialize(0.0f, true), Editable(-360f, 360f)]
        public float FootAngle
        {
            get => MathHelper.ToDegrees(FootAngleInRadians);
            set
            {
                FootAngleInRadians = MathHelper.ToRadians(value);                
            }
        }
        public float FootAngleInRadians { get; private set; }

        [Serialize("0.0, 0.0", true, description: "Added to the calculated foot positions, e.g. a value of {-1.0, 0.0f} would make the character \"drag\" their feet one unit behind them."), Editable(DecimalCount = 2)]
        public Vector2 FootMoveOffset { get; set; }

        [Serialize(10.0f, true, description: "How much torque is used to bend the characters legs when taking a step."), Editable(MinValueFloat = 0, MaxValueFloat = 100)]
        public float LegBendTorque { get; set; }

        [Serialize("0.4, 0.15", true, description: "How much the hands move along each axis."), Editable(DecimalCount = 2)]
        public Vector2 HandMoveAmount { get; set; }

        [Serialize("-0.15, 0.0", true, description: "Added to the calculated hand positions, e.g. a value of {-1.0, 0.0f} would make the character \"drag\" their hands one unit behind them."), Editable(DecimalCount = 2)]
        public Vector2 HandMoveOffset { get; set; }

        [Serialize(-1.0f, true, description: "The position of the hands is clamped below this (relative to the position of the character's torso)."), Editable(DecimalCount = 2)]
        public float HandClampY { get; set; }

        [Serialize(1f, true, description: "How much force is used to move the arms."), Editable(MinValueFloat = 0, MaxValueFloat = 10, DecimalCount = 2)]
        public float ArmMoveStrength { get; set; }

        [Serialize(1f, true, description: "How much force is used to move the hands."), Editable(MinValueFloat = 0, MaxValueFloat = 10, DecimalCount = 2)]
        public float HandMoveStrength { get; set; }
    }

    public interface IHumanAnimation
    {
        float FootAngle { get; set; }
        float FootAngleInRadians { get; }

        float ArmMoveStrength { get; set; }

        float HandMoveStrength { get; set; }
    }
}
