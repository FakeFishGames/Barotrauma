using Microsoft.Xna.Framework;

namespace Barotrauma
{
    /// <summary>
    /// Adjusts the crew's reputation by some value.
    /// </summary>
    class ReputationAction : EventAction
    {
        public enum ReputationType
        {
            None,
            Location,
            Faction
        }

        public ReputationAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "Amount of reputation to add or remove.")]
        public float Increase { get; set; }

        [Serialize(float.MinValue, IsPropertySaveable.Yes, description: "The reputation is clamped above this value after adding/removing the specified amount.")]
        public float Min { get; set; }

        [Serialize(float.MaxValue, IsPropertySaveable.Yes, description: "The reputation is clamped below this value after adding/removing the specified amount.")]
        public float Max { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the faction you want to adjust the reputation for. Ignored if TargetType is set to Location.")]
        public Identifier Identifier { get; set; }

        [Serialize(ReputationType.None, IsPropertySaveable.Yes, description: "Do you want to adjust the reputation for a specific faction, or whichever faction controls the current location?")]
        public ReputationType TargetType { get; set; }

        private bool isFinished;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset(bool shouldEndConversations = true)
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            if (GameMain.GameSession?.GameMode is CampaignMode campaign)
            {
                switch (TargetType)
                {
                    case ReputationType.Faction:
                        {
                            Faction faction = campaign.GetFaction(Identifier);
                            if (faction != null)
                            {
                                ModifyReputation(faction.Reputation);
                            }
                            else
                            {
                                DebugConsole.ThrowError($"Faction with the identifier \"{Identifier}\" was not found.",
                                    contentPackage: ParentEvent.Prefab.ContentPackage);
                            }

                            break;
                        }
                    case ReputationType.Location:
                        {
                            ModifyReputation(campaign.Map.CurrentLocation?.Reputation);
                            break;
                        }
                    default:
                        {
                            DebugConsole.ThrowError("ReputationAction requires a \"TargetType\" but none were specified.",
                                contentPackage: ParentEvent.Prefab.ContentPackage);
                            break;
                        }
                }
            }

            void ModifyReputation(Reputation reputation)
            {
                if (reputation == null) { return; }
                reputation.AddReputation(Increase);
                if (reputation.Value < Min)
                {
                    reputation.SetReputation(Min);
                }
                else if (reputation.Value > Max)
                {
                    reputation.SetReputation(Max);
                }
            }

            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(ReputationAction)} -> (FactionIdentifier: {Identifier.ColorizeObject()}, TargetType: {TargetType.ColorizeObject()}, Increase: {Increase.ColorizeObject()})";
        }
    }
}