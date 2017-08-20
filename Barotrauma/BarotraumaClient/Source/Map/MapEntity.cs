using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    abstract partial class MapEntity : Entity
    {
        protected static Vector2 selectionPos = Vector2.Zero;
        protected static Vector2 selectionSize = Vector2.Zero;

        protected static Vector2 startMovingPos = Vector2.Zero;

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

        public virtual void Draw(SpriteBatch spriteBatch, bool editing, bool back = true) { }

        public virtual void DrawDamage(SpriteBatch spriteBatch, Effect damageEffect) { }


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

            Vector2 nudgeAmount = Vector2.Zero;
            if (PlayerInput.KeyHit(Keys.Up))    nudgeAmount.Y = 1f;
            if (PlayerInput.KeyHit(Keys.Down))  nudgeAmount.Y = -1f;
            if (PlayerInput.KeyHit(Keys.Left))  nudgeAmount.X = -1f;
            if (PlayerInput.KeyHit(Keys.Right)) nudgeAmount.X = 1f;            
            if (nudgeAmount != Vector2.Zero)
            {
                foreach (MapEntity entityToNudge in selectedList)
                {
                    entityToNudge.Move(nudgeAmount);
                }
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
                new Rectangle((int)PlayerInput.MousePosition.X + 15, (int)PlayerInput.MousePosition.Y + 15, 150, highlightedEntities.Count * 18 + 5),
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
                            Color.DarkRed, false, 0, (int)Math.Max(1.5f / GameScreen.Selected.Cam.Zoom, 1.0f));

                    //stop dragging the "selection rectangle"
                    selectionPos = Vector2.Zero;
                }
            }
            if (selectionPos != null && selectionPos != Vector2.Zero)
            {
                GUI.DrawRectangle(spriteBatch, new Vector2(selectionPos.X, -selectionPos.Y), selectionSize, Color.DarkRed, false, 0, (int)Math.Max(1.5f / GameScreen.Selected.Cam.Zoom, 1.0f));
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

        public virtual void AddToGUIUpdateList()
        {
            if (editingHUD != null && editingHUD.UserData == this) editingHUD.AddToGUIUpdateList();
        }

        public virtual void UpdateEditing(Camera cam) { }

        public virtual void DrawEditing(SpriteBatch spriteBatch, Camera cam) { }

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

                    bool highlighted = Vector2.Distance(PlayerInput.MousePosition, handlePos) < 5.0f;

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

    }
}
