using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class ArtifactEvent : Event
    {
        private ItemPrefab itemPrefab;

        private Item item;

        private int state;

        private Vector2 spawnPos;

        private bool spawnPending;

        public bool SpawnPending => spawnPending;
        public int State => state;
        public Item Item => item;
        public Vector2 SpawnPos => spawnPos;

        public override Vector2 DebugDrawPos
        {
            get { return spawnPos; }
        }
        
        public override string ToString()
        {
            return $"ArtifactEvent ({(itemPrefab == null ? "null" : itemPrefab.Name)})";
        }

        public ArtifactEvent(EventPrefab prefab)
            : base(prefab)
        {
            if (prefab.ConfigElement.GetAttribute("itemname") != null)
            {
                DebugConsole.ThrowError("Error in ArtifactEvent - use item identifier instead of the name of the item.");
                string itemName = prefab.ConfigElement.GetAttributeString("itemname", "");
                itemPrefab = MapEntityPrefab.Find(itemName) as ItemPrefab;
                if (itemPrefab == null)
                {
                    DebugConsole.ThrowError("Error in SalvageMission: couldn't find an item prefab with the name " + itemName);
                }
            }
            else
            {
                string itemIdentifier = prefab.ConfigElement.GetAttributeString("itemidentifier", "");
                itemPrefab = MapEntityPrefab.Find(null, itemIdentifier) as ItemPrefab;
                if (itemPrefab == null)
                {
                    DebugConsole.ThrowError("Error in ArtifactEvent - couldn't find an item prefab with the identifier " + itemIdentifier);
                }
            }
        }

        public override void Init(bool affectSubImmediately)
        {
            spawnPos = Level.Loaded.GetRandomItemPos(
                (Rand.Value(Rand.RandSync.ServerAndClient) < 0.5f) ? 
                Level.PositionType.MainPath | Level.PositionType.SidePath : 
                Level.PositionType.Cave | Level.PositionType.Ruin,
                500.0f, 10000.0f, 30.0f, SpawnPosFilter);

            spawnPending = true;
        }
        
        private void SpawnItem()
        {
            item = new Item(itemPrefab, spawnPos, null);
            item.body.FarseerBody.BodyType = FarseerPhysics.BodyType.Kinematic;

            //try to find an artifact holder and place the artifact inside it
            foreach (Item it in Item.ItemList)
            {
                if (it.Submarine != null || !it.HasTag("artifactholder")) continue;

                var itemContainer = it.GetComponent<Items.Components.ItemContainer>();
                if (itemContainer == null) continue;
                if (itemContainer.Combine(item, user: null)) break; // Placement successful
            }

            if (GameSettings.CurrentConfig.VerboseLogging)
            {
                DebugConsole.NewMessage("Initialized ArtifactEvent (" + item.Name + ")", Color.White);
            }

#if SERVER
            if (GameMain.Server != null)
            {
                Entity.Spawner.CreateNetworkEvent(new EntitySpawner.SpawnEntity(item));
            }
#endif
        }

        public override void Update(float deltaTime)
        {
            if (spawnPending)
            {
                SpawnItem();
                spawnPending = false;
            }

            switch (state)
            {
                case 0:
                    if (item.ParentInventory != null) { item.body.FarseerBody.BodyType = FarseerPhysics.BodyType.Dynamic; }                
                    if (item.CurrentHull == null) return;

                    state = 1;
                    break;
                case 1:
                    if (!Submarine.MainSub.AtEndExit && !Submarine.MainSub.AtStartExit) return;

                    Finished();
                    state = 2;
                    break;
            }    
        }
    }
}
