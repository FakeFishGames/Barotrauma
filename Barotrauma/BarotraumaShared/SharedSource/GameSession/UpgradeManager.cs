#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
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
        /// Prevents the player from upgrading the submarine when we are switching to a new one.
        /// </summary>
        /// <remarks>
        /// In singleplayer we check if CampaignMode.PendingSubmarineSwitch is not null indicating we are switching submarines
        /// but in multiplayer that value is not synced so we use this variable instead by setting it to false in <see cref="UpgradeManager.ClientRead"/> 
        /// and then set it back to true when the round ends in <see cref="MultiPlayerCampaign.End"/>
        /// </remarks>
        public bool CanUpgrade = true;

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
        private int spentMoney;

        public event Action? OnUpgradesChanged;

        public UpgradeManager(CampaignMode campaign)
        {
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
                foreach (XElement subElement in element.Elements())
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

        public int DetermineItemSwapCost(Item item, ItemPrefab replacement)
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
                if (replacement != item.prefab)
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
        public void PurchaseUpgrade(UpgradePrefab prefab, UpgradeCategory category, bool force = false)
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

            if (Campaign.Money >= price)
            {
                if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
                {
                    // only make the NPC speak if more than 5 minutes have passed since the last purchased service
                    if (lastUpgradeSpeak == DateTime.MinValue || lastUpgradeSpeak.AddMinutes(5) < DateTime.Now)
                    {
                        UpgradeNPCSpeak(TextManager.Get("Dialog.UpgradePurchased"), Campaign.IsSinglePlayer);
                        lastUpgradeSpeak = DateTime.Now;
                    }
                }

                Campaign.Money -= price;
                spentMoney += price;

                PurchasedUpgrade? upgrade = FindMatchingUpgrade(prefab, category);

#if CLIENT
                DebugLog($"CLIENT: Purchased level {GetUpgradeLevel(prefab, category) + 1} {category.Name}.{prefab.Name} for {price}", GUI.Style.Orange);
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
                OnUpgradesChanged?.Invoke();
            }
            else
            {
                DebugConsole.ThrowError("Tried to purchase an upgrade with insufficient funds, the transaction has not been completed.\n" +
                                        $"Upgrade: {prefab.Name}, Cost: {price}, Have: {Campaign.Money}");
            }
        }

        /// <summary>
        /// Purchases an item swap and handles logic for deducting the credit.
        /// </summary>
        public void PurchaseItemSwap(Item itemToRemove, ItemPrefab itemToInstall, bool force = false)
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
            if (!UpgradeCategory.Categories.Any(c => c.ItemTags.Any(t => itemToRemove.HasTag(t)) && c.ItemTags.Any(t => itemToInstall.Tags.Contains(t))))
            {
                DebugConsole.ThrowError($"Failed to swap item \"{itemToRemove.Name}\" with \"{itemToInstall.Name}\" (not in the same upgrade category).");
                return;
            }

            /*if (itemToRemove.PendingItemSwap != null)
            {
                CancelItemSwap(itemToRemove);
            }
            else */
            if (itemToRemove.prefab == itemToInstall)
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

            int price = 0;
            if (!itemToRemove.AvailableSwaps.Contains(itemToInstall))
            {
                price = itemToInstall.SwappableItem.GetPrice(Campaign.Map?.CurrentLocation);
            }

            if (force)
            {
                price = 0;
            }

            if (Campaign.Money >= price)
            {
                PurchasedItemSwaps.RemoveAll(p => p.ItemToRemove == itemToRemove);
                if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
                {
                    // only make the NPC speak if more than 5 minutes have passed since the last purchased service
                    if (lastUpgradeSpeak == DateTime.MinValue || lastUpgradeSpeak.AddMinutes(5) < DateTime.Now)
                    {
                        UpgradeNPCSpeak(TextManager.Get("Dialog.UpgradePurchased"), Campaign.IsSinglePlayer);
                        lastUpgradeSpeak = DateTime.Now;
                    }
                }

                Campaign.Money -= price;
                spentMoney += price;

                itemToRemove.AvailableSwaps.Add(itemToRemove.Prefab);
                if (itemToInstall != null) { itemToRemove.AvailableSwaps.Add(itemToInstall); }

                if (itemToRemove.Prefab != itemToInstall && itemToInstall != null)
                {
                    itemToRemove.PendingItemSwap = itemToInstall;
                    PurchasedItemSwaps.Add(new PurchasedItemSwap(itemToRemove, itemToInstall));
                    DebugLog($"CLIENT: Swapped item \"{itemToRemove.Name}\" with \"{itemToInstall.Name}\".", Color.Orange);
                }
                else
                {
                    DebugLog($"CLIENT: Cancelled swapping the item \"{itemToRemove.Name}\" with \"{(itemToRemove.PendingItemSwap?.Name ?? null)}\".", Color.Orange);
                }
                OnUpgradesChanged?.Invoke();
            }
            else
            {
                DebugConsole.ThrowError("Tried to swap an item with insufficient funds, the transaction has not been completed.\n" +
                                        $"Item to remove: {itemToRemove.Name}, Item to install: {itemToInstall.Name}, Cost: {price}, Have: {Campaign.Money}");
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

            if (itemToRemove?.PendingItemSwap == null && string.IsNullOrEmpty(itemToRemove?.Prefab.SwappableItem?.ReplacementOnUninstall))
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
                    UpgradeNPCSpeak(TextManager.Get("Dialog.UpgradePurchased"), Campaign.IsSinglePlayer);
                    lastUpgradeSpeak = DateTime.Now;
                }
            }

            if (itemToRemove.PendingItemSwap == null)
            {
                var replacement = MapEntityPrefab.Find("", swappableItem.ReplacementOnUninstall) as ItemPrefab;
                if (replacement == null)
                {
                    DebugConsole.ThrowError($"Failed to uninstall item \"{itemToRemove.Name}\". Could not find the replacement item \"{swappableItem.ReplacementOnUninstall}\".");
                    return;
                }
                PurchasedItemSwaps.RemoveAll(p => p.ItemToRemove == itemToRemove);
                PurchasedItemSwaps.Add(new PurchasedItemSwap(itemToRemove, replacement));
                DebugLog($"Uninstalled item item \"{itemToRemove.Name}\".", Color.Orange);
                itemToRemove.PendingItemSwap = replacement;
            }
            else
            {
                PurchasedItemSwaps.RemoveAll(p => p.ItemToRemove == itemToRemove);
                DebugLog($"Cancelled swapping the item \"{itemToRemove.Name}\" with \"{itemToRemove.PendingItemSwap.Name}\".", Color.Orange);
                itemToRemove.PendingItemSwap = null;
            }
#if CLIENT
            OnUpgradesChanged?.Invoke();
#endif       
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

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
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
                if (newLevel > 0)
                {
                    SetUpgradeLevel(prefab, category, Math.Clamp(newLevel, 0, prefab.MaxLevel));
                }
            }

            PendingUpgrades.Clear();
            loadedUpgrades?.Clear();
            loadedUpgrades = null;
            spentMoney = 0;
        }

        /// <summary>
        /// Cancels the pending upgrades and refunds the money spent
        /// </summary>
        private void RefundUpgrades()
        {
            DebugConsole.Log($"Refunded {spentMoney} marks in pending upgrades.");
            if (spentMoney > 0)
            {
#if CLIENT
                GUIMessageBox msgBox = new GUIMessageBox(TextManager.Get("UpgradeRefundTitle"), TextManager.Get("UpgradeRefundBody"), new[] { TextManager.Get("Ok") });
                msgBox.Buttons[0].OnClicked += msgBox.Close;
#endif
            }

            Campaign.Money += spentMoney;
            spentMoney = 0;
            PendingUpgrades.Clear();
            PurchasedUpgrades.Clear();
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
        public void SanityCheckUpgrades(Submarine submarine)
        {
            // check walls
            foreach (Structure wall in submarine.GetWalls(UpgradeAlsoConnectedSubs))
            {
                foreach (UpgradeCategory category in UpgradeCategory.Categories)
                {
                    foreach (UpgradePrefab prefab in UpgradePrefab.Prefabs)
                    {
                        int level = GetRealUpgradeLevel(prefab, category);
                        if (level == 0 || !prefab.IsWallUpgrade) { continue; }

                        Upgrade? upgrade = wall.GetUpgrade(prefab.Identifier);

                        bool isOverMax = IsOverMaxLevel(level, prefab);
                        if (isOverMax)
                        {
                            SetUpgradeLevel(prefab, category, prefab.MaxLevel);
                            level = prefab.MaxLevel;
                        }

                        if (upgrade == null || upgrade.Level != level || isOverMax)
                        {
                            DebugConsole.AddWarning($"{wall.prefab.Name} has incorrect \"{prefab.Name}\" level! Expected {level} but got {upgrade?.Level ?? 0}. Fixing...");
                            FixUpgradeOnItem(wall, prefab, level);
                        }
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
                        if (!category.CanBeApplied(item, prefab)) { continue; }

                        int level = GetRealUpgradeLevel(prefab, category);
                        if (level == 0) { continue; }

                        Upgrade? upgrade = item.GetUpgrade(prefab.Identifier);
                        bool isOverMax = IsOverMaxLevel(level, prefab);
                        if (isOverMax)
                        {
                            SetUpgradeLevel(prefab, category, prefab.MaxLevel);
                            level = prefab.MaxLevel;
                        }

                        if (upgrade == null || upgrade.Level != level || isOverMax)
                        {
                            DebugConsole.AddWarning($"{item.prefab.Name} has incorrect \"{prefab.Name}\" level! Expected {level} but got {upgrade?.Level ?? 0}{(isOverMax ? " (Over max level!)" : string.Empty)}. Fixing...");
                            FixUpgradeOnItem(item, prefab, level);
                        }
                    }
                }
            }

            static bool IsOverMaxLevel(int level, UpgradePrefab prefab) => level > prefab.MaxLevel;
        }

        private static void FixUpgradeOnItem(ISerializableEntity target, UpgradePrefab prefab, int level)
        {
            if (target is MapEntity mapEntity)
            {
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
        private static int BuyUpgrade(UpgradePrefab prefab, UpgradeCategory category, Submarine submarine, int level = 1)
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
                XElement? root = loadedSub.Info?.SubmarineElement;
                if (root == null) { continue; }

                if (root.Name.ToString().Equals("LinkedSubmarine", StringComparison.OrdinalIgnoreCase))
                {
                    if (root.Attribute("location") == null) { continue; }

                    // Check if this is our linked submarine
                    ushort dockingPortID = (ushort) root.GetAttributeInt("originallinkedto", 0);
                    if (dockingPortID > 0 && submarine.GetItems(true).Any(item => item.ID == dockingPortID))
                    {
                        BuyUpgrade(prefab, category, loadedSub, level);
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
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return CanUpgrade; }

            return Campaign.PendingSubmarineSwitch == null;
        }

        public void RefundResetAndReload(SubmarineInfo newSubmarine, bool notifyClients = false)
        {
            RefundUpgrades();
            ResetUpgrades();
            Dictionary<string, int> newUpgrades = ReloadUpgradeValues(newSubmarine);
#if SERVER
            if (notifyClients)
            {
                SendUpgradeResetMessage(newUpgrades);
            }
#endif
        }

        /// <summary>
        /// Parses a SubmarineInfo and sets the store values accordingly.
        /// Used when reloading a previously saved submarine. 
        /// </summary>
        /// <param name="info"></param>
        private Dictionary<string, int> ReloadUpgradeValues(SubmarineInfo info)
        {
            Dictionary<string, int> newValues = new Dictionary<string, int>();
            IEnumerable<XElement> linkedSubElements = info.SubmarineElement.Elements().Where(element => element.Name.ToString().Equals("LinkedSubmarine", StringComparison.OrdinalIgnoreCase)).SelectMany(element => element.Elements());
            IEnumerable<XElement> mainSubElements = info.SubmarineElement.Elements().Where(Predicate);
            List<XElement> elements = mainSubElements.Concat(linkedSubElements.Where(Predicate)).ToList();
            foreach (UpgradeCategory category in UpgradeCategory.Categories)
            {
                foreach (UpgradePrefab prefab in UpgradePrefab.Prefabs)
                {
                    if (!prefab.UpgradeCategories.Contains(category)) { continue; }

                    List<int> levels = GetUpgradeFromXML(elements, category, prefab);
                    if (levels.Any())
                    {
                        int level = (int) levels.Average(i => i);
                        newValues.Add(FormatIdentifier(prefab, category), level);
                    }
                }
            }

            foreach (var (dataIdentifier, level) in newValues)
            {
                Campaign.CampaignMetadata.SetValue(dataIdentifier, level);
            }

            return newValues;

            static List<int> GetUpgradeFromXML(List<XElement> elements, UpgradeCategory category, UpgradePrefab prefab)
            {
                List<int> levels = new List<int>();
                foreach (XElement subElement in elements)
                {
                    if (!category.CanBeApplied(subElement, prefab)) { continue; }

                    foreach (XElement component in subElement.Elements())
                    {
                        if (string.Equals(component.Name.ToString(), "upgrade", StringComparison.OrdinalIgnoreCase))
                        {
                            string identifier = component.GetAttributeString("identifier", string.Empty);
                            int level = component.GetAttributeInt("level", -1);
                            if (string.IsNullOrWhiteSpace(identifier) || level <= 0) { continue; }

                            UpgradePrefab? matchingPrefab = UpgradePrefab.Find(identifier);
                            if (matchingPrefab == null || matchingPrefab != prefab) { continue; }

                            if (matchingPrefab.UpgradeCategories.Contains(category)) { levels.Add(level); }
                        }
                    }
                }

                return levels;
            }

            static bool Predicate(XElement element) => element.HasElements && element.Elements().Any(e => e.Name.ToString().Equals("upgrade", StringComparison.OrdinalIgnoreCase));
        }


        /// <summary>
        /// Resets our upgrade progress and prices.
        /// This does not actually remove the upgrades from the submarine but resets the store interface.
        /// </summary>
        /// <remarks>
        /// This method works by iterating thru all upgrade categories and prefabs and checking if they have a
        /// valid key stored in the metadata, if they do set it to 0, upgrades without a key stored are always
        /// assumed to be 0 so they don't need to be reset.
        ///
        /// Should initially be called server side as we can't trust clients with such a simple notification.
        /// </remarks>
        private void ResetUpgrades()
        {
            foreach (UpgradeCategory category in UpgradeCategory.Categories)
            {
                foreach (UpgradePrefab prefab in UpgradePrefab.Prefabs)
                {
                    if (!prefab.UpgradeCategories.Contains(category)) { continue; }

                    string dataIdentifier = FormatIdentifier(prefab, category);
                    if (Metadata.HasKey(dataIdentifier))
                    {
                        Metadata.SetValue(dataIdentifier, 0);
                    }
                }
            }

            OnUpgradesChanged?.Invoke();
        }

        public void Save(XElement? parent)
        {
            if (parent == null) { return; }

            var upgradeManagerElement = new XElement("upgrademanager");
            parent.Add(upgradeManagerElement);

            SavePendingUpgrades(upgradeManagerElement, PendingUpgrades);
        }

        private void SavePendingUpgrades(XElement? parent, List<PurchasedUpgrade> upgrades)
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
            if (element == null || !element.HasElements) { return; }

            List<PurchasedUpgrade> pendingUpgrades = new List<PurchasedUpgrade>();

            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (XElement upgrade in element.Elements())
            {
                string? categoryIdentifier = upgrade.GetAttributeString("category", null);
                UpgradeCategory? category = UpgradeCategory.Find(categoryIdentifier);
                if (string.IsNullOrWhiteSpace(categoryIdentifier) || category == null) { continue; }

                string? prefabIdentifier = upgrade.GetAttributeString("prefab", null);
                UpgradePrefab? prefab = UpgradePrefab.Find(prefabIdentifier);
                if (string.IsNullOrWhiteSpace(prefabIdentifier) || prefab == null) { continue; }

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

        public static Dictionary<string, int> GetMetadataLevels(CampaignMetadata? metadata)
        {
            Dictionary<string, int> values = new Dictionary<string, int>();

            if (metadata == null) { return values; }

            foreach (UpgradeCategory category in UpgradeCategory.Categories)
            {
                foreach (UpgradePrefab prefab in UpgradePrefab.Prefabs)
                {
                    string identifier = FormatIdentifier(prefab, category);
                    if (metadata.HasKey(identifier) && !values.ContainsKey(identifier))
                    {
                        values.Add(identifier, metadata.GetInt(identifier));
                    }
                }
            }

            return values;
        }

        /// <summary>
        /// Used to sync the pending upgrades list in multiplayer.
        /// </summary>
        /// <param name="upgrades"></param>
        /// <remarks>
        /// In singleplayer this is not used and should not be.
        /// </remarks>
        public void SetPendingUpgrades(List<PurchasedUpgrade> upgrades)
        {
            PendingUpgrades.Clear();
            PendingUpgrades.AddRange(upgrades);
            OnUpgradesChanged?.Invoke();
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

        private static string FormatIdentifier(UpgradePrefab prefab, UpgradeCategory category) => $"upgrade.{category.Identifier}.{prefab.Identifier}";
    }
}