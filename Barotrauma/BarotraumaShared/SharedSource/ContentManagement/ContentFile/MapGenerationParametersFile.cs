using System.Xml.Linq;

namespace Barotrauma
{
    sealed class MapGenerationParametersFile : ContentFile
    {
        public MapGenerationParametersFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        public override void LoadFile()
        {
            XDocument doc = XMLExtensions.TryLoadXml(Path);
            if (doc == null)
            {
                DebugConsole.ThrowError($"Loading map generation parameters file failed: {Path}");
                return;
            }
            var mainElement = doc.Root.FromContent(Path);
            bool isOverride = mainElement.IsOverride();
            if (isOverride) { mainElement = mainElement.FirstElement(); }
            var prefab = new MapGenerationParams(mainElement, this);
            MapGenerationParams.Params.Add(prefab, isOverride);
        }

        public override void UnloadFile()
        {
            MapGenerationParams.Params.RemoveByFile(this);
        }

        public override void Sort()
        {
            MapGenerationParams.Params.Sort();
        }
    }
}