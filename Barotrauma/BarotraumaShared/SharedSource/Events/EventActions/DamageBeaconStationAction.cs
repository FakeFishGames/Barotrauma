#nullable enable
namespace Barotrauma;


/// <summary>
/// Can be used to disconnect wires and break devices and walls in beacon stations. Useful if you want the beacon to be in tact by default, and use events to determine whether it should be e.g. manned by bandits, or destroyed and infested by monsters.
/// </summary>
class DamageBeaconStationAction : EventAction
{
    [Serialize(0.0f, IsPropertySaveable.Yes, description: "Probability of disconnecting wires (0.5 = 50% chance of disconnecting any given wire, 1 = all wires disconnected).")]
    public float DisconnectWireProbability { get; set; }

    [Serialize(0.0f, IsPropertySaveable.Yes, description: "Probability of a wall sections leaking (0.5 = 50% creating a leak on any given wall section, 1 = all walls leak).")]
    public float DamageWallProbability { get; set; }

    [Serialize(0.0f, IsPropertySaveable.Yes, description: "Probability of devices being damaged (0.5 = 50% chance of damaging any given devices, 1 = all devices are damaged).")]
    public float DamageDeviceProbability { get; set; }

    private bool isFinished;

    public DamageBeaconStationAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
    { 
        if (DisconnectWireProbability <= 0.0f && DamageWallProbability <= 0.0f && DamageDeviceProbability <= 0.0f)
        {
            DebugConsole.LogError($"Potential error in event {GetEventDebugName()}: {DisconnectWireProbability}, {DamageWallProbability} and {DamageDeviceProbability} are all set to 0 in {nameof(DamageBeaconStationAction)}, and the action will do nothing.",
                contentPackage: parentEvent.Prefab.ContentPackage);
        }
    }

    public override bool IsFinished(ref string goToLabel) => isFinished;

    public override void Reset()
    {
        isFinished = false;
    }

    public override void Update(float deltaTime)
    {
        if (isFinished) { return; }

        if (Level.Loaded != null)
        {
            Level.Loaded.DisconnectBeaconStationWires(DisconnectWireProbability);
            Level.Loaded.DamageBeaconStationWalls(DamageWallProbability);
            Level.Loaded.DamageBeaconStationDevices(DamageDeviceProbability);
        }

        isFinished = true;
    }

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(DamageBeaconStationAction)}";
    }
}