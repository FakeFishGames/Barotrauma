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

        public override bool TryPutItem(Item item, int i, bool allowSwapping)
        {
            bool wasPut = base.TryPutItem(item, i, allowSwapping);

            if (wasPut)
            {
                foreach (Character c in Character.CharacterList)
                {
                    if (!c.HasSelectedItem(item)) continue;
                    
                    item.Unequip(c);
                    break;                    
                }
                //item.Container = container.Item;
                container.IsActive = true;
            }
            return wasPut;
        }

    }
}
