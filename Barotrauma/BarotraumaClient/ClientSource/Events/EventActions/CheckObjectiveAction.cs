using Barotrauma.Tutorials;
using Segment = Barotrauma.ObjectiveManager.Segment;

namespace Barotrauma;

partial class CheckObjectiveAction : BinaryOptionAction
{
    public enum CheckType
    {
        Added,
        Completed
    }

    [Serialize(CheckType.Completed, IsPropertySaveable.Yes)]
    public CheckType Type { get; set; }

    [Serialize("", IsPropertySaveable.Yes)]
    public Identifier Identifier { get; set; }

    partial void DetermineSuccessProjSpecific(ref bool success)
    {
        success = false;
        if (Identifier.IsEmpty)
        {
            success = ObjectiveManager.AllActiveObjectivesCompleted();
        }
        else if (ObjectiveManager.GetObjective(Identifier) is Segment segment)
        {
            success = Type switch
            {
                CheckType.Added => true,
                CheckType.Completed => segment.IsCompleted,
                _ => false
            };
        }
    }
}