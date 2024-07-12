#nullable enable
using System.Diagnostics;

namespace Barotrauma
{
    /// <summary>
    /// Check whether the reputation of the crew for a specific faction meets some criteria (e.g. equal to, larger than or less than some value).
    /// </summary>
    class CheckReputationAction : CheckDataAction
    {
        [Serialize(ReputationAction.ReputationType.None, IsPropertySaveable.Yes, description: "Should the action check the reputation for a given faction, or whichever faction owns the current location.")]
        public ReputationAction.ReputationType TargetType { get; set; }

        public CheckReputationAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        protected override float GetFloat(CampaignMode campaignMode)
        {
            switch (TargetType)
            {
                case ReputationAction.ReputationType.Faction:
                {
                    Faction? faction = campaignMode.Factions.Find(f => f.Prefab.Identifier == Identifier);
                    if (faction != null) { return faction.Reputation.Value; }
                    break;
                }
                case ReputationAction.ReputationType.Location:
                {
                    Location? location = campaignMode.Map.CurrentLocation;
                    Debug.Assert(location?.Reputation != null, "location?.Reputation != null");
                    if (location?.Reputation != null) { return location.Reputation.Value; }
                    break;
                }
                default:
                {
                    DebugConsole.ThrowError("CheckReputationAction requires a \"TargetType\" but none were specified.", 
                        contentPackage: ParentEvent.Prefab.ContentPackage);
                    break;
                }
            }

            return 0.0f;
        }

        protected override bool GetBool(CampaignMode campaignMode)
        {
            DebugConsole.ThrowError("Boolean comparison cannot be applied to reputations.", 
                contentPackage: ParentEvent.Prefab.ContentPackage);
            return false;
        }
        
        public override string ToDebugString()
        {
            string condition = "?";
            if (value2 != null && value1 != null)
            {
                condition = $"{value1.ColorizeObject()} {Operator.ColorizeObject()} {value2.ColorizeObject()}";
            }

            return $"{ToolBox.GetDebugSymbol(succeeded.HasValue)} {nameof(CheckReputationAction)} -> (Type: {TargetType.ColorizeObject()}, " +
                   $"{(Identifier.IsEmpty ? string.Empty : $"Identifier: {Identifier.ColorizeObject()}, ")}" +
                   $"Success: {succeeded.ColorizeObject()}, Expression: {condition})";
        }
    }
}