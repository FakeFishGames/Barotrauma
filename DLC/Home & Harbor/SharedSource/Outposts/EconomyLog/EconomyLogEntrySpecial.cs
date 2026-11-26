using Barotrauma;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace HomeAndHarbor;

internal sealed class EconomyLogEntrySpecial(Identifier textTag, Identifier resourceIdentifier, float change, LogLocationReference relatedLocationRef, Identifier relatedResource, float relatedResourceAmount)
    : EconomyLogEntry(resourceIdentifier, change, relatedLocationRef, relatedResource, relatedResourceAmount)
{
    private readonly Identifier textTag = textTag;

    public override bool IsNoChange { get; } = MathUtils.NearlyEqual(change, 0, epsilon: 0.05f);

    public static EconomyLogEntrySpecial Create(Identifier tag, Resource resource, float oldValue, float newValue, Location? relatedLocation = null, Resource? relatedResource = null)
    {
        var logEntry = new EconomyLogEntrySpecial(tag, resource.Identifier, newValue - oldValue, relatedLocation ?? LogLocationReference.Empty, relatedResource?.Identifier ?? Identifier.Empty, relatedResource?.Amount ?? 0f)
        {
            IsDebugOnly = resource.Hidden
        };
        LocalizedString text = TextManager.Get(tag);
        if (text.Contains("[otherLocation]") && relatedLocation == null)
        {
            DebugConsole.ThrowError("Attempted to create a special log entry with a message that mentions [otherLocation] without specifying the other location.\n" + Environment.StackTrace);
        }
        if (text.Contains("[relatedResource]") && relatedResource == null)
        {
            DebugConsole.ThrowError("Attempted to create a special log entry with a message that mentions [relatedResource] without specifying the other resource.\n" + Environment.StackTrace);
        }
        return logEntry;
    }

    protected override Identifier GetTextTag() => textTag;

    public override bool TryMerge(EconomyLogEntry otherEntry, [NotNullWhen(true)] out EconomyLogEntry? combined)
    {
        combined = null;
        if (otherEntry is not EconomyLogEntrySpecial other) { return false; }

        if (textTag != other.textTag) { return false; }
        if (resourceIdentifier != other.resourceIdentifier) { return false; }
        if (relatedResource != other.relatedResource) { return false; }
        if (relatedLocationRef != other.relatedLocationRef) { return false; }

        float newChange = change + other.change;
        float newRelatedAmount = relatedResourceAmount + other.relatedResourceAmount;
        combined = new EconomyLogEntrySpecial(textTag, resourceIdentifier, newChange, relatedLocationRef, relatedResource, newRelatedAmount)
        {
            IsDebugOnly = IsDebugOnly
        };
        return true;
    }

    public override XElement ToXML()
    {
        XElement element = new(nameof(EconomyLogEntrySpecial),
            new XAttribute("tag", textTag),
            new XAttribute("resource", resourceIdentifier),
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

    public new static EconomyLogEntry FromXML(XElement element)
    {
        Identifier tag = element.GetAttributeIdentifier("tag", Identifier.Empty);
        Identifier resource = element.GetAttributeIdentifier("resource", Identifier.Empty);
        float change = element.GetAttributeFloat("change", 0f);

        int otherLocationIndex = element.GetAttributeInt("otherlocationindex", -1);
        Identifier relatedResource = element.GetAttributeIdentifier("relatedresource", Identifier.Empty);
        float relatedResourceAmount = element.GetAttributeFloat("relatedresourceamount", 0f);

        return new EconomyLogEntrySpecial(tag, resource, change, new LogLocationReference(otherLocationIndex), relatedResource, relatedResourceAmount);
    }
}