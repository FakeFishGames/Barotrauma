#nullable enable
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class Faction
    {
        public Reputation Reputation { get; }
        public FactionPrefab Prefab { get; }

        public Faction(CampaignMetadata metadata, FactionPrefab prefab)
        {
            Prefab = prefab;
            Reputation = new Reputation(metadata, $"faction.{prefab.Identifier}", prefab.MinReputation, prefab.MaxReputation, prefab.InitialReputation);
        }
    }

    internal class FactionPrefab : IDisposable
    {
        public static List<FactionPrefab> Prefabs { get; set; }

        public string Name { get; }

        public string Description { get; }
        public string ShortDescription { get; }

        public string Identifier { get; }

        /// <summary>
        /// How low the reputation can drop on this faction
        /// </summary>
        public int MinReputation { get; }

        /// <summary>
        /// Maximum reputation level you can gain on this faction
        /// </summary>
        public int MaxReputation { get; }

        /// <summary>
        /// What reputation does this faction start with
        /// </summary>
        public int InitialReputation { get; }

#if CLIENT
        public Sprite? Icon { get; private set; }

        public Sprite? BackgroundPortrait { get; private set; }

        public Color IconColor { get; }
#endif

        private FactionPrefab(XElement element)
        {
            Identifier = element.GetAttributeString("identifier", string.Empty);
            MinReputation = element.GetAttributeInt("minreputation", -100);
            MaxReputation = element.GetAttributeInt("maxreputation", 100);
            InitialReputation = element.GetAttributeInt("initialreputation", 0);
            Name = element.GetAttributeString("name", null) ?? TextManager.Get($"faction.{Identifier}", returnNull: true) ?? "Unnamed";
            Description = element.GetAttributeString("description", null) ?? TextManager.Get($"faction.{Identifier}.description", returnNull: true) ?? "";
            ShortDescription = element.GetAttributeString("shortdescription", null) ?? TextManager.Get($"faction.{Identifier}.shortdescription", returnNull: true) ?? "";
#if CLIENT
            foreach (XElement subElement in element.Elements())
            {

                if (subElement.Name.ToString().Equals("icon", StringComparison.OrdinalIgnoreCase))
                {
                    IconColor = subElement.GetAttributeColor("color", Color.White);
                    Icon = new Sprite(subElement);
                }
                else if (subElement.Name.ToString().Equals("portrait", StringComparison.OrdinalIgnoreCase))
                {
                    BackgroundPortrait = new Sprite(subElement);
                }
            }
#endif
        }

        public static void LoadFactions()
        {
            Prefabs?.ForEach(set => set.Dispose());
            Prefabs = new List<FactionPrefab>();
            IEnumerable<ContentFile> files = GameMain.Instance.GetFilesOfType(ContentType.Factions);
            foreach (ContentFile file in files)
            {
                XDocument doc = XMLExtensions.TryLoadXml(file.Path);
                XElement? rootElement = doc?.Root;

                if (doc == null || rootElement == null) { continue; }

                if (doc.Root.IsOverride())
                {
                    Prefabs.Clear();
                    DebugConsole.NewMessage($"Overriding all factions with '{file.Path}'", Color.Yellow);
                }

                foreach (XElement element in rootElement.Elements())
                {
                    bool isOverride = element.IsOverride();
                    XElement sourceElement = isOverride ? element.FirstElement() : element;
                    string elementName = sourceElement.Name.ToString().ToLowerInvariant();
                    string identifier = sourceElement.GetAttributeString("identifier", null);

                    if (string.IsNullOrWhiteSpace(identifier))
                    {
                        DebugConsole.ThrowError($"No identifier defined for the faction config '{elementName}' in file '{file.Path}'");
                        continue;
                    }

                    var existingParams = Prefabs.Find(set => set.Identifier == identifier);
                    if (existingParams != null)
                    {
                        if (isOverride)
                        {
                            DebugConsole.NewMessage($"Overriding faction config '{identifier}' using the file '{file.Path}'", Color.Yellow);
                            Prefabs.Remove(existingParams);
                        }
                        else
                        {
                            DebugConsole.ThrowError($"Duplicate faction config: '{identifier}' defined in {elementName} of '{file.Path}'");
                            continue;
                        }
                    }

                    Prefabs.Add(new FactionPrefab(element));
                }
            }
        }

        public void Dispose()
        {
#if CLIENT
            Icon?.Remove();
            Icon = null;
#endif
            GC.SuppressFinalize(this);
        }
    }
}