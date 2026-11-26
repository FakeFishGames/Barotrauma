using System.Collections.Immutable;
using Barotrauma;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HomeAndHarbor
{
    internal class OutpostItemSwapUI
    {
        private readonly GUIComponent swapParent;

        private Option<Item> selectedItem = Option.None;

        public bool IsSwapUIVisible => selectedItem.IsSome();

        private GUITextBlock? selectedItemNameText;
        private GUITextBlock? selectedItemPriceText;
        private GUIButton? selectedItemSwapButton;
        private ItemPrefab? selectedItemPrefab;

        private static CampaignMode? Campaign
            => GameMain.GameSession?.Campaign;

        private static readonly Identifier EventIdentifier = "OutpostItemSwapUI.OutpostItemSwapEvent".ToIdentifier();

        private readonly OutpostBuilderUI builderUi;

        private static Md5Hash? prevContentPackagesHash;
        private static IReadOnlyDictionary<ItemPrefab, ImmutableArray<ItemPrefab>> availableSwapsCache = 
            new Dictionary<ItemPrefab, ImmutableArray<ItemPrefab>>();

        public OutpostItemSwapUI(OutpostBuilderUI builderUi, GUIComponent swapParent)
        {
            this.builderUi = builderUi;
            this.swapParent = swapParent;

            Campaign?.OnMoneyChanged.RegisterOverwriteExisting(EventIdentifier, _ => RefreshPotentialItemUI());
            GameMain.Client?.OnPermissionChanged.RegisterOverwriteExisting(EventIdentifier, _ => RefreshPotentialItemUI());
        }

        private void RefreshPotentialItemUI()
        {
            if (!selectedItem.TryUnwrap(out Item? item)) { return; }
            UpdateSelectedItemUI(item, selectedItemPrefab, selectedItemNameText, selectedItemPriceText, selectedItemSwapButton);
        }

        public void ClearSwapUI()
        {
            swapParent.Visible = false;
            selectedItemNameText = null;
            selectedItemPriceText = null;
            selectedItemSwapButton = null;
            selectedItemPrefab = null;
            selectedItem = Option.None;
            swapParent.ClearChildren();
        }

        public void CreateSwapUIFor(Item item)
        {
            ClearSwapUI();

            swapParent.Visible = true;
            swapParent.SetAsLastChild();

            GUIFrame backgroundBlocker = new GUIFrame(new RectTransform(Vector2.One, swapParent.RectTransform, Anchor.Center), style: "GUIBackgroundBlocker")
            {
                CanBeFocused = false
            };

            GUIFrame swapArea = new GUIFrame(new RectTransform(new Vector2(0.4f, 0.5f), swapParent.RectTransform, Anchor.Center), style: null)
            {
                CanBeFocused = false
            };

            selectedItem = Option.Some(item);

            int index = 0;
            if (!FindSwappableItems(item.Prefab, out List<ItemPrefab> availableReplacements)) { return; }

            // Order by placed item -> available items -> unavailable items
            availableReplacements =
                availableReplacements.OrderBy(prefab => item.Prefab != prefab)
                .ThenBy(prefab => !OutpostBuilder.IsItemSwapAvailable(prefab, out _))
                .ToList();

            int currentItemIndex = availableReplacements.IndexOf(item.Prefab);
            if (currentItemIndex > 0)
            {
                index = currentItemIndex;
            }

            selectedItemPrefab = availableReplacements[index];

            new GUICustomComponent(new RectTransform(new Vector2(0.5f), swapArea.RectTransform, Anchor.Center), onDraw: DrawItemSwapPreview);

            GUILayoutGroup bottomLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.25f), swapArea.RectTransform, Anchor.BottomCenter), childAnchor: Anchor.TopCenter);

            selectedItemNameText = new GUITextBlock(new RectTransform(new Vector2(1f, 0.25f), bottomLayout.RectTransform), item.Prefab.Name, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Center);

            selectedItemPriceText = new GUITextBlock(new RectTransform(new Vector2(1f, 0.2f), bottomLayout.RectTransform), "", font: GUIStyle.Font, textAlignment: Alignment.Center);

            //spacing
            new GUIFrame(new RectTransform(new Point(bottomLayout.Rect.Width, GUI.IntScale(20)), bottomLayout.RectTransform), style: null);

            selectedItemSwapButton = new GUIButton(new RectTransform(new Vector2(0.5f, 0.25f), bottomLayout.RectTransform), TextManager.Get("outpostswap.swapbutton"))
            {
                Enabled = GameMain.IsSingleplayer || OutpostBuilder.AllowedToManageOutpost(),
                OnClicked = (button, o) =>
                {
                    if (Campaign is null) { return false; }

                    if (GameMain.IsMultiplayer)
                    {
                        GameNetwork.Send(Plugin.NetworkHeaders.SwapOutpostItem, new OutpostBuilder.SwapOutpostItemData(item.ID, selectedItemPrefab.Identifier));
                        button.Enabled = false;
                        return true;
                    }

                    int price = OutpostBuilder.GetPriceForItemSwap(selectedItemPrefab);

                    if (OutpostBuilder.IsItemSwapAvailable(selectedItemPrefab, out var _) && Campaign.TryPurchase(client: null, price))
                    {
                        if (ItemSwapStorage.TryGetItemSwapStorageForCampaign(Campaign, out ItemSwapStorage? storage))
                        {
                            storage.TryDeductItem(selectedItemPrefab);
                            storage.StoreItem(item.Prefab);
                        }

                        //force info panel to close, so it gets recreated if the player wants to keep looking at the module info
                        builderUi.HideModuleInfoPanel();
                        item.ReplaceWithLinkedItems(selectedItemPrefab);
                        button.Enabled = false;
                        return true;
                    }

                    return false;
                },
            };

            // exit button
            new GUIButton(new RectTransform(new Vector2(0.5f, 0.25f), bottomLayout.RectTransform), TextManager.Get("outpostswap.exit"), textAlignment: Alignment.Center)
            {
                OnClicked = (button, o) =>
                {
                    ClearSwapUI();
                    return true;
                },
            };

            // left button
            CreateButton(increment: false);

            // right button
            CreateButton(increment: true);

            bottomLayout.ForceLayoutRecalculation();

            UpdateSelectedItemUI(item, selectedItemPrefab, selectedItemNameText, selectedItemPriceText, selectedItemSwapButton);

            return;

            void CreateButton(bool increment)
            {
                new GUIButton(new RectTransform(new Vector2(0.1f, 0.25f), swapArea.RectTransform, increment ? Anchor.CenterRight : Anchor.CenterLeft), style: increment ? "GUIButtonToggleRight" : "GUIButtonToggleLeft")
                {
                    Enabled = availableReplacements.Count > 1,
                    OnClicked = (button, o) =>
                    {
                        index += increment ? 1 : -1;

                        if (index >= availableReplacements.Count)
                        {
                            index = 0;
                        }

                        if (index < 0)
                        {
                            index = availableReplacements.Count - 1;
                        }

                        selectedItemPrefab = availableReplacements[index];

                        UpdateSelectedItemUI(item, selectedItemPrefab, selectedItemNameText, selectedItemPriceText, selectedItemSwapButton);
                        return true;
                    },
                };
            }

            void DrawItemSwapPreview(SpriteBatch spriteBatch, GUICustomComponent component)
            {
                if (selectedItemPrefab.SwappableItem is not { } swappable) { return; }

                int exclusiveCount = availableReplacements.Count - 1;

                int indexMinusOne = index - 1,
                    indexPlusOne = index + 1;

                int leftIndex = indexMinusOne < 0 ? exclusiveCount : indexMinusOne;
                int rightIndex = indexPlusOne > exclusiveCount ? 0 : indexPlusOne;

                DrawSprite(spriteBatch, component.Rect, swappable, GetColorForPrefab(selectedItemPrefab));

                float xOffset = component.Rect.Width / 8f;

                if (availableReplacements[leftIndex].SwappableItem is { } leftSwappable)
                {
                    DrawSprite(spriteBatch, new RectangleF(component.Rect.X - xOffset, component.Rect.Y, component.Rect.Width / 2f, component.Rect.Height), leftSwappable, GetColorForPrefab(availableReplacements[leftIndex]) * 0.5f);
                }

                if (availableReplacements[rightIndex].SwappableItem is { } rightSwappable)
                {
                    DrawSprite(spriteBatch, new RectangleF(component.Rect.X + xOffset + component.Rect.Width / 2f, component.Rect.Y, component.Rect.Width / 2f, component.Rect.Height), rightSwappable, GetColorForPrefab(availableReplacements[rightIndex]) * 0.5f);
                }

                return;

                static void DrawSprite(SpriteBatch spriteBatch, RectangleF rect, SwappableItem swappable, Color color)
                {
                    Sprite schematicsSprite = swappable.SchematicSprite;
                    if (schematicsSprite == null) { return; }

                    float schematicsScale = Math.Min(rect.Width / 2f / schematicsSprite.size.X, rect.Height / schematicsSprite.size.Y);
                    Vector2 center = new Vector2(rect.Center.X, rect.Center.Y);
                    schematicsSprite.Draw(spriteBatch, new Vector2(center.X, center.Y), color, new Vector2(schematicsSprite.size.X / 2, schematicsSprite.size.Y / 2),
                                          scale: schematicsScale);
                }

                Color GetColorForPrefab(ItemPrefab prefab)
                {
                    if (selectedItem.TryUnwrap(out Item? it))
                    {
                        if (it.Prefab == prefab)
                        {
                            return GUIStyle.Green;
                        }
                    }

                    return OutpostBuilder.GetPriceForItemSwap(prefab) > 0
                               ? Color.DarkGray
                               : GUIStyle.Orange;
                }
            }
        }

        private static LocalizedString ToPriceText(int price)
        {
            if (price is 0)
            {
                return TextManager.Get("outpostswap.alreadyowned");
            }

            LocalizedString priceText = TextManager.GetWithVariable("currencyformat", "[credits]", price.ToString());
            return priceText;
        }

        private static void UpdateSelectedItemUI(Item item,
                                                 ItemPrefab? newPrefab,
                                                 GUITextBlock? itemNameText,
                                                 GUITextBlock? itemPriceText,
                                                 GUIButton? swapButton)
        {
            if (newPrefab is null) { return; }

            if (itemNameText is null || itemPriceText is null || swapButton is null) { return; }

            itemNameText.Text = newPrefab.Name.Value;
            itemPriceText.TextColor = GUIStyle.TextColorNormal;

            if (item.Prefab == newPrefab)
            {
                itemPriceText.TextColor = GUIStyle.Red;
                itemPriceText.Text = TextManager.Get("outpostswap.sameitem");
                swapButton.Text = TextManager.Get("outpostswap.swapbutton");
                swapButton.Enabled = false;
                return;
            }

            int price = OutpostBuilder.GetPriceForItemSwap(newPrefab);
            if (ItemSwapStorage.TryGetItemSwapStorageForCurrentCampaign(out ItemSwapStorage? storage) && storage.GetItemCount(newPrefab) is int existingCount && existingCount > 0)
            {
                itemPriceText.Text = TextManager.GetWithVariable("outpostswap.alreadyownedwithcount", "[count]", existingCount.ToString());
            }
            else if (!OutpostBuilder.IsItemSwapAvailable(newPrefab, out TechnologyPrefab? technologyPrefab) && technologyPrefab != null)
            {
                itemPriceText.Text = TextManager.GetWithVariable("outpostswap.researchrequired", "[technology]", technologyPrefab.GetDisplayName());
                itemPriceText.TextColor = GUIStyle.Red;
                swapButton.Text = TextManager.Get("outpostswap.swapbutton");
                swapButton.Enabled = false;
                return;
            }
            else
            {
                itemPriceText.Text = ToPriceText(price);
            }

            swapButton.Text = price > 0
                                  ? TextManager.Get("outpostswap.buyandswap")
                                  : TextManager.Get("outpostswap.swapbutton");

            // if the current item is just the hardpoint, make the button stay "install" instead of swap
            // makes it clearer that you're not discarding anything or swapping some existing intallation on the hardpoint
            if (item.Prefab.Identifier == newPrefab.SwappableItem.ReplacementOnUninstall)
            {
                swapButton.Text = TextManager.Get("outpostswap.installbutton");

            }

            if (Campaign != null)
            {
                if (Campaign.CanAfford(price))
                {
                    itemPriceText.TextColor = GUIStyle.Green;
                    swapButton.Enabled = true;
                }
                else
                {
                    itemPriceText.TextColor = GUIStyle.Red;
                    swapButton.Enabled = false;
                }
            }


            if (!OutpostBuilder.AllowedToManageOutpost())
            {
                itemPriceText.TextColor = GUIStyle.Red;
                swapButton.Enabled = false;
            }
        }

        public void OnItemReplaced(Item oldItem, Item newItem)
        {
            builderUi.RecreateMiniMap();
            if (selectedItem.TryUnwrap(out Item? selected) && selected == oldItem)
            {
                selectedItem = Option.Some(newItem);
                CreateSwapUIFor(newItem);
            }

            PersistentOutpostData.RefreshFacilityResources();
            OutpostManagementUI.Instance?.OutpostOverviewUI?.Refresh();
        }

        /// <summary>
        /// Get a list of prefabs to show as available replacements for the given item identifier.
        /// </summary>
        /// <returns>A list of prefabs to show on the swap UI, or an empty array if the item is not swappable.</returns>
        /// <remarks>
        /// If the item is swappable but not valid swaps are found, the list will still at least contain the item itself as a valid replacement.
        /// Thus, GetSwappableItems(ItemPrefab).Length can be checked for 0 to determine if the item has no valid replacements.
        /// </remarks>
        public static ImmutableArray<ItemPrefab> GetSwappableItems(Identifier identifier)
        {
            if (!ItemPrefab.Prefabs.TryGet(identifier, out ItemPrefab? itemPrefab))
            {
                return ImmutableArray<ItemPrefab>.Empty;
            }

            if (prevContentPackagesHash == null ||
                !prevContentPackagesHash.Equals(ContentPackageManager.EnabledPackages.MergedHash))
            {
                BuildCache();

                prevContentPackagesHash = ContentPackageManager.EnabledPackages.MergedHash;
            }

            var currentCache = availableSwapsCache; // Make sure we have a stable snapshot
            return currentCache.TryGetValue(itemPrefab, out ImmutableArray<ItemPrefab> swappableItems)
                       ? swappableItems
                       : ImmutableArray<ItemPrefab>.Empty;

            static void BuildCache()
            {
                var newCache = new Dictionary<ItemPrefab, ImmutableArray<ItemPrefab>>();
                foreach (ItemPrefab prefab in ItemPrefab.Prefabs)
                {
                    if (prefab is not { SwappableItem: not null }) { continue; }

                    if (!FindSwappableItems(prefab, out List<ItemPrefab> availableReplacements)) { continue; }

                    newCache[prefab] = [..availableReplacements];
                }
                
                availableSwapsCache = newCache; // A single atomic operation to avoid issues with threads
            }
        }

        /// <inheritdoc cref="GetSwappableItems(Identifier)"/>
        public static ImmutableArray<ItemPrefab> GetSwappableItems(ItemPrefab prefab)
            => GetSwappableItems(prefab.Identifier);

        /// <summary>
        /// Get a list of prefabs to show as available replacements for the given item.
        /// </summary>
        /// <param name="itemPrefab">The item to get replacements for</param>
        /// <param name="availableReplacements">List of prefabs to show on the swap UI</param>
        /// <returns>true if the item has valid swappable prefabs, false if the item has no replacements.</returns>
        private static bool FindSwappableItems(ItemPrefab itemPrefab, out List<ItemPrefab> availableReplacements)
        {
            availableReplacements = [];
            
            if (itemPrefab.SwappableItem == null) { return false; }

            // Add the hardpoint as a valid replacement
            if (ItemPrefab.Prefabs.TryGet(itemPrefab.SwappableItem.ReplacementOnUninstall, out ItemPrefab? replacementPrefab))
            {
                availableReplacements.Add(replacementPrefab);
            }

            List<ItemPrefab> swaps = ItemPrefab.Prefabs
                .Where(p => p.SwappableItem != null && p.SwappableItem.IsAvailableForSwapping(itemPrefab.SwappableItem.SwapIdentifier))
                .ToList();
            bool hasSwaps = swaps.Count > 0;
            availableReplacements.AddRange(swaps);

            // Always show the item itself as a valid replacement (but don't allow swapping it for itself)
            if (!availableReplacements.Contains(itemPrefab))
            {
                availableReplacements.Add(itemPrefab);
            }

            return hasSwaps;
        }
    }
}