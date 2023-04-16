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

        public ReputationAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        [Serialize(0.0f, IsPropertySaveable.Yes)]
        public float Increase { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier Identifier { get; set; }

        [Serialize(ReputationType.None, IsPropertySaveable.Yes)]
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
                                DebugConsole.ThrowError($"Faction with the identifier \"{Identifier}\" was not found.");
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