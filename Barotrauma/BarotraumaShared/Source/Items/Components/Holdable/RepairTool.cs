using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
#if CLIENT
using Barotrauma.Particles;
#endif

namespace Barotrauma.Items.Components
{
    partial class RepairTool : ItemComponent
    {
        public enum UseEnvironment
        {
            Air, Water, Both, None
        };

        private readonly List<string> fixableEntities;
        private Vector2 pickedPosition;
        private float activeTimer;

        private Vector2 debugRayStartPos, debugRayEndPos;

        [Serialize("Both", false, description: "Can the item be used in air, water or both.")]
        public UseEnvironment UsableIn
        {
            get; set;
        }

        [Serialize(0.0f, false, description: "The distance at which the item can repair targets.")]
        public float Range { get; set; }

        [Serialize(0.0f, false, description: "How many units of damage the item removes from structures per second.")]
        public float StructureFixAmount
        {
            get; set;
        }
        [Serialize(0.0f, false, description: "How much the item decreases the size of fires per second.")]
        public float ExtinguishAmount
        {
            get; set;
        }

        [Serialize("0.0,0.0", false, description: "The position of the barrel as an offset from the item's center (in pixels).")]
        public Vector2 BarrelPos { get; set; }

        [Serialize(false, false, description: "Can the item repair things through walls.")]
        public bool RepairThroughWalls { get; set; }

        [Serialize(false, false, description: "Can the item repair multiple things at once, or will it only affect the first thing the ray from the barrel hits.")]
        public bool RepairMultiple { get; set; }

        [Serialize(false, false, description: "Can the item repair things through holes in walls.")]
        public bool RepairThroughHoles { get; set; }

        [Serialize(0.0f, false, description: "The probability of starting a fire somewhere along the ray fired from the barrel (for example, 0.1 = 10% chance to start a fire during a second of use).")]
        public float FireProbability { get; set; }

        public Vector2 TransformedBarrelPos
        {
            get
            {
                Matrix bodyTransform = Matrix.CreateRotationZ(item.body.Rotation);
                Vector2 flippedPos = BarrelPos;
                if (item.body.Dir < 0.0f) flippedPos.X = -flippedPos.X;
                return (Vector2.Transform(flippedPos, bodyTransform));
            }
        }

        public RepairTool(Item item, XElement element)
            : base(item, element)
        {
            this.item = item;

            if (element.Attribute("limbfixamount") != null)
            {
                DebugConsole.ThrowError("Error in item \"" + item.Name + "\" - RepairTool damage should be configured using a StatusEffect with Afflictions, not the limbfixamount attribute.");
            }

            fixableEntities = new List<string>();
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "fixable":
                        if (subElement.Attribute("name") != null)
                        {
                            DebugConsole.ThrowError("Error in RepairTool " + item.Name + " - use identifiers instead of names to configure fixable entities.");
                            fixableEntities.Add(subElement.Attribute("name").Value);
                        }
                        else
                        {
                            fixableEntities.Add(subElement.GetAttributeString("identifier", ""));
                        }
                        break;
                }
            }
            item.IsShootable = true;
            // TODO: should define this in xml if we have repair tools that don't require aim to use
            item.RequireAimToUse = true;
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        public override void Update(float deltaTime, Camera cam)
        {
            activeTimer -= deltaTime;
            if (activeTimer <= 0.0f) IsActive = false;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (character == null || character.Removed) return false;
            if (item.RequireAimToUse && !character.IsKeyDown(InputType.Aim)) return false;
            
            float degreeOfSuccess = DegreeOfSuccess(character);

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
            Vector2 sourcePos = character?.AnimController == null ? item.SimPosition : character.AnimController.AimSourceSimPos;
            Vector2 barrelPos = item.SimPosition + ConvertUnits.ToSimUnits(TransformedBarrelPos);
            //make sure there's no obstacles between the base of the item (or the shoulder of the character) and the end of the barrel
            if (Submarine.PickBody(sourcePos, barrelPos, collisionCategory: Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionItemBlocking) == null)
            {
                //no obstacles -> we start the raycast at the end of the barrel
                rayStart = ConvertUnits.ToSimUnits(item.WorldPosition + TransformedBarrelPos);
            }
            else
            {
                rayStart = Submarine.LastPickedPosition + Submarine.LastPickedNormal * 0.1f;
                if (item.Submarine != null) { rayStart += item.Submarine.SimPosition; }
            }

            Vector2 rayEnd = rayStart + 
                ConvertUnits.ToSimUnits(new Vector2(
                    (float)Math.Cos(item.body.Rotation),
                    (float)Math.Sin(item.body.Rotation)) * Range * item.body.Dir);

            List<Body> ignoredBodies = new List<Body>();
            foreach (Limb limb in character.AnimController.Limbs)
            {
                if (Rand.Range(0.0f, 0.5f) > degreeOfSuccess) continue;
                ignoredBodies.Add(limb.body.FarseerBody);
            }
            ignoredBodies.Add(character.AnimController.Collider.FarseerBody);

            IsActive = true;
            activeTimer = 0.1f;
            
            debugRayStartPos = ConvertUnits.ToDisplayUnits(rayStart);
            debugRayEndPos = ConvertUnits.ToDisplayUnits(rayEnd);

            if (character.Submarine == null)
            {
                foreach (Submarine sub in Submarine.Loaded)
                {
                    Rectangle subBorders = sub.Borders;
                    subBorders.Location += new Point((int)sub.WorldPosition.X, (int)sub.WorldPosition.Y - sub.Borders.Height);
                    if (!MathUtils.CircleIntersectsRectangle(item.WorldPosition, Range * 5.0f, subBorders))
                    {
                        continue;
                    }
                    Repair(rayStart - sub.SimPosition, rayEnd - sub.SimPosition, deltaTime, character, degreeOfSuccess, ignoredBodies);
                }
                Repair(rayStart, rayEnd, deltaTime, character, degreeOfSuccess, ignoredBodies);
            }
            else
            {
                Repair(rayStart - character.Submarine.SimPosition, rayEnd - character.Submarine.SimPosition, deltaTime, character, degreeOfSuccess, ignoredBodies);
            }
            
            UseProjSpecific(deltaTime, rayStart);

            return true;
        }

        partial void UseProjSpecific(float deltaTime, Vector2 raystart);

        private readonly HashSet<Character> hitCharacters = new HashSet<Character>();
        private readonly List<FireSource> fireSourcesInRange = new List<FireSource>();
        private void Repair(Vector2 rayStart, Vector2 rayEnd, float deltaTime, Character user, float degreeOfSuccess, List<Body> ignoredBodies)
        {
            var collisionCategories = Physics.CollisionWall | Physics.CollisionCharacter | Physics.CollisionItem | Physics.CollisionLevel | Physics.CollisionRepair;

            float lastPickedFraction = 0.0f;
            if (RepairMultiple)
            {
                var bodies = Submarine.PickBodies(rayStart, rayEnd, ignoredBodies, collisionCategories, ignoreSensors: RepairThroughHoles, allowInsideFixture: true);
                lastPickedFraction = Submarine.LastPickedFraction;
                Type lastHitType = null;
                hitCharacters.Clear();
                foreach (Body body in bodies)
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

                    if (FixBody(user, deltaTime, degreeOfSuccess, body))
                    {
                        lastPickedFraction = Submarine.LastPickedBodyDist(body);
                        if (bodyType != null) { lastHitType = bodyType; }
                    }
                }
            }
            else
            {
                FixBody(user, deltaTime, degreeOfSuccess, 
                    Submarine.PickBody(rayStart, rayEnd, 
                    ignoredBodies, collisionCategories, ignoreSensors: RepairThroughHoles, 
                    customPredicate: (Fixture f) => { return f?.Body?.UserData != null; },
                    allowInsideFixture: true));
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
                }

                foreach (FireSource fs in fireSourcesInRange)
                {
                    fs.Extinguish(deltaTime, ExtinguishAmount);
#if SERVER
                    GameMain.Server.KarmaManager.OnExtinguishingFire(user, deltaTime);                    
#endif
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

            pickedPosition = Submarine.LastPickedPosition;

            if (targetBody.UserData is Structure targetStructure)
            {
                if (targetStructure.IsPlatform) { return false; }
                int sectionIndex = targetStructure.FindSectionIndex(ConvertUnits.ToDisplayUnits(pickedPosition));
                if (sectionIndex < 0) { return false; }

                if (!fixableEntities.Contains("structure") && !fixableEntities.Contains(targetStructure.Prefab.Identifier)) { return true; }

                FixStructureProjSpecific(user, deltaTime, targetStructure, sectionIndex);
                targetStructure.AddDamage(sectionIndex, -StructureFixAmount * degreeOfSuccess, user);

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
                        targetStructure.AddDamage(sectionIndex + i, -StructureFixAmount * degreeOfSuccess);
                    }
                }
                return true;
            }
            else if (targetBody.UserData is Character targetCharacter)
            {
                if (targetCharacter.Removed) { return false; }
                targetCharacter.LastDamageSource = item;
                ApplyStatusEffectsOnTarget(user, deltaTime, ActionType.OnUse, new List<ISerializableEntity>() { targetCharacter });
                FixCharacterProjSpecific(user, deltaTime, targetCharacter);
                return true;
            }
            else if (targetBody.UserData is Limb targetLimb)
            {
                if (targetLimb.character == null || targetLimb.character.Removed) { return false; }
                targetLimb.character.LastDamageSource = item;
                ApplyStatusEffectsOnTarget(user, deltaTime, ActionType.OnUse, new List<ISerializableEntity>() { targetLimb.character, targetLimb });
                FixCharacterProjSpecific(user, deltaTime, targetLimb.character);
                return true;
            }
            else if (targetBody.UserData is Item targetItem)
            {
                targetItem.IsHighlighted = true;
                
                ApplyStatusEffectsOnTarget(user, deltaTime, ActionType.OnUse, targetItem.AllPropertyObjects);

                var levelResource = targetItem.GetComponent<LevelResource>();
                if (levelResource != null && levelResource.IsActive &&
                    levelResource.requiredItems.Any() &&
                    levelResource.HasRequiredItems(user, addMessage: false))
                {
                    levelResource.DeattachTimer += deltaTime;
#if CLIENT
                    Character.Controlled?.UpdateHUDProgressBar(
                        this,
                        targetItem.WorldPosition,
                        levelResource.DeattachTimer / levelResource.DeattachDuration,
                        Color.Red, Color.Green);
#endif
                }
                FixItemProjSpecific(user, deltaTime, targetItem);
                return true;
            }
            return false;
        }
    
        partial void FixStructureProjSpecific(Character user, float deltaTime, Structure targetStructure, int sectionIndex);
        partial void FixCharacterProjSpecific(Character user, float deltaTime, Character targetCharacter);
        partial void FixItemProjSpecific(Character user, float deltaTime, Item targetItem);

        private float sinTime;
        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            if (!(objective.OperateTarget is Gap leak)) { return true; }
            if (leak.Submarine == null) { return true; }
            Vector2 fromCharacterToLeak = leak.WorldPosition - character.WorldPosition;
            float dist = fromCharacterToLeak.Length();
            float reach = Range + ConvertUnits.ToDisplayUnits(((HumanoidAnimController)character.AnimController).ArmLength);

            //too far away -> consider this done and hope the AI is smart enough to move closer
            if (dist > reach * 3) { return true; }

            //steer closer if almost in range
            if (dist > reach)
            {
                if (character.AnimController.InWater)
                {
                    if (character.AIController.SteeringManager is IndoorsSteeringManager indoorSteering)
                    {
                        // Swimming inside the sub
                        if (indoorSteering.CurrentPath != null && !indoorSteering.IsPathDirty && indoorSteering.CurrentPath.Unreachable)
                        {
                            Vector2 dir = Vector2.Normalize(fromCharacterToLeak);
                            character.AIController.SteeringManager.SteeringManual(deltaTime, dir / 2);
                        }
                        else
                        {
                            character.AIController.SteeringManager.SteeringSeek(character.GetRelativeSimPosition(leak));
                        }
                    }
                    else
                    {
                        // Swimming outside the sub
                        character.AIController.SteeringManager.SteeringSeek(character.GetRelativeSimPosition(leak));
                    }
                }
                else
                {
                    // TODO: use the collider size?
                    if (!character.AnimController.InWater && character.AnimController is HumanoidAnimController &&
                        Math.Abs(fromCharacterToLeak.X) < 100.0f && fromCharacterToLeak.Y < 0.0f && fromCharacterToLeak.Y > -150.0f)
                    {
                        ((HumanoidAnimController)character.AnimController).Crouching = true;
                    }
                    Vector2 standPos = new Vector2(Math.Sign(-fromCharacterToLeak.X), Math.Sign(-fromCharacterToLeak.Y)) / 2;
                    if (leak.IsHorizontal)
                    {
                        standPos.X *= 2;
                        standPos.Y = 0;
                    }
                    else
                    {
                        standPos.X = 0;
                    }
                    character.AIController.SteeringManager.SteeringSeek(standPos);
                }
            }
            else
            {
                if (dist < reach / 2)
                {
                    // Too close -> steer away
                    character.AIController.SteeringManager.SteeringManual(deltaTime, Vector2.Normalize(character.SimPosition - leak.SimPosition) / 2);
                }
                else if (dist <= reach)
                {
                    // In range
                    character.AIController.SteeringManager.Reset();
                }
                else
                {
                    return false;
                }
            }
            sinTime += deltaTime / 2;
            character.CursorPosition = leak.Position + VectorExtensions.Forward(Item.body.TransformedRotation + (float)Math.Sin(sinTime), dist);
            if (item.RequireAimToUse)
            {
                bool isOperatingButtons = false;
                if (character.AIController.SteeringManager is IndoorsSteeringManager indoorSteering)
                {
                    var door = indoorSteering.CurrentPath?.CurrentNode?.ConnectedDoor;
                    if (door != null && !door.IsOpen)
                    {
                        isOperatingButtons = door.HasIntegratedButtons || door.Item.GetConnectedComponents<Controller>(true).Any();
                    }
                }
                if (!isOperatingButtons)
                {
                    character.SetInput(InputType.Aim, false, true);
                }
            }
            // Press the trigger only when the tool is approximately facing the target.
            Vector2 fromItemToLeak = leak.WorldPosition - item.WorldPosition;
            var angle = VectorExtensions.Angle(VectorExtensions.Forward(item.body.TransformedRotation), fromItemToLeak);
            if (angle < MathHelper.PiOver4)
            {
                character.SetInput(InputType.Shoot, false, true);
                Use(deltaTime, character);
            }

            bool leakFixed = (leak.Open <= 0.0f || leak.Removed) && 
                (leak.ConnectedWall == null || leak.ConnectedWall.Sections.Average(s => s.damage) < 1);

            if (leakFixed && leak.FlowTargetHull != null)
            {
                sinTime = 0;
                if (!leak.FlowTargetHull.ConnectedGaps.Any(g => !g.IsRoomToRoom && g.Open > 0.0f))
                {
                    
                    character.Speak(TextManager.GetWithVariable("DialogLeaksFixed", "[roomname]", leak.FlowTargetHull.DisplayName, true), null, 0.0f, "leaksfixed", 10.0f);
                }
                else
                {
                    character.Speak(TextManager.GetWithVariable("DialogLeakFixed", "[roomname]", leak.FlowTargetHull.DisplayName, true), null, 0.0f, "leakfixed", 10.0f);
                }
            }

            return leakFixed;
        }

        private void ApplyStatusEffectsOnTarget(Character user, float deltaTime, ActionType actionType, IEnumerable<ISerializableEntity> targets)
        {
            if (statusEffectLists == null) { return; }
            if (!statusEffectLists.TryGetValue(actionType, out List<StatusEffect> statusEffects)) { return; }

            foreach (StatusEffect effect in statusEffects)
            {
                effect.SetUser(user);
                if (effect.HasTargetType(StatusEffect.TargetType.UseTarget))
                {
                    effect.Apply(actionType, deltaTime, item, targets);
                }
#if CLIENT
                // Hard-coded progress bars for welding doors stuck.
                // A general purpose system could be better, but it would most likely require changes in the way we define the status effects in xml.
                foreach (ISerializableEntity target in targets)
                {
                    if (target is Door door)
                    {
                        if (!door.CanBeWelded) continue;
                        for (int i = 0; i < effect.propertyNames.Length; i++)
                        {
                            string propertyName = effect.propertyNames[i];
                            if (propertyName != "stuck") { continue; }
                            if (door.SerializableProperties == null || !door.SerializableProperties.TryGetValue(propertyName, out SerializableProperty property)) { continue; }
                            object value = property.GetValue(target);
                            if (door.Stuck > 0)
                            {
                                var progressBar = user.UpdateHUDProgressBar(door, door.Item.WorldPosition, door.Stuck / 100, Color.DarkGray * 0.5f, Color.White);
                                if (progressBar != null) { progressBar.Size = new Vector2(60.0f, 20.0f); }
                            }
                        }
                    }
                }
#endif
            }
        }
    }
}
