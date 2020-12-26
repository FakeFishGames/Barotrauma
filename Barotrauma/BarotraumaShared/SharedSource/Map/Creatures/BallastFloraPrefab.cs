using System;
using System.Collections;
using System.Xml.Linq;

namespace Barotrauma
{
    class BallastFloraPrefab : IPrefab, IDisposable
    {
        public string OriginalName { get; }
        public string Identifier { get; }
        public string FilePath { get; }
        public XElement Element { get; }

        public ContentPackage ContentPackage { get; private set; }

        public bool Disposed;

        public static readonly PrefabCollection<BallastFloraPrefab> Prefabs = new PrefabCollection<BallastFloraPrefab>();

        private BallastFloraPrefab(XElement element, string filePath, bool isOverride)
        {
            Identifier = element.GetAttributeString("identifier", "");
            OriginalName = element.GetAttributeString("name", "");
            Element = element;
            FilePath = filePath;
            Prefabs.Add(this, isOverride);
        }

        public static BallastFloraPrefab Find(string idenfitier)
        {
            return !string.IsNullOrWhiteSpace(idenfitier) ? Prefabs.Find(prefab => prefab.Identifier == idenfitier) : null;
        }

        public static void LoadAll(IEnumerable files)
        {
            DebugConsole.Log("Loading map creature prefabs: ");

            foreach (ContentFile file in files) { LoadFromFile(file); }
        }

        public static void LoadFromFile(ContentFile file)
        {
            XDocument doc = XMLExtensions.TryLoadXml(file.Path);

            var rootElement = doc?.Root;
            if (rootElement == null) { return; }

            switch (rootElement.Name.ToString().ToLowerInvariant())
            {
                case "ballastflorabehavior":
                {
                    new BallastFloraPrefab(rootElement, file.Path, false) { ContentPackage = file.ContentPackage };
                    break;
                }
                case "ballastflorabehaviors":
                {
                    foreach (var element in rootElement.Elements())
                    {
                        if (element.IsOverride())
                        {
                            XElement upgradeElement = element.GetChildElement("mapcreature");
                            if (upgradeElement != null)
                            {
                                new BallastFloraPrefab(upgradeElement, file.Path, true) { ContentPackage = file.ContentPackage };
                            }
                            else
                            {
                                DebugConsole.ThrowError($"Cannot find a map creature element from the children of the override element defined in {file.Path}");
                            }
                        }
                        else
                        {
                            if (element.Name.ToString().Equals("mapcreature", StringComparison.OrdinalIgnoreCase))
                            {
                                new BallastFloraPrefab(element, file.Path, false) { ContentPackage = file.ContentPackage };
                            }
                        }
                    }

                    break;
                }
                case "override":
                {
                    XElement mapCreatures = rootElement.GetChildElement("ballastflorabehaviors");
                    if (mapCreatures != null)
                    {
                        foreach (XElement element in mapCreatures.Elements())
                        {
                            new BallastFloraPrefab(element, file.Path, true) { ContentPackage = file.ContentPackage };
                        }
                    }

                    foreach (XElement element in rootElement.GetChildElements("ballastflorabehavior"))
                    {
                        new BallastFloraPrefab(element, file.Path, true) { ContentPackage = file.ContentPackage };
                    }

                    break;
                }
                default:
                {
                    DebugConsole.ThrowError($"Invalid XML root element: '{rootElement.Name}' in {file.Path}\n " +
                                            "Valid elements are: \"MapCreature\", \"MapCreatures\" and \"Override\".");
                    break;
                }
            }
        }

        private void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    Prefabs.Remove(this);
                }
            }

            Disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}