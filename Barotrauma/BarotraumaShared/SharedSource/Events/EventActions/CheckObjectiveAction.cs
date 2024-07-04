namespace Barotrauma;

/// <summary>
/// Checks the state of an Objective created using <see cref="EventObjectiveAction"/>.
/// </summary>
partial class CheckObjectiveAction : BinaryOptionAction
{
    public CheckObjectiveAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

    protected override bool? DetermineSuccess()
    {
        bool success = false;
        DetermineSuccessProjSpecific(ref success);
        return success;
    }

    partial void DetermineSuccessProjSpecific(ref bool success);
}