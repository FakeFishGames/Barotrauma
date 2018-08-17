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
                if (doc == null || doc.Root == null) continue;

                foreach (XElement element in doc.Root.Elements())
                {
                    if (prefabs.ContainsKey(element.Name.ToString()))
                    {
                        DebugConsole.ThrowError("Error in " + configFile + "! Each decal prefab must have a unique name.");
                        continue;
                    }
                    prefabs.Add(element.Name.ToString(), new DecalPrefab(element));
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
