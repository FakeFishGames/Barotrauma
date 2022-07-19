using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public class GrimeSprite : Prefab
    {
        public GrimeSprite(Sprite spr, DecalsFile file, int indexInFile) : base(file, $"{nameof(GrimeSprite)}{indexInFile}".ToIdentifier())
        {
            Sprite = spr;
            IndexInFile = indexInFile;
        }

        public readonly int IndexInFile;
        
        public Sprite Sprite { get; private set; }

        public override void Dispose()
        {
            Sprite?.Remove(); Sprite = null;
        }
    }

    static class DecalManager
    {
        public static readonly PrefabCollection<DecalPrefab> Prefabs = new PrefabCollection<DecalPrefab>();

        public static int GrimeSpriteCount { get; private set; } = 0;

        public static readonly PrefabCollection<GrimeSprite> GrimeSprites = new PrefabCollection<GrimeSprite>(
            onAdd: (sprite, b) => GrimeSpriteCount = Math.Max(GrimeSpriteCount, sprite.IndexInFile + 1),
            onRemove: (s) =>
                GrimeSpriteCount = GrimeSprites.AllPrefabs
                    .SelectMany(kvp => kvp.Value)
                    .Where(p => p != s).Select(p => p.IndexInFile + 1).MaxOrNull() ?? 0,
            onSort: null, onAddOverrideFile: null, onRemoveOverrideFile: null);

        public static void LoadFromFile(DecalsFile configFile)
        {
            XDocument doc = XMLExtensions.TryLoadXml(configFile.Path);
            if (doc == null) { return; }

            bool allowOverriding = false;
            var mainElement = doc.Root.FromPackage(configFile.ContentPackage);
            if (doc.Root.IsOverride())
            {
                mainElement = mainElement.FirstElement();
                allowOverriding = true;
            }

            int grimeIndex = 0;
            foreach (var sourceElement in mainElement.Elements())
            {
                var element = sourceElement.IsOverride() ? sourceElement.FirstElement() : sourceElement;
                bool isOverride = allowOverriding || sourceElement.IsOverride();
                string name = element.Name.ToString().ToLowerInvariant();

                switch (name)
                {
                    case "grime":
                        GrimeSprites.Add(new GrimeSprite(new Sprite(element), configFile, grimeIndex), isOverride);
                        grimeIndex++;
                        break;
                    default:
                        var prefab = new DecalPrefab(element, configFile);
                        Prefabs.Add(prefab, isOverride);
                        break;
                }
            }
        }

        public static void RemoveByFile(DecalsFile configFile)
        {
            Prefabs.RemoveByFile(configFile);
            GrimeSprites.RemoveByFile(configFile);
        }

        public static void SortAll()
        {
            Prefabs.SortAll();
            GrimeSprites.SortAll();
        }

        public static Decal CreateDecal(string decalName, float scale, Vector2 worldPosition, Hull hull, int? spriteIndex = null)
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
