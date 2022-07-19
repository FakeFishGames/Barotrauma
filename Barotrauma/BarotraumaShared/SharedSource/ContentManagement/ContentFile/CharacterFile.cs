using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage]
    sealed class CharacterFile : ContentFile
    {
        public CharacterFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        public override void LoadFile()
        {
            XDocument doc = XMLExtensions.TryLoadXml(Path);
            if (doc == null)
            {
                DebugConsole.ThrowError($"Loading character file failed: {Path}");
                return;
            }
            if (CharacterPrefab.Prefabs.AllPrefabs.Any(kvp => kvp.Value.Any(cf => cf?.ContentFile == this)))
            {
                DebugConsole.ThrowError($"Duplicate path: {Path}");
                return;
            }
            var mainElement = doc.Root.FromPackage(ContentPackage);
            bool isOverride = mainElement.IsOverride();
            if (isOverride) { mainElement = mainElement.FirstElement(); }
            if (!CharacterPrefab.CheckSpeciesName(mainElement, this, out Identifier n)) { return; }
            var prefab = new CharacterPrefab(mainElement, this);
            CharacterPrefab.Prefabs.Add(prefab, isOverride);
        }

        public override void UnloadFile()
        {
            CharacterPrefab.Prefabs.RemoveByFile(this);
        }

        public override void Sort()
        {
            CharacterPrefab.Prefabs.SortAll();
        }

        public override void Preload(Action<Sprite> addPreloadedSprite)
        {
#if CLIENT
            CharacterPrefab characterPrefab = CharacterPrefab.FindByFilePath(Path.Value);
            if (characterPrefab?.ConfigElement == null)
            {
                throw new Exception($"Failed to load the character config file from {Path}!");
            }
            var mainElement = characterPrefab.ConfigElement;
            mainElement.GetChildElements("sound").ForEach(e => RoundSound.Load(e));
            if (!CharacterPrefab.CheckSpeciesName(mainElement, this, out Identifier speciesName)) { return; }
            bool humanoid = mainElement.GetAttributeBool("humanoid", false);
            RagdollParams ragdollParams;
            try
            {
                if (humanoid)
                {
                    ragdollParams = RagdollParams.GetRagdollParams<HumanRagdollParams>(speciesName);
                }
                else
                {
                    ragdollParams = RagdollParams.GetRagdollParams<FishRagdollParams>(speciesName);
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Failed to preload a ragdoll file for the character \"{characterPrefab.Name}\"", e);
                return;
            }

            if (ragdollParams != null)
            {
                HashSet<string> texturePaths = new HashSet<string>
                {
                    ContentPath.FromRaw(CharacterPrefab.Prefabs[speciesName].ContentPackage, ragdollParams.Texture).Value
                };
                foreach (RagdollParams.LimbParams limb in ragdollParams.Limbs)
                {
                    if (!string.IsNullOrEmpty(limb.normalSpriteParams?.Texture)) { texturePaths.Add(limb.normalSpriteParams.Texture); }
                    if (!string.IsNullOrEmpty(limb.deformSpriteParams?.Texture)) { texturePaths.Add(limb.deformSpriteParams.Texture); }
                    if (!string.IsNullOrEmpty(limb.damagedSpriteParams?.Texture)) { texturePaths.Add(limb.damagedSpriteParams.Texture); }
                    foreach (var decorativeSprite in limb.decorativeSpriteParams)
                    {
                        if (!string.IsNullOrEmpty(decorativeSprite.Texture)) { texturePaths.Add(decorativeSprite.Texture); }
                    }
                }
                foreach (string texturePath in texturePaths)
                {
                    addPreloadedSprite(new Sprite(texturePath, Vector2.Zero));
                }
            }
#endif
        }
    }
}