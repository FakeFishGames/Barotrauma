using Microsoft.Xna.Framework;
using Barotrauma.Lights;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.Networking;
using FarseerPhysics;

namespace Barotrauma
{
    class Explosion
    {
        private Attack attack;
        
        private float force;
        
        public float CameraShake;

        private bool sparks, shockwave, flames, smoke;

        public Explosion(float range, float force, float damage, float structureDamage)
        {
            attack = new Attack(damage, structureDamage, 0.0f, range);
            this.force = force;
            sparks = true;
            shockwave = true;
            flames = true;
        }

        public Explosion(XElement element)
        {
            attack = new Attack(element);

            force = ToolBox.GetAttributeFloat(element, "force", 0.0f);

            sparks      = ToolBox.GetAttributeBool(element, "sparks", true);
            shockwave   = ToolBox.GetAttributeBool(element, "shockwave", true);
            flames      = ToolBox.GetAttributeBool(element, "flames", true);
            smoke       = ToolBox.GetAttributeBool(element, "smoke", true);

            CameraShake = ToolBox.GetAttributeFloat(element, "camerashake", attack.Range*0.1f);
        }
        
        public void Explode(Vector2 worldPosition)
        {
            Hull hull = Hull.FindHull(worldPosition);

            if (shockwave)
            {
                GameMain.ParticleManager.CreateParticle("shockwave", worldPosition,
                    Vector2.Zero, 0.0f, hull);
            }

            for (int i = 0; i < attack.Range * 0.1f; i++)
            {
                Vector2 bubblePos = Rand.Vector(attack.Range * 0.5f);
                GameMain.ParticleManager.CreateParticle("bubbles", worldPosition+bubblePos,
                    bubblePos, 0.0f, hull);

                if (sparks)
                {
                    GameMain.ParticleManager.CreateParticle("spark", worldPosition,
                        Rand.Vector(Rand.Range(500.0f, 800.0f)), 0.0f, hull);
                }
                if (flames)
                {
                    GameMain.ParticleManager.CreateParticle("explosionfire", ClampParticlePos(worldPosition + Rand.Vector(50f), hull),
                        Rand.Vector(Rand.Range(50.0f, 100.0f)), 0.0f, hull);
                }
                if (smoke)
                {
                    GameMain.ParticleManager.CreateParticle("smoke", ClampParticlePos(worldPosition + Rand.Vector(50f), hull),
                        Rand.Vector(Rand.Range(1.0f, 10.0f)), 0.0f, hull);
                }
            }

            float displayRange = attack.Range;
            if (displayRange < 0.1f) return;

            var light = new LightSource(worldPosition, displayRange, Color.LightYellow, null);
            CoroutineManager.StartCoroutine(DimLight(light));

            float cameraDist = Vector2.Distance(GameMain.GameScreen.Cam.Position, worldPosition)/2.0f;
            GameMain.GameScreen.Cam.Shake = CameraShake * Math.Max((displayRange - cameraDist) / displayRange, 0.0f);
            
            if (attack.GetStructureDamage(1.0f) > 0.0f)
            {
                RangedStructureDamage(worldPosition, displayRange, attack.GetStructureDamage(1.0f));
            }

            if (force == 0.0f && attack.Stun == 0.0f && attack.GetDamage(1.0f) == 0.0f) return;

            ApplyExplosionForces(worldPosition, attack.Range, force, attack.GetDamage(1.0f), attack.Stun);

            if (flames && GameMain.Client == null)
            {
                foreach (Item item in Item.ItemList)
                {
                    if (item.CurrentHull != hull || item.FireProof || item.Condition <= 0.0f) continue;

                    if (Vector2.Distance(item.WorldPosition, worldPosition) > attack.Range * 0.1f) continue;

                    item.ApplyStatusEffects(ActionType.OnFire, 1.0f);

                    if (item.Condition <= 0.0f && GameMain.Server != null)
                    {
                        GameMain.Server.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnFire });
                    }
                }
            }

        }

        private Vector2 ClampParticlePos(Vector2 particlePos, Hull hull)
        {
            if (hull == null) return particlePos;

            return new Vector2(
                MathHelper.Clamp(particlePos.X, hull.WorldRect.X, hull.WorldRect.Right),
                MathHelper.Clamp(particlePos.Y, hull.WorldRect.Y - hull.WorldRect.Height, hull.WorldRect.Y));
        }


        private IEnumerable<object> DimLight(LightSource light)
        {
            float currBrightness = 1.0f;
            float startRange = light.Range;

            while (light.Color.A > 0.0f)
            {
                light.Color = new Color(light.Color.R, light.Color.G, light.Color.B, currBrightness);
                light.Range = startRange * currBrightness;

                currBrightness -= CoroutineManager.DeltaTime * 20.0f;

                yield return CoroutineStatus.Running;
            }

            light.Remove();

            yield return CoroutineStatus.Success;
        }

        public static void ApplyExplosionForces(Vector2 worldPosition, float range, float force, float damage = 0.0f, float stun = 0.0f)
        {
            if (range <= 0.0f) return;

            foreach (Character c in Character.CharacterList)
            {
                Vector2 explosionPos = worldPosition;
                if (c.Submarine != null) explosionPos -= c.Submarine.Position;

                explosionPos = ConvertUnits.ToSimUnits(explosionPos);

                foreach (Limb limb in c.AnimController.Limbs)
                {
                    float dist = Vector2.Distance(limb.WorldPosition, worldPosition);
                    
                    //calculate distance from the "outer surface" of the physics body
                    //doesn't take the rotation of the limb into account, but should be accurate enough for this purpose
                    float limbRadius = Math.Max(Math.Max(limb.body.width * 0.5f, limb.body.height * 0.5f), limb.body.radius);
                    dist = Math.Max(0.0f, dist - FarseerPhysics.ConvertUnits.ToDisplayUnits(limbRadius));

                    if (dist > range) continue;

                    float distFactor = 1.0f - dist / range;

                    //solid obstacles between the explosion and the limb reduce the effect of the explosion by 90%
                    if (Submarine.CheckVisibility(limb.SimPosition, explosionPos) != null) distFactor *= 0.1f;

                    c.AddDamage(limb.WorldPosition, DamageType.None,
                        damage / c.AnimController.Limbs.Length * distFactor, 0.0f, stun * distFactor, false);

                    if (limb.WorldPosition == worldPosition) continue;

                    if (force > 0.0f)
                    {
                        limb.body.ApplyLinearImpulse(Vector2.Normalize(limb.WorldPosition - worldPosition) * distFactor * force);
                    }
                }
            }
        }

        /// <summary>
        /// Returns a dictionary where the keys are the structures that took damage and the values are the amount of damage taken
        /// </summary>
        public static Dictionary<Structure,float> RangedStructureDamage(Vector2 worldPosition, float worldRange, float damage)
        {
            List<Structure> structureList = new List<Structure>();            
            float dist = 600.0f;
            foreach (MapEntity entity in MapEntity.mapEntityList)
            {
                Structure structure = entity as Structure;
                if (structure == null) continue;

                if (structure.HasBody &&
                    !structure.IsPlatform &&
                    Vector2.Distance(structure.WorldPosition, worldPosition) < dist * 3.0f)
                {
                    structureList.Add(structure);
                }
            }

            Dictionary<Structure, float> damagedStructures = new Dictionary<Structure, float>();
            foreach (Structure structure in structureList)
            {
                for (int i = 0; i < structure.SectionCount; i++)
                {
                    float distFactor = 1.0f - (Vector2.Distance(structure.SectionPosition(i, true), worldPosition) / worldRange);
                    if (distFactor <= 0.0f) continue;
                    
                    structure.AddDamage(i, damage * distFactor);

                    if (damagedStructures.ContainsKey(structure))
                    { 
                        damagedStructures[structure] += damage * distFactor;
                    }
                    else
                    {
                        damagedStructures.Add(structure, damage * distFactor);
                    }
                }                
            }

            return damagedStructures;
        }
    }
}
