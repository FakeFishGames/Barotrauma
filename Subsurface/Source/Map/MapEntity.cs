using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.ObjectModel;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    abstract class MapEntity : Entity
    {
        public static List<MapEntity> mapEntityList = new List<MapEntity>();
        
        //which entities have been selected for editing
        private static List<MapEntity> selectedList = new List<MapEntity>();
        public static List<MapEntity> SelectedList
        {
            get
            {
                return selectedList;
            }
        }
        private static List<MapEntity> copiedList = new List<MapEntity>();

        private static List<MapEntity> highlightedList = new List<MapEntity>();

        private static float highlightTimer;

        private static GUIListBox highlightedListBox;
        public static GUIListBox HighlightedListBox
        {
            get { return highlightedListBox; }
        }        


        protected static GUIComponent editingHUD;
        public static GUIComponent EditingHUD
        {
            get
            {
                return editingHUD;
            }
        }

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

        //protected bool isSelected;

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
            set { aiTarget.SightRange = value; }
        }

        public bool IsHighlighted {
            get { return isHighlighted; }
            set { isHighlighted = value; } 
        }

        public bool IsSelected
        {
            get { return selectedList.Contains(this); }
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
                    
                    (clones[itemIndex] as Item).GetComponent<ConnectionPanel>().Connections[connectionIndex].TryAddLink(cloneWire);
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
                if (existingSprite.Texture == this.Sprite.Texture) break;
            }

            mapEntityList.Insert(i, this);
        }

        public virtual bool IsVisible(Rectangle worldView)
        {
            return true;
        }

        public virtual void Draw(SpriteBatch spriteBatch, bool editing, bool back=true) {}

        public virtual void DrawDamage(SpriteBatch spriteBatch, Effect damageEffect) {}

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

            if (selectedList.Contains(this))
            {
                selectedList = selectedList.FindAll(e => e != this);
            }

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
        /// Update the selection logic in submarine editor
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
            }

            if (DisableSelect)
            {
                DisableSelect = false;
                return;
            }

            if (GUIComponent.MouseOn != null || !PlayerInput.MouseInsideWindow)
            {
                if (highlightedListBox == null || 
                    (GUIComponent.MouseOn != highlightedListBox && !highlightedListBox.IsParentOf(GUIComponent.MouseOn)))
                {
                    UpdateHighlightedListBox(null);
                    return;
                }
            }
            
            if (MapEntityPrefab.Selected != null)
            {
                selectionPos = Vector2.Zero;
                selectedList.Clear();
                return;
            }

            if (PlayerInput.KeyDown(Keys.Delete))
            {
                selectedList.ForEach(e => e.Remove());
                selectedList.Clear();
            }

            if (PlayerInput.KeyDown(Keys.LeftControl) || PlayerInput.KeyDown(Keys.RightControl))
            {
                if (PlayerInput.GetKeyboardState.IsKeyDown(Keys.C) &&
                    PlayerInput.GetOldKeyboardState.IsKeyUp(Keys.C))
                {
                    CopyEntities(selectedList);
                }
                else if (PlayerInput.GetKeyboardState.IsKeyDown(Keys.X) &&
                    PlayerInput.GetOldKeyboardState.IsKeyUp(Keys.X))
                {
                    CopyEntities(selectedList);

                    selectedList.ForEach(e => e.Remove());
                    selectedList.Clear();
                }
                else if (copiedList.Count > 0 &&
                    PlayerInput.GetKeyboardState.IsKeyDown(Keys.V) &&
                    PlayerInput.GetOldKeyboardState.IsKeyUp(Keys.V))
                {
                    var clones = Clone(copiedList);

                    Vector2 center = Vector2.Zero;
                    clones.ForEach(c => center += c.WorldPosition);
                    center = Submarine.VectorToWorldGrid(center / clones.Count);

                    Vector2 moveAmount = Submarine.VectorToWorldGrid(cam.WorldViewCenter - center);

                    selectedList = new List<MapEntity>(clones);
                    foreach (MapEntity clone in selectedList)
                    {
                        clone.Move(moveAmount);
                        clone.Submarine = Submarine.MainSub;
                    }                    
                }
            }

            Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);

            MapEntity highLightedEntity = null;

            if (startMovingPos == Vector2.Zero)
            {
                List<MapEntity> highlightedEntities = new List<MapEntity>();
                if (highlightedListBox != null && highlightedListBox.IsParentOf(GUIComponent.MouseOn))
                {
                    highLightedEntity = GUIComponent.MouseOn.UserData as MapEntity;
                }
                else
                {
                    foreach (MapEntity e in mapEntityList)
                    {
                        if (!e.SelectableInEditor) continue;

                        if (e.IsMouseOn(position))
                        {
                            int i = 0;
                            while (i < highlightedEntities.Count && 
                                e.Sprite != null &&
                                (highlightedEntities[i].Sprite == null || highlightedEntities[i].Sprite.Depth < e.Sprite.Depth))
                            {
                                i++;
                            }

                            highlightedEntities.Insert(i, e);

                            if (i == 0) highLightedEntity = e;                            
                        }
                    }

                    if (PlayerInput.MouseSpeed.LengthSquared() > 10)
                    {
                        highlightTimer = 0.0f;
                    }
                    else
                    {
                        bool mouseNearHighlightBox = false;

                        if (highlightedListBox != null)
                        {
                            Rectangle expandedRect = highlightedListBox.Rect;
                            expandedRect.Inflate(20, 20);
                            mouseNearHighlightBox = expandedRect.Contains(PlayerInput.MousePosition);
                            if (!mouseNearHighlightBox) highlightedListBox = null;
                        }

                        highlightTimer += (float)Timing.Step;
                        if (highlightTimer > 1.0f)
                        {
                            if (!mouseNearHighlightBox)
                            {
                                UpdateHighlightedListBox(highlightedEntities);
                                highlightTimer = 0.0f;
                            }
                        }
                    }
                }

                if (highLightedEntity != null) highLightedEntity.isHighlighted = true;
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
                        //clone
                        if (PlayerInput.KeyDown(Keys.LeftControl) || PlayerInput.KeyDown(Keys.RightControl))
                        {
                            var clones = Clone(selectedList);
                            selectedList = clones;
                            selectedList.ForEach(c => c.Move(moveAmount));
                        }
                        else // move
                        {
                            foreach (MapEntity e in selectedList) e.Move(moveAmount);
                        }
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

                if (PlayerInput.LeftButtonReleased())
                {
                    if (PlayerInput.KeyDown(Keys.LeftControl) ||
                        PlayerInput.KeyDown(Keys.RightControl))
                    {
                        foreach (MapEntity e in newSelection)
                        {
                            if (selectedList.Contains(e))
                                selectedList.Remove(e);
                            else
                                selectedList.Add(e);
                        }
                    }
                    else
                    {
                        selectedList = newSelection;
                    }

                    //select wire if both items it's connected to are selected
                    var selectedItems = selectedList.Where(e => e is Item).Cast<Item>().ToList();
                    foreach (Item item in selectedItems)
                    {
                        if (item.Connections == null) continue;
                        foreach (Connection c in item.Connections)
                        {
                            foreach (Wire w in c.Wires)
                            {
                                if (w == null || selectedList.Contains(w.Item)) continue;

                                if (w.OtherConnection(c) != null && selectedList.Contains(w.OtherConnection(c).Item))
                                {
                                    selectedList.Add(w.Item);
                                }
                            }
                        }
                    }
                    
                    selectionPos = Vector2.Zero;
                    selectionSize = Vector2.Zero;
                }
            }
            //default, not doing anything specific yet
            else
            {
                if (PlayerInput.LeftButtonHeld() &&
                    PlayerInput.KeyUp(Keys.Space) && 
                    (highlightedListBox == null || (GUIComponent.MouseOn != highlightedListBox && !highlightedListBox.IsParentOf(GUIComponent.MouseOn))))
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

        private static void UpdateHighlightedListBox(List<MapEntity> highlightedEntities)
        {
            if (highlightedEntities == null || highlightedEntities.Count < 2)
            {
                highlightedListBox = null;
                return;
            }
            if (highlightedListBox != null)
            {
                if (GUIComponent.MouseOn == highlightedListBox || highlightedListBox.IsParentOf(GUIComponent.MouseOn)) return;
                if (highlightedEntities.SequenceEqual(highlightedList)) return;
            }

            highlightedList = highlightedEntities;

            highlightedListBox = new GUIListBox(
                new Rectangle((int)PlayerInput.MousePosition.X+15, (int)PlayerInput.MousePosition.Y+15, 150, highlightedEntities.Count * 18 + 5), 
                null, Alignment.TopLeft, "GUIToolTip", null, false);
            
            foreach (MapEntity entity in highlightedEntities)
            {
                var textBlock = new GUITextBlock(
                    new Rectangle(0, 0, highlightedListBox.Rect.Width, 18),
                    ToolBox.LimitString(entity.Name, GUI.SmallFont, 140), "", Alignment.TopLeft, Alignment.CenterLeft, highlightedListBox, false, GUI.SmallFont);

                textBlock.UserData = entity;
            }
            
            highlightedListBox.OnSelected = (GUIComponent component, object obj) =>
            {
                MapEntity entity = obj as MapEntity;

                if (PlayerInput.KeyDown(Keys.LeftControl) ||
                    PlayerInput.KeyDown(Keys.RightControl))
                {
                    if (selectedList.Contains(entity))
                        selectedList.Remove(entity);
                    else
                        selectedList.Add(entity);                        
                }
                else
                {
                    SelectEntity(entity);
                }

                return true;
            };
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
                            Color.DarkRed,false,0,(int)Math.Max(1.5f/GameScreen.Selected.Cam.Zoom,1.0f));
                    
                    //stop dragging the "selection rectangle"
                    selectionPos = Vector2.Zero;
                }
            }
            if (selectionPos != null && selectionPos != Vector2.Zero)
            {                
                GUI.DrawRectangle(spriteBatch, new Vector2(selectionPos.X, -selectionPos.Y), selectionSize, Color.DarkRed,false,0,(int)Math.Max(1.5f / GameScreen.Selected.Cam.Zoom, 1.0f));
            }
        }

        public static void UpdateEditor(Camera cam)
        {
            if (highlightedListBox != null) highlightedListBox.Update((float)Timing.Step);            

            if (selectedList.Count == 1)
            {
                selectedList[0].UpdateEditing(cam);

                if (selectedList[0].ResizeHorizontal || selectedList[0].ResizeVertical)
                {
                    selectedList[0].UpdateResizing(cam);
                }
            }

            if (editingHUD != null)
            {
                if (selectedList.Count == 0 || editingHUD.UserData != selectedList[0])
                {
                    foreach (GUIComponent component in editingHUD.children)
                    {
                        var textBox = component as GUITextBox;
                        if (textBox == null) continue;

                        textBox.Deselect();
                    }

                    editingHUD = null;
                }
            }
        }

        public static void DrawEditor(SpriteBatch spriteBatch, Camera cam)
        {
            if (selectedList.Count == 1)
            {
                selectedList[0].DrawEditing(spriteBatch, cam);

                if (selectedList[0].ResizeHorizontal || selectedList[0].ResizeVertical)
                {
                    selectedList[0].DrawResizing(spriteBatch, cam);
                }
            }

            if (highlightedListBox != null)
            {
                highlightedListBox.Draw(spriteBatch);
            }
        }

        public static void DeselectAll()
        {
            selectedList.Clear();
        }


        public static void SelectEntity(MapEntity entity)
        {
            DeselectAll();

            selectedList.Add(entity);
        }
        
        /// <summary>
        /// copies a list of entities to the "clipboard" (copiedList)
        /// </summary>
        private static void CopyEntities(List<MapEntity> entities)
        {
            List<MapEntity> prevEntities = new List<MapEntity>(mapEntityList);

            copiedList = Clone(entities);

            //find all new entities created during cloning
            var newEntities = mapEntityList.Except(prevEntities).ToList();

            //do a "shallow remove" (removes the entities from the game without removing links between them)
            //  -> items will stay in their containers
            newEntities.ForEach(e => e.ShallowRemove());
        }

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
        
        public virtual void AddToGUIUpdateList()
        {
            if (editingHUD != null && editingHUD.UserData == this) editingHUD.AddToGUIUpdateList();
        }

        public virtual void UpdateEditing(Camera cam) { }

        public virtual void DrawEditing(SpriteBatch spriteBatch, Camera cam) {}

        private void UpdateResizing(Camera cam)
        {
            isHighlighted = true;

            int startX = ResizeHorizontal ? -1 : 0;
            int StartY = ResizeVertical ? -1 : 0;

            for (int x = startX; x < 2; x += 2)
            {
                for (int y = StartY; y < 2; y += 2)
                {
                    Vector2 handlePos = cam.WorldToScreen(Position + new Vector2(x * (rect.Width * 0.5f + 5), y * (rect.Height * 0.5f + 5)));

                    bool highlighted = Vector2.Distance(PlayerInput.MousePosition, handlePos) < 5.0f;

                    if (highlighted && PlayerInput.LeftButtonDown())
                    {
                        selectionPos = Vector2.Zero;
                        resizeDirX = x;
                        resizeDirY = y;
                        resizing = true;
                    }
                }
            }

            if (resizing)
            {
                Vector2 placePosition = new Vector2(rect.X, rect.Y);
                Vector2 placeSize = new Vector2(rect.Width, rect.Height);

                Vector2 mousePos = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

                if (resizeDirX > 0)
                {
                    mousePos.X = Math.Max(mousePos.X, rect.X + Submarine.GridSize.X);
                    placeSize.X = mousePos.X - placePosition.X;
                }
                else if (resizeDirX < 0)
                {
                    mousePos.X = Math.Min(mousePos.X, rect.Right - Submarine.GridSize.X);

                    placeSize.X = (placePosition.X + placeSize.X) - mousePos.X;
                    placePosition.X = mousePos.X;
                }
                if (resizeDirY < 0)
                {
                    mousePos.Y = Math.Min(mousePos.Y, rect.Y - Submarine.GridSize.Y);
                    placeSize.Y = placePosition.Y - mousePos.Y;
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

                    GUI.DrawRectangle(spriteBatch,
                        handlePos - new Vector2(3.0f, 3.0f),
                        new Vector2(6.0f, 6.0f),
                        Color.White * (highlighted ? 1.0f : 0.6f),
                        true, 0,
                        (int)Math.Max(1.5f / GameScreen.Selected.Cam.Zoom, 1.0f));
                }
            }
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
