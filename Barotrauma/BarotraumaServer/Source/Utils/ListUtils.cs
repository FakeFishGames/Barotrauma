using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public static class ListUtils
    {
        public static T WeightedRandom<T>(ICollection<T> collection, Func<int, int> random, Func<T, int> readSelectedWeight, Action<T, int> writeSelectedWeight, int entryWeight, int selectionWeight) where T : class
        {
            var count = collection.Count;
            if (count <= 0)
            {
                return null;
            }
            var maxCount = entryWeight + collection.Max(readSelectedWeight);
            var totalWeight = collection.Sum(entry => maxCount - readSelectedWeight(entry));
            var selected = random(totalWeight);
            foreach (var entry in collection)
            {
                var weight = readSelectedWeight(entry); 
                selected -= maxCount;
                selected += weight;
                if (selected <= 0)
                {
                    writeSelectedWeight(entry, weight + selectionWeight);
                    return entry;
                }
            }
            return null;
        }
    }
}