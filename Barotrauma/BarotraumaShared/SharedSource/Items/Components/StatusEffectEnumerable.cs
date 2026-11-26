using System.Collections;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    /// <summary>
    /// Zero-allocation enumerable for iterating status effects of a given <see cref="ActionType"/>.
    /// Automatically resolves status effect overrides via <see cref="Item.GetActiveStatusEffect"/>.
    /// When used in a foreach loop, no heap allocation occurs. LINQ operations (e.g. Concat) allocate via the explicit <see cref="IEnumerable{T}"/> implementation.
    /// </summary>
    public readonly struct StatusEffectEnumerable(List<StatusEffect> list, Item item) : IEnumerable<StatusEffect>
    {
        public Enumerator GetEnumerator() => new Enumerator(list, item);

        IEnumerator<StatusEffect> IEnumerable<StatusEffect>.GetEnumerator()
        {
            if (list == null) { yield break; }
            for (int i = 0; i < list.Count; i++)
            {
                yield return item.GetActiveStatusEffect(list[i]);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<StatusEffect>)this).GetEnumerator();

        public struct Enumerator(List<StatusEffect> list, Item item)
        {
            private int index = -1;

            public StatusEffect Current => item.GetActiveStatusEffect(list[index]);

            public bool MoveNext()
            {
                if (list == null) { return false; }
                index++;
                return index < list.Count;
            }
        }
    }
}
