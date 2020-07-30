using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class RemoveItemAction : EventAction
    {
        [Serialize("", true)]
        public string TargetTag { get; set; }

        [Serialize("", true)]
        public string ItemIdentifier { get; set; }

        [Serialize(1, true)]
        public int Amount { get; set; }

        public RemoveItemAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) { }

        private bool isFinished = false;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset()
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            var targets = ParentEvent.GetTargets(TargetTag)
                .Where(t => t is Character chr && chr.Inventory != null)
                .Select(t => t as Character).ToList();
            if (targets.Count <= 0) { return; }

            int count = Amount;
            while (count > 0 && targets.Count > 0)
            {
                var items = targets[0].Inventory.Items;
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i] != null && items[i].Prefab.Identifier.Equals(ItemIdentifier, StringComparison.InvariantCultureIgnoreCase))
                    {
                        Entity.Spawner.AddToRemoveQueue(items[i]);
                        count--;
                        if (count <= 0) { break; }
                    }
                }
                targets.RemoveAt(0);
            }
            isFinished = true;
        }
    }
}
