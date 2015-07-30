using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace Subsurface
{
    class Quest
    {
        private static List<Quest> list = new List<Quest>();

        private static string configFile = "Content/Quests.xml";

        private string name;

        private string description;

        protected bool completed;

        private string successMessage;
        private string failureMessage;

        private int reward;

        public string Name
        {
            get { return name; }
        }

        public string Description
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
        }

        public virtual string RadarLabel
        {
            get { return ""; }
        }

        public virtual Vector2 RadarPosition
        {
            get { return Vector2.Zero; }
        }

        public Quest(XElement element)
        {
            name = ToolBox.GetAttributeString(element, "name", "");

            description = ToolBox.GetAttributeString(element, "description", "");

            reward = ToolBox.GetAttributeInt(element, "reward", 1);

            successMessage = ToolBox.GetAttributeString(element, "successmessage", "");
            failureMessage = ToolBox.GetAttributeString(element, "failuremessage", "");
        }

        public static Quest LoadRandom(Location[] locations, Random rand)
        {
            XDocument doc = ToolBox.TryLoadXml(configFile);
            if (doc == null) return null;

            int eventCount = doc.Root.Elements().Count();
            //int[] commonness = new int[eventCount];
            float[] eventProbability = new float[eventCount];

            float probabilitySum = 0.0f;

            int i = 0;
            foreach (XElement element in doc.Root.Elements())
            {
                eventProbability[i] = ToolBox.GetAttributeInt(element, "commonness", 1);

                probabilitySum += eventProbability[i];

                i++;
            }

            float randomNumber = (float)rand.NextDouble() * probabilitySum;

            i = 0;
            foreach (XElement element in doc.Root.Elements())
            {
                if (randomNumber <= eventProbability[i])
                {
                    Type t;
                    string type = element.Name.ToString();

                    try
                    {
                        t = Type.GetType("Subsurface." + type + ", Subsurface", true, true);
                        if (t == null)
                        {
                            DebugConsole.ThrowError("Error in " + configFile + "! Could not find a quest class of the type ''" + type + "''.");
                            continue;
                        }
                    }
                    catch
                    {
                        DebugConsole.ThrowError("Error in " + configFile + "! Could not find a an event class of the type ''" + type + "''.");
                        continue;
                    }

                    ConstructorInfo constructor = t.GetConstructor(new[] { typeof(XElement) });
                    object instance = constructor.Invoke(new object[] { element });

                    Quest quest = (Quest)instance;
                    quest.description = quest.description.Replace("[location1]", locations[0].Name);
                    quest.description = quest.description.Replace("[location2]", locations[1].Name);

                    return quest;
                }

                randomNumber -= eventProbability[i];
                i++;
            }

            return null;
        }

        public virtual void Start(Level level)
        {
        }

        /// <summary>
        /// End the quest and give a reward if it was completed successfully
        /// </summary>
        /// <returns>whether the quest was completed or not</returns>
        public virtual void End()
        {
            completed = true;

            GiveReward();
        }

        public void GiveReward()
        {
            var mode = Game1.GameSession.gameMode as SinglePlayerMode;
            mode.Money += reward;

            if (!string.IsNullOrWhiteSpace(successMessage))
            {
                new GUIMessageBox("Quest completed", successMessage);
            }
        }
    }
}
