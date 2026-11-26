using Barotrauma;
using Barotrauma.Plugins;

namespace HomeAndHarbor;

/// <summary>
/// Check whether a tagged outpost dweller is currently assigned to a specific workstation.
/// Only works in a player-owned outpost, fails anywhere else.
/// </summary>
internal class CheckWorkstationAction : BinaryOptionAction
{
    [Serialize("", IsPropertySaveable.Yes, description: "Tag of the dweller to check.")]
    public Identifier NpcTag { get; set; } = Identifier.Empty;

    private readonly Identifier[] workstationIdentifiers;

    public CheckWorkstationAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
    {
        workstationIdentifiers = element.GetAttributeIdentifierArray("workstationidentifier", []);

        if (NpcTag.IsEmpty || workstationIdentifiers.Length == 0)
        {
            DebugConsole.ThrowError($"{GetEventDebugName()}: error in {nameof(CheckWorkstationAction)}, both 'npctag' and 'workstationidentifier' must be set.");
        }
    }

    protected override bool? DetermineSuccess()
    {
        if (NpcTag.IsEmpty || workstationIdentifiers.Length == 0)
        {
            return false;
        }

        if (Level.Loaded?.StartLocation is not Location startLocation ||
            Level.Loaded.Type != LevelData.LevelType.Outpost ||
            PersistentOutpostData.GetPersistentOutpostData(startLocation) is not PersistentOutpostData outpostData ||
            !outpostData.IsPlayerOwned)
        {
            return false;
        }

        Character? character = ParentEvent.GetTargets(NpcTag).OfType<Character>().FirstOrDefault();
        if (character == null)
        {
            Error($"{GetEventDebugName()}: error in {nameof(CheckWorkstationAction)}, no valid character was found for tag \"{NpcTag}\"! The check will fail.");
            return false;
        }

        WorkstationComponent? workstation = outpostData.DwellerManager.GetAssignedWorkstation(character);
        return workstation != null && workstationIdentifiers.Contains(workstation.Item.Prefab.Identifier);

        void Error(string errorMsg)
        {
#if DEBUG
            DebugConsole.ThrowError(errorMsg);
#else
            DebugConsole.AddWarning(errorMsg);
#endif
        }
    }

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(HasBeenDetermined())} {nameof(CheckWorkstationAction)} -> (NpcTag: {NpcTag.ColorizeObject()}, " +
            $"Workstations: {string.Join(',', workstationIdentifiers).ColorizeObject()}, " +
            $"Succeeded: {succeeded.ColorizeObject()})";
    }
}
