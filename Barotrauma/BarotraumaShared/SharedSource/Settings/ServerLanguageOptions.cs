using System;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma;

static class ServerLanguageOptions
{
    public readonly record struct LanguageOption(
        string Label,
        LanguageIdentifier Identifier,
        ImmutableArray<LanguageIdentifier> MapsFrom)
    {
        public static LanguageOption FromXElement(XElement element)
            => new LanguageOption(
                Label:
                    element.GetAttributeString("label", ""),
                Identifier:
                    element.GetAttributeIdentifier("identifier", LanguageIdentifier.None.Value)
                    .ToLanguageIdentifier(),
                MapsFrom:
                    element.GetAttributeIdentifierArray("mapsFrom", Array.Empty<Identifier>())
                    .Select(id => id.ToLanguageIdentifier()).ToImmutableArray());
    }

    public static readonly ImmutableArray<LanguageOption> Options;

    static ServerLanguageOptions()
    {
        var languageOptionElements
            = XMLExtensions.TryLoadXml("Data/languageoptions.xml")?.Root?.Elements()
              ?? Enumerable.Empty<XElement>();
        Options = languageOptionElements
            // Convert the XElements into LanguageOptions immediately since they can be worked with more directly
            .Select(LanguageOption.FromXElement)
            // Remove options with duplicate identifiers
            .DistinctBy(p => p.Identifier)
            // Remove options where the label is empty or the identifier is missing
            .Where(p => !p.Label.IsNullOrWhiteSpace() && p.Identifier != LanguageIdentifier.None)
            // Sort the options based on the lexicographical order of the labels
            .OrderBy(p => p.Label)
            .ToImmutableArray();
    }

    public static LanguageIdentifier PickLanguage(LanguageIdentifier id)
    {
        if (id == LanguageIdentifier.None)
        {
            id = GameSettings.CurrentConfig.Language;
        }

        foreach (var (_, identifier, mapsFrom) in Options)
        {
            if (id == identifier || mapsFrom.Contains(id))
            {
                return identifier;
            }
        }

        return TextManager.DefaultLanguage;
    }
}
