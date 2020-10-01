using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Explosion
    {
        private static readonly List<Triplet<Explosion, Vector2, float>> prevExplosions = new List<Triplet<Explosion, Vector2, float>>();

        private readonly Attack attack;
        
        private readonly float force;

        private readonly float cameraShake, cameraShakeRange;

        private readonly Color screenColor;
        private readonly float screenColorRange, screenColorDuration;

        private bool sparks, shockwave, flames, smoke, flash, underwaterBubble;
        private bool applyFireEffects;
        private readonly float flashDuration;
        private readonly float? flashRange;
        private readonly string decal;
        private readonly float decalSize;

        public float EmpStrength { get; set; }

        public Explosion(float range, float force, float damage, float structureDamage, float itemDamage, float empStrength = 0.0f)
        {
            attack = new Attack(damage, 0.0f, 0.0f, structureDamage, itemDamage, range)
            {
                SeverLimbsProbability = 1.0f
            };
            this.force = force;
            this.EmpStrength = empStrength;
            sparks = true;
            shockwave = true;
            smoke = true;
            flames = true;
            underwaterBubble = true;
        }
        
        public Explosion(XElement element, string parentDebugName)
        {
            attack = new Attack(element, parentDebugName + ", Explosion");

            force = element.GetAttributeFloat("force", 0.0f);

            sparks      = element.GetAttributeBool("sparks", true);
            shockwave   = element.GetAttributeBool("shockwave", true);
            flames      = element.GetAttributeBool("flames", true);
            underwaterBubble = element.GetAttributeBool("underwaterbubble", true);
            smoke       = element.GetAttributeBool("smoke", true);

            applyFireEffects = element.GetAttributeBool("applyfireeffects", flames);

            flash           = element.GetAttributeBool("flash", true);
            flashDuration   = element.GetAttributeFloat("flashduration", 0.05f);
            if (element.Attribute("flashrange") != null) { flashRange = element.GetAttributeFloat("flashrange", 100.0f); }

            EmpStrength = element.GetAttributeFloat("empstrength", 0.0f);

            decal       = element.GetAttributeString("decal", "");
            decalSize   = element.GetAttributeFloat(1.0f, "decalSize", "decalsize");

            cameraShake = element.GetAttributeFloat("camerashake", attack.Range * 0.1f);
            cameraShakeRange = element.GetAttributeFloat("camerashakerange", attack.Range);

            screenColorRange = element.GetAttributeFloat("screencolorrange", attack.Range * 0.1f);
            screenColor = element.GetAttributeColor("screencolor", Color.Transparent);
            screenColorDuration = element.GetAttributeFloat("screencolorduration", 0.1f);
        }

        public void DisableParticles()
        {
            sparks = false;
            shockwave = false;
            smoke = false;
            flash = false;
            flames = false;
            underwaterBubble = false;
        }

        public List<Triplet<Explosion, Vector2, float>> GetRecentExplosions(float maxSecondsAgo)
        {
            return prevExplosions.FindAll(e => e.Third >= Timing.TotalTime - maxSecondsAgo);
        }
        
        public void Explode(Vector2 worldPosition, Entity damageSource, Character attacker = null)
        {
            prevExplosions.Add(new Triplet<Explosion, Vector2, float>(this, worldPosition, (float)Timing.TotalTime));
            if (prevExplosions.Count > 100)
            {
                prevExplosions.RemoveAt(0);
            }

            Hull hull = Hull.FindHull(worldPosition);
            ExplodeProjSpecific(worldPosition, hull);

            if (hull != null && !string.IsNullOrWhiteSpace(decal) && decalSize > 0.0f)
            {
                hull.AddDecal(decal, worldPosition, decalSize, isNetworkEvent: false);
            }

            float displayRange = attack.Range;

            Vector2 cameraPos = Character.Controlled != null ? Character.Controlled.WorldPosition : GameMain.GameScreen.Cam.Position;
            float cameraDist = Vector2.Distance(cameraPos, worldPosition) / 2.0f;
            GameMain.GameScreen.Cam.Shake = cameraShake * Math.Max((cameraShakeRange - cameraDist) / cameraShakeRange, 0.0f);
#if CLIENT
            if (screenColor != Color.Transparent)
            {
                Color flashColor = Color.Lerp(Color.Transparent, screenColor, Math.Max((screenColorRange - cameraDist) / screenColorRange, 0.0f));
                Screen.Selected.ColorFade(flashColor, Color.Transparent, screenColorDuration);
            }
#endif

            if (displayRange < 0.1f) { return; }

            if (attack.GetStructureDamage(1.0f) > 0.0f)
            {
                RangedStructureDamage(worldPosition, displayRange, attack.GetStructureDamage(1.0f), attacker);
            }

            if (EmpStrength > 0.0f)
            {
                float displayRangeSqr = displayRange * displayRange;
                foreach (Item item in Item.ItemList)
                {
                    float distSqr = Vector2.DistanceSquared(item.WorldPosition, worldPosition);
                    if (distSqr > displayRangeSqr) continue;
                    
                    float distFactor = 1.0f - (float)Math.Sqrt(distSqr) / displayRange;

                    //damage repairable power-consuming items
                    var powered = item.GetComponent<Powered>();
                    if (powered == null || !powered.VulnerableToEMP) continue;
                    if (item.Repairables.Any())
                    {
                        item.Condition -= item.MaxCondition * EmpStrength * distFactor;
                    }

                    //discharge batteries
                    var powerContainer = item.GetComponent<PowerContainer>();
                    if (powerContainer != null)
                    {
                        powerContainer.Charge -= powerContainer.Capacity * EmpStrength * distFactor;
                    }
                }
            }

            if (MathUtils.NearlyEqual(force, 0.0f) && MathUtils.NearlyEqual(attack.Stun, 0.0f) && MathUtils.NearlyEqual(attack.GetTotalDamage(false), 0.0f))
            {
                return;
            }

            DamageCharacters(worldPosition, attack, force, damageSource, attacker);

            if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient)
            {
                foreach (Item item in Item.ItemList)
                {
                    if (item.Condition <= 0.0f) { continue; }
                    if (Vector2.Distance(item.WorldPosition, worldPosition) > attack.Range * 0.5f) { continue; }
                    if (applyFireEffects && !item.FireProof)
                    {
                        //don't apply OnFire effects if the item is inside a fireproof container
                        //(or if it's inside a container that's inside a fireproof container, etc)
                        Item container = item.Container;
                        bool fireProof = false;
                        while (container != null)
                        {
                            if (container.FireProof)
                            { 
                                fireProof = true; 
                                break; 
                            }
                            container = container.Container;
                        }
                        if (!fireProof)
                        {
                            item.ApplyStatusEffects(ActionType.OnFire, 1.0f);
                            if (item.Condition <= 0.0f && GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                            {
                                GameMain.NetworkMember.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnFire });
                            }
                        }
                    }

                    if (item.Prefab.DamagedByExplosions && !item.Indestructible)
                    {
                        float limbRadius = item.body == null ? 0.0f : item.body.GetMaxExtent();
                        float dist = Vector2.Distance(item.WorldPosition, worldPosition);
                        dist = Math.Max(0.0f, dist - ConvertUnits.ToDisplayUnits(limbRadius));
                        if (dist > attack.Range)
                        {
                            continue;
                        }
                        float distFactor = 1.0f - dist / attack.Range;
                        float damageAmount = attack.GetItemDamage(1.0f) * item.Prefab.ExplosionDamageMultiplier;
                        item.Condition -= damageAmount * distFactor;
                    }
                }
            }
        }

        partial void ExplodeProjSpecific(Vector2 worldPosition, Hull hull);
        
        public static void DamageCharacters(Vector2 worldPosition, Attack attack, float force, Entity damageSource, Character attacker)
        {
            if (attack.Range <= 0.0f) { return; }

            //long range for the broad distance check, because large characters may still be in range even if their collider isn't
            float broadRange = Math.Max(attack.Range * 10.0f, 10000.0f);

            foreach (Character c in Character.CharacterList)
            {
                if (!c.Enabled || 
                    Math.Abs(c.WorldPosition.X - worldPosition.X) > broadRange ||
                    Math.Abs(c.WorldPosition.Y - worldPosition.Y) > broadRange)
                {
                    continue;
                }

                Vector2 explosionPos = worldPosition;
                if (c.Submarine != null) { explosionPos -= c.Submarine.Position; }

                Hull hull = Hull.FindHull(explosionPos, null, false);
                bool underWater = hull == null || explosionPos.Y < hull.Surface;

                explosionPos = ConvertUnits.ToSimUnits(explosionPos);

                Dictionary<Limb, float> distFactors = new Dictionary<Limb, float>();
                Dictionary<Limb, float> damages = new Dictionary<Limb, float>();
                List<Affliction> modifiedAfflictions = new List<Affliction>();
                foreach (Limb limb in c.AnimController.Limbs)
                {
                    if (limb.IsSevered || limb.ignoreCollisions) { continue; }

                    float dist = Vector2.Distance(limb.WorldPosition, worldPosition);
                    
                    //calculate distance from the "outer surface" of the physics body
                    //doesn't take the rotation of the limb into account, but should be accurate enough for this purpose
                    float limbRadius = limb.body.GetMaxExtent();
                    dist = Math.Max(0.0f, dist - ConvertUnits.ToDisplayUnits(limbRadius));

                    if (dist > attack.Range) { continue; }

                    float distFactor = 1.0f - dist / attack.Range;

                    //solid obstacles between the explosion and the limb reduce the effect of the explosion
                    var obstacles = Submarine.PickBodies(limb.SimPosition, explosionPos, collisionCategory: Physics.CollisionItem | Physics.CollisionItemBlocking | Physics.CollisionWall);
                    foreach (var body in obstacles)
                    {
                        if (body.UserData is Item item)
                        {
                            var door = item.GetComponent<Door>();
                            if (door != null && !door.IsBroken) { distFactor *= 0.01f; }
                        }
                        else if (body.UserData is Structure structure)
                        {
                            int sectionIndex = structure.FindSectionIndex(worldPosition, world: true, clamp: true);  
                            if (structure.SectionBodyDisabled(sectionIndex))
                            {
                                continue;
                            }
                            else if (structure.SectionIsLeaking(sectionIndex))
                            {
                                distFactor *= 0.1f;
                            }
                            else
                            {
                                distFactor *= 0.01f;
                            }
                        }
                        else
                        {
                            distFactor *= 0.1f;
                        }
                    }
                    if (distFactor <= 0.05f) { continue; }

                    distFactors.Add(limb, distFactor);

                    modifiedAfflictions.Clear();
                    foreach (Affliction affliction in attack.Afflictions.Keys)
                    {
                        //previously the damage would be divided by the number of limbs (the intention was to prevent characters with more limbs taking more damage from explosions)
                        //that didn't work well on large characters like molochs and endworms: the explosions tend to only damage one or two of their limbs, and since the characters
                        //have lots of limbs, they tended to only take a fraction of the damage they should

                        //now we just divide by 10, which keeps the damage to normal-sized characters roughly the same as before and fixes the large characters
                        modifiedAfflictions.Add(affliction.CreateMultiplied(distFactor / 10));
                    }
                    c.LastDamageSource = damageSource;
                    if (attacker == null)
                    {
                        if (damageSource is Item item)
                        {
                            attacker = item.GetComponent<Projectile>()?.User;
                            if (attacker == null)
                            {
                                attacker = item.GetComponent<MeleeWeapon>()?.User;
                            }
                        }
                    }

                    //use a position slightly from the limb's position towards the explosion
                    //ensures that the attack hits the correct limb and that the direction of the hit can be determined correctly in the AddDamage methods
                    Vector2 dir = worldPosition - limb.WorldPosition;
                    Vector2 hitPos = limb.WorldPosition + (dir.LengthSquared() <= 0.001f ? Rand.Vector(1.0f) : Vector2.Normalize(dir)) * 0.01f;
                    AttackResult attackResult = c.AddDamage(hitPos, modifiedAfflictions, attack.Stun * distFactor, false, attacker: attacker);
                    damages.Add(limb, attackResult.Damage);
                    
                    if (attack.StatusEffects != null && attack.StatusEffects.Any())
                    {
                        attack.SetUser(attacker);
                        var statusEffectTargets = new List<ISerializableEntity>() { c, limb };
                        foreach (StatusEffect statusEffect in attack.StatusEffects)
                        {
                            statusEffect.Apply(ActionType.OnUse, 1.0f, damageSource, statusEffectTargets);
                            statusEffect.Apply(ActionType.Always, 1.0f, damageSource, statusEffectTargets);
                            statusEffect.Apply(underWater ? ActionType.InWater : ActionType.NotInWater, 1.0f, damageSource, statusEffectTargets);
                        }
                    }
                    
                    if (limb.WorldPosition != worldPosition && !MathUtils.NearlyEqual(force, 0.0f))
                    {
                        Vector2 limbDiff = Vector2.Normalize(limb.WorldPosition - worldPosition);
                        if (!MathUtils.IsValid(limbDiff)) { limbDiff = Rand.Vector(1.0f); }
                        Vector2 impulse = limbDiff * distFactor * force;
                        Vector2 impulsePoint = limb.SimPosition - limbDiff * limbRadius;
                        limb.body.ApplyLinearImpulse(impulse, impulsePoint, maxVelocity: NetConfig.MaxPhysicsBodyVelocity * 0.2f);
                    }
                }

                if (c == Character.Controlled && !c.IsDead)
                {
                    Limb head = c.AnimController.GetLimb(LimbType.Head);
                    if (damages.TryGetValue(head, out float headDamage) && headDamage > 0.0f && distFactors.TryGetValue(head, out float headFactor))
                    {
                        PlayTinnitusProjSpecific(headFactor);
                    }
                }

                //sever joints 
                if (attack.SeverLimbsProbability > 0.0f)
                {
                    foreach (Limb limb in c.AnimController.Limbs)
                    {
                        if (limb.character.Removed || limb.Removed) { continue; }
                        if (limb.IsSevered) { continue; }
                        if (!c.IsDead && !limb.CanBeSeveredAlive) { continue; }
                        if (distFactors.TryGetValue(limb, out float distFactor))
                        {
                            if (damages.TryGetValue(limb, out float damage))
                            {
                                c.TrySeverLimbJoints(limb, attack.SeverLimbsProbability * distFactor, damage, allowBeheading: true);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns a dictionary where the keys are the structures that took damage and the values are the amount of damage taken
        /// </summary>
        public static Dictionary<Structure, float> RangedStructureDamage(Vector2 worldPosition, float worldRange, float damage, Character attacker = null)
        {
            List<Structure> structureList = new List<Structure>();            
            float dist = 600.0f;
            foreach (MapEntity entity in MapEntity.mapEntityList)
            {
                if (!(entity is Structure structure)) { continue; }

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
                    
                    structure.AddDamage(i, damage * distFactor, attacker);

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

        static partial void PlayTinnitusProjSpecific(float volume);
    }
}
