using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma
{
    class Mission
    {
        public static List<string> MissionTypes = new List<string>() { "Random" };
        
        private string name;

        private string description;

        protected bool completed;

        protected string successMessage;
        protected string failureMessage;

        protected string radarLabel;

        protected List<string> headers;
        protected List<string> messages;

        private int reward;
        
        public string Name
        {
            get { return name; }
        }

        public virtual string Description
        {
            get { return description; }
        }

        public int Reward
        {
            get { return reward; }
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
            get { return radarLabel; }
        }

        public virtual Vector2 RadarPosition
        {
            get { return Vector2.Zero; }
        }

        virtual public string SuccessMessage
        {
            get { return successMessage; }
        }

        public string FailureMessage
        {
            get { return failureMessage; }
        }

        public static void Init()
        {
            var files = GameMain.SelectedPackage.GetFilesOfType(ContentType.Missions);
            foreach (string file in files)
            {
                XDocument doc = ToolBox.TryLoadXml(file);
                if (doc == null || doc.Root == null) continue;

                foreach (XElement element in doc.Root.Elements())
                {
                    string missionTypeName = element.Name.ToString();
                    missionTypeName = missionTypeName.Replace("Mission", "");

                    if (!MissionTypes.Contains(missionTypeName)) MissionTypes.Add(missionTypeName);
                }

            }
        }

        public Mission(XElement element, Location[] locations)
        {
            name = ToolBox.GetAttributeString(element, "name", "");

            description = ToolBox.GetAttributeString(element, "description", "");

            reward = ToolBox.GetAttributeInt(element, "reward", 1);

            successMessage = ToolBox.GetAttributeString(element, "successmessage", 
                "Mission completed successfully");
            failureMessage = ToolBox.GetAttributeString(element, "failuremessage", 
                "Mission failed");

            radarLabel = ToolBox.GetAttributeString(element, "radarlabel", "");

            messages = new List<string>();
            headers = new List<string>();
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "message") continue;
                headers.Add(ToolBox.GetAttributeString(subElement, "header", ""));
                messages.Add(ToolBox.GetAttributeString(subElement, "text", ""));
            }
            
            for (int n = 0; n < 2; n++)
            {
                description = description.Replace("[location" + (n + 1) + "]", locations[n].Name);

                successMessage = successMessage.Replace("[location" + (n + 1) + "]", locations[n].Name);
                failureMessage = failureMessage.Replace("[location" + (n + 1) + "]", locations[n].Name);

                for (int m = 0; m < messages.Count; m++)
                {
                    messages[m] = messages[m].Replace("[location" + (n + 1) + "]", locations[n].Name);
                }
            }
        }

        public static Mission LoadRandom(Location[] locations, MTRandom rand, string missionType = "", bool isSinglePlayer = false)
        {
            missionType = missionType.ToLowerInvariant();

            var files = GameMain.SelectedPackage.GetFilesOfType(ContentType.Missions);
            string configFile = files[rand.Next(files.Count)];

            XDocument doc = ToolBox.TryLoadXml(configFile);
            if (doc == null) return null;

            int eventCount = doc.Root.Elements().Count();
            //int[] commonness = new int[eventCount];
            float[] eventProbability = new float[eventCount];

            float probabilitySum = 0.0f;

            List<XElement> matchingElements = new List<XElement>();

            if (missionType == "random")
            {
                matchingElements = doc.Root.Elements().ToList();
            }
            else if (missionType == "none")
            {
                return null;
            }
            else if (string.IsNullOrWhiteSpace(missionType))
            {
                matchingElements = doc.Root.Elements().ToList();           
            }
            else
            {
                matchingElements = doc.Root.Elements().ToList().FindAll(m => m.Name.ToString().ToLowerInvariant().Replace("mission", "") == missionType);
            }

            if (isSinglePlayer)
            {
                matchingElements.RemoveAll(m => ToolBox.GetAttributeBool(m, "multiplayeronly", false));
            }
            else
            {
                matchingElements.RemoveAll(m => ToolBox.GetAttributeBool(m, "singleplayeronly", false));
            }
            
            int i = 0;
            foreach (XElement element in matchingElements)
            {
                eventProbability[i] = ToolBox.GetAttributeInt(element, "commonness", 1);

                probabilitySum += eventProbability[i];

                i++;
            }

            float randomNumber = (float)rand.NextDouble() * probabilitySum;

            i = 0;
            foreach (XElement element in matchingElements)
            {
                if (randomNumber <= eventProbability[i])
                {
                    Type t;
                    string type = element.Name.ToString();

                    try
                    {
                        t = Type.GetType("Barotrauma." + type, true, true);
                        if (t == null)
                        {
                            DebugConsole.ThrowError("Error in " + configFile + "! Could not find a mission class of the type \"" + type + "\".");
                            continue;
                        }
                    }
                    catch
                    {
                        DebugConsole.ThrowError("Error in " + configFile + "! Could not find a mission class of the type \"" + type + "\".");
                        continue;
                    }
                    
                    ConstructorInfo constructor = t.GetConstructor(new[] { typeof(XElement), typeof(Location[]) });
                    
                    object instance = constructor.Invoke(new object[] { element, locations });

                    Mission mission = (Mission)instance;
                    
                    return mission;
                }

                randomNumber -= eventProbability[i];
                i++;
            }

            return null;
        }

        public virtual void Start(Level level) { }

        public virtual void Update(float deltaTime) { }

        public virtual bool AssignTeamIDs(List<Networking.Client> clients, out int hostTeam)
        {
            clients.ForEach(c => c.TeamID = 1);
            hostTeam = 1; 
            return false; 
        }

        public void ShowMessage(int index)
        {
            if (index >= headers.Count && index >= messages.Count) return;

            string header = index < headers.Count ? headers[index] : "";
            string message = index < messages.Count ? messages[index] : "";

            GameServer.Log("Mission info: " + header + " - " + message, ServerLog.MessageType.Error);

            new GUIMessageBox(header, message);
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
            var mode = GameMain.GameSession.gameMode as SinglePlayerMode;
            if (mode == null) return;

            mode.Money += reward;
        }
    }
}
