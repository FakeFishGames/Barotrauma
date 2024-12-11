using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class MissionPrefab : PrefabWithUintIdentifier
    {
        public static readonly PrefabCollection<MissionPrefab> Prefabs = new PrefabCollection<MissionPrefab>();

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

        public static readonly HashSet<Identifier> HiddenMissionTypes = new HashSet<Identifier>() { "GoTo".ToIdentifier(), "End".ToIdentifier() };

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

        private readonly ConstructorInfo constructor;

        public readonly Identifier Type;

        public readonly Type MissionClass;

        public readonly bool MultiplayerOnly, SingleplayerOnly;

        public readonly Identifier TextIdentifier;

        public readonly ImmutableHashSet<Identifier> Tags;

        public readonly LocalizedString Name;
        public readonly LocalizedString Description;
        public readonly LocalizedString SuccessMessage;
        public readonly LocalizedString FailureMessage;
        public readonly LocalizedString SonarLabel;
        public readonly Identifier SonarIconIdentifier;

        public readonly Identifier AchievementIdentifier;

        public readonly ImmutableList<ReputationReward> ReputationRewards;

        public readonly List<(Identifier Identifier, object Value, SetDataAction.OperationType OperationType)>
            DataRewards = new List<(Identifier Identifier, object Value, SetDataAction.OperationType OperationType)>();

        public readonly int Commonness;
        /// <summary>
        /// Displayed difficulty (indicator)
        /// </summary>
        public readonly int? Difficulty;
        public const int MinDifficulty = 1, MaxDifficulty = 4;
        /// <summary>
        /// The actual minimum difficulty of the level allowed for this mission to trigger.
        /// </summary>
        public readonly int MinLevelDifficulty = 0;
        /// <summary>
        /// The actual maximum difficulty of the level allowed for this mission to trigger.
        /// </summary>
        public readonly int MaxLevelDifficulty = 100;

        public readonly int Reward;

        public readonly float ExperienceMultiplier;

        // The titles and bodies of the popup messages during the mission, shown when the state of the mission changes. The order matters.
        public readonly ImmutableArray<LocalizedString> Headers;
        public readonly ImmutableArray<LocalizedString> Messages;

        public readonly bool AllowRetry;

        public readonly bool ShowInMenus, ShowStartMessage;

        public readonly bool IsSideObjective;

        public readonly bool AllowOtherMissionsInLevel;

        public readonly bool RequireWreck, RequireRuin, RequireBeaconStation, RequireThalamusWreck;
        public readonly bool SpawnBeaconStationInMiddle;

        public readonly bool AllowOutpostNPCs;

        public readonly Identifier ForceOutpostGenerationParameters;

        public readonly RespawnMode? ForceRespawnMode;

        /// <summary>
        /// If set, the players can choose which outpost is used for the mission (selected from the outposts that have this tag). Only works in multiplayer.
        /// </summary>
        public readonly Identifier AllowOutpostSelectionFromTag;

        public readonly bool LoadSubmarines = true;

        /// <summary>
        /// If enabled, locations this mission takes place in cannot change their type
        /// </summary>
        public readonly bool BlockLocationTypeChanges;

        public readonly bool ShowProgressBar;
        public readonly bool ShowProgressInNumbers;
        public readonly int MaxProgressState;
        public readonly LocalizedString ProgressBarLabel;

        /// <summary>
        /// The mission can only be received when travelling from a location of the first type to a location of the second type
        /// </summary>
        public readonly List<(Identifier from, Identifier to)> AllowedConnectionTypes;

        /// <summary>
        /// The mission can only be received in these location types
        /// </summary>
        public readonly List<Identifier> AllowedLocationTypes = new List<Identifier>();

        /// <summary>
        /// The mission can only happen in locations owned by this faction. In the mission mode, the location is forced to be owned by this faction.
        /// </summary>
        public readonly Identifier RequiredLocationFaction;

        /// <summary>
        /// Show entities belonging to these sub categories when the mission starts
        /// </summary>
        public readonly List<string> UnhideEntitySubCategories = new List<string>();

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

        public readonly List<TriggerEvent> TriggerEvents = new List<TriggerEvent>();

        public LocationTypeChange LocationTypeChangeOnCompleted;

        public readonly ContentXElement ConfigElement;

        public MissionPrefab(ContentXElement element, MissionsFile file) : base(file, element.GetAttributeIdentifier("identifier", ""))
        {
            ConfigElement = element;

            TextIdentifier = element.GetAttributeIdentifier("textidentifier", Identifier);

            Tags = element.GetAttributeIdentifierArray("tags", Array.Empty<Identifier>()).ToImmutableHashSet();

            Name = GetText(element.GetAttributeString("name", ""), "MissionName");
            Description = GetText(element.GetAttributeString("description", ""), "MissionDescription");

            LocalizedString GetText(string textTag, string textTagPrefix)
            {
                if (string.IsNullOrEmpty(textTag))
                {
                    return TextManager.Get($"{textTagPrefix}.{TextIdentifier}");
                }
                else
                {
                    return
                        //prefer finding a text based on the specific text tag defined in the mission config
                        TextManager.Get(textTag)
                        //2nd option: the "default" format (MissionName.SomeMission)
                        .Fallback(TextManager.Get($"{textTagPrefix}.{TextIdentifier}"))
                        //last option: use the text in the xml as-is with no localization
                        .Fallback(textTag);
                }
            }

            Reward      = element.GetAttributeInt("reward", 1);
            ExperienceMultiplier = element.GetAttributeFloat("experiencemultiplier", 1.0f);
            AllowRetry  = element.GetAttributeBool("allowretry", false);
            ShowInMenus = element.GetAttributeBool("showinmenus", true);
            ShowStartMessage = element.GetAttributeBool("showstartmessage", true);
            IsSideObjective = element.GetAttributeBool("sideobjective", false);

            RequireWreck = element.GetAttributeBool(nameof(RequireWreck), false);
            RequireThalamusWreck = element.GetAttributeBool(nameof(RequireThalamusWreck), false);
            RequireRuin = element.GetAttributeBool(nameof(RequireRuin), false);
            RequireBeaconStation = element.GetAttributeBool(nameof(RequireBeaconStation), false);
            SpawnBeaconStationInMiddle = element.GetAttributeBool(nameof(SpawnBeaconStationInMiddle), false);
            if (RequireThalamusWreck) { RequireWreck = true; }

            LoadSubmarines = element.GetAttributeBool(nameof(LoadSubmarines), true);

            BlockLocationTypeChanges = element.GetAttributeBool(nameof(BlockLocationTypeChanges), false);
            RequiredLocationFaction = element.GetAttributeIdentifier(nameof(RequiredLocationFaction), Identifier.Empty);
            Commonness  = element.GetAttributeInt("commonness", 1);
            AllowOtherMissionsInLevel = element.GetAttributeBool("allowothermissionsinlevel", true);
            if (element.GetAttribute("difficulty") != null)
            {
                int difficulty = element.GetAttributeInt("difficulty", MinDifficulty);
                Difficulty = Math.Clamp(difficulty, MinDifficulty, MaxDifficulty);
            }
            MinLevelDifficulty = element.GetAttributeInt(nameof(MinLevelDifficulty), MinLevelDifficulty);
            MaxLevelDifficulty = element.GetAttributeInt(nameof(MaxLevelDifficulty), MaxLevelDifficulty);
            MinLevelDifficulty = Math.Clamp(MinLevelDifficulty, 0, Math.Min(MaxLevelDifficulty, 100));
            MaxLevelDifficulty = Math.Clamp(MaxLevelDifficulty, Math.Max(MinLevelDifficulty, 0), 100);

            AllowOutpostNPCs = element.GetAttributeBool(nameof(AllowOutpostNPCs), true);
            ForceOutpostGenerationParameters = element.GetAttributeIdentifier(nameof(ForceOutpostGenerationParameters), Identifier.Empty);
            AllowOutpostSelectionFromTag = element.GetAttributeIdentifier(nameof(AllowOutpostSelectionFromTag), Identifier.Empty);

            if (element.GetAttribute(nameof(ForceRespawnMode)) != null)
            {
                ForceRespawnMode = element.GetAttributeEnum(nameof(ForceRespawnMode), RespawnMode.MidRound);
            }

            ShowProgressBar = element.GetAttributeBool(nameof(ShowProgressBar), false);
            ShowProgressInNumbers = element.GetAttributeBool(nameof(ShowProgressInNumbers), false);
            MaxProgressState = element.GetAttributeInt(nameof(MaxProgressState), 1);
            string progressBarLabel = element.GetAttributeString(nameof(ProgressBarLabel), "");
            ProgressBarLabel = TextManager.Get(progressBarLabel).Fallback(progressBarLabel);

            string successMessageTag = element.GetAttributeString("successmessage", "");
            SuccessMessage = TextManager.Get($"MissionSuccess.{TextIdentifier}");
            if (!string.IsNullOrEmpty(successMessageTag))
            {
                SuccessMessage = SuccessMessage
                    .Fallback(TextManager.Get(successMessageTag))
                    .Fallback(successMessageTag);
            }
            SuccessMessage = SuccessMessage.Fallback(TextManager.Get("missioncompleted"));

            string failureMessageTag = element.GetAttributeString("failuremessage", "");
            FailureMessage = TextManager.Get($"MissionFailure.{TextIdentifier}");
            if (!string.IsNullOrEmpty(failureMessageTag))
            {
                FailureMessage = FailureMessage
                    .Fallback(TextManager.Get(failureMessageTag))
                    .Fallback(failureMessageTag);
            }
            FailureMessage = FailureMessage.Fallback(TextManager.Get("missionfailed"));

            string sonarLabelTag = element.GetAttributeString("sonarlabel", "");
            SonarLabel =
                TextManager.Get($"MissionSonarLabel.{sonarLabelTag}")
                .Fallback(TextManager.Get(sonarLabelTag))
                .Fallback(TextManager.Get($"MissionSonarLabel.{TextIdentifier}"));
            if (!string.IsNullOrEmpty(sonarLabelTag))
            {
                SonarLabel = SonarLabel.Fallback(sonarLabelTag);
            }

            SonarIconIdentifier = element.GetAttributeIdentifier("sonaricon", "");

            MultiplayerOnly     = element.GetAttributeBool("multiplayeronly", false);
            SingleplayerOnly    = element.GetAttributeBool("singleplayeronly", false);

            AchievementIdentifier = element.GetAttributeIdentifier("achievementidentifier", "");

            UnhideEntitySubCategories = element.GetAttributeStringArray("unhideentitysubcategories", Array.Empty<string>()).ToList();

            var headers = new List<LocalizedString>();
            var messages = new List<LocalizedString>();
            AllowedConnectionTypes = new List<(Identifier from, Identifier to)>();

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

            List<ReputationReward> reputationRewards = new List<ReputationReward>();
            int messageIndex = 0;
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "message":
                        if (messageIndex > headers.Count - 1)
                        {
                            headers.Add(string.Empty);
                            messages.Add(string.Empty);
                        }
                        headers[messageIndex] = 
                            TextManager.Get($"MissionHeader{messageIndex}.{TextIdentifier}")
                            .Fallback(TextManager.Get(subElement.GetAttributeString("header", "")))
                            .Fallback(subElement.GetAttributeString("header", ""));
                        messages[messageIndex] = 
                            TextManager.Get($"MissionMessage{messageIndex}.{TextIdentifier}")
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
                            AllowedConnectionTypes.Add((
                                subElement.GetAttributeIdentifier("from", ""),
                                subElement.GetAttributeIdentifier("to", "")));
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
                                operation = (SetDataAction.OperationType) Enum.Parse(typeof(SetDataAction.OperationType), operatingString);
                            }

                            DataRewards.Add((identifier, value, operation));
                        }
                        break;
                    case "triggerevent":
                        TriggerEvents.Add(new TriggerEvent(subElement));
                        break;
                }
            }
            Headers = headers.ToImmutableArray();
            Messages = messages.ToImmutableArray();
            ReputationRewards = reputationRewards.ToImmutableList();

            MissionClass = FindMissionClass(element);
            Type = element.GetAttributeIdentifier(nameof(Type), Identifier.Empty);
         
#if DEBUG
            if (MissionClass == typeof(MonsterMission) && SonarLabel.IsNullOrEmpty())
            {
                DebugConsole.AddWarning($"Potential error in mission prefab \"{Identifier}\" - sonar label not set.");
            }
#endif

            if (!LoadSubmarines && MissionClass != typeof(CombatMission))
            {
                DebugConsole.AddWarning($"Potential error in mission {Identifier}: Disabling submarines is only intended for combat missions taking place in an outpost, and may lead to issues in other types of missions.",
                    contentPackage: element.ContentPackage);
            }

            constructor = FindMissionConstructor(element, MissionClass);
            if (constructor == null)
            {
                DebugConsole.ThrowError($"Failed to find a constructor for the mission type \"{Type}\"!",
                    contentPackage: element.ContentPackage);
            }

            InitProjSpecific(element);
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
        
        partial void InitProjSpecific(ContentXElement element);

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
