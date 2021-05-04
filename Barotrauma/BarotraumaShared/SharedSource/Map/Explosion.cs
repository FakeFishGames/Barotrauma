using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Barotrauma.MapCreatures.Behavior;

namespace Barotrauma
{
    partial class Explosion
    {
        private static readonly List<Triplet<Explosion, Vector2, float>> prevExplosions = new List<Triplet<Explosion, Vector2, float>>();

        public readonly Attack Attack;
        
        private readonly float force;

        private readonly float cameraShake, cameraShakeRange;

        private readonly Color screenColor;
        private readonly float screenColorRange, screenColorDuration;

        private bool sparks, shockwave, flames, smoke, flash, underwaterBubble;
        private bool playTinnitus;
        private bool applyFireEffects;
        private string[] ignoreFireEffectsForTags;
        private bool ignoreCover;
        private bool onlyInside;
        private bool onlyOutside;
        private readonly float flashDuration;
        private readonly float? flashRange;
        private readonly string decal;
        private readonly float decalSize;

        public float EmpStrength { get; set; }
        
        public float BallastFloraDamage { get; set; }

        public Explosion(float range, float force, float damage, float structureDamage, float itemDamage, float empStrength = 0.0f, float ballastFloraStrength = 0.0f)
        {
            Attack = new Attack(damage, 0.0f, 0.0f, structureDamage, itemDamage, range)
            {
                SeverLimbsProbability = 1.0f
            };
            this.force = force;
            this.EmpStrength = empStrength;
            BallastFloraDamage = ballastFloraStrength;
            sparks = true;
            shockwave = true;
            smoke = true;
            flames = true;
            underwaterBubble = true;
            ignoreFireEffectsForTags = new string[0];
        }
        
        public Explosion(XElement element, string parentDebugName)
        {
            Attack = new Attack(element, parentDebugName + ", Explosion");

            force = element.GetAttributeFloat("force", 0.0f);

            sparks      = element.GetAttributeBool("sparks", true);
            shockwave   = element.GetAttributeBool("shockwave", true);
            flames      = element.GetAttributeBool("flames", true);
            underwaterBubble = element.GetAttributeBool("underwaterbubble", true);
            smoke       = element.GetAttributeBool("smoke", true);

            playTinnitus = element.GetAttributeBool("playtinnitus", true);

            applyFireEffects = element.GetAttributeBool("applyfireeffects", flames);
            ignoreFireEffectsForTags = element.GetAttributeStringArray("ignorefireeffectsfortags", new string[0], convertToLowerInvariant: true);

            ignoreCover = element.GetAttributeBool("ignorecover", false);
            onlyInside = element.GetAttributeBool("onlyinside", false);
            onlyOutside = element.GetAttributeBool("onlyoutside", false);

            flash           = element.GetAttributeBool("flash", true);
            flashDuration   = element.GetAttributeFloat("flashduration", 0.05f);
            if (element.Attribute("flashrange") != null) { flashRange = element.GetAttributeFloat("flashrange", 100.0f); }

            EmpStrength = element.GetAttributeFloat("empstrength", 0.0f);
            BallastFloraDamage = element.GetAttributeFloat("ballastfloradamage", 0.0f);

            decal       = element.GetAttributeString("decal", "");
            decalSize   = element.GetAttributeFloat(1.0f, "decalSize", "decalsize");

            cameraShake = element.GetAttributeFloat("camerashake", Attack.Range * 0.1f);
            cameraShakeRange = element.GetAttributeFloat("camerashakerange", Attack.Range);

            screenColorRange = element.GetAttributeFloat("screencolorrange", Attack.Range * 0.1f);
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

            float displayRange = Attack.Range;

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

            if (Attack.GetStructureDamage(1.0f) > 0.0f || Attack.GetLevelWallDamage(1.0f) > 0.0f)
            {
                RangedStructureDamage(worldPosition, displayRange, Attack.GetStructureDamage(1.0f), Attack.GetLevelWallDamage(1.0f), attacker);
            }

            if (BallastFloraDamage > 0.0f)
            {
                RangedBallastFloraDamage(worldPosition, displayRange, BallastFloraDamage, attacker);
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

            if (MathUtils.NearlyEqual(force, 0.0f) && MathUtils.NearlyEqual(Attack.Stun, 0.0f) && MathUtils.NearlyEqual(Attack.GetTotalDamage(false), 0.0f))
            {
                return;
            }

            DamageCharacters(worldPosition, Attack, force, damageSource, attacker);

            if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient)
            {
                foreach (Item item in Item.ItemList)
                {
                    if (item.Condition <= 0.0f) { continue; }
                    float dist = Vector2.Distance(item.WorldPosition, worldPosition);
                    float itemRadius = item.body == null ? 0.0f : item.body.GetMaxExtent();
                    dist = Math.Max(0.0f, dist - ConvertUnits.ToDisplayUnits(itemRadius));
                    if (dist > Attack.Range) { continue; }

                    if (dist < Attack.Range * 0.5f && applyFireEffects && !item.FireProof && ignoreFireEffectsForTags.None(t => item.HasTag(t)))
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
                        float distFactor = 1.0f - dist / Attack.Range;
                        float damageAmount = Attack.GetItemDamage(1.0f) * item.Prefab.ExplosionDamageMultiplier;

                        Vector2 explosionPos = worldPosition;
                        if (item.Submarine != null) { explosionPos -= item.Submarine.Position; }

                        damageAmount *= GetObstacleDamageMultiplier(ConvertUnits.ToSimUnits(explosionPos), worldPosition, item.SimPosition);
                        item.Condition -= damageAmount * distFactor;
                    }
                }
            }
        }

        partial void ExplodeProjSpecific(Vector2 worldPosition, Hull hull);
        
        private void DamageCharacters(Vector2 worldPosition, Attack attack, float force, Entity damageSource, Character attacker)
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
                if (onlyInside && c.Submarine == null) { continue; }
                else if (onlyOutside && c.Submarine != null) { continue; }

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
                    if (limb.IsSevered || limb.IgnoreCollisions || !limb.body.Enabled) { continue; }

                    float dist = Vector2.Distance(limb.WorldPosition, worldPosition);
                    
                    //calculate distance from the "outer surface" of the physics body
                    //doesn't take the rotation of the limb into account, but should be accurate enough for this purpose
                    float limbRadius = limb.body.GetMaxExtent();
                    dist = Math.Max(0.0f, dist - ConvertUnits.ToDisplayUnits(limbRadius));

                    if (dist > attack.Range) { continue; }

                    float distFactor = 1.0f - dist / attack.Range;

                    //solid obstacles between the explosion and the limb reduce the effect of the explosion
                    if (!ignoreCover)
                    {
                        distFactor *= GetObstacleDamageMultiplier(explosionPos, worldPosition, limb.SimPosition);
                    }
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

                if (c == Character.Controlled && !c.IsDead && playTinnitus)
                {
                    Limb head = c.AnimController.GetLimb(LimbType.Head);
                    if (head != null && damages.TryGetValue(head, out float headDamage) && headDamage > 0.0f && distFactors.TryGetValue(head, out float headFactor))
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
        public static Dictionary<Structure, float> RangedStructureDamage(Vector2 worldPosition, float worldRange, float damage, float levelWallDamage, Character attacker = null)
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

            if (Level.Loaded != null && !MathUtils.NearlyEqual(levelWallDamage, 0.0f))
            {
                for (int i = Level.Loaded.ExtraWalls.Count - 1; i >= 0; i--)
                {
                    if (!(Level.Loaded.ExtraWalls[i] is DestructibleLevelWall destructibleWall)) { continue; }
                    foreach (var cell in destructibleWall.Cells)
                    {
                        if (cell.IsPointInside(worldPosition))
                        {
                            destructibleWall.AddDamage(levelWallDamage, worldPosition);
                            continue;
                        }
                        foreach (var edge in cell.Edges)
                        {
                            if (MathUtils.LineSegmentToPointDistanceSquared((edge.Point1 + cell.Translation).ToPoint(), (edge.Point2 + cell.Translation).ToPoint(), worldPosition.ToPoint()) < worldRange * worldRange)
                            {
                                destructibleWall.AddDamage(levelWallDamage, worldPosition);
                                break;
                            }
                        }
                    }
                }
            }

            return damagedStructures;
        }

        public void RangedBallastFloraDamage(Vector2 worldPosition, float worldRange, float damage, Character attacker = null)
        {
            List<BallastFloraBehavior> ballastFlorae = new List<BallastFloraBehavior>();

            foreach (Hull hull in Hull.hullList)
            {
                if (hull.BallastFlora != null) { ballastFlorae.Add(hull.BallastFlora); }
            }

            foreach (BallastFloraBehavior ballastFlora in ballastFlorae)
            {
                float resistanceMuliplier = ballastFlora.HasBrokenThrough ? 1f : 1f - ballastFlora.ExplosionResistance; 
                ballastFlora.Branches.ForEachMod(branch =>
                {
                    Vector2 branchWorldPos = ballastFlora.GetWorldPosition() + branch.Position;
                    float branchDist = Vector2.Distance(branchWorldPos, worldPosition);
                    if (branchDist < worldRange)
                    {
                        float distFactor = 1.0f - (branchDist / worldRange);
                        if (distFactor <= 0.0f) { return; }

                        Vector2 explosionPos = worldPosition;
                        Vector2 branchPos = branchWorldPos;
                        if (ballastFlora.Parent?.Submarine != null) 
                        { 
                            explosionPos -= ballastFlora.Parent.Submarine.Position;
                            branchPos -= ballastFlora.Parent.Submarine.Position; 
                        }
                        distFactor *= GetObstacleDamageMultiplier(ConvertUnits.ToSimUnits(explosionPos), worldPosition, ConvertUnits.ToSimUnits(branchPos));
                        ballastFlora.DamageBranch(branch, damage * distFactor * resistanceMuliplier, BallastFloraBehavior.AttackType.Explosives, attacker);
                    }
                });
            }
        }

        private static float GetObstacleDamageMultiplier(Vector2 explosionSimPos, Vector2 explosionWorldPos, Vector2 targetSimPos)
        {
            float damageMultiplier = 1.0f;
            var obstacles = Submarine.PickBodies(targetSimPos, explosionSimPos, collisionCategory: Physics.CollisionItem | Physics.CollisionItemBlocking | Physics.CollisionWall);
            foreach (var body in obstacles)
            {
                if (body.UserData is Item item)
                {
                    var door = item.GetComponent<Door>();
                    if (door != null && !door.IsBroken) { damageMultiplier *= 0.01f; }
                }
                else if (body.UserData is Structure structure)
                {
                    int sectionIndex = structure.FindSectionIndex(explosionWorldPos, world: true, clamp: true);
                    if (structure.SectionBodyDisabled(sectionIndex))
                    {
                        continue;
                    }
                    else if (structure.SectionIsLeaking(sectionIndex))
                    {
                        damageMultiplier *= 0.1f;
                    }
                    else
                    {
                        damageMultiplier *= 0.01f;
                    }
                }
                else
                {
                    damageMultiplier *= 0.1f;
                }
            }
            return damageMultiplier;
        }

        static partial void PlayTinnitusProjSpecific(float volume);
    }
}
