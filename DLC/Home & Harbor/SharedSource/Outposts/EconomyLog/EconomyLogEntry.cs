using Barotrauma;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace HomeAndHarbor;

/// <summary>
/// We use a reference to a location (index of it in the map) to handle locations
/// in log entries, this is because we can't properly resolve the map index
/// at the time of loading from a save file.
/// 
/// (Map loads location, location loads economy logs, logs can't access map because map is still loading)
/// </summary>
internal readonly record struct LogLocationReference(int index)
{
    public readonly int MapIndex = index;

    public LocalizedString DisplayName
        => GetLocation()?.DisplayName ?? "ERROR!";

    public static readonly LogLocationReference Empty = new(index: -1);

    public bool IsEmpty
        => MapIndex < 0;

    public Location? GetLocation()
    {
        if (MapIndex < 0) { return null; }

        if (GameMain.GameSession?.Map is { } map)
        {
            if (MapIndex < map.Locations.Count)
            {
                return map.Locations[MapIndex];
            }
        }

        return null;
    }

    public static implicit operator LogLocationReference(Location? location)
        => new LogLocationReference(location?.LocationIndex ?? -1);
}

internal abstract class EconomyLogEntry( Identifier resourceIdentifier, float change, LogLocationReference relatedLocationRef, Identifier relatedResource, float relatedResourceAmount)
{
    protected readonly Identifier resourceIdentifier = resourceIdentifier;

    // we store these as strings to shave off decimals
    protected readonly float change = change;
    protected readonly string changeStr = RoundAmount(MathF.Abs(change));
    protected readonly float relatedResourceAmount = relatedResourceAmount;
    protected readonly string relatedResourceAmountStr = RoundAmount(relatedResourceAmount);

    protected readonly LogLocationReference relatedLocationRef = relatedLocationRef;
    protected readonly Identifier relatedResource = relatedResource;

    protected enum IncreasingOrDecreasing
    {
        Increasing,
        Decreasing
    }
    protected readonly IncreasingOrDecreasing incOrDec = change > 0f ? IncreasingOrDecreasing.Increasing : IncreasingOrDecreasing.Decreasing;

    public float Change => change;

    protected abstract Identifier GetTextTag();

    public LocalizedString GetMessage()
    {
        RichString resourceIconWithColor = CreateNameWithIcon(resourceIdentifier);
#if CLIENT
        resourceIconWithColor = RichString.ColorizeText(resourceIconWithColor, GUIStyle.Orange);
#endif
        (string, LocalizedString)[] tags =
        [
            ("[resourcetype]", resourceIconWithColor),
            ("[change]", TextManager.GetWithVariable($"economylog.Change.{incOrDec}", "[change]", changeStr)),
            ("[otherLocation]", relatedLocationRef.GetLocation()?.DisplayName ?? "ERROR!"),
            ("[relatedResource]", CreateNameWithIcon(relatedResource)),
            ("[relatedResourceValue]", relatedResourceAmountStr)
        ];
        Identifier textTag = GetTextTag();
        return TextManager.GetWithVariables(textTag.Value, tags).Fallback($"ERROR! {textTag}");

        static LocalizedString CreateNameWithIcon(Identifier resourceId)
        {
            return LocalizedString.JoinIfNotNullOrEmpty(" ",
                    TextManager.Get($"resourceicon.{resourceId}"), TextManager.Get($"resource.{resourceId}")
                        .Fallback(resourceId.Value));
        }
    }
    public abstract XElement ToXML();

    // Rather than adding 0 checks for every time we add a log,
    // we perform the check in this struct and then discard it when we try to store it
    /// <summary>
    /// If <see langword="true"/>, this entry is prevented from being stored.
    /// </summary>
    public abstract bool IsNoChange { get; }

    /// <summary>
    /// If <see langword="true"/>, this entry is prevented from being stored in non-debug builds, and colored purple when shown in the log.<br/>
    /// Debug-only logs are never saved to XML.
    /// </summary>
    public bool IsDebugOnly { get; init; }

    public static string RoundAmount(float amount)
            =>
#if DEBUG
            // Don't do rounding on debug builds
            true
#else
            amount < 1f
#endif
                   ? $"{amount:0.#}"
                   : ((int)MathF.Floor(amount)).ToString();

    /// <summary>Attempts to combine the numerical values of this and another log into one. Both logs may only differ in numerical values for merging to succeed.</summary>
    /// <returns>- <see langword="true"/> if the merge was successful.<br/>- <see langword="false"/> if the merge failed.</returns>
    public abstract bool TryMerge(EconomyLogEntry other, [NotNullWhen(true)] out EconomyLogEntry? combined);

    public static EconomyLogEntry? FromXML(XElement element)
    {
        Identifier name = element.NameAsIdentifier();

        if (name == nameof(EconomyLogEntryGeneric))
        {
            return EconomyLogEntryGeneric.FromXML(element);
        }
        else if (name == nameof(EconomyLogEntrySpecial))
        {
            return EconomyLogEntrySpecial.FromXML(element);
        }

        DebugConsole.ThrowError($"Unable to parse log entry {element}, '{name}' is not a valid log entry type.");
        return null;
    }

    protected static Location? GetLocationByIndex(int index)
    {
        if (GameMain.GameSession?.Map is { } map)
        {
            if (index >= 0 && index < map.Locations.Count)
            {
                return map.Locations[index];
            }
        }

        return null;
    }

    public bool MatchesResource(Resource res) => res.Identifier == resourceIdentifier;
}
