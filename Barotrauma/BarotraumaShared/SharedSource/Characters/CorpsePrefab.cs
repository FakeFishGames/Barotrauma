using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class CorpsePrefab : HumanPrefab, IPrefab, IDisposable
    {
        public static readonly PrefabCollection<CorpsePrefab> Prefabs = new PrefabCollection<CorpsePrefab>();

        private bool disposed = false;
        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            Prefabs.Remove(this);
        }

        public static CorpsePrefab Get(string identifier)
        {
            if (Prefabs == null)
            {
                DebugConsole.ThrowError("Issue in the code execution order: job prefabs not loaded.");
                return null;
            }
            if (Prefabs.ContainsKey(identifier))
            {
                return Prefabs[identifier];
            }
            else
            {
                DebugConsole.ThrowError("Couldn't find a job prefab with the given identifier: " + identifier);
                return null;
            }
        }

        [Serialize(Level.PositionType.Wreck, false)]
        public Level.PositionType SpawnPosition { get; private set; }

        public ContentPackage ContentPackage { get; private set; }

        public CorpsePrefab(XElement element, string filePath, bool allowOverriding) : base(element, filePath)
        {
            Prefabs.Add(this, allowOverriding);
        }

        public static CorpsePrefab Random(Rand.RandSync sync = Rand.RandSync.Unsynced) => Prefabs.GetRandom(sync);

        public static void LoadAll(IEnumerable<ContentFile> files)
        {
            foreach (ContentFile file in files)
            {
                LoadFromFile(file);
            }
        }

        public static void LoadFromFile(ContentFile file)
        {
            DebugConsole.Log("*** " + file.Path + " ***");
            RemoveByFile(file.Path);

            XDocument doc = XMLExtensions.TryLoadXml(file.Path);
            if (doc == null) { return; }

            var rootElement = doc.Root;
            switch (rootElement.Name.ToString().ToLowerInvariant())
            {
                case "corpse":
                    new CorpsePrefab(rootElement, file.Path, false)
                    {
                        ContentPackage = file.ContentPackage
                    };
                    break;
                case "corpses":
                    foreach (var element in rootElement.Elements())
                    {
                        if (element.IsOverride())
                        {
                            var itemElement = element.GetChildElement("item");
                            if (itemElement != null)
                            {
                                new CorpsePrefab(itemElement, file.Path, true)
                                {
                                    ContentPackage = file.ContentPackage
                                };
                            }
                            else
                            {
                                DebugConsole.ThrowError($"Cannot find an item element from the children of the override element defined in {file.Path}");
                            }
                        }
                        else
                        {
                            new CorpsePrefab(element, file.Path, false)
                            {
                                ContentPackage = file.ContentPackage
                            };
                        }
                    }
                    break;
                case "override":
                    var corpses = rootElement.GetChildElement("corpses");
                    if (corpses != null)
                    {
                        foreach (var element in corpses.Elements())
                        {
                            new CorpsePrefab(element, file.Path, true)
                            {
                                ContentPackage = file.ContentPackage,
                            };
                        }
                    }
                    foreach (var element in rootElement.GetChildElements("corpse"))
                    {
                        new CorpsePrefab(element, file.Path, true)
                        {
                            ContentPackage = file.ContentPackage
                        };
                    }
                    break;
                default:
                    DebugConsole.ThrowError($"Invalid XML root element: '{rootElement.Name}' in {file.Path}");
                    break;
            }
        }

        public static void RemoveByFile(string filePath)
        {
            Prefabs.RemoveByFile(filePath);
        }       
    }
}
