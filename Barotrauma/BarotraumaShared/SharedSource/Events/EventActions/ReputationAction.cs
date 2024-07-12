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

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the faction you want to adjust the reputation for. Ignored if TargetType is set to Location.")]
        public Identifier Identifier { get; set; }

        [Serialize(ReputationType.None, IsPropertySaveable.Yes, description: "Do you want to adjust the reputation for a specific faction, or whichever faction controls the current location?")]
        public ReputationType TargetType { get; set; }

        private bool isFinished;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset()
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
                            Faction faction = campaign.Factions.Find(faction1 => faction1.Prefab.Identifier == Identifier);
                            if (faction != null)
                            {
                                faction.Reputation.AddReputation(Increase);
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
                            campaign.Map.CurrentLocation?.Reputation?.AddReputation(Increase);
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

            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(ReputationAction)} -> (FactionIdentifier: {Identifier.ColorizeObject()}, TargetType: {TargetType.ColorizeObject()}, Increase: {Increase.ColorizeObject()})";
        }
    }
}