using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;

namespace Barotrauma.Particles
{
    class DecalManager
    {
        private Dictionary<string, DecalPrefab> prefabs;

        public DecalManager()
        {
            var decalElements = new Dictionary<string, XElement>();
            foreach (string configFile in GameMain.Instance.GetFilesOfType(ContentType.Decals))
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile);
                if (doc == null) { continue; }

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
                    if (decalElements.ContainsKey(name))
                    {
                        if (allowOverriding || sourceElement.IsOverride())
                        {
                            DebugConsole.NewMessage($"Overriding the existing decal prefab '{name}' using the file '{configFile}'", Color.Yellow);
                            decalElements.Remove(name);
                        }
                        else
                        {
                            DebugConsole.ThrowError($"Error in '{configFile}': Duplicate decal prefab '{name}' found in '{configFile}'! Each decal prefab must have a unique name. " +
                                "Use <override></override> tags to override prefabs.");
                            continue;
                        }

                    }
                    decalElements.Add(name, element);
                }
            }
            //prefabs = decalElements.ToDictionary(d => d.Key, d => new DecalPrefab(d.Value));
            prefabs = new Dictionary<string, DecalPrefab>();
            foreach (var kvp in decalElements)
            {
                prefabs.Add(kvp.Key, new DecalPrefab(kvp.Value));
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
