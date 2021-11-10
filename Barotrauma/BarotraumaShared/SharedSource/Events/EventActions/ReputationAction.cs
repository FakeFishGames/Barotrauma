using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class ReputationAction : EventAction
    {
        public enum ReputationType
        {
            None,
            Location,
            Faction
        }

        public ReputationAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) { }

        [Serialize(0.0f, true)]
        public float Increase { get; set; }

        [Serialize("", true)]
        public string Identifier { get; set; }

        [Serialize(ReputationType.None, true)]
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
                        Faction faction = campaign.Factions.Find(faction1 => faction1.Prefab.Identifier.Equals(Identifier, StringComparison.OrdinalIgnoreCase));
                        if (faction != null)
                        {
                            faction.Reputation.AddReputation(Increase);
                        }
                        else
                        {
                            DebugConsole.ThrowError($"Faction with the identifier \"{Identifier}\" was not found.");
                        }

                        break;
                    }
                    case ReputationType.Location:
                    {
                        Location location = campaign.Map.CurrentLocation;
                        if (location != null)
                        {
                            location.Reputation.AddReputation(Increase);
                            IEnumerable<Location> locations = location.Connections.SelectMany(c => c.Locations).Distinct().Where(l => l != null && l != location);
                            foreach (Location connectedLocation in locations)
                            {
                                Debug.Assert(connectedLocation.Reputation != null, "connectedLocation.Reputation != null");
                                if (connectedLocation.Reputation != null)
                                {
                                    connectedLocation.Reputation.AddReputation(Increase / 4);
                                }
                            }
                        }

                        break;
                    }
                    default:
                    {
                        DebugConsole.ThrowError("ReputationAction requires a \"TargetType\" but none were specified.");
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