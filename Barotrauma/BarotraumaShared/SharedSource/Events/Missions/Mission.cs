using Barotrauma.Abilities;
using Barotrauma.Extensions;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public abstract partial class Mission
    {
        /// <summary>
        /// Used by certain mission types where the win/fail conditions are configurable.
        /// </summary>
        public enum MissionResultCondition
        {
            CrewEliminated,
            AnyoneFromCrewSurvived,
            /// <summary>
            /// Means the mission cannot succeed (or fail, if used as the failure condition).
            /// </summary>
            Never,
            /// <summary>
            /// Makes the mission always succeed at the end of the round as long as the failure condition is not met. If used as the failure condition, the mission always fails.
            /// </summary>
            Always,
            /// <summary>
            /// The submarine is flooded and there's only a few living crew members on board, and no-one currently assaulting inside the enemy sub/outpost.
            /// Or, the submarine has sunken way below the bottom of the level.
            /// </summary>
            SubmarineIncapacitated,
            /// <summary>
            /// The opposite of <see cref="SubmarineIncapacitated"/>: makes the mission succeed if the submarine wasn't incapacitated (or fail, if used as a failure condition).
            /// </summary>
            SubmarineNotIncapacitated,
            /// <summary>
            /// The outpost is considered destroyed when most of the NPCs are dead or when the reactor has been blown up.
            /// </summary>
            OutpostDestroyed
        }


        public readonly MissionPrefab Prefab;
        private bool completed;
        protected bool failed;

        protected Level level;

        public Level Level => level;

        /// <summary>
        /// The condition that must be met for the mission to fail. 
        /// Note that not winning is not the same as failing - a mission can also be neither success or failure, in which case it can be reattempted.
        /// </summary>
        public MissionResultCondition FailureCondition = MissionResultCondition.Never;

        protected int state;
        public virtual int State
        {
            get { return state; }
            set
            {
                if (state != value)
                {
                    int previousState = state;
                    state = value;
                    TryTriggerEvents(state);
#if SERVER
                    GameMain.Server?.UpdateMissionState(this);
#elif CLIENT
                    if (Prefab.ShowProgressBar)
                    {
                        CharacterHUD.ShowMissionProgressBar(this);
                    }

                    // If the mission start message was hidden when it started, it may have become unhidden now due to the state change
                    ShowMissionStartMessageIfNeeded();

#endif
                    ShowMessage(State);
                    OnMissionStateChanged?.Invoke(this);
                    MissionStateChanged(previousState);
                }
            }
        }

        public int TimesAttempted { get; set; }

        protected static bool IsClient => GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient;

        protected readonly CheckDataAction completeCheckDataAction;

        public readonly ImmutableArray<LocalizedString> Headers;
        public ImmutableArray<LocalizedString> Messages { get; private set; }

        /// <summary>
        /// The reward that was actually given from completing the mission, taking any talent bonuses into account 
        /// (some of which may not be possible to determine in advance)
        /// </summary>
        private int? finalReward;

        public virtual LocalizedString Name => Prefab.Name;

        public RichString NameWithContentPackage
        {
            get
            {
                RichString missionName = Name;
                if (Prefab.ContentPackage != ContentPackageManager.VanillaCorePackage)
                {
                    missionName = $"{missionName}\n‖color:{Prefab.ContentPackage.GetAccentColor().ToStringHex()}‖[{Prefab.ContentPackage?.Name}]‖color:end‖";
                }
                return missionName;
            }
        }

        private LocalizedString successMessage;
        /// <summary>
        /// Shown if the mission has been <see cref="Mission.Completed">completed successfully</see>.
        /// </summary>
        public virtual LocalizedString SuccessMessage
        {
            get { return successMessage; }
        }

        private LocalizedString failureMessage;

        /// <summary>
        /// Shown if the mission has <see cref="Mission.Failed">failed</see>, or if the mission was not completed successfully and <see cref="FailureMessage"/> is not defined.
        /// </summary>
        public virtual LocalizedString FailureMessage
        {
            get { return failureMessage; }
        }

        private LocalizedString neutralResultMessage;
        /// <summary>
        /// Shown if the mission was neither <see cref="Mission.Completed">completed successfully</see> or <see cref="Mission.Failed">failed</see>. 
        /// If not defined, the <see cref="FailureMessage"/> is used if the mission was not completed.
        /// </summary>
        public virtual LocalizedString NeutralResultMessage
        {
            get { return neutralResultMessage; }
        }

        protected LocalizedString description;
        public virtual LocalizedString Description
        {
            get { return description; }
        }

        protected LocalizedString descriptionWithoutReward;
        
        public bool ShowInMenus =>
            Prefab.ShowInMenusAfterReachingState > -1 ?
                state >= Prefab.ShowInMenusAfterReachingState :
                Prefab.ShowInMenus;

        public virtual bool AllowUndocking
        {
            get { return true; }
        }

        public bool DisallowLeavingLocation
        {
            get =>  Prefab.DisallowLeavingLocation && !completed;
        }

        public bool DisallowSaving
        {
            get => Prefab.DisallowSaving && !completed;
        }

        public virtual int Reward
        {
            get 
            {
                return Prefab.Reward;
            }
        }

        public ImmutableList<MissionPrefab.ReputationReward> ReputationRewards
        {
            get { return Prefab.ReputationRewards; }
        }

        public bool Completed
        {
            get { return completed; }
            set { completed = value; }
        }

        /// <summary>
        /// Does the mission currently prevent the locations it takes place in from changing their type? See <see cref="MissionPrefab.BlockLocationTypeChanges"/>.
        /// </summary>
        public virtual bool BlocksLocationTypeChanges => !Completed && Prefab.BlockLocationTypeChanges;

        public bool Failed
        {
            get { return failed || ForceFailure; }
        }

        public bool ForceFailure;
        public bool ForceRetry;

        public virtual bool AllowRespawning
        {
            get { return true; }
        }

        public virtual IEnumerable<(LocalizedString Label, Vector2 Position)> SonarLabels
        {
            get { return Enumerable.Empty<(LocalizedString Label, Vector2 Position)>(); }
        }

        public Identifier SonarIconIdentifier => Prefab.SonarIconIdentifier;

        /// <summary>
        /// Where was this mission received from? Affects which faction we give reputation for if the mission is configured to give reputation for the faction that gave the mission.
        /// Defaults to Locations[0]
        /// </summary>
        public Location OriginLocation;

        public readonly Location[] Locations;

        /// <summary>
        /// The actual, final destination of the mission: may be something else than Locations[1] on multi-level missions (see <see cref="PathToDestination"/>)
        /// </summary>
        public Location Destination => PathToDestination == null || PathToDestination.Length < 2 ? Locations[1] : PathToDestination.Last();

        /// <summary>
        /// If set, this mission is treated as a "chain" of missions leading up to the destination location.
        /// In practice, when this mission instance completes, an identical mission is placed on the next LocationConnection towards the destination.
        /// </summary>
        public ImmutableArray<Location> PathToDestination { get; private set; }

        /// <summary>
        /// Is there more path to the destination remaining after the current level (i.e. is this a multi-level mission that's not yet at the last level?)
        /// </summary>
        public bool MorePathToDestinationRemaining => PathToDestination != null && PathToDestination.Length > 2;

        public bool TimeLimitReached => Prefab.TimeLimitWorldSteps > -1 && WorldStepsSinceUnlocked >= Prefab.TimeLimitWorldSteps;

        /// <summary>
        /// How many world steps until the mission expires (= is removed from the available missions with no other effects).
        /// Unlike <see cref="MissionPrefab.TimeLimitWorldSteps"/>, the expiry doesn't progress while the mission is available in the players' location or being played.
        /// -1 = never expires. Set by <see cref="MissionAction.ExpireAfter"/>.
        /// </summary>
        public int WorldStepsUntilExpiry { get; set; } = -1;

        public bool Expired => WorldStepsUntilExpiry == 0;

        /// <summary>
        /// How many world steps until the mission reaches its final destination (e.g. until a transport sub arrives at its destination)?
        /// Takes into account the remaining levels on multi-level missions. Null if the mission has no time limit.
        /// </summary>
        public int? GetWorldStepsUntilDestinationReached()
        {
            if (Prefab.TimeLimitWorldSteps < 0) { return null; }
            int steps = Math.Max(Prefab.TimeLimitWorldSteps - WorldStepsSinceUnlocked, 0);
            if (MorePathToDestinationRemaining)
            {
                int currentDestinationIndex = PathToDestination.IndexOf(Locations[1]);
                if (currentDestinationIndex >= 0)
                {
                    //each of the remaining levels takes the full time limit (a new mission instance is unlocked for each one)
                    int remainingLevels = PathToDestination.Length - 1 - currentDestinationIndex;
                    steps += remainingLevels * Prefab.TimeLimitWorldSteps;
                }
            }
            return steps;
        }

        public int WorldStepsSinceUnlocked { get; set; }

        public virtual bool RequiresSpecificWreck { get; }

        public int? Difficulty
        {
            get { return Prefab.Difficulty; }
        }

        public bool missionMessageShown = false;

        /// <summary>
        /// Is this mission currently in a state that should disallow <see cref="AIObjectiveExploreOutside"/> in outpost levels?
        /// E.g. to prevent bots from going out fishing when there's monsters or a hostile sub attacking the outpost.
        /// </summary>
        public virtual bool DisallowExploreOutsideOutpost => false;

        private class DelayedTriggerEvent
        {
            public readonly MissionPrefab.TriggerEvent TriggerEvent;
            public float Delay;

            public DelayedTriggerEvent(MissionPrefab.TriggerEvent triggerEvent, float delay)
            {
                TriggerEvent = triggerEvent;
                Delay = delay;
            }
        }

        private readonly List<DelayedTriggerEvent> delayedTriggerEvents = new List<DelayedTriggerEvent>();

        public Action<Mission> OnMissionStateChanged;
        
        protected readonly ContentXElement characterConfig;
        protected readonly List<Character> characters = new List<Character>();
        protected readonly Dictionary<Character, List<Item>> characterItems = new Dictionary<Character, List<Item>>();

        public enum OutpostDestroyedState
        {
            None,
            ReactorDestroyed,
            TooManyNPCsKilled,
            TooFlooded
        }

        /// <summary>
        /// Why was the outpost considered destroyed? Only relevant in outpost missions with the <see cref="FailureCondition"/> set to <see cref="MissionResultCondition.OutpostDestroyed"/>.
        /// </summary>
        public OutpostDestroyedState OutpostDestroyedReason { get; set; } = OutpostDestroyedState.None;

        /// <summary>
        /// The amount of crew left below which the submarine is considered incapacitated for the purposes of <see cref="MissionResultCondition.SubmarineIncapacitated"/>.
        /// E.g. a value of 0.2 would mean that the sub is considered incapacitated if it's flooded and there's less than 20% of the initial crew alive and on board.
        /// </summary>
        private readonly float submarineIncapacitatedWhenCrewBelowThreshold;

        /// <summary>
        /// The percentage of the sub that needs to be flooded for the purposes of <see cref="MissionResultCondition.SubmarineIncapacitated"/>.
        /// E.g. a value of 0.5 would mean that the sub is considered incapacitated if more than 50% of it's volume is flooded and the crew is below <see cref="submarineIncapacitatedWhenCrewBelowThreshold"/>.
        /// </summary>
        private readonly float submarineIncapacitatedWhenSubFloodedThreshold;

        /// <summary>
        /// The amount of outpost NPCs left below which the outpost is considered destroyed for the purposes of <see cref="MissionResultCondition.OutpostDestroyed"/>.
        /// E.g. a value of 0.2 would mean that the outpost is considered destroyed if less than 20% of the NPCs are alive.
        /// </summary>
        private readonly float outpostDestroyedWhenNPCsBelowThreshold;

        /// <summary>
        /// The percentage of the sub that needs to be flooded for it to be considered destroyed for the purposes of <see cref="MissionResultCondition.OutpostDestroyed"/>.
        /// E.g. a value of 0.8 would mean that the outpost is considered destroyed if more than 80% of it's volume is flooded.
        /// </summary>
        private readonly float outpostDestroyedWhenFloodedThreshold;

        private Item outpostReactor;

        private int? initialOutpostDwellerCount;

        public Mission(MissionPrefab prefab, Location[] locations, Submarine sub)
        {
            System.Diagnostics.Debug.Assert(locations.Length == 2);

            Prefab = prefab;
            Headers = prefab.Headers;
            OriginLocation = locations[0];
            Locations = locations;

            submarineIncapacitatedWhenCrewBelowThreshold = prefab.ConfigElement.GetAttributeFloat(nameof(submarineIncapacitatedWhenCrewBelowThreshold), 0.2f);
            submarineIncapacitatedWhenSubFloodedThreshold = prefab.ConfigElement.GetAttributeFloat(nameof(submarineIncapacitatedWhenSubFloodedThreshold), 0.5f);
            outpostDestroyedWhenNPCsBelowThreshold = prefab.ConfigElement.GetAttributeFloat(nameof(outpostDestroyedWhenNPCsBelowThreshold), 0.1f);
            outpostDestroyedWhenFloodedThreshold = prefab.ConfigElement.GetAttributeFloat(nameof(outpostDestroyedWhenFloodedThreshold), 0.8f);

            FailureCondition = prefab.ConfigElement.GetAttributeEnum(nameof(FailureCondition), MissionResultCondition.Never);

            var endConditionElement = prefab.ConfigElement.GetChildElement(nameof(completeCheckDataAction));
            if (endConditionElement != null)
            {
                completeCheckDataAction = new CheckDataAction(endConditionElement, $"Mission ({prefab.Identifier})");
            }

            RefreshDescriptionAndMessages(sub);

            characterConfig = prefab.ConfigElement.GetChildElement("Characters");
            if (prefab.ConfigElement.GetChildElements("Characters").Count() > 1)
            {
                DebugConsole.AddWarning($"Error in mission {Prefab.Identifier}: multiple <Characters> elements found. Only the first one will be used.",
                    contentPackage: prefab.ContentPackage);
            }
        }

        private void RefreshDescriptionAndMessages(Submarine sub)
        {
            descriptionWithoutReward = ReplaceVariablesInMissionMessage(Prefab.Description.Value, sub, replaceReward: false);
            description = ReplaceVariablesInMissionMessage(Prefab.Description.Value, sub);
            successMessage = ReplaceVariablesInMissionMessage(Prefab.SuccessMessage.Value, sub);
            failureMessage = ReplaceVariablesInMissionMessage(Prefab.FailureMessage.Value, sub);
            neutralResultMessage = ReplaceVariablesInMissionMessage(Prefab.NeutralResultMessage.Value, sub);

            var messages = Prefab.Messages.ToArray();
            for (int m = 0; m < messages.Length; m++)
            {
                messages[m] = ReplaceVariablesInMissionMessage(messages[m], sub);
            }
            Messages = messages.ToImmutableArray();
        }

        public LocalizedString ReplaceVariablesInMissionMessage(LocalizedString message, Submarine sub, bool replaceReward = true)
        {
            for (int locationIndex = 0; locationIndex < 2; locationIndex++)
            {
                Location location = Locations[locationIndex];
                string locationName = $"‖color:gui.orange‖{location.DisplayName}‖end‖";
                message = message.Replace("[location" + (locationIndex + 1) + "]", locationName);
            }
            if (Destination != null)
            {
                string destinationName = $"‖color:gui.orange‖{Destination.DisplayName}‖end‖";
                message = message.Replace("[destination]", destinationName);
            }
            if (replaceReward)
            {
                string rewardText = $"‖color:gui.orange‖{string.Format(CultureInfo.InvariantCulture, "{0:N0}", GetReward(sub))}‖end‖";
                message = message.Replace("[reward]", rewardText);
            }
            return message;
        }

        protected virtual void MissionStateChanged(int previousState) {}

        public static Mission LoadRandom(Location[] locations, string seed, bool requireCorrectLocationType, IEnumerable<Identifier> missionTypes, 
            bool isSinglePlayer = false, bool isCampaign = false, float? difficultyLevel = null)
        {
            return LoadRandom(locations, new MTRandom(ToolBox.StringToInt(seed)), requireCorrectLocationType, missionTypes, isSinglePlayer, isCampaign, difficultyLevel);
        }

        public static Mission LoadRandom(Location[] locations, MTRandom rand, bool requireCorrectLocationType, IEnumerable<Identifier> missionTypes, 
            bool isSinglePlayer = false, bool isCampaign = false, float? difficultyLevel = null)
        {
            List<MissionPrefab> allowedMissions = new List<MissionPrefab>();
            if (missionTypes.None())
            {
                return null;
            }
            else
            {
                allowedMissions.AddRange(MissionPrefab.Prefabs.Where(m => missionTypes.Contains(m.Type)));
            }
            if (!isCampaign) { allowedMissions.RemoveAll(m => m.CampaignOnly); }
            allowedMissions.RemoveAll(m => isSinglePlayer ? m.MultiplayerOnly : m.SingleplayerOnly);
            if (requireCorrectLocationType)
            {
                allowedMissions.RemoveAll(m => !m.IsAllowed(locations[0], locations[1]));
            }
            if (difficultyLevel.HasValue)
            {
                allowedMissions.RemoveAll(m => !m.IsAllowedDifficulty(difficultyLevel.Value));
            }
            if (allowedMissions.Count == 0) { return null; }
            MissionPrefab missionPrefab = ToolBox.SelectWeightedRandom(allowedMissions, m => m.Commonness, rand);
            return missionPrefab.Instantiate(locations, Submarine.MainSub);
        }

        /// <summary>
        /// Calculates the base reward, can be overridden for different mission types
        /// </summary>
        public virtual float GetBaseReward(Submarine sub)
        {
            return Prefab.Reward;
        }

        /// <summary>
        /// Calculates the available monetary reward, taking into account universal modifiers such as campaign settings.
        /// </summary>
        public int GetReward(Submarine sub)
        {
            float reward = GetBaseReward(sub);
            // Some modifiers should apply universally to all implementations of GetBaseReward
            if (GameMain.GameSession?.Campaign is CampaignMode campaign)
            {
                reward *= campaign.Settings.MissionRewardMultiplier;
            }
            return (int)Math.Round(reward);
        }
        
        /// <summary>
        /// Call to load character elements to be spawned. Has to be implemented (and synced) separately per each mission.
        /// </summary>
        protected void InitCharacters(Submarine submarine)
        {
            characters.Clear();
            characterItems.Clear();

            //use level seed + mission id as the seed -> same set of monsters appears if you retry the same mission
            var rand = new MTRandom(ToolBox.StringToInt(Level.Loaded?.Seed ?? 0 + Prefab.Identifier.Value));

            if (characterConfig != null) 
            { 
                foreach (XElement element in characterConfig.Elements())
                {
                    if (GameMain.NetworkMember == null && element.GetAttributeBool("multiplayeronly", false)) { continue; }

                    int defaultCount = element.GetAttributeInt("count", -1);
                    if (defaultCount < 0)
                    {
                        defaultCount = element.GetAttributeInt("amount", 1);
                    }
                    int min = Math.Min(element.GetAttributeInt("min", defaultCount), 255);
                    int max = Math.Min(Math.Max(min, element.GetAttributeInt("max", defaultCount)), 255);
                    int count = rand.Next(min, max + 1);

                    if (element.Attribute("identifier") != null && element.Attribute("from") != null)
                    {
                        HumanPrefab humanPrefab = GetHumanPrefabFromElement(element); 
                        if (humanPrefab == null)
                        {
                            DebugConsole.ThrowError($"Couldn't spawn a human character for a mission: human prefab \"{element.GetAttributeString("identifier", string.Empty)}\" not found",
                                contentPackage: Prefab.ContentPackage);
                            continue;
                        }
                        for (int i = 0; i < count; i++)
                        {
                            LoadHuman(humanPrefab, element, submarine);
                        }
                    }
                    else
                    {
                        Identifier speciesName = element.GetAttributeIdentifier("character", element.GetAttributeIdentifier("identifier", Identifier.Empty));
                        var characterPrefab = CharacterPrefab.FindBySpeciesName(speciesName);
                        if (characterPrefab == null)
                        {
                            DebugConsole.ThrowError($"Couldn't spawn a character for a mission: character prefab \"{speciesName}\" not found",
                                contentPackage: Prefab.ContentPackage);
                            continue;
                        }
                        for (int i = 0; i < count; i++)
                        {
                            LoadMonster(characterPrefab, element, submarine);
                        }
                    }
                }
            }            
        }
        
        private SpawnAction.SpawnLocationType GetSpawnLocationTypeFromSubmarineType(Submarine sub)
        {
            return sub.Info.Type switch
            {
                SubmarineType.Outpost or SubmarineType.OutpostModule => SpawnAction.SpawnLocationType.Outpost,
                SubmarineType.Wreck => SpawnAction.SpawnLocationType.Wreck,
                SubmarineType.Ruin => SpawnAction.SpawnLocationType.Ruin,
                SubmarineType.BeaconStation => SpawnAction.SpawnLocationType.BeaconStation,
                SubmarineType.Player => SpawnAction.SpawnLocationType.MainSub,
                _ => SpawnAction.SpawnLocationType.Any
            };
        }

        protected virtual Character LoadHuman(HumanPrefab humanPrefab, XElement element, Submarine submarine)
        {
            Identifier[] moduleFlags = element.GetAttributeIdentifierArray("moduleflags", null);
            Identifier[] spawnPointTags = element.GetAttributeIdentifierArray("spawnpointtags", null);
            var spawnPointType = element.GetAttributeEnum("spawnpointtype", SpawnType.Human);
            ISpatialEntity spawnPos = SpawnAction.GetSpawnPos(
                GetSpawnLocationTypeFromSubmarineType(submarine), spawnPointType,
                moduleFlags ?? humanPrefab.GetModuleFlags(),
                spawnPointTags ?? humanPrefab.GetSpawnPointTags(),
                element.GetAttributeBool("asfaraspossible", false),
                debugLogName: $"Outpost NPC ({humanPrefab.Identifier})");
            spawnPos ??= submarine.GetHulls(alsoFromConnectedSubs: false).GetRandomUnsynced();
            var teamId = element.GetAttributeEnum("teamid", CharacterTeamType.None);
            var originalTeam = Level.Loaded.StartOutpost?.TeamID ?? teamId;
            Character spawnedCharacter = CreateHuman(humanPrefab, characters, characterItems, submarine, originalTeam, spawnPos);
            //consider the NPC to be "originally" from the team of the outpost it spawns in, and change it to the desired (hostile) team afterwards
            //that allows the NPC to fight intruders and otherwise function in the outpost if the mission is configured to spawn the hostile NPCs in a friendly outpost
            if (teamId != originalTeam)
            {
                spawnedCharacter.SetOriginalTeamAndChangeTeam(teamId, processImmediately: true);
            }
            if (element.GetAttribute("color") != null)
            {
                spawnedCharacter.UniqueNameColor = element.GetAttributeColor("color", Color.Red);
            }
            if (submarine.Info is { IsOutpost: true } outpostInfo)
            {
                outpostInfo.AddOutpostNPC(spawnedCharacter);
            }
            if (spawnPos is WayPoint wp)
            {
                spawnedCharacter.GiveIdCardTags(wp);
            }
            InitCharacter(spawnedCharacter, element);
            return spawnedCharacter;
        }

        protected virtual Character LoadMonster(CharacterPrefab monsterPrefab, XElement element, Submarine submarine)
        {
            Identifier[] moduleFlags = element.GetAttributeIdentifierArray("moduleflags", null);
            Identifier[] spawnPointTags = element.GetAttributeIdentifierArray("spawnpointtags", null);

            ISpatialEntity spawnPos = SpawnAction.GetSpawnPos(GetSpawnLocationTypeFromSubmarineType(submarine), SpawnType.Enemy, moduleFlags, spawnPointTags, element.GetAttributeBool("asfaraspossible", false));
            spawnPos ??= submarine.GetHulls(alsoFromConnectedSubs: false).GetRandomUnsynced();
            Character spawnedCharacter = Character.Create(monsterPrefab.Identifier, spawnPos.WorldPosition, ToolBox.RandomSeed(8), createNetworkEvent: false);
            characters.Add(spawnedCharacter);
            if (spawnedCharacter.Inventory != null)
            {
                characterItems.Add(spawnedCharacter, spawnedCharacter.Inventory.FindAllItems(recursive: true));
            }
            if (spawnedCharacter.AIController is EnemyAIController enemyAi && submarine != null)
            {
                enemyAi.SetUnattackableSubmarines(submarine);
            }
            InitCharacter(spawnedCharacter, element);
            return spawnedCharacter;
        }
        
        protected virtual void InitCharacter(Character character, XElement element)
        {
            if (element.GetAttributeBool(Tags.IgnoredByAI.Value, false))
            {
                character.AddAbilityFlag(AbilityFlags.IgnoredByEnemyAI);
            }
            float playDeadProbability = element.GetAttributeFloat("playdeadprobability", -1);
            if (playDeadProbability >= 0)
            {
                character.EvaluatePlayDeadProbability(playDeadProbability);
            }
            float huskProbability = element.GetAttributeFloat("huskprobability", 0);
            if (huskProbability > 0 && Rand.Value() <= huskProbability)
            {
                character.TurnIntoHusk();
            }
            else if (element.GetAttributeBool("corpse", false))
            {
                character.Kill(CauseOfDeathType.Unknown, causeOfDeathAffliction: null, log: false);
            }
        }

        public void Start(Level level)
        {
            state = 0;
            initialOutpostDwellerCount = null;
#if CLIENT
            shownMessages.Clear();
#endif
            delayedTriggerEvents.Clear();
            foreach (string categoryToShow in Prefab.UnhideEntitySubCategories)
            {
                foreach (MapEntity entityToShow in MapEntity.MapEntityList.Where(me => me.Prefab?.HasSubCategory(categoryToShow) ?? false))
                {
                    entityToShow.SetDisabled(disabled: false);
                }
            }
            this.level = level;
            TryTriggerEvents(0);

            if (level.Type == LevelData.LevelType.Outpost && Level.Loaded.StartOutpost is { } startOutpost)
            {
                if (GameMain.GameSession.GameMode is not CampaignMode && Prefab.ForceOutpostTeamInMissionMode.HasValue)
                {
                    Submarine.SetOutpostTeam(startOutpost, Prefab.ForceOutpostTeamInMissionMode.Value);
                    if (startOutpost.TeamID == CharacterTeamType.Team1)
                    {
                        OutpostGenerator.SetDevicesDetachable(startOutpost, disableDetaching: false);
                        OutpostGenerator.SetWiresLocked(startOutpost, isLocked: false);
                        OutpostGenerator.SetDevicesPlayerTeamInteractable(startOutpost);
                        OutpostGenerator.SetAllowSellingOutpostItems(startOutpost, allowSelling: true);
                    }
                }
            }

            if (level.StartOutpost is { } outpost)
            {
                outpostReactor = outpost.GetItems(alsoFromConnectedSubs: false).FirstOrDefault(it => it.HasTag(Tags.Reactor));
            }

            StartMissionSpecific(level);
        }

        protected virtual void StartMissionSpecific(Level level) { }

        public void Update(float deltaTime)
        {
            if (!initialOutpostDwellerCount.HasValue && Level.Loaded?.StartOutpost != null)
            {
                initialOutpostDwellerCount = FindOutpostDwellers().Count();
            }

            for (int i = delayedTriggerEvents.Count - 1; i >= 0; i--)
            {
                delayedTriggerEvents[i].Delay -= deltaTime;
                if (delayedTriggerEvents[i].Delay <= 0.0f)
                {
                    TriggerEvent(delayedTriggerEvents[i].TriggerEvent);
                    delayedTriggerEvents.RemoveAt(i);
                }
            }
            UpdateMissionSpecific(deltaTime);
        }

        public virtual void CharacterAttacked(Character attackedCharacter, Character attacker, AttackResult attackResult) { }
        public virtual void StructureDamaged(Structure structure, float damageAmount, Character attacker) { }

        protected virtual void UpdateMissionSpecific(float deltaTime) { }

        /// <param name="progressExpiry">Should the step count towards the mission's expiry (see <see cref="WorldStepsUntilExpiry"/>)?
        /// Should be false for missions that are available in the players' location or being played.</param>
        public void ProgressWorld(bool progressExpiry = true)
        {
            WorldStepsSinceUnlocked++;

            if (progressExpiry && WorldStepsUntilExpiry > 0 && !completed)
            {
                WorldStepsUntilExpiry--;
                if (Expired)
                {
                    //no other effects, the mission is just removed from the available missions (see ShouldBeRemovedFromLocation)
                    DebugConsole.Log($"Mission \"{Name}\" ({Prefab.Identifier}) has expired.");
#if SERVER
                    (GameMain.GameSession?.Campaign as MultiPlayerCampaign)?.IncrementLastUpdateIdForFlag(MultiPlayerCampaign.NetFlags.MapAndMissions);
#endif
                }
            }

            //already completed, won't fail even if the time limit passes
            //(otherwise e.g. an already completed "stop an assault sub" mission would fail if the round takes too long)
            if (completed) { return; }

            if (TimeLimitReached)
            {
                if (Prefab.CompleteWhenTimeLimitReached)
                {
                    //already failed during a round (e.g. a transport sub that got destroyed), can't complete anymore
                    if (failed || completed) { return; }
                    DebugConsole.Log($"Mission \"{Name}\" ({Prefab.Identifier}) has been completed: time limit reached ({Prefab.TimeLimitWorldSteps} world steps).");
                    completed = true;
                    ApplyActionsOnComplete();
                }
                else
                {
                    DebugConsole.Log($"Mission \"{Name}\" ({Prefab.Identifier}) has failed: not completed in time (within {Prefab.TimeLimitWorldSteps} world steps).");
                    failed = true;
                    ApplyActionsOnFailure();
                }
#if SERVER
                (GameMain.GameSession?.Campaign as MultiPlayerCampaign)?.IncrementLastUpdateIdForFlag(MultiPlayerCampaign.NetFlags.MapAndMissions);
#endif
            }
        }

        public void CheckDestinationStillValid()
        {
            if (Prefab.RequiredDestinationFaction.IsEmpty) { return; }

            //already completed, won't fail if the destination becomes invalid
            if (completed) { return; }
            
            if ((Prefab.RequiredDestinationFaction == "None" && Destination.Faction != null) ||
                (Prefab.RequiredDestinationFaction != "None" && Destination.Faction?.Prefab.Identifier != Prefab.RequiredDestinationFaction))
            {
                //fail, but do not apply any actions on failure - we want the mission to silently disappear if the destination is no longer valid
                failed = true;
                ApplyActionsOnFailure();
#if SERVER
                (GameMain.GameSession?.Campaign as MultiPlayerCampaign)?.IncrementLastUpdateIdForFlag(MultiPlayerCampaign.NetFlags.MapAndMissions);
#endif
            }            
        }

        protected void ShowMessage(int missionState)
        {
            ShowMessageProjSpecific(missionState);
        }

        partial void ShowMessageProjSpecific(int missionState);

        protected virtual LocalizedString ModifyMessage(LocalizedString message, bool color = true)
        {
            return message;
        }

        private void TryTriggerEvents(int state)
        {
            foreach (var triggerEvent in Prefab.TriggerEvents)
            {
                if (triggerEvent.State == state)
                {
                    TryTriggerEvent(triggerEvent);
                }
            }
        }

        /// <summary>
        /// Triggers the event or adds it to the delayedTriggerEvents it if it has a delay
        /// </summary>
        private void TryTriggerEvent(MissionPrefab.TriggerEvent trigger)
        {
            if (trigger.CampaignOnly && GameMain.GameSession?.Campaign == null) { return; }
            if (trigger.Delay > 0 || trigger.State == 0)
            {
                if (!delayedTriggerEvents.Any(t => t.TriggerEvent == trigger))
                {
                    delayedTriggerEvents.Add(new DelayedTriggerEvent(trigger, trigger.Delay));
                }
            }
            else
            {
                TriggerEvent(trigger);
            }
        }

        /// <summary>
        /// Triggers the event immediately, ignoring any delays
        /// </summary>
        private void TriggerEvent(MissionPrefab.TriggerEvent trigger)
        {
            if (trigger.CampaignOnly && GameMain.GameSession?.Campaign == null) { return; }
            if (Rand.Range(0.0f, 1.0f) > trigger.Probability) { return; }
            //clients are not allowed to trigger events, they're handled by the server
            if (GameMain.NetworkMember is { IsClient: true }) { return; }
            EventPrefab eventPrefab = EventPrefab.FindEventPrefab(trigger.EventIdentifier, trigger.EventTag, Prefab.ContentPackage);
            if (eventPrefab == null)
            {
                DebugConsole.ThrowError($"Mission {Prefab.Identifier} failed to trigger an event (identifier: {trigger.EventIdentifier}, tag: {trigger.EventTag}).", contentPackage: Prefab.ContentPackage);
                return;
            }
            if (GameMain.GameSession?.EventManager != null)
            {
                var newEvent = eventPrefab.CreateInstance(GameMain.GameSession.EventManager.RandomSeed);
                newEvent.TriggeringMission = this;
                GameMain.GameSession.EventManager.ActivateEvent(newEvent);
            }
        }

        /// <summary>
        /// End the mission and give a reward if it was completed successfully
        /// </summary>
        public void End(CampaignMode.TransitionType transitionType)
        {
            //clients aren't allowed to make the mission fail by themselves, it's received from the server at the end of the round
            //if the round ended without it (e.g. the client returned to the lobby mid-round or got disconnected),
            //don't apply anything that'd modify the state of the campaign (that'd make the client's campaign state diverge from the server's)
            bool outcomeKnown = true;
#if CLIENT
            outcomeKnown = GameMain.NetworkMember is not { IsClient: true } || failedStateFromServer.HasValue;
#endif
            if (GameMain.NetworkMember is not { IsClient: true })
            {
                completed =
                    !ForceFailure &&
                    DetermineCompleted(transitionType) &&
                    (completeCheckDataAction == null || completeCheckDataAction.GetSuccess());
            }
            if (completed && outcomeKnown)
            {
                if (Prefab.LocationTypeChangeOnCompleted != null)
                {
                    ChangeLocationType(Prefab.LocationTypeChangeOnCompleted);
                }
                try
                {
                    ApplyActionsOnComplete();
                }
                catch (Exception e)
                {
                    string errorMsg = "Unknown error while giving mission rewards.";
                    DebugConsole.ThrowError(errorMsg, e, contentPackage: Prefab.ContentPackage);
                    GameAnalyticsManager.AddErrorEventOnce("Mission.End:GiveReward", GameAnalyticsManager.ErrorSeverity.Error, errorMsg + "\n" + e.StackTrace);
#if SERVER
                    GameMain.Server?.SendChatMessage(errorMsg + "\n" + e.StackTrace, Networking.ChatMessageType.Error);
#endif
                }
            }

            TimesAttempted++;

            bool wasPreviouslyFailed = failed;
            EndMissionSpecific(completed);
            if (ForceFailure)
            {
                failed = true;
            }
#if CLIENT
            if (failedStateFromServer.HasValue)
            {
                //the server decides whether the mission failed
                failed = failedStateFromServer.Value;
                failedStateFromServer = null;
            }
            else if (!outcomeKnown)
            {
                //don't let the client-side logic in EndMissionSpecific mark the mission as failed (which would e.g. remove it from the location)
                failed = wasPreviouslyFailed;
            }
#endif
            if (failed && outcomeKnown)
            {
                ApplyActionsOnFailure();
            }

            // Clear any unique name colors set by this mission
            foreach (var character in characters)
            {
                if (character != null && !character.Removed)
                {
                    character.UniqueNameColor = null;
                }
            }
        }

        protected abstract bool DetermineCompleted(CampaignMode.TransitionType transitionType);

        protected virtual void EndMissionSpecific(bool completed) { }

        /// <summary>
        /// Get the final reward, taking talent bonuses into account if the mission has concluded and the talents modified the reward accordingly.
        /// </summary>
        public int GetFinalReward(Submarine sub)
        {
            return finalReward ?? GetReward(sub);
        }

        /// <summary>
        /// Calculates the final reward after talent bonuses have been applied. Note that this triggers talent effects of the type OnGainMissionMoney, 
        /// and should only be called once when the mission is completed!
        /// </summary>
        private void CalculateFinalReward(Submarine sub)
        {
            int reward = GetReward(sub);
            IEnumerable<Character> crewCharacters = GameSession.GetSessionCrewCharacters(CharacterType.Both);
            var missionMoneyGainMultiplier = new AbilityMissionMoneyGainMultiplier(this, 1f);
            CharacterTalent.CheckTalentsForCrew(crewCharacters, AbilityEffectType.OnGainMissionMoney, missionMoneyGainMultiplier);
            crewCharacters.ForEach(c => missionMoneyGainMultiplier.Value += c.GetStatValue(StatTypes.MissionMoneyGainMultiplier));
            finalReward = (int)(reward * missionMoneyGainMultiplier.Value);
        }

        /// <summary>
        /// Calculates the amount of reputation given by the reward after talent bonuses have been applied.
        /// </summary>
        /// <param name="preview">Should be true if the amount is only used for display purposes.
        /// If false, this triggers talent effects of the type OnCrewGainMissionReputation, and should only be called when the reward is actually given!</param>
        private float GetReputationRewardAmount(MissionPrefab.ReputationReward reputationReward, bool preview)
        {
            //talents only boost reputation gains, they shouldn't make a negative reward take away even more reputation
            if (reputationReward.Amount <= 0.0f) { return reputationReward.Amount; }

            var reputationGainMultiplier = new AbilityMissionReputationGainMultiplier(this, 1f, character: null);
            foreach (var c in GameSession.GetSessionCrewCharacters(CharacterType.Both))
            {
                if (preview)
                {
                    c.PreviewTalentEffects(AbilityEffectType.OnCrewGainMissionReputation, reputationGainMultiplier);
                }
                else
                {
                    c.CheckTalents(AbilityEffectType.OnCrewGainMissionReputation, reputationGainMultiplier);
                }
            }
            return reputationReward.Amount * reputationGainMultiplier.Value;
        }

        private float CalculateDifficultyXPMultiplier()
        {
            const float minMissionDifficulty = 1;
            const float maxMissionDifficulty = 4;
            const float maxXpBonus = 1.3f;
            float selectedMissionDifficulty = MathUtils.InverseLerp(minMissionDifficulty, maxMissionDifficulty, Prefab.Difficulty.GetValueOrDefault());
            float xpBonusMultiplier = MathHelper.Lerp(1.0f, maxXpBonus, selectedMissionDifficulty);

            return xpBonusMultiplier;
        }

        public void ApplyActionsOnComplete()
        {
            if (Prefab.ContinuePathCondition is MissionPrefab.CompletionCondition.Always or MissionPrefab.CompletionCondition.OnlyWhenCompleted &&
                MorePathToDestinationRemaining)
            {
                ContinuePathTowardsDestination();
                return;
            }

            if (GameMain.GameSession.GameMode is not CampaignMode campaign) { return; }

            float xpReward = GetBaseReward(Submarine.MainSub) * Prefab.ExperienceMultiplier * campaign.Settings.ExperienceRewardMultiplier;
            float xpGain = 
                xpReward * (level?.LevelData.Biome.ExperienceFromMissionRewards ?? 1.0f) * CalculateDifficultyXPMultiplier()
                + Prefab.AdditionalExperienceGain;

            IEnumerable<Character> crewCharacters = GameSession.GetSessionCrewCharacters(CharacterType.Both);

            // use multipliers here so that we can easily add them together without introducing multiplicative XP stacking
            var experienceGainMultiplier = new AbilityMissionExperienceGainMultiplier(this, 1f, character: null);
            crewCharacters.ForEach(c => experienceGainMultiplier.Value += c.GetStatValue(StatTypes.MissionExperienceGainMultiplier));

            DistributeExperienceToCrew(crewCharacters, (int)(xpGain * experienceGainMultiplier.Value));

            CalculateFinalReward(Submarine.MainSub);
#if SERVER
            finalReward = DistributeRewardsToCrew(GameSession.GetSessionCrewCharacters(CharacterType.Player), finalReward.Value);
#endif
            bool isSingleplayerOrServer = GameMain.IsSingleplayer || GameMain.NetworkMember is { IsServer: true };
            if (isSingleplayerOrServer)
            {
                if (finalReward > 0)
                {
                    campaign.Bank.Give(finalReward.Value);
                }

                foreach (Character character in crewCharacters)
                {
                    character.Info.MissionsCompletedSinceDeath++;
                }

                foreach (var reputationReward in ReputationRewards)
                {
                    float amount = GetReputationRewardAmount(reputationReward, preview: false);

                    if (reputationReward.FactionIdentifier == "location")
                    {
                        OriginLocation.Reputation?.AddReputation(amount);
                        TryGiveReputationForOpposingFaction(OriginLocation.Faction, reputationReward.AmountForOpposingFaction);
                    }
                    else
                    {
                        Faction faction = campaign.GetFaction(reputationReward.FactionIdentifier);
                        if (faction != null)
                        {
                            faction.Reputation.AddReputation(amount);
                            TryGiveReputationForOpposingFaction(faction, reputationReward.AmountForOpposingFaction);
                        }
                    }
                }

                void TryGiveReputationForOpposingFaction(Faction thisFaction, float amount)
                {
                    if (MathUtils.NearlyEqual(amount, 0.0f)) { return; }
                    if (thisFaction?.Prefab is { OpposingFaction.IsEmpty: false })
                    {
                        Faction faction = campaign.GetFaction(thisFaction.Prefab.OpposingFaction);
                        faction?.Reputation.AddReputation(amount);
                    }
                }
            }

            if (!Prefab.ActionsOnCompleteCampaignOnly || GameMain.GameSession.Campaign != null)
            {
                ApplyActions(Prefab.ActionsOnComplete);                
            }

            if (Prefab.DataRewards != null)
            {
                foreach (var (identifier, value, operation) in Prefab.DataRewards)
                {
                    SetDataAction.PerformOperation(campaign.CampaignMetadata, identifier, value, operation);
                }
            }
        }

        public void ApplyActionsOnFailure()
        {
            if (Prefab.ContinuePathCondition is MissionPrefab.CompletionCondition.Always or MissionPrefab.CompletionCondition.OnlyWhenFailed &&
                MorePathToDestinationRemaining)
            {
                ContinuePathTowardsDestination();
                return;
            }

            if (!Prefab.ActionsOnFailureCampaignOnly || GameMain.GameSession.Campaign != null)
            {
                ApplyActions(Prefab.ActionsOnFailure);
            }
        }

        private void ApplyActions(IEnumerable<EventAction> actions)
        {
            string throwaway = null;
            int remainingTries = 100;
            foreach (var action in actions)
            {
                action.Reset();
                action.ParentMission = this;
                //run the actions until they're finished
                //(e.g. BinaryOptionActions may check the conditional on the first iteration and continue into subactions on further iterations)
                while (remainingTries > 0 && !action.IsFinished(ref throwaway))
                {
                    action.Update(1.0f);
                    remainingTries--;
                }
            }
        }

        #region Win and failure conditions

        protected bool CheckMissionResultCondition(MissionResultCondition conditionToMeet, Submarine referenceSub, IEnumerable<Character> characters)
        {
            return CheckMissionResultCondition(conditionToMeet, referenceSub, characters, out _);
        }

        protected bool CheckMissionResultCondition(MissionResultCondition conditionToMeet, Submarine referenceSub, IEnumerable<Character> characters, out OutpostDestroyedState outpostDestroyedReason)
        {
            outpostDestroyedReason = OutpostDestroyedState.None;
            return conditionToMeet switch
            {
                MissionResultCondition.CrewEliminated => characters.All(DeadOrCaptured),
                MissionResultCondition.AnyoneFromCrewSurvived => characters.Any(c => !DeadOrCaptured(c)),
                //note that the Always condition is handled at the end of the round in DetermineCompleted
                MissionResultCondition.SubmarineIncapacitated => IsSubmarineIncapacitated(referenceSub),
                MissionResultCondition.SubmarineNotIncapacitated => !IsSubmarineIncapacitated(referenceSub),
                MissionResultCondition.OutpostDestroyed => IsOutpostDestroyed(out outpostDestroyedReason),
                _ => false,
            };
        }

        private IEnumerable<Character> FindOutpostDwellers()
        {
            if (level.StartOutpost is not { } outpost)
            {
                DebugConsole.ThrowError($"Error in mission {nameof(SubmarineEncounterMission)}. No outpost found.");
                return [];
            }
            return Character.CharacterList.Where(c => c.TeamID == CharacterTeamType.FriendlyNPC && c.IsHuman && !c.IsDead);
        }

        protected static bool DeadOrCaptured(Character character)
        {
            return character == null || character.Removed || character.Submarine == null || (character.LockHands && character.Submarine == Submarine.MainSub) || character.IsIncapacitated;
        }

        private bool IsSubmarineIncapacitated(Submarine sub)
        {
            if (sub == null) { return true; }

            if (level?.GenerationParams != null)
            {
                if (sub.AtDamageDepth) { return true; }
                float midwayToSeaFloor = level.GenerationParams.SeaFloorDepth / 2;
                if (sub.WorldPosition.Y < midwayToSeaFloor) { return true; }

                //1000 m below the main submarine or the bottom of the level
                //(or in other words, sinking way below the bottom of the level counts as incapacitated,
                //except if the player sub is somewhere down there too, potentially still engaged in combat?)
                if (sub.WorldPosition.Y < Math.Min(Submarine.MainSub.WorldPosition.Y, 0.0f) - ConvertUnits.ToDisplayUnits(1000.0f))
                {
                    return true;
                }
            }

            var aliveCrew = characters.Where(c => !DeadOrCaptured(c));
            if (aliveCrew.None()) { return true; }

            //some of the crew are currently alive in some enemy sub = attacking the outpost or player sub?
            //Don't end the mission until they're dead or captured
            if (aliveCrew.Any(c => c.Submarine != null && !c.IsInSameTeamSub)) { return false; }

            float waterVolume = 0.0f, totalHullVolume = 0.0f;
            foreach (Hull hull in sub.GetHulls(alsoFromConnectedSubs: true))
            {
                if (hull.IsWetRoom) { continue; }
                waterVolume += hull.WaterVolume;
                totalHullVolume += hull.Volume;
            }

            float crewRatio = aliveCrew.Count() / (float)Math.Max(characters.Count, 1);
            //half of the sub flooded, not much crew left
            return waterVolume > totalHullVolume * submarineIncapacitatedWhenSubFloodedThreshold && crewRatio < submarineIncapacitatedWhenCrewBelowThreshold;
        }

        private bool IsOutpostDestroyed(out OutpostDestroyedState reason)
        {
            reason = OutpostDestroyedState.None;
            if (level.StartOutpost is not { } outpost)
            {
                DebugConsole.ThrowErrorOnce(identifier: "IsOutpostDestroyed", $"Error in mission {nameof(SubmarineEncounterMission)}. No outpost found.");
                return false;
            }
            if (outpostReactor != null && outpostReactor.Condition <= 0.0f)
            {
                reason = OutpostDestroyedState.ReactorDestroyed;
                return true;
            }
            if (initialOutpostDwellerCount.HasValue &&
                initialOutpostDwellerCount > 0 &&
                FindOutpostDwellers().Count() / (float)initialOutpostDwellerCount < outpostDestroyedWhenNPCsBelowThreshold)
            {
                reason = OutpostDestroyedState.TooManyNPCsKilled;
                return true;
            }
            float waterVolume = 0.0f, totalHullVolume = 0.0f;
            foreach (Hull hull in outpost.GetHulls(alsoFromConnectedSubs: true))
            {
                if (hull.IsWetRoom) { continue; }
                waterVolume += hull.WaterVolume;
                totalHullVolume += hull.Volume;
            }
            if (waterVolume > totalHullVolume * outpostDestroyedWhenFloodedThreshold)
            {
                reason = OutpostDestroyedState.TooFlooded;
                return true;
            }
            return false;
        }

        #endregion



        /// <summary>
        /// Makes the mission progress towards the next location in the path, if a path to destination has been set.
        /// (In practice unlocks an identical mission heading towards the destination)
        /// </summary>
        private void ContinuePathTowardsDestination()
        {
            if (!MorePathToDestinationRemaining) { return; }
            
            var nextStartLocation = PathToDestination[1];
            var nextDestinationLocation = PathToDestination[2];
            var newMission = nextStartLocation.UnlockMission(Prefab, nextStartLocation.GetConnection(nextDestinationLocation));
            if (newMission != null)
            {
                if (OriginLocation != null)
                {
                    newMission.OriginLocation = OriginLocation;
                }
                newMission.PathToDestination = PathToDestination.Skip(1).ToImmutableArray();
                if (Prefab.DiscoverPathAsProgressing && GameMain.GameSession?.Map is { } currentMap)
                {
                    //discover the next leg of the path, so the progress of the mission can be followed on the map
                    currentMap.Discover(nextStartLocation, checkTalents: false);
                    currentMap.Discover(nextDestinationLocation, checkTalents: false);
                }
            }
#if CLIENT
            //remove mission icon immediately (so the icon doesn't gradually disappear, making both this and the new mission appear on the map at the same time)
            if (GameMain.GameSession?.Map is { } map)
            {
                map.RemoveMissionIcon(this);
            }
#endif            
        }

        partial void DistributeExperienceToCrew(IEnumerable<Character> crew, int experienceGain);

        public static int GetRewardDistibutionSum(IEnumerable<Character> crew, int rewardDistribution = 0) => crew.Sum(c => c.Wallet.RewardDistribution) + rewardDistribution;

        public static (int Amount, int Percentage, float Sum) GetRewardShare(int rewardDistribution, IEnumerable<Character> crew, Option<int> reward)
        {
            float sum = GetRewardDistibutionSum(crew, rewardDistribution);
            if (MathUtils.NearlyEqual(sum, 0)) { return (0, 0, sum); }

            float rewardWeight = sum > 100 ? rewardDistribution / sum : rewardDistribution / 100f;
            int rewardPercentage = (int)(rewardWeight * 100);

            int amount = reward.TryUnwrap(out var a) ? a : 0;

            return ((int)(amount * rewardWeight), rewardPercentage, sum);
        }

        public void SetPathToDestination(IEnumerable<Location> path)
        {
            if (!path.Contains(Locations[0]) || !path.Contains(Locations[1]))
            {
                DebugConsole.ThrowError(
                    $"Tried to set an invalid path to destination on the mission {Prefab.Identifier}."+
                    $" Locations the mission takes place between ({Locations[0].DisplayName}, {Locations[1].DisplayName}) not present in the path ({string.Join(" -> ", path.Select(l => l.DisplayName))}).");
                return;
            }
            PathToDestination = path.ToImmutableArray();
            RefreshDescriptionAndMessages(Submarine.MainSub);
        }

        protected void ChangeLocationType(LocationTypeChange change)
        {
            if (change == null) { throw new ArgumentException(); }
            if (GameMain.GameSession.GameMode is CampaignMode campaign && !IsClient)
            {
                int srcIndex = -1;
                for (int i = 0; i < Locations.Length; i++)
                {
                    if (Locations[i].Type.Identifier == change.CurrentType)
                    {
                        srcIndex = i;
                        break;
                    }
                }
                if (srcIndex == -1) { return; }
                var location = Locations[srcIndex];

                if (location.LocationTypeChangesBlocked) { return; }

                if (change.RequiredDurationRange.X > 0)
                {
                    location.PendingLocationTypeChange = (change, Rand.Range(change.RequiredDurationRange.X, change.RequiredDurationRange.Y), Prefab);
                }
                else
                {
                    location.ChangeType(campaign, LocationType.Prefabs[change.ChangeToType]);
                    location.LocationTypeChangeCooldown = change.CooldownAfterChange;
                }
            }
        }

        public virtual void AdjustLevelData(LevelData levelData) { }

        public bool ShouldBeRemovedFromLocation()
        {
            if (ForceRetry) 
            { 
                // Prevent the mission from being removed even if the mission was completed/failed
                return false; 
            }

            if (ForceFailure) { return true; }
            if (Completed) { return true; }
            if (Failed && !Prefab.AllowRetry) { return true; }
            if (Expired) { return true; }

            return false;
        }

        // putting these here since both escort and pirate missions need them. could be tucked away into another class that they can inherit from (or use composition)
        protected HumanPrefab GetHumanPrefabFromElement(XElement element)
        {
            if (element.Attribute("name") != null)
            {
                DebugConsole.ThrowError($"Error in mission \"{Name}\" - use character identifiers instead of names to configure the characters.",
                    contentPackage: Prefab.ContentPackage);
                return null;
            }

            Identifier characterIdentifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);
            Identifier characterFrom = element.GetAttributeIdentifier("from", Identifier.Empty);
            HumanPrefab humanPrefab = NPCSet.Get(characterFrom, characterIdentifier, contentPackageToLogInError: Prefab.ContentPackage);
            if (humanPrefab == null)
            {
                DebugConsole.ThrowError($"Couldn't spawn character for mission: character prefab \"{characterIdentifier}\" not found in the NPC set \"{characterFrom}\".",
                    contentPackage: Prefab.ContentPackage);
                return null;
            }

            return humanPrefab;
        }

        protected static Character CreateHuman(HumanPrefab humanPrefab, List<Character> characters, Dictionary<Character, List<Item>> characterItems, Submarine submarine, CharacterTeamType teamType, ISpatialEntity positionToStayIn = null, Rand.RandSync humanPrefabRandSync = Rand.RandSync.ServerAndClient)
        {
            var characterInfo = humanPrefab.CreateCharacterInfo(Rand.RandSync.ServerAndClient);
            characterInfo.TeamID = teamType;

            positionToStayIn ??= 
                    WayPoint.GetRandom(SpawnType.Human, characterInfo.Job?.Prefab, submarine) ??
                    WayPoint.GetRandom(SpawnType.Human, null, submarine);

            Character spawnedCharacter = Character.Create(characterInfo.SpeciesName, positionToStayIn.WorldPosition, ToolBox.RandomSeed(8), characterInfo, createNetworkEvent: false);
            spawnedCharacter.HumanPrefab = humanPrefab;
            humanPrefab.InitializeCharacter(spawnedCharacter, positionToStayIn);
            humanPrefab.GiveItems(spawnedCharacter, submarine, positionToStayIn as WayPoint, Rand.RandSync.ServerAndClient, createNetworkEvents: false);
            characters.Add(spawnedCharacter);
            characterItems.Add(spawnedCharacter, spawnedCharacter.Inventory.FindAllItems(recursive: true));

            return spawnedCharacter;
        }

        protected ItemPrefab FindItemPrefab(XElement element)
        {
            ItemPrefab itemPrefab = null;
            if (element.Attribute("name") != null)
            {
                DebugConsole.ThrowError($"Error in mission \"{Name}\" - use item identifiers instead of names to configure the items", 
                    contentPackage: Prefab.ContentPackage);
                string itemName = element.GetAttributeString("name", "");
                itemPrefab = MapEntityPrefab.FindByName(itemName) as ItemPrefab;
                if (itemPrefab == null)
                {
                    DebugConsole.ThrowError($"Couldn't spawn item for mission \"{Name}\": item prefab \"{itemName}\" not found.", 
                        contentPackage: Prefab.ContentPackage);
                }
            }
            else if (element.Attribute("identifier") != null)
            {
                Identifier itemIdentifier = element.GetAttributeIdentifier("identifier", "");
                itemPrefab = MapEntityPrefab.FindByIdentifier(itemIdentifier) as ItemPrefab;
                if (itemPrefab == null)
                {
                    DebugConsole.ThrowError($"Couldn't spawn item for mission \"{Name}\": item prefab \"{itemIdentifier}\" not found.",
                        contentPackage: Prefab.ContentPackage);
                }
            }
            else if (element.Attribute("tag") != null)
            {
                Identifier itemTag = element.GetAttributeIdentifier("tag", "");
                var matchingItemPrefabs = ItemPrefab.Prefabs.Where(p => p.Tags.Contains(itemTag));
                if (matchingItemPrefabs.None())
                {
                    DebugConsole.ThrowError($"Couldn't spawn item for mission \"{Name}\": could not find item prefabs with the tag \"{itemTag}\".",
                        contentPackage: Prefab.ContentPackage);
                }
                itemPrefab = matchingItemPrefabs.GetRandomUnsynced();
            }
            return itemPrefab;
        }

        protected Vector2? GetCargoSpawnPosition(ItemPrefab itemPrefab, out Submarine cargoRoomSub, Submarine targetSub = null)
        {
            targetSub ??= Submarine.MainSub;

            cargoRoomSub = null;

            WayPoint cargoSpawnPos = WayPoint.GetRandom(SpawnType.Cargo, null, targetSub, useSyncedRand: true);
            if (cargoSpawnPos == null)
            {
                DebugConsole.ThrowError($"Couldn't spawn items for mission \"{Name}\": no waypoints marked as Cargo were found",
                    contentPackage: Prefab.ContentPackage);
                return null;
            }

            var cargoRoom = cargoSpawnPos.CurrentHull;
            if (cargoRoom == null)
            {
                DebugConsole.ThrowError($"Couldn't spawn items for mission \"{Name}\": waypoints marked as Cargo must be placed inside a room",
                    contentPackage: Prefab.ContentPackage);
                return null;
            }

            cargoRoomSub = cargoRoom.Submarine;

            return new Vector2(
                cargoSpawnPos.Position.X + Rand.Range(-20.0f, 20.0f, Rand.RandSync.ServerAndClient),
                cargoRoom.Rect.Y - cargoRoom.Rect.Height + itemPrefab.Size.Y / 2);
        }

        /// <summary>
        /// Gets a random submarine by tags, filtered by difficulty. Used by missions that force specific submarines (wrecks, beacons, etc.)
        /// </summary>
        /// <param name="tags">Mission tags to match against</param>
        /// <param name="submarineSelector">Function to filter submarines by type (e.g., s => s.IsWreck)</param>
        /// <param name="submarineTypeName">Name of submarine type for error messages (e.g., "wreck", "beacon station")</param>
        /// <returns>Selected submarine, or null if none found</returns>
        protected static SubmarineInfo GetRandomSubmarineByTagsAndDifficulty(
            IEnumerable<Identifier> tags,
            LevelData levelData,
            Func<SubmarineInfo, bool> submarineSelector,
            string submarineTypeName)
        {
            var rand = new MTRandom(ToolBox.StringToInt(levelData.Seed));
            float levelDifficulty = levelData.Difficulty;

            var submarinesWithTags = SubmarineInfo.SavedSubmarines
                .Where(submarineSelector)
                .Where(s =>
                {
                    return s.GetExtraSubmarineInfo is { } extraInfo && (tags.None() || tags.Any(t => extraInfo.MissionTags.Contains(t)));
                })
                .ToList();

            var matchingSubmarines = submarinesWithTags
                .Where(s =>
                {
                    return s.GetExtraSubmarineInfo is { } extraInfo &&
                           levelDifficulty >= extraInfo.MinLevelDifficulty &&
                           levelDifficulty <= extraInfo.MaxLevelDifficulty;
                })
                .ToList();

            if (matchingSubmarines.Count == 0)
            {
                if (submarinesWithTags.Count > 0)
                {
                    DebugConsole.ThrowError($"Found {submarinesWithTags.Count} {submarineTypeName}(s) with matching tags \"{string.Join(", ", tags)}\", but none are suitable for level difficulty {levelDifficulty:F1}.");
                }
                return null;
            }
            return matchingSubmarines[rand.Next(matchingSubmarines.Count)];
        }
    }

    class AbilityMissionMoneyGainMultiplier : AbilityObject, IAbilityValue, IAbilityMission
    {
        public AbilityMissionMoneyGainMultiplier(Mission mission, float moneyGainMultiplier)
        {
            Value = moneyGainMultiplier;
            Mission = mission;
        }
        public float Value { get; set; }
        public Mission Mission { get; set; }
    }

    class AbilityMissionExperienceGainMultiplier : AbilityObject, IAbilityValue, IAbilityMission, IAbilityCharacter
    {
        public AbilityMissionExperienceGainMultiplier(Mission mission, float missionExperienceGainMultiplier, Character character)
        {
            Value = missionExperienceGainMultiplier;
            Mission = mission;
            Character = character;
        }

        public float Value { get; set; }
        public Mission Mission { get; set; }
        public Character Character { get; set; }
    }
    
    class AbilityMissionReputationGainMultiplier : AbilityObject, IAbilityValue, IAbilityMission, IAbilityCharacter
    {
        public AbilityMissionReputationGainMultiplier(Mission mission, float reputationMultiplier, Character character)
        {
            Value = reputationMultiplier;
            Mission = mission;
            Character = character;
        }

        public float Value { get; set; }
        public Mission Mission { get; set; }
        public Character Character { get; set; }
    }

}
