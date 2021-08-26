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
        OnAnyMissionCompleted,
        OnAllMissionsCompleted,
        OnGiveOrder,
        OnCrewKillCharacter,
        OnDieToCharacter,
        OnAllyGainMissionExperience,
        OnGainMissionExperience,
        OnGainMissionMoney,
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
        SwimmingSpeed,
        BuffDurationMultiplier,
        DebuffDurationMultiplier,
        // Combat
        AttackMultiplier,
        RangedAttackSpeed,
        TurretAttackSpeed,
        MeleeAttackSpeed,
        SpreadMultiplier,
        // Utility
        RepairSpeed,
        // Misc
        ReputationGainMultiplier,
        MissionMoneyGainMultiplier,
        ExperienceGainMultiplier,
        MissionExperienceGainMultiplier,

    }

    public enum AbilityFlags
    {
        None,
        MustWalk,
        ImmuneToPressure,
        IgnoredByEnemyAI,
        MoveNormallyWhileDragging,
        CanTinker,
    }

}
