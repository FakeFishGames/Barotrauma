#nullable enable

using System;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    internal partial class EntitySpawnerComponent : ItemComponent, IDrawableComponent
    {
        public enum AreaShape
        {
            Rectangle,
            Circle
        }

        [Editable, Serialize("", IsPropertySaveable.Yes, "Identifier of the item to spawn, does nothing if SpeciesName is set. Separate by comma to have multiple items spawn at random.")]
        public string? ItemIdentifier { get; set; }

        [Editable, Serialize("", IsPropertySaveable.Yes, "Species name of the creature to spawn, takes priority if ItemIdentifier is set. Separate by comma to have multiple creatures spawn at random.")]
        public string? SpeciesName { get; set; }

        [Editable, Serialize(true, IsPropertySaveable.Yes, "Only spawn if crew members are within certain area")]
        public bool OnlySpawnWhenCrewInRange { get; set; }

        [Editable, Serialize(AreaShape.Rectangle, IsPropertySaveable.Yes, "Shape of the area where crew members need to stay")]
        public AreaShape CrewAreaShape { get; set; }

        [Editable(MaxValueFloat = int.MaxValue, MinValueFloat = 0, ValueStep = 10f), Serialize("500,500", IsPropertySaveable.Yes, "Size of the rectangle where crew members need to stay. Does nothing if CrewAreaShape is set to Circle")]
        public Vector2 CrewAreaBounds { get; set; }

        [Editable(MaxValueFloat = int.MaxValue, MinValueFloat = 0, ValueStep = 10f), Serialize(500f, IsPropertySaveable.Yes, "Radius of the circle to spawn stuff in. Does nothing if CrewAreaShape is set to Rectangle")]
        public float CrewAreaRadius { get; set; }

        [Editable(MaxValueFloat = int.MaxValue, MinValueFloat = int.MinValue, ValueStep = 10f), Serialize("0,0", IsPropertySaveable.Yes, "Offset of the crew area from the center of the item")]
        public Vector2 CrewAreaOffset { get; set; }

        [Editable, Serialize(AreaShape.Rectangle, IsPropertySaveable.Yes, "Shape of the area where enemies or items are spawned")]
        public AreaShape SpawnAreaShape { get; set; }

        [Editable(MaxValueFloat = int.MaxValue, MinValueFloat = 0, ValueStep = 10f), Serialize("500,500", IsPropertySaveable.Yes, "Size of the rectangle where items or creatures will be spawned. Does nothing if SpawnAreaShape is set to Circle")]
        public Vector2 SpawnAreaBounds { get; set; }

        [Editable(MaxValueFloat = int.MaxValue, MinValueFloat = 0, ValueStep = 10f), Serialize(500f, IsPropertySaveable.Yes, "Radius of the circle where items or creatures will be spawned. Does nothing if SpawnAreaShape is set to Rectangle")]
        public float SpawnAreaRadius { get; set; }

        [Editable(MaxValueFloat = int.MaxValue, MinValueFloat = int.MinValue, ValueStep = 10f), Serialize("0,0", IsPropertySaveable.Yes, "Offset of the spawn area from the center of the item")]
        public Vector2 SpawnAreaOffset { get; set; }

        [Editable(MaxValueFloat = int.MaxValue, MinValueFloat = int.MinValue, ValueStep = 1f), Serialize("10,40", IsPropertySaveable.Yes, "Time range between spawn attempts in seconds. Set both to a negative value to disable automatic spawning.")]
        public Vector2 SpawnTimerRange { get; set; }

        [Editable(MaxValueFloat = int.MaxValue, MinValueFloat = 1f, ValueStep = 1f, DecimalCount = 0), Serialize("1,3", IsPropertySaveable.Yes, "Minumum and maximum amount of items or creatures to spawn in one attempt")]
        public Vector2 SpawnAmountRange { get; set; }

        [Editable(MinValueInt = 0, MaxValueInt = int.MaxValue), Serialize(8, IsPropertySaveable.Yes, "Total maximum amount of items or creatures that can be spawned. 0 = unrestricted.")]
        public int MaximumAmount { get; set; }

        [Editable(MinValueInt = 0, MaxValueInt = int.MaxValue), Serialize(8, IsPropertySaveable.Yes, "Amount of items or creatures in the spawn area that will prevent further items or creatures from being spawned. 0 = unrestricted.")]
        public int MaximumAmountInArea { get; set; }

        [Editable(MaxValueFloat = int.MaxValue, MinValueFloat = 0, ValueStep = 10f), Serialize(500f, IsPropertySaveable.Yes, "Inflate the circle of rectangle by this value to extend the area that counts towards the maximum amount of items or enemies to be spawned")]
        public float MaximumAmountRangePadding { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, "")]
        public bool CanSpawn { get; set; } = true;

        private float spawnTimer;
        private float? spawnTimerGoal;

        private int spawnedAmount = 0;

        public EntitySpawnerComponent(Item item, ContentXElement element) : base(item, element)
        {
            IsActive = true;
        }

        public override void OnItemLoaded()
        {
            if (!string.IsNullOrWhiteSpace(ItemIdentifier))
            {
                string[] allItems = ItemIdentifier.Split(',');
                foreach (string itemIdentifier in allItems)
                {
                    string trimmedString = itemIdentifier.Trim();

                    bool found = false;

                    foreach (ItemPrefab prefab in ItemPrefab.Prefabs)
                    {
                        if (trimmedString == prefab.Identifier)
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        DebugConsole.ThrowError($"Error loading {nameof(EntitySpawnerComponent)} - item prefab \"" + name + "\" (identifier \"" + trimmedString + "\") not found.");
                    }
                }
            }

            base.OnItemLoaded();
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            item.SendSignal(CanSpawn ? "1" : "0", "state_out");

            if (GameMain.NetworkMember is { IsClient: true }) { return; }

            float minTime = Math.Min(SpawnTimerRange.X, SpawnTimerRange.Y),
                  maxTime = Math.Max(SpawnTimerRange.X, SpawnTimerRange.Y);

            if (minTime < 0 && maxTime < 0) { return; }

            spawnTimerGoal ??= Rand.Range(minTime, maxTime, Rand.RandSync.Unsynced);

            spawnTimer += deltaTime;

            if (spawnTimer > spawnTimerGoal)
            {
                Spawn();
                spawnTimerGoal = null;
                spawnTimer = 0;
            }
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            bool isNonZero = signal.value != "0";
            bool isClient = GameMain.NetworkMember is { IsClient: true };

            switch (connection.Name)
            {
                case "set_state":
                    CanSpawn = isNonZero;
                    break;
                case "toggle" when isNonZero:
                    CanSpawn = !CanSpawn;
                    break;
                case "trigger_in" when isNonZero && !isClient:
                    Spawn();
                    break;
            }
        }

        private RectangleF GetAreaRectangle(Vector2 size, Vector2 offset, bool draw)
        {
            Vector2 pos = item.WorldPosition;
            pos += offset;
            if (draw)
            {
                pos.Y = -pos.Y;
            }

            RectangleF rect = new RectangleF(pos.X - size.X / 2f, pos.Y - size.Y / 2f, size.X, size.Y);
            return rect;
        }

        private bool CanSpawnMore()
        {
            if (!CanSpawn) { return false; }
            if (MaximumAmount > 0 && spawnedAmount >= MaximumAmount) { return false; }

            if (OnlySpawnWhenCrewInRange)
            {
                if (!Character.CharacterList.Any(c => !c.IsDead && c.IsOnPlayerTeam && IsInRange(c.WorldPosition, crewArea: true, rangePad: false)))
                {
                    return false;
                }
            }

            if (MaximumAmountInArea <= 0) { return true; }

            int amount;
            if (!string.IsNullOrWhiteSpace(SpeciesName))
            {
                amount = Character.CharacterList.Count(c => !c.IsDead && c.SpeciesName == SpeciesName && IsInRange(c.WorldPosition, crewArea: false, rangePad: true));
            }
            else if (!string.IsNullOrWhiteSpace(ItemIdentifier))
            {
                amount = Item.ItemList.Count(it => it.Submarine == item.Submarine && it.Prefab.Identifier == ItemIdentifier && IsInRange(it.WorldPosition, crewArea: false, rangePad: true));
            }
            else
            {
                return false;
            }

            return amount < MaximumAmountInArea;
        }

        private bool IsInRange(Vector2 worldPos, bool crewArea = false, bool rangePad = false)
        {
            Vector2 offset = crewArea ? CrewAreaOffset : SpawnAreaOffset;
            switch (crewArea ? CrewAreaShape : SpawnAreaShape)
            {
                case AreaShape.Circle:
                    Vector2 center = item.WorldPosition + offset;
                    float distance = (crewArea ? CrewAreaRadius : SpawnAreaRadius) + (rangePad ? MaximumAmountRangePadding : 0);
                    return Vector2.DistanceSquared(worldPos, center) < distance * distance;

                case AreaShape.Rectangle:
                    RectangleF rect = GetAreaRectangle(crewArea ? CrewAreaBounds : SpawnAreaBounds, offset, draw: false);
                    if (rangePad)
                    {
                        rect.Inflate(MaximumAmountRangePadding, MaximumAmountRangePadding);
                    }

                    return rect.Contains(worldPos);
            }

            return false;
        }

        public void Spawn()
        {
            if (!CanSpawnMore()) { return; }

            int minAmount = Math.Min((int)SpawnAmountRange.X, (int)SpawnAmountRange.Y),
                maxAmount = Math.Max((int)SpawnAmountRange.X, (int)SpawnAmountRange.Y);

            int amount = Rand.Range(minAmount, maxAmount, Rand.RandSync.Unsynced);

            Vector2 offset = SpawnAreaOffset;
            offset.Y = -offset.Y;

            switch (SpawnAreaShape)
            {
                case AreaShape.Circle:
                {
                    var (x, y) = item.WorldPosition + offset;

                    for (int i = 0; i < Math.Max(1, amount); i++)
                    {
                        float angle = Rand.Range(-MathHelper.TwoPi, MathHelper.TwoPi);
                        float distance = Rand.Range(0, SpawnAreaRadius, Rand.RandSync.Unsynced);
                        Vector2 spawnPos = new Vector2(x + distance * (float)Math.Cos(angle), y + distance * (float)Math.Sin(angle));

                        SpawnEntity(spawnPos);
                    }
                    break;
                }
                case AreaShape.Rectangle:
                {
                    RectangleF rect = GetAreaRectangle(SpawnAreaBounds, offset, draw: false);

                    for (int i = 0; i < Math.Max(1, amount); i++)
                    {
                        float minX = Math.Min(rect.Left, rect.Right),
                              maxX = Math.Max(rect.Left, rect.Right),
                              minY = Math.Min(rect.Top, rect.Bottom),
                              maxY = Math.Max(rect.Top, rect.Bottom);

                        Vector2 spawnPos = new Vector2(Rand.Range(minX, maxX, Rand.RandSync.Unsynced), Rand.Range(minY, maxY, Rand.RandSync.Unsynced));

                        SpawnEntity(spawnPos);
                    }
                    break;
                }
            }

            void SpawnEntity(Vector2 pos)
            {
                if (!string.IsNullOrWhiteSpace(SpeciesName))
                {
                    Identifier[] allSpecies = SpeciesName.Split(',').Select(s => s.Trim()).ToIdentifiers().ToArray();
                    Identifier species = allSpecies.GetRandomUnsynced();
                    Entity.Spawner?.AddCharacterToSpawnQueue(species, pos);
                    spawnedAmount++;
                }
                else if (!string.IsNullOrWhiteSpace(ItemIdentifier))
                {
                    Identifier[] allItems = ItemIdentifier.Split(',').Select(s => s.Trim()).ToIdentifiers().ToArray();
                    Identifier itemIdentifier = allItems.GetRandomUnsynced();
                    ItemPrefab? prefab = ItemPrefab.Find(null, itemIdentifier);
                    if (prefab is null) { return; }

                    if (item.Submarine is { } sub)
                    {
                        pos -= sub.Position;
                    }

                    Entity.Spawner?.AddItemToSpawnQueue(prefab, pos, item.Submarine);
                    spawnedAmount++;
                }
            }
        }
    }
}