﻿using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma
{
    abstract partial class MapEntity : Entity, ISpatialEntity
    {
        public static List<MapEntity> mapEntityList = new List<MapEntity>();

        public readonly MapEntityPrefab Prefab;

        protected List<ushort> linkedToID;
        public List<ushort> unresolvedLinkedToID;

        /// <summary>
        /// List of upgrades this item has
        /// </summary>
        protected readonly List<Upgrade> Upgrades = new List<Upgrade>();

        public readonly HashSet<Identifier> DisallowedUpgradeSet = new HashSet<Identifier>();
        
        [Editable, Serialize("", IsPropertySaveable.Yes)]
        public string DisallowedUpgrades
        {
            get { return string.Join(",", DisallowedUpgradeSet); }
            set
            {
                DisallowedUpgradeSet.Clear();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    string[] splitTags = value.Split(',');
                    foreach (string tag in splitTags)
                    {
                        string[] splitTag = tag.Trim().Split(':');
                        DisallowedUpgradeSet.Add(string.Join(":", splitTag).ToIdentifier());
                    }
                }
            }
        }

        public readonly List<MapEntity> linkedTo = new List<MapEntity>();

        protected bool flippedX, flippedY;
        public bool FlippedX { get { return flippedX; } }
        public bool FlippedY { get { return flippedY; } }

        public bool ShouldBeSaved = true;

        //the position and dimensions of the entity
        protected Rectangle rect;

        protected static readonly HashSet<MapEntity> highlightedEntities = new HashSet<MapEntity>();

        public static IEnumerable<MapEntity> HighlightedEntities => highlightedEntities;


        private bool externalHighlight = false;
        public bool ExternalHighlight
        {
            get { return externalHighlight; }
            set
            {
                if (value != externalHighlight)
                {
                    externalHighlight = value;
                    CheckIsHighlighted();
                }
            }
        }

        //is the mouse inside the rect
        private bool isHighlighted;

        public bool IsHighlighted
        {
            get { return isHighlighted || ExternalHighlight; }
            set 
            { 
                if (value != IsHighlighted)
                {
                    isHighlighted = value; 
                    CheckIsHighlighted();
                }
            }
        }

        public virtual Rectangle Rect
        {
            get { return rect; }
            set { rect = value; }
        }

        public Rectangle WorldRect
        {
            get { return Submarine == null ? rect : new Rectangle((int)(Submarine.Position.X + rect.X), (int)(Submarine.Position.Y + rect.Y), rect.Width, rect.Height); }
        }

        public virtual Sprite Sprite
        {
            get { return null; }
        }

        public virtual bool DrawBelowWater
        {
            get
            {
                return Sprite != null && SpriteDepth > 0.5f;
            }
        }

        public virtual bool DrawOverWater
        {
            get
            {
                return !DrawBelowWater;
            }
        }

        public virtual bool Linkable
        {
            get { return false; }
        }

        public IEnumerable<Identifier> AllowedLinks => Prefab == null ? Enumerable.Empty<Identifier>() : Prefab.AllowedLinks;

        public bool ResizeHorizontal
        {
            get { return Prefab != null && Prefab.ResizeHorizontal; }
        }
        public bool ResizeVertical
        {
            get { return Prefab != null && Prefab.ResizeVertical; }
        }

        //for upgrading the dimensions of the entity from xml
        [Serialize(0, IsPropertySaveable.No)]
        public int RectWidth
        {
            get { return rect.Width; }
            set
            {
                if (value <= 0) { return; }
                Rect = new Rectangle(rect.X, rect.Y, value, rect.Height);
            }
        }
        //for upgrading the dimensions of the entity from xml
        [Serialize(0, IsPropertySaveable.No)]
        public int RectHeight
        {
            get { return rect.Height; }
            set
            {
                if (value <= 0) { return; }
                Rect = new Rectangle(rect.X, rect.Y, rect.Width, value);
            }
        }

        // We could use NaN or nullables, but in this case the first is not preferable, because it needs to be checked every time the value is used.
        // Nullable on the other requires boxing that we don't want to do too often, since it generates garbage.
        public bool SpriteDepthOverrideIsSet { get; private set; }
        public float SpriteOverrideDepth => SpriteDepth;
        private float _spriteOverrideDepth = float.NaN;
        [Editable(0.001f, 0.999f, decimals: 3), Serialize(float.NaN, IsPropertySaveable.Yes)]
        public float SpriteDepth
        {
            get
            {
                if (SpriteDepthOverrideIsSet) { return _spriteOverrideDepth; }
                return Sprite != null ? Sprite.Depth : 0;
            }
            set
            {
                if (!float.IsNaN(value))
                {
                    _spriteOverrideDepth = MathHelper.Clamp(value, 0.001f, 0.999999f);
                    if (this is Item) { _spriteOverrideDepth = Math.Min(_spriteOverrideDepth, 0.9f); }
                    SpriteDepthOverrideIsSet = true;
                }
            }
        }

        [Serialize(1f, IsPropertySaveable.Yes), Editable(0.01f, 10f, DecimalCount = 3, ValueStep = 0.1f)]
        public virtual float Scale { get; set; } = 1;

        [Editable, Serialize(false, IsPropertySaveable.Yes)]
        public bool HiddenInGame
        {
            get;
            set;
        }

        public override Vector2 Position
        {
            get
            {
                Vector2 rectPos = new Vector2(
                    rect.X + rect.Width / 2.0f,
                    rect.Y - rect.Height / 2.0f);

                //if (MoveWithLevel) rectPos += Level.Loaded.Position;
                return rectPos;
            }
        }

        public override Vector2 SimPosition
        {
            get
            {
                return ConvertUnits.ToSimUnits(Position);
            }
        }

        public float SoundRange
        {
            get
            {
                if (aiTarget == null) return 0.0f;
                return aiTarget.SoundRange;
            }
            set
            {
                if (aiTarget == null) return;
                aiTarget.SoundRange = value;
            }
        }

        public float SightRange
        {
            get
            {
                if (aiTarget == null) return 0.0f;
                return aiTarget.SightRange;
            }
            set
            {
                if (aiTarget == null) return;
                aiTarget.SightRange = value;
            }
        }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool RemoveIfLinkedOutpostDoorInUse
        {
            get;
            protected set;
        } = true;

        [Serialize("", IsPropertySaveable.Yes, "Submarine editor layer")]
        public string Layer { get; set; }

        /// <summary>
        /// The index of the outpost module this entity originally spawned in (-1 if not an outpost item)
        /// </summary>
        public int OriginalModuleIndex = -1;

        public int OriginalContainerIndex = -1;

        public virtual string Name
        {
            get { return ""; }
        }

        public MapEntity(MapEntityPrefab prefab, Submarine submarine, ushort id) : base(submarine, id)
        {
            this.Prefab = prefab;
            Scale = prefab != null ? prefab.Scale : 1;
        }

        protected void ParseLinks(XElement element, IdRemap idRemap)
        {
            string linkedToString = element.GetAttributeString("linked", "");
            if (!string.IsNullOrEmpty(linkedToString))
            {
                string[] linkedToIds = linkedToString.Split(',');
                for (int i = 0; i < linkedToIds.Length; i++)
                {
                    int srcId = int.Parse(linkedToIds[i]);
                    int targetId = idRemap.GetOffsetId(srcId);
                    if (targetId <= 0)
                    {
                        unresolvedLinkedToID ??= new List<ushort>();
                        unresolvedLinkedToID.Add((ushort)srcId);
                        continue;
                    }
                    linkedToID.Add((ushort)targetId);
                }
            }
        }

        public void ResolveLinks(IdRemap childRemap)
        {
            if (unresolvedLinkedToID == null) { return; }
            for (int i = 0; i < unresolvedLinkedToID.Count; i++)
            {
                int srcId = unresolvedLinkedToID[i];
                int targetId = childRemap.GetOffsetId(srcId);
                if (targetId > 0)
                {
                    var otherEntity = FindEntityByID((ushort)targetId) as MapEntity;
                    linkedTo.Add(otherEntity);
                    if (otherEntity.Linkable && otherEntity.linkedTo != null) otherEntity.linkedTo.Add(this);
                    unresolvedLinkedToID.RemoveAt(i);
                    i--;
                }
            }
        }

        public virtual void Move(Vector2 amount, bool ignoreContacts = true)
        {
            rect.X += (int)amount.X;
            rect.Y += (int)amount.Y;
        }

        public virtual bool IsMouseOn(Vector2 position)
        {
            return (Submarine.RectContains(WorldRect, position));
        }

        public bool HasUpgrade(Identifier identifier)
        {
            return GetUpgrade(identifier) != null;
        }

        public Upgrade GetUpgrade(Identifier identifier)
        {
            return Upgrades.Find(upgrade => upgrade.Identifier == identifier);
        }

        public List<Upgrade> GetUpgrades()
        {
            return Upgrades;
        }

        public void SetUpgrade(Upgrade upgrade, bool createNetworkEvent = false)
        {
            Upgrade existingUpgrade = GetUpgrade(upgrade.Identifier);
            if (existingUpgrade != null)
            {
                existingUpgrade.Level = upgrade.Level;
                existingUpgrade.ApplyUpgrade();
                upgrade.Dispose();
            }
            else
            {
                AddUpgrade(upgrade, createNetworkEvent);
            }
            DebugConsole.Log($"Set (ID: {ID} {Prefab.Name})'s \"{upgrade.Prefab.Name}\" upgrade to level {upgrade.Level}");
        }

        /// <summary>
        /// Adds a new upgrade to the item
        /// </summary>
        public virtual bool AddUpgrade(Upgrade upgrade, bool createNetworkEvent = false)
        {
            if (!upgrade.Prefab.UpgradeCategories.Any(category => category.CanBeApplied(this, upgrade.Prefab)))
            {
                return false;
            }

            if (DisallowedUpgradeSet.Contains(upgrade.Identifier)) { return false; }

            Upgrade existingUpgrade = GetUpgrade(upgrade.Identifier);

            if (existingUpgrade != null)
            {
                existingUpgrade.Level += upgrade.Level;
                existingUpgrade.ApplyUpgrade();
                upgrade.Dispose();
            }
            else
            {
                upgrade.ApplyUpgrade();
                Upgrades.Add(upgrade);
            }

            return true;
        }

        protected virtual void CheckIsHighlighted()
        {
            if (IsHighlighted || ExternalHighlight)
            {
                highlightedEntities.Add(this);
            }
            else
            {
                highlightedEntities.Remove(this);
            }
        }

        private static readonly List<MapEntity> tempHighlightedEntities = new List<MapEntity>();
        public static void ClearHighlightedEntities()
        {
            highlightedEntities.RemoveWhere(e => e.Removed);
            tempHighlightedEntities.Clear();
            tempHighlightedEntities.AddRange(highlightedEntities);
            foreach (var entity in tempHighlightedEntities)
            {
                entity.IsHighlighted = false;
            }
        }


        public abstract MapEntity Clone();

        public static List<MapEntity> Clone(List<MapEntity> entitiesToClone)
        {
            List<MapEntity> clones = new List<MapEntity>();
            foreach (MapEntity e in entitiesToClone)
            {
                Debug.Assert(e != null);
                try
                {
                    clones.Add(e.Clone());
                }
                catch (Exception ex)
                {
                    DebugConsole.ThrowError("Cloning entity \"" + e.Name + "\" failed.", ex);
                    GameAnalyticsManager.AddErrorEventOnce(
                        "MapEntity.Clone:" + e.Name,
                        GameAnalyticsManager.ErrorSeverity.Error,
                        "Cloning entity \"" + e.Name + "\" failed (" + ex.Message + ").\n" + ex.StackTrace.CleanupStackTrace());
                    return clones;
                }
                Debug.Assert(clones.Last() != null);
            }

            Debug.Assert(clones.Count == entitiesToClone.Count);

            //clone links between the entities
            for (int i = 0; i < clones.Count; i++)
            {
                if (entitiesToClone[i].linkedTo == null) { continue; }
                foreach (MapEntity linked in entitiesToClone[i].linkedTo)
                {
                    if (!entitiesToClone.Contains(linked)) { continue; }
                    clones[i].linkedTo.Add(clones[entitiesToClone.IndexOf(linked)]);
                }
            }

            //connect clone wires to the clone items and refresh links between doors and gaps
            List<Wire> orphanedWires = new List<Wire>();
            for (int i = 0; i < clones.Count; i++)
            {
                if (clones[i] is not Item cloneItem) { continue; }

                var door = cloneItem.GetComponent<Door>();
                door?.RefreshLinkedGap();

                var cloneWire = cloneItem.GetComponent<Wire>();
                if (cloneWire == null) { continue; }

                var originalWire = ((Item)entitiesToClone[i]).GetComponent<Wire>();

                cloneWire.SetNodes(originalWire.GetNodes());

                for (int n = 0; n < 2; n++)
                {
                    if (originalWire.Connections[n] == null)
                    {
                        var disconnectedFrom = entitiesToClone.Find(e => e is Item item && (item.GetComponent<ConnectionPanel>()?.DisconnectedWires.Contains(originalWire) ?? false));
                        if (disconnectedFrom == null) { continue; }

                        int disconnectedFromIndex = entitiesToClone.IndexOf(disconnectedFrom);
                        var disconnectedFromClone = (clones[disconnectedFromIndex] as Item)?.GetComponent<ConnectionPanel>();
                        if (disconnectedFromClone == null) { continue; }

                        disconnectedFromClone.DisconnectedWires.Add(cloneWire);
                        if (cloneWire.Item.body != null) { cloneWire.Item.body.Enabled = false; }
                        cloneWire.IsActive = false;
                        continue;
                    }

                    var connectedItem = originalWire.Connections[n].Item;
                    if (connectedItem == null || !entitiesToClone.Contains(connectedItem)) { continue; }

                    //index of the item the wire is connected to
                    int itemIndex = entitiesToClone.IndexOf(connectedItem);
                    if (itemIndex < 0)
                    {
                        DebugConsole.ThrowError("Error while cloning wires - item \"" + connectedItem.Name + "\" was not found in entities to clone.");
                        GameAnalyticsManager.AddErrorEventOnce("MapEntity.Clone:ConnectedNotFound" + connectedItem.ID,
                            GameAnalyticsManager.ErrorSeverity.Error,
                            "Error while cloning wires - item \"" + connectedItem.Name + "\" was not found in entities to clone.");
                        continue;
                    }

                    //index of the connection in the connectionpanel of the target item
                    int connectionIndex = connectedItem.Connections.IndexOf(originalWire.Connections[n]);
                    if (connectionIndex < 0)
                    {
                        DebugConsole.ThrowError("Error while cloning wires - connection \"" + originalWire.Connections[n].Name + "\" was not found in connected item \"" + connectedItem.Name + "\".");
                        GameAnalyticsManager.AddErrorEventOnce("MapEntity.Clone:ConnectionNotFound" + connectedItem.ID,
                            GameAnalyticsManager.ErrorSeverity.Error,
                            "Error while cloning wires - connection \"" + originalWire.Connections[n].Name + "\" was not found in connected item \"" + connectedItem.Name + "\".");
                        continue;
                    }

                    (clones[itemIndex] as Item).Connections[connectionIndex].TryAddLink(cloneWire);
                    cloneWire.Connect((clones[itemIndex] as Item).Connections[connectionIndex], n, addNode: false);
                }

                if (originalWire.Connections.Any(c => c != null) &&
                    (cloneWire.Connections[0] == null || cloneWire.Connections[1] == null) && 
                    cloneItem.GetComponent<DockingPort>() == null)
                {
                    if (!clones.Any(c => (c as Item)?.GetComponent<ConnectionPanel>()?.DisconnectedWires.Contains(cloneWire) ?? false))
                    {
                        orphanedWires.Add(cloneWire);
                    }
                }
            }

            foreach (var orphanedWire in orphanedWires)
            {
                orphanedWire.Item.Remove();
                clones.Remove(orphanedWire.Item);
            }

            return clones;
        }

        protected void InsertToList()
        {
            if (Sprite == null)
            {
                mapEntityList.Add(this);
                return;
            }

            int i = 0;
            while (i < mapEntityList.Count)
            {
                i++;
                if (mapEntityList[i - 1]?.Prefab == Prefab)
                {
                    mapEntityList.Insert(i, this);
                    return;
                }
            }

#if CLIENT
            i = 0;
            while (i < mapEntityList.Count)
            {
                i++;
                Sprite existingSprite = mapEntityList[i - 1].Sprite;
                if (existingSprite == null) { continue; }
                if (existingSprite.Texture == this.Sprite.Texture) { break; }
            }
#endif
            mapEntityList.Insert(i, this);
        }

        /// <summary>
        /// Remove the entity from the entity list without removing links to other entities
        /// </summary>
        public virtual void ShallowRemove()
        {
            base.Remove();

            mapEntityList.Remove(this);

            if (aiTarget != null) aiTarget.Remove();
        }

        public override void Remove()
        {
            base.Remove();

            mapEntityList.Remove(this);

#if CLIENT
            Submarine.ForceRemoveFromVisibleEntities(this);
            if (SelectedList.Contains(this))
            {
                SelectedList = SelectedList.Where(e => e != this).ToHashSet();
            }
#endif

            if (aiTarget != null)
            {
                aiTarget.Remove();
                aiTarget = null;
            }

            if (linkedTo != null)
            {
                for (int i = linkedTo.Count - 1; i >= 0; i--)
                {
                    linkedTo[i].RemoveLinked(this);
                }
                linkedTo.Clear();
            }
        }

        /// <summary>
        /// Call Update() on every object in Entity.list
        /// </summary>
        public static void UpdateAll(float deltaTime, Camera cam)
        {
#if CLIENT
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
#endif
            foreach (Hull hull in Hull.HullList)
            {
                hull.Update(deltaTime, cam);
            }
#if CLIENT
            Hull.UpdateCheats(deltaTime, cam);
#endif

            foreach (Structure structure in Structure.WallList)
            {
                structure.Update(deltaTime, cam);
            }

            //update gaps in random order, because otherwise in rooms with multiple gaps
            //the water/air will always tend to flow through the first gap in the list,
            //which may lead to weird behavior like water draining down only through
            //one gap in a room even if there are several
            foreach (Gap gap in Gap.GapList.OrderBy(g => Rand.Int(int.MaxValue)))
            {
                gap.Update(deltaTime, cam);
            }

#if CLIENT
            sw.Stop();
            GameMain.PerformanceCounter.AddElapsedTicks("Update:MapEntity:Misc", sw.ElapsedTicks);
            sw.Restart();
#endif
            Powered.UpdatePower(deltaTime);
            foreach (Item item in Item.ItemList)
            {
                item.Update(deltaTime, cam);
            }

            UpdateAllProjSpecific(deltaTime);

#if CLIENT
            sw.Stop();
            GameMain.PerformanceCounter.AddElapsedTicks("Update:MapEntity:Items", sw.ElapsedTicks);
            sw.Restart();
#endif
            Spawner?.Update();
        }

        static partial void UpdateAllProjSpecific(float deltaTime);

        public virtual void Update(float deltaTime, Camera cam) { }

        /// <summary>
        /// Flip the entity horizontally
        /// </summary>
        /// <param name="relativeToSub">Should the entity be flipped across the y-axis of the sub it's inside</param>
        public virtual void FlipX(bool relativeToSub)
        {
            flippedX = !flippedX;
            if (!relativeToSub || Submarine == null) { return; }

            Vector2 relative = WorldPosition - Submarine.WorldPosition;
            relative.Y = 0.0f;
            Move(-relative * 2.0f);
        }

        /// <summary>
        /// Flip the entity vertically
        /// </summary>
        /// <param name="relativeToSub">Should the entity be flipped across the x-axis of the sub it's inside</param>
        public virtual void FlipY(bool relativeToSub)
        {
            flippedY = !flippedY;
            if (!relativeToSub || Submarine == null) { return; }

            Vector2 relative = WorldPosition - Submarine.WorldPosition;
            relative.X = 0.0f;
            Move(-relative * 2.0f);
        }

        public static List<MapEntity> LoadAll(Submarine submarine, XElement parentElement, string filePath, int idOffset)
        {
            IdRemap idRemap = new IdRemap(parentElement, idOffset);

            List<MapEntity> entities = new List<MapEntity>();
            foreach (var element in parentElement.Elements())
            {
#if CLIENT
                GameMain.GameSession?.Campaign?.ThrowIfStartRoundCancellationRequested();
#endif
                string typeName = element.Name.ToString();

                Type t;
                try
                {
                    t = Type.GetType("Barotrauma." + typeName, true, true);
                    if (t == null)
                    {
                        DebugConsole.ThrowError("Error in " + filePath + "! Could not find a entity of the type \"" + typeName + "\".");
                        continue;
                    }
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error in " + filePath + "! Could not find a entity of the type \"" + typeName + "\".", e);
                    continue;
                }

                if (t == typeof(Structure))
                {
                    string name = element.Attribute("name").Value;
                    Identifier identifier = element.GetAttributeIdentifier("identifier", "");
                    StructurePrefab structurePrefab = Structure.FindPrefab(name, identifier);
                    if (structurePrefab == null)
                    {
                        ItemPrefab itemPrefab = ItemPrefab.Find(name, identifier);
                        if (itemPrefab != null)
                        {
                            t = typeof(Item);
                        }
                    }
                }

                try
                {
                    MethodInfo loadMethod = t.GetMethod("Load", new[] { typeof(ContentXElement), typeof(Submarine), typeof(IdRemap) });
                    if (loadMethod == null)
                    {
                        DebugConsole.ThrowError("Could not find the method \"Load\" in " + t + ".");
                    }
                    else if (!loadMethod.ReturnType.IsSubclassOf(typeof(MapEntity)))
                    {
                        DebugConsole.ThrowError("Error loading entity of the type \"" + t.ToString() + "\" - load method does not return a valid map entity.");
                    }
                    else
                    {
                        object newEntity = loadMethod.Invoke(t, new object[] { element.FromPackage(null), submarine, idRemap });
                        if (newEntity != null)
                        {
                            entities.Add((MapEntity)newEntity);
                        }
                    }
                }
                catch (TargetInvocationException e)
                {
                    DebugConsole.ThrowError("Error while loading entity of the type " + t + ".", e.InnerException);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error while loading entity of the type " + t + ".", e);
                }
            }
            return entities;
        }

        /// <summary>
        /// Update the linkedTo-lists of the entities based on the linkedToID-lists
        /// Has to be done after all the entities have been loaded (an entity can't
        /// be linked to some other entity that hasn't been loaded yet)
        /// </summary>
        private bool mapLoadedCalled;
        public static void MapLoaded(List<MapEntity> entities, bool updateHulls)
        {
            InitializeLoadedLinks(entities);

            List<LinkedSubmarine> linkedSubs = new List<LinkedSubmarine>();
            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i].mapLoadedCalled || entities[i].Removed) { continue; }
                if (entities[i] is LinkedSubmarine)
                {
                    linkedSubs.Add((LinkedSubmarine)entities[i]);
                    continue;
                }

                entities[i].OnMapLoaded();
            }

            if (updateHulls)
            {
                Item.UpdateHulls();
                Gap.UpdateHulls();
            }

            entities.ForEach(e => e.mapLoadedCalled = true);

            foreach (LinkedSubmarine linkedSub in linkedSubs)
            {
                linkedSub.OnMapLoaded();
            }
        }

        public static void InitializeLoadedLinks(IEnumerable<MapEntity> entities)
        {
            foreach (MapEntity e in entities)
            {
                if (e.mapLoadedCalled) { continue; }
                if (e.linkedToID == null) { continue; }
                if (e.linkedToID.Count == 0) { continue; }

                e.linkedTo.Clear();

                foreach (ushort i in e.linkedToID)
                {
                    if (FindEntityByID(i) is MapEntity linked)
                    {
                        e.linkedTo.Add(linked);
                    }
                    else
                    {
#if DEBUG
                        DebugConsole.ThrowError($"Linking the entity \"{e.Name}\" to another entity failed. Could not find an entity with the ID \"{i}\".");
#endif
                    }
                }
                e.linkedToID.Clear();

                (e as WayPoint)?.InitializeLinks();
            }
        }

        public virtual void OnMapLoaded() { }

        public virtual XElement Save(XElement parentElement)
        {
            DebugConsole.ThrowError("Saving entity " + GetType() + " failed.");
            return null;
        }

        public void RemoveLinked(MapEntity e)
        {
            if (linkedTo == null) return;
            if (linkedTo.Contains(e)) linkedTo.Remove(e);
        }

        /// <summary>
        /// Gets all linked entities of specific type.
        /// </summary>
        public HashSet<T> GetLinkedEntities<T>(HashSet<T> list = null, int? maxDepth = null, Func<T, bool> filter = null) where T : MapEntity
        {
            list = list ?? new HashSet<T>();
            int startDepth = 0;
            GetLinkedEntitiesRecursive<T>(this, list, ref startDepth, maxDepth, filter);
            return list;
        }

        /// <summary>
        /// Gets all linked entities of specific type.
        /// </summary>
        private static void GetLinkedEntitiesRecursive<T>(MapEntity mapEntity, HashSet<T> linkedTargets, ref int depth, int? maxDepth = null, Func<T, bool> filter = null)
            where T : MapEntity
        {
            if (depth > maxDepth) { return; }
            foreach (var linkedEntity in mapEntity.linkedTo)
            {
                if (linkedEntity is T linkedTarget)
                {
                    if (!linkedTargets.Contains(linkedTarget) && (filter == null || filter(linkedTarget)))
                    {
                        linkedTargets.Add(linkedTarget);
                        depth++;
                        GetLinkedEntitiesRecursive(linkedEntity, linkedTargets, ref depth, maxDepth, filter);
                    }
                }
            }
        }
    }
}
