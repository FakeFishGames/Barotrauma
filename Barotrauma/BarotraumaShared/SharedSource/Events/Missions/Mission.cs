using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    abstract partial class Mission
    {
        public readonly MissionPrefab Prefab;
        protected bool completed, failed;

        protected Level level;

        protected int state;
        public int State
        {
            get { return state; }
            protected set
            {
                if (state != value)
                {
                    state = value;
#if SERVER
                    GameMain.Server?.UpdateMissionState(this, state);
#endif
                    ShowMessage(State);
                }
            }
        }

        protected bool IsClient => GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient;

        public readonly List<string> Headers;
        public readonly List<string> Messages;
        
        public string Name
        {
            get { return Prefab.Name; }
        }

        private string successMessage;
        public virtual string SuccessMessage
        {
            get { return successMessage; }
            private set { successMessage = value; }
        }

        private string failureMessage;
        public virtual string FailureMessage
        {
            get { return failureMessage; }
            private set { failureMessage = value; }
        }

        protected string description;
        public virtual string Description
        {
            get { return description; }
            private set { description = value; }
        }

        public int Reward
        {
            get { return Prefab.Reward; }
        }

        public Dictionary<string, float> ReputationRewards
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

        public virtual IEnumerable<Vector2> SonarPositions
        {
            get { return Enumerable.Empty<Vector2>(); }
        }
        
        public virtual string SonarLabel
        {
            get { return Prefab.SonarLabel; }
        }
        public string SonarIconIdentifier
        {
            get { return Prefab.SonarIconIdentifier; }
        }

        public readonly Location[] Locations;
           
        public Mission(MissionPrefab prefab, Location[] locations)
        {
            System.Diagnostics.Debug.Assert(locations.Length == 2);

            Prefab = prefab;

            description = prefab.Description;
            successMessage = prefab.SuccessMessage;
            FailureMessage = prefab.FailureMessage;
            Headers = new List<string>(prefab.Headers);
            Messages = new List<string>(prefab.Messages);

            Locations = locations;

            for (int n = 0; n < 2; n++)
            {
                if (description != null) description = description.Replace("[location" + (n + 1) + "]", locations[n].Name);
                if (successMessage != null) successMessage = successMessage.Replace("[location" + (n + 1) + "]", locations[n].Name);
                if (failureMessage != null) failureMessage = failureMessage.Replace("[location" + (n + 1) + "]", locations[n].Name);
                for (int m = 0; m < Messages.Count; m++)
                {
                    Messages[m] = Messages[m].Replace("[location" + (n + 1) + "]", locations[n].Name);
                }
            }
            if (description != null) description = description.Replace("[reward]", Reward.ToString("N0"));
            if (successMessage != null) successMessage = successMessage.Replace("[reward]", Reward.ToString("N0"));
            if (failureMessage != null) failureMessage = failureMessage.Replace("[reward]", Reward.ToString("N0"));
            for (int m = 0; m < Messages.Count; m++)
            {
                Messages[m] = Messages[m].Replace("[reward]", Reward.ToString("N0"));
            }
        }
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
                allowedMissions.AddRange(MissionPrefab.List.Where(m => ((int)(missionType & m.Type)) != 0));
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
                    return missionPrefab.Instantiate(locations);
                }
                randomNumber -= missionPrefab.Commonness;
            }

            return null;
        }

        public void Start(Level level)
        {
            foreach (string categoryToShow in Prefab.UnhideEntitySubCategories)
            {
                foreach (MapEntity entityToShow in MapEntity.mapEntityList.Where(me => me.prefab.HasSubCategory(categoryToShow)))
                {
                    entityToShow.HiddenInGame = false;
                }
            }
            this.level = level;
            StartMissionSpecific(level);
        }

        protected virtual void StartMissionSpecific(Level level) { }

        public virtual void Update(float deltaTime) { }

        protected void ShowMessage(int missionState)
        {
            ShowMessageProjSpecific(missionState);
        }

        partial void ShowMessageProjSpecific(int missionState);

        /// <summary>
        /// End the mission and give a reward if it was completed successfully
        /// </summary>
        public virtual void End()
        {
            completed = true;
            if (Prefab.LocationTypeChangeOnCompleted != null)
            {
                ChangeLocationType(Prefab.LocationTypeChangeOnCompleted);
            }
            GiveReward();
        }

        public void GiveReward()
        {
            if (!(GameMain.GameSession.GameMode is CampaignMode campaign)) { return; }
            campaign.Money += Reward;

            foreach (KeyValuePair<string, float> reputationReward in ReputationRewards)
            {
                if (reputationReward.Key.Equals("location", StringComparison.OrdinalIgnoreCase))
                {
                    Locations[0].Reputation.Value += reputationReward.Value;
                    Locations[1].Reputation.Value += reputationReward.Value;
                }
                else
                {
                    Faction faction = campaign.Factions.Find(faction1 => faction1.Prefab.Identifier.Equals(reputationReward.Key, StringComparison.OrdinalIgnoreCase));
                    if (faction != null) { faction.Reputation.Value += reputationReward.Value; }
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

        protected void ChangeLocationType(LocationTypeChange change)
        {
            if (change == null) { throw new ArgumentException(); }
            if (GameMain.GameSession.GameMode is CampaignMode && !IsClient)
            {
                int srcIndex = -1;
                for (int i = 0; i < Locations.Length; i++)
                {
                    if (Locations[i].Type.Identifier.Equals(change.CurrentType, StringComparison.OrdinalIgnoreCase))
                    {
                        srcIndex = i;
                        break;
                    }
                }
                if (srcIndex == -1) { return; }
                var location = Locations[srcIndex];

                if (change.RequiredDurationRange.X > 0)
                {
                    location.PendingLocationTypeChange = (change, Rand.Range(change.RequiredDurationRange.X, change.RequiredDurationRange.Y), Prefab);
                }
                else
                {
                    location.ChangeType(LocationType.List.Find(lt => lt.Identifier.Equals(change.ChangeToType, StringComparison.OrdinalIgnoreCase)));
                    location.LocationTypeChangeCooldown = change.CooldownAfterChange;
                }
            }
        }

        public virtual void AdjustLevelData(LevelData levelData) { }
    }
}
