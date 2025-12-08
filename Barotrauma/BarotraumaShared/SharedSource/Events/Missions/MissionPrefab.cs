using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma
{
    internal sealed partial class MissionPrefab : PrefabWithUintIdentifier, IImplementsVariants<MissionPrefab>
    {
        public static readonly PrefabCollection<MissionPrefab> Prefabs = [];

        /// <summary>
        /// The keys here are for backwards compatibility, tying the old mission types to the appropriate class. 
        /// Now the mission class is defined by the name of the mission element, and the type can be any arbitrary string.
        /// </summary>
        public static readonly Dictionary<Identifier, Type> CoOpMissionClasses = new Dictionary<Identifier, Type>()
        {
            { "Salvage".ToIdentifier(), typeof(SalvageMission) },
            { "Monster".ToIdentifier(), typeof(MonsterMission) },
            { "Cargo".ToIdentifier(), typeof(CargoMission) },
            { "Beacon".ToIdentifier(), typeof(BeaconMission) },
            { "Nest".ToIdentifier(), typeof(NestMission) },
            { "Mineral".ToIdentifier(), typeof(MineralMission) },
            { "AbandonedOutpost".ToIdentifier(), typeof(AbandonedOutpostMission) },
            { "Escort".ToIdentifier(), typeof(EscortMission) },
            { "Pirate".ToIdentifier(), typeof(PirateMission) },
            { "GoTo".ToIdentifier(), typeof(GoToMission) },
            { "ScanAlienRuins".ToIdentifier(), typeof(ScanMission) },
            { "EliminateTargets".ToIdentifier(), typeof(EliminateTargetsMission) },
            { "End".ToIdentifier(), typeof(EndMission) }
        };

        /// <summary>
        /// The keys here are for backwards compatibility, tying the old mission types to the appropriate class. 
        /// Now the mission class is defined by the name of the mission element, and the type can be any arbitrary string.
        /// </summary>
        public static readonly Dictionary<Identifier, Type> PvPMissionClasses = new Dictionary<Identifier, Type>()
        {
            { "Combat".ToIdentifier(), typeof(CombatMission) }
        };

        public static readonly HashSet<Identifier> HiddenMissionTypes = ["GoTo".ToIdentifier(), "End".ToIdentifier()];

        public class ReputationReward
        {
            public readonly Identifier FactionIdentifier;
            public readonly float Amount;
            public readonly float AmountForOpposingFaction;

            public ReputationReward(XElement element)
            {
                FactionIdentifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);
                Amount = element.GetAttributeFloat(nameof(Amount), 0.0f);
                AmountForOpposingFaction = element.GetAttributeFloat(nameof(AmountForOpposingFaction), 0.0f);
            }
        }

        private ConstructorInfo constructor;

        public Identifier Type { get; private set; }

        public Type MissionClass { get; private set; }

        public bool MultiplayerOnly { get; private set; }
        public bool SingleplayerOnly { get; private set; }

        public Identifier TextIdentifier { get; private set; }

        public ImmutableHashSet<Identifier> Tags { get; private set; }

        public LocalizedString Name { get; private set; }
        public LocalizedString Description { get; private set; }
        public LocalizedString SuccessMessage { get; private set; }
        public LocalizedString FailureMessage { get; private set; }
        public LocalizedString SonarLabel { get; private set; }
        public Identifier SonarIconIdentifier { get; private set; }

        public Identifier AchievementIdentifier { get; private set; }

        public ImmutableList<ReputationReward> ReputationRewards { get; private set; }

        public readonly List<(Identifier Identifier, object Value, SetDataAction.OperationType OperationType)> DataRewards = [];

        public int Commonness { get; private set; }
        /// <summary>
        /// Displayed difficulty (indicator)
        /// </summary>
        public int? Difficulty { get; private set; }
        public const int MinDifficulty = 1, MaxDifficulty = 4;
        /// <summary>
        /// The actual minimum difficulty of the level allowed for this mission to trigger.
        /// </summary>
        public int MinLevelDifficulty { get; private set; } = 0;
        /// <summary>
        /// The actual maximum difficulty of the level allowed for this mission to trigger.
        /// </summary>
        public int MaxLevelDifficulty { get; private set; } = 100;

        public int Reward { get; private set; }

        public float ExperienceMultiplier { get; private set; }

        // The titles and bodies of the popup messages during the mission, shown when the state of the mission changes. The order matters.
        public ImmutableArray<LocalizedString> Headers { get; private set; }
        public ImmutableArray<LocalizedString> Messages { get; private set; }

        public bool AllowRetry { get; private set; }

        public bool ShowSonarLabels { get; private set; }

        public bool ShowInMenus { get; private set; }
        public bool ShowStartMessage { get; private set; }

        /// <summary>
        /// Makes the mission not count for the maximum mission limit, and forces it to always be selected when it's available in a level.
        /// </summary>
        public bool IsSideObjective { get; private set; }

        public bool AllowOtherMissionsInLevel { get; private set; }

        public bool RequireWreck { get; private set; }
        public bool RequireRuin { get; private set; }
        public bool RequireBeaconStation { get; private set; }
        public bool RequireThalamusWreck { get; private set; }
        public bool SpawnBeaconStationInMiddle { get; private set; }

        public bool AllowOutpostNPCs { get; private set; }

        public Identifier ForceOutpostGenerationParameters { get; private set; }

        public RespawnMode? ForceRespawnMode { get; private set; }

        /// <summary>
        /// If set, the players can choose which outpost is used for the mission (selected from the outposts that have this tag). Only works in multiplayer.
        /// </summary>
        public Identifier AllowOutpostSelectionFromTag { get; private set; }

        public bool LoadSubmarines { get; private set; } = true;

        /// <summary>
        /// If enabled, locations this mission takes place in cannot change their type
        /// </summary>
        public bool BlockLocationTypeChanges { get; private set; }

        public bool ShowProgressBar { get; private set; }
        public bool ShowProgressInNumbers { get; private set; }
        public int MaxProgressState { get; private set; }
        public LocalizedString ProgressBarLabel { get; private set; }

        /// <summary>
        /// The mission can only be received when travelling from a location of the first type to a location of the second type
        /// </summary>
        public List<(Identifier from, Identifier to)> AllowedConnectionTypes { get; private set; }

        /// <summary>
        /// The mission can only be received in these location types
        /// </summary>
        public readonly List<Identifier> AllowedLocationTypes = [];

        /// <summary>
        /// The mission can only happen in locations owned by this faction. In the mission mode, the location is forced to be owned by this faction.
        /// </summary>
        public Identifier RequiredLocationFaction { get; private set; }

        /// <summary>
        /// Show entities belonging to these sub categories when the mission starts
        /// </summary>
        public List<string> UnhideEntitySubCategories { get; private set; }

        public class TriggerEvent
        {
            [Serialize("", IsPropertySaveable.Yes)]
            public Identifier EventIdentifier { get; private set; }

            [Serialize("", IsPropertySaveable.Yes)]
            public Identifier EventTag { get; private set; }

            [Serialize(0, IsPropertySaveable.Yes)]
            public int State { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.Yes)]
            public float Delay { get; private set; }

            [Serialize(false, IsPropertySaveable.Yes)]
            public bool CampaignOnly { get; private set; }

            public TriggerEvent(XElement element)
            {
                SerializableProperty.DeserializeProperties(this, element);
            }
        }

        public readonly List<TriggerEvent> TriggerEvents = [];

        public LocationTypeChange LocationTypeChangeOnCompleted;

        private readonly ContentXElement originalElement;
        public ContentXElement ConfigElement { get; private set; }

        public Identifier VariantOf { get; }
        public MissionPrefab ParentPrefab { get; set; }

        public MissionPrefab(ContentXElement element, MissionsFile file) : base(file, element.GetAttributeIdentifier("identifier", ""))
        {
            ConfigElement = originalElement = element;

            VariantOf = element.VariantOf();
            if (!VariantOf.IsEmpty) { return; } // Don't read the XML until the PrefabCollection loads the parent.
            ParseConfigElement();
        }

        public void InheritFrom(MissionPrefab parent)
        {
            ConfigElement = originalElement.CreateVariantXML(parent.ConfigElement);
            ParseConfigElement(parent);
        }

        private void ParseConfigElement(MissionPrefab variantOf = null)
        {
            TextIdentifier = ConfigElement.GetAttributeIdentifier("textidentifier", Identifier);

            Tags = [.. ConfigElement.GetAttributeIdentifierArray("tags", [])];

            Name = GetText(ConfigElement.GetAttributeString("name", ""), "MissionName");
            Description = GetText(ConfigElement.GetAttributeString("description", ""), "MissionDescription");

            LocalizedString GetText(string textTag, string textTagPrefix)
            {
                if (string.IsNullOrEmpty(textTag))
                {
                    return TextManager.Get($"{textTagPrefix}.{TextIdentifier}");
                }
                else
                {
                    return TextManager.Get(textTag) // Prefer finding a text based on the specific text tag defined in the mission config.
                           .Fallback(TextManager.Get($"{textTagPrefix}.{TextIdentifier}")) // 2nd option: the "default" format (MissionName.SomeMission).
                           .Fallback(textTag); // Last option: Use the text in the xml as-is with no localization.
                }
            }

            Reward = ConfigElement.GetAttributeInt(nameof(Reward), 1);
            ExperienceMultiplier = ConfigElement.GetAttributeFloat(nameof(ExperienceMultiplier), 1f);
            AllowRetry = ConfigElement.GetAttributeBool(nameof(AllowRetry), false);
            ShowSonarLabels = ConfigElement.GetAttributeBool(nameof(ShowSonarLabels), true);
            ShowInMenus = ConfigElement.GetAttributeBool(nameof(ShowInMenus), true);
            ShowStartMessage = ConfigElement.GetAttributeBool(nameof(ShowStartMessage), true);
            IsSideObjective = ConfigElement.GetAttributeBool("sideobjective", false);

            RequireWreck = ConfigElement.GetAttributeBool(nameof(RequireWreck), false);
            RequireThalamusWreck = ConfigElement.GetAttributeBool(nameof(RequireThalamusWreck), false);
            RequireRuin = ConfigElement.GetAttributeBool(nameof(RequireRuin), false);
            RequireBeaconStation = ConfigElement.GetAttributeBool(nameof(RequireBeaconStation), false);
            SpawnBeaconStationInMiddle = ConfigElement.GetAttributeBool(nameof(SpawnBeaconStationInMiddle), false);
            RequireWreck |= RequireThalamusWreck;

            LoadSubmarines = ConfigElement.GetAttributeBool(nameof(LoadSubmarines), true);

            BlockLocationTypeChanges = ConfigElement.GetAttributeBool(nameof(BlockLocationTypeChanges), false);
            RequiredLocationFaction = ConfigElement.GetAttributeIdentifier(nameof(RequiredLocationFaction), Identifier.Empty);
            Commonness = ConfigElement.GetAttributeInt(nameof(Commonness), 1);
            AllowOtherMissionsInLevel = ConfigElement.GetAttributeBool(nameof(AllowOtherMissionsInLevel), true);

            if (ConfigElement.GetAttribute("difficulty") != null)
            {
                int difficulty = ConfigElement.GetAttributeInt(nameof(Difficulty), MinDifficulty);
                Difficulty = Math.Clamp(difficulty, MinDifficulty, MaxDifficulty);
            }
            MinLevelDifficulty = ConfigElement.GetAttributeInt(nameof(MinLevelDifficulty), MinLevelDifficulty);
            MaxLevelDifficulty = ConfigElement.GetAttributeInt(nameof(MaxLevelDifficulty), MaxLevelDifficulty);
            MinLevelDifficulty = Math.Clamp(MinLevelDifficulty, 0, Math.Min(MaxLevelDifficulty, 100));
            MaxLevelDifficulty = Math.Clamp(MaxLevelDifficulty, Math.Max(MinLevelDifficulty, 0), 100);

            AllowOutpostNPCs = ConfigElement.GetAttributeBool(nameof(AllowOutpostNPCs), true);
            ForceOutpostGenerationParameters = ConfigElement.GetAttributeIdentifier(nameof(ForceOutpostGenerationParameters), Identifier.Empty);
            AllowOutpostSelectionFromTag = ConfigElement.GetAttributeIdentifier(nameof(AllowOutpostSelectionFromTag), Identifier.Empty);

            if (ConfigElement.GetAttribute(nameof(ForceRespawnMode)) != null)
            {
                ForceRespawnMode = ConfigElement.GetAttributeEnum(nameof(ForceRespawnMode), RespawnMode.MidRound);
            }

            ShowProgressBar = ConfigElement.GetAttributeBool(nameof(ShowProgressBar), false);
            ShowProgressInNumbers = ConfigElement.GetAttributeBool(nameof(ShowProgressInNumbers), false);
            MaxProgressState = ConfigElement.GetAttributeInt(nameof(MaxProgressState), 1);
            string progressBarLabel = ConfigElement.GetAttributeString(nameof(ProgressBarLabel), "");
            ProgressBarLabel = TextManager.Get(progressBarLabel).Fallback(progressBarLabel);

            string successMessageTag = ConfigElement.GetAttributeString("successmessage", "");
            SuccessMessage = TextManager.Get($"MissionSuccess.{TextIdentifier}");
            if (!string.IsNullOrEmpty(successMessageTag))
            {
                SuccessMessage = SuccessMessage
                                 .Fallback(TextManager.Get(successMessageTag))
                                 .Fallback(successMessageTag);
            }
            SuccessMessage = SuccessMessage.Fallback(TextManager.Get("missioncompleted"));

            string failureMessageTag = ConfigElement.GetAttributeString("failuremessage", "");
            FailureMessage = TextManager.Get($"MissionFailure.{TextIdentifier}");
            if (!string.IsNullOrEmpty(failureMessageTag))
            {
                FailureMessage = FailureMessage
                                 .Fallback(TextManager.Get(failureMessageTag))
                                 .Fallback(failureMessageTag);
            }
            FailureMessage = FailureMessage.Fallback(TextManager.Get("missionfailed"));

            string sonarLabelTag = ConfigElement.GetAttributeString("sonarlabel", "");
            SonarLabel = TextManager.Get($"MissionSonarLabel.{sonarLabelTag}")
                         .Fallback(TextManager.Get(sonarLabelTag))
                         .Fallback(TextManager.Get($"MissionSonarLabel.{TextIdentifier}"));
            if (!string.IsNullOrEmpty(sonarLabelTag))
            {
                SonarLabel = SonarLabel.Fallback(sonarLabelTag);
            }

            SonarIconIdentifier = ConfigElement.GetAttributeIdentifier("sonaricon", "");

            MultiplayerOnly = ConfigElement.GetAttributeBool("multiplayeronly", false);
            SingleplayerOnly = ConfigElement.GetAttributeBool("singleplayeronly", false);

            AchievementIdentifier = ConfigElement.GetAttributeIdentifier("achievementidentifier", "");

            UnhideEntitySubCategories = [.. ConfigElement.GetAttributeStringArray("unhideentitysubcategories", [])];

            List<LocalizedString> headers = [];
            List<LocalizedString> messages = [];
            AllowedConnectionTypes = [];

            for (int i = 0; i < 100; i++)
            {
                LocalizedString header = TextManager.Get($"MissionHeader{i}.{TextIdentifier}");
                LocalizedString message = TextManager.Get($"MissionMessage{i}.{TextIdentifier}");
                if (!message.IsNullOrEmpty())
                {
                    headers.Add(header);
                    messages.Add(message);
                }
            }

            List<ReputationReward> reputationRewards = [];
            int messageIndex = 0;
            foreach (ContentXElement subElement in ConfigElement.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "message":
                        if (messageIndex >= headers.Count)
                        {
                            headers.Add(string.Empty);
                            messages.Add(string.Empty);
                        }
                        headers[messageIndex] = TextManager.Get($"MissionHeader{messageIndex}.{TextIdentifier}")
                                                .Fallback(TextManager.Get(subElement.GetAttributeString("header", "")))
                                                .Fallback(subElement.GetAttributeString("header", ""));
                        messages[messageIndex] = TextManager.Get($"MissionMessage{messageIndex}.{TextIdentifier}")
                                                 .Fallback(TextManager.Get(subElement.GetAttributeString("text", "")))
                                                 .Fallback(subElement.GetAttributeString("text", ""));
                        messageIndex++;
                        break;
                    case "locationtype":
                    case "connectiontype":
                        if (subElement.GetAttribute("identifier") != null)
                        {
                            AllowedLocationTypes.Add(subElement.GetAttributeIdentifier("identifier", ""));
                        }
                        else
                        {
                            AllowedConnectionTypes.Add((subElement.GetAttributeIdentifier("from", ""), subElement.GetAttributeIdentifier("to", "")));
                        }
                        break;
                    case "locationtypechange":
                        LocationTypeChangeOnCompleted = new LocationTypeChange(subElement.GetAttributeIdentifier("from", ""), subElement, requireChangeMessages: false, defaultProbability: 1.0f);
                        break;
                    case "reputation":
                    case "reputationreward":
                        reputationRewards.Add(new ReputationReward(subElement));
                        break;
                    case "metadata":
                        Identifier identifier = subElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                        string stringValue = subElement.GetAttributeString("value", string.Empty);
                        if (!string.IsNullOrWhiteSpace(stringValue) && !identifier.IsEmpty)
                        {
                            object value = SetDataAction.ConvertXMLValue(stringValue);
                            SetDataAction.OperationType operation = SetDataAction.OperationType.Set;

                            string operatingString = subElement.GetAttributeString("operation", string.Empty);
                            if (!string.IsNullOrWhiteSpace(operatingString))
                            {
                                operation = (SetDataAction.OperationType)Enum.Parse(typeof(SetDataAction.OperationType), operatingString);
                            }

                            DataRewards.Add((identifier, value, operation));
                        }
                        break;
                    case "triggerevent":
                        TriggerEvents.Add(new TriggerEvent(subElement));
                        break;
                }
            }
            Headers = [.. headers];
            Messages = [.. messages];
            ReputationRewards = [.. reputationRewards];

            MissionClass = FindMissionClass(ConfigElement);
            Type = ConfigElement.GetAttributeIdentifier(nameof(Type), Identifier.Empty);

#if DEBUG
            if (MissionClass == typeof(MonsterMission) && SonarLabel.IsNullOrEmpty())
            {
                DebugConsole.AddWarning($"Potential error in mission prefab \"{Identifier}\" - sonar label not set.");
            }
#endif

            if (!LoadSubmarines && MissionClass != typeof(CombatMission))
            {
                DebugConsole.AddWarning($"Potential error in mission {Identifier}: Disabling submarines is only intended for combat missions taking place in an outpost, and may lead to issues in other types of missions.",
                    contentPackage: ConfigElement.ContentPackage);
            }

            constructor = FindMissionConstructor(ConfigElement, MissionClass);
            if (constructor == null)
            {
                DebugConsole.ThrowError($"Failed to find a constructor for the mission type \"{Type}\"!",
                    contentPackage: ConfigElement.ContentPackage);
            }

#if CLIENT
            ParseConfigElementClient(ConfigElement, variantOf);
#endif
        }

        private Type FindMissionClass(ContentXElement element)
        {
            Type type;
            Identifier typeName = element.NameAsIdentifier();
            type = TryGetClass(typeName.RemoveFromEnd("Mission"));

            if (type == null)
            {
                //backwards compatibility: the actual mission class used to be defined by the "type" attribute,
                //Now the mission class is defined by the name of the mission element, and the type can be any arbitrary string,
                //but if we failed to find the class based on the name, let's try the type attribute.
                Identifier typeNameLegacy = (element.GetAttributeIdentifier("type", Identifier.Empty)).ToIdentifier();
                if (typeNameLegacy == "OutpostDestroy" || typeNameLegacy == "OutpostRescue")
                {
                    typeNameLegacy = "AbandonedOutpost".ToIdentifier();
                }
                else if (typeNameLegacy == "clearalienruins")
                {
                    typeNameLegacy = "EliminateTargets".ToIdentifier();
                }
                type = TryGetClass(typeNameLegacy) ?? TryGetClass(typeNameLegacy.AppendIfMissing("Mission"));
                if (type == null)
                {
                    DebugConsole.ThrowError($"Failed to find the mission type \"{typeNameLegacy}\" for the mission {Identifier}.",
                        contentPackage: element.ContentPackage);
                    return null;
                }
            }

            static Type TryGetClass(Identifier typeName)
            {
                if (CoOpMissionClasses.TryGetValue(typeName, out Type coOpMissionClass))
                {
                    return coOpMissionClass;
                }
                else if (PvPMissionClasses.TryGetValue(typeName, out Type pvpMissionClass))
                {
                    return pvpMissionClass;
                }
                return null;
            }

            return type;
        }

        private ConstructorInfo FindMissionConstructor(ContentXElement element, Type missionClass)
        {
            ConstructorInfo constructor;
            if (missionClass == null) { return null; }
            if (missionClass != typeof(Mission) && !missionClass.IsSubclassOf(typeof(Mission))) { return null; }
            constructor = missionClass.GetConstructor(new Type[] { typeof(MissionPrefab), typeof(Location[]), typeof(Submarine) });
            if (constructor == null)
            {
                DebugConsole.ThrowError(
                    $"Could not find the constructor of the mission type \"{missionClass}\" for the mission {Identifier}",
                    contentPackage: element.ContentPackage);
                return null;
            }
            return constructor;
        }

        public bool IsAllowed(Location from, Location to)
        {
            if (from == to)
            {
                if (!RequiredLocationFaction.IsEmpty && from.Faction?.Prefab.Identifier != RequiredLocationFaction)
                {
                    return false;
                }
                return
                    AllowedLocationTypes.Any(lt => lt == "any") ||
                    AllowedLocationTypes.Any(lt => lt == Barotrauma.Tags.AnyOutpost && from.HasOutpost() && from.Type.IsAnyOutpost) ||
                    AllowedLocationTypes.Any(lt => lt == from.Type.Identifier);
            }

            foreach (var (fromType, toType) in AllowedConnectionTypes)
            {
                if (fromType == "any" ||
                    fromType == from.Type.Identifier ||
                    (fromType == Barotrauma.Tags.AnyOutpost && from.HasOutpost() && from.Type.IsAnyOutpost && from.Type.Identifier != "abandoned"))
                {
                    if (toType == "any" ||
                        toType == to.Type.Identifier ||
                        (toType == Barotrauma.Tags.AnyOutpost && to.HasOutpost() && to.Type.IsAnyOutpost && to.Type.Identifier != "abandoned"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        
        /// <summary>
        /// Inclusive (matching the min an max values is accepted).
        /// </summary>
        public bool IsAllowedDifficulty(float difficulty) => difficulty >= MinLevelDifficulty && difficulty <= MaxLevelDifficulty;

        public Mission Instantiate(Location[] locations, Submarine sub)
        {
            return constructor?.Invoke(new object[] { this, locations, sub }) as Mission;
        }

        partial void DisposeProjectSpecific();
        public override void Dispose()
        {
            DisposeProjectSpecific();
        }

        /// <summary>
        /// Returns all mission types that can be selected e.g. in the server lobby, excluding any special, hidden ones like EndMission 
        /// (the mission at the end of the campaign)
        /// </summary>
        public static IEnumerable<Identifier> GetAllMultiplayerSelectableMissionTypes()
        {
            List<Identifier> missionTypes = new List<Identifier>();
            foreach (var missionPrefab in Prefabs)
            {
                if (missionPrefab.Commonness <= 0.0f) { continue; }
                if (missionPrefab.SingleplayerOnly) { continue; }
                if (HiddenMissionTypes.Contains(missionPrefab.Type))
                {
                    continue;
                }
                if (!missionTypes.Contains(missionPrefab.Type)) 
                {
                    missionTypes.Add(missionPrefab.Type);
                }
            }
            return missionTypes.OrderBy(t => t.Value);
        }
    }
}
