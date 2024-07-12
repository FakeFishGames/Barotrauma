using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

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
        AbandonedOutpost = 0x80,
        Escort = 0x100,
        Pirate = 0x200,
        GoTo = 0x400,
        ScanAlienRuins = 0x800,
        EliminateTargets = 0x1000,
        End = 0x2000,
        All = Salvage | Monster | Cargo | Beacon | Nest | Mineral | Combat | AbandonedOutpost | Escort | Pirate | GoTo | ScanAlienRuins | EliminateTargets | End
    }

    partial class MissionPrefab : PrefabWithUintIdentifier
    {
        public static readonly PrefabCollection<MissionPrefab> Prefabs = new PrefabCollection<MissionPrefab>();

        public static readonly Dictionary<MissionType, Type> CoOpMissionClasses = new Dictionary<MissionType, Type>()
        {
            { MissionType.Salvage, typeof(SalvageMission) },
            { MissionType.Monster, typeof(MonsterMission) },
            { MissionType.Cargo, typeof(CargoMission) },
            { MissionType.Beacon, typeof(BeaconMission) },
            { MissionType.Nest, typeof(NestMission) },
            { MissionType.Mineral, typeof(MineralMission) },
            { MissionType.AbandonedOutpost, typeof(AbandonedOutpostMission) },
            { MissionType.Escort, typeof(EscortMission) },
            { MissionType.Pirate, typeof(PirateMission) },
            { MissionType.GoTo, typeof(GoToMission) },
            { MissionType.ScanAlienRuins, typeof(ScanMission) },
            { MissionType.EliminateTargets, typeof(EliminateTargetsMission) },
            { MissionType.End, typeof(EndMission) }
        };
        public static readonly Dictionary<MissionType, Type> PvPMissionClasses = new Dictionary<MissionType, Type>()
        {
            { MissionType.Combat, typeof(CombatMission) }
        };

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

        public static readonly HashSet<MissionType> HiddenMissionClasses = new HashSet<MissionType>() { MissionType.GoTo, MissionType.End };

        private readonly ConstructorInfo constructor;

        public readonly MissionType Type;

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

        // The titles and bodies of the popup messages during the mission, shown when the state of the mission changes. The order matters.
        public readonly ImmutableArray<LocalizedString> Headers;
        public readonly ImmutableArray<LocalizedString> Messages;

        public readonly bool AllowRetry;

        public readonly bool ShowInMenus, ShowStartMessage;

        public readonly bool IsSideObjective;

        public readonly bool AllowOtherMissionsInLevel;

        public readonly bool RequireWreck, RequireRuin, RequireThalamusWreck;

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
            public string EventIdentifier { get; private set; }

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
            AllowRetry  = element.GetAttributeBool("allowretry", false);
            ShowInMenus = element.GetAttributeBool("showinmenus", true);
            ShowStartMessage = element.GetAttributeBool("showstartmessage", true);
            IsSideObjective = element.GetAttributeBool("sideobjective", false);
            RequireWreck = element.GetAttributeBool("requirewreck", false);
            RequireRuin = element.GetAttributeBool("requireruin", false);
            RequireThalamusWreck = element.GetAttributeBool("requirethalamuswreck", false);

            if (RequireThalamusWreck) { RequireWreck = true; }

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

            Identifier missionTypeName = element.GetAttributeIdentifier("type", Identifier.Empty);
            //backwards compatibility

            if (missionTypeName == "outpostdestroy" || missionTypeName == "outpostrescue")
            {
                missionTypeName = nameof(MissionType.AbandonedOutpost).ToIdentifier();
            }
            else if (missionTypeName == "clearalienruins")
            {
                missionTypeName = nameof(MissionType.EliminateTargets).ToIdentifier();
            }
            
            if (!Enum.TryParse(missionTypeName.Value, true, out Type))
            {
                DebugConsole.ThrowErrorLocalized("Error in mission prefab \"" + Name + "\" - \"" + missionTypeName + "\" is not a valid mission type.");
                return;
            }
            if (Type == MissionType.None)
            {
                DebugConsole.ThrowErrorLocalized("Error in mission prefab \"" + Name + "\" - mission type cannot be none.");
                return;
            }
#if DEBUG
            if (Type == MissionType.Monster && SonarLabel.IsNullOrEmpty())
            {
                DebugConsole.AddWarning($"Potential error in mission prefab \"{Identifier}\" - sonar label not set.");
            }
#endif

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
                DebugConsole.ThrowErrorLocalized("Error in mission prefab \"" + Name + "\" - unsupported mission type \"" + Type.ToString() + "\"");
            }
            if (constructor == null)
            {
                DebugConsole.ThrowError($"Failed to find a constructor for the mission type \"{Type}\"!",
                    contentPackage: element.ContentPackage);
            }

            InitProjSpecific(element);
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
                    AllowedLocationTypes.Any(lt => lt == "anyoutpost" && from.HasOutpost()) ||
                    AllowedLocationTypes.Any(lt => lt == from.Type.Identifier);
            }

            foreach (var (fromType, toType) in AllowedConnectionTypes)
            {
                if (fromType == "any" ||
                    fromType == from.Type.Identifier ||
                    (fromType == "anyoutpost" && from.HasOutpost() && from.Type.Identifier != "abandoned"))
                {
                    if (toType == "any" ||
                        toType == to.Type.Identifier ||
                        (toType == "anyoutpost" && to.HasOutpost() && to.Type.Identifier != "abandoned"))
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
    }
}
