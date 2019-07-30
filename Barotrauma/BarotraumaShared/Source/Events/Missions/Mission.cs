using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Barotrauma
{
    partial class Mission
    {
        public readonly MissionPrefab Prefab;
        protected bool completed;
                
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

        private string description;
        public virtual string Description
        {
            get { return description; }
            private set { description = value; }
        }

        public int Reward
        {
            get { return Prefab.Reward; }
        }

        public bool Completed
        {
            get { return completed; }
            set { completed = value; }
        }
        
        public virtual bool AllowRespawn
        {
            get { return true; }
        }

        public virtual IEnumerable<Vector2> SonarPositions
        {
            get { return Enumerable.Empty<Vector2>(); }
        }
        
        public string SonarLabel
        {
            get { return Prefab.SonarLabel; }
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
            if (missionType == MissionType.Random)
            {
                allowedMissions.AddRange(MissionPrefab.List);
            }
            else if (missionType == MissionType.None)
            {
                return null;
            }
            else
            {
                allowedMissions = MissionPrefab.List.FindAll(m => m.type == missionType);
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

        public virtual void Start(Level level) { }

        public virtual void Update(float deltaTime) { }

        public virtual bool AssignTeamIDs(List<Networking.Client> clients)
        {
            clients.ForEach(c => c.TeamID = Character.TeamType.Team1);
            return false; 
        }

        protected void ShowMessage(int index)
        {
            ShowMessageProjSpecific(index);
        }

        partial void ShowMessageProjSpecific(int index);

        /// <summary>
        /// End the mission and give a reward if it was completed successfully
        /// </summary>
        public virtual void End()
        {
            completed = true;

            GiveReward();
        }

        public void GiveReward()
        {
            var mode = GameMain.GameSession.GameMode as CampaignMode;
            if (mode == null) return;
            
            mode.Money += Reward;
        }
    }
}
