using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma
{
    class AfflictionSpaceHerpes : Affliction
    {
        private float invertControlsCooldown = 60.0f;
        private float stunCoolDown = 60.0f;
        private float invertControlsTimer;

        private float invertControlsToggleTimer;

        public AfflictionSpaceHerpes(AfflictionPrefab prefab, float strength) : base(prefab, strength)
        {
        }

        public override void Update(CharacterHealth characterHealth, Limb targetLimb, float deltaTime)
        {
            base.Update(characterHealth, targetLimb, deltaTime);

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            invertControlsCooldown -= deltaTime;
            if (invertControlsCooldown <= 0.0f)
            {
                //invert controls every 126-234 seconds when strength is close to 0
                //every 56-104 seconds when strength is close to 100
                invertControlsCooldown = (180.0f - Strength) * Rand.Range(0.7f, 1.3f);
                invertControlsTimer = MathHelper.Lerp(10.0f, 60.0f, Strength / 100.0f) * Rand.Range(0.7f, 1.3f);
            }
            else if (invertControlsTimer > 0.0f)
            {
                //randomly toggle inverted controls on/off every 5 seconds
                invertControlsToggleTimer -= deltaTime;
                if (invertControlsToggleTimer <= 0.0f)
                {
                    invertControlsToggleTimer = 5.0f;
                    if (Rand.Range(0.0f, 1.0f) < 0.5f)
                    {
                        characterHealth.ReduceAffliction(null, "invertcontrols", 100);
                    }
                    else
                    {
                        var invertControlsAffliction = AfflictionPrefab.List.Find(ap => ap.Identifier == "invertcontrols");
                        characterHealth.ApplyAffliction(null, new Affliction(invertControlsAffliction, 5.0f));
                    }
                }

                invertControlsTimer -= deltaTime;
            }


            if (Strength > 50.0f)
            {
                stunCoolDown -= deltaTime;
                if (stunCoolDown <= 0.0f)
                {
                    //stun every 126-234 seconds when strength is close to 0
                    //stun 56-104 seconds when strength is close to 100
                    stunCoolDown = (180.0f - Strength) * Rand.Range(0.7f, 1.3f);
                    float stunDuration = MathHelper.Lerp(3.0f, 10.0f, Strength / 100.0f) * Rand.Range(0.7f, 1.3f);
                    characterHealth.Character.SetStun(stunDuration);
                }
            }
        }
    }
}
