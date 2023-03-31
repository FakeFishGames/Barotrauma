using Barotrauma.Abilities;
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
    abstract partial class Mission
    {
        public readonly MissionPrefab Prefab;
        private bool completed;
        protected bool failed;

        protected Level level;

        protected int state;
        public virtual int State
        {
            get { return state; }
            set
            {
                if (state != value)
                {
                    state = value;
                    TryTriggerEvents(state);
#if SERVER
                    GameMain.Server?.UpdateMissionState(this);
#elif CLIENT
                    if (Prefab.ShowProgressBar)
                    {
                        CharacterHUD.ShowMissionProgressBar(this);
                    }
#endif
                    ShowMessage(State);
                    OnMissionStateChanged?.Invoke(this);
                }
            }
        }

        public int TimesAttempted { get; set; }

        protected static bool IsClient => GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient;

        private readonly CheckDataAction completeCheckDataAction;

        public readonly ImmutableArray<LocalizedString> Headers;
        public readonly ImmutableArray<LocalizedString> Messages;

        /// <summary>
        /// The reward that was actually given from completing the mission, taking any talent bonuses into account 
        /// (some of which may not be possible to determine in advance)
        /// </summary>
        private int? finalReward;

        public virtual LocalizedString Name => Prefab.Name;

        private readonly LocalizedString successMessage;
        public virtual LocalizedString SuccessMessage
        {
            get { return successMessage; }
            //private set { successMessage = value; }
        }

        private readonly LocalizedString failureMessage;
        public virtual LocalizedString FailureMessage
        {
            get { return failureMessage; }
            //private set { failureMessage = value; }
        }

        protected LocalizedString description;
        public virtual LocalizedString Description
        {
            get { return description; }
            //private set { description = value; }
        }

        protected LocalizedString descriptionWithoutReward;

        public virtual bool AllowUndocking
        {
            get { return true; }
        }

        public virtual int Reward
        {
            get 
            {
                return Prefab.Reward;
            }
        }

        public Dictionary<Identifier, float> ReputationRewards
        {
            get { return Prefab.ReputationRewards; }
        }

        public bool Completed
        {
            get { return completed; }
            set { completed = value; }
        }

        public bool Failed
        {
            get { return failed; }
        }

        public virtual bool AllowRespawn
        {
            get { return true; }
        }

        public virtual int TeamCount
        {
            get { return 1; }
        }

        public virtual SubmarineInfo EnemySubmarineInfo
        {
            get { return null; }
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

        public int? Difficulty
        {
            get { return Prefab.Difficulty; }
        }

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

        public Mission(MissionPrefab prefab, Location[] locations, Submarine sub)
        {
            System.Diagnostics.Debug.Assert(locations.Length == 2);

            Prefab = prefab;

            description = prefab.Description.Value;
            successMessage = prefab.SuccessMessage.Value;
            failureMessage = prefab.FailureMessage.Value;
            Headers = prefab.Headers;
            var messages = prefab.Messages.ToArray();

            OriginLocation = locations[0];
            Locations = locations;

            var endConditionElement = prefab.ConfigElement.GetChildElement(nameof(completeCheckDataAction));
            if (endConditionElement != null)
            {
                completeCheckDataAction = new CheckDataAction(endConditionElement, $"Mission ({prefab.Identifier})");
            }

            for (int n = 0; n < 2; n++)
            {
                string locationName = $"‖color:gui.orange‖{locations[n].Name}‖end‖";
                if (description != null) { description = description.Replace("[location" + (n + 1) + "]", locationName); }
                if (successMessage != null) { successMessage = successMessage.Replace("[location" + (n + 1) + "]", locationName); }
                if (failureMessage != null) { failureMessage = failureMessage.Replace("[location" + (n + 1) + "]", locationName); }
                for (int m = 0; m < messages.Length; m++)
                {
                    messages[m] = messages[m].Replace("[location" + (n + 1) + "]", locationName);
                }
            }
            string rewardText = $"‖color:gui.orange‖{string.Format(CultureInfo.InvariantCulture, "{0:N0}", GetReward(sub))}‖end‖";
            if (description != null) 
            {
                descriptionWithoutReward = description;
                description = description.Replace("[reward]", rewardText); 
            }
            if (successMessage != null) { successMessage = successMessage.Replace("[reward]", rewardText); }
            if (failureMessage != null) { failureMessage = failureMessage.Replace("[reward]", rewardText); }
            for (int m = 0; m < messages.Length; m++)
            {
                messages[m] = messages[m].Replace("[reward]", rewardText);
            }

            Messages = messages.ToImmutableArray();
        }

        public virtual void SetLevel(LevelData level) { }

        public static Mission LoadRandom(Location[] locations, string seed, bool requireCorrectLocationType, MissionType missionType, bool isSinglePlayer = false)
        {
            return LoadRandom(locations, new MTRandom(ToolBox.StringToInt(seed)), requireCorrectLocationType, missionType, isSinglePlayer);
        }

        public static Mission LoadRandom(Location[] locations, MTRandom rand, bool requireCorrectLocationType, MissionType missionType, bool isSinglePlayer = false)
        {
            List<MissionPrefab> allowedMissions = new List<MissionPrefab>();
            if (missionType == MissionType.None)
            {
                return null;
            }
            else
            {
                allowedMissions.AddRange(MissionPrefab.Prefabs.Where(m => ((int)(missionType & m.Type)) != 0));
            }

            allowedMissions.RemoveAll(m => isSinglePlayer ? m.MultiplayerOnly : m.SingleplayerOnly);            
            if (requireCorrectLocationType)
            {
                allowedMissions.RemoveAll(m => !m.IsAllowed(locations[0], locations[1]));
            }

            if (allowedMissions.Count == 0)
            {
                return null;
            }

            int probabilitySum = allowedMissions.Sum(m => m.Commonness);
            int randomNumber = rand.NextInt32() % probabilitySum;
            foreach (MissionPrefab missionPrefab in allowedMissions)
            {
                if (randomNumber <= missionPrefab.Commonness)
                {
                    return missionPrefab.Instantiate(locations, Submarine.MainSub);
                }
                randomNumber -= missionPrefab.Commonness;
            }

            return null;
        }

        public virtual int GetReward(Submarine sub)
        {
            return Prefab.Reward;
        }

        public void Start(Level level)
        {
            state = 0;
#if CLIENT
            shownMessages.Clear();
#endif
            delayedTriggerEvents.Clear();
            foreach (string categoryToShow in Prefab.UnhideEntitySubCategories)
            {
                foreach (MapEntity entityToShow in MapEntity.mapEntityList.Where(me => me.Prefab?.HasSubCategory(categoryToShow) ?? false))
                {
                    entityToShow.HiddenInGame = false;
                }
            }
            this.level = level;
            TryTriggerEvents(0);
            StartMissionSpecific(level);
        }

        protected virtual void StartMissionSpecific(Level level) { }

        public void Update(float deltaTime)
        {
            for (int i = delayedTriggerEvents.Count - 1; i>=0;i--)
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

        protected virtual void UpdateMissionSpecific(float deltaTime) { }

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
            var eventPrefab = EventSet.GetAllEventPrefabs().Find(p => p.Identifier == trigger.EventIdentifier);
            if (eventPrefab == null)
            {
                DebugConsole.ThrowError($"Mission \"{Name}\" failed to trigger an event (couldn't find an event with the identifier \"{trigger.EventIdentifier}\").");
                return;
            }
            if (GameMain.GameSession?.EventManager != null)
            {
                var newEvent = eventPrefab.CreateInstance();
                GameMain.GameSession.EventManager.ActiveEvents.Add(newEvent);
                newEvent.Init();
            }
        }

        /// <summary>
        /// End the mission and give a reward if it was completed successfully
        /// </summary>
        public void End()
        {
            completed = 
                DetermineCompleted() && 
                (completeCheckDataAction == null ||completeCheckDataAction.GetSuccess());
            if (completed)
            {
                if (Prefab.LocationTypeChangeOnCompleted != null)
                {
                    ChangeLocationType(Prefab.LocationTypeChangeOnCompleted);
                }
                GiveReward();
            }

            TimesAttempted++;

            EndMissionSpecific(completed);
        }

        protected abstract bool DetermineCompleted();

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
            crewCharacters.ForEach(c => c.CheckTalents(AbilityEffectType.OnGainMissionMoney, missionMoneyGainMultiplier));
            crewCharacters.ForEach(c => missionMoneyGainMultiplier.Value += c.GetStatValue(StatTypes.MissionMoneyGainMultiplier));
            finalReward = (int)(reward * missionMoneyGainMultiplier.Value);
        }

        private void GiveReward()
        {
            if (GameMain.GameSession.GameMode is not CampaignMode campaign) { return; }
            int reward = GetReward(Submarine.MainSub);

            float baseExperienceGain = reward * 0.09f;

            float difficultyMultiplier = 1 + level.Difficulty / 100f;
            baseExperienceGain *= difficultyMultiplier;

            IEnumerable<Character> crewCharacters = GameSession.GetSessionCrewCharacters(CharacterType.Both);

            // use multipliers here so that we can easily add them together without introducing multiplicative XP stacking
            var experienceGainMultiplier = new AbilityMissionExperienceGainMultiplier(this, 1f);
            crewCharacters.ForEach(c => c.CheckTalents(AbilityEffectType.OnAllyGainMissionExperience, experienceGainMultiplier));
            crewCharacters.ForEach(c => experienceGainMultiplier.Value += c.GetStatValue(StatTypes.MissionExperienceGainMultiplier));

            int experienceGain = (int)(baseExperienceGain * experienceGainMultiplier.Value);
#if CLIENT
            foreach (Character character in crewCharacters)
            {
                GiveMissionExperience(character.Info);
            }
#else
            foreach (Barotrauma.Networking.Client c in GameMain.Server.ConnectedClients)
            {
                //give the experience to the stored characterinfo if the client isn't currently controlling a character
                GiveMissionExperience(c.Character?.Info ?? c.CharacterInfo);
            }
            foreach (Character bot in GameSession.GetSessionCrewCharacters(CharacterType.Bot))
            {
                GiveMissionExperience(bot.Info);
            }
#endif

            void GiveMissionExperience(CharacterInfo info)
            {
                var experienceGainMultiplierIndividual = new AbilityMissionExperienceGainMultiplier(this, 1f);
                info?.Character?.CheckTalents(AbilityEffectType.OnGainMissionExperience, experienceGainMultiplierIndividual);
                info?.GiveExperience((int)((experienceGain * experienceGainMultiplier.Value) * experienceGainMultiplierIndividual.Value));
            }

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

                foreach (KeyValuePair<Identifier, float> reputationReward in ReputationRewards)
                {
                    if (reputationReward.Key == "location")
                    {
                        OriginLocation.Reputation?.AddReputation(reputationReward.Value);
                    }
                    else
                    {
                        Faction faction = campaign.Factions.Find(faction1 => faction1.Prefab.Identifier == reputationReward.Key);
                           float prevValue = faction.Reputation.Value;
                        faction?.Reputation.AddReputation(reputationReward.Value);
                    }
                }
            }

            if (Prefab.DataRewards != null)
            {
                foreach (var (identifier, value, operation) in Prefab.DataRewards)
                {
                    SetDataAction.PerformOperation(campaign.CampaignMetadata, identifier, value, operation);
                }
            }
        }

#if SERVER
        public static int DistributeRewardsToCrew(IEnumerable<Character> crew, int totalReward)
        {
            int remainingRewards = totalReward;
            float sum = GetRewardDistibutionSum(crew);
            if (MathUtils.NearlyEqual(sum, 0)) { return remainingRewards; }
            foreach (Character character in crew)
            {
                int rewardDistribution = character.Wallet.RewardDistribution;
                float rewardWeight = sum > 100 ? rewardDistribution / sum : rewardDistribution / 100f;
                int reward = (int)(totalReward * rewardWeight);
                reward = Math.Min(remainingRewards, reward);
                character.Wallet.Give(reward);
                remainingRewards -= reward;
                if (remainingRewards <= 0) { break; }
            }

            return remainingRewards;
        }
#endif

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

        // putting these here since both escort and pirate missions need them. could be tucked away into another class that they can inherit from (or use composition)
        protected HumanPrefab GetHumanPrefabFromElement(XElement element)
        {
            if (element.Attribute("name") != null)
            {
                DebugConsole.ThrowError("Error in mission \"" + Name + "\" - use character identifiers instead of names to configure the characters.");
                return null;
            }

            Identifier characterIdentifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);
            Identifier characterFrom = element.GetAttributeIdentifier("from", Identifier.Empty);
            HumanPrefab humanPrefab = NPCSet.Get(characterFrom, characterIdentifier);
            if (humanPrefab == null)
            {
                DebugConsole.ThrowError($"Couldn't spawn character for mission: character prefab \"{characterIdentifier}\" not found in the NPC set \"{characterFrom}\".");
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
            ItemPrefab itemPrefab;
            if (element.Attribute("name") != null)
            {
                DebugConsole.ThrowError($"Error in mission \"{Name}\" - use item identifiers instead of names to configure the items");
                string itemName = element.GetAttributeString("name", "");
                itemPrefab = MapEntityPrefab.Find(itemName) as ItemPrefab;
                if (itemPrefab == null)
                {
                    DebugConsole.ThrowError($"Couldn't spawn item for mission \"{Name}\": item prefab \"{itemName}\" not found");
                }
            }
            else
            {
                string itemIdentifier = element.GetAttributeString("identifier", "");
                itemPrefab = MapEntityPrefab.Find(null, itemIdentifier) as ItemPrefab;
                if (itemPrefab == null)
                {
                    DebugConsole.ThrowError($"Couldn't spawn item for mission \"{Name}\": item prefab \"{itemIdentifier}\" not found");
                }
            }
            return itemPrefab;
        }

        protected Vector2? GetCargoSpawnPosition(ItemPrefab itemPrefab, out Submarine cargoRoomSub)
        {
            cargoRoomSub = null;

            WayPoint cargoSpawnPos = WayPoint.GetRandom(SpawnType.Cargo, null, Submarine.MainSub, useSyncedRand: true);
            if (cargoSpawnPos == null)
            {
                DebugConsole.ThrowError($"Couldn't spawn items for mission \"{Name}\": no waypoints marked as Cargo were found");
                return null;
            }

            var cargoRoom = cargoSpawnPos.CurrentHull;
            if (cargoRoom == null)
            {
                DebugConsole.ThrowError($"Couldn't spawn items for mission \"{Name}\": waypoints marked as Cargo must be placed inside a room");
                return null;
            }

            cargoRoomSub = cargoRoom.Submarine;

            return new Vector2(
                cargoSpawnPos.Position.X + Rand.Range(-20.0f, 20.0f, Rand.RandSync.ServerAndClient),
                cargoRoom.Rect.Y - cargoRoom.Rect.Height + itemPrefab.Size.Y / 2);
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

    class AbilityMissionExperienceGainMultiplier : AbilityObject, IAbilityValue, IAbilityMission
    {
        public AbilityMissionExperienceGainMultiplier(Mission mission, float missionExperienceGainMultiplier)
        {
            Value = missionExperienceGainMultiplier;
            Mission = mission;
        }

        public float Value { get; set; }
        public Mission Mission { get; set; }
    }

}
