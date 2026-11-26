using Barotrauma.Extensions;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Linq;

namespace Barotrauma
{
    public sealed partial class MissionPrefab : PrefabWithUintIdentifier, IImplementsVariants<MissionPrefab>
    {
        public static readonly PrefabCollection<MissionPrefab> Prefabs = [];
                
        public enum CompletionCondition
        {
            Always,
            Never,
            OnlyWhenCompleted,
            OnlyWhenFailed,

            True = Always,
            False = Never
        }

        /// <summary>
        /// The keys here are for backwards compatibility, tying the old mission types to the appropriate class. 
        /// Now the mission class is defined by the name of the mission element, and the type can be any arbitrary string.
        /// </summary>
        public static readonly Dictionary<Identifier, Type> CoOpMissionClasses = new Dictionary<Identifier, Type>()
        {
            { "Salvage".ToIdentifier(), typeof(SalvageMission) },
            { "Monster".ToIdentifier(), typeof(MonsterMission) },
            { "Cargo".ToIdentifier(), typeof(CargoMission) },
            { "Beacon".ToIdentifier(), typeof(BeaconMission) },
            { "Nest".ToIdentifier(), typeof(NestMission) },
            { "Mineral".ToIdentifier(), typeof(MineralMission) },
            { "AbandonedOutpost".ToIdentifier(), typeof(AbandonedOutpostMission) },
            { "Escort".ToIdentifier(), typeof(EscortMission) },
            { "Pirate".ToIdentifier(), typeof(SubmarineEncounterMission) },
            { "SubmarineEncounter".ToIdentifier(), typeof(SubmarineEncounterMission) },
            { "GoTo".ToIdentifier(), typeof(GoToMission) },
            { "ScanAlienRuins".ToIdentifier(), typeof(ScanMission) },
            { "EliminateTargets".ToIdentifier(), typeof(EliminateTargetsMission) },
            { "End".ToIdentifier(), typeof(EndMission) },
            { "Custom".ToIdentifier(), typeof(CustomMission) }
        };

        /// <summary>
        /// The keys here are for backwards compatibility, tying the old mission types to the appropriate class. 
        /// Now the mission class is defined by the name of the mission element, and the type can be any arbitrary string.
        /// </summary>
        public static readonly Dictionary<Identifier, Type> PvPMissionClasses = new Dictionary<Identifier, Type>()
        {
            { "Combat".ToIdentifier(), typeof(CombatMission) }
        };

        public static readonly HashSet<Identifier> HiddenMissionTypes = ["GoTo".ToIdentifier(), "End".ToIdentifier()];

        public class ReputationReward
        {
            public readonly Identifier FactionIdentifier;
            public readonly float Amount;
            public readonly float AmountForOpposingFaction;

            public ReputationReward(XElement element)
            {
                FactionIdentifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);
                Amount = element.GetAttributeFloat(nameof(Amount), 0.0f);
                AmountForOpposingFaction = element.GetAttributeFloat(nameof(AmountForOpposingFaction), 0.0f);
            }
        }

        private ConstructorInfo constructor;

        public Identifier Type { get; private set; }

        public Type MissionClass { get; private set; }

        public bool CampaignOnly { get; private set; }
        public bool MultiplayerOnly { get; private set; }
        public bool SingleplayerOnly { get; private set; }

        public Identifier TextIdentifier { get; private set; }

        public ImmutableHashSet<Identifier> Tags { get; private set; }

        public LocalizedString Name { get; private set; }
        public LocalizedString Description { get; private set; }
        /// <summary>
        /// Shown if the mission has been <see cref="Mission.Completed">completed successfully</see>.
        /// </summary>
        public LocalizedString SuccessMessage { get; private set; }
        /// <summary>
        /// Shown if the mission has <see cref="Mission.Failed">failed</see>, or if the mission was not completed successfully and <see cref="FailureMessage"/> is not defined.
        /// </summary>
        public LocalizedString FailureMessage { get; private set; }
        /// <summary>
        /// Shown if the mission was neither <see cref="Mission.Completed">completed successfully</see> or <see cref="Mission.Failed">failed</see>. 
        /// If not defined, the <see cref="FailureMessage"/> is used if the mission was not completed.
        /// </summary>
        public LocalizedString NeutralResultMessage { get; private set; }
        public LocalizedString SonarLabel { get; private set; }
        public Identifier SonarIconIdentifier { get; private set; }

        public Identifier AchievementIdentifier { get; private set; }

        public ImmutableList<ReputationReward> ReputationRewards { get; private set; }

        public readonly List<(Identifier Identifier, object Value, SetDataAction.OperationType OperationType)> DataRewards = [];

        /// <summary>
        /// If set to true, the <see cref="ActionsOnComplete"/> are only executed if the mission is completed in the campaign mode.
        /// </summary>
        public bool ActionsOnCompleteCampaignOnly { get; private set; }
        public ImmutableList<EventAction> ActionsOnComplete { get; private set; }

        /// <summary>
        /// If set to true, the <see cref="ActionsOnFailure"/> are only executed if the mission is failed in the campaign mode.
        /// </summary>
        public bool ActionsOnFailureCampaignOnly { get; private set; }
        public ImmutableList<EventAction> ActionsOnFailure { get; private set; }

        public int Commonness { get; private set; }

        /// <summary>
        /// Displayed difficulty (indicator)
        /// </summary>
        public int? Difficulty { get; private set; }
        public const int MinDifficulty = 1, MaxDifficulty = 4;
        /// <summary>
        /// The actual minimum difficulty of the level allowed for this mission to trigger.
        /// </summary>
        public int MinLevelDifficulty { get; private set; } = 0;
        /// <summary>
        /// The actual maximum difficulty of the level allowed for this mission to trigger.
        /// </summary>
        public int MaxLevelDifficulty { get; private set; } = 100;

        public int Reward { get; private set; }

        public bool RewardDefinedInXML { get; private set; }

        /// <summary>
        /// Flat experience gain when completing the mission. Added on top of the XP gain calculated based on the mission reward.
        /// </summary>
        public float AdditionalExperienceGain { get; private set; }

        public float ExperienceMultiplier { get; private set; }

        // The titles and bodies of the popup messages during the mission, shown when the state of the mission changes. The order matters.
        public ImmutableArray<LocalizedString> Headers { get; private set; }
        public ImmutableArray<LocalizedString> Messages { get; private set; }

        public bool AllowRetry { get; private set; }

        public bool ShowSonarLabels { get; private set; }

        /// <summary>
        /// Note: use <see cref="Mission.ShowInMenus"/> to get the state of the mission to be taken into account 
        /// for missions that are only shown after reaching a certain state.
        /// </summary>
        public bool ShowInMenus { get; private set; }

        /// <summary>
        /// If set, the mission will be shown in menus after it reaches this state.
        /// </summary>
        public int ShowInMenusAfterReachingState { get; private set; } = -1;

        public bool ShowStartMessage { get; private set; }

        /// <summary>
        /// Should the start message be shown at the start of the round even if the mission is hidden from menus at that point
        /// (see <see cref="ShowInMenus"/> and <see cref="ShowInMenusAfterReachingState"/>)?
        /// Requires <see cref="ShowStartMessage"/> to be enabled.
        /// </summary>
        public bool ShowStartMessageWhenHidden { get; private set; }

        public CompletionCondition ShowEndMessage { get; private set; }

        /// <summary>
        /// Should the success/failure icon be shown on the mission in the round summary and tab menu?
        /// Can be disabled on missions where success/failure is not clear-cut from the player's point of view (e.g. encountering a neutral submarine that continues its journey counts as a failure).
        /// </summary>
        public bool ShowCompletionState { get; private set; }

        /// <summary>
        /// Makes the mission not count for the maximum mission limit, and forces it to always be selected when it's available in a level.
        /// </summary>
        public bool IsSideObjective { get; private set; }

        /// <summary>
        /// Missions that aren't shown in menus can't be selected by the player, so they (and side objectives) are always selected automatically when available in a level.
        /// </summary>
        public bool IsSelectedAutomatically => !ShowInMenus || IsSideObjective;

        public bool AllowOtherMissionsInLevel { get; private set; }

        /// <summary>
        /// Can there be multiple instances of this mission in the same level?
        /// Use with caution, since most missions aren't designed to work like this. In the vanilla game, used to allow multiple submarines to transport things through the same level 
        /// - however, only one of the missions in the level spawns a sub, see <see cref="SubmarineEncounterMission.DisallowSubSpawnWithMissionsOfSameType"/>
        /// </summary>
        public bool AllowMultipleInSameLevel { get; private set; }

        /// <summary>
        /// When should the mission continue towards the destination if it's a multi-level mission (see <see cref="Mission.PathToDestination"/>)?
        /// </summary>
        public CompletionCondition ContinuePathCondition;

        /// <summary>
        /// Should the locations along the path be discovered as the mission progresses towards the destination? 
        /// Only relevant on multi-level missions (see <see cref="Mission.PathToDestination"/>)?
        /// </summary>
        public bool DiscoverPathAsProgressing { get; private set; }

        /// <summary>
        /// When enabled, missions of the same type aren't allowed to trigger in the same level.
        /// </summary>
        public bool DisallowMissionsOfSameType { get; private set; }

        /// <summary>
        /// If <see cref="DisallowMissionsOfSameType"/> is enabled, can another mission of the same type replace this one when attempting to unlock it in the same level.
        /// Can be used e.g. to allow special abandoned outpost missions to replace the pre-generated ones.
        /// </summary>
        public bool AllowReplacingWithMissionOfSameType { get; private set; }

        /// <summary>
        /// If enabled, this mission will replace any other missions taking place in the same level, assuming they are allowed to be replaced <see cref="AllowReplacingWithOtherMissions"/>.
        /// </summary>
        public bool ReplaceOtherMissionsInLevel { get; private set; }

        /// <summary>
        /// Are other missions allowed to replace in the level (see <see cref="ReplaceOtherMissionsInLevel"/>)
        /// </summary>
        public bool AllowReplacingWithOtherMissions { get; private set; }

        public bool RequireWreck { get; private set; }
        public bool RequireCave { get; private set; }
        public bool RequireRuin { get; private set; }
        public bool RequireBeaconStation { get; private set; }
        public bool RequireThalamusWreck { get; private set; }
        public bool SpawnBeaconStationInMiddle { get; private set; }

        public bool AllowOutpostNPCs { get; private set; }

        public Identifier ForceOutpostGenerationParameters { get; private set; }

        /// <summary>
        /// Outpost generation parameters that are forced to be used when the mission is active (includes PvP mode).
        /// </summary>
        public Identifier ForceOutpostGenerationParametersInMissionMode { get; private set; }

        /// <summary>
        /// ID cards that should be given to everyone in the mission mode when the mission is active (includes PvP mode).
        /// </summary>
        public ImmutableArray<Identifier> GiveIdCardTagsToEveryoneInMissionMode { get; private set; }

        public RespawnMode? ForceRespawnMode { get; private set; }

        public bool ForceRespawnInsideOutpost { get; private set; }

        public bool ForceTeam1SpawnInsideSub { get; private set; }
        public bool ForceTeam2SpawnInsideSub { get; private set; }

        /// <summary>
        /// If set, the players can choose which outpost is used for the mission (selected from the outposts that have this tag). Only works in multiplayer.
        /// </summary>
        public Identifier AllowOutpostSelectionFromTag { get; private set; }

        /// <summary>
        /// If set, the players can choose a procedurally generated outpost for the mission. Only works in multiplayer, and when AllowOutpostSelectionFromTag is set (if it's not, the outpost is always procedurally generated).
        /// </summary>
        public bool AllowOutpostSelectionProcedurallyGenerated { get; private set; }

        public bool LoadSubmarines { get; private set; } = true;

        /// <summary>
        /// Should the mission be visible as an icon on the campaign map?
        /// </summary>
        public bool ShowOnMap { get; private set; }

        /// <summary>
        /// Should the arrival time (time after which the mission completes/fails) be shown on the campaign map? Only relevant if <see cref="ShowOnMap"/> is true.
        /// </summary>
        public bool ShowArrivalTime { get; private set; }

        /// <summary>
        /// If set, a notification about the mission is displayed in the "news crawl" at the bottom of the campaign map when the mission unlocks.
        /// </summary>
        public string MapNotificationOnUnlock { get; private set; }

        /// <summary>
        /// The mission will automatically fail if it hasn't been completed in this many world steps. World steps are advanced between rounds and roughly correspond to 10 minutes of in-game time.
        /// The property is intended for missions that have been unlocked in a level somewhere else on the map. A value of -1 means no time limit. Defaults to -1.
        /// </summary>
        public int TimeLimitWorldSteps { get; private set; }

        /// <summary>
        /// If enabled, reaching the <see cref="TimeLimitWorldSteps">time limit</see> completes the mission instead of failing it.
        /// </summary>
        public bool CompleteWhenTimeLimitReached { get; private set; }

        /// <summary>
        /// If enabled, locations this mission takes place in cannot change their type
        /// </summary>
        public bool BlockLocationTypeChanges { get; private set; }

        public bool ShowProgressBar { get; private set; }
        public bool ShowProgressInNumbers { get; private set; }
        public int MaxProgressState { get; private set; }
        public LocalizedString ProgressBarLabel { get; private set; }

        /// <summary>
        /// The mission can only be received when travelling from a location of the first type to a location of the second type
        /// </summary>
        public List<(Identifier from, Identifier to)> AllowedConnectionTypes { get; private set; }

        /// <summary>
        /// The mission can only be received in these location types
        /// </summary>
        public readonly List<Identifier> AllowedLocationTypes = [];

        /// <summary>
        /// The mission can only happen in locations owned by this faction. In the mission mode, the location is forced to be owned by this faction.
        /// </summary>
        public Identifier RequiredLocationFaction { get; private set; }

        /// <summary>
        /// The destination of the mission must belong to this faction. If None, it must not belong to any faction.
        /// If the owner of the destination changes to an invalid faction while the mission is active, the mission is removed.
        /// </summary>
        public Identifier RequiredDestinationFaction { get; private set; }

        /// <summary>
        /// Show entities belonging to these sub categories when the mission starts
        /// </summary>
        public List<string> UnhideEntitySubCategories { get; private set; }

        /// <summary>
        /// When enabled, outposts are always destructible regardless of the <see cref="ServerSettings.DestructibleOutposts"/> server setting.
        /// </summary>
        public bool ForceDestructibleOutposts { get; private set; }

        /// <summary>
        /// When enabled, taking items from outposts is not considered stealing.
        /// </summary>
        public bool ForceAllowStealing { get; private set; }

        /// <summary>
        /// When set, the outpost is set to belong to this faction in the mission mode. Maybe necessary in e.g. outpost combat missions to get faction-specific security to spawn.
        /// </summary>
        public Identifier ForceOutpostFactionInMissionMode { get; private set; }

        /// <summary>
        /// When set, the outpost is set to belong to this team in the mission mode. One possible usage of the field is to make the outpost hostile to the players by setting the team to Team2, 
        /// or to make them consider your crew the same team by setting the team to Team1 (which will allow you to e.g. freely take items from the outpost).
        /// </summary>
        public CharacterTeamType? ForceOutpostTeamInMissionMode { get; private set; }

        /// <summary>
        /// Prevents the player from being able to leave their current location until this mission is completed.
        /// </summary>
        public bool DisallowLeavingLocation { get; private set; }

        /// <summary>
        /// Prevents the player from being able to save until this mission is completed.
        /// </summary>
        public bool DisallowSaving { get; private set; }

        public class TriggerEvent
        {
            [Serialize("", IsPropertySaveable.Yes)]
            public Identifier EventIdentifier { get; private set; }

            [Serialize("", IsPropertySaveable.Yes)]
            public Identifier EventTag { get; private set; }

            [Serialize(0, IsPropertySaveable.Yes)]
            public int State { get; private set; }

            [Serialize(1.0f, IsPropertySaveable.Yes)]
            public float Probability { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.Yes)]
            public float Delay { get; private set; }

            [Serialize(false, IsPropertySaveable.Yes)]
            public bool CampaignOnly { get; private set; }

            public TriggerEvent(XElement element)
            {
                SerializableProperty.DeserializeProperties(this, element);
            }
        }

        public readonly List<TriggerEvent> TriggerEvents = [];

        public LocationTypeChange LocationTypeChangeOnCompleted;

        private readonly ContentXElement originalElement;
        public ContentXElement ConfigElement { get; private set; }

        public Identifier VariantOf { get; }
        public MissionPrefab ParentPrefab { get; set; }

        public MissionPrefab(ContentXElement element, MissionsFile file) : base(file, element.GetAttributeIdentifier("identifier", ""))
        {
            ConfigElement = originalElement = element;

            VariantOf = element.VariantOf();
            if (!VariantOf.IsEmpty) { return; } // Don't read the XML until the PrefabCollection loads the parent.
            ParseConfigElement();
        }

        public void InheritFrom(MissionPrefab parent)
        {
            ConfigElement = originalElement.CreateVariantXML(parent.ConfigElement);
            ParseConfigElement(parent);
        }

        private void ParseConfigElement(MissionPrefab variantOf = null)
        {
            TextIdentifier = ConfigElement.GetAttributeIdentifier("textidentifier", Identifier);

            Tags = [.. ConfigElement.GetAttributeIdentifierArray("tags", [])];

            Name = GetText(ConfigElement.GetAttributeString("name", ""), "MissionName");
            Description = GetText(ConfigElement.GetAttributeString("description", ""), "MissionDescription");

            LocalizedString GetText(string textTag, string textTagPrefix)
            {
                if (string.IsNullOrEmpty(textTag))
                {
                    return TextManager.Get($"{textTagPrefix}.{TextIdentifier}");
                }
                else
                {
                    return TextManager.Get(textTag) // Prefer finding a text based on the specific text tag defined in the mission config.
                           .Fallback(TextManager.Get($"{textTagPrefix}.{TextIdentifier}")) // 2nd option: the "default" format (MissionName.SomeMission).
                           .Fallback(textTag); // Last option: Use the text in the xml as-is with no localization.
                }
            }

            RewardDefinedInXML = ConfigElement.GetAttribute(nameof(Reward)) != null;
            Reward = ConfigElement.GetAttributeInt(nameof(Reward), 1);
            AdditionalExperienceGain = ConfigElement.GetAttributeFloat(nameof(AdditionalExperienceGain), 0f);
            ExperienceMultiplier = ConfigElement.GetAttributeFloat(nameof(ExperienceMultiplier), 1f);
            AllowRetry = ConfigElement.GetAttributeBool(nameof(AllowRetry), false);
            ShowSonarLabels = ConfigElement.GetAttributeBool(nameof(ShowSonarLabels), true);
            ShowInMenusAfterReachingState = ConfigElement.GetAttributeInt(nameof(ShowInMenusAfterReachingState), -1);
            //default to not showing in menus if the mission is configured to show after reaching a state
            ShowInMenus = ConfigElement.GetAttributeBool(nameof(ShowInMenus), ShowInMenusAfterReachingState == -1);
            ShowStartMessage = ConfigElement.GetAttributeBool(nameof(ShowStartMessage), true);
            ShowStartMessageWhenHidden = ConfigElement.GetAttributeBool(nameof(ShowStartMessageWhenHidden), false);
            ShowEndMessage = ConfigElement.GetAttributeEnum(nameof(ShowEndMessage), ShowInMenus ? CompletionCondition.Always : CompletionCondition.Never);
            ShowCompletionState = ConfigElement.GetAttributeBool(nameof(ShowCompletionState), true);
            IsSideObjective = ConfigElement.GetAttributeBool(nameof(IsSideObjective), ConfigElement.GetAttributeBool("sideobjective", false));

            RequireCave = ConfigElement.GetAttributeBool(nameof(RequireCave), false);
            RequireWreck = ConfigElement.GetAttributeBool(nameof(RequireWreck), false);
            RequireThalamusWreck = ConfigElement.GetAttributeBool(nameof(RequireThalamusWreck), false);
            RequireRuin = ConfigElement.GetAttributeBool(nameof(RequireRuin), false);
            RequireBeaconStation = ConfigElement.GetAttributeBool(nameof(RequireBeaconStation), false);
            SpawnBeaconStationInMiddle = ConfigElement.GetAttributeBool(nameof(SpawnBeaconStationInMiddle), false);
            RequireWreck |= RequireThalamusWreck;

            LoadSubmarines = ConfigElement.GetAttributeBool(nameof(LoadSubmarines), true);

            BlockLocationTypeChanges = ConfigElement.GetAttributeBool(nameof(BlockLocationTypeChanges), false);
            RequiredLocationFaction = ConfigElement.GetAttributeIdentifier(nameof(RequiredLocationFaction), Identifier.Empty);
            RequiredDestinationFaction = ConfigElement.GetAttributeIdentifier(nameof(RequiredDestinationFaction), Identifier.Empty);

            Commonness = ConfigElement.GetAttributeInt(nameof(Commonness), 1);
            AllowOtherMissionsInLevel = ConfigElement.GetAttributeBool(nameof(AllowOtherMissionsInLevel), true);
            AllowMultipleInSameLevel = ConfigElement.GetAttributeBool(nameof(AllowMultipleInSameLevel), false);

            DisallowMissionsOfSameType = ConfigElement.GetAttributeBool(nameof(DisallowMissionsOfSameType), false);
            ReplaceOtherMissionsInLevel = ConfigElement.GetAttributeBool(nameof(ReplaceOtherMissionsInLevel), false);
            AllowReplacingWithMissionOfSameType = ConfigElement.GetAttributeBool(nameof(AllowReplacingWithMissionOfSameType), false);
            AllowReplacingWithOtherMissions = ConfigElement.GetAttributeBool(nameof(AllowReplacingWithOtherMissions), false);

            ContinuePathCondition = ConfigElement.GetAttributeEnum(nameof(ContinuePathCondition), CompletionCondition.OnlyWhenCompleted);
            DiscoverPathAsProgressing = ConfigElement.GetAttributeBool(nameof(DiscoverPathAsProgressing), false);

            if (ConfigElement.GetAttribute("difficulty") != null)
            {
                int difficulty = ConfigElement.GetAttributeInt(nameof(Difficulty), MinDifficulty);
                Difficulty = Math.Clamp(difficulty, MinDifficulty, MaxDifficulty);
            }
            MinLevelDifficulty = ConfigElement.GetAttributeInt(nameof(MinLevelDifficulty), MinLevelDifficulty);
            MaxLevelDifficulty = ConfigElement.GetAttributeInt(nameof(MaxLevelDifficulty), MaxLevelDifficulty);
            MinLevelDifficulty = Math.Clamp(MinLevelDifficulty, 0, Math.Min(MaxLevelDifficulty, 100));
            MaxLevelDifficulty = Math.Clamp(MaxLevelDifficulty, Math.Max(MinLevelDifficulty, 0), 100);

            ShowOnMap = ConfigElement.GetAttributeBool(nameof(ShowOnMap), false);
            ShowArrivalTime = ConfigElement.GetAttributeBool(nameof(ShowArrivalTime), true);
            MapNotificationOnUnlock = ConfigElement.GetAttributeString(nameof(MapNotificationOnUnlock), string.Empty);

            TimeLimitWorldSteps = ConfigElement.GetAttributeInt(nameof(TimeLimitWorldSteps), -1);
            CompleteWhenTimeLimitReached = ConfigElement.GetAttributeBool(nameof(CompleteWhenTimeLimitReached), false);

            AllowOutpostNPCs = ConfigElement.GetAttributeBool(nameof(AllowOutpostNPCs), true);
            ForceOutpostGenerationParameters = ConfigElement.GetAttributeIdentifier(nameof(ForceOutpostGenerationParameters), Identifier.Empty);
            ForceOutpostGenerationParametersInMissionMode = ConfigElement.GetAttributeIdentifier(nameof(ForceOutpostGenerationParametersInMissionMode), Identifier.Empty);
            GiveIdCardTagsToEveryoneInMissionMode = ConfigElement.GetAttributeIdentifierArray(nameof(GiveIdCardTagsToEveryoneInMissionMode), []).ToImmutableArray();
            AllowOutpostSelectionFromTag = ConfigElement.GetAttributeIdentifier(nameof(AllowOutpostSelectionFromTag), Identifier.Empty);
            AllowOutpostSelectionProcedurallyGenerated = ConfigElement.GetAttributeBool(nameof(AllowOutpostSelectionProcedurallyGenerated), false);
            DisallowLeavingLocation = ConfigElement.GetAttributeBool(nameof(DisallowLeavingLocation), false);
            DisallowSaving = ConfigElement.GetAttributeBool(nameof(DisallowSaving), false);

            ForceDestructibleOutposts = ConfigElement.GetAttributeBool(nameof(ForceDestructibleOutposts), false);
            ForceAllowStealing = ConfigElement.GetAttributeBool(nameof(ForceAllowStealing), false);
            ForceOutpostFactionInMissionMode = ConfigElement.GetAttributeIdentifier(nameof(ForceOutpostFactionInMissionMode), Identifier.Empty);
            if (ConfigElement.GetAttribute(nameof(ForceOutpostTeamInMissionMode)) != null)
            {
                ForceOutpostTeamInMissionMode = ConfigElement.GetAttributeEnum(nameof(ForceOutpostTeamInMissionMode), CharacterTeamType.FriendlyNPC);
            }

            if (ConfigElement.GetAttribute(nameof(ForceRespawnMode)) != null)
            {
                ForceRespawnMode = ConfigElement.GetAttributeEnum(nameof(ForceRespawnMode), RespawnMode.MidRound);
            }
            ForceRespawnInsideOutpost = ConfigElement.GetAttributeBool(nameof(ForceRespawnInsideOutpost), false);
            ForceTeam1SpawnInsideSub = ConfigElement.GetAttributeBool(nameof(ForceTeam1SpawnInsideSub), false);
            ForceTeam2SpawnInsideSub = ConfigElement.GetAttributeBool(nameof(ForceTeam2SpawnInsideSub), false);

            ShowProgressBar = ConfigElement.GetAttributeBool(nameof(ShowProgressBar), false);
            ShowProgressInNumbers = ConfigElement.GetAttributeBool(nameof(ShowProgressInNumbers), false);
            MaxProgressState = ConfigElement.GetAttributeInt(nameof(MaxProgressState), 1);
            string progressBarLabel = ConfigElement.GetAttributeString(nameof(ProgressBarLabel), "");
            ProgressBarLabel = TextManager.Get(progressBarLabel).Fallback(progressBarLabel);

            string successMessageTag = ConfigElement.GetAttributeString("successmessage", "");
            SuccessMessage = TextManager.Get($"MissionSuccess.{TextIdentifier}");
            if (!string.IsNullOrEmpty(successMessageTag))
            {
                SuccessMessage = SuccessMessage
                                 .Fallback(TextManager.Get(successMessageTag))
                                 .Fallback(successMessageTag);
            }
            SuccessMessage = SuccessMessage.Fallback(TextManager.Get("missioncompleted"));

            string failureMessageTag = ConfigElement.GetAttributeString("failuremessage", "");
            FailureMessage = TextManager.Get($"MissionFailure.{TextIdentifier}");
            if (!string.IsNullOrEmpty(failureMessageTag))
            {
                FailureMessage = FailureMessage
                                 .Fallback(TextManager.Get(failureMessageTag))
                                 .Fallback(failureMessageTag);
            }
            FailureMessage = FailureMessage.Fallback(TextManager.Get("missionfailed"));

            string neutralResultMessageTag   = ConfigElement.GetAttributeString("neutralresultmessage", "");
            NeutralResultMessage = TextManager.Get($"MissionResultNeutral.{TextIdentifier}");
            if (!string.IsNullOrEmpty(neutralResultMessageTag))
            {
                NeutralResultMessage = NeutralResultMessage
                                 .Fallback(TextManager.Get(neutralResultMessageTag))
                                 .Fallback(neutralResultMessageTag);
            }

            string sonarLabelTag = ConfigElement.GetAttributeString("sonarlabel", "");
            SonarLabel = TextManager.Get($"MissionSonarLabel.{sonarLabelTag}")
                         .Fallback(TextManager.Get(sonarLabelTag))
                         .Fallback(TextManager.Get($"MissionSonarLabel.{TextIdentifier}"));
            if (!string.IsNullOrEmpty(sonarLabelTag))
            {
                SonarLabel = SonarLabel.Fallback(sonarLabelTag);
            }

            SonarIconIdentifier = ConfigElement.GetAttributeIdentifier("sonaricon", "");

            CampaignOnly = ConfigElement.GetAttributeBool(nameof(CampaignOnly), false);
            MultiplayerOnly = ConfigElement.GetAttributeBool(nameof(MultiplayerOnly), false);
            SingleplayerOnly = ConfigElement.GetAttributeBool(nameof(SingleplayerOnly), false);

            AchievementIdentifier = ConfigElement.GetAttributeIdentifier("achievementidentifier", "");

            UnhideEntitySubCategories = [.. ConfigElement.GetAttributeStringArray("unhideentitysubcategories", [])];

            List<LocalizedString> headers = [];
            List<LocalizedString> messages = [];
            AllowedConnectionTypes = [];

            for (int i = 0; i < 100; i++)
            {
                LocalizedString header = TextManager.Get($"MissionHeader{i}.{TextIdentifier}");
                LocalizedString message = TextManager.Get($"MissionMessage{i}.{TextIdentifier}");
                if (!message.IsNullOrEmpty())
                {
                    headers.Add(header);
                    messages.Add(message);
                }
            }

            List<ReputationReward> reputationRewards = [];
            List<EventAction> actionsOnComplete = [];
            List<EventAction> actionsOnFailure = [];

            int messageIndex = 0;
            foreach (ContentXElement subElement in ConfigElement.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "message":
                        if (messageIndex >= headers.Count)
                        {
                            headers.Add(string.Empty);
                            messages.Add(string.Empty);
                        }
                        headers[messageIndex] = TextManager.Get($"MissionHeader{messageIndex}.{TextIdentifier}")
                                                .Fallback(TextManager.Get(subElement.GetAttributeString("header", "")))
                                                .Fallback(subElement.GetAttributeString("header", ""));
                        messages[messageIndex] = TextManager.Get($"MissionMessage{messageIndex}.{TextIdentifier}")
                                                 .Fallback(TextManager.Get(subElement.GetAttributeString("text", "")))
                                                 .Fallback(subElement.GetAttributeString("text", ""));
                        messageIndex++;
                        break;
                    case "locationtype":
                    case "connectiontype":
                        if (subElement.GetAttribute("identifier") != null)
                        {
                            AllowedLocationTypes.Add(subElement.GetAttributeIdentifier("identifier", ""));
                        }
                        else
                        {
                            AllowedConnectionTypes.Add((subElement.GetAttributeIdentifier("from", ""), subElement.GetAttributeIdentifier("to", "")));
                        }
                        break;
                    case "locationtypechange":
                        LocationTypeChangeOnCompleted = 
                            new LocationTypeChange(subElement.GetAttributeIdentifier("from", ""), subElement, requireChangeMessages: false, parentDebugName: ToString(), defaultProbability: 1.0f);
                        break;
                    case "reputation":
                    case "reputationreward":
                        reputationRewards.Add(new ReputationReward(subElement));
                        break;
                    case "metadata":
                        Identifier identifier = subElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                        string stringValue = subElement.GetAttributeString("value", string.Empty);
                        if (!string.IsNullOrWhiteSpace(stringValue) && !identifier.IsEmpty)
                        {
                            object value = SetDataAction.ConvertXMLValue(stringValue);
                            SetDataAction.OperationType operation = SetDataAction.OperationType.Set;

                            string operatingString = subElement.GetAttributeString("operation", string.Empty);
                            if (!string.IsNullOrWhiteSpace(operatingString))
                            {
                                operation = (SetDataAction.OperationType)Enum.Parse(typeof(SetDataAction.OperationType), operatingString);
                            }

                            DataRewards.Add((identifier, value, operation));
                        }
                        break;
                    case "triggerevent":
                        TriggerEvents.Add(new TriggerEvent(subElement));
                        break;
                    case "actionsoncomplete":
                    case "actionsonsuccess":
                        ActionsOnCompleteCampaignOnly = subElement.GetAttributeBool("CampaignOnly", false);
                        foreach (var actionElement in subElement.Elements())
                        {
                            var eventAction = EventAction.Instantiate(scriptedEvent: null, actionElement);
                            if (eventAction != null)
                            {
                                actionsOnComplete.Add(eventAction);
                            }
                        }
                        break;
                    case "actionsonfailure":
                        ActionsOnFailureCampaignOnly = subElement.GetAttributeBool("CampaignOnly", false);
                        foreach (var actionElement in subElement.Elements())
                        {
                            var eventAction = EventAction.Instantiate(scriptedEvent: null, actionElement);
                            if (eventAction != null)
                            {
                                actionsOnFailure.Add(eventAction);
                            }
                        }
                        break;
                }
            }

            Headers = [.. headers];
            Messages = [.. messages];
            ReputationRewards = [.. reputationRewards];
            ActionsOnComplete = [.. actionsOnComplete];
            ActionsOnFailure = [.. actionsOnFailure];

            MissionClass = FindMissionClass(ConfigElement);
            Type = ConfigElement.GetAttributeIdentifier(nameof(Type), Identifier.Empty);

#if DEBUG
            if (MissionClass == typeof(MonsterMission) && SonarLabel.IsNullOrEmpty())
            {
                DebugConsole.AddWarning($"Potential error in mission prefab \"{Identifier}\" - sonar label not set.");
            }
            if (Type == "Encounter".ToIdentifier() && !CampaignOnly)
            {
                DebugConsole.AddWarning(
                    $"Potential error in mission prefab \"{Identifier}\" - encounter mission not configured as {CampaignOnly}." +
                    " Encounter missions don't generally have a clear objective (the player is free to ignore the sub), so they might not make sense to be playable in the mission mode.");
                
            }
            if (Type.Contains("OutpostDefense") && AllowedLocationTypes.Any() && !ForceDestructibleOutposts)
            {
                DebugConsole.AddWarning(
                    $"Potential error in mission prefab \"{Identifier}\" - encounter mission appears in outpost levels, but does not force outposts to be damageable (see {ForceDestructibleOutposts})." +
                    $" This means the outpost can be indestructible if the {nameof(ServerSettings.DestructibleOutposts)} setting is disabled.");                
            }
#endif
            if (!LoadSubmarines && MissionClass != typeof(CombatMission))
            {
                DebugConsole.AddWarning($"Potential error in mission {Identifier}: Disabling submarines is only intended for combat missions taking place in an outpost, and may lead to issues in other types of missions.",
                    contentPackage: ConfigElement.ContentPackage);
            }

            constructor = FindMissionConstructor(ConfigElement, MissionClass);
            if (constructor == null)
            {
                DebugConsole.ThrowError($"Failed to find a constructor for the mission type \"{Type}\"!",
                    contentPackage: ConfigElement.ContentPackage);
            }

#if CLIENT
            ParseConfigElementClient(ConfigElement, variantOf);
#endif
        }

        private Type FindMissionClass(ContentXElement element)
        {
            Type type;
            Identifier typeName = element.NameAsIdentifier();
            type = TryGetClass(typeName.RemoveFromEnd("Mission"));

            if (type == null)
            {
                //backwards compatibility: the actual mission class used to be defined by the "type" attribute,
                //Now the mission class is defined by the name of the mission element, and the type can be any arbitrary string,
                //but if we failed to find the class based on the name, let's try the type attribute.
                Identifier typeNameLegacy = (element.GetAttributeIdentifier("type", Identifier.Empty)).ToIdentifier();
                if (typeNameLegacy == "OutpostDestroy" || typeNameLegacy == "OutpostRescue")
                {
                    typeNameLegacy = "AbandonedOutpost".ToIdentifier();
                }
                else if (typeNameLegacy == "clearalienruins")
                {
                    typeNameLegacy = "EliminateTargets".ToIdentifier();
                }
                type = TryGetClass(typeNameLegacy) ?? TryGetClass(typeNameLegacy.AppendIfMissing("Mission"));
                if (type == null)
                {
                    DebugConsole.ThrowError($"Failed to find the mission type \"{typeNameLegacy}\" for the mission {Identifier}.",
                        contentPackage: element.ContentPackage);
                    return null;
                }
            }

            static Type TryGetClass(Identifier typeName)
            {
                if (CoOpMissionClasses.TryGetValue(typeName, out Type coOpMissionClass))
                {
                    return coOpMissionClass;
                }
                else if (PvPMissionClasses.TryGetValue(typeName, out Type pvpMissionClass))
                {
                    return pvpMissionClass;
                }
                return null;
            }

            return type;
        }

        private ConstructorInfo FindMissionConstructor(ContentXElement element, Type missionClass)
        {
            ConstructorInfo constructor;
            if (missionClass == null) { return null; }
            if (missionClass != typeof(Mission) && !missionClass.IsSubclassOf(typeof(Mission))) { return null; }
            constructor = missionClass.GetConstructor(new Type[] { typeof(MissionPrefab), typeof(Location[]), typeof(Submarine) });
            if (constructor == null)
            {
                DebugConsole.ThrowError(
                    $"Could not find the constructor of the mission type \"{missionClass}\" for the mission {Identifier}",
                    contentPackage: element.ContentPackage);
                return null;
            }
            return constructor;
        }

        public bool IsAllowed(Location from, Location to)
        {
            if (from == to)
            {
                if (!RequiredLocationFaction.IsEmpty && from.Faction?.Prefab.Identifier != RequiredLocationFaction)
                {
                    return false;
                }
                return
                    AllowedLocationTypes.Any(lt => lt == Barotrauma.Tags.AnyLocation) ||
                    AllowedLocationTypes.Any(lt => lt == Barotrauma.Tags.AnyOutpost && from.HasOutpost() && from.Type.IsValidForAnyOutpostEventOrMission) ||
                    AllowedLocationTypes.Any(lt => lt == from.Type.Identifier);
            }

            foreach (var (fromType, toType) in AllowedConnectionTypes)
            {
                if (fromType == Barotrauma.Tags.AnyLocation ||
                    fromType == from.Type.Identifier ||
                    (fromType == Barotrauma.Tags.AnyOutpost && from.HasOutpost() && from.Type.IsValidForAnyOutpostEventOrMission && from.Type.Identifier != "abandoned"))
                {
                    if (toType == Barotrauma.Tags.AnyLocation ||
                        toType == to.Type.Identifier ||
                        (toType == Barotrauma.Tags.AnyOutpost && to.HasOutpost() && to.Type.IsValidForAnyOutpostEventOrMission && to.Type.Identifier != "abandoned"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        
        /// <summary>
        /// Inclusive (matching the min an max values is accepted).
        /// </summary>
        public bool IsAllowedDifficulty(float difficulty) => difficulty >= MinLevelDifficulty && difficulty <= MaxLevelDifficulty;

        public Mission Instantiate(Location[] locations, Submarine sub)
        {
            return constructor?.Invoke(new object[] { this, locations, sub }) as Mission;
        }

        partial void DisposeProjectSpecific();
        public override void Dispose()
        {
            DisposeProjectSpecific();
        }

        /// <summary>
        /// Returns all mission types that can be selected in the server lobby, excluding any special, hidden ones like EndMission 
        /// (the mission at the end of the campaign)
        /// </summary>
        public static IEnumerable<Identifier> GetAllMultiplayerSelectableMissionTypes()
        {
            List<Identifier> missionTypes = [];
            foreach (var missionPrefab in Prefabs)
            {
                if (missionPrefab.Commonness <= 0.0f) { continue; }
                if (missionPrefab.CampaignOnly) { continue; }
                if (missionPrefab.SingleplayerOnly) { continue; }
                if (HiddenMissionTypes.Contains(missionPrefab.Type))
                {
                    continue;
                }
                if (!missionTypes.Contains(missionPrefab.Type)) 
                {
                    missionTypes.Add(missionPrefab.Type);
                }
            }
            return missionTypes.OrderBy(t => t.Value);
        }

        /// <summary>
        /// Checks mission prefabs for errors. Must be ran after all content has been loaded, because the checks rely on e.g. location types being loaded.
        /// </summary>
        public static void CheckForErrors()
        {
            var allowedLocationTypes = LocationType.Prefabs.Where(lt => lt.AllowInRandomLevels).ToList();
            foreach (var missionPrefab in Prefabs)
            {
                if (missionPrefab.CampaignOnly) { continue; }
                
                if (missionPrefab.AllowedLocationTypes.Any())
                {
                    if (allowedLocationTypes
                        .None(lt => missionPrefab.AllowedLocationTypes.Any(allowedLocationType => LocationTypeMatches(lt, allowedLocationType))))
                    {
                        DebugConsole.ThrowError(
                            $"Error in mission {missionPrefab.Identifier}: the mission is not campaign-only, "+
                            $"but is only allowed in locations taking place in campaign location types ({string.Join(", ", missionPrefab.AllowedLocationTypes.Select(lt => lt.Value))}). "+
                            $"The mission is probably missing CampaignOnly=\"true\".",
                            contentPackage: missionPrefab.ContentPackage);
                    }
                }
                if (missionPrefab.AllowedConnectionTypes.Any())
                {
                    if (allowedLocationTypes
                        .None(lt => missionPrefab.AllowedConnectionTypes.Any(allowedLocationTypes => allowedLocationTypes.from.IsEmpty || LocationTypeMatches(lt, allowedLocationTypes.from))))
                    {
                        DebugConsole.ThrowError(
                            $"Error in mission {missionPrefab.Identifier}: the mission is not campaign-only, " +
                            $"but it only allows campaign location types as the start location ({string.Join(", ", missionPrefab.AllowedConnectionTypes.Select(m => m.from.Value))}). " +
                            $"The mission is probably missing CampaignOnly=\"true\".",
                            contentPackage: missionPrefab.ContentPackage);
                    }
                    if (allowedLocationTypes
                        .None(lt => missionPrefab.AllowedConnectionTypes.Any(allowedLocationTypes => allowedLocationTypes.to.IsEmpty || LocationTypeMatches(lt, allowedLocationTypes.to))))
                    {
                        DebugConsole.ThrowError(
                            $"Error in mission {missionPrefab.Identifier}: the mission is not campaign-only, " +
                            $"but it only allows campaign location types as the end location ({string.Join(", ", missionPrefab.AllowedConnectionTypes.Select(m => m.to.Value))}). " +
                            $"The mission is probably missing CampaignOnly=\"true\".",
                            contentPackage: missionPrefab.ContentPackage);
                    }
                }
            }
            static bool LocationTypeMatches(LocationType lt, Identifier id) =>
              id == lt.Identifier ||
              id == Barotrauma.Tags.AnyLocation ||
              (id == Barotrauma.Tags.AnyOutpost && lt.HasOutpost);
        }

        public override string ToString()
        {
            return $"{nameof(MissionPrefab)} ({Identifier})";
        }
    }
}
