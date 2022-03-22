using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Barotrauma
{
    class DecalManager
    {
        public PrefabCollection<DecalPrefab> Prefabs { get; private set; }

        public readonly List<Sprite> GrimeSprites = new List<Sprite>();
        private Dictionary<string, List<Sprite>> grimeSpritesByFile = new Dictionary<string, List<Sprite>>();

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

            if (grimeSpritesByFile.ContainsKey(configFile.Path))
            {
                foreach (Sprite sprite in grimeSpritesByFile[configFile.Path])
                {
                    sprite.Remove();
                    GrimeSprites.Remove(sprite);
                }
                grimeSpritesByFile.Remove(configFile.Path);
            }

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

                switch (name)
                {
                    case "grime":
                        if (!grimeSpritesByFile.ContainsKey(configFile.Path))
                        {
                            grimeSpritesByFile.Add(configFile.Path, new List<Sprite>());
                        }
                        var grimeSprite = new Sprite(element);
                        GrimeSprites.Add(grimeSprite);
                        grimeSpritesByFile[configFile.Path].Add(grimeSprite);
                        break;
                    default:
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
                        var newPrefab = new DecalPrefab(element, configFile);
                        Prefabs.Add(newPrefab, allowOverriding || sourceElement.IsOverride());
                        newPrefab.CalculatePrefabUIntIdentifier(Prefabs);
                        break;
                }
            }
        }

        public void RemoveByFile(string filePath)
        {
            Prefabs.RemoveByFile(filePath);
            if (grimeSpritesByFile.ContainsKey(filePath))
            {
                foreach (Sprite sprite in grimeSpritesByFile[filePath])
                {
                    sprite.Remove();
                    GrimeSprites.Remove(sprite);
                }
                grimeSpritesByFile.Remove(filePath);
            }
        }

        public Decal CreateDecal(string decalName, float scale, Vector2 worldPosition, Hull hull, int? spriteIndex = null)
        {
            string lowerCaseDecalName = decalName.ToLowerInvariant();
            if (!Prefabs.ContainsKey(lowerCaseDecalName))
            {
                DebugConsole.ThrowError("Decal prefab " + decalName + " not found!");
                return null;
            }

            DecalPrefab prefab = Prefabs[lowerCaseDecalName];

            return new Decal(prefab, scale, worldPosition, hull, spriteIndex);
        }
    }
}
