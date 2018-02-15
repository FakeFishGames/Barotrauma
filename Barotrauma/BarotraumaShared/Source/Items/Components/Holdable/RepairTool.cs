using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

#if CLIENT
using Barotrauma.Particles;
#endif

namespace Barotrauma.Items.Components
{
    class RepairTool : ItemComponent
    {
        private readonly List<string> fixableEntities;

        private float range;

        private Vector2 pickedPosition;

        private Vector2 barrelPos;

        private string particles;

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
        public float ExtinquishAmount
        {
            get; set;
        }

#if CLIENT
        public ParticleEmitter ParticleEmitter
        {
            get;
            private set;
        }

        private List<ParticleEmitter> ParticleEmitterHitStructure = new List<ParticleEmitter>();

        private List<ParticleEmitter> ParticleEmitterHitItem = new List<ParticleEmitter>();

        private List<ParticleEmitter> ParticleEmitterHitCharacter = new List<ParticleEmitter>();
#endif

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
                        fixableEntities.Add(subElement.Attribute("name").Value);
                        break;
#if CLIENT
                    case "particleemitter":
                        ParticleEmitter = new ParticleEmitter(subElement);
                        break;
                    case "particleemitterhititem":
                        ParticleEmitterHitItem.Add(new ParticleEmitter(subElement));
                        break;
                    case "particleemitterhitstructure":
                        ParticleEmitterHitStructure.Add(new ParticleEmitter(subElement));
                        break;
                    case "particleemitterhitcharacter":
                        ParticleEmitterHitCharacter.Add(new ParticleEmitter(subElement));
                        break;
#endif
                }
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            activeTimer -= deltaTime;
            if (activeTimer <= 0.0f) IsActive = false;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (character == null) return false;
            if (!character.IsKeyDown(InputType.Aim)) return false;
            
            float degreeOfSuccess = DegreeOfSuccess(character)/100.0f;

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

#if CLIENT
            float particleAngle = item.body.Rotation + ((item.body.Dir > 0.0f) ? 0.0f : MathHelper.Pi);
            ParticleEmitter.Emit(
                deltaTime, item.WorldPosition + TransformedBarrelPos, 
                item.CurrentHull, particleAngle, -particleAngle);
#endif
          
            return true;
        }

        private void Repair(Vector2 rayStart, Vector2 rayEnd, float deltaTime, Character user, float degreeOfSuccess, List<Body> ignoredBodies)
        {
            if (ExtinquishAmount > 0.0f && item.CurrentHull != null)
            {
                Vector2 displayPos = ConvertUnits.ToDisplayUnits(rayStart + (rayEnd - rayStart) * Submarine.LastPickedFraction * 0.9f);

                displayPos += item.CurrentHull.Submarine.Position;

                Hull hull = Hull.FindHull(displayPos, item.CurrentHull);
                if (hull != null)
                {
                    hull.Extinguish(deltaTime, ExtinquishAmount, displayPos);
                    if (hull != item.CurrentHull)
                    {
                        item.CurrentHull.Extinguish(deltaTime, ExtinquishAmount, displayPos);
                    }
                }

            }

            Body targetBody = Submarine.PickBody(rayStart, rayEnd, ignoredBodies, 
                Physics.CollisionWall | Physics.CollisionCharacter | Physics.CollisionItem | Physics.CollisionLevel | Physics.CollisionRepair, false);

            if (targetBody == null || targetBody.UserData == null) return;

            pickedPosition = Submarine.LastPickedPosition;

            Structure targetStructure;
            Limb targetLimb;
            Item targetItem;
            if ((targetStructure = (targetBody.UserData as Structure)) != null)
            {
                if (!fixableEntities.Contains("structure") && !fixableEntities.Contains(targetStructure.Name)) return;
                if (targetStructure.IsPlatform) return;

                int sectionIndex = targetStructure.FindSectionIndex(ConvertUnits.ToDisplayUnits(pickedPosition));
                if (sectionIndex < 0) return;

#if CLIENT
                Vector2 progressBarPos = targetStructure.SectionPosition(sectionIndex);
                if (targetStructure.Submarine != null) progressBarPos += targetStructure.Submarine.DrawPosition;

                var progressBar = user.UpdateHUDProgressBar(
                    targetStructure,
                    progressBarPos,
                    1.0f - targetStructure.SectionDamage(sectionIndex) / targetStructure.Health,
                    Color.Red, Color.Green);

                if (progressBar != null) progressBar.Size = new Vector2(60.0f, 20.0f);

                Vector2 particlePos = ConvertUnits.ToDisplayUnits(pickedPosition);
                if (targetStructure.Submarine != null) particlePos += targetStructure.Submarine.DrawPosition; 
                foreach (var emitter in ParticleEmitterHitStructure)
                {
                    float particleAngle = item.body.Rotation + ((item.body.Dir > 0.0f) ? 0.0f : MathHelper.Pi);
                    emitter.Emit(deltaTime, particlePos, item.CurrentHull, particleAngle + MathHelper.Pi, -particleAngle + MathHelper.Pi);
                }
#endif

                targetStructure.AddDamage(sectionIndex, -StructureFixAmount * degreeOfSuccess,user);

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
            else if ((targetLimb = (targetBody.UserData as Limb)) != null)
            {
                targetLimb.character.DamageLimb(targetLimb.WorldPosition, targetLimb, 
                    new List<Affliction>() { AfflictionPrefab.InternalDamage.Instantiate(-LimbFixAmount * degreeOfSuccess) }, 0.0f, false, 0.0f, user);

#if CLIENT
                Vector2 particlePos = ConvertUnits.ToDisplayUnits(pickedPosition);
                if (targetLimb.character.Submarine != null) particlePos += targetLimb.character.Submarine.DrawPosition; 
                foreach (var emitter in ParticleEmitterHitCharacter)
                {
                    float particleAngle = item.body.Rotation + ((item.body.Dir > 0.0f) ? 0.0f : MathHelper.Pi);
                    emitter.Emit(deltaTime, particlePos, item.CurrentHull, particleAngle + MathHelper.Pi, -particleAngle + MathHelper.Pi);
                }
#endif
            }
            else if ((targetItem = (targetBody.UserData as Item)) != null)
            {
                targetItem.IsHighlighted = true;

                float prevCondition = targetItem.Condition;

                ApplyStatusEffectsOnTarget(deltaTime, ActionType.OnUse, targetItem.AllPropertyObjects);

#if CLIENT
                if (item.Condition != prevCondition)
                {
                    Vector2 progressBarPos = targetItem.DrawPosition;

                    var progressBar = user.UpdateHUDProgressBar(
                        targetItem,
                        progressBarPos,
                        targetItem.Condition / 100.0f,
                        Color.Red, Color.Green);

                    if (progressBar != null) progressBar.Size = new Vector2(60.0f, 20.0f);

                    Vector2 particlePos = ConvertUnits.ToDisplayUnits(pickedPosition);
                    if (targetItem.Submarine != null) particlePos += targetItem.Submarine.DrawPosition; 
                    foreach (var emitter in ParticleEmitterHitItem)
                    {
                        float particleAngle = item.body.Rotation + ((item.body.Dir > 0.0f) ? 0.0f : MathHelper.Pi);
                        emitter.Emit(deltaTime, particlePos, item.CurrentHull, particleAngle + MathHelper.Pi, -particleAngle + MathHelper.Pi);
                    }
                }
#endif
            }
        }
        
        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            Gap leak = objective.OperateTarget as Gap;
            if (leak == null) return true;
            
            float dist = Vector2.Distance(leak.WorldPosition, item.WorldPosition);

            //too far away -> consider this done and hope the AI is smart enough to move closer
            if (dist > range * 5.0f) return true;
            
            //steer closer if almost in range
            if (dist > range)
            {
                Vector2 standPos = leak.IsHorizontal ?
                    new Vector2(Math.Sign(item.WorldPosition.X - leak.WorldPosition.X), 0.0f)
                    : new Vector2(0.0f, Math.Sign(item.WorldPosition.Y - leak.WorldPosition.Y));

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

            return leak.Open <= 0.0f;
        }

        private void ApplyStatusEffectsOnTarget(float deltaTime, ActionType actionType, List<ISerializableEntity> targets)
        {
            if (statusEffectLists == null) return;

            List<StatusEffect> statusEffects;
            if (!statusEffectLists.TryGetValue(actionType, out statusEffects)) return;

            foreach (StatusEffect effect in statusEffects)
            {
                if (effect.Targets.HasFlag(StatusEffect.TargetType.UseTarget))
                {
                    effect.Apply(actionType, deltaTime, item, targets);
                }
            }
        }
    }
}
