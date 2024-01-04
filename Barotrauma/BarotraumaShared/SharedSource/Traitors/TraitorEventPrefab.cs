#nullable enable
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class TraitorEventPrefab : EventPrefab
    {
        class MissionRequirement
        {
            public Identifier MissionIdentifier;
            public Identifier MissionTag;
            public MissionType MissionType;

            public MissionRequirement(XElement element, TraitorEventPrefab prefab)
            {
                MissionIdentifier = element.GetAttributeIdentifier(nameof(MissionIdentifier), Identifier.Empty);
                MissionTag = element.GetAttributeIdentifier(nameof(MissionTag), Identifier.Empty);
                MissionType = element.GetAttributeEnum(nameof(MissionType), MissionType.None);
                if (MissionIdentifier.IsEmpty && MissionTag.IsEmpty && MissionType == MissionType.None)
                {
                    DebugConsole.ThrowError($"Error in traitor event \"{prefab.Identifier}\". Mission requirement with no {nameof(MissionIdentifier)}, {nameof(MissionTag)} or {nameof(MissionType)}.",
                        contentPackage: prefab.ContentPackage);
                }
            }

            public bool Match(Mission mission)
            {
                if (mission == null) { return MissionIdentifier.IsEmpty && MissionTag.IsEmpty && MissionType == MissionType.None; }
                if (!MissionIdentifier.IsEmpty)
                {
                    return mission.Prefab.Identifier == MissionIdentifier;
                }
                else if (!MissionTag.IsEmpty)
                {
                    return mission.Prefab.Tags.Contains(MissionTag);
                }
                else if (MissionType != MissionType.None)
                {
                    return mission.Prefab.Type == MissionType;
                }
                return false;
            }
        }

        class LevelRequirement
        {
            private enum LevelType
            {
                LocationConnection,
                Outpost,
                Any
            }

            private readonly LevelType levelType;
            public ImmutableArray<Identifier> LocationTypes { get; }

            /// <summary>
            /// Minimimum difficulty of the level for this event to get selected. Defaults to 0.
            /// </summary>
            private readonly float minDifficulty;
            /// <summary>
            /// Minimimum difficulty of the level for this event to get selected. Defaults to 5 or <see cref="minDifficulty"/>, whichever is lower.
            /// </summary>
            private readonly float minDifficultyInCampaign;

            //feels a little weird to have something this specific here, but couldn't think of a better way to implement this
            public ImmutableArray<PropertyConditional> RequiredItemConditionals;

            public LevelRequirement(ContentXElement element, TraitorEventPrefab prefab)
            {
                levelType = element.GetAttributeEnum(nameof(LevelType), LevelType.Any);                
                LocationTypes = element.GetAttributeIdentifierArray("locationtype", Array.Empty<Identifier>()).ToImmutableArray();
                minDifficulty = element.GetAttributeFloat(nameof(minDifficulty), 0.0f);
                minDifficultyInCampaign = element.GetAttributeFloat(nameof(minDifficultyInCampaign), Math.Max(minDifficulty, 5.0f));
                List<PropertyConditional> requiredItemConditionals = new List<PropertyConditional>();
                foreach (var subElement in element.Elements())
                {
                    if (subElement.NameAsIdentifier() == "itemconditional")
                    {
                        requiredItemConditionals.AddRange(PropertyConditional.FromXElement(subElement));
                    }
                }
                RequiredItemConditionals = requiredItemConditionals.ToImmutableArray();
            }

            public bool Match(Level level)
            {
                if (level?.LevelData == null) { return false; }
                switch (levelType)
                {
                    case LevelType.LocationConnection:
                        if (level.LevelData.Type != LevelData.LevelType.LocationConnection) { return false; }
                        break;
                    case LevelType.Outpost:
                        if (level.LevelData.Type != LevelData.LevelType.Outpost) { return false; }
                        break;
                }
                if (GameMain.GameSession?.Campaign != null)
                {
                    if (level.Difficulty < minDifficultyInCampaign) { return false; }
                }
                else
                {
                    if (level.Difficulty < minDifficulty) { return false; }
                }
                if (level.StartLocation == null) 
                {
                    if (LocationTypes.Any()) { return false; }
                }
                else
                {
                    if (LocationTypes.Any() && !LocationTypes.Contains(level.StartLocation.Type.Identifier)) { return false; }
                }
                if (RequiredItemConditionals.Any())
                {
                    bool matchFound = false;
                    foreach (var item in Item.ItemList)
                    {
                        if (RequiredItemConditionals.All(c => item.ConditionalMatches(c)))
                        {
                            matchFound = true;
                            break;
                        }
                    }
                    if (!matchFound) { return false; }
                }                
                return true;
            }
        }

        class ReputationRequirement
        {
            public Identifier Faction;
            public Identifier CompareToFaction;
            public float CompareToValue;

            public readonly PropertyConditional.ComparisonOperatorType Operator;

            public ReputationRequirement(XElement element, TraitorEventPrefab prefab)
            {
                Faction = element.GetAttributeIdentifier(nameof(Faction), Identifier.Empty);

                string conditionStr = element.GetAttributeString("reputation", string.Empty);

                string[] splitString = conditionStr.Split(' ');
                string value;
                if (splitString.Length > 0)
                {
                    //the first part of the string is the operator, skip it
                    value = string.Join(" ", splitString.Skip(1));
                }
                else
                {
                    DebugConsole.ThrowError(
                        $"{conditionStr} in {prefab.Identifier} is too short."+
                        "It should start with an operator followed by a faction identifier or a floating point value.");
                    return;
                }
                Operator = PropertyConditional.GetComparisonOperatorType(splitString[0]);

                if (float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var floatVal))
                {
                    CompareToValue = floatVal;
                }
                else
                {
                    CompareToFaction = value.ToIdentifier();
                }
            }

            public bool Match(CampaignMode campaign)
            {
                var faction1 = campaign.GetFaction(Faction);
                if (faction1 == null)
                {
                    DebugConsole.ThrowError($"Could not find the faction {Faction}.");
                    return false; 
                } 
                if (!CompareToFaction.IsEmpty)
                {
                    var faction2 = campaign.GetFaction(Faction);
                    if (faction2 == null)
                    {
                        DebugConsole.ThrowError($"Could not find the faction {CompareToFaction}.");
                        return false;
                    }
                    return PropertyConditional.CompareFloat(faction1.Reputation.Value, faction2.Reputation.Value, Operator);
                }
                return PropertyConditional.CompareFloat(faction1.Reputation.Value, CompareToValue, Operator);
            }
        }

        public readonly Sprite? Icon;
        public readonly Color IconColor;

        public const int MinDangerLevel = 1;
        public const int MaxDangerLevel = 3;

        public ImmutableHashSet<Identifier> Tags;

        private readonly ImmutableArray<ReputationRequirement> reputationRequirements;
        private readonly ImmutableArray<MissionRequirement> missionRequirements;
        private readonly ImmutableArray<LevelRequirement> levelRequirements;
        public bool HasReputationRequirements => reputationRequirements.Any();
        public bool HasMissionRequirements => missionRequirements.Any();
        public bool HasLevelRequirements => levelRequirements.Any();

        /// <summary>
        /// An event with one of these tags must've been completed previously for this event to trigger.
        /// </summary>
        public ImmutableHashSet<Identifier> RequiredCompletedTags;

        public readonly int DangerLevel;

        /// <summary>
        /// An event of this danger level (or higher) must have been selected previously for this event to trigger. 
        /// It does not matter whether the event was completed successfully or not. Defaults to one less than the DangerLevel of this event.
        /// </summary>
        public readonly int RequiredPreviousDangerLevel;

        /// <summary>
        /// An event of a lower danger level must have been completed on the previous round for this event to trigger.
        /// Defaults to false (no requirements)
        /// </summary>
        public readonly bool RequirePreviousDangerLevelCompleted;

        /// <summary>
        /// Minimum number of non-spectating human players on the server for the event to get selected.
        /// </summary>
        public readonly int MinPlayerCount;

        /// <summary>
        /// Number of players to assign as a "secondary traitor". 
        /// If both this and <see cref="SecondaryTraitorPercentage"/> are defined, this is treated as a minimum number of secondary traitors.
        /// </summary>
        public readonly int SecondaryTraitorAmount;

        /// <summary>
        /// Percentage of players to assign as a "secondary traitor".
        /// </summary>
        public readonly float SecondaryTraitorPercentage;

        /// <summary>
        /// Does accusing a secondary traitor count as correctly identifying the traitor?
        /// </summary>
        public readonly bool AllowAccusingSecondaryTraitor;

        /// <summary>
        /// Money penalty if the crew votes a wrong player as the traitor
        /// </summary>
        public readonly int MoneyPenaltyForUnfoundedTraitorAccusation;

        /// <summary>
        /// Is this event chainable, i.e. does the same traitor get another, higher-lvl one if they complete this one successfully?
        /// </summary>
        public readonly bool IsChainable;

        public readonly float StealPercentageOfExperience;

        public TraitorEventPrefab(ContentXElement element, RandomEventsFile file, Identifier fallbackIdentifier = default)
            : base(element, file, fallbackIdentifier)
        {
            DangerLevel = MathHelper.Clamp(element.GetAttributeInt(nameof(DangerLevel), MinDangerLevel), min: MinDangerLevel, max: MaxDangerLevel);

            RequiredPreviousDangerLevel = MathHelper.Clamp(element.GetAttributeInt(nameof(RequiredPreviousDangerLevel), def: DangerLevel - 1), min: 0, max: MaxDangerLevel - 1);
            RequirePreviousDangerLevelCompleted = element.GetAttributeBool(nameof(RequirePreviousDangerLevelCompleted), false);

            MinPlayerCount = element.GetAttributeInt(nameof(MinPlayerCount), 0);

            SecondaryTraitorAmount = element.GetAttributeInt(nameof(SecondaryTraitorAmount), 0);
            SecondaryTraitorPercentage = element.GetAttributeFloat(nameof(SecondaryTraitorPercentage), 0.0f);

            AllowAccusingSecondaryTraitor = element.GetAttributeBool(nameof(AllowAccusingSecondaryTraitor), true);

            MoneyPenaltyForUnfoundedTraitorAccusation = element.GetAttributeInt(nameof(MoneyPenaltyForUnfoundedTraitorAccusation), 100);

            Tags = element.GetAttributeIdentifierImmutableHashSet(nameof(Tags), ImmutableHashSet<Identifier>.Empty);
            RequiredCompletedTags = element.GetAttributeIdentifierImmutableHashSet(nameof(RequiredCompletedTags), ImmutableHashSet<Identifier>.Empty);

            StealPercentageOfExperience = element.GetAttributeFloat(nameof(StealPercentageOfExperience), 0.0f);

            IsChainable = element.GetAttributeBool(nameof(IsChainable), true);

            List<ReputationRequirement> reputationRequirements = new List<ReputationRequirement>();
            List<LevelRequirement> levelRequirements = new List<LevelRequirement>();
            List<MissionRequirement> missionRequirements = new List<MissionRequirement>();
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "reputationrequirement":
                        reputationRequirements.Add(new ReputationRequirement(subElement!, this));
                        break;
                    case "missionrequirement":
                        missionRequirements.Add(new MissionRequirement(subElement!, this));
                        break;
                    case "levelrequirement":
                        levelRequirements.Add(new LevelRequirement(subElement!, this));
                        break;
                    case "icon":
                        Icon = new Sprite(subElement);
                        IconColor = subElement.GetAttributeColor("color", Color.White);
                        break;
                }
            }
            this.reputationRequirements = reputationRequirements.ToImmutableArray();
            this.levelRequirements = levelRequirements.ToImmutableArray();
            this.missionRequirements = missionRequirements.ToImmutableArray();
        }

        public bool ReputationRequirementsMet(CampaignMode? campaign)
        {
            if (campaign == null) 
            { 
                //no requirements in the campaign
                return true; 
            }
            foreach (ReputationRequirement requirement in reputationRequirements)
            {
                if (!requirement.Match(campaign)) { return false; }
            }
            return true;
        }
        public bool MissionRequirementsMet(GameSession? gameSession)
        {
            if (gameSession == null) { return false; }
            foreach (MissionRequirement requirement in missionRequirements)
            {
                if (gameSession.Missions.None(m => requirement.Match(m))) { return false; }
            }
            return true;
        }
        public bool LevelRequirementsMet(Level? level)
        {
            if (level == null) { return false; }
            //by default (if no requirements are specified) traitor events happen in LocationConnections.
            if (levelRequirements.None() && level.Type != LevelData.LevelType.LocationConnection)
            {
                return false;
            }
            foreach (LevelRequirement requirement in levelRequirements)
            {
                if (!requirement.Match(level)) { return false; }
            }
            return true;
        }

        public override void Dispose()
        {
            Icon?.Remove();
        }

        public override string ToString()
        {
            return $"{nameof(TraitorEventPrefab)} ({Identifier})";
        }

    }
}
