using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Particles
{
    class DecalManager
    {
        private Dictionary<string, DecalPrefab> prefabs;

        public DecalManager()
        {
            prefabs = new Dictionary<string, DecalPrefab>();
            foreach (string configFile in GameMain.Instance.GetFilesOfType(ContentType.Decals))
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile);
                if (doc == null || doc.Root == null) { continue; }

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
                    if (prefabs.TryGetValue(name, out DecalPrefab duplicate))
                    {
                        if (allowOverriding || sourceElement.IsOverride())
                        {
                            DebugConsole.NewMessage($"Overriding the existing decal prefab '{name}' using the file '{configFile}'", Color.Yellow);
                            prefabs.Remove(name);
                        }
                        else
                        {
                            DebugConsole.ThrowError($"Error in '{configFile}': Duplicate decal prefab '{name}' found in '{configFile}'! Each decal prefab must have a unique name. " +
                                "Use <override></override> tags to override prefabs.");
                            continue;
                        }

                    }
                    prefabs.Add(name, new DecalPrefab(element));
                }
            }
        }

        public Decal CreateDecal(string decalName, float scale, Vector2 worldPosition, Hull hull)
        {
            DecalPrefab prefab;
            prefabs.TryGetValue(decalName, out prefab);

            if (prefab == null)
            {
                DebugConsole.ThrowError("Decal prefab " + decalName + " not found!");
                return null;
            }
            
            return new Decal(prefab, scale, worldPosition, hull);
        }
    }
}
