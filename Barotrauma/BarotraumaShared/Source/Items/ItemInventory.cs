using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System;

namespace Barotrauma
{
    class ItemInventory : Inventory
    {
        ItemContainer container;

        public ItemInventory(Item owner, ItemContainer container, int capacity, Vector2? centerPos = null, int slotsPerRow = 5)
            : base(owner, capacity, centerPos, slotsPerRow)
        {
            this.container = container;
        }

        public override int FindAllowedSlot(Item item)
        {
            if (ItemOwnsSelf(item)) return -1;

            for (int i = 0; i < capacity; i++)
            {
                //item is already in the inventory!
                if (Items[i] == item) return -1;
            }

            if (!container.CanBeContained(item)) return -1;

            for (int i = 0; i < capacity; i++)
            {
                if (Items[i] == null) return i;
            }

            return -1;
        }

        public override bool CanBePut(Item item, int i)
        {
            if (ItemOwnsSelf(item)) return false;
            if (i < 0 || i >= Items.Length) return false;
            return (item!=null && Items[i]==null && container.CanBeContained(item));
        }


        public override bool TryPutItem(Item item, Character user, List<InvSlotType> allowedSlots = null, bool createNetworkEvent = true)
        {
            bool wasPut = base.TryPutItem(item, user, allowedSlots, createNetworkEvent);

            if (wasPut)
            {
                foreach (Character c in Character.CharacterList)
                {
                    if (!c.HasSelectedItem(item)) continue;

                    item.Unequip(c);
                    break;
                }

                container.IsActive = true;
                container.OnItemContained(item);
            }

            return wasPut;
        }

        public override bool TryPutItem(Item item, int i, bool allowSwapping, bool allowCombine, Character user, bool createNetworkEvent = true)
        {
            bool wasPut = base.TryPutItem(item, i, allowSwapping, allowCombine, user, createNetworkEvent);

            if (wasPut)
            {
                if (GameMain.NilMod.EnableGriefWatcher && GameMain.Server != null && user != null)
                {
                    Barotrauma.Networking.Client warnedclient = GameMain.Server.ConnectedClients.Find(c => c.Character == user);
                    if (warnedclient != null)
                    {
                        Item ownerasitem = Owner as Item;

                        //Detonator + Explosives checks
                        for (int y = 0; y < NilMod.NilModGriefWatcher.GWListDetonators.Count; y++)
                        {
                            if (NilMod.NilModGriefWatcher.GWListDetonators[y] == ownerasitem.Name)
                            {
                                for (int z = 0; z < NilMod.NilModGriefWatcher.GWListExplosives.Count; z++)
                                {
                                    if (NilMod.NilModGriefWatcher.GWListExplosives[z] == item.Name)
                                    {
                                        NilMod.NilModGriefWatcher.SendWarning(user.LogName
                                            + " placed explosive " + item.Name
                                            + " into " + ownerasitem.Name, warnedclient);
                                    }
                                }
                            }
                        }

                        //Railgun Ammo Loading checks
                        for (int y = 0; y < NilMod.NilModGriefWatcher.GWListRailgunRacks.Count; y++)
                        {
                            if (NilMod.NilModGriefWatcher.GWListRailgunRacks[y] == ownerasitem.Name)
                            {
                                for (int z = 0; z < NilMod.NilModGriefWatcher.GWListRailgunAmmo.Count; z++)
                                {
                                    if (NilMod.NilModGriefWatcher.GWListRailgunAmmo[z] == item.Name)
                                    {
                                        if (item.ContainedItems == null || item.ContainedItems.All(it => it == null))
                                        {
                                            NilMod.NilModGriefWatcher.SendWarning(user.LogName
                                                + " Loaded " + item.Name
                                                + " into " + ownerasitem.Name, warnedclient);
                                        }
                                        else
                                        {
                                            NilMod.NilModGriefWatcher.SendWarning(user.LogName
                                                + " Loaded " + item.Name
                                                + " into " + ownerasitem.Name
                                                + " (" + string.Join(", ", System.Array.FindAll(item.ContainedItems, it => it != null).Select(it => it.Name))
                                                + ")", warnedclient);
                                        }
                                    }
                                }
                            }
                        }
                        //Syringe Chemical checks
                        for (int y = 0; y < NilMod.NilModGriefWatcher.GWListSyringes.Count; y++)
                        {
                            if (NilMod.NilModGriefWatcher.GWListSyringes[y] == ownerasitem.Name)
                            {
                                for (int z = 0; z < NilMod.NilModGriefWatcher.GWListSyringechems.Count; z++)
                                {
                                    if (NilMod.NilModGriefWatcher.GWListSyringechems[z] == item.Name)
                                    {

                                        NilMod.NilModGriefWatcher.SendWarning(user.LogName
                                            + " placed dangerous chemical "
                                            + item.Name + " into "
                                            + ownerasitem.Name, warnedclient);
                                    }
                                }
                            }
                        }

                        //Ranged weapon ammo checks
                        for (int y = 0; y < NilMod.NilModGriefWatcher.GWListRanged.Count; y++)
                        {
                            if (NilMod.NilModGriefWatcher.GWListRanged[y] == ownerasitem.Name)
                            {
                                for (int z = 0; z < NilMod.NilModGriefWatcher.GWListRangedAmmo.Count; z++)
                                {
                                    if (NilMod.NilModGriefWatcher.GWListRangedAmmo[z] == item.Name)
                                    {
                                        if (item.ContainedItems == null || item.ContainedItems.All(it => it == null))
                                        {
                                            NilMod.NilModGriefWatcher.SendWarning(user.LogName
                                                + " Loaded weapon " + ownerasitem.Name
                                                + " with " + item.Name, warnedclient);
                                        }
                                        else
                                        {
                                            NilMod.NilModGriefWatcher.SendWarning(user.LogName
                                                + " Loaded weapon " + ownerasitem.Name
                                                + " with " + item.Name
                                                + " (" + string.Join(", ", System.Array.FindAll(item.ContainedItems, it => it != null).Select(it => it.Name))
                                                + ")", warnedclient);
                                        }
                                    }
                                }
                            }
                        }

                        //This is a characters or other characters inventory and the item is inside it
                        if (ownerasitem.ParentInventory != null && ownerasitem.ParentInventory is CharacterInventory)
                        {
                            CharacterInventory characterinventory = ownerasitem.ParentInventory as CharacterInventory;

                            //Mask item checks
                            if (!characterinventory.character.IsDead
                            && ((GameMain.Server.ConnectedClients.Find(c => c.Character == characterinventory.character) != null)
                            || characterinventory.character.AIController == null))
                            {
                                //This is a currently worn item
                                if (characterinventory.IsInLimbSlot(ownerasitem, InvSlotType.Face)
                                    || characterinventory.IsInLimbSlot(ownerasitem, InvSlotType.Head)
                                    || characterinventory.IsInLimbSlot(ownerasitem, InvSlotType.Torso)
                                    || characterinventory.IsInLimbSlot(ownerasitem, InvSlotType.Legs))
                                {
                                    for (int y = 0; y < NilMod.NilModGriefWatcher.GWListMaskItems.Count; y++)
                                    {
                                        if (NilMod.NilModGriefWatcher.GWListMaskItems[y] == ownerasitem.Name)
                                        {
                                            for (int z = 0; z < NilMod.NilModGriefWatcher.GWListMaskHazardous.Count; z++)
                                            {
                                                if (Array.FindAll(ownerasitem.ContainedItems, it => it != null).Select(it => it.Name).Contains(NilMod.NilModGriefWatcher.GWListMaskHazardous[z]))
                                                {
                                                    NilMod.NilModGriefWatcher.SendWarning(user.LogName
                                                        + " placed lethal wearable " + ownerasitem.Name
                                                        + " (" + string.Join(", ", Array.FindAll(ownerasitem.ContainedItems, it => it != null).Select(it => it.Name)) + ")"
                                                        + " on " + characterinventory.character.LogName, warnedclient);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                foreach (Character c in Character.CharacterList)
                {
                    if (!c.HasSelectedItem(item)) continue;
                    
                    item.Unequip(c);
                    break;                    
                }

                container.IsActive = true;
                container.OnItemContained(item);
            }

            return wasPut;
        }

        public override void RemoveItem(Item item)
        {
            base.RemoveItem(item);
            container.OnItemRemoved(item);
        }
    }
}
