using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma
{
    class MissionPrefab
    {
        public static List<MissionPrefab> List = new List<MissionPrefab>();
        public static List<string> MissionTypes = new List<string>() { "Random" };

        private string name;

        public string Name
        {
            get { return name; }
        }

        private Type missionType;
        private ConstructorInfo constructor;

        public virtual string Description { get; private set; }

        public bool MultiplayerOnly { get; private set; }
        public bool SingleplayerOnly { get; private set; }

        public float Commonness { get; private set; }

        public int Reward { get; private set; }

        public string RadarLabel { get; private set; }

        public List<string> Headers { get; private set; }
        public List<string> Messages { get; private set; }

        public string SuccessMessage { get; private set; }
        public string FailureMessage { get; private set; }

        public XElement ConfigElement { get; private set; }

        public static void Init()
        {
            var files = GameMain.SelectedPackage.GetFilesOfType(ContentType.Missions);
            foreach (string file in files)
            {
                XDocument doc = XMLExtensions.TryLoadXml(file);
                if (doc?.Root == null) continue;

                foreach (XElement element in doc.Root.Elements())
                {
                    string missionTypeName = element.Name.ToString();
                    missionTypeName = missionTypeName.Replace("Mission", "");

                    List.Add(new MissionPrefab(element));
                    if (!MissionTypes.Contains(missionTypeName)) MissionTypes.Add(missionTypeName);
                }

            }
        }

        public MissionPrefab(XElement element)
        {
            ConfigElement = element;

            name = element.GetAttributeString("name", "");
            Description = element.GetAttributeString("description", "");
            Commonness = element.GetAttributeFloat("commonness", 1.0f);
            SingleplayerOnly = element.GetAttributeBool("singleplayeronly", false);
            MultiplayerOnly = element.GetAttributeBool("multiplayeronly", false);

            Reward = element.GetAttributeInt("reward", 1);

            SuccessMessage = element.GetAttributeString("successmessage", "Mission completed successfully");
            FailureMessage = element.GetAttributeString("failuremessage", "Mission failed");
            RadarLabel = element.GetAttributeString("radarlabel", "");

            Messages = new List<string>();
            Headers = new List<string>();
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "message") continue;
                Headers.Add(subElement.GetAttributeString("header", ""));
                Messages.Add(subElement.GetAttributeString("text", ""));
            }

            string type = element.Name.ToString();

            try
            {
                missionType = Type.GetType("Barotrauma." + type, true, true);
                if (missionType == null)
                {
                    DebugConsole.ThrowError("Error in mission prefab " + Name + "! Could not find a mission class of the type \"" + type + "\".");
                    return;
                }
            }
            catch
            {
                DebugConsole.ThrowError("Error in mission prefab " + Name + "! Could not find a mission class of the type \"" + type + "\".");
                return;
            }
            constructor = missionType.GetConstructor(new[] { typeof(MissionPrefab), typeof(Location[]) });
        }

        public Mission Instantiate(Location[] locations)
        {
            return constructor?.Invoke(new object[] { this, locations }) as Mission;
        }

        public bool TypeMatches(string typeName)
        {
            //TODO: use enums instead of strings?
            typeName = typeName.ToLowerInvariant();
            return missionType.Name.ToString().Replace("Mission", "").ToLowerInvariant() == typeName;
        }
    }
}