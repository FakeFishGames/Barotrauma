using Barotrauma;

namespace HomeAndHarbor;

/// <summary>
/// Scripting action that converts an NPC into a permanent outpost dweller.
/// </summary>
internal class ConvertNPCToDwellerAction(ScriptedEvent parentEvent, ContentXElement element) : DwellerActionBase(parentEvent, element)
{
    protected override bool ExecuteOnCharacter(Character character, PersistentOutpostData outpostData)
    {
        return outpostData.DwellerManager.ConvertNPCToDweller(character);
    }
}