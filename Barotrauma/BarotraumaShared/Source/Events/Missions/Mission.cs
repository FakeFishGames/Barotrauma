using Microsoft.Xna.Framework;
using System;
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

        public virtual string RadarLabel
        {
            get { return Prefab.RadarLabel; }
        }

        public virtual Vector2 RadarPosition
        {
            get { return Vector2.Zero; }
        }
        
        public Mission(MissionPrefab prefab, Location[] locations)
        {
            Prefab = prefab;

            Description = prefab.Description;
            SuccessMessage = prefab.SuccessMessage;
            FailureMessage = prefab.FailureMessage;

            Headers = new List<string>(prefab.Headers);
            Messages = new List<string>(prefab.Messages);
            
            for (int n = 0; n < 2; n++)
            {
                Description = Description.Replace("[location" + (n + 1) + "]", locations[n].Name);

                SuccessMessage = SuccessMessage.Replace("[location" + (n + 1) + "]", locations[n].Name);
                FailureMessage = FailureMessage.Replace("[location" + (n + 1) + "]", locations[n].Name);

                for (int m = 0; m < Messages.Count; m++)
                {
                    Messages[m] = Messages[m].Replace("[location" + (n + 1) + "]", locations[n].Name);
                }
            }
        }
        
        public static Mission CreateRandom(Location[] locations, MTRandom rand, bool requireCorrectLocationType, string missionType = "", bool isSinglePlayer = false)
        {
            missionType = missionType.ToLowerInvariant();
            
            List<MissionPrefab> matchingMissions;
            if (missionType == "random" || string.IsNullOrWhiteSpace(missionType))
            {
                matchingMissions = new List<MissionPrefab>(MissionPrefab.List);
            }
            else if (missionType == "none")
            {
                return null;
            }
            else
            {
                matchingMissions = MissionPrefab.List.FindAll(m => m.Name.ToString().ToLowerInvariant().Replace("mission", "") == missionType);
            }
            
            matchingMissions.RemoveAll(m => isSinglePlayer ? m.MultiplayerOnly : m.SingleplayerOnly);

            if (requireCorrectLocationType)
            {
                matchingMissions.RemoveAll(m => !m.IsAllowed(locations[0], locations[1]));
            }
            
            float probabilitySum = matchingMissions.Sum(m => m.Commonness);
            float randomNumber = (float)rand.NextDouble() * probabilitySum;
            
            foreach (MissionPrefab missionPrefab in matchingMissions)
            {
                if (randomNumber <= missionPrefab.Commonness)
                {
                    Type t;
                    string type = missionPrefab.TypeName;

                    try
                    {
                        t = Type.GetType("Barotrauma." + type, true, true);
                        if (t == null)
                        {
                            DebugConsole.ThrowError("Error in mission prefab " + missionPrefab.Name + "! Could not find a mission class of the type \"" + type + "\".");
                            continue;
                        }
                    }
                    catch
                    {
                        DebugConsole.ThrowError("Error in mission prefab " + missionPrefab.Name + "! Could not find a mission class of the type \"" + type + "\".");
                        continue;
                    }
                    
                    ConstructorInfo constructor = t.GetConstructor(new[] { typeof(MissionPrefab), typeof(Location[]) });                    
                    object instance = constructor.Invoke(new object[] { missionPrefab, locations });                   
                    return (Mission)instance;
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

            mode.Money += Prefab.Reward;
        }
    }
}
