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
        OnDeath = OnBroken,
        OnSuccess,
        OnAbility,
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
        OnAllyItemFabricatedAmount,
        OnOpenItemContainer,
        OnUseRangedWeapon,
        OnReduceAffliction,
        OnAddDamageAffliction,
        OnSelfRagdoll,
        OnRoundEnd,
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
        OnItemDeconstructedMaterial,
        OnItemDeconstructedInventory,
        OnStopTinkering,
        OnItemPicked,
        AfterSubmarineAttacked,
    }

    public enum StatTypes
    {
        None,
        // Skills
        ElectricalSkillBonus,
        HelmSkillBonus,
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
        DeconstructorSpeedMultiplier,
        RepairToolStructureRepairMultiplier,
        RepairToolStructureDamageMultiplier,
        RepairToolDeattachTimeMultiplier,
        MaxRepairConditionMultiplier,
        IncreaseFabricationQuality,
        GeneticMaterialRefineBonus,
        GeneticMaterialTaintedProbabilityReductionOnCombine,
        SkillGainSpeed,
        // Tinker
        TinkeringDuration,
        TinkeringStrength,
        TinkeringDamage,
        // Misc
        ReputationGainMultiplier,
        MissionMoneyGainMultiplier,
        ExperienceGainMultiplier,
        MissionExperienceGainMultiplier,
        // these should be deprecated and moved to their own implementation, no sense making them share space with stat values
        Coauthor,
        WarriorPoetMissionRuns,
        WarriorPoetEnemiesKilled,
        QuickfixRepairCount,
    }

    public enum AbilityFlags
    {
        None,
        MustWalk,
        ImmuneToPressure,
        IgnoredByEnemyAI,
        MoveNormallyWhileDragging,
        CanTinker,
        CanTinkerFabricatorsAndDeconstructors,
        TinkeringPowersDevices,
        GainSkillPastMaximum,
        RetainExperienceForNewCharacter,
        AllowSecondOrderedTarget,
    }

}
