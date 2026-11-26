using Barotrauma;

namespace HomeAndHarbor;

/// <summary>
/// Unassigns a dweller from any workstations and makes them unavailable for work (assigning to workstations or moving to crew) for a number of work shifts (world steps).
/// </summary>
internal class SetDwellerUnavailableAction : DwellerActionBase
{
    [Serialize(defaultValue: 0, IsPropertySaveable.Yes, description: "How many work shifts the dweller stays unavailable for work. Must always be defined. -1 means indefinitely until explicitly set otherwise, and 0 resets the unavailability back to normally available.")]
    public int WorkShifts { get; set; }

    [Serialize(defaultValue: "", IsPropertySaveable.Yes, description: "Optional text tag shown in the dweller UI in place of the workstation name while the dweller is away, eg. the flavor reason they're not working. Falls back to the generic 'dwellerui.unavailable'.")]
    public Identifier Reason { get; set; }

    private readonly bool workShiftsDefined;

    public SetDwellerUnavailableAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
    {
        workShiftsDefined = element.GetAttribute(nameof(WorkShifts)) != null;
        if (!workShiftsDefined)
        {
            DebugConsole.ThrowError($"{GetEventDebugName()}: error in {nameof(SetDwellerUnavailableAction)}, '{nameof(WorkShifts)}' must be defined, without it the action will do nothing. Use an explicit value of 0 to clear the unavailability.");
        }
    }

    protected override bool ExecuteOnCharacter(Character character, PersistentOutpostData outpostData)
    {
        if (!workShiftsDefined) { return false; }

        return outpostData.DwellerManager.SetWorkUnavailability(character, WorkShifts, Reason);
    }
}
