using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class MalfunctionEvent : Event
    {
        private Identifier[] targetItemIdentifiers;

        private List<Item> targetItems;

        private int minItemAmount, maxItemAmount;

        private float decreaseConditionAmount;

        private float duration;

        private float timer;

        public override string ToString()
        {
            return "MalfunctionEvent (" + string.Join(", ", targetItemIdentifiers) + ")";
        }
        
        public MalfunctionEvent(EventPrefab prefab)
            : base(prefab)
        {
            targetItems = new List<Item>();

            minItemAmount = prefab.ConfigElement.GetAttributeInt("minitemamount", 1);
            maxItemAmount = prefab.ConfigElement.GetAttributeInt("maxitemamount", minItemAmount);

            decreaseConditionAmount = prefab.ConfigElement.GetAttributeFloat("decreaseconditionamount", 0.0f);
            duration = prefab.ConfigElement.GetAttributeFloat("duration", 0.0f);

            targetItemIdentifiers = prefab.ConfigElement.GetAttributeIdentifierArray("itemidentifiers", Array.Empty<Identifier>());
        }

        public override void Init(EventSet parentSet)
        {
            base.Init(parentSet);
            var matchingItems = Item.ItemList.FindAll(i => i.Condition > 0.0f && targetItemIdentifiers.Contains(i.Prefab.Identifier));
            int itemAmount = Rand.Range(minItemAmount, maxItemAmount, Rand.RandSync.ServerAndClient);
            for (int i = 0; i < itemAmount; i++)
            {
                if (matchingItems.Count == 0) break;
                targetItems.Add(matchingItems[Rand.Int(matchingItems.Count, Rand.RandSync.ServerAndClient)]);
            }
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) return;
            if (targetItems.Count == 0 || timer >= duration)
            {
                Finish();
                return;
            }

            targetItems.RemoveAll(i => i.Removed || i.Condition <= 0.0f);
            foreach (Item item in targetItems)
            {
                if (duration <= 0.0f)
                {
                    item.Condition = 0.0f;
                }
                else
                {
                    item.Condition -= decreaseConditionAmount / duration * deltaTime;
                }
            }

            timer += deltaTime;
        }
    }
}
