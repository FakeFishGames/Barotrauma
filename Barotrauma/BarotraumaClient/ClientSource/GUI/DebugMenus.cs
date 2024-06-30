using Barotrauma.Extensions;
using Barotrauma.Items.Components;
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
        // TODO: GUIComponent/RectTransform editors

        public static InspectorMode inspectorMode;

        private static Vector2 CursorPosWorld => Screen.Selected?.Cam?.ScreenToWorld(PlayerInput.MousePosition) ?? PlayerInput.MousePosition;
        private static IEnumerable<Entity> Entities => Entity.GetEntities();
        private static IEnumerable<Entity> EntitiesUnderCursor => Entities.Where(e => e switch
        {
            Item i when i.Components.OfType<Wire>().Any(w => w.IsMouseOn()) => true,
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
            itemSpawnerWindow.Visible = false;
            selectedItemPrefabs.Clear();
        }

        #region Entity Explorers
        private static readonly List<GUIFrame> entityExplorerWindows = new List<GUIFrame>();

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
                    return TryCreateEditorWindow(obj as Entity);
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
                entry.Text = RichString.Rich($"{entity.GetName()} (‖color:GUI.Green‖{entity.GetType().Name}‖end‖) {entity.WorldPosition.ToPoint()}");

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
        private static readonly Dictionary<Entity, GUIFrame> entityEditorWindows = new Dictionary<Entity, GUIFrame>();

        private static bool TryCreateEditorWindow(Entity entity)
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

        private static void RefreshEditor(GUIListBox list, Entity entity)
        {
            list.ClearChildren();

            switch (entity)
            {
                case ISerializableEntity sEntity:
                    new SerializableEntityEditor(list.Content.RectTransform, sEntity, false, true);
                    switch (sEntity)
                    {
                        case Item item:
                            item.Components.ForEach(component => new SerializableEntityEditor(list.Content.RectTransform, component, false, true));
                            break;
                        case Character character:
                            new SerializableEntityEditor(list.Content.RectTransform, character.Params, false, true);
                            new SerializableEntityEditor(list.Content.RectTransform, character.AnimController.RagdollParams, false, true);
                            character.AnimController.Limbs.ForEach(limb =>
                            {
                                new SerializableEntityEditor(list.Content.RectTransform, limb, false, true);
                                limb.DamageModifiers.ForEach(mod => new SerializableEntityEditor(list.Content.RectTransform, mod, false, true));
                            });
                            break;
                    }
                    break;
                case Submarine sub:
                    SubmarineInfo info = sub.Info;
                    if (info.OutpostGenerationParams is not null)
                    {
                        new SerializableEntityEditor(list.Content.RectTransform, info.OutpostGenerationParams, false, true);
                    }
                    if (info.OutpostModuleInfo is not null)
                    {
                        new SerializableEntityEditor(list.Content.RectTransform, info.OutpostModuleInfo, false, true);
                    }
                    if (info.GetExtraSubmarineInfo is not null)
                    {
                        new SerializableEntityEditor(list.Content.RectTransform, info.GetExtraSubmarineInfo, false, true);
                    }
                    break;
            }
        }
        #endregion

        #region GUI Explorers
        private static readonly Dictionary<GUIComponent, GUIFrame> guiExplorerWindows = new Dictionary<GUIComponent, GUIFrame>();
        private static readonly List<GUITextBlock> guiExplorerEntries = new List<GUITextBlock>();

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

            GUIListBox list = new(new(Vector2.One, content.RectTransform))
            {
                OnSelected = (_, obj) => obj is GUIComponent component && TryCreateGUIExplorer(component)
            };

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

        #region Item Spawner
        public static GUIFrame itemSpawnerWindow;
        private static GUIListBox categorizedEntityList, allEntityList;
        private static GUITextBox entityFilterBox;
        private static MapEntityCategory? selectedCategory;
        private static IEnumerable<MapEntityCategory> itemCategories;
        private static readonly Dictionary<(ItemPrefab, int), int> selectedItemPrefabs = new Dictionary<(ItemPrefab, int), int>();
        private static int quality;

        private static GUIFrame CreateItemSpawnerWindow()
        {
            GUILayoutGroup content = CreateWindowBase(out GUIFrame window);
            window.Visible = false;

            GUILayoutGroup entityMenuTop = new(new(new Vector2(1, 0.13f), content.RectTransform), true, Anchor.CenterLeft);

            new GUIButton(new(Vector2.One, entityMenuTop.RectTransform, scaleBasis: ScaleBasis.BothHeight), "", style: "CategoryButton.All")
            {
                Selected = true,
                ToolTip = TextManager.Get("MapEntityCategory.All"),
                OnClicked = (_, _) =>
                {
                    OpenEntityMenu(null);
                    return true;
                }
            };

            foreach (MapEntityCategory category in itemCategories)
            {
                new GUIButton(new(Vector2.One, entityMenuTop.RectTransform, scaleBasis: ScaleBasis.BothHeight), "", style: "CategoryButton." + category.ToString())
                {
                    ToolTip = TextManager.Get("MapEntityCategory." + category.ToString()),
                    OnClicked = (_, _) =>
                    {
                        OpenEntityMenu(category);
                        return true;
                    }
                };
            }

            IEnumerable<GUIButton> entityCategoryButtons = entityMenuTop.Children.OfType<GUIButton>();
            Point buttonSize = new(entityMenuTop.Rect.Width / entityCategoryButtons.Count());
            entityMenuTop.RectTransform.MaxSize = new(entityMenuTop.Rect.Width, buttonSize.Y);
            entityCategoryButtons.ForEach(b =>
            {
                b.RectTransform.MaxSize = buttonSize;
                b.OnClicked += (_, _) =>
                {
                    entityCategoryButtons.ForEach(b => b.Selected = false);
                    return b.Selected = true;
                };
            });

            entityFilterBox = CreateFilterBox(content);
            entityFilterBox.OnTextChanged += (_, text) =>
            {
                FilterEntities(text);
                return true;
            };

            GUIFrame entityListContainer = new(new(new Vector2(1, 0.9f), content.RectTransform), null);
            categorizedEntityList = new(new(Vector2.One, entityListContainer.RectTransform), useMouseDownToSelect: true);
            allEntityList = new(new(Vector2.One, entityListContainer.RectTransform), useMouseDownToSelect: true)
            {
                OnSelected = SelectPrefab,
                UseGridLayout = true,
                CheckSelected = MapEntityPrefab.GetSelected,
                Visible = false,
                PlaySoundOnSelect = true,
            };

            GUILayoutGroup qualitySelection = new(new(Vector2.UnitX, content.RectTransform), true);
            for (int i = 0; i <= Quality.MaxQuality; i++)
            {
                Color color = GUIStyle.GetQualityColor(i);
                new GUIButton(new(new Vector2(1f / (Quality.MaxQuality + 1), 0), qualitySelection.RectTransform), TextManager.Get($"qualityname{i}"), color: color)
                {
                    UserData = i,
                    OnClicked = (button, obj) =>
                    {
                        quality = (int)obj;
                        return true;
                    },
                    HoverColor = new Color(color.ToVector3() * new Vector3(1.25f)),
                    PressedColor = new Color(color.ToVector3() * new Vector3(0.9f)),
                    SelectedColor = new Color(color.ToVector3() * new Vector3(1.25f))
                };
            }
            qualitySelection.RectTransform.MinSize = new(0, qualitySelection.GetChild(0).Rect.Height);
            IEnumerable<GUIButton> qualityButtons = qualitySelection.Children.OfType<GUIButton>();
            qualityButtons.ForEach(b =>
            {
                b.OnClicked += (_, _) =>
                {
                    qualityButtons.ForEach(b => b.Selected = false);
                    b.Selected = true;
                    return true;
                };
            });

            GUIButton closeButton = new(new(Vector2.UnitX, content.RectTransform), TextManager.Get("Close"))
            {
                OnClicked = (_, _) => window.Visible = false
            };

            OpenEntityMenu(null);
            return window;
        }

        private static void FilterEntities(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                allEntityList.Visible = false;
                categorizedEntityList.Visible = true;

                foreach (GUIComponent child in categorizedEntityList.Content.Children)
                {
                    if (!(child.Visible = !selectedCategory.HasValue || selectedCategory == (MapEntityCategory)child.UserData)) return;
                    GUIListBox innerList = child.GetChild<GUIListBox>();
                    foreach (GUIComponent grandChild in innerList.Content.Children)
                    {
                        grandChild.Visible = ((MapEntityPrefab)grandChild.UserData).Name.Value.Contains(filter, StringComparison.OrdinalIgnoreCase);
                    }
                };
                categorizedEntityList.UpdateScrollBarSize();
                categorizedEntityList.BarScroll = 0;
                return;
            }

            allEntityList.Visible = true;
            categorizedEntityList.Visible = false;
            filter = filter.ToLower();
            foreach (GUIComponent child in allEntityList.Content.Children)
            {
                child.Visible = (!selectedCategory.HasValue || ((MapEntityPrefab)child.UserData).Category.HasFlag(selectedCategory.Value)) && ((MapEntityPrefab)child.UserData).Name.Value.Contains(filter, StringComparison.OrdinalIgnoreCase);
            }
            allEntityList.UpdateScrollBarSize();
            allEntityList.BarScroll = 0;
        }

        private static void OpenEntityMenu(MapEntityCategory? entityCategory)
        {
            categorizedEntityList.Content.ClearChildren();
            allEntityList.Content.ClearChildren();

            int maxTextWidth = (int)(GUIStyle.SubHeadingFont.MeasureString(TextManager.Get("MapEntityCategory.Misc")).X + GUI.IntScale(50));
            Dictionary<string, List<ItemPrefab>> entityLists = new Dictionary<string, List<ItemPrefab>>();
            Dictionary<string, MapEntityCategory> categoryKeys = new Dictionary<string, MapEntityCategory>();

            foreach (MapEntityCategory category in itemCategories)
            {
                LocalizedString categoryName = TextManager.Get("MapEntityCategory." + category);
                maxTextWidth = (int)Math.Max(maxTextWidth, GUIStyle.SubHeadingFont.MeasureString(categoryName.Replace(" ", "\n")).X + GUI.IntScale(50));
                foreach (ItemPrefab ip in MapEntityPrefab.List.OfType<ItemPrefab>())
                {
                    if (!ip.Category.HasFlag(category)) continue;

                    if (!entityLists.ContainsKey(category + ip.Subcategory))
                    {
                        entityLists[category + ip.Subcategory] = new List<ItemPrefab>();
                    }
                    entityLists[category + ip.Subcategory].Add(ip);
                    categoryKeys[category + ip.Subcategory] = category;
                    LocalizedString subcategoryName = TextManager.Get("SubCategory." + ip.Subcategory).Fallback(ip.Subcategory);
                    if (subcategoryName != null)
                    {
                        maxTextWidth = (int)Math.Max(maxTextWidth, GUIStyle.SubHeadingFont.MeasureString(subcategoryName.Replace(" ", "\n")).X + GUI.IntScale(50));
                    }
                }
            }

            categorizedEntityList.Content.ClampMouseRectToParent = true;
            int entitiesPerRow = (int)Math.Ceiling(categorizedEntityList.Content.Rect.Width / Math.Max(125 * GUI.Scale, 60));
            foreach (string categoryKey in entityLists.Keys)
            {
                GUILayoutGroup categoryFrame = new(new(Vector2.One, categorizedEntityList.Content.RectTransform))
                {
                    ClampMouseRectToParent = true,
                    UserData = categoryKeys[categoryKey]
                };

                LocalizedString categoryName = TextManager.Get("MapEntityCategory." + entityLists[categoryKey].First().Category);
                LocalizedString subCategoryName = entityLists[categoryKey].First().Subcategory;
                subCategoryName = subCategoryName.IsNullOrEmpty() ? "" : (TextManager.Get($"subcategory.{subCategoryName}").Fallback(subCategoryName));

                GUILayoutGroup categoryTitle = new(new(new Point(categoryFrame.Rect.Width, (int)GUIStyle.SubHeadingFont.LineHeight), categoryFrame.RectTransform, isFixedSize: true), true);
                new GUITextBlock(new(new Vector2(0.5f, 1), categoryTitle.RectTransform), categoryName, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.BottomLeft);
                new GUITextBlock(new(new Vector2(0.5f, 1), categoryTitle.RectTransform), subCategoryName, textAlignment: Alignment.BottomRight);

                GUIFrame divider = new(new(Vector2.One, categoryFrame.RectTransform), "HorizontalLine");

                GUIListBox entityListInner = new(new(Vector2.One, categoryFrame.RectTransform, Anchor.CenterRight), style: null, useMouseDownToSelect: true)
                {
                    ScrollBarVisible = false,
                    AutoHideScrollBar = false,
                    OnSelected = SelectPrefab,
                    UseGridLayout = true,
                    CheckSelected = MapEntityPrefab.GetSelected,
                    ClampMouseRectToParent = true,
                    PlaySoundOnSelect = true,
                };
                entityListInner.ContentBackground.ClampMouseRectToParent = true;
                entityListInner.Content.ClampMouseRectToParent = true;

                foreach (ItemPrefab ip in entityLists[categoryKey])
                {
#if !DEBUG
                    if ((ip.HideInMenus || ip.HideInEditors) && !GameMain.DebugDraw) continue;
#endif
                    CreateEntityElement(ip, entitiesPerRow, entityListInner.Content);
                }

                entityListInner.UpdateScrollBarSize();
                int innerContentHeight = (int)(entityListInner.TotalSize + entityListInner.Padding.Y + entityListInner.Padding.W);
                int outerContentHeight = innerContentHeight + categoryTitle.Rect.Height + divider.Rect.Height;
                categoryFrame.RectTransform.NonScaledSize = new Point(categoryFrame.Rect.Width, outerContentHeight);
                categoryFrame.RectTransform.MinSize = new Point(0, outerContentHeight);
                entityListInner.RectTransform.NonScaledSize = new Point(entityListInner.Rect.Width, innerContentHeight);
                entityListInner.RectTransform.MinSize = new Point(0, innerContentHeight);

                entityListInner.Content.RectTransform.SortChildren((i1, i2) => string.Compare(((ItemPrefab)i1.GUIComponent.UserData)?.Name.Value, (i2.GUIComponent.UserData as ItemPrefab)?.Name.Value, StringComparison.Ordinal));
            }

            foreach (ItemPrefab ip in MapEntityPrefab.List.OfType<ItemPrefab>())
            {
#if !DEBUG
                if ((ip.HideInMenus || ip.HideInEditors) && !GameMain.DebugDraw) continue;
#endif
                CreateEntityElement(ip, entitiesPerRow, allEntityList.Content);
            }
            allEntityList.Content.RectTransform.SortChildren((i1, i2) => string.Compare(((ItemPrefab)i1.GUIComponent.UserData)?.Name.Value, (i2.GUIComponent.UserData as ItemPrefab)?.Name.Value, StringComparison.Ordinal));

            selectedCategory = entityCategory;

            foreach (GUIComponent child in categorizedEntityList.Content.Children)
            {
                child.Visible = !entityCategory.HasValue || (MapEntityCategory)child.UserData == entityCategory;
                GUIListBox innerList = child.GetChild<GUIListBox>();
                foreach (GUIComponent grandChild in innerList.Content.Children)
                {
                    grandChild.Visible = true;
                }
            }

            FilterEntities(entityFilterBox.Text);

            categorizedEntityList.UpdateScrollBarSize();
            categorizedEntityList.BarScroll = 0;
        }

        private static void CreateEntityElement(ItemPrefab ip, int entitiesPerRow, GUIComponent parent)
        {
            bool legacy = ip.Category.HasFlag(MapEntityCategory.Legacy);

            float relWidth = 1f / entitiesPerRow;
            GUIFrame frame = new(new(new Vector2(relWidth, relWidth * ((float)parent.Rect.Width / parent.Rect.Height)), parent.RectTransform, minSize: new(0, 50)), style: "GUITextBox")
            {
                UserData = ip,
                ClampMouseRectToParent = true,
                OnSecondaryClicked = DeselectPrefab,
            };
            frame.RectTransform.MinSize = new Point(0, frame.Rect.Width);
            frame.RectTransform.MaxSize = new Point(int.MaxValue, frame.Rect.Width);

            LocalizedString name = legacy ? TextManager.GetWithVariable("legacyitemformat", "[name]", ip.Name) : ip.Name;
            frame.ToolTip = ip.CreateTooltipText();

            if (ip.IsModded)
            {
                frame.Color = Color.Magenta;
            }

            if (ip.HideInMenus || ip.HideInEditors)
            {
                frame.Color = Color.Red;
                name = "[HIDDEN] " + name;
            }
            frame.ToolTip = RichString.Rich(frame.ToolTip);

            GUILayoutGroup paddedFrame = new(new(new Vector2(0.8f), frame.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                Stretch = true,
                RelativeSpacing = 0.03f,
                CanBeFocused = false
            };

            Sprite icon = ip.InventoryIcon ?? ip.Sprite;
            Color iconColor = ip.InventoryIcon is not null ? ip.InventoryIconColor : ip.SpriteColor;
            GUIImage img = new(new(new Vector2(1, 0.8f), paddedFrame.RectTransform, Anchor.TopCenter), icon)
            {
                CanBeFocused = false,
                LoadAsynchronously = true,
                SpriteEffects = icon.effects,
                Color = legacy ? iconColor * 0.6f : iconColor
            };

            GUITextBlock textBlock = new(new(Vector2.UnitX, paddedFrame.RectTransform, Anchor.BottomCenter), name, font: GUIStyle.SmallFont, textAlignment: Alignment.Center)
            {
                CanBeFocused = false
            };
            if (legacy)
            {
                textBlock.TextColor *= 0.6f;
            }
            if (name.IsNullOrEmpty())
            {
                DebugConsole.AddWarning($"Entity \"{ip.Identifier.Value}\" has no name!", contentPackage: ip.ContentPackage);
                textBlock.Text = frame.ToolTip = ip.Identifier.Value;
                textBlock.TextColor = GUIStyle.Red;
            }
            textBlock.Text = ToolBox.LimitString(textBlock.Text, textBlock.Font, textBlock.Rect.Width);

            paddedFrame.Recalculate();
            if (img.Sprite is not null)
            {
                img.Scale = Math.Min(Math.Min(img.Rect.Width / img.Sprite.size.X, img.Rect.Height / img.Sprite.size.Y), 1.5f);
                img.RectTransform.NonScaledSize = new Point((int)(img.Sprite.size.X * img.Scale), img.Rect.Height);
            }
        }

        private static bool SelectPrefab(GUIComponent _, object obj)
        {
            if (obj is not ItemPrefab prefab) return false;

            SoundPlayer.PlayUISound(GUISoundType.PickItem);
            (ItemPrefab, int) key = new(prefab, quality);
            if (!selectedItemPrefabs.TryAdd(key, 1) && selectedItemPrefabs.Values.Sum() < 100)
            {
                selectedItemPrefabs[key]++;
            }

            return true;
        }

        private static bool DeselectPrefab(GUIComponent _, object obj)
        {
            if (obj is not ItemPrefab prefab) return false;

            SoundPlayer.PlayUISound(GUISoundType.DropItem);
            (ItemPrefab, int) key = new(prefab, quality);
            if (selectedItemPrefabs.TryGetValue(key, out int amount) && amount > 0)
            {
                selectedItemPrefabs[key]--;
                if (selectedItemPrefabs[key] <= 0)
                {
                    selectedItemPrefabs.Remove(key);
                }
            }

            return true;
        }

        private static void SpawnItem(ItemPrefab prefab, Either<Vector2, Inventory> spawnLocation, Submarine sub, int quality, int amount)
        {
            for (int i = 0; i < amount; i++)
            {
                if (spawnLocation.TryGet(out Vector2 pos))
                {
                    Item item = new(prefab, pos, sub)
                    {
                        Quality = quality
                    };
                }
                else if (spawnLocation.TryGet(out Inventory inv))
                {
                    Item item = new(prefab, inv.Owner.Position, sub)
                    {
                        Quality = quality
                    };

                    inv.TryPutItem(item, null, item.AllowedSlots);

                    if (item.ParentInventory?.Owner is Character character)
                    {
                        item.GetComponents<WifiComponent>().ForEach(wc => wc.TeamID = character.TeamID);
                    }
                }
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

        #region Update & Draw
        public static void Init()
        {
            itemCategories = Enum.GetValues(typeof(MapEntityCategory)).Cast<MapEntityCategory>().Where(c => c is not MapEntityCategory.None && ItemPrefab.Prefabs.Any(ip => ip.Category.HasFlag(c)));
            itemSpawnerWindow = CreateItemSpawnerWindow();
        }

        public static void Update()
        {
            if (!HasPermission())
            {
                CloseAll();
                return;
            }

            itemSpawnerWindow.AddToGUIUpdateList();

            foreach (GUIFrame window in entityExplorerWindows)
            {
                window.AddToGUIUpdateList();
            }

            foreach ((GUIComponent component, GUIFrame window) in guiExplorerWindows)
            {
                if (component is null)
                {
                    guiExplorerWindows.Remove(component);
                    continue;
                }

                window.AddToGUIUpdateList();
            }

            foreach ((Entity entity, GUIFrame window) in entityEditorWindows)
            {
                if (!Entities.Contains(entity))
                {
                    entityEditorWindows.Remove(entity);
                    continue;
                }

                window.AddToGUIUpdateList();
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
            else if (selectedItemPrefabs.Any())
            {
                if (!itemSpawnerWindow.GetAllChildren().Prepend(itemSpawnerWindow).Contains(GUI.MouseOn))
                {
                    if (PlayerInput.SecondaryMouseButtonClicked() || PlayerInput.KeyHit(Keys.Escape))
                    {
                        selectedItemPrefabs.Clear();
                    }
                    else if (PlayerInput.PrimaryMouseButtonClicked())
                    {
                        Submarine sub = EntitiesUnderCursor.OfType<Submarine>().FirstOrDefault();
                        Either<Vector2, Inventory> spawnLocation = Character.Controlled?.Inventory?.visualSlots.Any(vs => vs.MouseOn()) ?? false ? Character.Controlled.Inventory : CursorPosWorld - (sub is not null ? sub.Position : Vector2.Zero);
                        foreach (((ItemPrefab item, int quality), int amount) in selectedItemPrefabs)
                        {
                            SpawnItem(item, spawnLocation, sub, quality, amount);
                        }

                        if (!PlayerInput.IsShiftDown())
                        {
                            selectedItemPrefabs.Clear();
                        }
                    }
                }
            }

            guiExplorerEntries.RemoveAll(i => i.Parent is null);
        }

        public static void Draw(SpriteBatch sb)
        {
            if (!HasPermission()) return;

            Vector2 cursorOffset = PlayerInput.MousePosition + new Vector2(25, 0);

            sb.Begin();
            if (inspectorMode is not InspectorMode.Disabled)
            {
                string tooltip = $"Inspector mode (RMB to cancel)\nCursor pos: {CursorPosWorld}";
                switch (inspectorMode)
                {
                    case InspectorMode.Entities:
                        IEnumerable<Entity> entities = EntitiesUnderCursor;
                        tooltip += $"\nEntities below cursor: {entities.Count()}";
                        entities.ForEach(e => tooltip += $"\n- {e.GetName()} (‖color:GUI.Green‖{e.GetType().Name}‖end‖)");
                        break;
                    case InspectorMode.GUI when GUI.MouseOn is not null:
                        tooltip += $"\nSelected GUIComponent: ‖color:GUI.Green‖{GUI.MouseOn.GetType().Name}‖end‖ ({GUI.MouseOn.Style?.Name ?? "no style"})";
                        GUI.MouseOn.DrawGUIDebugOverlay(sb);
                        break;
                    case InspectorMode.GUI when GUI.MouseOn is null:
                        tooltip += "\nNo GUIComponent selected";
                        break;
                }

                ImmutableArray<RichTextData>? data = RichTextData.GetRichTextData(tooltip, out tooltip);
                GUI.DrawStringWithColors(sb, cursorOffset, tooltip, GUIStyle.TextColorNormal, data, new(0, 0, 0, 0.5f));
            }
            else
            {
                if (GameMain.DebugDraw)
                {
                    GUI.MouseOn?.DrawGUIDebugOverlay(sb);
                }

                if (selectedItemPrefabs.Any())
                {
                    Vector2 drawPos = cursorOffset;
                    float iconHeight = 32;
                    foreach (((ItemPrefab item, int quality), int amount) in selectedItemPrefabs)
                    {
                        Sprite icon = item.InventoryIcon ?? item.Sprite;
                        sb.Draw(icon.Texture, new Rectangle(drawPos.ToPoint(), new Vector2(iconHeight / icon.SourceRect.Height * icon.SourceRect.Width, iconHeight).ToPoint()), icon.SourceRect, item.InventoryIcon is not null ? item.InventoryIconColor : item.SpriteColor);
                        GUI.DrawString(sb, drawPos + new Vector2(iconHeight * 0.75f, iconHeight * 0.5f), $"x{amount}", GUIStyle.GetQualityColor(quality));
                        drawPos += new Vector2(0, iconHeight);
                    }
                }
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
                GUI.DrawRectangle(sb, component.MouseRect, Color.Lime * (PlayerInput.IsAltDown() ? 0.5f : 1), PlayerInput.IsAltDown());
                GUI.DrawRectangle(sb, component.Rect, Color.Cyan * (PlayerInput.IsAltDown() ? 0.5f : 1), PlayerInput.IsAltDown());
            }
        }

        private static bool HasPermission() => (GameMain.Client is null || GameMain.Client.HasConsoleCommandPermission(new("debugmenu")))
#if !DEBUG
        && DebugConsole.CheatsEnabled
#endif
        ;
        #endregion
    }
}