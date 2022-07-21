using System.Collections.Generic;

namespace Barotrauma
{
    public class CreatureMetrics
    {
        public readonly HashSet<Identifier> RecentlyEncountered = new HashSet<Identifier>();
        public readonly HashSet<Identifier> Encountered = new HashSet<Identifier>();
        public readonly HashSet<Identifier> Killed = new HashSet<Identifier>();

        public readonly static CreatureMetrics Instance = new CreatureMetrics();
    }
}