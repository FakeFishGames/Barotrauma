namespace Barotrauma
{
    class ModifyLocationAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier Faction { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier SecondaryFaction { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier Type { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public string Name { get; set; }

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
                            DebugConsole.ThrowError($"Error in ModifyLocationAction ({ParentEvent.Prefab.Identifier}): could not find a faction with the identifier \"{Faction}\".");
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
                            DebugConsole.ThrowError($"Error in ModifyLocationAction ({ParentEvent.Prefab.Identifier}): could not find a faction with the identifier \"{SecondaryFaction}\".");
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
                            DebugConsole.ThrowError($"Error in ModifyLocationAction ({ParentEvent.Prefab.Identifier}): could not find a location type with the identifier \"{Type}\".");
                        }
                        else if (!location.LocationTypeChangesBlocked)
                        {                           
                            location.ChangeType(campaign, locationType);
                        }
                    }
                    if (!string.IsNullOrEmpty(Name))
                    {
                        location.ForceName(TextManager.Get(Name).Fallback(Name).Value);
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