namespace Barotrauma;

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