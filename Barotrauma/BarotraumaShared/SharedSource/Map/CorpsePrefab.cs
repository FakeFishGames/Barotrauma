using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class CorpsePrefab : IPrefab, IDisposable
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

        [Serialize("notfound", false)]
        public string Identifier { get; private set; }

        [Serialize("any", false)]
        public string Job { get; private set; }

        [Serialize(1f, false)]
        public float Commonness { get; private set; }

        [Serialize(Level.PositionType.Wreck, false)]
        public Level.PositionType SpawnPosition { get; private set; }

        public string OriginalName { get { return Identifier; } }

        public ContentPackage ContentPackage { get; private set; }

        public string FilePath { get; private set; }

        public XElement Element { get; private set; }

        public readonly Dictionary<XElement, float> ItemSets = new Dictionary<XElement, float>();

        public CorpsePrefab(XElement element, string filePath, bool allowOverriding)
        {
            FilePath = filePath;
            SerializableProperty.DeserializeProperties(this, element);
            Identifier = Identifier.ToLowerInvariant();
            Job = Job.ToLowerInvariant();
            Element = element;
            element.GetChildElements("itemset").ForEach(e => ItemSets.Add(e, e.GetAttributeFloat("commonness", 1)));
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
                    DebugConsole.ThrowError($"Invalid XML root element: '{rootElement.Name.ToString()}' in {file.Path}");
                    break;
            }
        }

        public static void RemoveByFile(string filePath)
        {
            Prefabs.RemoveByFile(filePath);
        }

        public void GiveItems(Character character, Submarine submarine)
        {
            var spawnItems = ToolBox.SelectWeightedRandom(ItemSets.Keys.ToList(), ItemSets.Values.ToList(), Rand.RandSync.Unsynced);
            foreach (XElement itemElement in spawnItems.GetChildElements("item"))
            {
                InitializeItems(character, itemElement, submarine);
            }
        }

        private void InitializeItems(Character character, XElement itemElement, Submarine submarine, Item parentItem = null)
        {
            ItemPrefab itemPrefab;
            string itemIdentifier = itemElement.GetAttributeString("identifier", "");
            itemPrefab = MapEntityPrefab.Find(null, itemIdentifier) as ItemPrefab;
            if (itemPrefab == null)
            {
                DebugConsole.ThrowError("Tried to spawn \"" + Identifier + "\" with the item \"" + itemIdentifier + "\". Matching item prefab not found.");
                return;
            }
            Item item = new Item(itemPrefab, character.Position, null);
#if SERVER
            if (GameMain.Server != null && Entity.Spawner != null)
            {
                if (GameMain.Server.EntityEventManager.UniqueEvents.Any(ev => ev.Entity == item))
                {
                    string errorMsg = $"Error while spawning job items. Item {item.Name} created network events before the spawn event had been created.";
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("Job.InitializeJobItem:EventsBeforeSpawning", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    GameMain.Server.EntityEventManager.UniqueEvents.RemoveAll(ev => ev.Entity == item);
                    GameMain.Server.EntityEventManager.Events.RemoveAll(ev => ev.Entity == item);
                }

                Entity.Spawner.CreateNetworkEvent(item, false);
            }
#endif
            if (itemElement.GetAttributeBool("equip", false))
            {
                List<InvSlotType> allowedSlots = new List<InvSlotType>(item.AllowedSlots);
                allowedSlots.Remove(InvSlotType.Any);

                character.Inventory.TryPutItem(item, null, allowedSlots);
            }
            else
            {
                character.Inventory.TryPutItem(item, null, item.AllowedSlots);
            }
            if (item.Prefab.Identifier == "idcard" || item.Prefab.Identifier == "idcardwreck")
            {
                item.AddTag("name:" + character.Name);
                item.ReplaceTag("wreck_id", Level.Loaded.GetWreckIDTag("wreck_id", submarine));
                var job = character.Info?.Job;
                if (job != null)
                {
                    item.AddTag("job:" + job.Name);
                }
            }
            foreach (WifiComponent wifiComponent in item.GetComponents<WifiComponent>())
            {
                wifiComponent.TeamID = character.TeamID;
            }
            if (parentItem != null)
            {
                parentItem.Combine(item, user: null);
            }
            foreach (XElement childItemElement in itemElement.Elements())
            {
                InitializeItems(character, childItemElement, submarine, item);
            }
        }
    }
}
