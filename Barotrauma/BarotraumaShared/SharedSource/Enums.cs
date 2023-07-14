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

    /// <summary>
    /// ActionTypes define when a <see cref="StatusEffect"/> is executed.
    /// </summary>
    public enum ActionType
    {
        /// <summary>
        /// Executes every frame regardless of the state of the entity.
        /// </summary>
        Always = 0,
        /// <summary>
        /// Executes when the item is picked up. Only valid for items.
        /// </summary>
        OnPicked = 1,
        /// <summary>
        /// Executes when the item is used. The meaning of "using" an item depends on the item, but generally it means the action that happens when holding the item and clicking LMB. Only valid for items.
        /// </summary>
        OnUse = 2,
        /// <summary>
        /// Executes when an item is held and the aim key is held. Only valid for items.
        /// </summary>
        OnSecondaryUse = 3,
        /// <summary>
        /// Executes continuously while the item is being worn. Only valid for wearable items.
        /// </summary>
        OnWearing = 4,
        /// <summary>
        /// Executes continuously when a specific Containable is inside an ItemContainer. Only valid for Containables defined in an ItemContainer component.
        /// </summary>
        OnContaining = 5,
        /// <summary>
        /// Executes continuously when the item is contained in some inventory. Only valid for items.
        /// </summary>
        OnContained = 6,
        /// <summary>
        /// Executes continuously when the item is NOT contained in an inventory. Only valid for items.
        /// </summary>
        OnNotContained = 7,
        /// <summary>
        /// Executes continuously when the item is active. The meaning of "active" depends on the item, but generally means the item is on, powered, and doing the thing it's intended for. Only valid for items.
        /// </summary>
        OnActive = 8,
        /// <summary>
        /// Executes when using the item fails due to a failed skill check. Only valid for items.
        /// </summary>
        OnFailure = 9,
        /// <summary>
        /// Executes when using the item's condition drops to 0. Only valid for items.
        /// </summary>
        OnBroken = 10,
        /// <summary>
        /// Executes continuously when the entity is within the damage range of fire. Valid for items and characters.
        /// </summary>
        OnFire = 11,
        /// <summary>
        /// Executes continuously when the entity is submerged. Valid for items and characters.
        /// </summary>
        InWater = 12,
        /// <summary>
        /// Executes continuously when the entity is NOT submerged. Valid for items and characters.
        /// </summary>
        NotInWater = 13,
        /// <summary>
        /// Executes when the entity hits something hard enough. For items, the threshold is determined by <see cref="ItemPrefab.ImpactTolerance"/>,
        /// for characters by <see cref="Ragdoll.ImpactTolerance"/>. Valid for items and characters.
        /// </summary>
        OnImpact = 14,
        /// <summary>
        /// Executes continuously when the character is eating another character. Only valid for characters.
        /// </summary>
        OnEating = 15,
        /// <summary>
        /// Executes when the entity receives damage from an external source (i.e. an affliction that increases in severity, or an item degrading by itself don't count).
        /// Valid for items and characters.
        /// </summary>
        OnDamaged = 16,
        /// <summary>
        /// Executes when the limb gets severed. Only valid for limbs.
        /// </summary>
        OnSevered = 17,
        /// <summary>
        /// Executes when a <see cref="Items.Components.Growable"/> produces an item (e.g. when a plant grows a fruit). Only valid for Growable items.
        /// </summary>
        OnProduceSpawned = 18,
        /// <summary>
        /// Executes when a <see cref="Items.Components.Door"/> is opened. Only valid for doors.
        /// </summary>
        OnOpen = 19,
        /// <summary>
        /// Executes when a <see cref="Items.Components.Door"/> is closed. Only valid for doors.
        /// </summary>
        OnClose = 20,
        /// <summary>
        /// Executes when the entity spawns. Valid for items and characters.
        /// </summary>
        OnSpawn = 21,
        /// <summary>
        /// Executes when using the item succeeds based on a skill check. Only valid for items.
        /// </summary>
        OnSuccess = 22,
        /// <summary>
        /// Executes when an Ability (an effect from a talent) triggers the status effect. Only valid in Abilities, the target can be either a character or an item depending on the type of Ability.
        /// </summary>
        OnAbility = 23,
        /// <summary>
        /// Executes when the character dies. Only valid for characters.
        /// </summary>
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

    /// <summary>
    /// StatTypes are used to alter several traits of a character. They are mostly used by talents.
    ///
    /// A lot of StatTypes use a "percentage" value. The way this works is that the value is 0 by default and 1 is added to the value of the stat type to get the final multiplier.
    /// For example if the value is set to 0.2 then 1 is added to it making it 1.2 and that is used as a multiplier.
    /// This makes it so values between -100% and +100% can be easily represented as -1 and 1 respectively. For example 0.5 would translate to 1.5 for +50% and -0.2 would translate to 0.8 for -20% multiplier.
    /// </summary>
    public enum StatTypes
    {
        /// <summary>
        /// Used to indicate an invalid stat type. Should not be used.
        /// </summary>
        None,

        /// <summary>
        /// Boosts electrical skill by a flat amount.
        /// </summary>
        ElectricalSkillBonus,

        /// <summary>
        /// Boosts helm skill by a flat amount.
        /// </summary>
        HelmSkillBonus,

        /// <summary>
        /// Boosts mechanical skill by a flat amount.
        /// </summary>
        MechanicalSkillBonus,

        /// <summary>
        /// Boosts medical skill by a flat amount.
        /// </summary>
        MedicalSkillBonus,

        /// <summary>
        /// Boosts weapons skill by a flat amount.
        /// </summary>
        WeaponsSkillBonus,

        /// <summary>
        /// Boosts the character's helm skill to the given value if it's lower than the given value.
        /// </summary>
        HelmSkillOverride,

        /// <summary>
        /// Boosts the character's medical skill to the given value if it's lower than the given value.
        /// </summary>
        MedicalSkillOverride,

        /// <summary>
        /// Boosts the character's weapons skill to the given value if it's lower than the given value.
        /// </summary>
        WeaponsSkillOverride,

        /// <summary>
        /// Boosts the character's electrical skill to the given value if it's lower than the given value.
        /// </summary>
        ElectricalSkillOverride,

        /// <summary>
        /// Boosts the character's mechanical skill to the given value if it's lower than the given value.
        /// </summary>
        MechanicalSkillOverride,

        /// <summary>
        /// Increases character's maximum vitality by a percentage.
        /// </summary>
        MaximumHealthMultiplier,

        /// <summary>
        /// Increases both walking and swimming speed of the character by a percentage.
        /// </summary>
        MovementSpeed,

        /// <summary>
        /// Increases the character's walking speed by a percentage.
        /// </summary>
        WalkingSpeed,

        /// <summary>
        /// Increases the character's swimming speed by a percentage.
        /// </summary>
        SwimmingSpeed,

        /// <summary>
        /// Decreases how long it takes for buffs applied to the character decay over time by a percentage.
        /// Buffs are afflictions that have isBuff set to true.
        /// </summary>
        BuffDurationMultiplier,

        /// <summary>
        /// Decreases how long it takes for debuff applied to the character decay over time by a percentage.
        /// Debuffs are afflictions that have isBuff set to false.
        /// </summary>
        DebuffDurationMultiplier,

        /// <summary>
        /// Increases the strength of afflictions that are applied to the character by a percentage.
        /// Medicines are items that have the "medical" tag.
        /// </summary>
        MedicalItemEffectivenessMultiplier,

        /// <summary>
        /// Increases the resistance to pushing force caused by flowing water by a percentage. The resistance cannot be below 0% or higher than 100%.
        /// </summary>
        FlowResistance,

        /// <summary>
        /// Increases how much damage the character deals via all attacks by a percentage.
        /// </summary>
        AttackMultiplier,

        /// <summary>
        /// Increases how much damage the character deals to other characters on the same team by a percentage.
        /// </summary>
        TeamAttackMultiplier,

        /// <summary>
        /// Decreases the reload time of ranged weapons held by the character by a percentage.
        /// </summary>
        RangedAttackSpeed,

        /// <summary>
        /// Decreases the reload time of submarine turrets operated by the character by a percentage.
        /// </summary>
        TurretAttackSpeed,

        /// <summary>
        /// Decreases the power consumption of submarine turrets operated by the character by a percentage.
        /// </summary>
        TurretPowerCostReduction,

        /// <summary>
        /// Increases how fast submarine turrets operated by the character charge up by a percentage. Affects turrets like pulse laser.
        /// </summary>
        TurretChargeSpeed,

        /// <summary>
        /// Increases how fast the character can swing melee weapons by a percentage.
        /// </summary>
        MeleeAttackSpeed,

        /// <summary>
        /// Increases the damage dealt by melee weapons held by the character by a percentage.
        /// </summary>
        MeleeAttackMultiplier,

        /// <summary>
        /// Decreases the spread of ranged weapons held by the character by a percentage.
        /// </summary>
        RangedSpreadReduction,

        /// <summary>
        /// Increases the repair speed of the character by a percentage.
        /// </summary>
        RepairSpeed,

        /// <summary>
        /// Increases the repair speed of the character when repairing mechanical items by a percentage.
        /// </summary>
        MechanicalRepairSpeed,

        /// <summary>
        /// Increase deconstruction speed of deconstructor operated by the character by a percentage.
        /// </summary>
        DeconstructorSpeedMultiplier,

        /// <summary>
        /// Increases the repair speed of repair tools that fix submarine walls by a percentage.
        /// </summary>
        RepairToolStructureRepairMultiplier,

        /// <summary>
        /// Increases the wall damage of tools that destroy submarine walls like plasma cutter by a percentage.
        /// </summary>
        RepairToolStructureDamageMultiplier,

        /// <summary>
        /// Increase the detach speed of items like minerals that require a tool to detach from the wall by a percentage.
        /// </summary>
        RepairToolDeattachTimeMultiplier,

        /// <summary>
        /// Allows the character to repair mechanical items past the maximum condition by a flat percentage amount. For example setting this to 0.1 allows the character to repair mechanical items to 110% condition.
        /// </summary>
        MaxRepairConditionMultiplierMechanical,

        /// <summary>
        /// Allows the character to repair electrical items past the maximum condition by a flat percentage amount. For example setting this to 0.1 allows the character to repair electrical items to 110% condition.
        /// </summary>
        MaxRepairConditionMultiplierElectrical,

        /// <summary>
        /// Increase the the quality of items crafted by the character by a flat amount.
        /// Can be made to only affect certain item with a given tag types by specifying a tag via CharacterAbilityGivePermanentStat, when no tag is specified the ability affects all items.
        /// </summary>
        IncreaseFabricationQuality,

        /// <summary>
        /// Boosts the condition of genes combined by the character by a flat amount.
        /// </summary>
        GeneticMaterialRefineBonus,

        /// <summary>
        /// Reduces the chance to taint a gene when combining genes by a percentage. Tainting probability can not go below 0% or above 100%.
        /// </summary>
        GeneticMaterialTaintedProbabilityReductionOnCombine,

        /// <summary>
        /// Increases the speed at which the character gains skills by a percentage.
        /// </summary>
        SkillGainSpeed,

        /// <summary>
        /// Whenever the character's skill level up add a flat amount of more skill levels to the character.
        /// </summary>
        ExtraLevelGain,

        /// <summary>
        /// Increases the speed at which the character gains helm skill by a percentage.
        /// </summary>
        HelmSkillGainSpeed,

        /// <summary>
        /// Increases the speed at which the character gains weapons skill by a percentage.
        /// </summary>
        WeaponsSkillGainSpeed,

        /// <summary>
        /// Increases the speed at which the character gains medical skill by a percentage.
        /// </summary>
        MedicalSkillGainSpeed,

        /// <summary>
        /// Increases the speed at which the character gains electrical skill by a percentage.
        /// </summary>
        ElectricalSkillGainSpeed,

        /// <summary>
        /// Increases the speed at which the character gains mechanical skill by a percentage.
        /// </summary>
        MechanicalSkillGainSpeed,

        /// <summary>
        /// Increases the strength of afflictions the character applies to other characters via medicine by a percentage.
        /// Medicines are items that have the "medical" tag.
        /// </summary>
        MedicalItemApplyingMultiplier,

        /// <summary>
        /// Increases the strength of afflictions the character applies to other characters via medicine by a percentage.
        /// Works only for afflictions that have isBuff set to true.
        /// </summary>
        BuffItemApplyingMultiplier,

        /// <summary>
        /// Increases the strength of afflictions the character applies to other characters via medicine by a percentage.
        /// Works only for afflictions that have "poison" type.
        /// </summary>
        PoisonMultiplier,

        /// <summary>
        /// Increases how long the character can tinker with items by a flat amount where 1 = 1 second.
        /// </summary>
        TinkeringDuration,

        /// <summary>
        /// Increases the effectiveness of the character's tinkerings by a percentage.
        /// Tinkering strength affects the speed and effectiveness of the item that is being tinkered with.
        /// </summary>
        TinkeringStrength,

        /// <summary>
        /// Increases how much condition tinkered items lose when the character tinkers with them by a percentage.
        /// </summary>
        TinkeringDamage,

        /// <summary>
        /// Increases how much reputation the character gains by a percentage.
        /// Can be made to only affect certain factions with a given tag types by specifying a tag via CharacterAbilityGivePermanentStat, when no tag is specified the ability affects all factions.
        /// </summary>
        ReputationGainMultiplier,

        /// <summary>
        /// Increases how much reputation the character loses by a percentage.
        /// Can be made to only affect certain factions with a given tag types by specifying a tag via CharacterAbilityGivePermanentStat, when no tag is specified the ability affects all factions.
        /// </summary>
        ReputationLossMultiplier,

        /// <summary>
        /// Increases how much money the character gains from missions by a percentage.
        /// </summary>
        MissionMoneyGainMultiplier,

        /// <summary>
        /// Increases how much talent experience the character gains from all sources by a percentage.
        /// </summary>
        ExperienceGainMultiplier,

        /// <summary>
        /// Increases how much talent experience the character gains from missions by a percentage.
        /// </summary>
        MissionExperienceGainMultiplier,

        /// <summary>
        /// Increases how many missions the characters crew can have at the same time by a flat amount.
        /// </summary>
        ExtraMissionCount,

        /// <summary>
        /// Increases how many items are in stock in special sales in the store by a flat amount.
        /// </summary>
        ExtraSpecialSalesCount,

        /// <summary>
        /// Increases how much money is gained from selling items to the store by a percentage.
        /// </summary>
        StoreSellMultiplier,

        /// <summary>
        /// Decreases the prices of items in affiliated store by a percentage.
        /// </summary>
        StoreBuyMultiplierAffiliated,

        /// <summary>
        /// Decreases the prices of items in all stores by a percentage.
        /// </summary>
        StoreBuyMultiplier,

        /// <summary>
        /// Decreases the price of upgrades and submarines in affiliated outposts by a percentage.
        /// </summary>
        ShipyardBuyMultiplierAffiliated,

        /// <summary>
        /// Decreases the price of upgrades and submarines in all outposts by a percentage.
        /// </summary>
        ShipyardBuyMultiplier,

        /// <summary>
        /// Limits how many of a certain item can be attached to the wall in the submarine at the same time.
        /// Has to be used with CharacterAbilityGivePermanentStat to specify the tag of the item that is affected. Does nothing if no tag is specified.
        /// </summary>
        MaxAttachableCount,

        /// <summary>
        /// Increase the radius of explosions caused by the character by a percentage.
        /// </summary>
        ExplosionRadiusMultiplier,

        /// <summary>
        /// Increases the damage of explosions caused by the character by a percentage.
        /// </summary>
        ExplosionDamageMultiplier,

        /// <summary>
        /// Decreases the time it takes to fabricate items on fabricators operated by the character by a percentage.
        /// </summary>
        FabricationSpeed,

        /// <summary>
        /// Increases how much damage the character deals to ballast flora by a percentage.
        /// </summary>
        BallastFloraDamageMultiplier,

        /// <summary>
        /// Increases the time it takes for the character to pass out when out of oxygen.
        /// </summary>
        HoldBreathMultiplier,

        /// <summary>
        /// Used to set the character's apprencticeship to a certain job.
        /// Used by the "apprenticeship" talent and requires a job to be specified via CharacterAbilityGivePermanentStat.
        /// </summary>
        Apprenticeship,

        /// <summary>
        /// Increases the revival chance of the character when performing CPR by a percentage.
        /// </summary>
        CPRBoost,

        /// <summary>
        /// Can be used to prevent certain talents from being unlocked by specifying the talent's identifier via CharacterAbilityGivePermanentStat.
        /// </summary>
        LockedTalents
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
        ReactorFuelConsumption,
        DeconstructorSpeed,
        FabricationSpeed
    }

    /// <summary>
    /// AbilityFlags are a set of toggleable flags that can be applied to characters.
    /// </summary>
    [Flags]
    public enum AbilityFlags
    {
        /// <summary>
        /// Used to indicate an erroneous ability flag. Should not be used.
        /// </summary>
        None = 0,

        /// <summary>
        /// Character will not be able to run.
        /// </summary>
        MustWalk = 0x1,

        /// <summary>
        /// Character is immune to pressure.
        /// </summary>
        ImmuneToPressure = 0x2,

        /// <summary>
        /// Character won't be targeted by enemy AI.
        /// </summary>
        IgnoredByEnemyAI = 0x4,

        /// <summary>
        /// Character can drag corpses without a movement speed penalty.
        /// </summary>
        MoveNormallyWhileDragging = 0x8,

        /// <summary>
        /// Character is able to tinker with items.
        /// </summary>
        CanTinker = 0x10,

        /// <summary>
        /// Character is able to tinker with fabricators and deconstructors.
        /// </summary>
        CanTinkerFabricatorsAndDeconstructors = 0x20,

        /// <summary>
        /// Allows items tinkered by the character to consume no power.
        /// </summary>
        TinkeringPowersDevices = 0x40,

        /// <summary>
        /// Allows the character to gain skills past 100.
        /// </summary>
        GainSkillPastMaximum = 0x80,

        /// <summary>
        /// Allows the character to retain experience when respawning as a new character.
        /// </summary>
        RetainExperienceForNewCharacter = 0x100,

        /// <summary>
        /// Allows CharacterAbilityApplyStatusEffectsToLastOrderedCharacter to affect the last 2 characters ordered.
        /// </summary>
        AllowSecondOrderedTarget = 0x200,

        /// <summary>
        /// Character will stay conscious even if their vitality drops below 0.
        /// </summary>
        AlwaysStayConscious = 0x400,

        /// <summary>
        /// Prevents afflictions on the character from dropping the characters vitality below the kill threshold.
        /// The character can still die from sources like getting crushed by pressure or if their head is severed.
        /// </summary>
        CanNotDieToAfflictions = 0x800
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