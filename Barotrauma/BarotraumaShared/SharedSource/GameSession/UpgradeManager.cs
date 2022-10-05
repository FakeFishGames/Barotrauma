#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Networking;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal class PurchasedUpgrade
    {
        public readonly UpgradeCategory Category;
        public readonly UpgradePrefab Prefab;
        public int Level;

        public PurchasedUpgrade(UpgradePrefab upgradePrefab, UpgradeCategory category, int level = 1)
        {
            Category = category;
            Prefab = upgradePrefab;
            Level = level;
        }

        public void Deconstruct(out UpgradePrefab prefab, out UpgradeCategory category, out int level)
        {
            prefab = Prefab;
            category = Category;
            level = Level;
        }
    }

    internal class PurchasedItemSwap
    {
        public readonly Item ItemToRemove;
        public readonly ItemPrefab ItemToInstall;

        public PurchasedItemSwap(Item itemToRemove, ItemPrefab itemToInstall)
        {
            ItemToRemove = itemToRemove;
            ItemToInstall = itemToInstall;
        }
    }

    /// <summary>
    /// This class handles all upgrade logic.
    /// Storing, applying, checking and validation of upgrades.
    /// </summary>
    /// <remarks>
    /// Upgrades are applied per item basis meaning each item has their own set of slots for upgrades.
    /// The store applies upgrades globally to categories of items so the purpose of this class is to keep those individual "upgrade slots" in sync.
    /// The target level of an upgrade is stored in the metadata and is what the store displays and modifies while this class will make sure that
    /// the upgrades on the items match the values stored in the metadata.
    /// </remarks>
    partial class UpgradeManager
    {
        /// <summary>
        /// This one toggles whether or not connected submarines get upgraded too.
        /// Could probably be removed, I just didn't like magic numbers.
        /// </summary>
        public const bool UpgradeAlsoConnectedSubs = true;

        /// <summary>
        /// This is used by the client in multiplayer, acts like a secondary PendingUpgrades list
        /// but is not affected by server messages.
        /// </summary>
        /// <remarks>
        /// Not used in singleplayer.
        /// </remarks>
        private List<PurchasedUpgrade>? loadedUpgrades;

        /// <summary>
        /// This is used by the client to notify the server which upgrades are yet to be paid for.
        /// </summary>
        /// <remarks>
        /// In singleplayer this does nothing.
        /// </remarks>
        public readonly List<PurchasedUpgrade> PurchasedUpgrades = new List<PurchasedUpgrade>();

        public readonly List<PurchasedUpgrade> PendingUpgrades = new List<PurchasedUpgrade>();

        public readonly List<PurchasedItemSwap> PurchasedItemSwaps = new List<PurchasedItemSwap>();

        private CampaignMetadata Metadata => Campaign.CampaignMetadata;
        private readonly CampaignMode Campaign;

        public readonly NamedEvent<UpgradeManager> OnUpgradesChanged = new NamedEvent<UpgradeManager>();

        public UpgradeManager(CampaignMode campaign)
        {
            UpgradeCategory.Categories.ForEach(c => c.DeterminePrefabsThatAllowUpgrades());

            DebugConsole.Log("Created brand new upgrade manager.");
            Campaign = campaign;
        }

        public UpgradeManager(CampaignMode campaign, XElement element, bool isSingleplayer) : this(campaign)
        {
            DebugConsole.Log($"Restored upgrade manager from save file, ({element.Elements().Count()} pending upgrades).");

            //backwards compatibility: 
            //upgrades used to be saved to a <pendingupgrades> element, now upgrades and item swaps are saved separately under a <upgrademanager> element
            if (element.Name.LocalName.Equals("pendingupgrades", StringComparison.OrdinalIgnoreCase))
            {
                LoadPendingUpgrades(element, isSingleplayer);
            }
            else
            {
                foreach (var subElement in element.Elements())
                {
                    switch (subElement.Name.ToString().ToLowerInvariant())
                    {
                        case "pendingupgrades":
                            LoadPendingUpgrades(subElement, isSingleplayer);
                            break;
                    }
                }
            }
        }

        public int DetermineItemSwapCost(Item item, ItemPrefab? replacement)
        {
            if (replacement == null)
            {
                replacement = ItemPrefab.Find("", item.Prefab.SwappableItem.ReplacementOnUninstall);
                if (replacement == null)
                {
                    DebugConsole.ThrowError("Failed to determine swap cost for item \"{}\". Trying to uninstall the item but no replacement item found.");
                    return 0;
                }
            }

            int price = 0;
            if (replacement == item.Prefab)
            {
                if (item.PendingItemSwap != null)
                {
                    //refund the pending swap
                    price -= item.PendingItemSwap.SwappableItem.GetPrice(Campaign?.Map?.CurrentLocation);
                    //buy back the current item
                    price += item.Prefab.SwappableItem.GetPrice(Campaign?.Map?.CurrentLocation);
                }
            }
            else
            {
                price = replacement.SwappableItem.GetPrice(Campaign?.Map?.CurrentLocation);
                if (item.PendingItemSwap != null)
                {
                    //refund the pending swap
                    price -= item.PendingItemSwap.SwappableItem.GetPrice(Campaign?.Map?.CurrentLocation);
                    //buy back the current item
                    price += item.Prefab.SwappableItem.GetPrice(Campaign?.Map?.CurrentLocation);
                }
                //refund the current item
                if (replacement != ((MapEntity)item).Prefab)
                {
                    price -= item.Prefab.SwappableItem.GetPrice(Campaign?.Map?.CurrentLocation);
                }
            }
            return price;
        }

        private DateTime lastUpgradeSpeak, lastErrorSpeak;

        /// <summary>
        /// Purchases an upgrade and handles logic for deducting the credit.
        /// </summary>
        /// <remarks>
        /// Purchased upgrades are temporarily stored in <see cref="PendingUpgrades"/> and they are applied
        /// after the next round starts similarly how items are spawned in the stowage room after the round starts.
        /// </remarks>
        public void PurchaseUpgrade(UpgradePrefab prefab, UpgradeCategory category, bool force = false, Client? client = null)
        {
            if (!CanUpgradeSub())
            {
                DebugConsole.ThrowError("Cannot upgrade when switching to another submarine.");
                return;
            }

            int price = prefab.Price.GetBuyprice(GetUpgradeLevel(prefab, category), Campaign.Map?.CurrentLocation);
            int currentLevel = GetUpgradeLevel(prefab, category);

            if (currentLevel + 1 > prefab.MaxLevel)
            {
                DebugConsole.ThrowError($"Tried to purchase \"{prefab.Name}\" over the max level! ({currentLevel + 1} > {prefab.MaxLevel}). The transaction has been cancelled.");
                return;
            }

            if (price < 0)
            {
                Location? location = Campaign.Map?.CurrentLocation;
                LogError($"Upgrade price is less than 0! ({price})",
                    new Dictionary<string, object?>
                    {
                        { "Level", currentLevel },
                        { "Saved Level", GetRealUpgradeLevel(prefab, category) },
                        { "Upgrade", $"{category.Identifier}.{prefab.Identifier}" },
                        { "Location", location?.Type },
                        { "Reputation", $"{location?.Reputation?.Value} / {location?.Reputation?.MaxReputation}" },
                        { "Base Price", prefab.Price.BasePrice }
                    });
            }

            if (force)
            {
                price = 0;
            }

            if (Campaign.TryPurchase(client, price))
            {
                if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
                {
                    // only make the NPC speak if more than 5 minutes have passed since the last purchased service
                    if (lastUpgradeSpeak == DateTime.MinValue || lastUpgradeSpeak.AddMinutes(5) < DateTime.Now)
                    {
                        UpgradeNPCSpeak(TextManager.Get("Dialog.UpgradePurchased").Value, Campaign.IsSinglePlayer);
                        lastUpgradeSpeak = DateTime.Now;
                    }
                }

                GameAnalyticsManager.AddMoneySpentEvent(price, GameAnalyticsManager.MoneySink.SubmarineUpgrade, prefab.Identifier.Value);

                PurchasedUpgrade? upgrade = FindMatchingUpgrade(prefab, category);

#if CLIENT
                DebugLog($"CLIENT: Purchased level {GetUpgradeLevel(prefab, category) + 1} {category.Name}.{prefab.Name} for {price}", GUIStyle.Orange);
#endif

                if (upgrade == null)
                {
                    PendingUpgrades.Add(new PurchasedUpgrade(prefab, category));
                }
                else
                {
                    upgrade.Level++;
                }
#if CLIENT
                // tell the server that this item is yet to be paid for server side
                PurchasedUpgrades.Add(new PurchasedUpgrade(prefab, category));
#endif
                OnUpgradesChanged?.Invoke(this);
            }
            else
            {
                DebugConsole.ThrowError("Tried to purchase an upgrade with insufficient funds, the transaction has not been completed.\n" +
                                        $"Upgrade: {prefab.Name}, Cost: {price}, Have: {Campaign.GetWallet(client).Balance}");
            }
        }

        /// <summary>
        /// Purchases an item swap and handles logic for deducting the credit.
        /// </summary>
        public void PurchaseItemSwap(Item itemToRemove, ItemPrefab itemToInstall, bool force = false, Client? client = null)
        {
            if (!CanUpgradeSub())
            {
                DebugConsole.ThrowError("Cannot swap items when switching to another submarine.");
                return;
            }
            if (itemToRemove == null)
            {
                DebugConsole.ThrowError($"Cannot swap null item!");
                return;
            }
            if (itemToRemove.HiddenInGame)
            {
                DebugConsole.ThrowError($"Cannot swap item \"{itemToRemove.Name}\" because it's set to be hidden in-game.");
                return;
            }
            if (!itemToRemove.AllowSwapping)
            {
                DebugConsole.ThrowError($"Cannot swap item \"{itemToRemove.Name}\" because it's configured to be non-swappable.");
                return;
            }
            if (!UpgradeCategory.Categories.Any(c => c.ItemTags.Any(t => itemToRemove.HasTag(t)) && c.ItemTags.Any(t => itemToInstall.Tags.Contains(t))))
            {
                DebugConsole.ThrowError($"Failed to swap item \"{itemToRemove.Name}\" with \"{itemToInstall.Name}\" (not in the same upgrade category).");
                return;
            }

            if (((MapEntity)itemToRemove).Prefab == itemToInstall)
            {
                DebugConsole.ThrowError($"Failed to swap item \"{itemToRemove.Name}\" (trying to swap with the same item!).");
                return;
            }
            SwappableItem? swappableItem = itemToRemove.Prefab.SwappableItem;
            if (swappableItem == null)
            {
                DebugConsole.ThrowError($"Failed to swap item \"{itemToRemove.Name}\" (not configured as a swappable item).");
                return;
            }

            var linkedItems = GetLinkedItemsToSwap(itemToRemove);

            int price = 0;
            if (!itemToRemove.AvailableSwaps.Contains(itemToInstall))
            {
                price = itemToInstall.SwappableItem.GetPrice(Campaign.Map?.CurrentLocation) * linkedItems.Count;
            }

            if (force)
            {
                price = 0;
            }

            if (Campaign.TryPurchase(client, price))
            {
                PurchasedItemSwaps.RemoveAll(p => linkedItems.Contains(p.ItemToRemove));
                if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
                {
                    // only make the NPC speak if more than 5 minutes have passed since the last purchased service
                    if (lastUpgradeSpeak == DateTime.MinValue || lastUpgradeSpeak.AddMinutes(5) < DateTime.Now)
                    {
                        UpgradeNPCSpeak(TextManager.Get("Dialog.UpgradePurchased").Value, Campaign.IsSinglePlayer);
                        lastUpgradeSpeak = DateTime.Now;
                    }
                }

                GameAnalyticsManager.AddMoneySpentEvent(price, GameAnalyticsManager.MoneySink.SubmarineWeapon, itemToInstall.Identifier.Value);

                foreach (Item itemToSwap in linkedItems)
                {
                    itemToSwap.AvailableSwaps.Add(itemToSwap.Prefab);
                    if (itemToInstall != null && !itemToSwap.AvailableSwaps.Contains(itemToInstall)) 
                    {
                        itemToSwap.PurchasedNewSwap = true;
                        itemToSwap.AvailableSwaps.Add(itemToInstall); 
                    }

                    if (itemToSwap.Prefab != itemToInstall && itemToInstall != null)
                    {
                        itemToSwap.PendingItemSwap = itemToInstall;
                        PurchasedItemSwaps.Add(new PurchasedItemSwap(itemToSwap, itemToInstall));
                        DebugLog($"CLIENT: Swapped item \"{itemToSwap.Name}\" with \"{itemToInstall.Name}\".", Color.Orange);
                    }
                    else
                    {
                        DebugLog($"CLIENT: Cancelled swapping the item \"{itemToSwap.Name}\" with \"{(itemToSwap.PendingItemSwap?.Name ?? null)}\".", Color.Orange);
                    }
                }

                OnUpgradesChanged?.Invoke(this);
            }
            else
            {
                DebugConsole.ThrowError("Tried to swap an item with insufficient funds, the transaction has not been completed.\n" +
                                        $"Item to remove: {itemToRemove.Name}, Item to install: {itemToInstall.Name}, Cost: {price}, Have: {Campaign.GetWallet(client).Balance}");
            }
        }

        /// <summary>
        /// Cancels the currently pending item swap, or uninstalls the item if there's no swap pending
        /// </summary>
        public void CancelItemSwap(Item itemToRemove, bool force = false)
        {
            if (!CanUpgradeSub())
            {
                DebugConsole.ThrowError("Cannot swap items when switching to another submarine.");
                return;
            }

            if (itemToRemove?.PendingItemSwap == null && (itemToRemove?.Prefab.SwappableItem?.ReplacementOnUninstall.IsEmpty ?? true))
            {
                DebugConsole.ThrowError($"Cannot uninstall item \"{itemToRemove?.Name}\" (no replacement item configured).");
                return;
            }

            SwappableItem? swappableItem = itemToRemove.Prefab.SwappableItem;
            if (swappableItem == null)
            {
                DebugConsole.ThrowError($"Failed to uninstall item \"{itemToRemove.Name}\" (not configured as a swappable item).");
                return;
            }

            if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
            {
                // only make the NPC speak if more than 5 minutes have passed since the last purchased service
                if (lastUpgradeSpeak == DateTime.MinValue || lastUpgradeSpeak.AddMinutes(5) < DateTime.Now)
                {
                    UpgradeNPCSpeak(TextManager.Get("Dialog.UpgradePurchased").Value, Campaign.IsSinglePlayer);
                    lastUpgradeSpeak = DateTime.Now;
                }
            }

            var linkedItems = GetLinkedItemsToSwap(itemToRemove);

            foreach (Item itemToCancel in linkedItems)
            {
                if (itemToCancel.PendingItemSwap == null)
                {
                    var replacement = MapEntityPrefab.Find("", swappableItem.ReplacementOnUninstall) as ItemPrefab;
                    if (replacement == null)
                    {
                        DebugConsole.ThrowError($"Failed to uninstall item \"{itemToCancel.Name}\". Could not find the replacement item \"{swappableItem.ReplacementOnUninstall}\".");
                        return;
                    }
                    PurchasedItemSwaps.RemoveAll(p => p.ItemToRemove == itemToCancel);
                    PurchasedItemSwaps.Add(new PurchasedItemSwap(itemToCancel, replacement));
                    DebugLog($"Uninstalled item item \"{itemToCancel.Name}\".", Color.Orange);
                    itemToCancel.PendingItemSwap = replacement;
                }
                else
                {
                    PurchasedItemSwaps.RemoveAll(p => p.ItemToRemove == itemToCancel);
                    DebugLog($"Cancelled swapping the item \"{itemToCancel.Name}\" with \"{itemToCancel.PendingItemSwap.Name}\".", Color.Orange);
                    itemToCancel.PendingItemSwap = null;
                }
            }

#if CLIENT
            OnUpgradesChanged?.Invoke(this);
#endif       
        }

        public static ICollection<Item> GetLinkedItemsToSwap(Item item)
        {
            HashSet<Item> linkedItems = new HashSet<Item>() { item };
            foreach (MapEntity linkedEntity in item.linkedTo)
            {
                foreach (MapEntity secondLinkedEntity in linkedEntity.linkedTo)
                {
                    if (secondLinkedEntity is not Item linkedItem || linkedItem == item) { continue; }
                    if (linkedItem.AllowSwapping &&
                        linkedItem.Prefab.SwappableItem != null && (linkedItem.Prefab.SwappableItem.CanBeBought || item.Prefab.SwappableItem.ReplacementOnUninstall == ((MapEntity)linkedItem).Prefab.Identifier) &&
                        linkedItem.Prefab.SwappableItem.SwapIdentifier.Equals(item.Prefab.SwappableItem.SwapIdentifier, StringComparison.OrdinalIgnoreCase))
                    {
                        linkedItems.Add(linkedItem);
                    }
                }
            }
            return linkedItems;
        }

        /// <summary>
        /// Applies all our pending upgrades to the submarine.
        /// </summary>
        /// <remarks>
        /// Upgrades are applied similarly to how items on the submarine are spawned at the start of the round.
        /// Upgrades should be applied at the start of the round and after the round ends they are written into
        /// the submarine save and saved there.
        /// Because of the difficulty of accessing the actual Submarine object from and outpost or when the campaign UI is created
        /// we modify levels that are shown on the store interface using campaign metadata.
        ///
        /// This method should be called by both the client and the server during level generation.
        /// <see cref="SetUpgradeLevel"/>
        /// <seealso cref="GetUpgradeLevel"/>
        /// </remarks>
        public void ApplyUpgrades()
        {
            PurchasedUpgrades.Clear();
            PurchasedItemSwaps.Clear();
            if (Submarine.MainSub == null) { return; }

            List<PurchasedUpgrade> pendingUpgrades = PendingUpgrades;

            if (Level.Loaded is { Type: LevelData.LevelType.Outpost })
            {
                return;
            }

            if (GameMain.NetworkMember is { IsClient: true })
            {
                if (loadedUpgrades != null)
                {
                    // client receives pending upgrades from the save file
                    pendingUpgrades = loadedUpgrades;
                }
            }

            DebugConsole.Log("Applying upgrades...");
            foreach (var (prefab, category, level) in pendingUpgrades)
            {
                int newLevel = BuyUpgrade(prefab, category, Submarine.MainSub, level);
                DebugConsole.Log($"    - {category.Identifier}.{prefab.Identifier} lvl. {level}, new: ({newLevel})");
                SetUpgradeLevel(prefab, category, Math.Clamp(GetRealUpgradeLevel(prefab, category) + level, 0, prefab.MaxLevel));
            }

            PendingUpgrades.Clear();
            loadedUpgrades?.Clear();
            loadedUpgrades = null;
        }

        public void CreateUpgradeErrorMessage(string text, bool isSinglePlayer, Character character)
        {
            // 10 second cooldown on the error message but not the UI sound
            if (lastErrorSpeak == DateTime.MinValue || lastErrorSpeak.AddSeconds(10) < DateTime.Now)
            {
                UpgradeNPCSpeak(text, isSinglePlayer, character);
                lastErrorSpeak = DateTime.Now;
            }
#if CLIENT
            SoundPlayer.PlayUISound(GUISoundType.PickItemFail);
#endif
        }

        /// <summary>
        /// Makes the NPC talk or if no NPC has been specified find the upgrade NPC and make it talk.
        /// </summary>
        /// <param name="text"></param>
        /// <param name="isSinglePlayer"></param>
        /// <param name="character">Optional NPC to make talk, if null tries to find one at the outpost.</param>
        /// <remarks>
        /// This might seem a bit spaghetti but it's the only way I could figure out how to do this and make it work
        /// in both multiplayer and singleplayer because in multiplayer the client doesn't have access to SubmarineInfo.OutpostNPCs list
        /// so we cannot find the upgrade NPC using that and the client cannot use Character.Speak anyways in multiplayer so the alternative
        /// is to send network packages when interacting with the NPC.
        /// </remarks>
        partial void UpgradeNPCSpeak(string text, bool isSinglePlayer, Character? character = null);

        /// <summary>
        /// Validates that upgrade values stored in CampaignMetadata matches the values on the submarine and fixes any inconsistencies.
        /// Should be called after every round start right after <see cref="ApplyUpgrades"/>
        /// </summary>
        /// <param name="submarine"></param>
        public void SanityCheckUpgrades()
        {
            Submarine submarine = GameMain.GameSession?.Submarine ?? Submarine.MainSub;
            if (submarine is null) { return; }

            // check walls
            foreach (Structure wall in submarine.GetWalls(UpgradeAlsoConnectedSubs))
            {
                foreach (UpgradeCategory category in UpgradeCategory.Categories)
                {
                    foreach (UpgradePrefab prefab in UpgradePrefab.Prefabs)
                    {
                        if (!prefab.IsWallUpgrade) { continue; }
                        TryFixUpgrade(wall, category, prefab);
                    }
                }
            }

            // Check items
            foreach (Item item in submarine.GetItems(UpgradeAlsoConnectedSubs))
            {
                foreach (UpgradeCategory category in UpgradeCategory.Categories)
                {
                    foreach (UpgradePrefab prefab in UpgradePrefab.Prefabs)
                    {
                        TryFixUpgrade(item, category, prefab);
                    }
                }
            }

            void TryFixUpgrade(MapEntity entity, UpgradeCategory category, UpgradePrefab prefab)
            {
                if (!category.CanBeApplied(entity, prefab)) { return; }

                int level = GetRealUpgradeLevel(prefab, category);
                int maxLevel = submarine.Info is { } info ? prefab.GetMaxLevel(info) : prefab.MaxLevel;
                if (maxLevel < level) { level = maxLevel; }

                if (level == 0) { return; }

                Upgrade? upgrade = entity.GetUpgrade(prefab.Identifier);

                if (upgrade == null || upgrade.Level != level)
                {
                    DebugLog($"{entity.Prefab.Name} has incorrect \"{prefab.Name}\" level! Expected {level} but got {upgrade?.Level ?? 0}. Fixing...");
                    FixUpgradeOnItem((ISerializableEntity)entity, prefab, level);
                }
            }
        }

        private static void FixUpgradeOnItem(ISerializableEntity target, UpgradePrefab prefab, int level)
        {
            if (target is MapEntity mapEntity)
            {
                // do not fix what's not broken
                if (level == 0) { return; }

                mapEntity.SetUpgrade(new Upgrade(target, prefab, level), false);
            }
        }

        /// <summary>
        /// Applies an upgrade on the submarine, should be called by <see cref="ApplyUpgrades"/> when the round starts.
        /// </summary>
        /// <param name="prefab"></param>
        /// <param name="category"></param>
        /// <param name="submarine"></param>
        /// <param name="level"></param>
        /// <returns>New level that was applied, -1 if no upgrades were applied.</returns>
        private static int BuyUpgrade(UpgradePrefab prefab, UpgradeCategory category, Submarine submarine, int level = 1, Submarine? parentSub = null)
        {
            int? newLevel = null;
            if (category.IsWallUpgrade)
            {
                foreach (Structure structure in submarine.GetWalls(UpgradeAlsoConnectedSubs))
                {
                    Upgrade upgrade = new Upgrade(structure, prefab, level);
                    structure.AddUpgrade(upgrade, createNetworkEvent: false);

                    Upgrade? newUpgrade = structure.GetUpgrade(prefab.Identifier);
                    if (newUpgrade != null)
                    {
                        SanityCheck(newUpgrade, structure);
                        newLevel ??= newUpgrade.Level;
                    }
                }
            }
            else
            {
                foreach (Item item in submarine.GetItems(UpgradeAlsoConnectedSubs))
                {
                    if (category.CanBeApplied(item, prefab))
                    {
                        Upgrade upgrade = new Upgrade(item, prefab, level);
                        item.AddUpgrade(upgrade, createNetworkEvent: false);

                        Upgrade? newUpgrade = item.GetUpgrade(prefab.Identifier);
                        if (newUpgrade != null)
                        {
                            SanityCheck(newUpgrade, item);
                            newLevel ??= newUpgrade.Level;
                        }
                    }
                }
            }

            foreach (Submarine loadedSub in Submarine.Loaded.Where(sub => sub != submarine))
            {
                if (loadedSub == parentSub) { continue; }
                XElement? root = loadedSub.Info?.SubmarineElement;
                if (root == null) { continue; }

                if (root.Name.ToString().Equals("LinkedSubmarine", StringComparison.OrdinalIgnoreCase))
                {
                    if (root.Attribute("location") == null) { continue; }

                    // Check if this is our linked submarine
                    ushort dockingPortID = (ushort) root.GetAttributeInt("originallinkedto", 0);
                    if (dockingPortID > 0 && submarine.GetItems(true).Any(item => item.ID == dockingPortID))
                    {
                        BuyUpgrade(prefab, category, loadedSub, level, submarine);
                    }
                }
            }

            return newLevel ?? -1;

            void SanityCheck(Upgrade newUpgrade, MapEntity target)
            {
                if (newLevel != null && newLevel != newUpgrade.Level)
                {
                    // automatically fix this if it ever happens?
                    DebugConsole.AddWarning($"The upgrade {newUpgrade.Prefab.Name} in {target.Name} has a different level compared to other items! \n" +
                                            $"Expected level was ${newLevel} but got {newUpgrade.Level} instead.");
                }
            }
        }

        /// <summary>
        /// Gets the progress that is shown on the store interface.
        /// Includes values stored in the metadata and <see cref="PendingUpgrades"/>
        /// </summary>
        /// <param name="prefab"></param>
        /// <param name="category"></param>
        /// <returns></returns>
        public int GetUpgradeLevel(UpgradePrefab prefab, UpgradeCategory category)
        {
            if (!Metadata.HasKey(FormatIdentifier(prefab, category))) { return GetPendingLevel(); }

            return GetRealUpgradeLevel(prefab, category) + GetPendingLevel();

            int GetPendingLevel()
            {
                PurchasedUpgrade? upgrade = FindMatchingUpgrade(prefab, category);
                return upgrade?.Level ?? 0;
            }
        }

        /// <summary>
        /// Gets the level of the upgrade that is stored in the metadata.
        /// </summary>
        /// <param name="prefab"></param>
        /// <param name="category"></param>
        /// <returns></returns>
        public int GetRealUpgradeLevel(UpgradePrefab prefab, UpgradeCategory category)
        {
            return !Metadata.HasKey(FormatIdentifier(prefab, category)) ? 0 : Metadata.GetInt(FormatIdentifier(prefab, category), 0);
        }

        /// <summary>
        /// Stores the target upgrade level in the campaign metadata.
        /// </summary>
        /// <param name="prefab"></param>
        /// <param name="category"></param>
        /// <param name="level"></param>
        private void SetUpgradeLevel(UpgradePrefab prefab, UpgradeCategory category, int level)
        {
            Metadata.SetValue(FormatIdentifier(prefab, category), level);
        }

        public bool CanUpgradeSub()
        {
            return 
                Campaign.PendingSubmarineSwitch == null || 
                Campaign.PendingSubmarineSwitch.Name == Submarine.MainSub.Info.Name;
        }

        public void Save(XElement? parent)
        {
            if (parent == null) { return; }

            var upgradeManagerElement = new XElement("upgrademanager");
            parent.Add(upgradeManagerElement);

            SavePendingUpgrades(upgradeManagerElement, PendingUpgrades);
        }

        private static void SavePendingUpgrades(XElement? parent, List<PurchasedUpgrade> upgrades)
        {
            if (parent == null) { return; }

            DebugConsole.Log("Saving pending upgrades to save file...");
            XElement upgradeElement = new XElement("PendingUpgrades");
            foreach (var (prefab, category, level) in upgrades)
            {
                upgradeElement.Add(new XElement("PendingUpgrade",
                    new XAttribute("category", category.Identifier),
                    new XAttribute("prefab", prefab.Identifier),
                    new XAttribute("level", level)));
            }

            DebugConsole.Log($"Saved {upgradeElement.Elements().Count()} pending upgrades.");
            parent.Add(upgradeElement);
        }

        private void LoadPendingUpgrades(XElement? element, bool isSingleplayer = true)
        {
            if (!(element is { HasElements: true })) { return; }

            List<PurchasedUpgrade> pendingUpgrades = new List<PurchasedUpgrade>();

            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (XElement upgrade in element.Elements())
            {
                Identifier categoryIdentifier = upgrade.GetAttributeIdentifier("category", Identifier.Empty);
                UpgradeCategory? category = UpgradeCategory.Find(categoryIdentifier);
                if (categoryIdentifier.IsEmpty || category == null) { continue; }

                Identifier prefabIdentifier = upgrade.GetAttributeIdentifier("prefab", Identifier.Empty);
                UpgradePrefab? prefab = UpgradePrefab.Find(prefabIdentifier);
                if (prefabIdentifier.IsEmpty || prefab == null) { continue; }

                int level = upgrade.GetAttributeInt("level", -1);
                if (level < 0) { continue; }

                pendingUpgrades.Add(new PurchasedUpgrade(prefab, category, level));
            }

#if CLIENT
            if (isSingleplayer)
            {
                SetPendingUpgrades(pendingUpgrades);
            }
            else
            {
                loadedUpgrades = pendingUpgrades;
            }
#else
            SetPendingUpgrades(pendingUpgrades);
#endif
        }

        public static void LogError(string text, Dictionary<string, object?> data, Exception? e = null)
        {
            string error = $"{text}\n";
            foreach (var (label, value) in data)
            {
                error += $"    - {label}: {value ?? "NULL"}\n";
            }

            DebugConsole.ThrowError(error.TrimEnd('\n'), e);
        }

        /// <summary>
        /// Used to sync the pending upgrades list in multiplayer.
        /// </summary>
        /// <param name="upgrades"></param>
        public void SetPendingUpgrades(List<PurchasedUpgrade> upgrades)
        {
            PendingUpgrades.Clear();
            PendingUpgrades.AddRange(upgrades);
            OnUpgradesChanged?.Invoke(this);
        }

        public static void DebugLog(string msg, Color? color = null)
        {
#if DEBUG
            DebugConsole.NewMessage(msg, color ?? Color.GreenYellow);
#else
            DebugConsole.Log(msg);
#endif
        }

        private PurchasedUpgrade? FindMatchingUpgrade(UpgradePrefab prefab, UpgradeCategory category) => PendingUpgrades.Find(u => u.Prefab == prefab && u.Category == category);

        private static Identifier FormatIdentifier(UpgradePrefab prefab, UpgradeCategory category) => $"upgrade.{category.Identifier}.{prefab.Identifier}".ToIdentifier();
    }
}