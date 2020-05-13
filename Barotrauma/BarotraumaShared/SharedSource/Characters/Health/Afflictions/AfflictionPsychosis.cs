using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using Barotrauma.Extensions;
using System.Xml.Linq;
using System;

namespace Barotrauma
{
    partial class AfflictionPsychosis : Affliction
    {

        public AfflictionPsychosis(AfflictionPrefab prefab, float strength) : base(prefab, strength)
        {

        }

        public override void Update(CharacterHealth characterHealth, Limb targetLimb, float deltaTime)
        {
            base.Update(characterHealth, targetLimb, deltaTime);
            UpdateProjSpecific(characterHealth, targetLimb, deltaTime);
        }

        partial void UpdateProjSpecific(CharacterHealth characterHealth, Limb targetLimb, float deltaTime);
    }
}
