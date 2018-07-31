using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma
{
    enum CauseOfDeathType
    {
        Unknown, Pressure, Suffocation, Drowning, Affliction, Disconnected
    }

    class CauseOfDeath
    {
        public CauseOfDeathType Type;
        public AfflictionPrefab Affliction;
        public Character Killer;
        public Entity DamageSource;

        public CauseOfDeath(CauseOfDeathType type, AfflictionPrefab affliction, Character killer, Entity damageSource)
        {
            Type = type;
            Affliction = affliction;
            Killer = killer;
            DamageSource = damageSource;
        }
    }
}
