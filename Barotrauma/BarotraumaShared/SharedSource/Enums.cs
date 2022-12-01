using System;

namespace Barotrauma
{
    public enum TransitionMode
    {
        Linear,
        Smooth,
        Smoother,
        EaseIn,
        EaseOut,
        Exponential
    }

    public enum ActionType
    {
        Always = 0, OnPicked = 1, OnUse = 2, OnSecondaryUse = 3,
        OnWearing = 4, OnContaining = 5, OnContained = 6, OnNotContained = 7,
        OnActive = 8, OnFailure = 9, OnBroken = 10,
        OnFire = 11, InWater = 12, NotInWater = 13,
        OnImpact = 14,
        OnEating = 15,
        OnDamaged = 16,
        OnSevered = 17,
        OnProduceSpawned = 18,
        OnOpen = 19, OnClose = 20,
        OnSpawn = 21,
        OnSuccess = 22,
        OnAbility = 23,
        OnDeath = OnBroken
    }

    public enum AbilityEffectType
    {
        Undefined,
        None,
        OnAttack,
        OnAttackResult,
        OnAttacked,
        OnAttackedResult,
        OnGainSkillPoint,
        OnAllyGainSkillPoint,
        OnRepairComplete,
        OnItemFabricationSkillGain,
        OnItemFabricatedAmount,
        OnItemFabricatedIngredients,
        OnAllyItemFabricatedAmount,
        OnOpenItemContainer,
        OnUseRangedWeapon,
        OnReduceAffliction,
        OnAddDamageAffliction,
        OnSelfRagdoll,
        OnRagdoll,
        OnRoundEnd,
        OnLootCharacter,
        OnAnyMissionCompleted,
        OnAllMissionsCompleted,
        OnGiveOrder,
        OnCrewKillCharacter,
        OnKillCharacter,
        OnDieToCharacter,
        OnAllyGainMissionExperience,
        OnGainMissionExperience,
        OnGainMissionMoney,
        OnLocationDiscovered,
        OnItemDeconstructed,
        OnItemDeconstructedByAlly,
        OnItemDeconstructedMaterial,
        OnItemDeconstructedInventory,
        OnStopTinkering,
        OnItemPicked,
        OnGeneticMaterialCombinedOrRefined,
        OnCrewGeneticMaterialCombinedOrRefined,
        AfterSubmarineAttacked,
        OnApplyTreatment,
        OnStatusEffectIdentifier,
    }

    public enum StatTypes
    {
        None,
        // Skills
        ElectricalSkillBonus,
        HelmSkillBonus,
        HelmSkillOverride,
        MedicalSkillOverride,
        WeaponsSkillOverride,
        ElectricalSkillOverride,
        MechanicalSkillOverride,
        MechanicalSkillBonus,
        MedicalSkillBonus,
        WeaponsSkillBonus,
        // Character attributes
        MaximumHealthMultiplier,
        MovementSpeed,
        WalkingSpeed,
        SwimmingSpeed,
        BuffDurationMultiplier,
        DebuffDurationMultiplier,
        MedicalItemEffectivenessMultiplier,
        FlowResistance,
        // Combat
        AttackMultiplier,
        TeamAttackMultiplier,
        RangedAttackSpeed,
        TurretAttackSpeed,
        TurretPowerCostReduction,
        TurretChargeSpeed,
        MeleeAttackSpeed,
        MeleeAttackMultiplier,
        RangedAttackMultiplier,
        RangedSpreadReduction,
        // Utility
        RepairSpeed,
        MechanicalRepairSpeed,
        DeconstructorSpeedMultiplier,
        RepairToolStructureRepairMultiplier,
        RepairToolStructureDamageMultiplier,
        RepairToolDeattachTimeMultiplier,
        MaxRepairConditionMultiplierMechanical,
        MaxRepairConditionMultiplierElectrical,
        IncreaseFabricationQuality,
        GeneticMaterialRefineBonus,
        GeneticMaterialTaintedProbabilityReductionOnCombine,
        SkillGainSpeed,
        ExtraLevelGain,
        HelmSkillGainSpeed,
        WeaponsSkillGainSpeed,
        MedicalSkillGainSpeed,
        ElectricalSkillGainSpeed,
        MechanicalSkillGainSpeed,
        MedicalItemApplyingMultiplier,
        MedicalItemDurationMultiplier,
        PoisonMultiplier,
        // Tinker
        TinkeringDuration,
        TinkeringStrength,
        TinkeringDamage,
        // Misc
        ReputationGainMultiplier,
        ReputationLossMultiplier,
        MissionMoneyGainMultiplier,
        ExperienceGainMultiplier,
        MissionExperienceGainMultiplier,
        ExtraMissionCount,
        ExtraSpecialSalesCount,
        StoreSellMultiplier,
        StoreBuyMultiplierAffiliated,
        StoreBuyMultiplier,
        MaxAttachableCount,
        ExplosionRadiusMultiplier,
        ExplosionDamageMultiplier,
        FabricationSpeed,
        BallastFloraDamageMultiplier,
        HoldBreathMultiplier,
        Apprenticeship,
        Affiliation,
        CPRBoost
    }

    internal enum ItemTalentStats
    {
        None,
        DetoriationSpeed,
        BatteryCapacity,
        EngineSpeed,
        EngineMaxSpeed,
        PumpSpeed,
        PumpMaxFlow,
        ReactorMaxOutput,
        ReactorFuelEfficiency,
        DeconstructorSpeed,
        FabricationSpeed
    }

    [Flags]
    public enum AbilityFlags
    {
        None = 0,
        MustWalk = 0x1,
        ImmuneToPressure = 0x2,
        IgnoredByEnemyAI = 0x4,
        MoveNormallyWhileDragging = 0x8,
        CanTinker = 0x10,
        CanTinkerFabricatorsAndDeconstructors = 0x20,
        TinkeringPowersDevices = 0x40,
        GainSkillPastMaximum = 0x80,
        RetainExperienceForNewCharacter = 0x100,
        AllowSecondOrderedTarget = 0x200,
        AlwaysStayConscious = 0x400,
        CanNotDieToAfflictions = 0x800,
    }

    [Flags]
    public enum CharacterType
    {
        Bot = 0b01,
        Player = 0b10,
        Both = Bot | Player
    }

    public enum StartingBalanceAmount
    {
        Low,
        Medium,
        High,
    }

    public enum GameDifficulty
    {
        Easy,
        Medium,
        Hard,
        Hellish
    }

    public enum NumberType
    {
        Int,
        Float
    }

    public enum ChatMode
    {
        None,
        Local,
        Radio
    }
}
