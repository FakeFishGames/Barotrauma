using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;

namespace Barotrauma.Particles
{
    class DecalManager
    {
        private PrefabCollection<DecalPrefab> prefabs;

        public DecalManager()
        {
            prefabs = new PrefabCollection<DecalPrefab>();
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

                prefabs.Add(new DecalPrefab(element, configFile), allowOverriding && sourceElement.IsOverride());
            }
        }

        public void RemoveByFile(string filePath)
        {
            prefabs.RemoveByFile(filePath);
        }

        public Decal CreateDecal(string decalName, float scale, Vector2 worldPosition, Hull hull)
        {
            if (prefabs.ContainsKey(decalName))
            {
                DebugConsole.ThrowError("Decal prefab " + decalName + " not found!");
                return null;
            }

            DecalPrefab prefab = prefabs[decalName];

            return new Decal(prefab, scale, worldPosition, hull);
        }
    }
}
