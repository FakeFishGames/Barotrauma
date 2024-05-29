namespace Barotrauma
{
    /// <summary>
    /// Modifies the current location in some way (e.g. adjusting the faction, type of name).
    /// </summary>
    class ModifyLocationAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the faction to set as the location's primary faction (optional).")]
        public Identifier Faction { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the faction to set as the location's secondary faction (optional).")]
        public Identifier SecondaryFaction { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the location type to set as the location's new type (optional)")]
        public Identifier Type { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "New name to give to the location (optional). Can either be the name as-is, or a tag referring to a line in a text file.")]
        public Identifier Name { get; set; }

        private bool isFinished;

        public ModifyLocationAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
        }

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

            if (GameMain.GameSession.GameMode is CampaignMode campaign)
            {
                var location = campaign.Map.CurrentLocation;
                if (location != null) 
                { 
                    if (!Faction.IsEmpty)
                    {
                        var faction = campaign.Factions.Find(f => f.Prefab.Identifier == Faction);
                        if (faction == null)
                        {
                            DebugConsole.ThrowError($"Error in ModifyLocationAction ({ParentEvent.Prefab.Identifier}): could not find a faction with the identifier \"{Faction}\".",
                                contentPackage: ParentEvent?.Prefab?.ContentPackage);
                        }
                        else
                        {
                            location.Faction = faction;
                        }
                    }
                    if (!SecondaryFaction.IsEmpty)
                    {
                        var secondaryFaction = campaign.Factions.Find(f => f.Prefab.Identifier == SecondaryFaction);
                        if (secondaryFaction == null)
                        {
                            DebugConsole.ThrowError($"Error in ModifyLocationAction ({ParentEvent.Prefab.Identifier}): could not find a faction with the identifier \"{SecondaryFaction}\".",
                                contentPackage: ParentEvent.Prefab.ContentPackage);
                        }
                        else
                        {
                            location.SecondaryFaction = secondaryFaction;
                        }
                    }
                    if (!Type.IsEmpty)
                    {
                        var locationType = LocationType.Prefabs.Find(lt => lt.Identifier == Type);
                        if (locationType == null)
                        {
                            DebugConsole.ThrowError($"Error in ModifyLocationAction ({ParentEvent.Prefab.Identifier}): could not find a location type with the identifier \"{Type}\".",
                                contentPackage: ParentEvent.Prefab.ContentPackage);
                        }
                        else if (!location.LocationTypeChangesBlocked)
                        {                           
                            location.ChangeType(campaign, locationType);
                        }
                    }
                    if (!Name.IsEmpty)
                    {
                        location.ForceName(Name);
                    }
                }
            }
            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(ModifyLocationAction)}";
        }
    }
}