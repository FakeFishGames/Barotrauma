using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class OutpostGenerationParams : ISerializableEntity
    {
        public static List<OutpostGenerationParams> Params { get; private set; }

        public string Name { get; private set; }

        public string Identifier { get; private set; }

        private readonly List<string> allowedLocationTypes = new List<string>();

        /// <summary>
        /// Identifiers of the location types this outpost can appear in. If empty, can appear in all types of locations.
        /// </summary>
        public IEnumerable<string> AllowedLocationTypes 
        { 
            get { return allowedLocationTypes; } 
        }

        [Serialize(10, isSaveable: true), Editable(MinValueInt = 1, MaxValueInt = 50)]
        public int TotalModuleCount
        {
            get;
            set;
        }

        [Serialize(200.0f, isSaveable: true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1000.0f)]
        public float MinHallwayLength
        {
            get;
            set;
        }

        private readonly Dictionary<string, int> moduleCounts = new Dictionary<string, int>();

        public IEnumerable<KeyValuePair<string, int>> ModuleCounts
        {
            get { return moduleCounts; }
        }

        private readonly List<List<HumanPrefab>> humanPrefabLists = new List<List<HumanPrefab>>();

        public Dictionary<string, SerializableProperty> SerializableProperties { get; private set; }

        private OutpostGenerationParams(XElement element, string filePath)
        {
            Identifier = element.GetAttributeString("identifier", "");
            Name = element.GetAttributeString("name", Identifier);
            allowedLocationTypes = element.GetAttributeStringArray("allowedlocationtypes", Array.Empty<string>()).ToList();
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "modulecount":
                        string moduleFlag = (subElement.GetAttributeString("flag", null) ?? subElement.GetAttributeString("moduletype", "")).ToLowerInvariant();
                        moduleCounts[moduleFlag] = subElement.GetAttributeInt("count", 0);                        
                        break;
                    case "npcs":
                        humanPrefabLists.Add(new List<HumanPrefab>());
                        foreach (XElement npcElement in subElement.Elements())
                        {
                            string from = npcElement.GetAttributeString("from", string.Empty);
                            
                            // ReSharper disable once ConvertIfStatementToConditionalTernaryExpression
                            if (!string.IsNullOrWhiteSpace(from))
                            {
                                HumanPrefab prefab = NPCSet.Get(from, npcElement.GetAttributeString("identifier", string.Empty));
                                if (prefab != null)
                                {
                                    humanPrefabLists.Last().Add(prefab);
                                }
                            }
                            else
                            {
                                humanPrefabLists.Last().Add(new HumanPrefab(npcElement, filePath));
                            }
                        }
                        break;
                }
            }
        }

        public int GetModuleCount(string moduleFlag)
        {
            if (string.IsNullOrEmpty(moduleFlag) || moduleFlag == "none") { return int.MaxValue; }
            return moduleCounts.ContainsKey(moduleFlag) ? moduleCounts[moduleFlag] : 0;
        }

        public void SetModuleCount(string moduleFlag, int count)
        {
            if (string.IsNullOrEmpty(moduleFlag) || moduleFlag == "none") { return; }
            if (count <= 0)
            {
                moduleCounts.Remove(moduleFlag);
            }
            else
            {
                moduleCounts[moduleFlag]  = count;
            }
        }

        public void SetAllowedLocationTypes(IEnumerable<string> allowedLocationTypes)
        {
            this.allowedLocationTypes.Clear();
            foreach (string locationType in allowedLocationTypes)
            {
                if (locationType.Equals("any", StringComparison.OrdinalIgnoreCase)) { continue; }
                this.allowedLocationTypes.Add(locationType);
            }
        }

        public IEnumerable<HumanPrefab> GetHumanPrefabs(Rand.RandSync randSync)
        {
            if (humanPrefabLists == null || !humanPrefabLists.Any()) { return Enumerable.Empty<HumanPrefab>(); }
            return humanPrefabLists.GetRandom(randSync);
        }

        public static void LoadPresets()
        {
            Params = new List<OutpostGenerationParams>();
            var files = GameMain.Instance.GetFilesOfType(ContentType.OutpostConfig);
            foreach (ContentFile file in files)
            {
                XDocument doc = XMLExtensions.TryLoadXml(file.Path);
                if (doc?.Root == null) { continue; }
                var mainElement = doc.Root;
                if (doc.Root.IsOverride())
                {
                    Params.Clear();
                    DebugConsole.NewMessage($"Overriding all outpost generation parameters with '{file.Path}'", Color.Yellow);
                }

                foreach (XElement element in mainElement.Elements())
                {
                    bool isOverride = element.IsOverride();
                    XElement sourceElement = isOverride ? element.FirstElement() : element;
                    string elementName = sourceElement.Name.ToString().ToLowerInvariant();
                    string identifier = sourceElement.GetAttributeString("identifier", null);
    
                    if (string.IsNullOrWhiteSpace(identifier))
                    {
                        DebugConsole.ThrowError($"No identifier defined for the outpost config '{elementName}' in file '{file.Path}'");
                        continue;
                    }
                    var existingParams = Params.Find(p => p.Identifier == identifier);
                    if (existingParams != null)
                    {
                        if (isOverride)
                        {
                            DebugConsole.NewMessage($"Overriding outpost config '{identifier}' using the file '{file.Path}'", Color.Yellow);
                            Params.Remove(existingParams);
                        }
                        else
                        {
                            DebugConsole.ThrowError($"Duplicate outpost config: '{identifier}' defined in {elementName} of '{file.Path}'");
                            continue;
                        }
                    }
                    Params.Add(new OutpostGenerationParams(element, file.Path));
                }
            }
        }
    }
}
