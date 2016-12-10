using Microsoft.Xna.Framework;
using Barotrauma.Items.Components;

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

        protected override void DropItem(Item item)
        {
            item.Drop();
            if (item.body != null) item.body.Enabled = true;
            item.SetTransform(container.Item.SimPosition, 0.0f);
        }

        public override int FindAllowedSlot(Item item)
        {
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
            if (i < 0 || i >= Items.Length) return false;
            return (item!=null && Items[i]==null && container.CanBeContained(item));
        }


        public override bool TryPutItem(Item item, System.Collections.Generic.List<InvSlotType> allowedSlots = null, bool createNetworkEvent = true)
        {
            bool wasPut = base.TryPutItem(item, allowedSlots, createNetworkEvent);

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

        public override bool TryPutItem(Item item, int i, bool allowSwapping, bool createNetworkEvent = true)
        {
            bool wasPut = base.TryPutItem(item, i, allowSwapping, createNetworkEvent);

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

        public override void RemoveItem(Item item)
        {
            base.RemoveItem(item);
            container.OnItemRemoved(item);
        }
    }
}
