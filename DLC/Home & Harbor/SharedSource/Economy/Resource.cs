using Barotrauma;
using Barotrauma.Extensions;
using HomeAndHarbor.Resources;
using Microsoft.Xna.Framework;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace HomeAndHarbor
{
    internal readonly record struct ResourceParamOverride(Identifier Faction, Identifier LocationType, Identifier ExcludedLocationType, ImmutableDictionary<Identifier, string> Properties)
    {
        private bool Matches(Location location)
            => (LocationType.IsEmpty || location.Type.Identifier == LocationType) &&
               (Faction.IsEmpty || location.Faction?.Prefab.Identifier == Faction) &&
                (ExcludedLocationType.IsEmpty || location.Type.Identifier != ExcludedLocationType);

        public void TryProcess(Resource resource)
        {
            if (!Matches(resource.Location)) { return; }

            Dictionary<Identifier, SerializableProperty> dictionary = SerializableProperty.GetProperties(resource);
            foreach (var (name, value) in Properties)
            {
                if (!dictionary.TryGetValue(name, out SerializableProperty? property)) 
                { 
                    DebugConsole.ThrowError($"Failed to override property \"{name}\" in resource \"{resource.Identifier}\".");
                    continue; 
                }
                property.TrySetValue(resource, value);
            }
        }

        public static bool TryLoad(XElement element, out ResourceParamOverride value)
        {
            const string locationAttributeName = "locationtype",
                        excludedLocationAttributeName = "excludedlocationtype",
                        factionAttributeName = "faction";

            Identifier locationType = element.GetAttributeIdentifier(locationAttributeName, Identifier.Empty);
            Identifier excludedLocationType = element.GetAttributeIdentifier(excludedLocationAttributeName, Identifier.Empty);
            Identifier faction = element.GetAttributeIdentifier(factionAttributeName, Identifier.Empty);

            var builder = ImmutableDictionary.CreateBuilder<Identifier, string>();
            foreach (XAttribute attr in element.Attributes())
            {
                Identifier name = attr.NameAsIdentifier();
                if (name == locationAttributeName || name == factionAttributeName || name == excludedLocationAttributeName) 
                {
                    continue;
                }

                builder[name] = attr.Value;
            }

            if ((locationType.IsEmpty && excludedLocationType.IsEmpty && faction.IsEmpty) || builder.Count == 0)
            {
                value = default;
                return false;
            }

            value = new ResourceParamOverride(faction, locationType, excludedLocationType, builder.ToImmutable());
            return true;
        }
    }

    public class Requirement
    {
        public Identifier Resource { get; }
        public PropertyConditional.ComparisonOperatorType OperatorType { get; }
        public Either<float, Identifier> AmountOrIdentifierToCompareTo { get; }

        public Identifier HasResourceStatusIndicator { get; init; } = Identifier.Empty;

        /// <summary>
        /// Should we be checking the resource amount of the destination outpost? Only relevant for requirements in mission triggers.
        /// </summary>
        public bool CompareFromDestinationOutpost;

        /// <summary>
        /// Should the resource be compared to the destination outpost's resource amount? Only relevant for requirements in mission triggers.
        /// </summary>
        public bool CompareToDestinationOutpost;

        public Requirement(Identifier hasResourceStatusIndicator, PropertyConditional.ComparisonOperatorType operatorType)
        {
            if (operatorType is not (PropertyConditional.ComparisonOperatorType.Equals or PropertyConditional.ComparisonOperatorType.NotEquals))
            {
                DebugConsole.ThrowError($"{operatorType} is not a valid operator for checking the resource status indicator {hasResourceStatusIndicator} (must be 'equals' or 'not equals').");
            }
            HasResourceStatusIndicator = hasResourceStatusIndicator;
            OperatorType = operatorType;
            AmountOrIdentifierToCompareTo = Identifier.Empty;
        }

        public Requirement(Identifier resource, PropertyConditional.ComparisonOperatorType operatorType, float amountToCompareTo)
        {
            Resource = resource;
            OperatorType = operatorType;
            AmountOrIdentifierToCompareTo = amountToCompareTo;
        }

        public Requirement(Identifier resource, PropertyConditional.ComparisonOperatorType operatorType, Identifier identifierToCompareTo)
        {
            Resource = resource;
            OperatorType = operatorType;
            AmountOrIdentifierToCompareTo = identifierToCompareTo;
        }

        /// <summary>
        /// Used to serialize the requirement back to XML
        /// </summary>
        private string OriginalConditionString { get; init; } = string.Empty;

        private const string ConditionAttributeName = "condition",
                             ResourceAttributeName = "resource";


        public static bool TryCreate(XElement element, [NotNullWhen(true)] out Requirement? req)
        {
            req = default;

            Identifier hasResourceStatusIndicator = element.GetAttributeIdentifier(nameof(hasResourceStatusIndicator), Identifier.Empty);
            if (hasResourceStatusIndicator.IsEmpty)
            {
                Identifier resource = element.GetAttributeIdentifier(ResourceAttributeName, Identifier.Empty);
                string condition = element.GetAttributeString(ConditionAttributeName, string.Empty);

                if (resource.IsEmpty)
                {
                    DebugConsole.ThrowError($"Error loading {nameof(Resource)}: Requirement \"{resource}\" not found.");
                    return false;
                }

                if (string.IsNullOrEmpty(condition))
                {
                    DebugConsole.ThrowError($"Error loading {nameof(Resource)}: Requirement \"{resource}\" has no condition.");
                    return false;
                }

                var (op, conditionStr) = PropertyConditional.ExtractComparisonOperatorFromConditionString(condition);
                if (float.TryParse(conditionStr, out float conditionValue))
                {
                    req = new Requirement(resource, op, conditionValue)
                    {
                        OriginalConditionString = conditionStr
                    };
                }
                else
                {
                    req = new Requirement(resource, op, conditionStr.ToIdentifier())
                    {
                        OriginalConditionString = conditionStr
                    };
                }
            }
            else
            {
                var (op, strValue) = PropertyConditional.ExtractComparisonOperatorFromConditionString(hasResourceStatusIndicator.Value);
                if (op is not (PropertyConditional.ComparisonOperatorType.NotEquals or PropertyConditional.ComparisonOperatorType.Equals) || strValue.IsNullOrEmpty())
                {
                    DebugConsole.ThrowError(
                        $"Error in a resource requirement. Invalid value for \"{nameof(hasResourceStatusIndicator)}\" in a conditional:" +
                        " it should be the identifier of a resource status indicator, or a not-operator (!, neq) followed by the identifier.");
                    return false;
                }
                else
                {
                    req = new Requirement(strValue.ToIdentifier(), op);
                }
            }

            req.CompareFromDestinationOutpost = element.GetAttributeBool(nameof(CompareFromDestinationOutpost), false);
            req.CompareToDestinationOutpost = element.GetAttributeBool(nameof(CompareToDestinationOutpost), false);
            return true;
        }

        internal bool Matches(Location thisLocation)
        {
            return MatchesDestination(thisLocation, thisLocation);
        }
        
        internal bool MatchesDestination(Location thisLocation, Location destination)
        {
            var thisResourceManager = PersistentOutpostData.GetPersistentOutpostData(thisLocation)?.ResourceManager;
            var destinationResourceManager = PersistentOutpostData.GetPersistentOutpostData(destination)?.ResourceManager;
            ResourceManager? managerToCompareFrom = CompareFromDestinationOutpost ? destinationResourceManager : thisResourceManager;
            ResourceManager? managerToCompareTo = CompareToDestinationOutpost ? destinationResourceManager : thisResourceManager;
            if (!Resource.IsEmpty)
            {
                if (managerToCompareFrom == null || managerToCompareTo == null)
                {
                    return false;
                }
                if (!managerToCompareFrom.HasResource(Resource, OperatorType, AmountOrIdentifierToCompareTo, resourceManagerToCompareTo: managerToCompareTo))
                {
                    return false;
                }
            }
            if (!HasResourceStatusIndicator.IsEmpty)
            {
                if (managerToCompareTo == null)
                {
                    return false;
                }
                if ((OperatorType == PropertyConditional.ComparisonOperatorType.Equals) !=
                    managerToCompareTo.HasActiveResourceStatusIndicator(HasResourceStatusIndicator, debugLogName: $"Resource {Resource}"))
                {
                    return false;
                }
            }
            return true;
        }

        internal static bool RequirementsMatch(ResourceManager Manager, IEnumerable<Requirement> requirements, PropertyConditional.LogicalOperatorType comparisonType, string debugLogName)
        {
            if (requirements.None()) { return true; }

            bool allMatchRequirements = true;
            bool anyMatchesRequirements = false;
            foreach (Requirement req in requirements)
            {
                if (req.HasResourceStatusIndicator.IsEmpty)
                {
                    if (!Manager.HasResource(req.Resource, req.OperatorType, req.AmountOrIdentifierToCompareTo))
                    {
                        allMatchRequirements = false;
                    }
                    else
                    {
                        anyMatchesRequirements = true;
                    }
                }
                else
                {
                    if ((req.OperatorType == PropertyConditional.ComparisonOperatorType.Equals) !=
                        Manager.HasActiveResourceStatusIndicator(req.HasResourceStatusIndicator, debugLogName: debugLogName))
                    {
                        allMatchRequirements = false;
                    }
                    else
                    {
                        anyMatchesRequirements = true;
                    }
                }
            }
            return comparisonType == PropertyConditional.LogicalOperatorType.And ? allMatchRequirements : anyMatchesRequirements;
        }

        public void Save(XElement element)
        {
            element.Add(
                new XAttribute(ResourceAttributeName, Resource),
                new XAttribute(ConditionAttributeName, OriginalConditionString));
        }
    };

    internal class PassiveGenerationParams
    {
        public enum Operator
        {
            Set,
            Add,
            Subtract,
            Multiply,
            Divide,
            Min,
            Max
        }

        /// <summary>
        /// How often the generation runs, in world steps.
        /// </summary>
        public readonly int Interval;

        /// <summary>
        /// Probability for the generation to run (0-1). Defaults to 1.
        /// </summary>
        public readonly float Probability;

        /// <summary>
        /// The cause of modifying this resource
        /// </summary>
        public readonly EconomySource Source;

        public readonly Identifier LogTextTag;

        /// <summary>
        /// If the source is set to <see cref="RelatedResource"/> then this field is used
        /// to log which resource affected this generation
        /// </summary>
        public readonly Identifier RelatedResource;

        /// <summary>
        /// When set, the generated amount is also added to this other resource.
        /// </summary>
        public readonly Identifier AddToOtherResource;

        /// <summary>
        /// Determines how much of the generated amount is added to the other resource. For example, if this is set to 0.5, generating 10 of this resource will add 5 to <see cref="AddToOtherResource"/>.
        /// </summary>
        public readonly float AddToOtherResourceRatio;

        /// <summary>
        /// When set, the generated amount is also subtracted from this other resource.
        /// </summary>
        public readonly Identifier TakeFromOtherResource;

        /// <summary>
        /// Determines how much of the generated amount is taken from the other resource. For example, if this is set to 0.5, generating 10 of this resource will take 5 from <see cref="TakeFromOtherResource"/>.
        /// </summary>
        public readonly float TakeFromOtherResourceRatio;

        public readonly ImmutableHashSet<Identifier> DisabledOnLocationTypes;
        private readonly bool logPlayerOwned, logNpcOutpost;

        public readonly record struct Token(Either<float, Identifier> Value, Operator Operator)
        {
            public float Get(ResourceManager manager)
                => Value.TryGet(out float floatValue)
                       ? floatValue
                       : manager.GetResource(Value.TryGet(out Identifier identifier)
                                                 ? identifier
                                                 : throw new UnreachableException())?.Amount ?? 0f;
        }

        public readonly ImmutableArray<Token> Tokens;

        private readonly ResourceManager manager;

        public readonly ImmutableHashSet<Requirement> Requirements;

        public readonly PropertyConditional.LogicalOperatorType ConditionalComparison;

        public PassiveGenerationParams(XElement element, ResourceManager manager, ResourcePrefab? prefab = null)
        {
            var builder = ImmutableArray.CreateBuilder<Token>();
            var reqBuilder = ImmutableHashSet.CreateBuilder<Requirement>();

            logPlayerOwned = element.GetAttributeBool(nameof(logPlayerOwned), true);
            logNpcOutpost = element.GetAttributeBool(nameof(logNpcOutpost), true);

            ConditionalComparison = element.GetAttributeEnum(nameof(ConditionalComparison), PropertyConditional.LogicalOperatorType.And);

            foreach (XElement subElement in element.Elements())
            {
                Identifier name = subElement.NameAsIdentifier();

                if (name == "Conditional")
                {
                    if (Requirement.TryCreate(subElement, out var req))
                    {
                        reqBuilder.Add(req);
                    }
                    continue;
                }
                else
                {
                    if (!Enum.GetValues<Operator>().FirstOrNone(x => x.ToIdentifier() == name).TryUnwrap(out var op))
                    {
                        DebugConsole.ThrowError($"Error loading {nameof(PassiveGenerationParams)} in {(prefab != null ? $"resource prefab {prefab.Identifier}" : $"element {element}")}: Unknown operator \"{name}\".");
                        continue;
                    }
                    Identifier variableName = subElement.GetAttributeIdentifier("resource", Identifier.Empty);
                    float floatValue = subElement.GetAttributeFloat("constant", 0f);

                    builder.Add(variableName.IsEmpty
                                    ? new Token(floatValue, op)
                                    : new Token(variableName, op));
                }
            }

            Interval = element.GetAttributeInt(nameof(Interval), 1);
            Probability = element.GetAttributeFloat(nameof(Probability), 1.0f);

            Source = element.GetAttributeEnum(nameof(Source), EconomySource.None);
            RelatedResource = element.GetAttributeIdentifier(nameof(RelatedResource), Identifier.Empty);
            LogTextTag = element.GetAttributeIdentifier(nameof(LogTextTag), Identifier.Empty);

            AddToOtherResource = element.GetAttributeIdentifier(nameof(AddToOtherResource), Identifier.Empty);
            AddToOtherResourceRatio = element.GetAttributeFloat(nameof(AddToOtherResourceRatio), 1.0f);
            TakeFromOtherResource = element.GetAttributeIdentifier(nameof(TakeFromOtherResource), Identifier.Empty);
            TakeFromOtherResourceRatio = element.GetAttributeFloat(nameof(TakeFromOtherResourceRatio), 1.0f);

            DisabledOnLocationTypes = element.GetAttributeIdentifierImmutableHashSet(nameof(DisabledOnLocationTypes), ImmutableHashSet<Identifier>.Empty);

            Tokens = builder.ToImmutable();
            Requirements = reqBuilder.ToImmutable();
            this.manager = manager;
        }

        public float CalculateGeneration(float baseAmount)
        {
            //negative value = unknown
            if (baseAmount < 0)
            {
                return baseAmount;
            }

            float currentValue = baseAmount;

            foreach (Token token in Tokens)
            {
                float value = token.Get(manager);
                switch (token.Operator)
                {
                    case Operator.Set:
                        currentValue = value;
                        break;
                    case Operator.Add:
                        currentValue += value;
                        break;
                    case Operator.Subtract:
                        currentValue -= value;
                        break;
                    case Operator.Multiply:
                        currentValue *= value;
                        break;
                    case Operator.Divide:
                        currentValue /= value;
                        break;
                    case Operator.Min:
                        currentValue = Math.Min(currentValue, value);
                        break;
                    case Operator.Max:
                        currentValue = Math.Max(currentValue, value);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(token.Operator), token.Operator, null);
                }
            }
            return Math.Max(currentValue, 0);
        }

        public bool ShouldLog(Resource resource)
        {
            PersistentOutpostData data = PersistentOutpostData.GetPersistentOutpostData(resource.Location);
            if (!logPlayerOwned && data.IsPlayerOwned) { return false; }
            if (!logNpcOutpost && !data.IsPlayerOwned) { return false; }
            return true;
        }
    }

    internal class MissionTrigger
    {
        public readonly Identifier MissionIdentifier;
        public readonly Identifier MissionTag;

        public readonly ImmutableList<Requirement> Requirements;

        /// <summary>
        /// Probability for the mission to trigger (0-1) per world step. Defaults to 1.
        /// </summary>
        public readonly float Probability;

        public readonly ImmutableHashSet<Identifier> AllowedFactions;

        public readonly ImmutableHashSet<Identifier> AllowedPreviousFactions;

        /// <summary>
        /// Resource generation that triggers when the mission is triggered (e.g. subtracting resources from the outpost when it sends a cargo mission to another outpost).
        /// </summary>
        public readonly ImmutableList<(Identifier ResourceIdentifier, PassiveGenerationParams Generation)> GenerationOnTrigger;

        /// <summary>
        /// Resource generation that triggers on the destination when the mission is triggered 
        /// (e.g. subtracting money from the destination outpost when a cargo mission departs).
        /// The key is the identifier of the destination outpost resource to modify.
        /// </summary>
        public readonly ImmutableList<(Identifier ResourceIdentifier, PassiveGenerationParams Generation)> DestinationOutpostGenerationOnTrigger;

        /// <summary>
        /// Does the destination outpost need to belong to the same faction?
        /// </summary>
        public bool RequireSameFactionDestinationOutpost;

        /// <summary>
        /// Does the destination outpost need to have previously belonged to the same faction? Can be used e.g. on triggering missions where a faction tries to reclaim control of an outpost that was taken from them.
        /// </summary>
        public bool RequirePreviouslySameFactionDestinationOutpost;

        /// <summary>
        /// Does the destination outpost need to belong to some other faction? Outposts not belonging to any faction are not considered valid targets.
        /// </summary>
        public bool RequireDifferentFactionDestinationOutpost;

        /// <summary>
        /// Does the destination outpost need to belong to no faction? Player outposts are considered to not belong to any faction.
        /// </summary>
        public bool RequireNoFactionDestinationOutpost;

        /// <summary>
        /// Does the destination outpost need to belong to a hostile faction (or the player, if they're hostile towards this faction)?
        /// </summary>
        public bool RequireHostileDestinationOutpost;

        /// <summary>
        /// Does the destination outpost need to be owned by the player?
        /// </summary>
        public bool RequireDestinationPlayerOutpost;

        /// <summary>
        /// Maximum distance between this location and the destination of the mission.
        /// </summary>
        public int MaxDistanceFromDestinationOutpost;
        
        /// <summary>
        /// Disallow triggering the mission if there's a mission of any of these types in the path.
        /// </summary>
        public readonly ImmutableHashSet<Identifier> DisallowIfMissionsOfTypeInPath;

        /// <summary>
        /// If set, the mission difficulty is based on the furthest explored level instead of the level the mission actually triggers in.
        /// The intention is to allow more difficult missions to start appearing in the player's outpost as they get further in the game.
        /// </summary>
        public bool DifficultyBasedOnFurthestExploredLevel;

        /// <summary>
        /// When enabled, the mission can only trigger when you're entering the location in question (i.e. can't trigger in some location further on the map).
        /// </summary>
        public bool RequireEnteringLocation;

        /// <summary>
        /// If true, the location will be marked as discovered and will become visible on the campaign map, even if it's hidden by the fog of war.
        /// </summary>
        public bool MarkLocationAsDiscovered;

        /// <summary>
        /// If true, neither the origin nor the destination can belong to a faction that's hostile towards the player.
        /// </summary>
        public bool RequireNonHostileToPlayer;

        /// <summary>
        /// Content package this trigger is defined in, only used for logging purposes.
        /// </summary>
        public readonly ContentPackage? ContentPackage;

        public MissionTrigger(ContentXElement element, ResourceManager manager, string parentDebugName)
        {
            ContentPackage = element.ContentPackage;
            Probability = element.GetAttributeFloat(nameof(Probability), 1.0f);

            var reqBuilder = ImmutableList.CreateBuilder<Requirement>();
            MissionIdentifier = element.GetAttributeIdentifier(nameof(MissionIdentifier), Identifier.Empty);
            MissionTag = element.GetAttributeIdentifier(nameof(MissionTag), Identifier.Empty);

            RequireSameFactionDestinationOutpost = element.GetAttributeBool(nameof(RequireSameFactionDestinationOutpost), false);
            RequirePreviouslySameFactionDestinationOutpost = element.GetAttributeBool(nameof(RequirePreviouslySameFactionDestinationOutpost), false);
            RequireDifferentFactionDestinationOutpost = element.GetAttributeBool(nameof(RequireDifferentFactionDestinationOutpost), false);
            RequireNoFactionDestinationOutpost = element.GetAttributeBool(nameof(RequireNoFactionDestinationOutpost), false);
            RequireHostileDestinationOutpost = element.GetAttributeBool(nameof(RequireHostileDestinationOutpost), false);
            RequireDestinationPlayerOutpost = element.GetAttributeBool(nameof(RequireDestinationPlayerOutpost), false);
            MaxDistanceFromDestinationOutpost = element.GetAttributeInt(nameof(MaxDistanceFromDestinationOutpost), 1);
            DifficultyBasedOnFurthestExploredLevel = element.GetAttributeBool(nameof(DifficultyBasedOnFurthestExploredLevel), false);
            RequireEnteringLocation = element.GetAttributeBool(nameof(RequireEnteringLocation), false);
            AllowedFactions = element.GetAttributeIdentifierImmutableHashSet(nameof(AllowedFactions), ImmutableHashSet<Identifier>.Empty);
            AllowedPreviousFactions = element.GetAttributeIdentifierImmutableHashSet(nameof(AllowedPreviousFactions), ImmutableHashSet<Identifier>.Empty);
            MarkLocationAsDiscovered = element.GetAttributeBool(nameof(MarkLocationAsDiscovered), false);
            RequireNonHostileToPlayer = element.GetAttributeBool(nameof(RequireNonHostileToPlayer), false);

            DisallowIfMissionsOfTypeInPath = element.GetAttributeIdentifierImmutableHashSet(nameof(DisallowIfMissionsOfTypeInPath), ImmutableHashSet<Identifier>.Empty);

            if (MissionIdentifier.IsEmpty && MissionTag.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in a mission trigger defined in {parentDebugName}: neither mission identifier or mission tag configured.");
            }
            else if (!MissionIdentifier.IsEmpty && !MissionTag.IsEmpty)
            {
                DebugConsole.AddWarning($"Error in a mission trigger defined in {parentDebugName}: both mission identifier and mission tag have been configured. Only the identifier will be used.");
            }

            List<(Identifier, PassiveGenerationParams)> generationOnTriggerList = [];
            List<(Identifier, PassiveGenerationParams)> destinationOutpostgenerationOnTriggerList = [];
            foreach (ContentXElement subElement in element.Elements())
            {
                Identifier name = subElement.NameAsIdentifier();
                if (name == "Conditional")
                {
                    if (Requirement.TryCreate(subElement, out var req))
                    {
                        reqBuilder.Add(req);
                    }
                }
                else if (name == "Generation")
                {
                    Identifier resourceId = subElement.GetAttributeIdentifier("resource", Identifier.Empty);
                    if (subElement.GetAttributeIdentifier("target", Identifier.Empty) == "destination")
                    {
                        destinationOutpostgenerationOnTriggerList.Add((resourceId, new PassiveGenerationParams(subElement, manager)));
                    }
                    else
                    {
                        generationOnTriggerList.Add((resourceId, new PassiveGenerationParams(subElement, manager)));
                    }
                }
            }
            Requirements = reqBuilder.ToImmutable();
            GenerationOnTrigger = generationOnTriggerList.ToImmutableList();
            DestinationOutpostGenerationOnTrigger = destinationOutpostgenerationOnTriggerList.ToImmutableList();
        }

        internal bool MatchesDestination(Location thisLocation, Location destination)
        {
            int distanceToDestination = Map.GetDistanceToClosestLocationOrConnection(thisLocation, MaxDistanceFromDestinationOutpost, (Location otherLocation) => otherLocation == destination);
            if (distanceToDestination > MaxDistanceFromDestinationOutpost) { return false; }

            if (RequireEnteringLocation && GameMain.GameSession?.GameMode is CampaignMode campaign &&
                campaign.CurrentLocation != destination)
            {
                return false;
            }

            bool isDestinationPlayerOwned = PersistentOutpostData.GetPersistentOutpostData(destination) is { IsPlayerOwned: true };
            if (RequireDestinationPlayerOutpost && !isDestinationPlayerOwned)
            {
                return false;
            }
            if (RequireNonHostileToPlayer && (thisLocation.IsFactionHostile || destination.IsFactionHostile))
            {
                return false;
            }
            if ((RequireSameFactionDestinationOutpost && destination.Faction != thisLocation.Faction) ||
                (RequireNoFactionDestinationOutpost && destination.Faction != null) ||
                (RequireDifferentFactionDestinationOutpost && (destination.Faction == thisLocation.Faction || destination.Faction == null)) ||
                (RequirePreviouslySameFactionDestinationOutpost && destination.PreviousFaction != thisLocation.Faction))
            {
                return false;
            }
            if (RequireHostileDestinationOutpost)
            {
                if (isDestinationPlayerOwned)
                {
                    if (!thisLocation.IsFactionHostile) { return false; }
                }
                else if (destination.Faction?.Prefab.Identifier != thisLocation.Faction?.Prefab.OpposingFaction)
                {
                    return false;
                }
            }
            return true;
        }
    }

    internal class EventTrigger
    {
        public readonly Identifier EventIdentifier;
        public readonly Identifier EventTag;

        public readonly ImmutableList<Requirement> Requirements;

        /// <summary>
        /// Probability for the mission to trigger (0-1) per world step. Defaults to 1.
        /// </summary>
        public readonly float Probability;

        public readonly ImmutableHashSet<Identifier> AllowedFactions;
        public readonly ImmutableHashSet<Identifier> AllowedLocationTypes;

        public EventTrigger(ContentXElement element, ResourceManager manager, string parentDebugName)
        {
            Probability = element.GetAttributeFloat(nameof(Probability), 1.0f);

            EventIdentifier = element.GetAttributeIdentifier(nameof(EventIdentifier), Identifier.Empty);
            EventTag = element.GetAttributeIdentifier(nameof(EventTag), Identifier.Empty);

            AllowedFactions = element.GetAttributeIdentifierImmutableHashSet(nameof(AllowedFactions), ImmutableHashSet<Identifier>.Empty);
            AllowedLocationTypes = element.GetAttributeIdentifierImmutableHashSet(nameof(AllowedLocationTypes), ImmutableHashSet<Identifier>.Empty);

            if (EventIdentifier.IsEmpty && EventTag.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in a event trigger defined in {parentDebugName}: neither event identifier or event tag configured.");
            }
            else if (!EventIdentifier.IsEmpty && !EventTag.IsEmpty)
            {
                DebugConsole.AddWarning($"Error in a event trigger defined in {parentDebugName}: both event identifier and event tag have been configured. Only the identifier will be used.");
            }

            var reqBuilder = ImmutableList.CreateBuilder<Requirement>();
            foreach (ContentXElement subElement in element.Elements())
            {
                Identifier name = subElement.NameAsIdentifier();
                if (name == "Conditional")
                {
                    if (Requirement.TryCreate(subElement, out var req))
                    {
                        reqBuilder.Add(req);
                    }
                }
            }
            Requirements = reqBuilder.ToImmutable();
        }
    }

    internal abstract class Resource : ISerializableEntity
    {
        public readonly ResourcePrefab Prefab;

        public Identifier Identifier
            => Prefab.Identifier;

        public readonly LocalizedString DisplayName;

        [Serialize("0,0", IsPropertySaveable.No, description: "Default amount of the resource, randomly picked between min and max when the resource is created.")]
        public Vector2 DefaultAmount { get; private set; }

        [Serialize(0.0f, IsPropertySaveable.No, description: "Minimum amount of the resource (the amount cannot go below this).")]
        public float MinAmount { get; private set; }

        [Serialize(float.MaxValue, IsPropertySaveable.No, description: "Maximum amount of the resource (the amount cannot go above this).")]
        public float MaxAmount { get; private set; }

        [Serialize(-1f, IsPropertySaveable.No, description: "If set to a non negative value this value will be used for simulation instead of the actual amount when the actual value is unknown (-1). In other words, used as the \"default\" value on resources such as the number of devices or NPCs when the outpost hasn't been visited yet, and the game doesn't yet know how many of those will generate.")]
        public float SimulatedValue { get; private set; }

        [Serialize("", IsPropertySaveable.No, description: "If set, when the value of this resource changes, we instead log the changes to some other resource. Relevant for e.g. changes to population - we have a hidden, modifiable population resource, and a visible currentpopulation resource which also takes into account killed NPCs.")]
        public Identifier LogAsResource { get; private set; }

        /// <summary>
        /// Defaults to the value set in the ResourcePrefab. Can be used to selectively hide specific resources, for example based on location type.
        /// </summary>
        public bool Hidden
        {
            get;
            set;
        }

        /// <summary>
        /// If set to a non-negative value, forces the <see cref="Amount"/> to always stay at this specific value. 
        /// Useful for e.g. forcing a resource stay at a given value in an NPC outpost where it's not supposed to be simulated.
        /// </summary>
        [Serialize(-1f, IsPropertySaveable.No, description: "")]
        public float StaticAmount { get; private set; }

        /// <summary>
        /// Current amount of the resource. If the amount is unknown (negative), the <see cref="SimulatedValue">simulated value</see> is returned instead.
        /// </summary>
        public virtual float Amount
        {
            get
            {
                if (StaticAmount >= 0) { return StaticAmount; }
                float amount = GetAmount();
                if (amount < 0 && SimulatedValue >= 0)
                {
                    return SimulatedValue;
                }
                return amount;
            }
            set
            {
                //only clamp in the direction of the change: if the resource is already above the max (or below the min),
                //e.g. due to the amount being set to match the items in the outpost, it shouldn't be immediately clamped down to the max
                float currentAmount = Amount;
                value = Math.Clamp(value,
                    min: Math.Min(MinAmount, currentAmount),
                    max: Math.Max(MaxAmount, currentAmount));
                if (IsReadonly)
                {
                    DebugConsole.ThrowError($"Error setting amount for resource \"{Identifier}\": resource is readonly.");
                }
                else
                {
                    SetAmount(value);
                }
            }
        }

        /// <summary>
        /// Amount displayed on the UI. Simulated value is not used.
        /// </summary>
        public float DisplayedAmount
            => GetAmount();

        /// <summary>
        /// Displayed amount rounded to an integer.
        /// </summary>
        public int DisplayedAmountInt => (int)MathF.Floor(DisplayedAmount);

        public readonly ResourceManager Manager;

        public readonly ImmutableArray<PassiveGenerationParams> passiveGenerations;

        private readonly ImmutableArray<ResourceStatusIndicator> resourceResourceStatusIndicators;

        private readonly ImmutableList<MissionTrigger> missionTriggers;
        private readonly ImmutableList<EventTrigger> eventTriggers;

        public IEnumerable<ResourceStatusIndicator> ResourceStatusIndicators => resourceResourceStatusIndicators;

        public bool IsReadonly => this is ReadonlyResource || Prefab.Readonly;

        /// <summary>
        /// Can the server sync the amount of this resource to the clients?
        /// </summary>
        public virtual bool IsSyncable => true;

        public Location Location
            => Manager.Location;

        /// <param name="element">Element in a campaign save (when loading existing resources and their values).</param>
        protected Resource(ResourcePrefab prefab, ResourceManager manager, XElement? element = null)
        {
            SerializableProperties = SerializableProperty.GetProperties(this);
            Prefab = prefab;
            Manager = manager;
            DisplayName = TextManager.Get($"resource.{Identifier}").Fallback($"{Identifier}");

            //make Hidden default to the prefab value
            Hidden = element.GetAttributeBool(nameof(Hidden), Prefab.Hidden);

            SerializableProperty.DeserializeProperties(this, prefab.OriginalElement);
            SerializableProperty.AdditionalDeserializeProperties(this, element);

            List<PassiveGenerationParams> passiveGenerationList = [];
            List<ResourceStatusIndicator> resourceStatusIndicatorList = [];
            var missionTriggersBuilder = ImmutableList.CreateBuilder<MissionTrigger>();
            var eventTriggersBuilder = ImmutableList.CreateBuilder<EventTrigger>();
            foreach (var subElement in prefab.OriginalElement.Elements())
            {
                Identifier name = subElement.NameAsIdentifier();
                if (name == "Generation")
                {
                    passiveGenerationList.Add(new PassiveGenerationParams(subElement, manager));
                }
                else if (name == "Override")
                {
                    if (ResourceParamOverride.TryLoad(subElement, out var paramOverride))
                    {
                        paramOverride.TryProcess(this);
                    }
                    else
                    {
                        DebugConsole.ThrowError($"Error loading resource \"{prefab.Identifier}\": invalid override. Either a faction, location type or excluded location type must be specified, and at least one property to override.");
                    }
                }
                else if (name == nameof(MissionTrigger))
                {
                    missionTriggersBuilder.Add(new MissionTrigger(subElement, manager, parentDebugName: $"Resource ({Prefab.Identifier})"));
                }
                else if (name == nameof(EventTrigger))
                {
                    eventTriggersBuilder.Add(new EventTrigger(subElement, manager, parentDebugName: $"Resource ({Prefab.Identifier})"));
                }
                else if (name == nameof(ResourceStatusIndicator))
                {
                    resourceStatusIndicatorList.Add(new ResourceStatusIndicator(subElement, debugLogName: prefab.Identifier.Value));
                }
            }
            passiveGenerations = passiveGenerationList.ToImmutableArray();
            resourceResourceStatusIndicators = resourceStatusIndicatorList.ToImmutableArray();
            missionTriggers = missionTriggersBuilder.ToImmutableList();
            eventTriggers = eventTriggersBuilder.ToImmutable();
        }

        protected float GetAmount()
        {
            if (StaticAmount >= 0) { return StaticAmount; }
            return GetAmountResourceSpecific();
        }

        protected abstract float GetAmountResourceSpecific();

        protected abstract void SetAmount(float value);
        public LocalizedString GetAmountText(bool withNumber = false)
        {
            LocalizedString? result = null;
            if (DisplayedAmount < 0)
            {
                result = TextManager.Get("Unknown");
            }
            else
            {
                result = Prefab.DefaultAmountText;
                foreach ((float minAmount, LocalizedString text) in Prefab.AmountTexts)
                {
                    if (DisplayedAmount < minAmount) { continue; }
                    result = text;
                    break;
                }
            }

            result ??= DisplayedAmount.ToString();
#if DEBUG
            if (DisplayedAmount < 0)
            {
                //if unknown, show "Amount" (which uses the simulated value when the actual value is unknown).
                result += $" [{Amount}]";
                withNumber = false;
            }
            else
            {
                withNumber = true;
            }
#endif

            if (withNumber)
            {
                result += $" ({DisplayedAmountInt})";
            }

            return result;
        }

        public virtual XElement Save()
        {
            XElement saveElement = new XElement(GetType().Name, new XAttribute("identifier", Identifier));
            SerializableProperty.SerializeProperties(this, saveElement);
            return saveElement;
        }

        public static Resource? Load(ResourceManager parent, XElement element)
        {
            Identifier identifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);
            if (identifier.IsEmpty)
            {
                DebugConsole.ThrowError(
                    $"Error loading {nameof(Resource)}: ResourcePrefab with identifier \"{identifier}\" not found.");
                return null;
            }

            ResourcePrefab? prefab = ResourcePrefab.FindPrefab(identifier);

            if (prefab == null)
            {
                DebugConsole.ThrowError(
                    $"Error loading {nameof(Resource)}: ResourcePrefab with identifier \"{identifier}\" not found.");
                return null;
            }

            return Load(prefab, parent, element);
        }

        public static Resource? Load(ResourcePrefab prefab, ResourceManager parent, XElement? element = null)
        {
            Type? type = ReflectionUtils.GetTypeWithBackwardsCompatibility(
                Plugin.DlcAssembly,
                nameSpace: $"{nameof(HomeAndHarbor)}.{nameof(Resources)}",
                typeName: prefab.OriginalElement.Name.ToString(),
                throwOnError: true,
                ignoreCase: true);

            if (type == null)
            {
                DebugConsole.ThrowError(
                    $"Error loading {nameof(Resource)}: ResourcePrefab with identifier \"{prefab.Identifier}\" not found.");
                return null;
            }

            Resource resource = (Resource)Activator.CreateInstance(type, [prefab, parent, element])!;
            return resource;
        }

        public virtual void Tick(int currentWorldStep)
        {
            TryTriggerMissions(currentWorldStep);
            UpdateResourceStatusIndicators();

            Resource resourceToLog = GetResourceToLogAs() ?? this;

            foreach (var passiveGeneration in passiveGenerations)
            {
                float oldAmount = resourceToLog.Amount;
                ProcessResourceGeneration(passiveGeneration, currentWorldStep);

                if (passiveGeneration.ShouldLog(resourceToLog))
                {
                    EconomyLogEntry entry;
                    if (!passiveGeneration.LogTextTag.IsEmpty)
                    {
                        entry = EconomyLogEntrySpecial.Create(passiveGeneration.LogTextTag, resourceToLog, oldAmount, resourceToLog.Amount,
                            relatedResource: passiveGeneration.RelatedResource.IsEmpty ? null : Manager.GetResource(passiveGeneration.RelatedResource));
                    }
                    else
                    {
                        entry = passiveGeneration.Source switch
                        {
                            EconomySource.RelatedResource => EconomyLogEntryGeneric.Create(LogType.NaturalGenerationWithRelated, resourceToLog, oldAmount, resourceToLog.Amount,
                                relatedResource: Manager.GetResource(passiveGeneration.RelatedResource)),
                            EconomySource.SoftCap         => EconomyLogEntryGeneric.Create(LogType.NaturalGenerationSoftCap, resourceToLog, oldAmount, resourceToLog.Amount),
                            _                             => EconomyLogEntryGeneric.Create(LogType.NaturalGeneration, resourceToLog, oldAmount, resourceToLog.Amount)
                        };
                    }

                    OnChanged(entry);
                    oldAmount = resourceToLog.Amount;
                }
            }
        }

        public void UpdateResourceStatusIndicators()
        {
            foreach (var resourceStatusIndicator in resourceResourceStatusIndicators)
            {
                resourceStatusIndicator.UpdateIsActive(Manager);
            }
        }

        /// <summary>
        /// When the value of this resource changes, which resource should be logged in the resource logs?
        /// Some resources can be hidden "child resources" of a non-hidden parent resource (e.g. rawplantfood is a hidden resource, which affects the non-hidden food resource), and we should log changes to that parent resource instead.
        /// This method returns the root-level non-hidden resource. If one can't be found, returns null.
        /// </summary>
        /// <returns></returns>
        public Resource? GetResourceToLogAs()
        {
            if (!LogAsResource.IsEmpty && Manager.Resources.FirstOrDefault(r => r.Identifier == LogAsResource) is Resource resourceToLogAs)
            {
                return resourceToLogAs;
            }
            if (!Hidden) { return this; }
            foreach (var otherResource in Manager.Resources)
            {
                if (otherResource == this) { continue; }
                if (otherResource is CombinedResource combinedResource &&
                    combinedResource.ContainsResource(this))
                {
                    return otherResource.GetResourceToLogAs();
                }
            }
            return null;
        }

        private void TryTriggerMissions(int currentWorldStep)
        {
            foreach (var missionTrigger in missionTriggers)
            {
                ProcessMissionTrigger(missionTrigger, currentWorldStep);
            }            
        }

        public void TryTriggerEvents()
        {
            if (Level.IsLoadedOutpost &&
                GameMain.GameSession.Map?.CurrentLocation == Manager.Location)
            {
                foreach (var eventTrigger in eventTriggers)
                {
                    ProcessEventTrigger(eventTrigger);
                }
            }
        }

        private void ProcessMissionTrigger(MissionTrigger missionTrigger, int currentWorldStep)
        {
            if (Rand.Range(0.0f, 1.0f) > missionTrigger.Probability) { return; }
            TryTriggerMission(missionTrigger, Manager, triggeringResource: this, currentWorldStep);
        }

        /// <summary>
        /// Attempts to find a valid destination and unlock the mission from the location of the resource manager.
        /// If a mission is unlocked, the resource generations defined in the trigger are applied to the origin and the destination.
        /// Note that <see cref="MissionTrigger.Probability"/> is not checked here.
        /// </summary>
        /// <param name="manager">Resource manager of the location the mission is sent from.</param>
        /// <param name="triggeringResource">The resource the trigger is defined in, used by the generations that don't define which resource to modify.
        /// Null if the trigger isn't defined in a resource (e.g. when triggered by an event).</param>
        /// <param name="destinationCandidates">If set, only these locations are considered valid destinations.
        /// Otherwise any valid location within <see cref="MissionTrigger.MaxDistanceFromDestinationOutpost"/> can be chosen.</param>
        /// <param name="createNetworkEvent">Should the mission unlock and the resource changes be synced to clients immediately?</param>
        /// <param name="createUnlockNotification">Should the "mission unlocked" notification pop up when the mission is unlocked?</param>
        /// <returns>The unlocked mission, or null if the mission couldn't be unlocked.</returns>
        internal static Mission? TryTriggerMission(MissionTrigger missionTrigger, ResourceManager manager, Resource? triggeringResource, int currentWorldStep,
            IEnumerable<Location>? destinationCandidates = null, bool createNetworkEvent = false, bool createUnlockNotification = false)
        {
            Identifier factionId = manager.Location.Faction?.Prefab.Identifier ?? Identifier.Empty;
            if (missionTrigger.AllowedFactions.Any() &&
                !missionTrigger.AllowedFactions.Contains(factionId))
            {
                return null;
            }
            Identifier previousFactionId = manager.Location.PreviousFaction?.Prefab.Identifier ?? Identifier.Empty;
            if (missionTrigger.AllowedPreviousFactions.Any() &&
                !missionTrigger.AllowedPreviousFactions.Contains(previousFactionId))
            {
                return null;
            }
            int maxDistance = missionTrigger.MaxDistanceFromDestinationOutpost;

            if (maxDistance <= 0)
            {
                //if the mission is supposed to trigger in this location, check if it's valid first
                //this avoids the more expensive checks later, and also prevents us from checking
                //e.g. player-outpost-only resources in an NPC outpost if the mission is only configured to trigger in player outposts
                if (!missionTrigger.MatchesDestination(manager.Location, manager.Location)) { return null; }
            }

            bool matchesRequirements = Requirement.RequirementsMatch(manager, missionTrigger.Requirements.Where(req => !req.CompareToDestinationOutpost && !req.CompareFromDestinationOutpost),
                PropertyConditional.LogicalOperatorType.And, debugLogName: $"{nameof(MissionTrigger)}, Resource {triggeringResource?.Prefab.Identifier}");
            if (!matchesRequirements) { return null; }

            Identifier missionId = missionTrigger.MissionIdentifier.Replace("[faction]".ToIdentifier(), factionId);
            Identifier missionTag = missionTrigger.MissionTag.Replace("[faction]".ToIdentifier(), factionId);
            float furthestExploredLevelDifficulty = GameMain.GameSession.Map?.GetHighestVisitedLevelDifficulty() ?? manager.Location.LevelData.Difficulty;

            Mission? unlockedMission = null;
            Location? selectedDestination = null;
            if (maxDistance > 0)
            {
                //go through all the locations connected to this one
                var connectedLocations = Map.GetLocationsWithinDistance(manager.Location, maxDistance).ToList();
                connectedLocations.Remove(manager.Location);
                //shuffle so we don't always prefer the first connected location
                connectedLocations.Shuffle(Rand.RandSync.Unsynced);
                if (destinationCandidates != null)
                {
                    connectedLocations = destinationCandidates.Where(l => l != manager.Location).ToList();
                }

                List<Location>? pathToLocation = null;
                //check if we have an appropriate destination within range
                foreach (Location destination in connectedLocations)
                {
                    if (!missionTrigger.MatchesDestination(manager.Location, destination)) { continue; }
                    var destinationResourceManager = PersistentOutpostData.GetPersistentOutpostData(destination)?.ResourceManager;
                    if (destinationResourceManager == null)
                    {
                        continue;
                    }

                    pathToLocation = Map.GetPathToLocation(manager.Location, destination, maxDistance);
                    var firstConnection = manager.Location.Connections.First(c => c.Locations.Contains(pathToLocation.Skip(1).First()));
                    if (firstConnection.AvailableMissions.Any(m => missionTrigger.DisallowIfMissionsOfTypeInPath.Contains(m.Prefab.Type)))
                    {
                        //if there's a disallowed mission on the first connection of the path (let's say, a transport sub travelling in the level that we're trying to send a transport sub to), don't consider this a valid path
                        //note that we intentionally only check the first connection on the path - if there's subs somewhere further on the path, it can be fine to send a sub anyway (the other sub should move away by the time we reach that path)
                        continue;
                    }

                    bool destinationMatchesRequirements = true;
                    foreach (Requirement req in missionTrigger.Requirements.Where(req => req.CompareToDestinationOutpost || req.CompareFromDestinationOutpost))
                    {
                        if (!req.MatchesDestination(manager.Location, destination))
                        {
                            destinationMatchesRequirements = false;
                            break;
                        }
                    }
                    if (destinationMatchesRequirements)
                    {
                        selectedDestination = destination;
                        break;
                    }
                }
                if (selectedDestination == null || pathToLocation == null)
                {
                    return null;
                }
                unlockedMission = MissionAction.UnlockMission(GameMain.GameSession.Campaign, manager.Location, missionId, missionTag,
                    forceConnection: manager.Location.Connections.First(c => c.Locations.Contains(pathToLocation.Skip(1).First())),
                    markLocationAsDiscovered: missionTrigger.MarkLocationAsDiscovered,
                    createMessageBox: createUnlockNotification,
                    invokingContentPackage: missionTrigger.ContentPackage,
                    overrideLevelDifficulty: missionTrigger.DifficultyBasedOnFurthestExploredLevel ? furthestExploredLevelDifficulty : manager.Location.LevelData.Difficulty,
                    //no need to create network events between rounds, the resources are similated between rounds and the clients receive the mission info from the save file
                    createNetworkEvent: createNetworkEvent);
                unlockedMission?.SetPathToDestination(pathToLocation);
            }
            //no need to check MatchesDestination, we already do it earlier in the method to allow an early exit
            else
            {
                unlockedMission = MissionAction.UnlockMission(GameMain.GameSession.Campaign, manager.Location, missionId, missionTag,
                    createMessageBox: createUnlockNotification,
                    markLocationAsDiscovered: missionTrigger.MarkLocationAsDiscovered,
                    invokingContentPackage: missionTrigger.ContentPackage,
                    overrideLevelDifficulty: missionTrigger.DifficultyBasedOnFurthestExploredLevel ? furthestExploredLevelDifficulty : manager.Location.LevelData.Difficulty,
                    //no need to create network events between rounds, the resources are similated between rounds and the clients receive the mission info from the save file
                    createNetworkEvent: createNetworkEvent);
            }
            if (unlockedMission != null)
            {
                unlockedMission.OriginLocation = manager.Location;
                foreach ((Identifier resourceId, PassiveGenerationParams generation) in missionTrigger.GenerationOnTrigger)
                {
                    if (resourceId.IsEmpty)
                    {
                        if (triggeringResource == null)
                        {
                            DebugConsole.ThrowError("Failed to process resource generation in a MissionTrigger: the generation doesn't define a resource, and the trigger isn't defined in a resource.");
                            continue;
                        }
                        float oldAmount = triggeringResource.Amount;
                        triggeringResource.ProcessResourceGeneration(generation, currentWorldStep);
                        if (generation.ShouldLog(triggeringResource))
                        {
                            LogGenerationChange(generation, triggeringResource, oldAmount, selectedDestination);
                        }
                    }
                    else
                    {
                        if (manager.GetResource(resourceId) is { } resource)
                        {
                            Resource resourceToLog = resource.GetResourceToLogAs() ?? resource;
                            float oldAmount = resourceToLog.Amount;
                            resource.ProcessResourceGeneration(generation, currentWorldStep);
                            if (generation.ShouldLog(resourceToLog))
                            {
                                LogGenerationChange(generation, resourceToLog, oldAmount, selectedDestination);
                            }
                        }
                        else
                        {
                            DebugConsole.ThrowError($"Failed to process resource generation in a MissionTrigger: could not find a resource \"{resourceId}\" in the location {manager.Location.DisplayName}.");
                        }
                    }
                }

                if (selectedDestination != null)
                {
                    foreach ((Identifier resourceId, PassiveGenerationParams generation) in missionTrigger.DestinationOutpostGenerationOnTrigger)
                    {
                        var destinationResourceManager = PersistentOutpostData.GetPersistentOutpostData(selectedDestination)?.ResourceManager;
                        if (destinationResourceManager?.GetResource(resourceId) is { } destinationResource)
                        {
                            Resource resourceToLog = destinationResource.GetResourceToLogAs() ?? destinationResource;
                            float oldAmount = resourceToLog.Amount;
                            destinationResource.ProcessResourceGeneration(generation, currentWorldStep);
                            if (generation.ShouldLog(resourceToLog))
                            {
                                LogGenerationChange(generation, resourceToLog, oldAmount, manager.Location);
                            }
                        }
                        else
                        {
                            DebugConsole.ThrowError($"Failed to process resource generation in a MissionTrigger on destination outpost: could not find a resource \"{resourceId}\" in the location {selectedDestination.DisplayName}.");
                        }
                    }
                }
#if SERVER
                if (createNetworkEvent)
                {
                    manager.OutpostData.ServerWriteResources(createUINotification: true);
                    if (selectedDestination != null)
                    {
                        PersistentOutpostData.GetPersistentOutpostData(selectedDestination).ServerWriteResources(createUINotification: true);
                    }
                }
#endif
            }
            return unlockedMission;

            static void LogGenerationChange(PassiveGenerationParams generation, Resource resource, float oldAmount, Location? relevantLocation)
            {
                Resource resourceToLog = resource.GetResourceToLogAs() ?? resource;
                resource.OnChanged(
                    generation.LogTextTag.IsEmpty
                        ? EconomyLogEntryGeneric.Create(LogType.CargoPaid, resourceToLog, oldAmount, resourceToLog.Amount, relevantLocation)
                        : EconomyLogEntrySpecial.Create(generation.LogTextTag, resourceToLog, oldAmount, resourceToLog.Amount, relevantLocation,
                            generation.RelatedResource.IsEmpty ? null : resource.Manager.GetResource(generation.RelatedResource)));
            }
        }
        
        private void ProcessEventTrigger(EventTrigger eventTrigger)
        {
            if (Rand.Range(0.0f, 1.0f) > eventTrigger.Probability) { return; }

            if (eventTrigger.AllowedFactions.Any())
            {
                Identifier factionId = Manager.Location.Faction?.Prefab.Identifier ?? Identifier.Empty;
                if (!eventTrigger.AllowedFactions.Contains(factionId))
                {
                    return;
                }
            }
            if (eventTrigger.AllowedLocationTypes.Any())
            {
                if (!eventTrigger.AllowedLocationTypes.Contains(Location.Type.Identifier))
                {
                    return;
                }
            }

            foreach (Requirement req in eventTrigger.Requirements)
            {
                if (!req.Matches(Location))
                {
                    return;
                }
            }

            GameMain.GameSession?.EventManager?.TryTriggerEvent(eventTrigger.EventIdentifier, eventTrigger.EventTag);
        }

        internal void ProcessResourceGeneration(PassiveGenerationParams passiveGeneration, int currentWorldStep)
        {
            if (passiveGeneration.Interval > 0 && currentWorldStep % passiveGeneration.Interval != 0) { return; }

            if (passiveGeneration.DisabledOnLocationTypes.Contains(Manager.Location.Type.Identifier))
            {
                return;
            }
            if (Rand.Range(0.0f, 1.0f) > passiveGeneration.Probability) { return; }
            if (Requirement.RequirementsMatch(Manager, passiveGeneration.Requirements, passiveGeneration.ConditionalComparison, debugLogName: $"Resource {Identifier}"))
            {
                float prevAmount = Amount;
                Amount = passiveGeneration.CalculateGeneration(Amount);
                float change = Amount - prevAmount;
                if (!passiveGeneration.AddToOtherResource.IsEmpty && Manager.GetResource(passiveGeneration.AddToOtherResource) is { } resourceToAddTo)
                {
                    resourceToAddTo.Amount += change * passiveGeneration.AddToOtherResourceRatio;
                }
                if (!passiveGeneration.TakeFromOtherResource.IsEmpty && Manager.GetResource(passiveGeneration.TakeFromOtherResource) is { } resourceToTakeFrom)
                {
                    resourceToTakeFrom.Amount -= change * passiveGeneration.TakeFromOtherResourceRatio;
                }
            }
        }

        public virtual void SetDefault(Random rand)
        {
            if (DefaultAmount == Vector2.Zero || IsReadonly)
            {
                return;
            }
            Amount = rand.Next((int)DefaultAmount.X, (int)DefaultAmount.Y);            
        }

        public void OnChanged(EconomyLogEntry log) => Manager.OutpostData.StoreResourceChangeEvent(log);

        public void OnOutpostEntered(Submarine outpost, PersistentOutpostData outpostData)
        {
            OnOutpostEnteredResourceSpecific(outpost, outpostData);
            UpdateResourceStatusIndicators();
        }

        protected virtual void OnOutpostEnteredResourceSpecific(Submarine outpost, PersistentOutpostData outpostData)
        {
        }

        public void OnOutpostExited(Submarine outpost, PersistentOutpostData outpostData, bool shouldCreateLogs = true)
        {
            OnOutpostExitedResourceSpecific(outpost, outpostData, shouldCreateLogs);
            UpdateResourceStatusIndicators();
        }

        protected virtual void OnOutpostExitedResourceSpecific(Submarine outpost, PersistentOutpostData outpostData, bool shouldCreateLogs)
        {
        }

        public string Name
            => $"Resource ({Prefab.Name})";

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; }

        public override string ToString()
            => $"{Name}";
    }

    /// <summary>
    /// A type of resource that cannot be modified directly. 
    /// Note that a resource can be <see cref="ResourcePrefab.Readonly">read only</see> even if it doesn't inherit from this class, e.g. if a LinkedResource displays the value of some readonly property.
    /// </summary>
    /// <param name="element">An XML element we're loading the resource from (= a resource from a campaign save). Note that if you're looking for values defined in the resource prefab, you should be using <see cref="ResourcePrefab.OriginalElement"/>.</param>
    internal abstract class ReadonlyResource(ResourcePrefab prefab, ResourceManager manager, XElement? element = null) : Resource(prefab, manager, element)
    {
        protected sealed override void SetAmount(float value)
        {
            // no-op
        }
    }
}
