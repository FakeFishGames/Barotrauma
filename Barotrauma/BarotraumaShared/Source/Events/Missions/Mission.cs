using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Mission
    {
        protected bool completed;

        private readonly MissionPrefab prefab;

        public string Name
        {
            get { return prefab.Name; }
        }

        public bool Completed
        {
            get { return completed; }
            set { completed = value; }
        }

        public int Reward
        {
            get { return prefab.Reward; }
        }

        public virtual bool AllowRespawn
        {
            get { return true; }
        }

        public virtual Vector2 RadarPosition
        {
            get { return Vector2.Zero; }
        }

        public string RadarLabel
        {
            get { return prefab.RadarLabel; }
        }

        public List<string> Headers
        {
            get; private set;
        }

        public List<string> Messages
        {
            get; private set;
        }

        public virtual string SuccessMessage
        {
            get;
            protected set;
        }

        public string FailureMessage
        {
            get;
            protected set;
        }

        public virtual string Description
        {
            get;
            protected set;
        }

        public MissionPrefab Prefab
        {
            get { return prefab; }
        }

        public Mission(MissionPrefab prefab, Location[] locations)
        {
            System.Diagnostics.Debug.Assert(locations.Length == 2);

            this.prefab = prefab;

            Description = prefab.Description;
            SuccessMessage = prefab.SuccessMessage;
            FailureMessage = prefab.FailureMessage;
            Headers = new List<string>(prefab.Headers);
            Messages = new List<string>(prefab.Messages);

            for (int n = 0; n < 2; n++)
            {
                if (Description != null) Description = Description.Replace("[location" + (n + 1) + "]", locations[n].Name);
                if (SuccessMessage != null) SuccessMessage = SuccessMessage.Replace("[location" + (n + 1) + "]", locations[n].Name);
                if (FailureMessage != null) FailureMessage = FailureMessage.Replace("[location" + (n + 1) + "]", locations[n].Name);
                for (int m = 0; m < Messages.Count; m++)
                {
                    Messages[m] = Messages[m].Replace("[location" + (n + 1) + "]", locations[n].Name);
                }
            }
        }

        public static Mission LoadRandom(Location[] locations, string seed, string missionType = "", bool isSinglePlayer = false)
        {
            return LoadRandom(locations, new MTRandom(ToolBox.StringToInt(seed)), missionType, isSinglePlayer);
        }


        public static Mission LoadRandom(Location[] locations, MTRandom rand, string missionType = "", bool isSinglePlayer = false)
        {
            //todo: use something else than strings to define the mission type
            missionType = missionType.ToLowerInvariant();

            List<MissionPrefab> allowedMissions = new List<MissionPrefab>();
            if (missionType == "random")
            {
                allowedMissions.AddRange(MissionPrefab.List);
                if (GameMain.Server != null)
                {
                    allowedMissions.RemoveAll(mission => !GameMain.Server.AllowedRandomMissionTypes.Any(a => mission.TypeMatches(a)));
                }
            }
            else if (missionType == "none")
            {
                return null;
            }
            else if (string.IsNullOrWhiteSpace(missionType))
            {
                allowedMissions.AddRange(MissionPrefab.List);
            }
            else
            {
                allowedMissions = MissionPrefab.List.FindAll(m => m.TypeMatches(missionType));
            }

            if (isSinglePlayer)
            {
                allowedMissions.RemoveAll(m => m.MultiplayerOnly);
            }
            else
            {
                allowedMissions.RemoveAll(m => m.SingleplayerOnly);
            }

            float probabilitySum = allowedMissions.Sum(m => m.Commonness);
            float randomNumber = (float)rand.NextDouble() * probabilitySum;
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

        public virtual bool AssignTeamIDs(List<Networking.Client> clients, out byte hostTeam)
        {
            clients.ForEach(c => c.TeamID = 1);
            hostTeam = 1; 
            return false; 
        }

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

            mode.Money += Convert.ToInt32(Math.Round((Reward * GameMain.NilMod.CampaignBaseRewardMultiplier) + GameMain.NilMod.CampaignBonusMissionReward,0));
        }
    }
}
