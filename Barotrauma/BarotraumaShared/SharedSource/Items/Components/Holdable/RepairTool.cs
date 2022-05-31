using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Barotrauma.MapCreatures.Behavior;

namespace Barotrauma.Items.Components
{
    partial class RepairTool : ItemComponent
    {
        public enum UseEnvironment
        {
            Air, Water, Both, None
        };

        private readonly HashSet<Identifier> fixableEntities;
        private Vector2 pickedPosition;
        private float activeTimer;

        private Vector2 debugRayStartPos, debugRayEndPos;

        private readonly List<Body> ignoredBodies = new List<Body>();

        [Serialize("Both", IsPropertySaveable.No, description: "Can the item be used in air, water or both.")]
        public UseEnvironment UsableIn
        {
            get; set;
        }

        [Serialize(0.0f, IsPropertySaveable.No, description: "The distance at which the item can repair targets.")]
        public float Range { get; set; }

        [Serialize(0.0f, IsPropertySaveable.No, description: "Random spread applied to the firing angle when used by a character with sufficient skills to use the tool (in degrees).")]
        public float Spread
        {
            get;
            set;
        }

        [Serialize(0.0f, IsPropertySaveable.No, description: "Random spread applied to the firing angle when used by a character with insufficient skills to use the tool (in degrees).")]
        public float UnskilledSpread
        {
            get;
            set;
        }

        [Serialize(0.0f, IsPropertySaveable.No, description: "How many units of damage the item removes from structures per second.")]
        public float StructureFixAmount
        {
            get; set;
        }

        [Serialize(0.0f, IsPropertySaveable.No, description: "How much damage is applied to ballast flora.")]
        public float FireDamage
        {
            get; set;
        }

        [Serialize(0.0f, IsPropertySaveable.No, description: "How many units of damage the item removes from destructible level walls per second.")]
        public float LevelWallFixAmount
        {
            get; set;
        }

        [Serialize(0.0f, IsPropertySaveable.No, description: "How much the item decreases the size of fires per second.")]
        public float ExtinguishAmount
        {
            get; set;
        }

        [Serialize(0.0f, IsPropertySaveable.No, description: "How much water the item provides to planters per second.")]
        public float WaterAmount { get; set; }

        [Serialize("0.0,0.0", IsPropertySaveable.No, description: "The position of the barrel as an offset from the item's center (in pixels).")]
        public Vector2 BarrelPos { get; set; }

        [Serialize(false, IsPropertySaveable.No, description: "Can the item repair things through walls.")]
        public bool RepairThroughWalls { get; set; }

        [Serialize(false, IsPropertySaveable.No, description: "Can the item repair multiple things at once, or will it only affect the first thing the ray from the barrel hits.")]
        public bool RepairMultiple { get; set; }

        [Serialize(false, IsPropertySaveable.No, description: "Can the item repair things through holes in walls.")]
        public bool RepairThroughHoles { get; set; }


        [Serialize(100.0f, IsPropertySaveable.No, description: "How far two walls need to not be considered overlapping and to stop the ray.")]
        public float MaxOverlappingWallDist
        {
            get; set;
        }

        [Serialize(true, IsPropertySaveable.No, description: "Can the item hit broken doors.")]
        public bool HitItems { get; set; }

        [Serialize(false, IsPropertySaveable.No, description: "Can the item hit broken doors.")]
        public bool HitBrokenDoors { get; set; }

        [Serialize(0.0f, IsPropertySaveable.No, description: "The probability of starting a fire somewhere along the ray fired from the barrel (for example, 0.1 = 10% chance to start a fire during a second of use).")]
        public float FireProbability { get; set; }

        [Serialize(0.0f, IsPropertySaveable.No, description: "Force applied to the entity the ray hits.")]
        public float TargetForce { get; set; }

        [Serialize(0.0f, IsPropertySaveable.No, description: "Rotation of the barrel in degrees."), Editable(MinValueFloat = 0, MaxValueFloat = 360, VectorComponentLabels = new string[] { "editable.minvalue", "editable.maxvalue" })]
        public float BarrelRotation
        {
            get; set;
        }

        public Vector2 TransformedBarrelPos
        {
            get
            {
                if (item.body == null) { return BarrelPos; }
                Matrix bodyTransform = Matrix.CreateRotationZ(item.body.Rotation + MathHelper.ToRadians(BarrelRotation));
                Vector2 flippedPos = BarrelPos;
                if (item.body.Dir < 0.0f) { flippedPos.X = -flippedPos.X; }
                return Vector2.Transform(flippedPos, bodyTransform);
            }
        }

        public RepairTool(Item item, ContentXElement element)
            : base(item, element)
        {
            this.item = item;

            if (element.GetAttribute("limbfixamount") != null)
            {
                DebugConsole.ThrowError("Error in item \"" + item.Name + "\" - RepairTool damage should be configured using a StatusEffect with Afflictions, not the limbfixamount attribute.");
            }

            fixableEntities = new HashSet<Identifier>();
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "fixable":
                        if (subElement.GetAttribute("name") != null)
                        {
                            DebugConsole.ThrowError("Error in RepairTool " + item.Name + " - use identifiers instead of names to configure fixable entities.");
                            fixableEntities.Add(subElement.GetAttribute("name").Value.ToIdentifier());
                        }
                        else
                        {
                            fixableEntities.Add(subElement.GetAttributeIdentifier("identifier", ""));
                        }
                        break;
                }
            }
            item.IsShootable = true;
            item.RequireAimToUse = element.Parent.GetAttributeBool("requireaimtouse", true);
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(ContentXElement element);

        public override void Update(float deltaTime, Camera cam)
        {
            activeTimer -= deltaTime;
            if (activeTimer <= 0.0f) { IsActive = false; }
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (character != null)
            {
                if (item.RequireAimToUse && !character.IsKeyDown(InputType.Aim)) { return false; }
            }
            
            float degreeOfSuccess = character == null ? 0.5f : DegreeOfSuccess(character);

            if (Rand.Range(0.0f, 0.5f) > degreeOfSuccess)
            {
                ApplyStatusEffects(ActionType.OnFailure, deltaTime, character);
                return false;
            }

            if (UsableIn == UseEnvironment.None)
            {
                ApplyStatusEffects(ActionType.OnFailure, deltaTime, character);
                return false;
            }

            if (item.InWater)
            {
                if (UsableIn == UseEnvironment.Air)
                {
                    ApplyStatusEffects(ActionType.OnFailure, deltaTime, character);
                    return false;
                }
            }
            else
            {
                if (UsableIn == UseEnvironment.Water)
                {
                    ApplyStatusEffects(ActionType.OnFailure, deltaTime, character);
                    return false;
                }
            }

            Vector2 rayStart;
            Vector2 rayStartWorld;
            Vector2 sourcePos = character?.AnimController == null ? item.SimPosition : character.AnimController.AimSourceSimPos;
            Vector2 barrelPos = item.SimPosition + ConvertUnits.ToSimUnits(TransformedBarrelPos);
            //make sure there's no obstacles between the base of the item (or the shoulder of the character) and the end of the barrel
            if (Submarine.PickBody(sourcePos, barrelPos, collisionCategory: Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionItemBlocking) == null)
            {
                //no obstacles -> we start the raycast at the end of the barrel
                rayStart = ConvertUnits.ToSimUnits(item.Position + TransformedBarrelPos);
                rayStartWorld = ConvertUnits.ToSimUnits(item.WorldPosition + TransformedBarrelPos);
            }
            else
            {
                rayStart = rayStartWorld = Submarine.LastPickedPosition + Submarine.LastPickedNormal * 0.1f;
                if (item.Submarine != null) { rayStartWorld += item.Submarine.SimPosition; }
            }

            //if the calculated barrel pos is in another hull, use the origin of the item to make sure the particles don't end up in an incorrect hull
            if (item.CurrentHull != null)
            {
                var barrelHull = Hull.FindHull(ConvertUnits.ToDisplayUnits(rayStartWorld), item.CurrentHull, useWorldCoordinates: true);
                if (barrelHull != null && barrelHull != item.CurrentHull)
                {
                    if (MathUtils.GetLineRectangleIntersection(ConvertUnits.ToDisplayUnits(sourcePos), ConvertUnits.ToDisplayUnits(rayStart), item.CurrentHull.Rect, out Vector2 hullIntersection))
                    {
                        if (!item.CurrentHull.ConnectedGaps.Any(g => g.Open > 0.0f && Submarine.RectContains(g.Rect, hullIntersection))) 
                        { 
                            Vector2 rayDir = rayStart.NearlyEquals(sourcePos) ? Vector2.Zero : Vector2.Normalize(rayStart - sourcePos);
                            rayStartWorld = ConvertUnits.ToSimUnits(hullIntersection - rayDir * 5.0f);
                            if (item.Submarine != null) { rayStartWorld += item.Submarine.SimPosition; }
                        }
                    }
                }
            }

            float spread = MathHelper.ToRadians(MathHelper.Lerp(UnskilledSpread, Spread, degreeOfSuccess));

            float angle = MathHelper.ToRadians(BarrelRotation) + spread * Rand.Range(-0.5f, 0.5f);
            float dir = 1;
            if (item.body != null)
            {
                angle += item.body.Rotation;
                dir = item.body.Dir;
            }
            Vector2 rayEnd = rayStartWorld + ConvertUnits.ToSimUnits(new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * Range * dir);

            ignoredBodies.Clear();
            if (character != null)
            {
                foreach (Limb limb in character.AnimController.Limbs)
                {
                    if (Rand.Range(0.0f, 0.5f) > degreeOfSuccess) continue;
                    ignoredBodies.Add(limb.body.FarseerBody);
                }
                ignoredBodies.Add(character.AnimController.Collider.FarseerBody);
            }

            IsActive = true;
            activeTimer = 0.1f;
            
            debugRayStartPos = ConvertUnits.ToDisplayUnits(rayStartWorld);
            debugRayEndPos = ConvertUnits.ToDisplayUnits(rayEnd);

            Submarine parentSub = character?.Submarine ?? item.Submarine;
            if (parentSub == null)
            {
                foreach (Submarine sub in Submarine.Loaded)
                {
                    Rectangle subBorders = sub.Borders;
                    subBorders.Location += new Point((int)sub.WorldPosition.X, (int)sub.WorldPosition.Y - sub.Borders.Height);
                    if (!MathUtils.CircleIntersectsRectangle(item.WorldPosition, Range * 5.0f, subBorders))
                    {
                        continue;
                    }
                    Repair(rayStartWorld - sub.SimPosition, rayEnd - sub.SimPosition, deltaTime, character, degreeOfSuccess, ignoredBodies);
                }
                Repair(rayStartWorld, rayEnd, deltaTime, character, degreeOfSuccess, ignoredBodies);
            }
            else
            {
                Repair(rayStartWorld - parentSub.SimPosition, rayEnd - parentSub.SimPosition, deltaTime, character, degreeOfSuccess, ignoredBodies);
            }
            
            UseProjSpecific(deltaTime, rayStartWorld);

            return true;
        }

        partial void UseProjSpecific(float deltaTime, Vector2 raystart);

        private static readonly List<Body> hitBodies = new List<Body>();
        private readonly HashSet<Character> hitCharacters = new HashSet<Character>();
        private readonly List<FireSource> fireSourcesInRange = new List<FireSource>();
        private void Repair(Vector2 rayStart, Vector2 rayEnd, float deltaTime, Character user, float degreeOfSuccess, List<Body> ignoredBodies)
        {
            var collisionCategories = Physics.CollisionWall | Physics.CollisionCharacter | Physics.CollisionItem | Physics.CollisionLevel | Physics.CollisionRepair;

            //if the item can cut off limbs, activate nearby bodies to allow the raycast to hit them
            if (statusEffectLists != null && statusEffectLists.ContainsKey(ActionType.OnUse))
            {
                if (statusEffectLists[ActionType.OnUse].Any(s => s.SeverLimbsProbability > 0.0f))
                {
                    float rangeSqr = ConvertUnits.ToSimUnits(Range);
                    rangeSqr *= rangeSqr;
                    foreach (Character c in Character.CharacterList)
                    {
                        if (!c.Enabled || !c.AnimController.BodyInRest) { continue; }
                        //do a broad check first
                        if (Math.Abs(c.WorldPosition.X - item.WorldPosition.X) > 1000.0f) { continue; }
                        if (Math.Abs(c.WorldPosition.Y - item.WorldPosition.Y) > 1000.0f) { continue; }
                        foreach (Limb limb in c.AnimController.Limbs)
                        {
                            if (Vector2.DistanceSquared(limb.SimPosition, item.SimPosition) < rangeSqr && Vector2.Dot(rayEnd - rayStart, limb.SimPosition - rayStart) > 0)
                            {
                                c.AnimController.BodyInRest = false;
                                break;
                            }
                        }
                    }
                }
            }

            float lastPickedFraction = 0.0f;
            if (RepairMultiple)
            {
                var bodies = Submarine.PickBodies(rayStart, rayEnd, ignoredBodies, collisionCategories,
                    ignoreSensors: false,
                    customPredicate: (Fixture f) =>
                    {
                        if (f.IsSensor)
                        {
                            if (RepairThroughHoles && f.Body?.UserData is Structure) { return false; }
                            if (f.Body?.UserData is PhysicsBody) { return false; }
                        }
                        if (f.Body?.UserData is Item it && it.GetComponent<Planter>() != null) { return false; }
                        if (f.Body?.UserData as string == "ruinroom") { return false; }
                        if (f.Body?.UserData is VineTile && !(FireDamage > 0)) { return false; }
                        return true;
                    },
                    allowInsideFixture: true);

                hitBodies.Clear();
                hitBodies.AddRange(bodies.Distinct());

                lastPickedFraction = Submarine.LastPickedFraction;
                Type lastHitType = null;
                hitCharacters.Clear();
                foreach (Body body in hitBodies)
                {
                    Type bodyType = body.UserData?.GetType();
                    if (!RepairThroughWalls && bodyType != null && bodyType != lastHitType)
                    {
                        //stop the ray if it already hit a door/wall and is now about to hit some other type of entity
                        if (lastHitType == typeof(Item) || lastHitType == typeof(Structure)) { break; }
                    }

                    Character hitCharacter = null;
                    if (body.UserData is Limb limb)
                    {
                        hitCharacter = limb.character;
                    }
                    else if (body.UserData is Character character)
                    {
                        hitCharacter = character;
                    }
                    //only do damage once to each character even if they ray hit multiple limbs
                    if (hitCharacter != null)
                    {
                        if (hitCharacters.Contains(hitCharacter)) { continue; }
                        hitCharacters.Add(hitCharacter);
                    }

                    //if repairing through walls is not allowed and the next wall is more than 100 pixels away from the previous one, stop here
                    //(= repairing multiple overlapping walls is allowed as long as the edges of the walls are less than MaxOverlappingWallDist pixels apart)
                    float thisBodyFraction = Submarine.LastPickedBodyDist(body);
                    if (!RepairThroughWalls && lastHitType == typeof(Structure) && Range * (thisBodyFraction - lastPickedFraction) > MaxOverlappingWallDist)
                    {
                        break;
                    }
                    pickedPosition = rayStart + (rayEnd - rayStart) * thisBodyFraction;
                    if (FixBody(user, deltaTime, degreeOfSuccess, body))
                    {
                        lastPickedFraction = thisBodyFraction;
                        if (bodyType != null) { lastHitType = bodyType; }
                    }
                }
            }
            else
            {
                var pickedBody = Submarine.PickBody(rayStart, rayEnd,
                    ignoredBodies, collisionCategories,
                    ignoreSensors: false,
                    customPredicate: (Fixture f) =>
                    {
                        if (f.IsSensor)
                        {
                            if (RepairThroughHoles && f.Body?.UserData is Structure) { return false; }
                            if (f.Body?.UserData is PhysicsBody) { return false; }
                        }
                        if (f.Body?.UserData as string == "ruinroom") { return false; }
                        if (f.Body?.UserData is VineTile && !(FireDamage > 0)) { return false; }

                        if (f.Body?.UserData is Item targetItem)
                        {
                            if (!HitItems) { return false; }
                            if (HitBrokenDoors)
                            {
                                if (targetItem.GetComponent<Door>() == null && targetItem.Condition <= 0) { return false; }
                            }
                            else
                            {
                                if (targetItem.Condition <= 0) { return false; }
                            }
                        }
                        return f.Body?.UserData != null;
                    },
                    allowInsideFixture: true);
                pickedPosition = Submarine.LastPickedPosition;
                FixBody(user, deltaTime, degreeOfSuccess, pickedBody);                    
                lastPickedFraction = Submarine.LastPickedFraction;
            }
            
            if (ExtinguishAmount > 0.0f && item.CurrentHull != null)
            {
                fireSourcesInRange.Clear();
                //step along the ray in 10% intervals, collecting all fire sources in the range
                for (float x = 0.0f; x <= lastPickedFraction; x += 0.1f)
                {
                    Vector2 displayPos = ConvertUnits.ToDisplayUnits(rayStart + (rayEnd - rayStart) * x);
                    if (item.CurrentHull.Submarine != null) { displayPos += item.CurrentHull.Submarine.Position; }

                    Hull hull = Hull.FindHull(displayPos, item.CurrentHull);
                    if (hull == null) continue;
                    foreach (FireSource fs in hull.FireSources)
                    {
                        if (fs.IsInDamageRange(displayPos, 100.0f) && !fireSourcesInRange.Contains(fs))
                        {
                            fireSourcesInRange.Add(fs);
                        }
                    }
                    foreach (FireSource fs in hull.FakeFireSources)
                    {
                        if (fs.IsInDamageRange(displayPos, 100.0f) && !fireSourcesInRange.Contains(fs))
                        {
                            fireSourcesInRange.Add(fs);
                        }
                    }
                }

                foreach (FireSource fs in fireSourcesInRange)
                {
                    fs.Extinguish(deltaTime, ExtinguishAmount);
#if SERVER
                    if (!(fs is DummyFireSource))
                    {
                        GameMain.Server.KarmaManager.OnExtinguishingFire(user, deltaTime);     
                    }               
#endif
                }
            }

            if (WaterAmount > 0.0f && item.Submarine != null)
            {
                Vector2 pos = ConvertUnits.ToDisplayUnits(rayStart + item.Submarine.SimPosition);

                // Could probably be done much efficiently here
                foreach (Item it in Item.ItemList)
                {
                    if (it.Submarine == item.Submarine && it.GetComponent<Planter>() is { } planter)
                    {
                        if (it.GetComponent<Holdable>() is { } holdable && holdable.Attachable && !holdable.Attached) { continue; }

                        Rectangle collisionRect = it.WorldRect;
                        collisionRect.Y -= collisionRect.Height;
                        if (collisionRect.Left < pos.X && collisionRect.Right > pos.X && collisionRect.Bottom < pos.Y)
                        {
                            Body collision = Submarine.PickBody(rayStart, it.SimPosition, ignoredBodies, collisionCategories);
                            if (collision == null)
                            {
                                for (var i = 0; i < planter.GrowableSeeds.Length; i++)
                                {
                                    Growable seed = planter.GrowableSeeds[i];
                                    if (seed == null || seed.Decayed) { continue; }

                                    seed.Health += WaterAmount * deltaTime;

#if CLIENT
                                    float barOffset = 10f * GUI.Scale;
                                    Vector2 offset = planter.PlantSlots.ContainsKey(i) ? planter.PlantSlots[i].Offset : Vector2.Zero;
                                    user?.UpdateHUDProgressBar(planter, planter.Item.DrawPosition + new Vector2(barOffset, 0) + offset, seed.Health / seed.MaxHealth, GUIStyle.Blue, GUIStyle.Blue, "progressbar.watering");
#endif
                                }
                            }
                        }
                    }
                }
            }

            if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
            {
                if (Rand.Range(0.0f, 1.0f) < FireProbability * deltaTime)
                {
                    Vector2 displayPos = ConvertUnits.ToDisplayUnits(rayStart + (rayEnd - rayStart) * lastPickedFraction * 0.9f);
                    if (item.CurrentHull.Submarine != null) { displayPos += item.CurrentHull.Submarine.Position; }
                    new FireSource(displayPos);
                }
            }
        }

        private bool FixBody(Character user, float deltaTime, float degreeOfSuccess, Body targetBody)
        {
            if (targetBody?.UserData == null) { return false; }

            if (targetBody.UserData is Structure targetStructure)
            {
                if (targetStructure.IsPlatform) { return false; }
                int sectionIndex = targetStructure.FindSectionIndex(ConvertUnits.ToDisplayUnits(pickedPosition));
                if (sectionIndex < 0) { return false; }

                if (!fixableEntities.Contains("structure") && !fixableEntities.Contains(targetStructure.Prefab.Identifier)) { return true; }

                ApplyStatusEffectsOnTarget(user, deltaTime, ActionType.OnUse, structure: targetStructure);
                FixStructureProjSpecific(user, deltaTime, targetStructure, sectionIndex);

                float structureFixAmount = StructureFixAmount;
                if (structureFixAmount >= 0f)
                {
                    structureFixAmount *= 1 + user.GetStatValue(StatTypes.RepairToolStructureRepairMultiplier);
                    structureFixAmount *= 1 + item.GetQualityModifier(Quality.StatType.RepairToolStructureRepairMultiplier);
                }
                else
                {
                    structureFixAmount *= 1 + user.GetStatValue(StatTypes.RepairToolStructureDamageMultiplier);
                    structureFixAmount *= 1 + item.GetQualityModifier(Quality.StatType.RepairToolStructureDamageMultiplier);
                }

                targetStructure.AddDamage(sectionIndex, -structureFixAmount * degreeOfSuccess, user);

                //if the next section is small enough, apply the effect to it as well
                //(to make it easier to fix a small "left-over" section)
                for (int i = -1; i < 2; i += 2)
                {
                    int nextSectionLength = targetStructure.SectionLength(sectionIndex + i);
                    if ((sectionIndex == 1 && i == -1) ||
                        (sectionIndex == targetStructure.SectionCount - 2 && i == 1) ||
                        (nextSectionLength > 0 && nextSectionLength < Structure.WallSectionSize * 0.3f))
                    {
                        //targetStructure.HighLightSection(sectionIndex + i);
                        targetStructure.AddDamage(sectionIndex + i, -structureFixAmount * degreeOfSuccess);
                    }
                }
                return true;
            }
            else if (targetBody.UserData is Voronoi2.VoronoiCell cell && cell.IsDestructible)
            {
                if (Level.Loaded?.ExtraWalls.Find(w => w.Body == cell.Body) is DestructibleLevelWall levelWall)
                {
                    levelWall.AddDamage(-LevelWallFixAmount * deltaTime, item.WorldPosition);
                }
                return true;
            }
            else if (targetBody.UserData is LevelObject levelObject && levelObject.Prefab.TakeLevelWallDamage)
            {
                levelObject.AddDamage(-LevelWallFixAmount, deltaTime, item);                
                return true;
            }
            else if (targetBody.UserData is Character targetCharacter)
            {
                if (targetCharacter.Removed) { return false; }
                targetCharacter.LastDamageSource = item;
                Limb closestLimb = null;
                float closestDist = float.MaxValue;
                foreach (Limb limb in targetCharacter.AnimController.Limbs)
                {
                    float dist = Vector2.DistanceSquared(item.SimPosition, limb.SimPosition);
                    if (dist < closestDist)
                    {
                        closestLimb = limb;
                        closestDist = dist;
                    }
                }

                if (closestLimb != null && !MathUtils.NearlyEqual(TargetForce, 0.0f))
                {
                    Vector2 dir = closestLimb.WorldPosition - item.WorldPosition;
                    dir = dir.LengthSquared() < 0.0001f ? Vector2.UnitY : Vector2.Normalize(dir);
                    closestLimb.body.ApplyForce(dir * TargetForce, maxVelocity: 10.0f);
                }

                ApplyStatusEffectsOnTarget(user, deltaTime, ActionType.OnUse, character: targetCharacter, limb: closestLimb);
                FixCharacterProjSpecific(user, deltaTime, targetCharacter);
                return true;
            }
            else if (targetBody.UserData is Limb targetLimb)
            {
                if (targetLimb.character == null || targetLimb.character.Removed) { return false; }

                if (!MathUtils.NearlyEqual(TargetForce, 0.0f))
                {
                    Vector2 dir = targetLimb.WorldPosition - item.WorldPosition;
                    dir = dir.LengthSquared() < 0.0001f ? Vector2.UnitY : Vector2.Normalize(dir);
                    targetLimb.body.ApplyForce(dir * TargetForce, maxVelocity: 10.0f);
                }

                targetLimb.character.LastDamageSource = item;
                ApplyStatusEffectsOnTarget(user, deltaTime, ActionType.OnUse, character: targetLimb.character, limb: targetLimb);
                FixCharacterProjSpecific(user, deltaTime, targetLimb.character);
                return true;
            }
            else if (targetBody.UserData is Item targetItem)
            {
                if (!HitItems || !targetItem.IsInteractable(user)) { return false; }

                var levelResource = targetItem.GetComponent<LevelResource>();
                if (levelResource != null && levelResource.Attached &&
                    levelResource.requiredItems.Any() &&
                    levelResource.HasRequiredItems(user, addMessage: false))
                {
                    float addedDetachTime = deltaTime * (1f + user.GetStatValue(StatTypes.RepairToolDeattachTimeMultiplier)) * (1f + item.GetQualityModifier(Quality.StatType.RepairToolDeattachTimeMultiplier));
                    levelResource.DeattachTimer += addedDetachTime;
#if CLIENT
                    Character.Controlled?.UpdateHUDProgressBar(
                        this,
                        targetItem.WorldPosition,
                        levelResource.DeattachTimer / levelResource.DeattachDuration,
                        GUIStyle.Red, GUIStyle.Green, "progressbar.deattaching");
#endif
                    FixItemProjSpecific(user, deltaTime, targetItem, showProgressBar: false);
                    return true;
                }
                
                if (!targetItem.Prefab.DamagedByRepairTools) { return false; }

                if (HitBrokenDoors)
                {
                    if (targetItem.GetComponent<Door>() == null && targetItem.Condition <= 0) { return false; }
                }
                else
                {
                    if (targetItem.Condition <= 0) { return false; }
                }

                targetItem.IsHighlighted = true;
                
                ApplyStatusEffectsOnTarget(user, deltaTime, ActionType.OnUse, targetItem);

                if (targetItem.body != null && !MathUtils.NearlyEqual(TargetForce, 0.0f))
                {
                    Vector2 dir = targetItem.WorldPosition - item.WorldPosition;
                    dir = dir.LengthSquared() < 0.0001f ? Vector2.UnitY : Vector2.Normalize(dir);
                    targetItem.body.ApplyForce(dir * TargetForce, maxVelocity: 10.0f);
                }

                FixItemProjSpecific(user, deltaTime, targetItem, showProgressBar: true);
                return true;
            }
            else if (targetBody.UserData is BallastFloraBranch branch)
            {
                if (branch.ParentBallastFlora is { } ballastFlora)
                {
                    ballastFlora.DamageBranch(branch, FireDamage * deltaTime, BallastFloraBehavior.AttackType.Fire, user);
                }
            }
            return false;
        }
    
        partial void FixStructureProjSpecific(Character user, float deltaTime, Structure targetStructure, int sectionIndex);
        partial void FixCharacterProjSpecific(Character user, float deltaTime, Character targetCharacter);
        partial void FixItemProjSpecific(Character user, float deltaTime, Item targetItem, bool showProgressBar);

        private float sinTime;
        private float repairTimer;
        private Gap previousGap;
        private readonly float repairTimeOut = 5;
        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            if (!(objective.OperateTarget is Gap leak))
            {
                Reset();
                return true;
            }
            if (leak.Submarine == null || leak.Submarine != character.Submarine)
            {
                Reset();
                return true;
            }
            if (leak != previousGap)
            {
                Reset();
                previousGap = leak;
            }
            Vector2 fromCharacterToLeak = leak.WorldPosition - character.AnimController.AimSourceWorldPos;
            float dist = fromCharacterToLeak.Length();
            float reach = AIObjectiveFixLeak.CalculateReach(this, character);
            if (dist > reach * 2)
            {
                // Too far away -> consider this done and hope the AI is smart enough to move closer
                Reset();
                return true;
            }
            character.AIController.SteeringManager.Reset();
            if (character.AIController.SteeringManager is IndoorsSteeringManager pathSteering)
            {
                pathSteering.ResetPath();
            }
            if (!character.AnimController.InWater)
            {
                // TODO: use the collider size?
                if (!character.AnimController.InWater && character.AnimController is HumanoidAnimController humanAnim &&
                    Math.Abs(fromCharacterToLeak.X) < 100.0f && fromCharacterToLeak.Y < 0.0f && fromCharacterToLeak.Y > -150.0f)
                {
                    humanAnim.Crouching = true;
                }
            }
            if (!character.IsClimbing)
            {
                if (dist > reach * 0.8f || dist > reach * 0.5f && character.AnimController.Limbs.Any(l => l.InWater))
                {
                    // Steer closer
                    Vector2 dir = Vector2.Normalize(fromCharacterToLeak);
                    if (!character.InWater)
                    {
                        dir.Y = 0;
                    }
                    character.AIController.SteeringManager.SteeringManual(deltaTime, dir);
                }
                else if (dist < reach * 0.25f && !character.IsClimbing)
                {
                    // Too close -> steer away
                    character.AIController.SteeringManager.SteeringManual(deltaTime, Vector2.Normalize(character.SimPosition - leak.SimPosition));
                }
            }
            if (dist <= reach || character.IsClimbing)
            {
                // In range
                character.CursorPosition = leak.WorldPosition;
                if (character.Submarine != null)
                {
                    character.CursorPosition -= character.Submarine.Position;
                }
                character.CursorPosition += VectorExtensions.Forward(Item.body.TransformedRotation + (float)Math.Sin(sinTime) / 2, dist / 2);
                if (character.AnimController.InWater)
                {
                    var torso = character.AnimController.GetLimb(LimbType.Torso);
                    // Turn facing the target when not moving (handled in the animcontroller if not moving)
                    Vector2 mousePos = ConvertUnits.ToSimUnits(character.CursorPosition);
                    Vector2 diff = (mousePos - torso.SimPosition) * character.AnimController.Dir;
                    float newRotation = MathUtils.VectorToAngle(diff);
                    character.AnimController.Collider.SmoothRotate(newRotation, 5.0f);

                    if (VectorExtensions.Angle(VectorExtensions.Forward(torso.body.TransformedRotation), fromCharacterToLeak) < MathHelper.PiOver4)
                    {
                        // Swim past
                        Vector2 moveDir = leak.IsHorizontal ? Vector2.UnitY : Vector2.UnitX;
                        moveDir *= character.AnimController.Dir;
                        character.AIController.SteeringManager.SteeringManual(deltaTime, moveDir);
                    }
                }
                if (item.RequireAimToUse)
                {
                    character.SetInput(InputType.Aim, false, true);
                    sinTime += deltaTime * 5;
                }
                // Press the trigger only when the tool is approximately facing the target.
                Vector2 fromItemToLeak = leak.WorldPosition - item.WorldPosition;
                var angle = VectorExtensions.Angle(VectorExtensions.Forward(item.body.TransformedRotation), fromItemToLeak);
                if (angle < MathHelper.PiOver4)
                {
                    if (Submarine.PickBody(item.SimPosition, leak.SimPosition, collisionCategory: Physics.CollisionWall, allowInsideFixture: true)?.UserData is Item i)
                    {
                        var door = i.GetComponent<Door>();
                        // Hit a door, abandon so that we don't weld it shut.
                        return door != null && !door.CanBeTraversed;
                    }
                    // Check that we don't hit any friendlies
                    if (Submarine.PickBodies(item.SimPosition, leak.SimPosition, collisionCategory: Physics.CollisionCharacter).None(hit =>
                    {
                        if (hit.UserData is Character c)
                        {
                            if (c == character) { return false; }
                            return HumanAIController.IsFriendly(character, c);
                        }
                        return false;
                    }))
                    {
                        character.SetInput(InputType.Shoot, false, true);
                        Use(deltaTime, character);
                        repairTimer += deltaTime;
                        if (repairTimer > repairTimeOut)
                        {
#if DEBUG
                            DebugConsole.NewMessage($"{character.Name}: timed out while welding a leak in {leak.FlowTargetHull.DisplayName}.", color: Color.Yellow);
#endif
                            Reset();
                            return true;
                        }
                    }
                }
            }
            else
            {
                // Reset the timer so that we don't time out if the water forces push us away
                repairTimer = 0;
            }

            bool leakFixed = (leak.Open <= 0.0f || leak.Removed) && 
                (leak.ConnectedWall == null || leak.ConnectedWall.Sections.Max(s => s.damage) < 0.1f);

            if (leakFixed && leak.FlowTargetHull?.DisplayName != null && character.IsOnPlayerTeam)
            {
                if (!leak.FlowTargetHull.ConnectedGaps.Any(g => !g.IsRoomToRoom && g.Open > 0.0f))
                {
                    character.Speak(TextManager.GetWithVariable("DialogLeaksFixed", "[roomname]", leak.FlowTargetHull.DisplayName, FormatCapitals.Yes).Value, null, 0.0f, "leaksfixed".ToIdentifier(), 10.0f);
                }
                else
                {
                    character.Speak(TextManager.GetWithVariable("DialogLeakFixed", "[roomname]", leak.FlowTargetHull.DisplayName, FormatCapitals.Yes).Value, null, 0.0f, "leakfixed".ToIdentifier(), 10.0f);
                }
            }

            return leakFixed;

            void Reset()
            {
                sinTime = 0;
                repairTimer = 0;
            }
        }

        private static List<ISerializableEntity> currentTargets = new List<ISerializableEntity>();
        private void ApplyStatusEffectsOnTarget(Character user, float deltaTime, ActionType actionType, Item targetItem = null, Character character = null, Limb limb = null, Structure structure = null)
        {
            if (statusEffectLists == null) { return; }
            if (!statusEffectLists.TryGetValue(actionType, out List<StatusEffect> statusEffects)) { return; }

            foreach (StatusEffect effect in statusEffects)
            {
                currentTargets.Clear();
                effect.SetUser(user);
                if (effect.HasTargetType(StatusEffect.TargetType.UseTarget))
                {
                    if (targetItem != null)
                    {
                        currentTargets.AddRange(targetItem.AllPropertyObjects);
                    }
                    if (structure != null)
                    {
                        currentTargets.Add(structure);
                    }
                    if (character != null)
                    {
                        currentTargets.Add(character);
                    }
                    effect.Apply(actionType, deltaTime, item, currentTargets);
                }
                else if (effect.HasTargetType(StatusEffect.TargetType.Character))
                {
                    currentTargets.Add(character);
                    effect.Apply(actionType, deltaTime, item, currentTargets);
                }
                else if (effect.HasTargetType(StatusEffect.TargetType.Limb))
                {
                    currentTargets.Add(limb);
                    effect.Apply(actionType, deltaTime, item, currentTargets);
                }

#if CLIENT
                if (user == null) { return; }
                // Hard-coded progress bars for welding doors stuck.
                // A general purpose system could be better, but it would most likely require changes in the way we define the status effects in xml.
                foreach (ISerializableEntity target in currentTargets)
                {
                    if (!(target is Door door)) { continue; }                    
                    if (!door.CanBeWelded || !door.Item.IsInteractable(user)) { continue; }
                    for (int i = 0; i < effect.propertyNames.Length; i++)
                    {
                        Identifier propertyName = effect.propertyNames[i];
                        if (propertyName != "stuck") { continue; }
                        if (door.SerializableProperties == null || !door.SerializableProperties.TryGetValue(propertyName, out SerializableProperty property)) { continue; }
                        object value = property.GetValue(target);
                        if (door.Stuck > 0)
                        {
                            bool isCutting = effect.propertyEffects[i].GetType() == typeof(float) && (float)effect.propertyEffects[i] < 0;
                            var progressBar = user.UpdateHUDProgressBar(door, door.Item.WorldPosition, door.Stuck / 100, Color.DarkGray * 0.5f, Color.White,
                                textTag: isCutting ? "progressbar.cutting" : "progressbar.welding");
                            if (progressBar != null) { progressBar.Size = new Vector2(60.0f, 20.0f); }
                            if (!isCutting) { HintManager.OnWeldingDoor(user, door); }
                        }
                    }                    
                }
#endif
            }
        }
    }
}
