using Microsoft.Xna.Framework;
using Subsurface.Items.Components;

namespace Subsurface
{
    class ItemInventory : Inventory
    {
        ItemContainer container;

        public ItemInventory(ItemContainer container, int capacity, Vector2? centerPos = null, int slotsPerRow = 5)
            : base(capacity, centerPos, slotsPerRow)
        {
            this.container = container;
        }

        public override int FindAllowedSlot(Item item)
        {
            for (int i = 0; i < capacity; i++)
            {
                //item is already in the inventory!
                if (items[i] == item) return -1;
            }

            if (!container.CanBeContained(item)) return -1;

            for (int i = 0; i < capacity; i++)
            {
                if (items[i] == null) return i;
            }

            return -1;
        }

        public override bool CanBePut(Item item, int i)
        {
            if (i < 0 || i >= items.Length) return false;
            return (item!=null && items[i]==null && container.CanBeContained(item));
        }

        public override bool TryPutItem(Item item, int i, bool createNetworkEvent)
        {
            bool wasPut = base.TryPutItem(item, i, createNetworkEvent);

            if (wasPut)
            {
                foreach (Character c in Character.CharacterList)
                {
                    if (!c.HasSelectedItem(item)) continue;
                    
                    item.Unequip(c);
                    break;                    
                }
                item.container = container.Item;
                container.IsActive = true;
            }
            return wasPut;
        }

    }
}
