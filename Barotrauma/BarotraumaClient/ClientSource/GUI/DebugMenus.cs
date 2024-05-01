using Barotrauma.Extensions;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    public enum InspectorMode
    {
        Disabled = 0,
        Entities = 1,
        GUI = 2
    }

    public static class DebugMenus
    {
        // TODO: Debug infos/overlays toggles window
        // TODO: Item spawner window
        // TODO: GUIComponent editors

        public static InspectorMode inspectorMode;

        private static readonly List<GUIFrame> entityExplorerWindows = new List<GUIFrame>();
        private static readonly Dictionary<ISerializableEntity, GUIFrame> entityEditorWindows = new Dictionary<ISerializableEntity, GUIFrame>();
        private static readonly Dictionary<GUIComponent, GUIFrame> guiExplorerWindows = new Dictionary<GUIComponent, GUIFrame>();
        private static readonly List<GUITextBlock> guiExplorerEntries = new List<GUITextBlock>();

        private static Vector2 CursorPosWorld => Screen.Selected?.Cam?.ScreenToWorld(PlayerInput.MousePosition) ?? PlayerInput.MousePosition;
        private static IEnumerable<Entity> Entities => Entity.GetEntities();
        private static IEnumerable<Entity> EntitiesUnderCursor => Entities.Where(e => e switch
        {
            MapEntity m => m is not Item { IsContained: true } && m.WorldRect.ContainsWorld(CursorPosWorld),
            Character c => c.AnimController.Limbs.Any(l => GameMain.World.TestPointAll(ConvertUnits.ToSimUnits(CursorPosWorld) - (c.Submarine is Submarine sub ? sub.SimPosition : Vector2.Zero)).Select(f => f.Body).Contains(l.body.FarseerBody)),
            Submarine s => s.VisibleBorders.ContainsWorld(CursorPosWorld - s.WorldPosition),
            _ => false
        });

        public static void CloseAll()
        {
            inspectorMode = InspectorMode.Disabled;
            entityExplorerWindows.Clear();
            entityEditorWindows.Clear();
            guiExplorerWindows.Clear();
        }

        #region Object Explorers
        internal static void CreateEntityExplorer(IEnumerable<Entity> entities = null)
        {
            GUILayoutGroup content = CreateWindowBase(out GUIFrame window);
            GUIButton refreshButton = entities is null ? new(new(Vector2.UnitX, content.RectTransform), TextManager.Get("ReloadLinkedSub")) : null;

            GUITextBox nameFilterBox = CreateFilterBox(content);

            GUIListBox list = new(new(Vector2.One, content.RectTransform));
            list.OnSelected += (component, obj) =>
            {
                if (obj is Submarine sub) DebugConsole.NewMessage(sub.VisibleBorders.ToString());
                if (Entity.GetEntities().Contains(obj) && !(obj as Entity).Removed)
                {
                    return obj is ISerializableEntity entity && TryCreateEditorWindow(entity);
                }
                else
                {
                    component.RectTransform.Parent = null;
                    return false;
                }
            };

            nameFilterBox.OnTextChanged += (textBox, text) =>
            {
                FilterEntries(list, text);
                return true;
            };

            GUIButton closeButton = new(new(Vector2.UnitX, content.RectTransform), TextManager.Get("Close"))
            {
                OnClicked = (_, _) => entityExplorerWindows.Remove(window)
            };

            if (refreshButton is not null)
            {
                refreshButton.OnClicked = (_, _) =>
                {
                    RecreateEntityList(list, entities ?? Entities);
                    FilterEntries(list, nameFilterBox.Text);
                    return true;
                };
            }

            entityExplorerWindows.Add(window);
            RecreateEntityList(list, entities ?? Entities);
        }

        private static void RecreateEntityList(GUIListBox list, IEnumerable<Entity> entities)
        {
            list.ClearChildren();
            foreach (Entity entity in entities)
            {
                GUITextBlock entry = CreateListEntry(list, entity, out GUILayoutGroup right);
                entry.Text = RichString.Rich($"{entity.GetName()} (‖color:{entity.GetColor()}‖{entity.GetType().Name}‖end‖) {entity.WorldPosition.ToPoint()}");

                new GUIButton(new(new Point(right.Rect.Height), right.RectTransform), style: "GUIMinusButton", color: GUIStyle.Red)
                {
                    ToolTip = TextManager.Get("BanListRemove"),
                    OnClicked = (_, _) =>
                    {
                        entry.RectTransform.Parent = null;
                        if (!entity.Removed) entity.Remove();
                        return true;
                    }
                };

                new GUITextBlock(new(new Vector2(0.1f, 1), right.RectTransform), entity.ID.ToString(), Color.Gray, textAlignment: Alignment.Right)
                {
                    CanBeFocused = false
                };
            }
        }
        #endregion

        #region Entity Editors
        private static bool TryCreateEditorWindow(ISerializableEntity entity)
        {
            if (entityEditorWindows.ContainsKey(entity))
            {
                entityEditorWindows[entity].Flash(GUIStyle.Green);
                return false;
            }

            GUILayoutGroup content = CreateWindowBase(out GUIFrame window);

            GUIButton editorListRefresh = new(new(Vector2.UnitX, content.RectTransform), TextManager.Get("ReloadLinkedSub"));

            GUIListBox editorList = new(new(Vector2.One, content.RectTransform));

            GUIButton closeButton = new(new(Vector2.UnitX, content.RectTransform), TextManager.Get("Close"))
            {
                OnClicked = (_, _) => entityEditorWindows.Remove(entity)
            };

            editorListRefresh.OnClicked = (_, _) =>
            {
                RefreshEditor(editorList, entity);
                return true;
            };

            RefreshEditor(editorList, entity);
            return entityEditorWindows.TryAdd(entity, window);
        }

        private static void RefreshEditor(GUIListBox list, ISerializableEntity entity)
        {
            list.ClearChildren();
            new SerializableEntityEditor(list.Content.RectTransform, entity, false, true);
            if (entity is Item item)
            {
                item.Components.ForEach(component => new SerializableEntityEditor(list.Content.RectTransform, component, false, true));
            }

            if (entity is Character character)
            {
                new SerializableEntityEditor(list.Content.RectTransform, character.Params, false, true);
                new SerializableEntityEditor(list.Content.RectTransform, character.AnimController.RagdollParams, false, true);
                character.AnimController.Limbs.ForEach(limb =>
                {
                    new SerializableEntityEditor(list.Content.RectTransform, limb, false, true);
                    limb.DamageModifiers.ForEach(mod => new SerializableEntityEditor(list.Content.RectTransform, mod, false, true));
                });
            }
        }
        #endregion

        #region GUI Explorers
        public static bool TryCreateGUIExplorer(GUIComponent component)
        {
            if (guiExplorerWindows.ContainsKey(component))
            {
                guiExplorerWindows[component].Flash(GUIStyle.Green);
                return false;
            }

            GUILayoutGroup content = CreateWindowBase(out GUIFrame window);

            GUIButton refreshButton = new(new(Vector2.UnitX, content.RectTransform), TextManager.Get("ReloadLinkedSub"));

            GUITextBox nameFilterBox = CreateFilterBox(content);

            GUIListBox list = new(new(Vector2.One, content.RectTransform));

            nameFilterBox.OnTextChanged += (textBox, text) =>
            {
                FilterEntries(list, text);
                return true;
            };

            GUIButton closeButton = new(new(Vector2.UnitX, content.RectTransform), TextManager.Get("Close"))
            {
                OnClicked = (_, _) => guiExplorerWindows.Remove(component)
            };

            refreshButton.OnClicked = (_, _) =>
            {
                RecreateGUIComponentList(list, component, nameFilterBox);
                FilterEntries(list, nameFilterBox.Text);
                return true;
            };

            RecreateGUIComponentList(list, component, nameFilterBox);
            return guiExplorerWindows.TryAdd(component, window);
        }

        private static void RecreateGUIComponentList(GUIListBox list, GUIComponent component, GUITextBox filter)
        {
            IEnumerable<GUIComponent> parentHeirarchy = new List<GUIComponent>();
            GUIComponent parent = component;
            while (parent is not null)
            {
                parentHeirarchy = parentHeirarchy.Prepend(parent);
                parent = parent.Parent;
            }

            list.ClearChildren();
            foreach (GUIComponent component1 in parentHeirarchy.Concat(component.GetAllChildren()))
            {
                if (component1 == component && component1.RectTransform.Parent is not null) DebugConsole.NewMessage(component1.RectTransform.Parent.GetType().Name);
                if (component1 == component && component1.Parent is not null) DebugConsole.NewMessage(component1.Parent.GetType().Name);
                GUITextBlock entry = CreateListEntry(list, component1, out GUILayoutGroup right);
                entry.Text = RichString.Rich($"‖color:gui.green‖{component1.GetType().Name}‖end‖ {(component1.Style is not null ? $"({component1.Style.Name})" : "")}");

                new GUIButton(new(new Point(right.Rect.Height), right.RectTransform), style: "GUIMinusButton", color: GUIStyle.Red)
                {
                    Enabled = component1.RectTransform.Parent != GUI.Canvas,
                    ToolTip = component1.RectTransform.Parent != GUI.Canvas ? "Deparent" : "Disabled: Cannot orphan children of the GUI canvas.",
                    OnClicked = (_, _) =>
                    {
                        component1.RectTransform.Parent = null;
                        if (parentHeirarchy.Contains(component1))
                        {
                            guiExplorerWindows.Remove(component);
                            if (component1.Parent is not null)
                            {
                                TryCreateGUIExplorer(component1.Parent);
                            }
                        }
                        else
                        {
                            RecreateGUIComponentList(list, component, filter);
                            FilterEntries(list, filter.Text);
                        }
                        return true;
                    }
                };

                GUITickBox visibleCheck = new(new(new Point(right.Rect.Height), right.RectTransform), "", style: "GUITickBoxNoMinimum")
                {
                    ToolTip = TextManager.Get("VisibleSubmarines"),
                    Selected = component1.Visible,
                    OnSelected = (obj) =>
                    {
                        component1.Visible = obj.Selected;
                        return true;
                    }
                };

                new GUITextBlock(new(new Vector2(0.1f, 1), right.RectTransform), component1.UpdateOrder.ToString(), Color.Gray, textAlignment: Alignment.Right)
                {
                    CanBeFocused = false
                };

                if (component1 == component)
                {
                    entry.Flash(GUIStyle.Green);
                }

                guiExplorerEntries.Add(entry);
            }
        }
        #endregion

        #region Shared Components
        private static GUILayoutGroup CreateWindowBase(out GUIFrame window)
        {
            window = new(new(new Vector2(0.25f, 0.5f), GUI.Canvas, Anchor.Center), "ItemUI")
            {
                CanBeFocused = false
            };

            GUIDragHandle handle = new(new(Vector2.One, window.RectTransform, Anchor.Center), window.RectTransform, null);

            int dragIconHeight = GUIStyle.ItemFrameMargin.Y / 4;
            new GUIImage(new(new Point(window.Rect.Width, dragIconHeight), handle.RectTransform, Anchor.TopCenter) { AbsoluteOffset = new Point(0, dragIconHeight / 2), MinSize = new Point(0, dragIconHeight) }, "GUIDragIndicatorHorizontal")
            {
                CanBeFocused = false
            };

            return new(new(window.Rect.Size - GUIStyle.ItemFrameMargin, window.RectTransform, Anchor.Center) { AbsoluteOffset = GUIStyle.ItemFrameOffset })
            {
                CanBeFocused = false,
                Stretch = true
            };
        }

        private static GUITextBlock CreateListEntry(GUIListBox list, object data, out GUILayoutGroup right)
        {
            GUITextBlock entry = new(new(Vector2.UnitX, list.Content.RectTransform, minSize: new(0, (int)GUIStyle.Font.LineHeight)), "", GUIStyle.TextColorNormal)
            {
                Padding = Vector4.Zero,
                UserData = data
            };

            right = new(new(Vector2.One, entry.RectTransform), true, Anchor.CenterRight)
            {
                CanBeFocused = false
            };

            return entry;
        }

        private static GUITextBox CreateFilterBox(GUIComponent parent)
        {
            GUITextBox filterBox = new(new(Vector2.UnitX, parent.RectTransform), createClearButton: true);
            GUITextBlock filterLabel = new(new(Vector2.UnitY, filterBox.RectTransform, Anchor.CenterLeft), TextManager.Get("serverlog.filter"), GUIStyle.TextColorNormal * 0.5f);
            filterBox.OnSelected += (_, _) => filterLabel.Visible = false;
            filterBox.OnDeselected += (sender, _) => filterLabel.Visible = !sender.Text.Any();
            filterBox.OnTextChanged += (textBox, text) => filterLabel.Visible = !text.Any() && !textBox.Selected;
            return filterBox;
        }

        private static void FilterEntries(GUIListBox list, string filter) => list.Content.Children.OfType<GUITextBlock>().ForEach(i => i.Visible = i.Text.Contains(filter, StringComparison.OrdinalIgnoreCase));
        #endregion

        private static string GetName(this Entity entity) => entity switch
        {
            ISerializableEntity e => e.Name,
            Submarine sub => sub.Info.DisplayName.ToString(),
            _ => "Unknown",
        };

        private static string GetColor(this Entity entity) => entity switch
        {
            ISerializableEntity => "GUI.ItemQualityColorGood",
            _ => "GUI.Green"
        };

        #region Update & Draw
        public static void Update()
        {
            foreach (GUIFrame window in entityExplorerWindows)
            {
                window.AddToGUIUpdateList(order: 100);
            }

            foreach ((GUIComponent component, GUIFrame window) in guiExplorerWindows)
            {
                if (component is null)
                {
                    guiExplorerWindows.Remove(component);
                    continue;
                }

                window.AddToGUIUpdateList(order: 100);
            }

            foreach ((ISerializableEntity entity, GUIFrame window) in entityEditorWindows)
            {
                if (!Entities.Contains(entity as Entity))
                {
                    entityEditorWindows.Remove(entity);
                    continue;
                }

                window.AddToGUIUpdateList(order: 100);
            }

            if (inspectorMode is not InspectorMode.Disabled)
            {
                if (PlayerInput.SecondaryMouseButtonClicked() || PlayerInput.KeyHit(Keys.Escape))
                {
                    inspectorMode = InspectorMode.Disabled;
                }
                else if (PlayerInput.PrimaryMouseButtonClicked())
                {
                    switch (inspectorMode)
                    {
                        case InspectorMode.Entities:
                            CreateEntityExplorer(EntitiesUnderCursor.Concat(EntitiesUnderCursor.OfType<Item>().SelectManyRecursive(e => e.ContainedItems)).Concat(EntitiesUnderCursor.OfType<Character>().SelectMany(e => e.Inventory.AllItems.Concat(e.Inventory.AllItems.SelectManyRecursive(i => i.ContainedItems)))));
                            break;
                        case InspectorMode.GUI when GUI.MouseOn is not null:
                            TryCreateGUIExplorer(GUI.MouseOn);
                            break;
                    }
                    inspectorMode = InspectorMode.Disabled;
                }
            }

            guiExplorerEntries.RemoveAll(i => i.Parent is null);
        }

        public static void Draw(SpriteBatch sb)
        {
            sb.Begin();
            if (inspectorMode is not InspectorMode.Disabled)
            {
                string tooltip = $"Inspector mode (RMB to cancel)\nCursor pos: {CursorPosWorld}";
                switch (inspectorMode)
                {
                    case InspectorMode.Entities:
                        tooltip += $"\nEntities below cursor: {EntitiesUnderCursor.Count()}";
                        EntitiesUnderCursor.ForEach(e => tooltip += $"\n- {e.GetName()} (‖color:{e.GetColor()}‖{e.GetType().Name}‖end‖)");
                        break;
                    case InspectorMode.GUI when GUI.MouseOn is not null:
                        tooltip += $"\nSelected GUIComponent: ‖color:gui.green‖{GUI.MouseOn.GetType().Name}‖end‖ ({GUI.MouseOn.Style?.Name ?? "no style"})";
                        GUI.MouseOn.DrawGUIDebugOverlay(sb);
                        break;
                    case InspectorMode.GUI when GUI.MouseOn is null:
                        tooltip += "\nNo GUIComponent selected";
                        break;
                }

                ImmutableArray<RichTextData>? data = RichTextData.GetRichTextData(tooltip, out tooltip);
                GUI.DrawStringWithColors(sb, PlayerInput.MousePosition + new Vector2(25, 0), tooltip, GUIStyle.TextColorNormal, data, new(0, 0, 0, 0.5f));
            }
            guiExplorerEntries.Where(i => i.State is GUIComponent.ComponentState.Hover or GUIComponent.ComponentState.HoverSelected).ForEach(i => (i.UserData as GUIComponent).DrawGUIDebugOverlay(sb));
            sb.End();
        }

        private static void DrawGUIDebugOverlay(this GUIComponent component, SpriteBatch sb)
        {
            if (PlayerInput.IsCtrlDown())
            {
                List<GUIComponent> hierarchy = new List<GUIComponent>();
                GUIComponent currComponent = component;
                while (currComponent is not null)
                {
                    hierarchy.Add(currComponent);
                    currComponent = currComponent.Parent;
                }

                Color[] colors =
                {
                    Color.Lime,
                    Color.Yellow,
                    Color.Aqua,
                    Color.Red
                };

                for (int index = hierarchy.Count - 1; index >= 0; index--)
                {
                    GUIComponent comp = hierarchy[index];
                    GUI.DrawRectangle(sb, comp.CanBeFocused ? comp.MouseRect : comp.Rect, colors[index % colors.Length] * (PlayerInput.IsAltDown() ? 0.5f : 1), PlayerInput.IsAltDown());
                }
            }
            else
            {
                GUI.DrawRectangle(sb, component.MouseRect, Color.Lime);
                GUI.DrawRectangle(sb, component.Rect, Color.Cyan);
            }
        }
        #endregion
    }
}