using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
#if CLIENT
using Barotrauma.Particles;
#endif

namespace Barotrauma.Items.Components
{
    partial class RepairTool : ItemComponent
    {
        private readonly List<string> fixableEntities;

        private float range;

        private Vector2 pickedPosition;

        private Vector2 barrelPos;
        
        private float activeTimer;

        [Serialize(0.0f, false)]
        public float Range
        {
            get { return range; }
            set { range = value; }
        }

        [Serialize(0.0f, false)]
        public float StructureFixAmount
        {
            get; set;
        }

        [Serialize(0.0f, false)]
        public float LimbFixAmount
        {
            get; set;
        }
        [Serialize(0.0f, false)]
        public float ExtinguishAmount
        {
            get; set;
        }
        
        [Serialize("0.0,0.0", false)]
        public Vector2 BarrelPos
        {
            get { return barrelPos; }
            set { barrelPos = value; }
        }

        public Vector2 TransformedBarrelPos
        {
            get
            {
                Matrix bodyTransform = Matrix.CreateRotationZ(item.body.Rotation);
                Vector2 flippedPos = barrelPos;
                if (item.body.Dir < 0.0f) flippedPos.X = -flippedPos.X;
                return (Vector2.Transform(flippedPos, bodyTransform));
            }
        }

        public RepairTool(Item item, XElement element)
            : base(item, element)
        {
            this.item = item;

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
            if (!character.IsKeyDown(InputType.Aim)) return false;
            
            float degreeOfSuccess = DegreeOfSuccess(character);

            if (Rand.Range(0.0f, 0.5f) > degreeOfSuccess)
            {
                ApplyStatusEffects(ActionType.OnFailure, deltaTime, character);
                return false;
            }

            Vector2 targetPosition = item.WorldPosition;
            targetPosition += new Vector2(
                (float)Math.Cos(item.body.Rotation),
                (float)Math.Sin(item.body.Rotation)) * range * item.body.Dir;

            List<Body> ignoredBodies = new List<Body>();
            foreach (Limb limb in character.AnimController.Limbs)
            {
                if (Rand.Range(0.0f, 0.5f) > degreeOfSuccess) continue;
                ignoredBodies.Add(limb.body.FarseerBody);
            }
            ignoredBodies.Add(character.AnimController.Collider.FarseerBody);

            IsActive = true;
            activeTimer = 0.1f;

            Vector2 rayStart    = ConvertUnits.ToSimUnits(item.WorldPosition);
            Vector2 rayEnd      = ConvertUnits.ToSimUnits(targetPosition);

            if (character.Submarine == null)
            {
                foreach (Submarine sub in Submarine.Loaded)
                {
                    Repair(rayStart - sub.SimPosition, rayEnd - sub.SimPosition, deltaTime, character, degreeOfSuccess, ignoredBodies);
                }
                Repair(rayStart, rayEnd, deltaTime, character, degreeOfSuccess, ignoredBodies);
            }
            else
            {
                Repair(rayStart - character.Submarine.SimPosition, rayEnd - character.Submarine.SimPosition, deltaTime, character, degreeOfSuccess, ignoredBodies);
            }

            UseProjSpecific(deltaTime);

            return true;
        }

        partial void UseProjSpecific(float deltaTime);

        private void Repair(Vector2 rayStart, Vector2 rayEnd, float deltaTime, Character user, float degreeOfSuccess, List<Body> ignoredBodies)
        {
            Body targetBody = Submarine.PickBody(rayStart, rayEnd, ignoredBodies, 
                Physics.CollisionWall | Physics.CollisionCharacter | Physics.CollisionItem | Physics.CollisionLevel | Physics.CollisionRepair, false);

            if (ExtinguishAmount > 0.0f && item.CurrentHull != null)
            {
                List<FireSource> fireSourcesInRange = new List<FireSource>();
                //step along the ray in 10% intervals, collecting all fire sources in the range
                for (float x = 0.0f; x <= Submarine.LastPickedFraction; x += 0.1f)
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
                }
            }

            if (targetBody == null || targetBody.UserData == null) return;

            pickedPosition = Submarine.LastPickedPosition;
            
            if (targetBody.UserData is Structure targetStructure)
            {
                if (!fixableEntities.Contains("structure") && !fixableEntities.Contains(targetStructure.Prefab.Identifier)) return;
                if (targetStructure.IsPlatform) return;

                int sectionIndex = targetStructure.FindSectionIndex(ConvertUnits.ToDisplayUnits(pickedPosition));
                if (sectionIndex < 0) return;

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
            }
            else if (targetBody.UserData is Character targetCharacter)
            {
                Vector2 hitPos = ConvertUnits.ToDisplayUnits(pickedPosition);
                if (targetCharacter.Submarine != null) hitPos += targetCharacter.Submarine.Position;

                targetCharacter.LastDamageSource = item;
                targetCharacter.AddDamage(hitPos,
                    new List<Affliction>() { AfflictionPrefab.Burn.Instantiate(-LimbFixAmount * degreeOfSuccess) }, 0.0f, false, 0.0f, user);
                FixCharacterProjSpecific(user, deltaTime, targetCharacter);
            }
            else if (targetBody.UserData is Limb targetLimb)
            {
                targetLimb.character.LastDamageSource = item;
                targetLimb.character.DamageLimb(targetLimb.WorldPosition, targetLimb, 
                    new List<Affliction>() { AfflictionPrefab.Burn.Instantiate(-LimbFixAmount * degreeOfSuccess) }, 0.0f, false, 0.0f, user);

                FixCharacterProjSpecific(user, deltaTime, targetLimb.character);
            }
            else if (targetBody.UserData is Item targetItem)
            {
                targetItem.IsHighlighted = true;

                float prevCondition = targetItem.Condition;

                ApplyStatusEffectsOnTarget(deltaTime, ActionType.OnUse, targetItem.AllPropertyObjects);

                var levelResource = targetItem.GetComponent<LevelResource>();
                if (levelResource != null && levelResource.IsActive &&
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
                FixItemProjSpecific(user, deltaTime, targetItem, prevCondition);
            }
        }

        partial void FixStructureProjSpecific(Character user, float deltaTime, Structure targetStructure, int sectionIndex);
        partial void FixCharacterProjSpecific(Character user, float deltaTime, Character targetCharacter);
        partial void FixItemProjSpecific(Character user, float deltaTime, Item targetItem, float prevCondition);

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            Gap leak = objective.OperateTarget as Gap;
            if (leak == null) return true;
            
            float dist = Vector2.Distance(leak.WorldPosition, item.WorldPosition);

            //too far away -> consider this done and hope the AI is smart enough to move closer
            if (dist > range * 5.0f) return true;

            Vector2 gapDiff = leak.WorldPosition - item.WorldPosition;
            if (!character.AnimController.InWater && character.AnimController is HumanoidAnimController &&
                Math.Abs(gapDiff.X) < 100.0f && gapDiff.Y < 0.0f && gapDiff.Y > -150.0f)
            {
                ((HumanoidAnimController)character.AnimController).Crouching = true;
            }

            //steer closer if almost in range
            if (dist > range)
            {
                Vector2 standPos = leak.IsHorizontal ?
                    new Vector2(Math.Sign(-gapDiff.X), 0.0f)
                    : new Vector2(0.0f, Math.Sign(-gapDiff.Y) * 0.5f);

                standPos = leak.WorldPosition + standPos * range;

                character.AIController.SteeringManager.SteeringManual(deltaTime, (standPos - character.WorldPosition) / 1000.0f);
            }
            else
            {
                //close enough -> stop moving
                character.AIController.SteeringManager.Reset();
            }

            character.CursorPosition = leak.Position;
            character.SetInput(InputType.Aim, false, true);

            Use(deltaTime, character);

            bool leakFixed = leak.Open <= 0.0f || leak.Removed;

            if (leakFixed && leak.FlowTargetHull != null)
            {
                if (!leak.FlowTargetHull.ConnectedGaps.Any(g => !g.IsRoomToRoom && g.Open > 0.0f))
                {
                    character.Speak(TextManager.Get("DialogLeaksFixed").Replace("[roomname]", leak.FlowTargetHull.RoomName), null, 0.0f, "leaksfixed", 10.0f);
                }
                else
                {
                    character.Speak(TextManager.Get("DialogLeakFixed").Replace("[roomname]", leak.FlowTargetHull.RoomName), null, 0.0f, "leakfixed", 10.0f);
                }
            }

            return leakFixed;
        }

        private void ApplyStatusEffectsOnTarget(float deltaTime, ActionType actionType, List<ISerializableEntity> targets)
        {
            if (statusEffectLists == null) return;

            List<StatusEffect> statusEffects;
            if (!statusEffectLists.TryGetValue(actionType, out statusEffects)) return;

            foreach (StatusEffect effect in statusEffects)
            {
                if (effect.HasTargetType(StatusEffect.TargetType.UseTarget))
                {
                    effect.Apply(actionType, deltaTime, item, targets);
                }
            }
        }
    }
}
