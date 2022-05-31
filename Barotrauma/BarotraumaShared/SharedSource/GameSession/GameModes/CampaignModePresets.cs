using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Xml.Linq;

namespace Barotrauma
{
    internal static class CampaignModePresets
    {
        public static readonly ImmutableArray<CampaignSettings> List;
        public static readonly ImmutableDictionary<Identifier, CampaignSettingDefinitions> Definitions;

        private static readonly string fileListPath = Path.Combine("Data", "campaignsettings.xml");

        static CampaignModePresets()
        {
            if (!File.Exists(fileListPath) || !(XMLExtensions.TryLoadXml(fileListPath)?.Root is { } docRoot))
            {
                List = ImmutableArray<CampaignSettings>.Empty;
                return;
            }

            List<CampaignSettings> list = new List<CampaignSettings>();
            Dictionary<Identifier, CampaignSettingDefinitions> definitions = new Dictionary<Identifier, CampaignSettingDefinitions>();

            foreach (XElement element in docRoot.Elements())
            {
                Identifier name = element.NameAsIdentifier();

                if (name == CampaignSettings.LowerCaseSaveElementName)
                {
                    list.Add(new CampaignSettings(element));
                }
                else if (name == nameof(CampaignSettingDefinitions))
                {
                    foreach (XElement subElement in element.Elements())
                    {
                        definitions.Add(subElement.NameAsIdentifier(), new CampaignSettingDefinitions(subElement));
                    }
                }
            }

            List = list.ToImmutableArray();
            Definitions = definitions.ToImmutableDictionary();
        }
    }

    internal readonly struct CampaignSettingDefinitions
    {
        // Definitely not the best way to do this
        private readonly ImmutableDictionary<Identifier, Either<int, float>> values;

        public CampaignSettingDefinitions(XElement element)
        {
            var definitions = new Dictionary<Identifier, Either<int, float>>();
            foreach (XAttribute attribute in element.Attributes())
            {
                Identifier name = attribute.NameAsIdentifier();
                if (attribute.Value.Contains('.'))
                {
                    definitions.Add(name, element.GetAttributeFloat(name.Value, 0));
                }
                else
                {
                    definitions.Add(name, element.GetAttributeInt(name.Value, 0));
                }
            }

            values = definitions.ToImmutableDictionary();
        }

        public float GetFloat(Identifier identifier)
        {
            return values.TryGetValue(identifier, out Either<int, float> value) && value.TryGet(out float range) ? range : 0.0f;
        }

        public int GetInt(Identifier identifier)
        {
            return values.TryGetValue(identifier, out Either<int, float> value) && value.TryGet(out int integer) ? integer : 0;
        }
    }
}