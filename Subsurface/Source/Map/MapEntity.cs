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

        private MapEntityPrefab prefab;

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
        
        public virtual bool IsLinkable
        {
            get { return false; }
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

        protected bool ResizeHorizontal
        {
            get { return prefab != null && prefab.ResizeHorizontal; }
        }
        protected bool ResizeVertical
        {
            get { return prefab != null && prefab.ResizeVertical; }
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
                for (int i = linkedTo.Count - 1; i >= 0; i-- )
                {
                    linkedTo[i].RemoveLinked(this);
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
            if (resizing)
            {
                if (selectedList.Count == 0) resizing = false;
                return;
            }

            foreach (MapEntity e in mapEntityList)
            {
                e.isHighlighted = false;
                e.isSelected = false;
            }

            if (DisableSelect)
            {
                DisableSelect = false;
                return;
            }

            if (GUIComponent.MouseOn != null || !PlayerInput.MouseInsideWindow) return;
            
            if (MapEntityPrefab.Selected != null)
            {
                selectionPos = Vector2.Zero;
                selectedList.Clear();
                return;
            }

            if (PlayerInput.KeyDown(Keys.Delete))
            {
                foreach (MapEntity e in selectedList) e.Remove();
                selectedList.Clear();
            }

            Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);

            MapEntity highLightedEntity = null;

            if (startMovingPos == Vector2.Zero)
            {
                foreach (MapEntity e in mapEntityList)
                {
                    if (!e.SelectableInEditor) continue;

                    if (highLightedEntity == null || e.Sprite == null ||
                        (highLightedEntity.Sprite != null && e.Sprite.Depth < highLightedEntity.Sprite.Depth))
                    {
                        if (e.IsMouseOn(position)) highLightedEntity = e;
                    }
                    e.isSelected = false;
                }

                if (highLightedEntity != null) highLightedEntity.isHighlighted = true;

            }

            foreach (MapEntity e in selectedList)
            {
                e.isSelected = true;
            }

            //started moving selected entities
            if (startMovingPos != Vector2.Zero)
            { 
                if (PlayerInput.LeftButtonReleased())
                {
                    //mouse released -> move the entities to the new position of the mouse

                    Vector2 moveAmount = position - startMovingPos;
                    moveAmount = Submarine.VectorToWorldGrid(moveAmount);

                    if (moveAmount != Vector2.Zero)
                    {
                        foreach (MapEntity e in selectedList) e.Move(moveAmount);
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

                if (PlayerInput.LeftButtonReleased())
                {
                    if (PlayerInput.KeyDown(Keys.LeftControl) ||
                        PlayerInput.KeyDown(Keys.RightControl))
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

                if (PlayerInput.LeftButtonHeld() &&
                    PlayerInput.KeyUp(Keys.Space))
                {
                    //if clicking a selected entity, start moving it
                    foreach (MapEntity e in selectedList)
                    {
                        if (e.IsMouseOn(position)) startMovingPos = position;
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

            Vector2 position = PlayerInput.MousePosition;
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

                if (selectedList[0].ResizeHorizontal || selectedList[0].ResizeVertical)
                {
                    selectedList[0].DrawResizing(spriteBatch, cam);
                }
            }
            else
            {
                if (editingHUD == null) return;

                foreach (GUIComponent component in editingHUD.children)
                {
                    var textBox = component as GUITextBox;
                    if (textBox == null) continue;

                    textBox.Deselect();
                }

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

        private void DrawResizing(SpriteBatch spriteBatch, Camera cam)
        {
            isHighlighted = true;

            int startX = ResizeHorizontal ? -1 : 0;
            int StartY = ResizeVertical ? -1 : 0;

            for (int x = startX; x < 2; x += 2)
            {
                for (int y = StartY; y < 2; y += 2)
                {
                    
                    Vector2 handlePos = cam.WorldToScreen(Position + new Vector2(x * (rect.Width * 0.5f + 5), y * (rect.Height * 0.5f + 5)));

                    bool highlighted = Vector2.Distance(PlayerInput.MousePosition, handlePos)<5.0f;

                    GUI.DrawRectangle(spriteBatch, handlePos - new Vector2(3.0f, 3.0f), new Vector2(6.0f, 6.0f), Color.White * (highlighted ? 1.0f : 0.6f), true);

                    if (highlighted)
                    {
                        if (PlayerInput.LeftButtonDown())
                        {
                            selectionPos = Vector2.Zero;
                            resizeDirX = x;
                            resizeDirY = y;
                            resizing = true;
                        }
                    }
                }
            }

            if (resizing)
            {

                Vector2 placePosition = new Vector2(rect.X, rect.Y);
                Vector2 placeSize = new Vector2(rect.Width, rect.Height);

                Vector2 mousePos = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

                if (resizeDirX >0)
                {
                    mousePos.X = Math.Max(mousePos.X, rect.X + Submarine.GridSize.X);
                    placeSize.X = mousePos.X - placePosition.X;
                }
                else if (resizeDirX <0)
                {
                    mousePos.X = Math.Min(mousePos.X, rect.Right - Submarine.GridSize.X);

                    placeSize.X = (placePosition.X + placeSize.X)-mousePos.X;
                    placePosition.X = mousePos.X;
                }
                if (resizeDirY < 0)
                {
                    mousePos.Y = Math.Min(mousePos.Y, rect.Y - Submarine.GridSize.Y);
                    placeSize.Y = placePosition.Y-mousePos.Y;
                }
                else if (resizeDirY > 0)
                {
                    mousePos.Y = Math.Max(mousePos.Y, rect.Y - rect.Height + Submarine.GridSize.X);

                    placeSize.Y = mousePos.Y - (rect.Y - rect.Height);
                    placePosition.Y = mousePos.Y;
                }

                if ((int)placePosition.X != rect.X || (int)placePosition.Y != rect.Y || (int)placeSize.X != rect.Width || (int)placeSize.Y != rect.Height)
                {
                    Rect = new Rectangle((int)placePosition.X, (int)placePosition.Y, (int)placeSize.X, (int)placeSize.Y);
                }

                if (!PlayerInput.LeftButtonHeld())
                {
                    resizing = false;
                }
            }
        }

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
                if (!e.SelectableInEditor) continue;

                if (Submarine.RectsOverlap(selectionRect, e.rect)) foundEntities.Add(e);
            }

            return foundEntities;
        }


        public virtual XElement Save(XElement parentElement)
        {
            DebugConsole.ThrowError("Saving entity " + GetType() + " failed.");
            return null;
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
                    Debug.Assert(linked.Submarine == sub);

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
        

        public void RemoveLinked(MapEntity e)
        {
            if (linkedTo == null) return;
            if (linkedTo.Contains(e)) linkedTo.Remove(e);
        }
        
    }
}
