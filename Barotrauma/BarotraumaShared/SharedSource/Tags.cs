using Microsoft.Xna.Framework;

namespace Barotrauma;

public static class Tags
{
    public static readonly Identifier Fuel = "reactorfuel".ToIdentifier();
    public static readonly Identifier Reactor = "reactor".ToIdentifier();

    public static readonly Identifier JunctionBox = "junctionbox".ToIdentifier();

    public static readonly Identifier Turret = "turret".ToIdentifier();
    public static readonly Identifier Hardpoint = "hardpoint".ToIdentifier();
    public static readonly Identifier Periscope = "periscope".ToIdentifier();
    public static readonly Identifier TurretAmmoSource = "turretammosource".ToIdentifier();

    public static readonly Identifier GeneticMaterial = "geneticmaterial".ToIdentifier();
    public static readonly Identifier GeneticDevice = "geneticdevice".ToIdentifier();

    public static readonly Identifier HeavyDivingGear = "deepdiving".ToIdentifier();
    public static readonly Identifier LightDivingGear = "lightdiving".ToIdentifier();

    public static readonly Identifier FPGACircuit = "fpgacircuit".ToIdentifier();
    public static readonly Identifier RedWire = new Identifier("redwire");

    /// <summary>
    /// Diving gear that's suitable for wearing indoors (-> the bots don't try to unequip it when they don't need diving gear)
    /// </summary>
    public static readonly Identifier DivingGearWearableIndoors = "divinggear_wearableindoors".ToIdentifier();

    public static readonly Identifier DockingPort = "dock".ToIdentifier();
    public static readonly Identifier Ballast = "ballast".ToIdentifier();
    public static readonly Identifier Airlock = "airlock".ToIdentifier();

    public static readonly Identifier HiddenItemContainer = "hidden".ToIdentifier();

    public static readonly Identifier MedicalItem = new Identifier("medical");

    public static readonly Identifier WeldingFuel = "weldingfuel".ToIdentifier();
    public static readonly Identifier DivingGear = "diving".ToIdentifier();
    public static readonly Identifier OxygenSource = "oxygensource".ToIdentifier();
    public static readonly Identifier FireExtinguisher = "fireextinguisher".ToIdentifier();
    public static readonly Identifier FallbackLocker = "locker".ToIdentifier();
    public static readonly Identifier DontTakeItems = "donttakeitems".ToIdentifier();
    public static readonly Identifier ToolItem = "tool".ToIdentifier();
    public static readonly Identifier LogicItem = "logic".ToIdentifier();
    public static readonly Identifier NavTerminal = "navterminal".ToIdentifier();
    public static readonly Identifier IdCardTag = "identitycard".ToIdentifier();
    public static readonly Identifier WireItem = "wire".ToIdentifier();
    public static readonly Identifier ChairItem = "chair".ToIdentifier();
    public static readonly Identifier ArtifactHolder = "artifactholder".ToIdentifier();
    public static readonly Identifier Thalamus = "thalamus".ToIdentifier();

    public static readonly Identifier IgnoreThis = "ignorethis".ToIdentifier();
    public static readonly Identifier UnignoreThis = "unignorethis".ToIdentifier();

    public static readonly Identifier DeconstructThis = "deconstructthis".ToIdentifier();
    public static readonly Identifier DontDeconstructThis = "dontdeconstructthis".ToIdentifier();

    public static readonly Identifier Poison = "poison".ToIdentifier();
    public static readonly Identifier Stun = "stun".ToIdentifier();

    public static readonly Identifier Crate = "crate".ToIdentifier();
    public static readonly Identifier DontSellItems = "dontsellitems".ToIdentifier();
    public static readonly Identifier CargoContainer = "cargocontainer".ToIdentifier();
    public static readonly Identifier DisallowCargo = "disallowcargo".ToIdentifier();

    public static readonly Identifier CargoMissionItem = "cargomission".ToIdentifier();

    public static readonly Identifier ItemIgnoredByAI = "ignorebyai".ToIdentifier();

    public static readonly Identifier GuardianShelter = "guardianshelter".ToIdentifier();

    public static readonly Identifier AllowCleanup = "allowcleanup".ToIdentifier();

    public static readonly Identifier Weapon = "weapon".ToIdentifier();
    public static readonly Identifier StunnerItem = "stunner".ToIdentifier();
    public static readonly Identifier MobileRadio = "mobileradio".ToIdentifier();

    public static readonly Identifier Scooter = "scooter".ToIdentifier();

    /// <summary>
    /// Any handcuffs.
    /// </summary>
    public static readonly Identifier HandLockerItem = "handlocker".ToIdentifier();

    /// <summary>
    /// Vanilla handcuffs.
    /// </summary>
    public static readonly Identifier Handcuffs = "handcuffs".ToIdentifier();

    /// <summary>
    /// A battery cell or similar.
    /// </summary>
    public static readonly Identifier MobileBattery = "mobilebattery".ToIdentifier();

    public static readonly Identifier Traitor = "traitor".ToIdentifier();
    public static readonly Identifier SecondaryTraitor = "secondarytraitor".ToIdentifier();
    public static readonly Identifier AnyTraitor = "anytraitor".ToIdentifier();
    public static readonly Identifier NonTraitor = "nontraitor".ToIdentifier();
    public static readonly Identifier NonTraitorPlayer = "nontraitorplayer".ToIdentifier();
    public static readonly Identifier TraitorMissionItem = "traitormissionitem".ToIdentifier();
    public static readonly Identifier TraitorGuidelinesForSecurity = "traitorguidelinesforsecurity".ToIdentifier();

    public static readonly Identifier ProvocativeToHumanAI = "provocativetohumanai".ToIdentifier();

    /// <summary>
    /// Container where the initial gear (diving suit, oxygen tank, etc) of respawning players is placed
    /// </summary>
    public static readonly Identifier RespawnContainer = "respawncontainer".ToIdentifier();

    /// <summary>
    /// Container spawned for the gear of a player who despawns (duffel bag)
    /// </summary>
    public static readonly Identifier DespawnContainer = "despawncontainer".ToIdentifier();

    /// <summary>
    /// Used by talents to target all stat identifiers
    /// </summary>
    public static readonly Identifier StatIdentifierTargetAll = "all".ToIdentifier();

    public static readonly Identifier HelmSkill = "helm".ToIdentifier();
    public static readonly Identifier WeaponsSkill = "weapons".ToIdentifier();
    public static readonly Identifier ElectricalSkill = "electrical".ToIdentifier();
    public static readonly Identifier MechanicalSkill = "mechanical".ToIdentifier();
    public static readonly Identifier MedicalSkill = "medical".ToIdentifier();

    public static readonly Identifier SkillLossDeathResistance = "skilllossdeath".ToIdentifier();
    public static readonly Identifier SkillLossRespawnResistance = "skilllossrespawn".ToIdentifier();
}

