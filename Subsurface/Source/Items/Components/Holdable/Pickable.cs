using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class Pickable : ItemComponent
    {
        protected Character picker;

        protected LimbSlot allowedSlots;

        public LimbSlot AllowedSlots
        {
            get { return allowedSlots; }
        }

        public Character Picker
        {
            get { return picker; }
        }
        
        public Pickable(Item item, XElement element)
            : base(item, element)
        {
            string slotString = ToolBox.GetAttributeString(element, "slots", "Any");
            string[] slots = slotString.Split(',');
            foreach (string slot in slots)
            {
                allowedSlots = allowedSlots | (LimbSlot)Enum.Parse(typeof(LimbSlot), slot.Trim());
            }

            canBePicked = true;            
        }

        public override bool Pick(Character picker)
        {
            if (picker == null) return false;
            if (picker.Inventory == null) return false;

            if (picker.Inventory.TryPutItem(item, allowedSlots))
            {
                if (!picker.HasSelectedItem(item) && item.body!=null) item.body.Enabled = false;
                this.picker = picker;

                for (int i = item.linkedTo.Count - 1; i >= 0; i--)
                    item.linkedTo[i].RemoveLinked(item);
                item.linkedTo.Clear();

                var connectionPanel = item.GetComponent<ConnectionPanel>();
                if (connectionPanel!=null)
                {
                    foreach (Connection c in connectionPanel.connections)
                    {
                        foreach (Wire w in c.Wires)
                        {
                            if (w == null) continue;

                            w.Item.Drop(picker);
                            w.Item.SetTransform(item.SimPosition, 0.0f);
                        }
                    }
                }

                ApplyStatusEffects(ActionType.OnPicked, 1.0f, picker);

                //foreach (StatusEffect effect in item.Prefab.statusEffects)
                //{
                //    effect.OnPicked(picker, null);
                //}

                return true;
            }

            return false;
        }

        public override void Drop(Character dropper)
        {            
            if (picker == null)
            {
                picker = dropper;

                //foreach (Character c in Character.characterList)
                //{
                //    if (c.Inventory == null) continue;
                //    if (c.Inventory.FindIndex(item) == -1) continue;
                    
                //    picker = c;
                //    break;                    
                //}
            }

            if (picker==null || picker.Inventory == null) return;
            
            if (item.body!= null && !item.body.Enabled)
            {
                Limb rightHand = picker.AnimController.GetLimb(LimbType.RightHand);
                item.SetTransform(rightHand.SimPosition, 0.0f);
                item.body.Enabled = true;
            }
            picker.Inventory.RemoveItem(item);
            picker = null;
        }

    }
}
