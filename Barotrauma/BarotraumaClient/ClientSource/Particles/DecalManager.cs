using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;

namespace Barotrauma.Particles
{
    class DecalManager
    {
        public PrefabCollection<DecalPrefab> Prefabs { get; private set; }

        public DecalManager()
        {
            Prefabs = new PrefabCollection<DecalPrefab>();
            foreach (ContentFile configFile in GameMain.Instance.GetFilesOfType(ContentType.Decals))
            {
                LoadFromFile(configFile);
            }
        }

        public void LoadFromFile(ContentFile configFile)
        {
            XDocument doc = XMLExtensions.TryLoadXml(configFile.Path);
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
                if (Prefabs.ContainsKey(name))
                {
                    if (allowOverriding || sourceElement.IsOverride())
                    {
                        DebugConsole.NewMessage($"Overriding the existing decal prefab '{name}' using the file '{configFile.Path}'", Color.Yellow);
                    }
                    else
                    {
                        DebugConsole.ThrowError($"Error in '{configFile.Path}': Duplicate decal prefab '{name}' found in '{configFile.Path}'! Each decal prefab must have a unique name. " +
                            "Use <override></override> tags to override prefabs.");
                        continue;
                    }

                }

                Prefabs.Add(new DecalPrefab(element, configFile), allowOverriding || sourceElement.IsOverride());
            }
        }

        public void RemoveByFile(string filePath)
        {
            Prefabs.RemoveByFile(filePath);
        }

        public Decal CreateDecal(string decalName, float scale, Vector2 worldPosition, Hull hull)
        {
            if (!Prefabs.ContainsKey(decalName.ToLowerInvariant()))
            {
                DebugConsole.ThrowError("Decal prefab " + decalName + " not found!");
                return null;
            }

            DecalPrefab prefab = Prefabs[decalName];

            return new Decal(prefab, scale, worldPosition, hull);
        }
    }
}
