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
        Beacon = 0x8,
        Nest = 0x10,
        Mineral = 0x20,
        Combat = 0x40,
        OutpostDestroy = 0x80,
        OutpostRescue = 0x100,
        Escort = 0x200,
        Pirate = 0x400,
        All = Salvage | Monster | Cargo | Beacon | Nest | Mineral | Combat | OutpostDestroy | OutpostRescue | Escort | Pirate
    }

    partial class MissionPrefab
    {
        public static readonly List<MissionPrefab> List = new List<MissionPrefab>();

        public static readonly Dictionary<MissionType, Type> CoOpMissionClasses = new Dictionary<MissionType, Type>()
        {
            { MissionType.Salvage, typeof(SalvageMission) },
            { MissionType.Monster, typeof(MonsterMission) },
            { MissionType.Cargo, typeof(CargoMission) },
            { MissionType.Beacon, typeof(BeaconMission) },
            { MissionType.Nest, typeof(NestMission) },
            { MissionType.Mineral, typeof(MineralMission) },
            { MissionType.OutpostDestroy, typeof(OutpostDestroyMission) },
            { MissionType.OutpostRescue, typeof(AbandonedOutpostMission) },
            { MissionType.Escort, typeof(EscortMission) },
            { MissionType.Pirate, typeof(PirateMission) }
        };
        public static readonly Dictionary<MissionType, Type> PvPMissionClasses = new Dictionary<MissionType, Type>()
        {
            { MissionType.Combat, typeof(CombatMission) }
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
        public readonly List<Tuple<string, object, SetDataAction.OperationType>> DataRewards = new List<Tuple<string, object, SetDataAction.OperationType>>();

        public readonly int Commonness;
        public readonly int? Difficulty;
        public const int MinDifficulty = 1, MaxDifficulty = 4;

        public readonly int Reward;

        public readonly List<string> Headers;
        public readonly List<string> Messages;

        public readonly bool AllowRetry;

        public readonly bool IsSideObjective;

        /// <summary>
        /// The mission can only be received when travelling from Pair.First to Pair.Second
        /// </summary>
        public readonly List<Pair<string, string>> AllowedConnectionTypes;

        /// <summary>
        /// The mission can only be received in these location types
        /// </summary>
        public readonly List<string> AllowedLocationTypes = new List<string>();

        /// <summary>
        /// Show entities belonging to these sub categories when the mission starts
        /// </summary>
        public readonly List<string> UnhideEntitySubCategories = new List<string>();

        public LocationTypeChange LocationTypeChangeOnCompleted;

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
            AllowRetry  = element.GetAttributeBool("allowretry", false);
            IsSideObjective = element.GetAttributeBool("sideobjective", false);
            Commonness  = element.GetAttributeInt("commonness", 1);
            if (element.GetAttribute("difficulty") != null)
            {
                int difficulty = element.GetAttributeInt("difficulty", MinDifficulty);
                Difficulty = Math.Clamp(difficulty, MinDifficulty, MaxDifficulty);
            }

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

            SonarLabel          = 
                TextManager.Get("MissionSonarLabel." + TextIdentifier, true) ?? 
                TextManager.Get("MissionSonarLabel." + element.GetAttributeString("sonarlabel", ""), true) ?? 
                element.GetAttributeString("sonarlabel", "");
            SonarIconIdentifier = element.GetAttributeString("sonaricon", "");

            MultiplayerOnly     = element.GetAttributeBool("multiplayeronly", false);
            SingleplayerOnly    = element.GetAttributeBool("singleplayeronly", false);

            AchievementIdentifier = element.GetAttributeString("achievementidentifier", "");

            UnhideEntitySubCategories = element.GetAttributeStringArray("unhideentitysubcategories", new string[0]).ToList();

            Headers = new List<string>();
            Messages = new List<string>();
            AllowedConnectionTypes = new List<Pair<string, string>>();

            for (int i = 0; i < 100; i++)
            {
                string header = TextManager.Get("MissionHeader" + i + "." + TextIdentifier, true);
                string message = TextManager.Get("MissionMessage" + i + "." + TextIdentifier, true);
                if (!string.IsNullOrEmpty(message))
                {
                    Headers.Add(header);
                    Messages.Add(message);
                }
            }

            int messageIndex = 0;
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "message":
                        if (messageIndex > Headers.Count - 1)
                        {
                            Headers.Add(string.Empty);
                            Messages.Add(string.Empty);
                        }
                        Headers[messageIndex] = TextManager.Get("MissionHeader" + messageIndex + "." + TextIdentifier, true) ?? subElement.GetAttributeString("header", "");
                        Messages[messageIndex] = TextManager.Get("MissionMessage" + messageIndex + "." + TextIdentifier, true) ?? subElement.GetAttributeString("text", "");
                        messageIndex++;
                        break;
                    case "locationtype":
                    case "connectiontype":
                        if (subElement.Attribute("identifier") != null)
                        {
                            AllowedLocationTypes.Add(subElement.GetAttributeString("identifier", ""));
                        }
                        else
                        {
                            AllowedConnectionTypes.Add(new Pair<string, string>(
                                subElement.GetAttributeString("from", ""),
                                subElement.GetAttributeString("to", "")));
                        }
                        break;
                    case "locationtypechange":
                        LocationTypeChangeOnCompleted = new LocationTypeChange(subElement.GetAttributeString("from", ""), subElement, requireChangeMessages: false, defaultProbability: 1.0f);
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
                    case "metadata":
                        string identifier = subElement.GetAttributeString("identifier", string.Empty);
                        string stringValue = subElement.GetAttributeString("value", string.Empty);
                        if (!string.IsNullOrWhiteSpace(stringValue) && !string.IsNullOrWhiteSpace(identifier))
                        {
                            object value = SetDataAction.ConvertXMLValue(stringValue);
                            SetDataAction.OperationType operation = SetDataAction.OperationType.Set;

                            string operatingString = subElement.GetAttributeString("operation", string.Empty);
                            if (!string.IsNullOrWhiteSpace(operatingString))
                            {
                                operation = (SetDataAction.OperationType) Enum.Parse(typeof(SetDataAction.OperationType), operatingString);
                            }

                            DataRewards.Add(Tuple.Create(identifier, value, operation));
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

            if (CoOpMissionClasses.ContainsKey(Type))
            {
                constructor = CoOpMissionClasses[Type].GetConstructor(new[] { typeof(MissionPrefab), typeof(Location[]), typeof(Submarine) });
            }
            else if (PvPMissionClasses.ContainsKey(Type))
            {
                constructor = PvPMissionClasses[Type].GetConstructor(new[] { typeof(MissionPrefab), typeof(Location[]), typeof(Submarine) });
            }
            else
            {
                DebugConsole.ThrowError("Error in mission prefab \"" + Name + "\" - unsupported mission type \"" + Type.ToString() + "\"");
            }
            if (constructor == null)
            {
                DebugConsole.ThrowError($"Failed to find a constructor for the mission type \"{Type}\"!");
            }

            InitProjSpecific(element);
        }
        
        partial void InitProjSpecific(XElement element);

        public bool IsAllowed(Location from, Location to)
        {
            if (from == to)
            {
                return 
                    AllowedLocationTypes.Any(lt => lt.Equals("any", StringComparison.OrdinalIgnoreCase)) ||
                    AllowedLocationTypes.Any(lt => lt.Equals(from.Type.Identifier, StringComparison.OrdinalIgnoreCase));
            }

            foreach (Pair<string, string> allowedConnectionType in AllowedConnectionTypes)
            {
                if (allowedConnectionType.First.Equals("any", StringComparison.OrdinalIgnoreCase) ||
                    allowedConnectionType.First.Equals(from.Type.Identifier, StringComparison.OrdinalIgnoreCase))
                {
                    if (allowedConnectionType.Second.Equals("any", StringComparison.OrdinalIgnoreCase) ||
                        allowedConnectionType.Second.Equals(to.Type.Identifier, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            if (Type == MissionType.Beacon)
            {
                var connection = from.Connections.Find(c => c.Locations.Contains(from) && c.Locations.Contains(to));
                if (connection?.LevelData == null || !connection.LevelData.HasBeaconStation || connection.LevelData.IsBeaconActive) { return false; }
            }

            return false;
        }

        public Mission Instantiate(Location[] locations, Submarine sub)
        {
            return constructor?.Invoke(new object[] { this, locations, sub }) as Mission;
        }
    }
}
