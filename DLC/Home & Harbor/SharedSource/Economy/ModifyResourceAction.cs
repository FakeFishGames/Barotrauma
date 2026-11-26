using Barotrauma;
using HomeAndHarbor.Resources;

namespace HomeAndHarbor;

/// <summary>
/// Modify the resources of the location is currently at, or the start or end location of the current event/mission.
/// </summary>
class ModifyResourceAction : EventAction
{
    public enum OperationType
    {
        Set,
        Multiply,
        Add,
        Subtract,
        Min,
        Max
    }

    public enum Target
    {
        /// <summary>
        /// The location the player is currently in.
        /// </summary>
        CurrentLocation,
        /// <summary>
        /// The location this mission originated from.
        /// </summary>
        StartLocation,
        /// <summary>
        /// The destination of the mission (in missions taking place in an outpost, it's always that outpost).
        /// </summary>
        EndLocation
    }

    [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the resource to modify.")]
    public Identifier Resource { get; set; }

    [Serialize(OperationType.Set, IsPropertySaveable.Yes, description: "How do you want to modify the resource (set, multiply, add, subtract, min, max)?")]
    public OperationType Operation { get; set; }

    [Serialize(0.0f, IsPropertySaveable.Yes, description: "Depending on the operation, the value you want to set the resource to, multiply it with, add to it, or subtract from it.")]
    public float Value { get; set; } = 0.0f;

    [Serialize(0, IsPropertySaveable.Yes, description: $"Duration of the effect. Only supported on {nameof(VariableResource)}.")]
    public int Duration { get; set; } = 0;

    [Serialize(Target.CurrentLocation, IsPropertySaveable.Yes)]
    public Target TargetLocation { get; set; }

    [Serialize(EconomySource.None, IsPropertySaveable.No, description: "Indicates the source of this modification, used by economy logging.")]
    public EconomySource Source { get; set; }

    [Serialize("", IsPropertySaveable.No, description: "Localization tag to use for logging this event into the economy log as an alternative to using Source.")]
    public Identifier LogTextTag { get; set; }

    private bool isFinished = false;

    public ModifyResourceAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
    {
        if (Operation == OperationType.Subtract && Value < 0)
        {
            DebugConsole.ThrowError($"Potential error in {nameof(ModifyResourceAction)} in event {GetEventDebugName()}: subtracting a negative value. This may be unintentional, as it adds to the resource instead.");
        }
    }

    public override bool IsFinished(ref string goTo)
    {
        return isFinished;
    }

    public override void Reset(bool shouldEndConversations = true)
    {
        isFinished = false;
    }

    public override void Update(float deltaTime)
    {
        if (isFinished) { return; }

        if ((LogTextTag.IsEmpty && Source is EconomySource.None) && ParentMission is not null)
        {
            DebugConsole.AddWarning($"{nameof(ModifyResourceAction)} does not have source or log text tag specified, this action will not show up in the resource log.");
        }

        if (!LogTextTag.IsEmpty && 
            !TextManager.ContainsTag(LogTextTag) && 
            !TextManager.ContainsTag(LogTextTag, TextManager.DefaultLanguage))
        {
            DebugConsole.ThrowError($"{nameof(ModifyResourceAction)} uses the text tag {LogTextTag}, but a matching text was not found in the text files.");
        }

        if (GameMain.GameSession?.Campaign is not { } campaign)
        {
            DebugConsole.AddWarning($"Potential error in event  {GetEventDebugName()}: cannot use {nameof(ModifyResourceAction)} when a campaign is not active.");
            return;
        }
        if (!ResourcePrefab.Prefabs.TryGet(Resource, out _))
        {
            DebugConsole.ThrowError($"Error in {nameof(ModifyResourceAction)},  {GetEventDebugName()}: could not find a resource with the identifier \"{Resource}\".");
            isFinished = true;
            return;
        }

        Location targetLocation = TargetLocation switch
        {
            Target.CurrentLocation => campaign.CurrentLocation,
            Target.StartLocation => StartLocation(),
            Target.EndLocation => EndLocation(),
            _ => throw new ArgumentOutOfRangeException(nameof(TargetLocation), TargetLocation, null)
        };

        var outpostData = PersistentOutpostData.GetPersistentOutpostData(targetLocation);
        if (outpostData.ResourceManager == null)
        {
            DebugConsole.AddWarning($" {nameof(ModifyResourceAction)} failed in {GetEventDebugName()}: no ResourceManager in the location {outpostData.Name}.");
            isFinished = true;
            return;
        }

        Resource? resource = outpostData.ResourceManager?.GetResource(Resource);
        if (resource == null)
        {
            DebugConsole.ThrowError($" {nameof(ModifyResourceAction)} failed in {GetEventDebugName()}: could not find the resource {Resource}.");
            isFinished = true;
            return;
        }

        Resource resourceToLog = resource.GetResourceToLogAs() ?? resource;
        float oldAmount = resourceToLog.Amount;
        if (Duration > 0)
        {
            if (resource is not VariableResource variableResource)
            {
                DebugConsole.ThrowError($" {nameof(ModifyResourceAction)} failed in {GetEventDebugName()}: {Resource} is not a {nameof(VariableResource)}, and cannot be given temporary effects with a duration.");
                isFinished = true;
                return;
            }
            variableResource.AddTemporaryEffect(Value, Operation, Duration);
        }
        else
        {
            if (resource.IsReadonly)
            {
                DebugConsole.ThrowError($" {nameof(ModifyResourceAction)} failed in {GetEventDebugName()}: {Resource} is read only.");
                isFinished = true;
                return;
            }

            switch (Operation)
            {
                case OperationType.Set:
                    SetResourceValue(Value);
                    break;
                case OperationType.Add:
                    SetResourceValue(oldAmount + Value);
                    break;
                case OperationType.Subtract:
                    SetResourceValue(oldAmount - Value);
                    break;
                case OperationType.Multiply:
                    SetResourceValue(oldAmount * Value);
                    break;
                case OperationType.Min:
                    SetResourceValue(Math.Min(oldAmount, Value));
                    break;
                case OperationType.Max:
                    SetResourceValue(Math.Max(oldAmount, Value));
                    break;
            }
        }

        Location location = ParentMission?.OriginLocation == outpostData.Location
                                ? EndLocation()
                                : StartLocation();
        LogType logType;
        switch (Source)
        {
            case EconomySource.CargoArrived:
                logType = LogType.CargoArrived;
                break;
            case EconomySource.CrewReturned:
                location = EndLocation();
                logType = LogType.CrewReturned;
                break;
            case EconomySource.EventDwellerInteraction:
                logType = LogType.None;
                if (LogTextTag.IsEmpty)
                {
                    LogTextTag = (resource.Amount > oldAmount ? "economylog.increasedbydwellerevent" : "economylog.decreasedbydwellerevent").ToIdentifier();
                }
                break;
            default:
                logType = LogType.None;
                break;
        }

        EconomyLogEntry logEntry;
        if (LogTextTag.IsEmpty)
        {
            logEntry = EconomyLogEntryGeneric.Create(logType, resourceToLog, oldAmount, resourceToLog.Amount, location);
        }
        else
        {
            logEntry = EconomyLogEntrySpecial.Create(LogTextTag, resourceToLog, oldAmount, resourceToLog.Amount, location);
        }
        outpostData.StoreResourceChangeEvent(logEntry);

        void SetResourceValue(float value)
        {
            outpostData.ResourceManager?.SetResource(Resource, value, createNetworkEvent: true);
        }

        isFinished = true;
        return;

        Location StartLocation()
            => ParentMission?.OriginLocation ?? ParentMission?.Locations[0] ?? Level.Loaded?.StartLocation ?? campaign.CurrentLocation;
        Location EndLocation()
            => ParentMission?.Destination ?? Level.Loaded?.EndLocation ?? campaign.CurrentLocation;
    }

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(ModifyResourceAction)} -> (Identifier: {Resource}, Value: {Value}, Operation: {Operation})";
    }
}
