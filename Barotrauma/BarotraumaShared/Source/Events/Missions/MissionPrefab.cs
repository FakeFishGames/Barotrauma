using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma
{
    class MissionPrefab
    {
        public static readonly List<string> MissionTypes = new List<string>() { "Random" };
        public static readonly List<MissionPrefab> List = new List<MissionPrefab>();

        private Type missionType;
        private ConstructorInfo constructor;

        public readonly bool MultiplayerOnly, SingleplayerOnly;

        public readonly string Name;
        public readonly string Description;
        public readonly string SuccessMessage;
        public readonly string FailureMessage;
        public readonly string RadarLabel;

        public readonly int Commonness;

        public readonly int Reward;

        public readonly List<string> Headers;
        public readonly List<string> Messages;

        //the mission can only be received when travelling from Pair.First to Pair.Second
        public readonly List<Pair<string, string>> AllowedLocationTypes;

        public readonly XElement ConfigElement;

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
                    if (!MissionTypes.Contains(missionTypeName)) MissionTypes.Add(missionTypeName);
                    List.Add(new MissionPrefab(element));
                }
            }
        }

        public MissionPrefab(XElement element)
        {
            ConfigElement = element;
            
            Name = element.GetAttributeString("name", "");
            Description = element.GetAttributeString("description", "");
            Reward = element.GetAttributeInt("reward", 1);

            Commonness = element.GetAttributeInt("commonness", 1);

            SuccessMessage = element.GetAttributeString("successmessage", "Mission completed successfully");
            FailureMessage = element.GetAttributeString("failuremessage", "Mission failed");

            MultiplayerOnly = element.GetAttributeBool("multiplayeronly", false);
            SingleplayerOnly = element.GetAttributeBool("singleplayeronly", false);

            RadarLabel = element.GetAttributeString("radarlabel", "");

            Headers = new List<string>();
            Messages = new List<string>();
            AllowedLocationTypes = new List<Pair<string, string>>();
            foreach (XElement subElement in element.Elements())
            {
                switch(subElement.Name.ToString().ToLowerInvariant())
                {
                    case "message":
                        Headers.Add(subElement.GetAttributeString("header", ""));
                        Messages.Add(subElement.GetAttributeString("text", ""));
                        break;
                    case "locationtype":
                        AllowedLocationTypes.Add(new Pair<string, string>(
                            subElement.GetAttributeString("from", ""), 
                            subElement.GetAttributeString("to", "")));

                        break;
                }                
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

        public bool IsAllowed(Location from, Location to)
        {
            foreach (Pair<string, string> allowedLocationType in AllowedLocationTypes)
            {
                if (allowedLocationType.First.ToLowerInvariant() == "any" ||
                    allowedLocationType.First.ToLowerInvariant() == from.Type.Name.ToLowerInvariant())
                {
                    if (allowedLocationType.Second.ToLowerInvariant() == "any" ||
                        allowedLocationType.Second.ToLowerInvariant() == to.Type.Name.ToLowerInvariant())
                    {
                        return true;
                    }
                }
            }

            return false;
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
