using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    [Flags]
    public enum MissionType
    {
        None = 0x0,
        Salvage = 0x1,
        Monster = 0x2,
        Cargo = 0x4,
        Combat = 0x8,
        All = 0xf
    }

    partial class MissionPrefab
    {
        public static readonly List<MissionPrefab> List = new List<MissionPrefab>();

        private static readonly Dictionary<MissionType, Type> missionClasses = new Dictionary<MissionType, Type>()
        {
            { MissionType.Salvage, typeof(SalvageMission) },
            { MissionType.Monster, typeof(MonsterMission) },
            { MissionType.Cargo, typeof(CargoMission) },
            { MissionType.Combat, typeof(CombatMission) },
        };
        
        private readonly ConstructorInfo constructor;

        public readonly MissionType Type;

        public readonly bool MultiplayerOnly, SingleplayerOnly;

        public readonly string Identifier;
        public readonly string TextIdentifier;

        private readonly string[] tags;
        public IEnumerable<string> Tags
        {
            get { return tags; }
        }

        public readonly string Name;
        public readonly string Description;
        public readonly string SuccessMessage;
        public readonly string FailureMessage;
        public readonly string SonarLabel;
        public readonly string SonarIconIdentifier;

        public readonly string AchievementIdentifier;

        public readonly Dictionary<string, float> ReputationRewards = new Dictionary<string, float>(); 

        public readonly int Commonness;

        public readonly int Reward;

        public readonly List<string> Headers;
        public readonly List<string> Messages;

        //the mission can only be received when travelling from Pair.First to Pair.Second
        public readonly List<Pair<string, string>> AllowedLocationTypes;

        public readonly XElement ConfigElement;

        public static void Init()
        {
            List.Clear();
            var files = GameMain.Instance.GetFilesOfType(ContentType.Missions);
            foreach (ContentFile file in files)
            {
                XDocument doc = XMLExtensions.TryLoadXml(file.Path);
                if (doc == null) { continue; }
                bool allowOverride = false;
                var mainElement = doc.Root;
                if (mainElement.IsOverride())
                {
                    allowOverride = true;
                    mainElement = mainElement.FirstElement();
                }

                foreach (XElement sourceElement in mainElement.Elements())
                {
                    var element = sourceElement.IsOverride() ? sourceElement.FirstElement() : sourceElement;
                    var identifier = element.GetAttributeString("identifier", string.Empty);
                    var duplicate = List.Find(m => m.Identifier == identifier);
                    if (duplicate != null)
                    {
                        if (allowOverride || sourceElement.IsOverride())
                        {
                            DebugConsole.NewMessage($"Overriding a mission with the identifier '{identifier}' using the file '{file.Path}'", Color.Yellow);
                            List.Remove(duplicate);
                        }
                        else
                        {
                            DebugConsole.ThrowError($"Duplicate mission found with the identifier '{identifier}' in file '{file.Path}'! Add <override></override> tags as the parent of the mission definition to allow overriding.");
                            // TODO: Don't allow adding duplicates when the issue with multiple missions is solved.
                            //continue;
                        }
                    }
                    List.Add(new MissionPrefab(element));
                }
            }
        }

        public MissionPrefab(XElement element)
        {
            ConfigElement = element;

            Identifier = element.GetAttributeString("identifier", "");
            TextIdentifier = element.GetAttributeString("textidentifier", null) ?? Identifier;

            tags = element.GetAttributeStringArray("tags", new string[0], convertToLowerInvariant: true);

            Name        = TextManager.Get("MissionName." + TextIdentifier, true) ?? element.GetAttributeString("name", "");
            Description = TextManager.Get("MissionDescription." + TextIdentifier, true) ?? element.GetAttributeString("description", "");
            Reward      = element.GetAttributeInt("reward", 1);

            Commonness  = element.GetAttributeInt("commonness", 1);

            SuccessMessage  = TextManager.Get("MissionSuccess." + TextIdentifier, true) ?? element.GetAttributeString("successmessage", "Mission completed successfully");
            FailureMessage  = TextManager.Get("MissionFailure." + TextIdentifier, true) ?? "";
            if (string.IsNullOrEmpty(FailureMessage) && TextManager.ContainsTag("missionfailed"))
            {
                FailureMessage = TextManager.Get("missionfailed", returnNull: true) ?? "";
            }
            if (string.IsNullOrEmpty(FailureMessage) && GameMain.Config.Language == "English")
            {
                FailureMessage = element.GetAttributeString("failuremessage", "");
            }

            SonarLabel          = TextManager.Get("MissionSonarLabel." + TextIdentifier, true) ?? element.GetAttributeString("sonarlabel", "");
            SonarIconIdentifier = element.GetAttributeString("sonaricon", "");

            MultiplayerOnly     = element.GetAttributeBool("multiplayeronly", false);
            SingleplayerOnly    = element.GetAttributeBool("singleplayeronly", false);

            AchievementIdentifier = element.GetAttributeString("achievementidentifier", "");

            Headers = new List<string>();
            Messages = new List<string>();
            AllowedLocationTypes = new List<Pair<string, string>>();
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "message":
                        int index = Messages.Count;

                        Headers.Add(TextManager.Get("MissionHeader" + index + "." + TextIdentifier, true) ?? subElement.GetAttributeString("header", ""));
                        Messages.Add(TextManager.Get("MissionMessage" + index + "." + TextIdentifier, true) ?? subElement.GetAttributeString("text", ""));
                        break;
                    case "locationtype":
                        AllowedLocationTypes.Add(new Pair<string, string>(
                            subElement.GetAttributeString("from", ""),
                            subElement.GetAttributeString("to", "")));
                        break;
                    case "reputation":
                    case "reputationreward":
                        string factionIdentifier = subElement.GetAttributeString("identifier", "");
                        float amount = subElement.GetAttributeFloat("amount", 0.0f);
                        if (ReputationRewards.ContainsKey(factionIdentifier))
                        {
                            DebugConsole.ThrowError($"Error in mission prefab \"{Identifier}\". Multiple reputation changes defined for the identifier \"{factionIdentifier}\".");
                            continue;
                        }
                        ReputationRewards.Add(factionIdentifier, amount);
                        if (!factionIdentifier.Equals("location", StringComparison.OrdinalIgnoreCase))
                        {
                            if (FactionPrefab.Prefabs != null && !FactionPrefab.Prefabs.Any(p => p.Identifier.Equals(factionIdentifier, StringComparison.OrdinalIgnoreCase)))
                            {
                                DebugConsole.ThrowError($"Error in mission prefab \"{Identifier}\". Could not find a faction with the identifier \"{factionIdentifier}\".");
                            }
                        }
                        break;
                }
            }

            string missionTypeName = element.GetAttributeString("type", "");
            if (!Enum.TryParse(missionTypeName, out Type))
            {
                DebugConsole.ThrowError("Error in mission prefab \"" + Name + "\" - \"" + missionTypeName + "\" is not a valid mission type.");
                return;
            }
            if (Type == MissionType.None)
            {
                DebugConsole.ThrowError("Error in mission prefab \"" + Name + "\" - mission type cannot be none.");
                return;
            }

            constructor = missionClasses[Type].GetConstructor(new[] { typeof(MissionPrefab), typeof(Location[]) });

            InitProjSpecific(element);
        }
        
        partial void InitProjSpecific(XElement element);

        public bool IsAllowed(Location from, Location to)
        {
            foreach (Pair<string, string> allowedLocationType in AllowedLocationTypes)
            {
                if (allowedLocationType.First.Equals("any", StringComparison.OrdinalIgnoreCase) ||
                    allowedLocationType.First.Equals(from.Type.Identifier, StringComparison.OrdinalIgnoreCase))
                {
                    if (allowedLocationType.Second.Equals("any", StringComparison.OrdinalIgnoreCase) ||
                        allowedLocationType.Second.Equals(to.Type.Identifier, StringComparison.OrdinalIgnoreCase))
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
    }
}
