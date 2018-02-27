using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    abstract partial class MapEntity : Entity
    {
        public static List<MapEntity> mapEntityList = new List<MapEntity>();

        private MapEntityPrefab prefab;

        protected List<ushort> linkedToID;

        //observable collection because some entities may need to be notified when the collection is modified
        public ObservableCollection<MapEntity> linkedTo;

        //protected float soundRange;
        //protected float sightRange;

        public bool MoveWithLevel
        {
            get;
            set;
        }
        
        //the position and dimensions of the entity
        protected Rectangle rect;
        
        //is the mouse inside the rect
        protected bool isHighlighted;

        public bool IsHighlighted
        {
            get { return isHighlighted; }
            set { isHighlighted = value; }
        }

        private static bool resizing;
        private int resizeDirX, resizeDirY;
        
        public virtual Rectangle Rect { 
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
                return Sprite != null && Sprite.Depth > 0.5f;
            }
        }

        public virtual bool DrawOverWater
        {
            get
            {
                return !DrawBelowWater;
            }
        }

        public virtual bool DrawDamageEffect
        {
            get
            {
                return false;
            }
        }
        
        public virtual bool Linkable
        {
            get { return false; }
        }

        public bool ResizeHorizontal
        {
            get { return prefab != null && prefab.ResizeHorizontal; }
        }
        public bool ResizeVertical
        {
            get { return prefab != null && prefab.ResizeVertical; }
        }

        public virtual bool SelectableInEditor
        {
            get { return true; }
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

        public virtual string Name
        {
            get { return ""; }
        }

        public MapEntity(MapEntityPrefab prefab, Submarine submarine) : base(submarine) 
        {
            this.prefab = prefab;
        }

        public virtual void Move(Vector2 amount) 
        {
            rect.X += (int)amount.X;
            rect.Y += (int)amount.Y;
        }

        public virtual bool IsMouseOn(Vector2 position)
        {
            return (Submarine.RectContains(WorldRect, position));
        }

        public virtual MapEntity Clone()
        {
            throw new NotImplementedException();
        }

        public static List<MapEntity> Clone(List<MapEntity> entitiesToClone)
        {
            List<MapEntity> clones = new List<MapEntity>();
            foreach (MapEntity e in entitiesToClone)
            {
                Debug.Assert(e != null);
                clones.Add(e.Clone());
                Debug.Assert(clones.Last() != null);
            }

            Debug.Assert(clones.Count == entitiesToClone.Count);

            //clone links between the entities
            for (int i = 0; i < clones.Count; i++)            
            {
                if (entitiesToClone[i].linkedTo == null) continue;
                foreach (MapEntity linked in entitiesToClone[i].linkedTo)
                {
                    if (!entitiesToClone.Contains(linked)) continue;

                    clones[i].linkedTo.Add(clones[entitiesToClone.IndexOf(linked)]);
                }
            }

            //connect clone wires to the clone items
            for (int i = 0; i < clones.Count; i++)
            {
                var cloneItem = clones[i] as Item;
                if (cloneItem == null) continue;

                var cloneWire = cloneItem.GetComponent<Wire>();
                if (cloneWire == null) continue;

                var originalWire = ((Item)entitiesToClone[i]).GetComponent<Wire>();

                cloneWire.SetNodes(originalWire.GetNodes());

                for (int n = 0; n < 2; n++)
                {
                    if (originalWire.Connections[n] == null) continue;

                    var connectedItem = originalWire.Connections[n].Item;
                    if (connectedItem == null) continue;
                    
                    //index of the item the wire is connected to
                    int itemIndex = entitiesToClone.IndexOf(connectedItem);
                    //index of the connection in the connectionpanel of the target item
                    int connectionIndex = connectedItem.Connections.IndexOf(originalWire.Connections[n]);
                    
                    (clones[itemIndex] as Item).Connections[connectionIndex].TryAddLink(cloneWire);
                    cloneWire.Connect((clones[itemIndex] as Item).Connections[connectionIndex], false);
                }
            }

            return clones;
        }
        
        protected void InsertToList()
        {
            int i = 0;

            if (Sprite==null)
            {
                mapEntityList.Add(this);
                return;
            }

            while (i<mapEntityList.Count)
            {
                i++;

                Sprite existingSprite = mapEntityList[i-1].Sprite;
                if (existingSprite == null) continue;
#if CLIENT
                if (existingSprite.Texture == this.Sprite.Texture) break;
#endif
            }

            mapEntityList.Insert(i, this);
        }

        public virtual bool IsVisible(Rectangle worldView)
        {
            return true;
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
            if (selectedList.Contains(this))
            {
                selectedList = selectedList.FindAll(e => e != this);
            }
#endif

            if (aiTarget != null) aiTarget.Remove();

            if (linkedTo != null)
            {
                for (int i = linkedTo.Count - 1; i >= 0; i-- )
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
            foreach (Hull hull in Hull.hullList)
            {
                hull.Update(deltaTime, cam);
            }

            foreach (Gap gap in Gap.GapList)
            {
                gap.Update(deltaTime, cam);
            }

            foreach (Item item in Item.ItemList)
            {
                item.Update(deltaTime, cam);
            }
            
            Spawner?.Update();
        }

        public virtual void Update(float deltaTime, Camera cam) { }

        public virtual void FlipX()
        {
            if (Submarine == null)
            {
                DebugConsole.ThrowError("Couldn't flip MapEntity \""+Name+"\", submarine==null");
                return;
            }

            Vector2 relative = WorldPosition - Submarine.WorldPosition;
            relative.Y = 0.0f;

            Move(-relative * 2.0f);
        }
        
        /// <summary>
        /// Update the linkedTo-lists of the entities based on the linkedToID-lists
        /// Has to be done after all the entities have been loaded (an entity can't
        /// be linked to some other entity that hasn't been loaded yet)
        /// </summary>
        public static void MapLoaded(Submarine sub)
        {
            foreach (MapEntity e in mapEntityList)
            {
                if (e.Submarine != sub) continue;
                if (e.linkedToID == null) continue;
                if (e.linkedToID.Count == 0) continue;

                e.linkedTo.Clear();

                foreach (ushort i in e.linkedToID)
                {
                    MapEntity linked = FindEntityByID(i) as MapEntity;

                    if (linked != null) e.linkedTo.Add(linked);
                }
            }

            List<LinkedSubmarine> linkedSubs = new List<LinkedSubmarine>();
            
            for (int i = 0; i<mapEntityList.Count; i++)
            {
                if (mapEntityList[i].Submarine != sub) continue;

                if (mapEntityList[i] is LinkedSubmarine)
                {
                    linkedSubs.Add((LinkedSubmarine)mapEntityList[i]);
                    continue;
                }

                mapEntityList[i].OnMapLoaded();
            }
            
            Item.UpdateHulls();
            Gap.UpdateHulls();

            foreach (LinkedSubmarine linkedSub in linkedSubs)
            {
                linkedSub.OnMapLoaded();
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
        
    }
}
