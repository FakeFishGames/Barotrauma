using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Xml.Linq;

namespace Barotrauma
{
    internal static class CampaignModePresets
    {
        public static readonly ImmutableArray<CampaignSettings> List;
        private static readonly ImmutableDictionary<Identifier, CampaignSettingDefinitions> definitions;

        private static readonly string fileListPath = Path.Combine("Data", "campaignsettings.xml");

        static CampaignModePresets()
        {
            if (!File.Exists(fileListPath) || !(XMLExtensions.TryLoadXml(fileListPath)?.Root is { } docRoot))
            {
                List = ImmutableArray<CampaignSettings>.Empty;
                return;
            }

            List<CampaignSettings> presetList = new List<CampaignSettings>();
            Dictionary<Identifier, CampaignSettingDefinitions> tempDefinitions = new Dictionary<Identifier, CampaignSettingDefinitions>();

            foreach (XElement element in docRoot.Elements())
            {
                Identifier name = element.NameAsIdentifier();

                // The campaign setting presets
                if (name == CampaignSettings.LowerCaseSaveElementName)
                {
                    presetList.Add(new CampaignSettings(element));
                }
                // All the definitions for the setting value options
                else if (name == nameof(CampaignSettingDefinitions))
                {
                    // The single definitions that the settings may refer to (eg. PatdownProbabilityMin)
                    foreach (XElement subElement in element.Elements())
                    {
                        tempDefinitions.Add(subElement.NameAsIdentifier(), new CampaignSettingDefinitions(subElement));
                    }
                }
            }

            List = presetList.ToImmutableArray();
            definitions = tempDefinitions.ToImmutableDictionary();
        }

        public static bool TryGetAttribute(Identifier propertyName, Identifier attributeName, out XAttribute attribute)
        {
            attribute = null;
            if (definitions.TryGetValue(propertyName, out CampaignSettingDefinitions definition))
            {
                if (definition.Attributes.TryGetValue(attributeName, out XAttribute att))
                {
                    attribute = att;
                    return true;
                }
            }
            return false;
        }
    }

    internal readonly struct CampaignSettingDefinitions
    {
        public readonly ImmutableDictionary<Identifier, XAttribute> Attributes;

        public CampaignSettingDefinitions(XElement element)
        {
            Attributes = element.Attributes().ToImmutableDictionary(
                a => a.NameAsIdentifier(),
                a => a
            );
        }
    }
}