#nullable enable
using Barotrauma;
using System;

namespace HomeAndHarbor;

/// <summary>
/// Can be used to check whether the amount of some resource matches a certain condition.
/// </summary>
public class CheckResourceAction : BinaryOptionAction
{
    public enum Target
    {
        CurrentLocation,
        StartLocation,
        EndLocation
    }

    [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the resource to check.")]
    public Identifier Resource { get; set; } = Identifier.Empty;

    [Serialize("", IsPropertySaveable.Yes, description: "The condition that must be met for the check to succeed. Uses the same formatting as conditionals (for example, \"gt 5.2\", \"lt 10\".)")]
    public string Condition { get; set; } = "";

    [Serialize("", IsPropertySaveable.Yes, description: "If set, the action will check whether the location has this resource status indicator.")]
    public Identifier HasResourceStatusIndicator { get; set; } = Identifier.Empty;

    [Serialize(false, IsPropertySaveable.Yes, description: "Should the check be probability-based (i.e. if you have half of the required resource amount, the chance of success is 50%), or should the check always fail when under the required amount and always succeed when above? ")]
    public bool ProbabilityBased { get; set; }

    [Serialize(Target.CurrentLocation, IsPropertySaveable.Yes)]
    public Target TargetLocation { get; set; }

    [Serialize(false, IsPropertySaveable.Yes, description: "Should ItemResources be refreshed to match the items currently in the outpost before the check? "+
        "Normally they're only updated when leaving the outpost, so the items brought to/taken from the outpost during the round aren't taken into account otherwise.")]
    public bool RecountOutpostItems { get; set; }

    protected PropertyConditional.ComparisonOperatorType Operator { get; set; }

    private readonly Either<float, Identifier> comparisonValue = 0f;

    public CheckResourceAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
    {
        string? hasResourceStatusIndicatorStr = element.GetAttributeString(nameof(HasResourceStatusIndicator), string.Empty);
        if (!hasResourceStatusIndicatorStr.IsNullOrEmpty())
        {
            (Operator, string strValue) = PropertyConditional.ExtractComparisonOperatorFromConditionString(hasResourceStatusIndicatorStr);
            if (Operator is not (PropertyConditional.ComparisonOperatorType.NotEquals or PropertyConditional.ComparisonOperatorType.Equals) || strValue.IsNullOrEmpty())
            {
                DebugConsole.ThrowError(
                    $"Error in scripted event \"{parentEvent.Prefab.Identifier}\". Invalid value for \"{HasResourceStatusIndicator}\" in {nameof(CheckResourceAction)}:"+
                    " it should be the identifier of a resource status indicator, or a not-operator (!, neq) followed by the identifier.");
            }
            else
            {
                HasResourceStatusIndicator = strValue.ToIdentifier();
            }
            return; 
        }

        if (string.IsNullOrEmpty(Condition))
        {
            Condition = element.GetAttributeString("value", string.Empty);
            if (string.IsNullOrEmpty(Condition))
            {
                DebugConsole.ThrowError($"Error in scripted event \"{parentEvent.Prefab.Identifier}\". {nameof(CheckResourceAction)} with no condition set ({element}).");
            }
        }
        (Operator, string stringValue) = PropertyConditional.ExtractComparisonOperatorFromConditionString(Condition);

        if (Operator == PropertyConditional.ComparisonOperatorType.None)
        {
            DebugConsole.ThrowError($"Error in scripted event \"{parentEvent.Prefab.Identifier}\". Condition \"{Condition}\" in {nameof(CheckResourceAction)} is invalid, it must specify an operator.");
        }
        else if (float.TryParse(stringValue, out float floatValue))
        {
            comparisonValue = floatValue;
        }
        else
        {
            // Compare to resource
            comparisonValue = stringValue.ToIdentifier();
        }
        if (ProbabilityBased)
        {
            if (!HasResourceStatusIndicator.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in scripted event \"{parentEvent.Prefab.Identifier}\". {nameof(CheckResourceAction)} cannot be probability-based if it checks for a status indicator.");
            }
        }
    }

    protected override bool? DetermineSuccess()
    {
        if (GameMain.GameSession?.Campaign is not { } campaign)
        {
            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: cannot use {nameof(CheckResourceAction)} when a campaign is not active.");
            return false;
        }

        Location targetLocation = TargetLocation switch
        {
            Target.CurrentLocation => campaign.CurrentLocation,
            Target.StartLocation => ParentMission?.Locations[0] ?? Level.Loaded?.StartLocation ?? campaign.CurrentLocation,
            Target.EndLocation => ParentMission?.Locations[1] ?? Level.Loaded?.EndLocation ?? campaign.CurrentLocation,
            _ => throw new ArgumentOutOfRangeException(nameof(TargetLocation), TargetLocation, null)
        };

        var outpostData = PersistentOutpostData.GetPersistentOutpostData(targetLocation);
        if (RecountOutpostItems)
        {
            outpostData.ResourceManager?.SyncResourcesWithLoadedOutpostItems();
        }
        if (!HasResourceStatusIndicator.IsEmpty) 
        {
            if (outpostData.ResourceManager == null) { return false; }
            return
                (Operator == PropertyConditional.ComparisonOperatorType.Equals) ==
                outpostData.ResourceManager.HasActiveResourceStatusIndicator(HasResourceStatusIndicator, debugLogName: $"Event {GetEventDebugName()}");
        }
        else
        {
            Resource? resource = outpostData.ResourceManager?.GetResource(Resource);
            if (resource == null)
            {
                DebugConsole.ThrowError($"{nameof(CheckResourceAction)} failed in event {GetEventDebugName()}: could not find the resource {Resource}.");
                return false;
            }

            float valueToCompareTo;

            if (comparisonValue.TryGet(out Identifier comparisonResourceIdentifier))
            {
                Resource? comparisonResource = outpostData.ResourceManager?.GetResource(comparisonResourceIdentifier);

                if (comparisonResource == null)
                {
                    DebugConsole.ThrowError($"{nameof(CheckResourceAction)} failed in event {GetEventDebugName()}: could not find the resource {comparisonResourceIdentifier}.");
                    return false;
                }
                valueToCompareTo = comparisonResource.Amount;
            }
            else if (comparisonValue.TryGet(out float comparisonFloat))
            {
                valueToCompareTo = comparisonFloat;
            }
            else
            {
                DebugConsole.ThrowError($"{nameof(CheckResourceAction)} failed in event {GetEventDebugName()}: could not determine the value to compare to.");
                return false;
            }

            if (ProbabilityBased)
            {
                bool success = resource.Amount / valueToCompareTo > Rand.Range(0.0f, 1.0f, Rand.RandSync.Unsynced);
                if (Operator is PropertyConditional.ComparisonOperatorType.LessThan or PropertyConditional.ComparisonOperatorType.LessThanEquals)
                {
                    // If the operator is a "less than" type, invert the success value, because having less of the resource should make the check more likely to succeed, not less likely
                    success = !success;
                }
                return success;
            }
            else
            {
                return PropertyConditional.CompareFloat(resource.Amount, valueToCompareTo, Operator);
            }
        }
    }

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(succeeded.HasValue)} {nameof(CheckResourceAction)} " +
            (HasResourceStatusIndicator.IsEmpty ?
            $"-> (Data: {Resource.ColorizeObject()}, Success: {succeeded.ColorizeObject()}, Expression: {Condition})" :
            $"-> ({nameof(HasResourceStatusIndicator)}: {HasResourceStatusIndicator.ColorizeObject()}, Success: {succeeded.ColorizeObject()})");
    }
}
