using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;

namespace Barotrauma.Particles
{
    class DecalManager
    {
        private Dictionary<string, List<DecalPrefab>> prefabs;

        public DecalManager()
        {
            prefabs = new Dictionary<string, List<DecalPrefab>>();
            foreach (string configFile in GameMain.Instance.GetFilesOfType(ContentType.Decals))
            {
                LoadFromFile(configFile);
            }
        }

        public void LoadFromFile(string configFile)
        {
            XDocument doc = XMLExtensions.TryLoadXml(configFile);
            if (doc == null) { return; }

            bool allowOverriding = false;
            var mainElement = doc.Root;
            if (doc.Root.IsOverride())
            {
                mainElement = doc.Root.FirstElement();
                allowOverriding = true;
            }

            foreach (XElement sourceElement in mainElement.Elements())
            {
                var element = sourceElement.IsOverride() ? sourceElement.FirstElement() : sourceElement;
                string name = element.Name.ToString().ToLowerInvariant();
                if (prefabs.ContainsKey(name))
                {
                    if (allowOverriding || sourceElement.IsOverride())
                    {
                        DebugConsole.NewMessage($"Overriding the existing decal prefab '{name}' using the file '{configFile}'", Color.Yellow);
                    }
                    else
                    {
                        DebugConsole.ThrowError($"Error in '{configFile}': Duplicate decal prefab '{name}' found in '{configFile}'! Each decal prefab must have a unique name. " +
                            "Use <override></override> tags to override prefabs.");
                        continue;
                    }

                }

                if (!prefabs.ContainsKey(name))
                {
                    prefabs.Add(name, new List<DecalPrefab>());
                }

                prefabs[name].Add(new DecalPrefab(element, configFile));
            }
        }

        public void RemoveByFile(string filePath)
        {
            List<string> keysToRemove = new List<string>();
            foreach (var kpv in prefabs)
            {
                List<DecalPrefab> prefabsToRemove = new List<DecalPrefab>();
                foreach (var prefab in kpv.Value)
                {
                    if (prefab.FilePath == filePath) { prefabsToRemove.Add(prefab); }
                }

                foreach (var prefab in prefabsToRemove)
                {
                    prefab.Sprites.ForEach(s => s.Remove());
                    kpv.Value.Remove(prefab);
                }
                if (kpv.Value.Count <= 0) { keysToRemove.Add(kpv.Key); }
            }

            foreach (string key in keysToRemove)
            {
                prefabs.Remove(key);
            }
        }

        public Decal CreateDecal(string decalName, float scale, Vector2 worldPosition, Hull hull)
        {
            prefabs.TryGetValue(decalName, out List<DecalPrefab> prefabList);
            DecalPrefab prefab = prefabList.Last();

            if (prefab == null)
            {
                DebugConsole.ThrowError("Decal prefab " + decalName + " not found!");
                return null;
            }
            
            return new Decal(prefab, scale, worldPosition, hull);
        }
    }
}
