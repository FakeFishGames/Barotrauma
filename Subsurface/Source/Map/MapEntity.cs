using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.ObjectModel;

namespace Barotrauma
{
    class MapEntity : Entity
    {
        public static List<MapEntity> mapEntityList = new List<MapEntity>();
        
        //which entities have been selected for editing
        protected static List<MapEntity> selectedList = new List<MapEntity>();
        
        protected static GUIComponent editingHUD;
        
        protected static Vector2 selectionPos = Vector2.Zero;
        protected static Vector2 selectionSize = Vector2.Zero;

        protected static Vector2 startMovingPos = Vector2.Zero;
        
        protected List<ushort> linkedToID;

        //observable collection because some entities may need to be notified when the collection is modified
        public ObservableCollection<MapEntity> linkedTo;

        //protected float soundRange;
        //protected float sightRange;

        //is the mouse inside the rect
        protected bool isHighlighted;

        protected bool isSelected;

        private static bool disableSelect;
        public static bool DisableSelect
        {
            get { return disableSelect; }
            set 
            { 
                disableSelect = value; 
                if (disableSelect)
                {
                    startMovingPos = Vector2.Zero;
                    selectionSize = Vector2.Zero;
                    selectionPos = Vector2.Zero;
                }
            }
        }

        public static bool SelectedAny
        {
            get { return selectedList.Count > 0; }
        }

        public bool MoveWithLevel
        {
            get;
            set;
        }
        
        //the position and dimensions of the entity
        protected Rectangle rect;
        
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
        
        public virtual bool IsLinkable
        {
            get { return false; }
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
            set { aiTarget.SoundRange = value; }
        }

        public float SightRange
        {
            get
            {
                if (aiTarget == null) return 0.0f;
                return aiTarget.SightRange;
            }
            set { aiTarget.SightRange = value; }
        }

        public bool IsHighlighted {
            get { return isHighlighted; }
            set { isHighlighted = value; } 
        }

        public bool IsSelected
        {
            get { return isSelected; }
            set { isSelected = value; }
        }

        public virtual string Name
        {
            get { return ""; }
        }

        public MapEntity(Submarine submarine) : base(submarine) { }

        public virtual void Move(Vector2 amount) 
        {
            rect.X += (int)amount.X;
            rect.Y += (int)amount.Y;
        }

        public virtual bool Contains(Vector2 position)
        {
            return (Submarine.RectContains(WorldRect, position));
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
                if (existingSprite.Texture == this.Sprite.Texture) break;
            }

            mapEntityList.Insert(i, this);
        }

        public virtual void Draw(SpriteBatch spriteBatch, bool editing, bool back=true) {}

        public override void Remove()
        {
            base.Remove();

            mapEntityList.Remove(this);

            if (aiTarget != null) aiTarget.Remove();

            if (linkedTo != null)
            {
                foreach (MapEntity e in linkedTo)
                {
                    e.RemoveLinked(this);
                }
                linkedTo.Clear();
            }
        }

        static Dictionary<string, float> timeElapsed = new Dictionary<string, float>();

        /// <summary>
        /// Call Update() on every object in Entity.list
        /// </summary>
        public static void UpdateAll(Camera cam, float deltaTime)
        {
            foreach (Hull hull in Hull.hullList)
            {
                hull.Update(cam, deltaTime);
            }

            foreach (Gap gap in Gap.GapList)
            {
                gap.Update(cam, deltaTime);
            }

            foreach (Item item in Item.ItemList)
            {
                item.Update(cam, deltaTime);
            }

            Item.Spawner.Update();
            Item.Remover.Update();
        }

        public virtual void Update(Camera cam, float deltaTime) { }

        /// <summary>
        /// Update the selection logic in editmap-screen
        /// </summary>
        public static void UpdateSelecting(Camera cam)
        {
            if (DisableSelect)
            {
                DisableSelect = false;
                return;
            }

            foreach (MapEntity e in mapEntityList)
            {
                e.isHighlighted = false;
                e.isSelected = false;
            }

            if (GUIComponent.MouseOn != null) return;



            if (MapEntityPrefab.Selected != null)
            {
                selectionPos = Vector2.Zero;
                selectedList.Clear();
                return;
            }

            if (PlayerInput.GetKeyboardState.IsKeyDown(Keys.Delete))
            {
                foreach (MapEntity e in selectedList) e.Remove();
                selectedList.Clear();
            }

            Vector2 position = new Vector2(PlayerInput.GetMouseState.X, PlayerInput.GetMouseState.Y);
            position = cam.ScreenToWorld(position);

            MapEntity highLightedEntity = null;

            foreach (MapEntity e in mapEntityList)
            {
                if (highLightedEntity == null || e.Sprite == null ||
                    (highLightedEntity.Sprite!=null && e.Sprite.Depth < highLightedEntity.Sprite.Depth))
                {
                    if (e.Contains(position)) highLightedEntity = e;
                }
                e.isSelected = false;
            }

            if (highLightedEntity != null) highLightedEntity.isHighlighted = true;

            foreach (MapEntity e in selectedList)
            {
                e.isSelected = true;
            }

            //started moving selected entities
            if (startMovingPos != Vector2.Zero)
            { 
                if (PlayerInput.GetMouseState.LeftButton == ButtonState.Released)
                {
                    //mouse released -> move the entities to the new position of the mouse

                    Vector2 moveAmount = position - startMovingPos;
                    moveAmount = Submarine.VectorToWorldGrid(moveAmount);

                    if (moveAmount != Vector2.Zero)
                    {
                        foreach (MapEntity e in selectedList)
                            e.Move(moveAmount);
                    }

                    startMovingPos = Vector2.Zero;
                }
            }
            //started dragging a "selection rectangle"
            else if (selectionPos != Vector2.Zero)
            {
                selectionSize.X = position.X - selectionPos.X;
                selectionSize.Y = selectionPos.Y - position.Y;

                List<MapEntity> newSelection = new List<MapEntity>();// FindSelectedEntities(selectionPos, selectionSize);
                if (Math.Abs(selectionSize.X) > Submarine.GridSize.X || Math.Abs(selectionSize.Y) > Submarine.GridSize.Y)
                {
                    newSelection = FindSelectedEntities(selectionPos, selectionSize);
                }
                else
                {
                    if (highLightedEntity != null) newSelection.Add(highLightedEntity);
                }

                foreach (MapEntity e in newSelection)
                {
                    e.isHighlighted = true;
                }

                if (PlayerInput.GetMouseState.LeftButton == ButtonState.Released)
                {
                    if (PlayerInput.GetKeyboardState.IsKeyDown(Keys.LeftControl) ||
                        PlayerInput.GetKeyboardState.IsKeyDown(Keys.RightControl))
                    {
                        foreach (MapEntity e in newSelection)
                        {
                            bool alreadySelected = false;
                            
                            foreach (MapEntity e2 in selectedList)
                            {
                                if (e.ID == e2.ID) alreadySelected = true;
                            }

                            if (alreadySelected)
                                selectedList.Remove(e);
                            else
                                selectedList.Add(e);
                        }
                    }
                    else
                    {
                        selectedList = newSelection;
                    }
                    
                    selectionPos = Vector2.Zero;
                    selectionSize = Vector2.Zero;
                }
            }
            //default, not doing anything specific yet
            else
            {

                if (PlayerInput.GetMouseState.LeftButton == ButtonState.Pressed &&
                    PlayerInput.GetKeyboardState.IsKeyUp(Keys.Space))
                {
                    //if clicking a selected entity, start moving it
                    foreach (MapEntity e in selectedList)
                    {
                        if (e.Contains(position)) startMovingPos = position;
                    }

                    selectionPos = position;
                }


            }
            
        }

        /// <summary>
        /// Draw the "selection rectangle" and outlines of entities that are being dragged (if any)
        /// </summary>
        public static void DrawSelecting(SpriteBatch spriteBatch, Camera cam)
        {
            if (GUIComponent.MouseOn != null) return;

            Vector2 position = new Vector2(PlayerInput.GetMouseState.X, PlayerInput.GetMouseState.Y);
            position = cam.ScreenToWorld(position);

            if (startMovingPos != Vector2.Zero)
            {
                Vector2 moveAmount = position - startMovingPos;
                moveAmount = Submarine.VectorToWorldGrid(moveAmount);
                moveAmount.Y = -moveAmount.Y;
                //started moving the selected entities
                if (moveAmount != Vector2.Zero)
                {
                    foreach (MapEntity e in selectedList)
                        GUI.DrawRectangle(spriteBatch, 
                            new Vector2(e.WorldRect.X, -e.WorldRect.Y) + moveAmount, 
                            new Vector2(e.rect.Width, e.rect.Height), 
                            Color.DarkRed);
                    
                    //stop dragging the "selection rectangle"
                    selectionPos = Vector2.Zero;
                }
            }
            if (selectionPos != null && selectionPos != Vector2.Zero)
            {                
                GUI.DrawRectangle(spriteBatch, new Vector2(selectionPos.X, -selectionPos.Y), selectionSize, Color.DarkRed);
            }
        }

        /// <summary>
        /// Call DrawEditing() if only one entity is selected
        /// </summary>
        public static void Edit(SpriteBatch spriteBatch, Camera cam)
        {
            if (selectedList.Count == 1)
            {
                selectedList[0].DrawEditing(spriteBatch, cam);
            }
            else
            {
                editingHUD = null;
            }
        }

        public static void SelectEntity(MapEntity entity)
        {
            foreach (MapEntity e in selectedList)
            { 
                e.isSelected = false;
            }
            selectedList.Clear();

            entity.isSelected = true;
            selectedList.Add(entity);
        }
        
        public virtual void DrawEditing(SpriteBatch spriteBatch, Camera cam) {}

        public static List<MapEntity> FindMapEntities(Vector2 pos)
        {
            List<MapEntity> foundEntities = new List<MapEntity>();
            foreach (MapEntity e in mapEntityList)
            {
                if (Submarine.RectContains(e.rect, pos)) foundEntities.Add(e);
            }
            return foundEntities;
        }

        public static MapEntity FindMapEntity(Vector2 pos)
        {
            foreach (MapEntity e in mapEntityList)
            {
                if (Submarine.RectContains(e.rect, pos)) return e;
            }
            return null;
        }

        /// <summary>
        /// Find entities whose rect intersects with the "selection rect"
        /// </summary>
        public static List<MapEntity> FindSelectedEntities(Vector2 pos, Vector2 size)
        {
            List<MapEntity> foundEntities = new List<MapEntity>();

            Rectangle selectionRect = Submarine.AbsRect(pos, size);
            
            foreach (MapEntity e in mapEntityList)
            {
                if (Submarine.RectsOverlap(selectionRect, e.rect))
                    foundEntities.Add(e);
            }

            return foundEntities;
        }


        public virtual XElement Save(XDocument doc)
        {
            DebugConsole.ThrowError("Saving entity " + GetType() + " failed.");
            return null;
        }

        /// <summary>
        /// Update the linkedTo-lists of the entities based on the linkedToID-lists
        /// Has to be done after all the entities have been loaded (an entity can't
        /// be linked to some other entity that hasn't been loaded yet)
        /// </summary>
        public static void MapLoaded()
        {
            foreach (MapEntity e in mapEntityList)
            {
                if (e.linkedToID == null) continue;
                if (e.linkedToID.Count == 0) continue;

                e.linkedTo.Clear();

                foreach (ushort i in e.linkedToID)
                {
                    MapEntity linked = FindEntityByID(i) as MapEntity;

                    if (linked != null) e.linkedTo.Add(linked);
                }
            }

            for (int i = 0; i<mapEntityList.Count; i++)
            {
                MapEntity e = mapEntityList[i];

                e.OnMapLoaded();

                if (e.Submarine != null) e.Move(Submarine.HiddenSubPosition);
            }



            //mapEntityList.Sort((x, y) =>
            //{
            //    return x.Name.CompareTo(y.Name);
            //});
        }


        public virtual void OnMapLoaded() { }
        

        public void RemoveLinked(MapEntity e)
        {
            if (linkedTo == null) return;
            if (linkedTo.Contains(e)) linkedTo.Remove(e);
        }
        
    }
}
