using Barotrauma;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace HomeAndHarbor;

public enum EconomySource
{
    None,

    /// <summary>
    /// Used by <see cref="ModifyResourceAction"/> to indicate this action should be logged as a cargo delivery
    /// </summary>
    CargoArrived,

    /// <summary>
    /// Used by <see cref="PassiveGenerationParams"/> to indicate this resource action is due to
    /// the resources overspilling
    /// </summary>
    SoftCap,

    /// <summary>
    /// Used by <see cref="PassiveGenerationParams"/> to indicate that this resource generated
    /// due to the influence of another resource and that resource should be 'blamed' in the log
    /// </summary>
    RelatedResource,

    /// <summary>
    /// Used by <see cref="ModifyResourceAction"/> to indicate this action should be logged as a missions submarine crew returning safely
    /// </summary>
    CrewReturned,

    /// <summary>
    /// Used to indicate that this resource change is due to an interaction with the dwellers in some scripted event
    /// </summary>
    EventDwellerInteraction
}

public enum LogType
{
    None,
    NaturalGeneration,
    NaturalGenerationWithRelated,
    NaturalGenerationSoftCap,
    WorkstationGeneration,
    ResearchGeneration,
    CargoSent,
    CargoPaid,
    CargoArrived,
    CrewReturned,
    PlayerResourceTransfer
}

internal sealed class EconomyLogEntryGeneric(LogType msgType, Identifier resourceIdentifier, float change, LogLocationReference relatedLocationRef, Identifier relatedResource, float relatedResourceAmount)
    : EconomyLogEntry(resourceIdentifier, change, relatedLocationRef, relatedResource, relatedResourceAmount)
{
    private readonly LogType msgType = msgType;

    public override bool IsNoChange { get; } = msgType is LogType.None || MathUtils.NearlyEqual(change, 0f, 0.05f);

    public static EconomyLogEntryGeneric Create(LogType type, Resource resource, float oldValue, float newValue, Location? relatedLocation = null, Resource? relatedResource = null)
    {
        var logEntry = new EconomyLogEntryGeneric(type, resource.Identifier, newValue - oldValue, relatedLocation ?? LogLocationReference.Empty, relatedResource?.Identifier ?? Identifier.Empty, relatedResource?.Amount ?? 0f)
        {
            IsDebugOnly = resource.Hidden
        };
        LocalizedString text = TextManager.Get(logEntry.GetTextTag());
        if (text.Contains("[otherLocation]") && relatedLocation == null)
        {
            DebugConsole.ThrowError("Attempted to create a log entry with a message that mentions [otherLocation] without specifying the other location.\n" + Environment.StackTrace);
        }
        if (text.Contains("[relatedResource]") && relatedResource == null)
        {
            DebugConsole.ThrowError("Attempted to create a log entry with a message that mentions [relatedResource] without specifying the other resource.\n" + Environment.StackTrace);
        }
        return logEntry;
    }

    protected override Identifier GetTextTag() => $"EconomyLog.{msgType}.{incOrDec}".ToIdentifier();

    public override bool TryMerge(EconomyLogEntry otherEntry, [NotNullWhen(true)] out EconomyLogEntry? combined)
    {
        combined = null;
        if (otherEntry is not EconomyLogEntryGeneric other) { return false; }

        if (msgType != other.msgType) { return false; }
        if (resourceIdentifier != other.resourceIdentifier) { return false; }
        if (relatedResource != other.relatedResource) { return false; }
        if (relatedLocationRef != other.relatedLocationRef) { return false; }

        float newChange = change + other.change;
        float newRelatedAmount = relatedResourceAmount + other.relatedResourceAmount;
        combined = new EconomyLogEntryGeneric(msgType, resourceIdentifier, newChange, relatedLocationRef, relatedResource, newRelatedAmount)
        {
            IsDebugOnly = IsDebugOnly
        };
        return true;
    }

    public override XElement ToXML()
    {
        XElement element = new(nameof(EconomyLogEntryGeneric),
            new XAttribute("type", msgType),
            new XAttribute("resourceidentifier", resourceIdentifier),
            new XAttribute("change", change)
        );

        if (!relatedLocationRef.IsEmpty)
        {
            element.Add(new XAttribute("otherlocationindex", relatedLocationRef.MapIndex));
        }

        if (!relatedResource.IsEmpty)
        {
            element.Add(new XAttribute("relatedresource", relatedResource));
            element.Add(new XAttribute("relatedresourceamount", relatedResourceAmount));
        }

        return element;
    }

    public new static EconomyLogEntry? FromXML(XElement element)
    {
        LogType type = element.GetAttributeEnum("type", LogType.None);
        if (type is LogType.None)
        {
            DebugConsole.ThrowError($"Error reading {element}: Not a valid log entry (Type == None)");
            return null;
        }

        Identifier resourceIdentifier = element.GetAttributeIdentifier("resourceidentifier", Identifier.Empty);
        float change = element.GetAttributeFloat("change", 0f);

        int otherLocationIndex = element.GetAttributeInt("otherlocationindex", -1);
        Identifier relatedResource = element.GetAttributeIdentifier("relatedresource", Identifier.Empty);
        float relatedResourceAmount = element.GetAttributeFloat("relatedresourceamount", 0);

        return new EconomyLogEntryGeneric(type, resourceIdentifier, change, new LogLocationReference(otherLocationIndex), relatedResource, relatedResourceAmount);
     }
}