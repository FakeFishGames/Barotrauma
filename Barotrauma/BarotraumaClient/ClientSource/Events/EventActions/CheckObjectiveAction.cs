using Segment = Barotrauma.ObjectiveManager.Segment;

namespace Barotrauma;

/// <summary>
/// Checks the state of an Objective created using <see cref="EventObjectiveAction"/>.
/// </summary>
partial class CheckObjectiveAction : BinaryOptionAction
{
    public enum CheckType
    {
        Added,
        Completed,
        Incomplete
    }

    [Serialize(CheckType.Completed, IsPropertySaveable.Yes, description: "The objective must be in this state for the check to succeed.")]
    public CheckType Type { get; set; }

    [Serialize("", IsPropertySaveable.Yes, description: "The identifier of the objective to check.")]
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
                CheckType.Incomplete => !segment.IsCompleted,
                _ => false
            };
        }
        else if (Type == CheckType.Incomplete)
        {
            success = true;
        }
    }
}