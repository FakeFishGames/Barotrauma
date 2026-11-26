using Barotrauma;

namespace HomeAndHarbor;

/// <summary>
/// Scripting action that removes dweller status from a target dweller, making them no longer persistent.
/// The character will remain in the outpost for the ongoing round, but will not respawn again.
/// </summary>
internal class RemoveNPCDwellerAction(ScriptedEvent parentEvent, ContentXElement element) : DwellerActionBase(parentEvent, element)
{
    protected override bool ExecuteOnCharacter(Character character, PersistentOutpostData outpostData)
    {
        return outpostData.DwellerManager.RemoveDwellerStatus(character);
    }
}