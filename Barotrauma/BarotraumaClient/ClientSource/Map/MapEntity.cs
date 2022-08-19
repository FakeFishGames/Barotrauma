using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Barotrauma.Lights;

namespace Barotrauma
{
    abstract partial class MapEntity : Entity
    {
        protected static Vector2 selectionPos = Vector2.Zero;
        protected static Vector2 selectionSize = Vector2.Zero;

        private static Vector2 startMovingPos = Vector2.Zero;

        private static float keyDelay;

        public static Vector2 StartMovingPos => startMovingPos;
        public static Vector2 SelectionPos => selectionPos;

        public event Action<Rectangle> Resized;

        private static bool resizing;
        private int resizeDirX, resizeDirY;
        private Rectangle? prevRect;

        public static bool SelectionChanged;

        //which entities have been selected for editing
        public static HashSet<MapEntity> SelectedList { get; private set; } = new HashSet<MapEntity>();

        public static List<MapEntity> CopiedList = new List<MapEntity>();

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

        public virtual bool SelectableInEditor => true;

        public static bool SelectedAny => SelectedList.Count > 0;

        public bool IsSelected => SelectedList.Contains(this);

        public bool IsIncludedInSelection { get; set; }

        public virtual bool IsVisible(Rectangle worldView)
        {
            return true;
        }

        /// <summary>
        /// Used for undo/redo to determine what this item has been replaced with
        /// </summary>
        public MapEntity ReplacedBy;

        public virtual void Draw(SpriteBatch spriteBatch, bool editing, bool back = true) { }

        /// <summary>
        /// A method that modifies the draw depth to prevent z-fighting between entities with the same sprite depth
        /// </summary>
        public float GetDrawDepth(float baseDepth, Sprite sprite)
        {
            float depth = baseDepth
                //take texture into account to get entities with (roughly) the same base depth and texture to render consecutively to minimize texture swaps
                + (sprite?.Texture?.SortingKey ?? 0) % 100 * 0.00001f
                + ID % 100 * 0.000001f;
            return Math.Min(depth, 1.0f);
        }

        /// <summary>
        /// Update the selection logic in submarine editor
        /// </summary>
        public static void UpdateSelecting(Camera cam)
        {
            if (resizing)
            {
                if (!SelectedAny)
                {
                    resizing = false;
                }
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

            if (startMovingPos == Vector2.Zero
                && selectionPos == Vector2.Zero
                && (GUI.MouseOn != null || !PlayerInput.MouseInsideWindow))
            {
                if (highlightedListBox == null ||
                    (GUI.MouseOn != highlightedListBox && !highlightedListBox.IsParentOf(GUI.MouseOn)))
                {
                    UpdateHighlightedListBox(null, false);
                    return;
                }
            }

            if (MapEntityPrefab.Selected != null)
            {
                selectionPos = Vector2.Zero;
                SelectedList.Clear();
                return;
            }
            if (GUI.KeyboardDispatcher.Subscriber == null)
            {
                if (PlayerInput.KeyHit(Keys.Delete))
                {
                    if (SelectedAny)
                    {
                        SubEditorScreen.StoreCommand(new AddOrDeleteCommand(new List<MapEntity>(SelectedList), true));
                    }
                    SelectedList.ForEach(e => { if (!e.Removed) { e.Remove(); } });
                    SelectedList.Clear();
                }

                if (PlayerInput.IsCtrlDown())
                {
#if DEBUG
                    if (PlayerInput.KeyHit(Keys.D))
                    {
                        bool terminate = false;
                        foreach (MapEntity entity in SelectedList)
                        {
                            if (entity is Item item && item.GetComponent<Planter>() is { } planter)
                            {
                                planter.Update(1.0f, cam);
                                for (var i = 0; i < planter.GrowableSeeds.Length; i++)
                                {
                                    Growable seed = planter.GrowableSeeds[i];
                                    PlantSlot slot = planter.PlantSlots.ContainsKey(i) ? planter.PlantSlots[i] : Planter.NullSlot;
                                    if (seed == null) { continue; }

                                    seed.CreateDebugHUD(planter, slot);
                                    terminate = true;
                                    break;
                                }
                            }

                            if (terminate) { break; }
                        }
                    }
#endif
                    if (PlayerInput.KeyHit(Keys.C))
                    {
                        Copy(SelectedList.ToList());
                    }
                    else if (PlayerInput.KeyHit(Keys.X))
                    {
                        Cut(SelectedList.ToList());
                    }
                    else if (PlayerInput.KeyHit(Keys.V))
                    {
                        Paste(cam.ScreenToWorld(PlayerInput.MousePosition));
                    }
                    /*else if (PlayerInput.KeyHit(Keys.G))
                    {
                        if (SelectedList.Any())
                        {
                            if (SelectionGroups.ContainsKey(SelectedList.Last()))
                            {
                                // Ungroup all selected
                                SelectedList.ForEach(e => SelectionGroups.Remove(e));
                            }
                            else
                            {
                                foreach (var entity in SelectedList)
                                {
                                    // Remove the old group, if any
                                    SelectionGroups.Remove(entity);
                                    // Create a group that can be accessed with any member
                                    SelectionGroups.Add(entity, SelectedList);
                                }
                            }
                        }
                    }*/
                }
            }

            Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);
            MapEntity highLightedEntity = null;
            if (startMovingPos == Vector2.Zero)
            {
                List<MapEntity> highlightedEntities = new List<MapEntity>();
                if (highlightedListBox != null && highlightedListBox.IsParentOf(GUI.MouseOn))
                {
                    highLightedEntity = GUI.MouseOn.UserData as MapEntity;
                }
                else
                {
                    foreach (MapEntity e in mapEntityList)
                    {
                        if (!e.SelectableInEditor) { continue; }
                        if (e.IsMouseOn(position))
                        {
                            int i = 0;
                            while (i < highlightedEntities.Count &&
                                e.Sprite != null &&
                                (highlightedEntities[i].Sprite == null || highlightedEntities[i].SpriteDepth < e.SpriteDepth))
                            {
                                i++;
                            }
                            highlightedEntities.Insert(i, e);
                            if (i == 0) highLightedEntity = e;
                        }
                    }

                    UpdateHighlighting(highlightedEntities);
                }

                if (highLightedEntity != null) highLightedEntity.isHighlighted = true;
            }

            if (GUI.KeyboardDispatcher.Subscriber == null)
            {
                Vector2 nudge = GetNudgeAmount();
                if (nudge != Vector2.Zero)
                {
                    foreach (MapEntity entityToNudge in SelectedList) { entityToNudge.Move(nudge); }
                }
            }
            else
            {
                keyDelay = 0;
            }

            bool isShiftDown = PlayerInput.IsShiftDown();

            //started moving selected entities
            if (startMovingPos != Vector2.Zero)
            {
                Item targetContainer = GetPotentialContainer(position, SelectedList);

                if (targetContainer != null) { targetContainer.IsHighlighted = true; }

                if (PlayerInput.PrimaryMouseButtonReleased())
                {
                    //mouse released -> move the entities to the new position of the mouse

                    Vector2 moveAmount = position - startMovingPos;

                    if (!isShiftDown)
                    {
                        moveAmount.X = (float)(moveAmount.X > 0.0f ? Math.Floor(moveAmount.X / Submarine.GridSize.X) : Math.Ceiling(moveAmount.X / Submarine.GridSize.X)) * Submarine.GridSize.X;
                        moveAmount.Y = (float)(moveAmount.Y > 0.0f ? Math.Floor(moveAmount.Y / Submarine.GridSize.Y) : Math.Ceiling(moveAmount.Y / Submarine.GridSize.Y)) * Submarine.GridSize.Y;
                    }

                    if (Math.Abs(moveAmount.X) >= Submarine.GridSize.X || Math.Abs(moveAmount.Y) >= Submarine.GridSize.Y || isShiftDown)
                    {
                        if (!isShiftDown) { moveAmount = Submarine.VectorToWorldGrid(moveAmount); }

                        //clone
                        if (PlayerInput.IsCtrlDown())
                        {
                            HashSet<MapEntity> clones = Clone(SelectedList.ToList()).Where(c => c != null).ToHashSet();
                            SelectedList = clones;
                            SelectedList.ForEach(c => c.Move(moveAmount));
                            SubEditorScreen.StoreCommand(new AddOrDeleteCommand(new List<MapEntity>(clones), false));
                        }
                        else // move
                        {
                            var oldRects = SelectedList.Select(e => e.Rect).ToList();
                            List<MapEntity> deposited = new List<MapEntity>();
                            foreach (MapEntity e in SelectedList)
                            {
                                e.Move(moveAmount);

                                if (isShiftDown && e is Item item && targetContainer != null)
                                {
                                    if (targetContainer.OwnInventory.TryPutItem(item, Character.Controlled))
                                    {
                                        SoundPlayer.PlayUISound(GUISoundType.DropItem);
                                        deposited.Add(item);
                                    }
                                    else
                                    {
                                        SoundPlayer.PlayUISound(GUISoundType.PickItemFail);
                                    }
                                }
                            }

                            SubEditorScreen.StoreCommand(new TransformCommand(new List<MapEntity>(SelectedList),SelectedList.Select(entity => entity.Rect).ToList(), oldRects, false));
                            if (deposited.Any() && deposited.Any(entity => entity is Item))
                            {
                                var depositedItems = deposited.Where(entity => entity is Item).Cast<Item>().ToList();
                                SubEditorScreen.StoreCommand(new InventoryPlaceCommand(targetContainer.OwnInventory, depositedItems, false));
                            }

                            deposited.ForEach(entity => { SelectedList.Remove(entity); });
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

                foreach (MapEntity entity in mapEntityList)
                {
                    entity.IsIncludedInSelection = false;
                }

                HashSet<MapEntity> newSelection = new HashSet<MapEntity>();// FindSelectedEntities(selectionPos, selectionSize);
                if (Math.Abs(selectionSize.X) > Submarine.GridSize.X || Math.Abs(selectionSize.Y) > Submarine.GridSize.Y)
                {
                    newSelection = FindSelectedEntities(selectionPos, selectionSize);
                }
                else
                {
                    if (highLightedEntity != null)
                    {
                        if (SubEditorScreen.IsLayerLinked(highLightedEntity)/*SelectionGroups.TryGetValue(highLightedEntity, out HashSet<MapEntity> group)*/)
                        {
                            ImmutableHashSet<MapEntity> entitiesInSameLayer = SubEditorScreen.GetEntitiesInSameLayer(highLightedEntity);
                            foreach (MapEntity entity in entitiesInSameLayer.Where(e => !newSelection.Contains(e)))
                            {
                                newSelection.Add(entity);
                            }

                            foreach (MapEntity entity in entitiesInSameLayer)
                            {
                                entity.IsIncludedInSelection = true;
                            }
                        }
                        else
                        {
                            newSelection.Add(highLightedEntity);
                            highLightedEntity.IsIncludedInSelection = true;
                        }
                    }
                }

                if (PlayerInput.PrimaryMouseButtonReleased())
                {
                    if (PlayerInput.IsCtrlDown())
                    {
                        foreach (MapEntity e in newSelection)
                        {
                            if (SelectedList.Contains(e))
                            {
                                RemoveSelection(e);
                            }
                            else
                            {
                                AddSelection(e);
                            }
                        }
                    }
                    else
                    {
                        SelectedList = new HashSet<MapEntity>(newSelection);
                        //selectedList.Clear();
                        //newSelection.ForEach(e => AddSelection(e));
                        foreach (var entity in newSelection)
                        {
                            HandleDoorGapLinks(entity,
                                onGapFound: (door, gap) =>
                                {
                                    door.RefreshLinkedGap();
                                    if (!SelectedList.Contains(gap))
                                    {
                                        SelectedList.Add(gap);
                                    }
                                },
                                onDoorFound: (door, gap) =>
                                {
                                    if (!SelectedList.Contains(door.Item))
                                    {
                                        SelectedList.Add(door.Item);
                                    }
                                });
                        }
                    }

                    //select wire if both items it's connected to are selected
                    var selectedItems = SelectedList.Where(e => e is Item).Cast<Item>().ToList();
                    foreach (Item item in Item.ItemList)
                    {
                        var wire = item.GetComponent<Wire>();
                        if (wire == null) { continue; }
                        Item item0 = wire.Connections[0]?.Item;
                        Item item1 = wire.Connections[1]?.Item;

                        if (item0 == null && item1 != null)
                        {
                            item0 = Item.ItemList.Find(it => it.GetComponent<ConnectionPanel>()?.DisconnectedWires.Contains(wire) ?? false);
                        }
                        else if (item0 != null && item1 == null)
                        {
                            item1 = Item.ItemList.Find(it => it.GetComponent<ConnectionPanel>()?.DisconnectedWires.Contains(wire) ?? false);
                        }
                        if (item0 != null && item1 != null && SelectedList.Contains(item0) && SelectedList.Contains(item1))
                        {
                            SelectedList.Add(item);
                        }
                    }

                    selectionPos = Vector2.Zero;
                    selectionSize = Vector2.Zero;
                    foreach (MapEntity entity in mapEntityList)
                    {
                        entity.IsIncludedInSelection = false;
                    }
                }
            }
            //default, not doing anything specific yet
            else
            {
                if (PlayerInput.PrimaryMouseButtonHeld() &&
                    PlayerInput.KeyUp(Keys.Space) &&
                    PlayerInput.KeyUp(Keys.LeftAlt) &&
                    PlayerInput.KeyUp(Keys.RightAlt) &&
                    (highlightedListBox == null || (GUI.MouseOn != highlightedListBox && !highlightedListBox.IsParentOf(GUI.MouseOn))))
                {
                    //if clicking a selected entity, start moving it
                    foreach (MapEntity e in SelectedList)
                    {
                        if (e.IsMouseOn(position)) startMovingPos = position;
                    }
                    selectionPos = position;

                    //stop camera movement to prevent accidental dragging or rect selection
                    Screen.Selected.Cam.StopMovement();
                }
            }
        }

        public static Vector2 GetNudgeAmount(bool doHold = true)
        {
            Vector2 nudgeAmount = Vector2.Zero;
            if (doHold)
            {
                int up = PlayerInput.KeyDown(Keys.Up) ? 1 : 0,
                    down = PlayerInput.KeyDown(Keys.Down) ? -1 : 0,
                    left = PlayerInput.KeyDown(Keys.Left) ? -1 : 0,
                    right = PlayerInput.KeyDown(Keys.Right) ? 1 : 0;

                int xKeysDown = (left + right);
                int yKeysDown = (up + down);

                if (xKeysDown != 0 || yKeysDown != 0) { keyDelay += (float) Timing.Step; } else { keyDelay = 0; }


                if (keyDelay >= 0.5f)
                {
                    nudgeAmount.Y = yKeysDown;
                    nudgeAmount.X = xKeysDown;
                }
            }

            if (PlayerInput.KeyHit(Keys.Up))    nudgeAmount.Y =  1f;
            if (PlayerInput.KeyHit(Keys.Down))  nudgeAmount.Y = -1f;
            if (PlayerInput.KeyHit(Keys.Left))  nudgeAmount.X = -1f;
            if (PlayerInput.KeyHit(Keys.Right)) nudgeAmount.X =  1f;

            return nudgeAmount;
        }

        public MapEntity GetReplacementOrThis()
        {
            return ReplacedBy?.GetReplacementOrThis() ?? this;
        }

        public static Item GetPotentialContainer(Vector2 position, HashSet<MapEntity> entities = null)
        {
            Item targetContainer = null;
            bool isShiftDown = PlayerInput.IsShiftDown();

            if (!isShiftDown) return null;

            foreach (MapEntity e in mapEntityList)
            {
                if (!e.SelectableInEditor ||!(e is Item potentialContainer)) { continue; }

                if (e.IsMouseOn(position))
                {
                    if (entities == null)
                    {
                        if (potentialContainer.OwnInventory != null && potentialContainer.ParentInventory == null && !potentialContainer.OwnInventory.IsFull(takeStacksIntoAccount: true))
                        {
                            targetContainer = potentialContainer;
                            break;
                        }
                    }
                    else
                    {
                        foreach (MapEntity selectedEntity in entities)
                        {
                            if (!(selectedEntity is Item selectedItem)) { continue; }
                            if (potentialContainer.OwnInventory != null && potentialContainer.ParentInventory == null && potentialContainer != selectedItem &&
                                potentialContainer.OwnInventory.CanBePut(selectedItem))
                            {
                                targetContainer = potentialContainer;
                                break;
                            }
                        }
                    }
                }
                if (targetContainer != null) { break; }
            }

            return targetContainer;
        }

        /// <summary>
        /// Updates the logic that runs the highlight box when the mouse is sitting still.
        /// </summary>
        /// <see cref="UpdateHighlightedListBox"/>
        /// <param name="highlightedEntities"></param>
        /// <param name="wiringMode">true to give items tooltip showing their connection</param>
        public static void UpdateHighlighting(List<MapEntity> highlightedEntities, bool wiringMode = false)
        {
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
                        UpdateHighlightedListBox(highlightedEntities, wiringMode);
                        highlightTimer = 0.0f;
                    }
                }
            }
        }

        private static void UpdateHighlightedListBox(List<MapEntity> highlightedEntities, bool wiringMode)
        {
            if (highlightedEntities == null || highlightedEntities.Count < 2)
            {
                highlightedListBox = null;
                return;
            }
            if (highlightedListBox != null)
            {
                if (GUI.MouseOn == highlightedListBox || highlightedListBox.IsParentOf(GUI.MouseOn)) return;
                if (highlightedEntities.SequenceEqual(highlightedList)) return;
            }

            highlightedList = highlightedEntities;

            highlightedListBox = new GUIListBox(new RectTransform(new Point(180, highlightedEntities.Count * 18 + 5), GUI.Canvas)
            {
                MaxSize = new Point(int.MaxValue, 256),
                ScreenSpaceOffset =  PlayerInput.MousePosition.ToPoint() + new Point(15)
            }, style: "GUIToolTip");

            foreach (MapEntity entity in highlightedEntities)
            {
                LocalizedString tooltip = string.Empty;

                if (wiringMode && entity is Item item)
                {
                    var wire = item.GetComponent<Wire>();
                    if (wire?.Connections != null)
                    {
                        for (var i = 0; i < wire.Connections.Length; i++)
                        {
                            var conn = wire.Connections[i];
                            if (conn != null)
                            {
                                tooltip += TextManager.GetWithVariables("wirelistformat",
                                    ("[item]", conn.Item?.Name),
                                    ("[pin]", conn.Name));
                            }
                            if (i != wire.Connections.Length - 1) { tooltip += '\n'; }
                        }
                    }
                }

                var textBlock = new GUITextBlock(new RectTransform(new Point(highlightedListBox.Content.Rect.Width, 15), highlightedListBox.Content.RectTransform),
                                                 ToolBox.LimitString(entity.Name, GUIStyle.SmallFont, 140), font: GUIStyle.SmallFont)
                {
                    ToolTip = tooltip,
                    UserData = entity
                };
            }

            highlightedListBox.OnSelected = (GUIComponent component, object obj) =>
            {
                MapEntity entity = obj as MapEntity;

                if (PlayerInput.IsCtrlDown() && !wiringMode)
                {
                    if (SelectedList.Contains(entity))
                    {
                        RemoveSelection(entity);
                    }
                    else
                    {
                        AddSelection(entity);
                    }

                    return true;
                }
                SelectEntity(entity);

                return true;
            };
        }

        public static void AddSelection(MapEntity entity)
        {
            if (SelectedList.Contains(entity)) { return; }
            SelectedList.Add(entity);
            HandleDoorGapLinks(entity,
                onGapFound: (door, gap) =>
                {
                    door.RefreshLinkedGap();
                    if (!SelectedList.Contains(gap))
                    {
                        SelectedList.Add(gap);
                    }
                },
                onDoorFound: (door, gap) =>
                {
                    if (!SelectedList.Contains(door.Item))
                    {
                        SelectedList.Add(door.Item);
                    }
                });
        }

        private static void HandleDoorGapLinks(MapEntity entity, Action<Door, Gap> onGapFound, Action<Door, Gap> onDoorFound)
        {
            switch (entity)
            {
                case Item i:
                {
                    var door = i.GetComponent<Door>();
                    var gap = door?.LinkedGap;
                    if (gap != null)
                    {
                        onGapFound(door, gap);
                    }

                    break;
                }
                case Gap gap:
                {
                    var door = gap.ConnectedDoor;
                    if (door != null)
                    {
                        onDoorFound(door, gap);
                    }

                    break;
                }
            }
        }

        public static void RemoveSelection(MapEntity entity)
        {
            SelectedList.Remove(entity);
            HandleDoorGapLinks(entity,
                onGapFound: (door, gap) => SelectedList.Remove(gap),
                onDoorFound: (door, gap) => SelectedList.Remove(door.Item));
        }

        static partial void UpdateAllProjSpecific(float deltaTime)
        {
            var entitiesToRender = Submarine.VisibleEntities ?? mapEntityList;
            foreach (MapEntity me in entitiesToRender)
            {
                if (me is Item item)
                {
                    item.UpdateSpriteStates(deltaTime);
                }
                else if (me is Structure structure)
                {
                    structure.UpdateSpriteStates(deltaTime);
                }
            }
        }

        /// <summary>
        /// Draw the "selection rectangle" and outlines of entities that are being dragged (if any)
        /// </summary>
        public static void DrawSelecting(SpriteBatch spriteBatch, Camera cam)
        {
            Vector2 position = PlayerInput.MousePosition;
            position = cam.ScreenToWorld(position);

            if (startMovingPos != Vector2.Zero)
            {
                Vector2 moveAmount = position - startMovingPos;
                moveAmount.Y = -moveAmount.Y;

                bool isShiftDown = PlayerInput.IsShiftDown();

                if (!isShiftDown)
                {
                    moveAmount.X = (float)(moveAmount.X > 0.0f ? Math.Floor(moveAmount.X / Submarine.GridSize.X) : Math.Ceiling(moveAmount.X / Submarine.GridSize.X)) * Submarine.GridSize.X;
                    moveAmount.Y = (float)(moveAmount.Y > 0.0f ? Math.Floor(moveAmount.Y / Submarine.GridSize.Y) : Math.Ceiling(moveAmount.Y / Submarine.GridSize.Y)) * Submarine.GridSize.Y;
                }

                //started moving the selected entities
                if (Math.Abs(moveAmount.X) >= Submarine.GridSize.X || Math.Abs(moveAmount.Y) >= Submarine.GridSize.Y || isShiftDown)
                {
                    foreach (MapEntity e in SelectedList)
                    {
                        SpriteEffects spriteEffects = SpriteEffects.None;
                        switch (e)
                        {
                            case Item item:
                                {
                                    if (item.FlippedX && item.Prefab.CanSpriteFlipX) { spriteEffects ^= SpriteEffects.FlipHorizontally; }
                                    if (item.flippedY && item.Prefab.CanSpriteFlipY) { spriteEffects ^= SpriteEffects.FlipVertically; }
                                    var wire = item.GetComponent<Wire>();
                                    if (wire != null && wire.Item.body != null && !wire.Item.body.Enabled)
                                    {
                                        wire.Draw(spriteBatch, editing: false, new Vector2(moveAmount.X, -moveAmount.Y));
                                        continue;
                                    }
                                    break;
                                }
                            case Structure structure:
                                {
                                    if (structure.FlippedX && structure.Prefab.CanSpriteFlipX) { spriteEffects ^= SpriteEffects.FlipHorizontally; }
                                    if (structure.flippedY && structure.Prefab.CanSpriteFlipY) { spriteEffects ^= SpriteEffects.FlipVertically; }
                                    break;
                                }
                            case WayPoint wayPoint:
                                {
                                    Vector2 drawPos = e.WorldPosition;
                                    drawPos.Y = -drawPos.Y;
                                    drawPos += moveAmount;
                                    wayPoint.Draw(spriteBatch, drawPos);
                                    continue;
                                }
                            case LinkedSubmarine linkedSub:
                                {
                                    var ma = moveAmount;
                                    ma.Y = -ma.Y;
                                    Vector2 lPos = linkedSub.Position;
                                    lPos += ma;
                                    linkedSub.Draw(spriteBatch, lPos, alpha: 0.5f);
                                    break;
                                }
                        }
                        e.Prefab?.DrawPlacing(spriteBatch,
                            new Rectangle(e.WorldRect.Location + new Point((int)moveAmount.X, (int)-moveAmount.Y), e.WorldRect.Size), e.Scale, spriteEffects);
                        GUI.DrawRectangle(spriteBatch,
                            new Vector2(e.WorldRect.X, -e.WorldRect.Y) + moveAmount,
                            new Vector2(e.rect.Width, e.rect.Height),
                            Color.White, false, 0, (int)Math.Max(3.0f / GameScreen.Selected.Cam.Zoom, 2.0f));
                    }

                    //stop dragging the "selection rectangle"
                    selectionPos = Vector2.Zero;
                }
            }
            if (selectionPos != Vector2.Zero)
            {
                var (sizeX, sizeY) = selectionSize;
                var (posX, posY) = selectionPos;

                posY = -posY;

                Vector2[] corners =
                {
                    new Vector2(posX, posY),
                    new Vector2(posX + sizeX, posY),
                    new Vector2(posX + sizeX, posY + sizeY),
                    new Vector2(posX, posY + sizeY)
                };

                Color selectionColor = GUIStyle.Blue;
                float thickness = Math.Max(2f, 2f / Screen.Selected.Cam.Zoom);

                GUI.DrawFilledRectangle(spriteBatch, corners[0], selectionSize, selectionColor * 0.1f);

                Vector2 offset = new Vector2(0f, thickness / 2f);

                if (sizeY < 0) { offset.Y = -offset.Y; }

                spriteBatch.DrawLine(corners[0], corners[1], selectionColor, thickness);
                spriteBatch.DrawLine(corners[1] - offset, corners[2] + offset, selectionColor, thickness);
                spriteBatch.DrawLine(corners[2], corners[3], selectionColor, thickness);
                spriteBatch.DrawLine(corners[3] + offset, corners[0] - offset, selectionColor, thickness);
            }
        }

        public static List<MapEntity> FilteredSelectedList { get; private set; } = new List<MapEntity>();

        public static void UpdateEditor(Camera cam, float deltaTime)
        {
            if (highlightedListBox != null) { highlightedListBox.UpdateManually(deltaTime); }

            if (editingHUD != null)
            {
                if (FilteredSelectedList.Count == 0 || editingHUD.UserData != FilteredSelectedList[0])
                {
                    foreach (GUIComponent component in editingHUD.Children)
                    {
                        var textBox = component as GUITextBox;
                        if (textBox == null) continue;
                        textBox.Deselect();
                    }
                    editingHUD = null;
                }
            }
            FilteredSelectedList.Clear();
            if (SelectedList.Count == 0) return;
            foreach (var e in SelectedList)
            {
                if (e is Gap gap && gap.ConnectedDoor != null) { continue; }
                FilteredSelectedList.Add(e);
            }
            var first = FilteredSelectedList.FirstOrDefault();
            if (first != null)
            {
                first.UpdateEditing(cam, deltaTime);
                if (first.ResizeHorizontal || first.ResizeVertical)
                {
                    first.UpdateResizing(cam);
                }
            }

            if (PlayerInput.IsCtrlDown())
            {
                if (PlayerInput.KeyHit(Keys.N))
                {
                    MapEntity firstSelected = SelectedList.First();

                    float minX = firstSelected.WorldRect.X,
                          maxX = firstSelected.WorldRect.Right;

                    foreach (MapEntity entity in SelectedList)
                    {
                        minX = Math.Min(minX, entity.WorldRect.X);
                        maxX = Math.Max(maxX, entity.WorldRect.Right);
                    }

                    float centerX = (minX + maxX) / 2.0f;
                    foreach (MapEntity me in SelectedList)
                    {
                        me.FlipX(false);
                        me.Move(new Vector2((centerX - me.WorldPosition.X) * 2.0f, 0.0f));
                    }
                }
                else if (PlayerInput.KeyHit(Keys.M))
                {
                    MapEntity firstSelected = SelectedList.First();

                    float minY = firstSelected.WorldRect.Y - firstSelected.WorldRect.Height,
                          maxY = firstSelected.WorldRect.Y;

                    foreach (MapEntity entity in SelectedList)
                    {

                        minY = Math.Min(minY, entity.WorldRect.Y - entity.WorldRect.Height);
                        maxY = Math.Max(maxY, entity.WorldRect.Y);
                    }

                    float centerY = (minY + maxY) / 2.0f;
                    foreach (MapEntity me in SelectedList)
                    {
                        me.FlipY(false);
                        me.Move(new Vector2(0.0f, (centerY - me.WorldPosition.Y) * 2.0f));
                    }
                }
            }
        }

        public static void DrawEditor(SpriteBatch spriteBatch, Camera cam)
        {
            if (SelectedList.Count == 1)
            {
                MapEntity firstSelected = SelectedList.First();
                firstSelected.DrawEditing(spriteBatch, cam);
                if (firstSelected.ResizeHorizontal || firstSelected.ResizeVertical)
                {
                    firstSelected.DrawResizing(spriteBatch, cam);
                }
            }
        }

        public static void DeselectAll()
        {
            SelectedList.Clear();
        }

        public static void SelectEntity(MapEntity entity)
        {
            DeselectAll();
            AddSelection(entity);
        }

        /// <summary>
        /// Copy the selected entities to the "clipboard" (copiedList)
        /// </summary>
        public static void Copy(List<MapEntity> entities)
        {
            if (entities.Count == 0) { return; }
            CopyEntities(entities);
        }

        /// <summary>
         /// Copy the entities to the "clipboard" (copiedList) and delete them
         /// </summary>
        public static void Cut(List<MapEntity> entities)
        {
            if (entities.Count == 0) { return; }

            CopyEntities(entities);

            SubEditorScreen.StoreCommand(new AddOrDeleteCommand(new List<MapEntity>(entities), true));

            entities.ForEach(e => { if (!e.Removed) { e.Remove(); } });
            entities.Clear();
        }

        public static void Paste(Vector2 position)
        {
            if (CopiedList.Count == 0) { return; }

            List<MapEntity> prevEntities = new List<MapEntity>(mapEntityList);
            Clone(CopiedList);

            var clones = mapEntityList.Except(prevEntities).ToList();
            var nonWireClones = clones.Where(c => !(c is Item item) || item.GetComponent<Wire>() == null);
            if (!nonWireClones.Any()) { nonWireClones = clones; }

            Vector2 center = Vector2.Zero;
            nonWireClones.ForEach(c => center += c.WorldPosition);
            center = Submarine.VectorToWorldGrid(center / nonWireClones.Count());

            Vector2 moveAmount = Submarine.VectorToWorldGrid(position - center);

            SelectedList = new HashSet<MapEntity>(clones);
            foreach (MapEntity clone in SelectedList)
            {
                clone.Move(moveAmount);
                clone.Submarine = Submarine.MainSub;
            }
            foreach (MapEntity clone in SelectedList)
            {
                (clone as Item)?.GetComponent<ItemContainer>()?.SetContainedItemPositions();
            }

            SubEditorScreen.StoreCommand(new AddOrDeleteCommand(clones, false, handleInventoryBehavior: false));
        }

        /// <summary>
        /// copies a list of entities to the "clipboard" (copiedList)
        /// </summary>
        public static List<MapEntity> CopyEntities(List<MapEntity> entities)
        {
            List<MapEntity> prevEntities = new List<MapEntity>(mapEntityList);

            CopiedList = Clone(entities);

            //find all new entities created during cloning
            var newEntities = mapEntityList.Except(prevEntities).ToList();

            //do a "shallow remove" (removes the entities from the game without removing links between them)
            //  -> items will stay in their containers
            newEntities.ForEach(e => e.ShallowRemove());

            return newEntities;
        }

        public virtual void AddToGUIUpdateList(int order = 0)
        {
            if (editingHUD != null && editingHUD.UserData == this) { editingHUD.AddToGUIUpdateList(order: order); }
        }

        public virtual void UpdateEditing(Camera cam, float deltaTime) { }

        protected static void PositionEditingHUD()
        {
            int maxHeight = 100;
            if (Screen.Selected == GameMain.SubEditorScreen)
            {
                editingHUD.RectTransform.SetPosition(Anchor.TopRight);
                editingHUD.RectTransform.AbsoluteOffset = new Point(0, GameMain.SubEditorScreen.TopPanel.Rect.Bottom);
                maxHeight = (GameMain.GraphicsHeight - GameMain.SubEditorScreen.EntityMenu.Rect.Height) - GameMain.SubEditorScreen.TopPanel.Rect.Bottom * 2 - 20;
            }
            else
            {
                editingHUD.RectTransform.SetPosition(Anchor.TopRight);
                editingHUD.RectTransform.RelativeOffset = new Vector2(0.0f, (HUDLayoutSettings.CrewArea.Bottom + 10.0f) / (editingHUD.RectTransform.Parent ?? GUI.Canvas).Rect.Height);
                maxHeight = HUDLayoutSettings.InventoryAreaLower.Y - HUDLayoutSettings.CrewArea.Bottom - 10;
            }

            var listBox = editingHUD.GetChild<GUIListBox>();
            if (listBox != null)
            {
                int padding = 20;
                int contentHeight = 0;
                foreach (GUIComponent child in listBox.Content.Children)
                {
                    contentHeight += child.Rect.Height + listBox.Spacing;
                    child.RectTransform.MaxSize = new Point(int.MaxValue, child.Rect.Height);
                    child.RectTransform.MinSize = new Point(0, child.Rect.Height);
                }

                editingHUD.RectTransform.Resize(
                    new Point(
                        editingHUD.RectTransform.NonScaledSize.X,
                        MathHelper.Clamp(contentHeight + padding * 2, 50, maxHeight)), resizeChildren: false);
                listBox.RectTransform.Resize(new Point(listBox.RectTransform.NonScaledSize.X, editingHUD.RectTransform.NonScaledSize.Y - padding * 2), resizeChildren: false);
            }
        }

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

                    if (highlighted && PlayerInput.PrimaryMouseButtonDown())
                    {
                        selectionPos = Vector2.Zero;
                        resizeDirX = x;
                        resizeDirY = y;
                        resizing = true;
                        startMovingPos = Vector2.Zero;
                        foreach (var mapEntity in mapEntityList)
                        {
                            if (mapEntity != this) { mapEntity.isHighlighted = false; }
                        }
                    }
                }
            }

            if (resizing)
            {
                if (prevRect == null)
                {
                    prevRect = new Rectangle(Rect.Location, Rect.Size);
                }

                Vector2 placePosition = new Vector2(rect.X, rect.Y);
                Vector2 placeSize = new Vector2(rect.Width, rect.Height);

                Vector2 mousePos = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

                if (PlayerInput.IsShiftDown())
                {
                    mousePos = cam.ScreenToWorld(PlayerInput.MousePosition);
                }

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

                if (!PlayerInput.PrimaryMouseButtonHeld())
                {
                    resizing = false;
                    Resized?.Invoke(rect);
                    if (prevRect != null)
                    {
                        var newData = new List<Rectangle> { Rect };
                        var oldData = new List<Rectangle> { prevRect.Value };
                        SubEditorScreen.StoreCommand(new TransformCommand(new List<MapEntity> { this }, newData, oldData, true));
                    }

                    if (this is Structure structure)
                    {
                        foreach (LightSource light in structure.Lights)
                        {
                            light.LightTextureTargetSize = Rect.Size.ToVector2();
                            light.Position = rect.Location.ToVector2();
                        }
                    }
                    prevRect = null;
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
        public static HashSet<MapEntity> FindSelectedEntities(Vector2 pos, Vector2 size)
        {
            HashSet<MapEntity> foundEntities = new HashSet<MapEntity>();

            Rectangle selectionRect = Submarine.AbsRect(pos, size);

            foreach (MapEntity entity in mapEntityList)
            {
                if (!entity.SelectableInEditor) { continue; }

                if (Submarine.RectsOverlap(selectionRect, entity.rect))
                {
                    foundEntities.Add(entity);
                    entity.IsIncludedInSelection = true;

                    if (SubEditorScreen.IsLayerLinked(entity))
                    {
                        ImmutableHashSet<MapEntity> entitiesInSameLayer = SubEditorScreen.GetEntitiesInSameLayer(entity);
                        foreach (MapEntity layerEntity in entitiesInSameLayer.Where(e => !foundEntities.Contains(e)))
                        {
                            foundEntities.Add(layerEntity);
                            layerEntity.IsIncludedInSelection = true;
                        }
                    }
                }
            }

            return foundEntities;
        }
    }
}
