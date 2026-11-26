#nullable enable
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma
{
    public enum FactionAffiliation
    {
        Positive,
        Neutral,
        Negative
    }

    public class Faction
    {
        public Reputation Reputation { get; }
        public FactionPrefab Prefab { get; }

        public Faction(CampaignMetadata? metadata, FactionPrefab prefab)
        {
            Prefab = prefab;
            Reputation = new Reputation(metadata, this, prefab.MinReputation, prefab.MaxReputation, prefab.InitialReputation);
        }
        
        public bool IsFactionHostile => Reputation.NormalizedValue < Reputation.HostileThreshold;

        /// <summary>
        /// Recovers the reputation towards <see cref="FactionPrefab.ReputationRecoveryThreshold"/> if it's below it.
        /// </summary>
        public void ProgressWorld()
        {
            if (Prefab.ReputationRecoveryPerWorldStep <= 0.0f) { return; }
            if (Reputation.Value >= Prefab.ReputationRecoveryThreshold) { return; }
            Reputation.SetReputation(Math.Min(Reputation.Value + Prefab.ReputationRecoveryPerWorldStep, Prefab.ReputationRecoveryThreshold));
        }

        /// <summary>
        /// Get what kind of affiliation this faction has towards the player depending on who they chose to side with via talents
        /// </summary>
        /// <returns></returns>
        public static FactionAffiliation GetPlayerAffiliationStatus(Faction faction)
        {
            if (GameMain.GameSession?.Campaign is not { } campaign) { return FactionAffiliation.Neutral; }

            bool isHighest = true;
            foreach (Faction otherFaction in campaign.Factions)
            {
                if (otherFaction == faction || otherFaction.Reputation.Value < faction.Reputation.Value) { continue; }

                isHighest = false;
                break;
            }

            return isHighest ? FactionAffiliation.Positive : FactionAffiliation.Negative;
        }

        public override string ToString()
        {
            return $"{base.ToString()} ({Prefab?.Identifier.ToString() ?? "null"})";
        }
    }

    public class FactionPrefab : Prefab
    {
        public readonly static PrefabCollection<FactionPrefab> Prefabs = new PrefabCollection<FactionPrefab>();

        public LocalizedString Name { get; }

        public LocalizedString Description { get; }
        public LocalizedString ShortDescription { get; }

        public Identifier OpposingFaction { get; }

        public class HireableCharacter
        {
            public readonly Identifier NPCSetIdentifier;
            public readonly Identifier NPCIdentifier;
            public readonly float MinReputation;

            public HireableCharacter(ContentXElement element)
            {
                NPCSetIdentifier = element.GetAttributeIdentifier("from", element.GetAttributeIdentifier("npcsetidentifier", Identifier.Empty));
                NPCIdentifier = element.GetAttributeIdentifier("identifier", element.GetAttributeIdentifier("npcidentifier", Identifier.Empty));
                MinReputation = element.GetAttributeFloat("minreputation", 0.0f);
            }
        }

        public ImmutableArray<HireableCharacter> HireableCharacters;

        public ImmutableArray<AutomaticMission> AutomaticMissions;

        public bool StartOutpost { get; }


        public int MenuOrder { get; }

        /// <summary>
        /// How low the reputation can drop on this faction
        /// </summary>
        public int MinReputation { get; }

        /// <summary>
        /// Maximum reputation level you can gain on this faction
        /// </summary>
        public int MaxReputation { get; }

        /// <summary>
        /// What reputation does this faction start with
        /// </summary>
        public int InitialReputation { get; }

        /// <summary>
        /// If the reputation is below this value, it slowly recovers towards it by <see cref="ReputationRecoveryPerWorldStep"/> each world step.
        /// </summary>
        public float ReputationRecoveryThreshold { get; }

        /// <summary>
        /// How much the reputation recovers per world step when below <see cref="ReputationRecoveryThreshold"/>. 0 = no recovery.
        /// </summary>
        public float ReputationRecoveryPerWorldStep { get; }

        public float ControlledOutpostPercentage { get; }

        public float SecondaryControlledOutpostPercentage { get; }

#if CLIENT
        public Sprite? Icon { get; private set; }

        public Sprite? IconSmall { get; private set; }

        public Sprite? BackgroundPortrait { get; private set; }
#endif

        public Color IconColor { get; }

        public FactionPrefab(ContentXElement element, FactionsFile file) : base(file, element.GetAttributeIdentifier("identifier", string.Empty))
        {
            MenuOrder = element.GetAttributeInt("menuorder", 0);
            StartOutpost = element.GetAttributeBool("startoutpost", false);
            MinReputation = element.GetAttributeInt("minreputation", -100);
            MaxReputation = element.GetAttributeInt("maxreputation", 100);
            InitialReputation = element.GetAttributeInt("initialreputation", 0);
            ReputationRecoveryThreshold = element.GetAttributeFloat(nameof(ReputationRecoveryThreshold), 0.0f);
            ReputationRecoveryPerWorldStep = element.GetAttributeFloat(nameof(ReputationRecoveryPerWorldStep), 0.0f);
            ControlledOutpostPercentage = element.GetAttributeFloat("controlledoutpostpercentage", 0);
            SecondaryControlledOutpostPercentage = element.GetAttributeFloat("secondarycontrolledoutpostpercentage", 0);
            Name = element.GetAttributeString("name", null) ?? TextManager.Get($"faction.{Identifier}").Fallback("Unnamed");
            Description = element.GetAttributeString("description", null) ?? TextManager.Get($"faction.{Identifier}.description").Fallback("");
            ShortDescription = element.GetAttributeString("shortdescription", null) ?? TextManager.Get($"faction.{Identifier}.shortdescription").Fallback("");
            OpposingFaction = element.GetAttributeIdentifier(nameof(OpposingFaction), Identifier.Empty);

            List<HireableCharacter> hireableCharacters = new List<HireableCharacter>();
            List<AutomaticMission> automaticMissions = new List<AutomaticMission>();
            foreach (var subElement in element.Elements())
            {
                var subElementId = subElement.NameAsIdentifier();
                if (subElementId == "icon")
                {
                    IconColor = subElement.GetAttributeColor("color", Color.White);
#if CLIENT
                    Icon = new Sprite(subElement);
#endif
                }
                else if (subElementId == "iconsmall")
                {
#if CLIENT
                    IconSmall = new Sprite(subElement);
#endif
                }
                else if (subElementId == "portrait")
                {
#if CLIENT
                    BackgroundPortrait = new Sprite(subElement);
#endif
                }
                else if (subElementId == "hireable")
                {
                    hireableCharacters.Add(new HireableCharacter(subElement));
                }
                else if (subElementId == "mission" || subElementId == "automaticmission")
                {
                    automaticMissions.Add(new AutomaticMission(subElement, Identifier.ToString()));
                }
            }
            HireableCharacters = hireableCharacters.ToImmutableArray();
            AutomaticMissions = automaticMissions.ToImmutableArray();
        }

        public override string ToString()
        {
            return $"{base.ToString()} ({Identifier})";
        }

        public override void Dispose()
        {
#if CLIENT
            Icon?.Remove();
            Icon = null;
#endif
        }
    }
}