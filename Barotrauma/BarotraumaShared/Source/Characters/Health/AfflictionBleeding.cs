using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma
{
    class AfflictionBleeding : Affliction
    {
        public AfflictionBleeding(AfflictionPrefab prefab, float strength) : 
            base(prefab, strength)
        {
        }

        public override void Update(CharacterHealth characterHealth, float deltaTime)
        {
            base.Update(characterHealth, deltaTime);
            characterHealth.BloodlossAmount += Strength * (1.0f / 60.0f) * deltaTime;
        }
    }
}
