#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    internal sealed class CircuitBoxUI
    {
        private readonly Camera camera;
        private static readonly Vector2 gridSize = new Vector2(128f);
        public readonly CircuitBox CircuitBox;
        private bool componentMenuOpen;
        private float componentMenuOpenState;

        private GUICustomComponent? circuitComponent;
        private GUIFrame? componentMenu;
        private GUIButton? toggleMenuButton;
        private GUIFrame? selectedWireFrame;
        private GUIListBox? componentList;
        private GUITextBlock? inventoryIndicatorText;
        private readonly Sprite? cursorSprite = GUIStyle.CursorSprite[CursorState.Default];

        private Option<RectangleF> selection = Option.None;
        private string searchTerm = string.Empty;

        public static Option<CircuitBoxWireRenderer> DraggedWire = Option.None;

        public readonly CircuitBoxMouseDragSnapshotHandler MouseSnapshotHandler;

        public List<CircuitBoxWireRenderer> VirtualWires = new();

        public bool Locked => CircuitBox.Locked;

        public CircuitBoxUI(CircuitBox box)
        {
            camera = new Camera
            {
                MinZoom = 0.25f,
                MaxZoom = 2f
            };

            CircuitBox = box;
            MouseSnapshotHandler = new CircuitBoxMouseDragSnapshotHandler(this);
        }

        #region UI

        public void CreateGUI(GUIFrame parent)
        {
            GUIFrame paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.97f, 0.95f), parent.RectTransform, Anchor.Center), style: null);
            circuitComponent = new GUICustomComponent(new RectTransform(Vector2.One, paddedFrame.RectTransform), onDraw: (spriteBatch, component) =>
            {
                Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = component.Rect;

                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable, transformMatrix: camera.Transform);
                DrawCircuits(spriteBatch);
                spriteBatch.End();

                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
                DrawHUD(spriteBatch, component.Rect);
                spriteBatch.End();

                spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
            });

            GUIScissorComponent menuContainer = new GUIScissorComponent(new RectTransform(Vector2.One, paddedFrame.RectTransform, anchor: Anchor.Center))
            {
                CanBeFocused = false
            };

            componentMenuOpen = true;
            componentMenu = new GUIFrame(new RectTransform(new Vector2(1f, 0.4f), menuContainer.Content.RectTransform, Anchor.BottomRight));
            toggleMenuButton = new GUIButton(new RectTransform(new Point(300, 30), GUI.Canvas) { MinSize = new Point(0, 15) }, style: "UIToggleButtonVertical")
            {
                OnClicked = (btn, userdata) =>
                {
                    componentMenuOpen = !componentMenuOpen;
                    if (Locked) { componentMenuOpen = false; }

                    foreach (GUIComponent child in btn.Children)
                    {
                        child.SpriteEffects = componentMenuOpen ? SpriteEffects.None : SpriteEffects.FlipVertically;
                    }

                    return true;
                }
            };

            GUILayoutGroup menuLayout = new GUILayoutGroup(new RectTransform(Vector2.One, componentMenu.RectTransform), childAnchor: Anchor.TopCenter) { RelativeSpacing = 0.02f };
            GUILayoutGroup headerLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.2f), menuLayout.RectTransform), isHorizontal: true);

            GUILayoutGroup labelLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.33f, 1f), headerLayout.RectTransform), isHorizontal: true);

            GUILayoutGroup searchBarLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.33f, 1f), headerLayout.RectTransform), childAnchor: Anchor.CenterLeft, isHorizontal: true);
            GUITextBlock searchBarLabel = new GUITextBlock(new RectTransform(new Vector2(0.15f, 1f), searchBarLayout.RectTransform), "Filter");
            GUITextBox searchbar = new GUITextBox(new RectTransform(new Vector2(0.85f, 1f), searchBarLayout.RectTransform), string.Empty, createClearButton: true);

            new GUIFrame(new RectTransform(new Vector2(0.5f, 0.01f), menuLayout.RectTransform), style: "HorizontalLine");

            componentList = new GUIListBox(new RectTransform(new Vector2(0.95f, 0.65f), menuLayout.RectTransform))
            {
                PlaySoundOnSelect = true,
                UseGridLayout = true,
                OnSelected = (_, o) =>
                {
                    if (o is not ItemPrefab prefab) { return false; }

                    CircuitBox.HeldComponent = Option.Some(prefab);
                    return true;
                }
            };

            GUILayoutGroup inventoryLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.33f, 1f), headerLayout.RectTransform), isHorizontal: true, childAnchor: Anchor.Center);
            GUILayoutGroup indicatorLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.2f, 1f), inventoryLayout.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
            GUIImage indicatorIcon = new GUIImage(new RectTransform(new Vector2(0.5f, 0.8f), indicatorLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "CircuitIndicatorIcon");
            inventoryIndicatorText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), indicatorLayout.RectTransform), GetInventoryText(), font: GUIStyle.SubHeadingFont);

            int gapSize = GUI.IntScale(8);
            selectedWireFrame = SubEditorScreen.CreateWiringPanel(Point.Zero, SelectWire);
            selectedWireFrame.RectTransform.AbsoluteOffset = new Point(parent.Rect.X - (selectedWireFrame.Rect.Width + gapSize), parent.Rect.Y);

            foreach (ItemPrefab prefab in ItemPrefab.Prefabs.OrderBy(static p => p.Name))
            {
                if (!prefab.Tags.Contains("circuitboxcomponent")) { continue; }

                CreateComponentElement(prefab, componentList.Content.RectTransform);
            }

            searchbar.OnTextChanged += (tb, s) =>
            {
                searchTerm = s;
                UpdateComponentList();
                return true;
            };
            int buttonHeight = (int)(GUIStyle.ItemFrameMargin.Y * 0.4f);
            var settingsIcon = new GUIButton(new RectTransform(new Point(buttonHeight), parent.RectTransform, Anchor.TopLeft) { AbsoluteOffset = new Point(buttonHeight / 4), MinSize = new Point(buttonHeight) },
                style: "GUIButtonSettings")
            {
                OnClicked = (btn, userdata) =>
                {
                    GUIContextMenu.CreateContextMenu(
                        new ContextMenuOption("circuitboxsetting.resetview", isEnabled: true, onSelected: ResetCamera)
                        {
                            Tooltip = TextManager.Get("circuitboxsettingdescription.resetview")
                        },
                        new ContextMenuOption("circuitboxsetting.find", isEnabled: true,
                            new ContextMenuOption("circuitboxsetting.focusinput", isEnabled: true, onSelected: () => FindInputOutput(CircuitBoxInputOutputNode.Type.Input))
                            {
                                Tooltip = TextManager.Get("circuitboxsettingdescription.focusinput")
                            },
                            new ContextMenuOption("circuitboxsetting.focusoutput", isEnabled: true, onSelected: () => FindInputOutput(CircuitBoxInputOutputNode.Type.Output))
                            {
                                Tooltip = TextManager.Get("circuitboxsettingdescription.focusoutput")
                            },
                            new ContextMenuOption("circuitboxsetting.focuscircuits", isEnabled: CircuitBox.Components.Any(), onSelected: FindCircuit)
                            {
                                Tooltip = TextManager.Get("circuitboxsettingdescription.focuscircuits")
                            }));


                    void ResetCamera()
                    {
                        // Vector2.One because Vector2.Zero means no value
                        camera.TargetPos = Vector2.One;
                    }

                    void FindInputOutput(CircuitBoxInputOutputNode.Type type)
                    {
                        var input = CircuitBox.InputOutputNodes.FirstOrDefault(n => n.NodeType == type);
                        if (input is null) { return; }

                        camera.TargetPos = input.Position;
                    }

                    void FindCircuit()
                    {
                        var closestComponent = CircuitBox.Components.MinBy(c => Vector2.DistanceSquared(c.Position, camera.Position));
                        if (closestComponent is null) { return; }

                        camera.TargetPos = closestComponent.Position;
                    }

                    return true;
                }
            };

            MouseSnapshotHandler.UpdateConnections();

            // update scales of everything
            foreach (var node in CircuitBox.Components) { node.OnUICreated(); }

            foreach (var node in CircuitBox.InputOutputNodes) { node.OnUICreated(); }

            foreach (var wire in CircuitBox.Wires) { wire.Update(); }
        }

        private string GetInventoryText() =>
            CircuitBox.ComponentContainer is { } container
                ? $"{container.Inventory.AllItems.Count()}/{container.Capacity}"
                : "0/0";

        public void UpdateComponentList()
        {
            if (inventoryIndicatorText is { } text)
            {
                text.Text = GetInventoryText();
            }

            if (componentList is null) { return; }

            var playerInventory = CircuitBox.GetSortedCircuitBoxItemsFromPlayer(Character.Controlled);

            foreach (GUIComponent child in componentList.Content.Children)
            {
                if (child.UserData is not ItemPrefab prefab) { continue; }

                child.Enabled = !CircuitBox.IsFull && (!CircuitBox.IsInGame() || CircuitBox.GetApplicableResourcePlayerHas(prefab, playerInventory).IsSome());

                if (child.GetChild<GUILayoutGroup>()?.GetChild<GUIImage>() is { } image)
                {
                    image.Enabled = child.Enabled;
                }

                child.ToolTip = child.Enabled
                    ? prefab.Description
                    : RichString.Rich(TextManager.GetWithVariable(new Identifier("CircuitBoxUIComponentNotAvailable"), new Identifier("[item]"), prefab.Name));

                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    child.Visible = true;
                    continue;
                }

                child.Visible = prefab.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool SelectWire(GUIComponent component, object obj)
        {
            if (obj is not ItemPrefab prefab) { return false; }

            CircuitBoxWire.SelectedWirePrefab = prefab;
            return true;
        }

        private static void CreateComponentElement(ItemPrefab prefab, RectTransform parent)
        {
            GUIFrame itemFrame = new GUIFrame(new RectTransform(new Vector2(0.1f, 0.9f), parent) { MinSize = new Point(0, 50) }, style: "GUITextBox")
            {
                UserData = prefab
            };

            itemFrame.RectTransform.MinSize = new Point(0, itemFrame.Rect.Width);
            itemFrame.RectTransform.MaxSize = new Point(int.MaxValue, itemFrame.Rect.Width);
            itemFrame.ToolTip = prefab.Name;

            GUILayoutGroup paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.8f), itemFrame.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                Stretch = true,
                RelativeSpacing = 0.03f,
                CanBeFocused = false
            };

            Sprite icon;
            Color iconColor;

            if (prefab.InventoryIcon != null)
            {
                icon = prefab.InventoryIcon;
                iconColor = prefab.InventoryIconColor;
            }
            else
            {
                icon = prefab.Sprite;
                iconColor = prefab.SpriteColor;
            }

            GUIImage? img = null;
            if (icon != null)
            {
                img = new GUIImage(new RectTransform(new Vector2(1.0f, 0.8f), paddedFrame.RectTransform, Anchor.TopCenter), icon)
                {
                    CanBeFocused = false,
                    LoadAsynchronously = true,
                    DisabledColor = Color.DarkGray * 0.8f,
                    Color = iconColor
                };
            }

            GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform, Anchor.BottomCenter),
                text: prefab.Name, textAlignment: Alignment.Center, font: GUIStyle.SmallFont)
            {
                CanBeFocused = false
            };

            textBlock.Text = ToolBox.LimitString(textBlock.Text, textBlock.Font, textBlock.Rect.Width);
            paddedFrame.Recalculate();

            if (img != null)
            {
                img.Scale = Math.Min(Math.Min(img.Rect.Width / img.Sprite.size.X, img.Rect.Height / img.Sprite.size.Y), 1.5f);
                img.RectTransform.NonScaledSize = new Point((int)(img.Sprite.size.X * img.Scale), img.Rect.Height);
            }
        }

        #endregion

        private void DrawHUD(SpriteBatch spriteBatch, Rectangle screenRect)
        {
            float scale = GUI.Scale / 1.5f;
            Vector2 offset = new Vector2(20, 40) * scale;

            foreach (var (character, cursor) in CircuitBox.ActiveCursors)
            {
                if (!cursor.IsActive) { continue; }

                Vector2 cursorWorldPos = camera.WorldToScreen(cursor.DrawPosition);

                if (cursor.Info.DragStart.TryUnwrap(out Vector2 dragStart))
                {
                    DrawSelection(spriteBatch, dragStart, cursor.DrawPosition, cursor.Color);
                }

                if (cursor.HeldPrefab.TryUnwrap(out ItemPrefab? otherHeldPrefab))
                {
                    otherHeldPrefab.Sprite.Draw(spriteBatch, cursorWorldPos);
                }

                cursorSprite?.Draw(spriteBatch, cursorWorldPos, cursor.Color, 0f, scale);
                GUI.DrawString(spriteBatch, cursorWorldPos + offset, character.Name, cursor.Color, Color.Black, GUI.IntScale(4), GUIStyle.SmallFont);
            }

            if (selection.TryUnwrap(out RectangleF rect))
            {
                Vector2 pos1 = rect.Location;
                Vector2 pos2 = new Vector2(rect.Location.X + rect.Size.X, rect.Location.Y + rect.Size.Y);
                DrawSelection(spriteBatch, pos1, pos2, GUIStyle.Blue);
            }

            if (CircuitBox.HeldComponent.TryUnwrap(out ItemPrefab? component))
            {
                component.Sprite.Draw(spriteBatch, PlayerInput.MousePosition);
            }
            if (PlayerInput.PrimaryMouseButtonHeld() && MouseSnapshotHandler.LastConnectorUnderCursor.IsSome())
            {
                CircuitBoxWire.SelectedWirePrefab.Sprite.Draw(spriteBatch, PlayerInput.MousePosition, CircuitBoxWire.SelectedWirePrefab.SpriteColor, scale: camera.Zoom);
            }

            foreach (var c in CircuitBox.Components)
            {
                c.DrawHUD(spriteBatch, camera);
            }

            foreach (var n in CircuitBox.InputOutputNodes)
            {
                n.DrawHUD(spriteBatch, camera);
            }

            if (Locked)
            {
                LocalizedString lockedText = TextManager.Get("CircuitBoxLocked")
                    .Fallback(TextManager.Get("ConnectionLocked"), useDefaultLanguageIfFound: false);

                Vector2 size = GUIStyle.LargeFont.MeasureString(lockedText);
                Vector2 pos = new Vector2(screenRect.Center.X - size.X / 2, screenRect.Top + screenRect.Height * 0.05f);
                GUI.DrawString(spriteBatch, pos, lockedText, Color.Red, Color.Black, 8, GUIStyle.LargeFont);
            }
        }

        private void DrawSelection(SpriteBatch spriteBatch, Vector2 pos1, Vector2 pos2, Color color)
        {
            Vector2 location = camera.WorldToScreen(pos1);
            location.Y = -location.Y;
            Vector2 location2 = camera.WorldToScreen(pos2);
            location2.Y = -location2.Y;
            MapEntity.DrawSelectionRect(spriteBatch, location, new Vector2(-(location.X - location2.X), location.Y - location2.Y), color);
        }

        private const float lineBaseWidth = 1f;
        private static float lineWidth;

        public static void DrawRectangleWithBorder(SpriteBatch spriteBatch, RectangleF rect, Color fillColor, Color borderColor)
        {
            GUI.DrawFilledRectangle(spriteBatch, rect, fillColor);
            DrawRectangleOnlyBorder(spriteBatch, rect, borderColor);
        }

        private static void DrawRectangleOnlyBorder(SpriteBatch spriteBatch, RectangleF rect, Color borderColor)
        {
            Vector2 topRight = new Vector2(rect.Right, rect.Top),
                    topLeft = new Vector2(rect.Left, rect.Top),
                    bottomRight = new Vector2(rect.Right, rect.Bottom),
                    bottomLeft = new Vector2(rect.Left, rect.Bottom);

            Vector2 offset = new Vector2(0f, lineWidth / 2f);

            spriteBatch.DrawLine(topRight, topLeft, borderColor, thickness: lineWidth);
            spriteBatch.DrawLine(topLeft - offset, bottomLeft + offset, borderColor, thickness: lineWidth);
            spriteBatch.DrawLine(bottomLeft, bottomRight, borderColor, thickness: lineWidth);
            spriteBatch.DrawLine(bottomRight + offset, topRight - offset, borderColor, thickness: lineWidth);
        }

        private void DrawCircuits(SpriteBatch spriteBatch)
        {
            camera.UpdateTransform(interpolate: true, updateListener: false);
            SubEditorScreen.DrawOutOfBoundsArea(spriteBatch, camera, CircuitBoxSizes.PlayableAreaSize, GUIStyle.Red * 0.33f);
            SubEditorScreen.DrawGrid(spriteBatch, camera, gridSize.X, gridSize.Y, zoomTreshold: false);
            lineWidth = lineBaseWidth / camera.Zoom;

            Vector2 mousePos = GetCursorPosition();
            mousePos.Y = -mousePos.Y;

            foreach (var label in CircuitBox.Labels)
            {
                if (label.IsSelected)
                {
                    label.DrawSelection(spriteBatch, GetSelectionColor(label));
                }

                label.Draw(spriteBatch, label.Position, label.Color);
            }

            foreach (CircuitBoxWire wire in CircuitBox.Wires)
            {
                wire.Renderer.Draw(spriteBatch, GetSelectionColor(wire));
            }

            foreach (var node in CircuitBox.Components)
            {
                if (node.IsSelected)
                {
                    node.DrawSelection(spriteBatch, GetSelectionColor(node));
                }

                node.Draw(spriteBatch, node.Position, node.Item.Prefab.SignalComponentColor * CircuitBoxNode.Opacity);
            }

            foreach (var ioNode in CircuitBox.InputOutputNodes)
            {
                if (ioNode.IsSelected)
                {
                    ioNode.DrawSelection(spriteBatch, GetSelectionColor(ioNode));
                }

                Color color = ioNode.NodeType is CircuitBoxInputOutputNode.Type.Input ? GUIStyle.Green : GUIStyle.Red;
                ioNode.Draw(spriteBatch, ioNode.Position, color * CircuitBoxNode.Opacity);
            }

            if (MouseSnapshotHandler.IsDragging)
            {
                var draggedNodes = MouseSnapshotHandler.GetMoveAffectedComponents();
                Vector2 dragOffset = MouseSnapshotHandler.GetDragAmount(GetCursorPosition());
                foreach (CircuitBoxNode moveable in draggedNodes)
                {
                    Color color = moveable switch
                    {
                        CircuitBoxComponent node => node.Item.Prefab.SignalComponentColor,
                        CircuitBoxLabelNode label => label.Color,
                        CircuitBoxInputOutputNode ioNode => ioNode.NodeType is CircuitBoxInputOutputNode.Type.Input ? GUIStyle.Green : GUIStyle.Red,
                        _ => Color.White
                    };
                    moveable.Draw(spriteBatch, moveable.Position + dragOffset, color * 0.5f);
                }
            }

            if (MouseSnapshotHandler.IsResizing && MouseSnapshotHandler.LastResizeAffectedNode.TryUnwrap(out var resize))
            {
                var (dir, node) = resize;
                Vector2 dragOffset = MouseSnapshotHandler.GetDragAmount(GetCursorPosition());

                var rect = node.Rect;
                rect.Y = -rect.Y;
                rect.Y -= rect.Height;

                if (dir.HasFlag(CircuitBoxResizeDirection.Down))
                {
                    rect.Height -= dragOffset.Y;
                    rect.Height = Math.Max(rect.Height, CircuitBoxLabelNode.MinSize.Y + CircuitBoxSizes.NodeHeaderHeight);
                }

                if (dir.HasFlag(CircuitBoxResizeDirection.Right))
                {
                    rect.Width += dragOffset.X;
                    rect.Width = Math.Max(rect.Width, CircuitBoxLabelNode.MinSize.X);
                }

                if (dir.HasFlag(CircuitBoxResizeDirection.Left))
                {
                    float oldWidth = rect.Width;
                    rect.Width -= dragOffset.X;
                    rect.Width = Math.Max(rect.Width, CircuitBoxLabelNode.MinSize.X);

                    float actualResize = rect.Width - oldWidth;
                    rect.X -= actualResize;
                }

                DrawRectangleOnlyBorder(spriteBatch, rect, GUIStyle.Yellow);
            }

            if (DraggedWire.TryUnwrap(out CircuitBoxWireRenderer? draggedWire))
            {
                draggedWire.Draw(spriteBatch, GUIStyle.Yellow);
            }
        }

        private Color GetSelectionColor(CircuitBoxNode node) => GetSelectionColor(node.SelectedBy, node.IsSelectedByMe);

        private Color GetSelectionColor(CircuitBoxWire wire) => GetSelectionColor(wire.SelectedBy, wire.IsSelectedByMe);

        private Color GetSelectionColor(ushort selectedBy, bool isSelectedByMe)
        {
#if !DEBUG
            if (isSelectedByMe)
            {
                return GUIStyle.Yellow;
            }
#endif

            foreach (var (_, cursor) in CircuitBox.ActiveCursors)
            {
                if (cursor.Info.CharacterID == selectedBy)
                {
                    return cursor.Color;
                }
            }

            return GUIStyle.Yellow;
        }

        private Vector2 cursorPos;
        public Vector2 GetCursorPosition() => cursorPos;
        public Option<Vector2> GetDragStart() => selection.Select(static f => f.Location);

        public void Update(float deltaTime)
        {
            cursorPos = camera.ScreenToWorld(PlayerInput.MousePosition);
            foreach (CircuitBoxWire wire in CircuitBox.Wires)
            {
                wire.Update();
            }

            bool foundSelected = false;
            foreach (var node in CircuitBox.Components)
            {
                if (!node.IsSelectedByMe) { continue; }

                foundSelected = true;
                if (circuitComponent is not null)
                {
                    node.UpdateEditing(circuitComponent.RectTransform);
                }

                break;
            }

            if (!foundSelected)
            {
                CircuitBoxComponent.RemoveEditingHUD();
            }

            bool isMouseOn = GUI.MouseOn == circuitComponent;

            if (isMouseOn)
            {
                Character.DisableControls = true;
            }

            camera.MoveCamera(deltaTime, allowMove: true, allowZoom: isMouseOn, allowInput: isMouseOn, followSub: false);

            if (camera.TargetPos != Vector2.Zero && MathUtils.NearlyEqual(camera.Position, camera.TargetPos, 0.01f))
            {
                camera.TargetPos = Vector2.Zero;
            }

            if (isMouseOn)
            {
                if (PlayerInput.PrimaryMouseButtonDown())
                {
                    if (CircuitBox.HeldComponent.IsNone())
                    {
                        MouseSnapshotHandler.StartDragging();
                    }
                    else
                    {
                        MouseSnapshotHandler.ClearSnapshot();
                    }
                }

                if (PlayerInput.DoubleClicked() && MouseSnapshotHandler.FindWireUnderCursor(cursorPos).IsNone())
                {
                    var topmostNode = GetTopmostNode(MouseSnapshotHandler.FindNodesUnderCursor(cursorPos));
                    if (topmostNode is CircuitBoxLabelNode label && circuitComponent is not null)
                    {
                        label.PromptEditText(circuitComponent);
                    }
                }

                if (PlayerInput.MidButtonHeld() || (PlayerInput.IsAltDown() && PlayerInput.PrimaryMouseButtonHeld()))
                {
                    Vector2 moveSpeed = PlayerInput.MouseSpeed / camera.Zoom;
                    moveSpeed.X = -moveSpeed.X;
                    camera.Position += moveSpeed;
                }

                if (PlayerInput.PrimaryMouseButtonHeld())
                {
                    MouseSnapshotHandler.UpdateDrag(GetCursorPosition());
                }

                if (MouseSnapshotHandler.IsWiring && MouseSnapshotHandler.LastConnectorUnderCursor.TryUnwrap(out var c))
                {
                    Vector2 start = c.Rect.Center,
                            end = GetCursorPosition();

                    end.Y = -end.Y;

                    if (!c.IsOutput)
                    {
                        (start, end) = (end, start);
                    }

                    if (DraggedWire.TryUnwrap(out var wire))
                    {
                        wire.Recompute(start, end, CircuitBoxWire.SelectedWirePrefab.SpriteColor);
                    }
                    else
                    {
                        DraggedWire = Option.Some(new CircuitBoxWireRenderer(Option.None, start, end, GUIStyle.Red, CircuitBox.WireSprite));
                    }
                }
                else
                {
                    DraggedWire = Option.None;
                }

                if (PlayerInput.SecondaryMouseButtonClicked())
                {
                    OpenContextMenu();
                }

                if (PlayerInput.PrimaryMouseButtonClicked())
                {
                    bool selectedNode = false;
                    if (MouseSnapshotHandler.IsResizing && MouseSnapshotHandler.LastResizeAffectedNode.TryUnwrap(out var r))
                    {
                        var (dir, node) = r;
                        CircuitBox.ResizeNode(node, dir, MouseSnapshotHandler.GetDragAmount(cursorPos));
                    }

                    if (CircuitBox.HeldComponent.TryUnwrap(out ItemPrefab? prefab))
                    {
                        CircuitBox.AddComponent(prefab, cursorPos);
                    }
                    else
                    {
                        if (MouseSnapshotHandler.IsDragging && PlayerInput.PrimaryMouseButtonReleased())
                        {
                            CircuitBox.MoveComponent(MouseSnapshotHandler.GetDragAmount(cursorPos), MouseSnapshotHandler.GetMoveAffectedComponents());
                        }
                        else if (!MouseSnapshotHandler.IsWiring)
                        {
                            selectedNode = TrySelectComponentsUnderCursor();
                        }
                    }

                    if (MouseSnapshotHandler.IsWiring && MouseSnapshotHandler.LastConnectorUnderCursor.TryUnwrap(out var one))
                    {
                        if (MouseSnapshotHandler.FindConnectorUnderCursor(cursorPos).TryUnwrap(out var two))
                        {
                            CircuitBox.AddWire(one, two);
                        }
                    }
                    
                    if (MouseSnapshotHandler.LastWireUnderCursor.TryUnwrap(out var wire) && !MouseSnapshotHandler.IsDragging && !selectedNode)
                    {
                        CircuitBox.SelectWires(ImmutableArray.Create(wire), !PlayerInput.IsShiftDown());
                    }
                    else if (CircuitBox.Wires.Any(static wire => wire.IsSelectedByMe))
                    {
                        CircuitBox.SelectWires(ImmutableArray<CircuitBoxWire>.Empty, !PlayerInput.IsShiftDown());
                    }

                    CircuitBox.HeldComponent = Option.None;
                    MouseSnapshotHandler.EndDragging();
                }

                if (MouseSnapshotHandler.GetLastComponentsUnderCursor().IsEmpty && MouseSnapshotHandler.LastConnectorUnderCursor.IsNone())
                {
                    UpdateSelection();
                }

                // Allow using both Delete key and Ctrl+D for those who don't have a Delete key
                bool hitDeleteCombo = PlayerInput.KeyHit(Keys.Delete) || (PlayerInput.IsCtrlDown() && PlayerInput.KeyHit(Keys.D));

                if (GUI.KeyboardDispatcher.Subscriber is null && hitDeleteCombo)
                {
                    CircuitBox.RemoveComponents(CircuitBox.Components.Where(static node => node.IsSelectedByMe).ToArray());
                    CircuitBox.RemoveWires(CircuitBox.Wires.Where(static wire => wire.IsSelectedByMe).ToImmutableArray());
                    CircuitBox.RemoveLabel(CircuitBox.Labels.Where(static label => label.IsSelectedByMe).ToImmutableArray());
                }
            }

            if (componentMenu is { } menu && toggleMenuButton is { } button)
            {
                button.Enabled = !Locked;
                componentMenuOpenState = componentMenuOpen && !Locked ? Math.Min(componentMenuOpenState + deltaTime * 5.0f, 1.0f) : Math.Max(componentMenuOpenState - deltaTime * 5.0f, 0.0f);

                menu.RectTransform.ScreenSpaceOffset = Vector2.Lerp(new Vector2(0.0f, menu.Rect.Height - 10), Vector2.Zero, componentMenuOpenState).ToPoint();
                button.RectTransform.AbsoluteOffset = new Point(menu.Rect.X + ((menu.Rect.Width / 2) - (button.Rect.Width / 2)), menu.Rect.Y - button.Rect.Height);
            }
            
            if (selectedWireFrame is { } wireFrame)
            {
                wireFrame.Visible = !Locked;
            }

            camera.Position = Vector2.Clamp(camera.Position,
                new Vector2(-CircuitBoxSizes.PlayableAreaSize / 2f),
                new Vector2(CircuitBoxSizes.PlayableAreaSize / 2f));
        }

        public void SetMenuVisibility(bool state)
            => componentMenuOpen = state;

        private void UpdateSelection()
        {
            if (!PlayerInput.IsAltDown() && PlayerInput.PrimaryMouseButtonDown())
            {
                selection = Option.Some(new RectangleF(GetCursorPosition(), Vector2.Zero));
            }

            if (!selection.TryUnwrap(out RectangleF rect)) { return; }

            if (!PlayerInput.PrimaryMouseButtonHeld())
            {
                selection = Option.None;
                RectangleF selectionRect = Submarine.AbsRectF(rect.Location, rect.Size);

                float treshold = 12f / camera.Zoom;
                if (selectionRect.Size.X < treshold || selectionRect.Size.Y < treshold) { return; }

                CircuitBox.SelectComponents(MouseSnapshotHandler.Nodes.Where(n => selectionRect.Intersects(n.Rect)).ToImmutableHashSet(), !PlayerInput.IsShiftDown());
            }
            else
            {
                RectangleF oldRect = rect;
                rect.Size = camera.ScreenToWorld(PlayerInput.MousePosition) - rect.Location;
                if (rect.Equals(oldRect)) { return; }

                selection = Option.Some(rect);
            }
        }

        private bool TrySelectComponentsUnderCursor()
        {
            CircuitBoxNode? foundNode = GetTopmostNode(MouseSnapshotHandler.GetLastComponentsUnderCursor());
            
            if (foundNode is CircuitBoxLabelNode && MouseSnapshotHandler.LastWireUnderCursor.IsSome())
            {
                foundNode = null;
            }

            CircuitBox.SelectComponents(foundNode is null ? ImmutableArray<CircuitBoxNode>.Empty : ImmutableArray.Create(foundNode), !PlayerInput.IsShiftDown());
            return foundNode is not null;
        }

        private void OpenContextMenu()
        {
            var wireOption = MouseSnapshotHandler.FindWireUnderCursor(cursorPos);
            var wireSelection = CircuitBox.Wires.Where(static w => w.IsSelectedByMe).ToImmutableArray();
            var nodeOption = GetTopmostNode(MouseSnapshotHandler.FindNodesUnderCursor(cursorPos));
            var nodeSelection = CircuitBox.Components.Where(static n => n.IsSelectedByMe).ToImmutableArray();
            var labels = CircuitBox.Labels.Where(static l => l.IsSelectedByMe).ToImmutableArray();

            var option = new ContextMenuOption(TextManager.Get("delete"), isEnabled: (wireOption.IsSome() || nodeOption is CircuitBoxComponent or CircuitBoxLabelNode) && !Locked, () =>
            {
                if (wireOption.TryUnwrap(out var wire))
                {
                    CircuitBox.RemoveWires(wire.IsSelected ? wireSelection : ImmutableArray.Create(wire));
                }

                switch (nodeOption)
                {
                    case CircuitBoxComponent node:
                        CircuitBox.RemoveComponents(node.IsSelected ? nodeSelection : ImmutableArray.Create(node));
                        break;
                    case CircuitBoxLabelNode label:
                        CircuitBox.RemoveLabel(label.IsSelected ? labels : ImmutableArray.Create(label));
                        break;
                }
            });

            var editLabel = new ContextMenuOption(TextManager.Get("circuitboxeditlabel"), isEnabled: nodeOption is CircuitBoxLabelNode && !Locked, () =>
            {
                if (circuitComponent is null) { return; }
                if (nodeOption is not CircuitBoxLabelNode label) { return; }

                label.PromptEditText(circuitComponent);
            });

            var editConnections = new ContextMenuOption(TextManager.Get("circuitboxrenameconnections"), isEnabled: nodeOption is CircuitBoxInputOutputNode && !Locked, () =>
            {
                if (circuitComponent is null) { return; }
                if (nodeOption is not CircuitBoxInputOutputNode io) { return; }

                io.PromptEdit(circuitComponent);
            });

            var addLabelOption = new ContextMenuOption(TextManager.Get("circuitboxaddlabel"), isEnabled: !Locked, () =>
            {
                CircuitBox.AddLabel(cursorPos);
            });

            ContextMenuOption[] allOptions = { addLabelOption, editLabel, editConnections, option };

            // show component name in the header to better indicate what is about to be deleted
            if (nodeOption is CircuitBoxComponent comp)
            {
                GUIContextMenu.CreateContextMenu(PlayerInput.MousePosition, comp.Item.Name, comp.Item.Prefab.SignalComponentColor, allOptions);
                return;
            }

            // also check if a wire is being deleted
            if (wireOption.TryUnwrap(out var foundWire))
            {
                GUIContextMenu.CreateContextMenu(PlayerInput.MousePosition, foundWire.UsedItemPrefab.Name, foundWire.Color, allOptions);
                return;
            }

            GUIContextMenu.CreateContextMenu(allOptions);
        }

        public CircuitBoxNode? GetTopmostNode(ImmutableHashSet<CircuitBoxNode> nodes)
        {
            CircuitBoxNode? foundNode = null;

            var allNodes = MouseSnapshotHandler.Nodes.ToImmutableArray();

            for (int i = allNodes.Length - 1; i >= 0; i--)
            {
                CircuitBoxNode node = allNodes[i];

                if (nodes.Contains(node))
                {
                    foundNode = node;
                    break;
                }
            }

            return foundNode;
        }

        public void AddToGUIUpdateList()
        {
            toggleMenuButton?.AddToGUIUpdateList();
            selectedWireFrame?.AddToGUIUpdateList();
        }
    }
}