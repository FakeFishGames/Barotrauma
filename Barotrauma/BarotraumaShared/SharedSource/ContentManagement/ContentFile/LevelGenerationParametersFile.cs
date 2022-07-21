using System;
using System.Xml.Linq;

namespace Barotrauma
{
    sealed class LevelGenerationParametersFile : ContentFile
    {
        public LevelGenerationParametersFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        private void LoadBiomes(ContentXElement element, bool isOverride)
        {
            foreach (var subElement in element.Elements())
            {
                Biome biome = new Biome(subElement, this);
                Biome.Prefabs.Add(biome, isOverride);
            }
        }

        private void LoadLevelGenerationParams(ContentXElement element, bool isOverride)
        {
            LevelGenerationParams lParams = new LevelGenerationParams(element, this);
            LevelGenerationParams.LevelParams.Add(lParams, isOverride);
        }

        private void LoadSubElements(ContentXElement element, bool overridePropagation)
        {
            foreach (var subElement in element.Elements())
            {
                if (subElement.IsOverride())
                {
                    LoadSubElements(subElement, true);
                }
                else if (subElement.NameAsIdentifier() == "clear")
                {
                    LevelGenerationParams.LevelParams.AddOverrideFile(this);
                    Biome.Prefabs.AddOverrideFile(this);
                }
                else if (subElement.NameAsIdentifier() == "biomes")
                {
                    LoadBiomes(subElement, overridePropagation);
                }
                else
                {
                    LoadLevelGenerationParams(subElement, overridePropagation);
                }
            }
        }

        public override void LoadFile()
        {
            XDocument doc = XMLExtensions.TryLoadXml(Path);
            if (doc is null) { return; }
            LoadSubElements(doc.Root.FromPackage(ContentPackage), false);
        }

        public override void UnloadFile()
        {
            LevelGenerationParams.LevelParams.RemoveByFile(this);
            Biome.Prefabs.RemoveByFile(this);
        }

        public override void Sort()
        {
            LevelGenerationParams.LevelParams.SortAll();
            Biome.Prefabs.SortAll();
        }
    }
}