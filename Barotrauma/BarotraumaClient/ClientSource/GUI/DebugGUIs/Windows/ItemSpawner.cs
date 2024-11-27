using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    internal sealed class ItemSpawner : DebugWindow
    {
        private readonly GUIListBox categorizedEntityList, allEntityList;
        private readonly GUITextBox entityFilterBox;
        private MapEntityCategory? selectedCategory;

        private static IEnumerable<MapEntityCategory> ItemCategories
            => Enum.GetValues(typeof(MapEntityCategory)).Cast<MapEntityCategory>()
            .Where(category
                => category != MapEntityCategory.None
                && ItemPrefab.Prefabs.Any(ip => ip.Category.HasFlag(category)));

        private readonly Dictionary<(ItemPrefab, int), int> selectedItemPrefabs = new();
        private int selectedQuality;

        private ItemSpawner() : base()
        {
            GUILayoutGroup entityMenuTop = new(new(new Vector2(1, 0.13f), Content.RectTransform), true, Anchor.CenterLeft);

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

            foreach (MapEntityCategory category in ItemCategories)
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

            entityFilterBox = CreateFilterBox(Content, (_, text) =>
            {
                FilterEntities(text);
                return true;
            });

            GUIFrame entityListContainer = new(new(new Vector2(1, 0.9f), Content.RectTransform), null);
            categorizedEntityList = new(new(Vector2.One, entityListContainer.RectTransform), useMouseDownToSelect: true);
            allEntityList = new(new(Vector2.One, entityListContainer.RectTransform), useMouseDownToSelect: true)
            {
                OnSelected = SelectPrefab,
                UseGridLayout = true,
                CheckSelected = MapEntityPrefab.GetSelected,
                Visible = false,
                PlaySoundOnSelect = true,
            };

            GUILayoutGroup qualitySelection = new(new(Vector2.UnitX, Content.RectTransform), true);
            for (int i = 0; i <= Quality.MaxQuality; i++)
            {
                Color color = GUIStyle.GetQualityColor(i);
                new GUIButton(new(new Vector2(1f / (Quality.MaxQuality + 1), 0), qualitySelection.RectTransform), TextManager.Get($"qualityname{i}"), color: color)
                {
                    UserData = i,
                    OnClicked = (button, obj) =>
                    {
                        selectedQuality = (int)obj;
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

            OpenEntityMenu(null);
        }

        public static ItemSpawner OpenNew() => new();

        private void FilterEntities(string filter)
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

        private void OpenEntityMenu(MapEntityCategory? entityCategory)
        {
            categorizedEntityList.Content.ClearChildren();
            allEntityList.Content.ClearChildren();

            int maxTextWidth = (int)(GUIStyle.SubHeadingFont.MeasureString(TextManager.Get("MapEntityCategory.Misc")).X + GUI.IntScale(50));
            Dictionary<string, List<ItemPrefab>> entityLists = new Dictionary<string, List<ItemPrefab>>();
            Dictionary<string, MapEntityCategory> categoryKeys = new Dictionary<string, MapEntityCategory>();

            foreach (MapEntityCategory category in ItemCategories)
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

        private void CreateEntityElement(ItemPrefab prefab, int entitiesPerRow, GUIComponent parent)
        {
            bool legacy = prefab.Category.HasFlag(MapEntityCategory.Legacy);

            float relWidth = 1f / entitiesPerRow;
            GUIFrame frame = new(new(new Vector2(relWidth, relWidth * ((float)parent.Rect.Width / parent.Rect.Height)), parent.RectTransform, minSize: new(0, 50)), style: "GUITextBox")
            {
                UserData = prefab,
                ClampMouseRectToParent = true,
                OnSecondaryClicked = DeselectPrefab,
            };
            frame.RectTransform.MinSize = new Point(0, frame.Rect.Width);
            frame.RectTransform.MaxSize = new Point(int.MaxValue, frame.Rect.Width);

            LocalizedString name = legacy ? TextManager.GetWithVariable("legacyitemformat", "[name]", prefab.Name) : prefab.Name;
            frame.ToolTip = prefab.CreateTooltipText();

            if (prefab.IsModded)
            {
                frame.Color = Color.Magenta;
            }

            if (prefab.HideInMenus || prefab.HideInEditors)
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

            Sprite icon = prefab.InventoryIcon ?? prefab.Sprite;
            Color iconColor = prefab.InventoryIcon != null ? prefab.InventoryIconColor : prefab.SpriteColor;
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
                DebugConsole.AddWarning($"Entity \"{prefab.Identifier.Value}\" has no name!", contentPackage: prefab.ContentPackage);
                textBlock.Text = frame.ToolTip = prefab.Identifier.Value;
                textBlock.TextColor = GUIStyle.Red;
            }
            textBlock.Text = ToolBox.LimitString(textBlock.Text, textBlock.Font, textBlock.Rect.Width);

            paddedFrame.Recalculate();
            if (img.Sprite != null)
            {
                img.Scale = Math.Min(Math.Min(img.Rect.Width / img.Sprite.size.X, img.Rect.Height / img.Sprite.size.Y), 1.5f);
                img.RectTransform.NonScaledSize = new Point((int)(img.Sprite.size.X * img.Scale), img.Rect.Height);
            }
        }

        private bool SelectPrefab(GUIComponent component, object userData)
        {
            if (userData is not ItemPrefab prefab) { return false; }

            SoundPlayer.PlayUISound(GUISoundType.PickItem);
            (ItemPrefab, int) key = new(prefab, selectedQuality);
            if (!selectedItemPrefabs.TryAdd(key, 1) && selectedItemPrefabs.Values.Sum() < 100)
            {
                selectedItemPrefabs[key]++;
            }

            return true;
        }

        private bool DeselectPrefab(GUIComponent component, object userData)
        {
            if (userData is not ItemPrefab prefab) { return false; }

            SoundPlayer.PlayUISound(GUISoundType.DropItem);
            (ItemPrefab, int) key = new(prefab, selectedQuality);
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

        protected override void Update()
        {
            if (selectedItemPrefabs.Any() && !Frame.GetAllChildren().Prepend(Frame).Contains(GUI.MouseOn))
            {
                if (PlayerInput.SecondaryMouseButtonClicked() || PlayerInput.KeyHit(Keys.Escape))
                {
                    selectedItemPrefabs.Clear();
                }
                else if (PlayerInput.PrimaryMouseButtonClicked())
                {
                    Submarine sub = Entity.GetEntities(entity => entity.IsUnderCursor).OfType<Submarine>().FirstOrDefault();
                    Either<Vector2, Inventory> spawnLocation = Character.Controlled?.Inventory?.visualSlots is { } slots && slots.Any(slot => slot.MouseOn()) ? Character.Controlled.Inventory : PlayerInput.MouseWorldPosition - (sub?.Position ?? Vector2.Zero);
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

            base.Update();
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (!selectedItemPrefabs.Any()) { return; }

            spriteBatch.Begin();

            Vector2 drawPos = PlayerInput.MousePosition + (25f, 0f);
            int iconHeight = 32;

            foreach (((ItemPrefab item, int quality), int amount) in selectedItemPrefabs)
            {
                Sprite icon = item.InventoryIcon ?? item.Sprite;
                spriteBatch.Draw(icon.Texture, new Rectangle(drawPos.ToPoint(), (iconHeight / icon.SourceRect.Height * icon.SourceRect.Width, iconHeight)), icon.SourceRect, item.InventoryIcon != null ? item.InventoryIconColor : item.SpriteColor);
                GUI.DrawString(spriteBatch, drawPos + (iconHeight * 0.75f, iconHeight * 0.5f), $"x{amount}", GUIStyle.GetQualityColor(quality));
                drawPos.Y += iconHeight;
            }

            spriteBatch.End();
        }
    }
}
