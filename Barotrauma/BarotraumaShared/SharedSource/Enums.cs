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
        Always, OnPicked, OnUse, OnSecondaryUse,
        OnWearing, OnContaining, OnContained, OnNotContained,
        OnActive, OnFailure, OnBroken,
        OnFire, InWater, NotInWater,
        OnImpact,
        OnEating,
        OnDamaged,
        OnSevered,
        OnProduceSpawned,
        OnOpen, OnClose,
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
        OnItemDeconstructed,
        OnItemDeconstructedMaterial,
        OnStopTinkering,
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
        // Combat
        AttackMultiplier,
        TeamAttackMultiplier,
        RangedAttackSpeed,
        TurretAttackSpeed,
        TurretPowerCostReduction,
        MeleeAttackSpeed,
        MeleeAttackMultiplier,
        RangedSpreadReduction,
        // Utility
        RepairSpeed,
        DeconstructorSpeedMultiplier,
        TinkeringDuration,
        // Misc
        ReputationGainMultiplier,
        MissionMoneyGainMultiplier,
        ExperienceGainMultiplier,
        MissionExperienceGainMultiplier,
        // these should be deprecated and moved to their own implementation, no sense making them share space with stat values
        Coathor,
        WarriorPoetMissionRuns,
        WarriorPoetEnemiesKilled,
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
        RetainExperienceForNewCharacter
    }

}
