using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Explosion
    {
        private Attack attack;
        
        private float force;
        
        public float CameraShake;

        private bool sparks, shockwave, flames, smoke, flash;

        private string decal;
        private float decalSize;

        public Explosion(float range, float force, float damage, float structureDamage)
        {
            attack = new Attack(damage, structureDamage, 0.0f, range);
            attack.SeverLimbsProbability = 1.0f;
            this.force = force;
            sparks = true;
            shockwave = true;
            flames = true;
        }

        public Explosion(XElement element)
        {
            attack = new Attack(element);

            force = element.GetAttributeFloat("force", 0.0f);

            sparks      = element.GetAttributeBool("sparks", true);
            shockwave   = element.GetAttributeBool("shockwave", true);
            flames      = element.GetAttributeBool("flames", true);
            smoke       = element.GetAttributeBool("smoke", true);
            flash       = element.GetAttributeBool("flash", true);

            decal       = element.GetAttributeString("decal", "");
            decalSize   = element.GetAttributeFloat("decalSize", 1.0f);

            CameraShake = element.GetAttributeFloat("camerashake", attack.Range * 0.1f);
        }
        
        public void Explode(Vector2 worldPosition)
        {
            Hull hull = Hull.FindHull(worldPosition);

            ExplodeProjSpecific(worldPosition, hull);

            float displayRange = attack.Range;
            if (displayRange < 0.1f) return;

            float cameraDist = Vector2.Distance(GameMain.GameScreen.Cam.Position, worldPosition)/2.0f;
            GameMain.GameScreen.Cam.Shake = CameraShake * Math.Max((displayRange - cameraDist) / displayRange, 0.0f);
            
            if (attack.GetStructureDamage(1.0f) > 0.0f)
            {
                RangedStructureDamage(worldPosition, displayRange, attack.GetStructureDamage(1.0f));
            }

            if (force == 0.0f && attack.Stun == 0.0f && attack.GetDamage(1.0f) == 0.0f) return;

            ApplyExplosionForces(worldPosition, attack, force);

            if (flames && GameMain.Client == null)
            {
                foreach (Item item in Item.ItemList)
                {
                    if (item.CurrentHull != hull || item.FireProof || item.Condition <= 0.0f) continue;

                    //don't apply OnFire effects if the item is inside a fireproof container
                    //(or if it's inside a container that's inside a fireproof container, etc)
                    Item container = item.Container;
                    while (container != null)
                    {
                        if (container.FireProof) return;
                        container = container.Container;
                    }

                    if (Vector2.Distance(item.WorldPosition, worldPosition) > attack.Range * 0.1f) continue;

                    item.ApplyStatusEffects(ActionType.OnFire, 1.0f);

                    if (item.Condition <= 0.0f && GameMain.Server != null)
                    {
                        GameMain.Server.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnFire });
                    }
                }
            }

        }

        partial void ExplodeProjSpecific(Vector2 worldPosition, Hull hull);

        private Vector2 ClampParticlePos(Vector2 particlePos, Hull hull)
        {
            if (hull == null) return particlePos;

            return new Vector2(
                MathHelper.Clamp(particlePos.X, hull.WorldRect.X, hull.WorldRect.Right),
                MathHelper.Clamp(particlePos.Y, hull.WorldRect.Y - hull.WorldRect.Height, hull.WorldRect.Y));
        }

        public static void ApplyExplosionForces(Vector2 worldPosition, Attack attack, float force)
        {
            if (attack.Range <= 0.0f) return;

            foreach (Character c in Character.CharacterList)
            {
                Vector2 explosionPos = worldPosition;
                if (c.Submarine != null) explosionPos -= c.Submarine.Position;

                explosionPos = ConvertUnits.ToSimUnits(explosionPos);

                Dictionary<Limb, float> distFactors = new Dictionary<Limb, float>();
                foreach (Limb limb in c.AnimController.Limbs)
                {
                    float dist = Vector2.Distance(limb.WorldPosition, worldPosition);
                    
                    //calculate distance from the "outer surface" of the physics body
                    //doesn't take the rotation of the limb into account, but should be accurate enough for this purpose
                    float limbRadius = Math.Max(Math.Max(limb.body.width * 0.5f, limb.body.height * 0.5f), limb.body.radius);
                    dist = Math.Max(0.0f, dist - FarseerPhysics.ConvertUnits.ToDisplayUnits(limbRadius));
                    
                    if (dist > attack.Range) continue;

                    float distFactor = 1.0f - dist / attack.Range;

                    //solid obstacles between the explosion and the limb reduce the effect of the explosion by 90%
                    if (Submarine.CheckVisibility(limb.SimPosition, explosionPos) != null) distFactor *= 0.1f;
                    
                    distFactors.Add(limb, distFactor);

                    c.AddDamage(limb.WorldPosition, DamageType.None,
                        attack.GetDamage(1.0f) / c.AnimController.Limbs.Length * distFactor, 
                        attack.GetBleedingDamage(1.0f) / c.AnimController.Limbs.Length * distFactor, 
                        attack.Stun * distFactor, 
                        false);

                    if (limb.WorldPosition != worldPosition && force > 0.0f)
                    {
                        Vector2 limbDiff = Vector2.Normalize(limb.WorldPosition - worldPosition);
                        Vector2 impulsePoint = limb.SimPosition - limbDiff * limbRadius;
                        limb.body.ApplyLinearImpulse(limbDiff * distFactor * force, impulsePoint);
                    }
                }     
                
                //sever joints 
                if (c.IsDead && attack.SeverLimbsProbability > 0.0f)
                {
                    foreach (Limb limb in c.AnimController.Limbs)
                    {
                        if (!distFactors.ContainsKey(limb)) continue;

                        foreach (LimbJoint joint in c.AnimController.LimbJoints)
                        {
                            if (joint.IsSevered || (joint.LimbA != limb && joint.LimbB != limb)) continue;

                            if (Rand.Range(0.0f, 1.0f) < attack.SeverLimbsProbability * distFactors[limb])
                            {
                                c.AnimController.SeverLimbJoint(joint);
                            }
                        }
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
