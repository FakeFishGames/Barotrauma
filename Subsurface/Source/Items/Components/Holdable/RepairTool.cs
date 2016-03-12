using System;
using System.Collections.Generic;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    class RepairTool : ItemComponent
    {
        private List<string> fixableEntities;

        private float range;

        private Vector2 pickedPosition;

        private Vector2 barrelPos;

        private string particles;

        [HasDefaultValue(0.0f, false)]
        public float Range
        {
            get { return range; }
            set { range = value; }
        }

        [HasDefaultValue(0.0f, false)]
        public float StructureFixAmount
        {
            get; set;
        }

        [HasDefaultValue(0.0f, false)]
        public float LimbFixAmount
        {
            get; set;
        }
        [HasDefaultValue(0.0f, false)]
        public float ExtinquishAmount
        {
            get; set;
        }

        [HasDefaultValue("", false)]
        public string Particles
        {
            get { return particles; }
            set { particles = value; }
        }

        [HasDefaultValue(0.0f, false)]
        public float ParticleSpeed
        {
            get; set;
        }

        [HasDefaultValue("0.0,0.0", false)]
        public string BarrelPos
        {
            get { return ToolBox.Vector2ToString(barrelPos); }
            set { barrelPos = ToolBox.ParseToVector2(value); }
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

            //range = ToolBox.GetAttributeFloat(element, "range", 100.0f);
            //range = ConvertUnits.ToSimUnits(range);

            //structureFixAmount = ToolBox.GetAttributeFloat(element, "structurefixamount", 1.0f);
            //limbFixAmount = ToolBox.GetAttributeFloat(element, "limbfixamount", -0.5f);


            fixableEntities = new List<string>();
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "fixable":
                        fixableEntities.Add(subElement.Attribute("name").Value);
                        break;
                }
            }
        }

        //public override void Update(float deltaTime, Camera cam)
        //{
        //    base.Update(deltaTime, cam);

        //}

        public override bool Use(float deltaTime, Character character = null)
        {
            if (character == null) return false;
            if (!character.IsKeyDown(InputType.Aim)) return false;

            //if (DoesUseFail(Character)) return false;

            IsActive = true;

            //targetPosition = targetPosition.X, -targetPosition.Y);

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

            Vector2 rayStart = item.WorldPosition;
            Vector2 rayEnd = targetPosition;

            Body targetBody = Submarine.PickBody(
                ConvertUnits.ToSimUnits(rayStart - Submarine.Loaded.Position), 
                ConvertUnits.ToSimUnits(rayEnd - Submarine.Loaded.Position), ignoredBodies);

            pickedPosition = Submarine.LastPickedPosition;

            if (ExtinquishAmount > 0.0f)
            {
                Vector2 displayPos = rayStart + (rayEnd-rayStart)*Submarine.LastPickedFraction*0.9f;
                Hull hull = Hull.FindHull(displayPos, item.CurrentHull);
                if (hull != null) hull.Extinquish(deltaTime, ExtinquishAmount, displayPos);
            }

            if (targetBody == null || targetBody.UserData == null) return true;

            Structure targetStructure;
            Limb targetLimb;
            Item targetItem;
            if ((targetStructure = (targetBody.UserData as Structure)) != null)
            {
                if (!fixableEntities.Contains(targetStructure.Name)) return true;

                int sectionIndex = targetStructure.FindSectionIndex(ConvertUnits.ToDisplayUnits(pickedPosition));
                if (sectionIndex < 0) return true;

                targetStructure.HighLightSection(sectionIndex);

                targetStructure.AddDamage(sectionIndex, -StructureFixAmount*degreeOfSuccess);

                //if the next section is small enough, apply the effect to it as well
                //(to make it easier to fix a small "left-over" section)
                for (int i = -1; i<2; i+=2)
                {
                    int nextSectionLength = targetStructure.SectionLength(sectionIndex + i);
                    if ((sectionIndex==1 && i ==-1) ||
                        (sectionIndex==targetStructure.SectionCount-2 && i == 1) || 
                        (nextSectionLength > 0 && nextSectionLength < Structure.wallSectionSize * 0.3f))
                    {
                        targetStructure.HighLightSection(sectionIndex + i);
                        targetStructure.AddDamage(sectionIndex + i, -StructureFixAmount * degreeOfSuccess);
                    }
                }


            }
            else if ((targetLimb = (targetBody.UserData as Limb)) != null)
            {
                if (character.IsKeyDown(InputType.Aim))
                {
                    targetLimb.character.AddDamage(CauseOfDeath.Damage, -LimbFixAmount * degreeOfSuccess, character);
                    //isActive = true;
                }
            }
            else if ((targetItem = (targetBody.UserData as Item)) != null)
            {
                //targetItem.Condition -= structureFixAmount;
                targetItem.IsHighlighted = true;

                foreach (StatusEffect effect in statusEffects)
                {
                    //if (Array.IndexOf(effect.TargetNames, targetItem.Name) == -1) continue;
                    effect.Apply(ActionType.OnUse, deltaTime, item, targetItem.AllPropertyObjects);
                    //targetItem.ApplyStatusEffect(effect, ActionType.OnUse, deltaTime);
                }
                //ApplyStatusEffects(ActionType.OnUse, 1.0f, null, targ);
            }
            //if (Character.SecondaryKeyDown.State)
            //{
            //    IPropertyObject propertyObject = targetBody.UserData as IPropertyObject;
            //    if (propertyObject!=null) ApplyStatusEffects(ActionType.OnUse, 1.0f, item.SimPosition, propertyObject);
            //    //isActive = true;
            //}

            return true;
        }

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            Gap leak = objective.OperateTarget as Gap;
            if (leak == null) return true;

            if (Vector2.Distance(leak.WorldPosition, item.WorldPosition) > range) return true;

            character.CursorPosition = leak.Position;
            character.SetInput(InputType.Aim, false, true);

            Use(deltaTime, character);

            return leak.Open <= 0.0f;

        }
        
        public override void Draw(SpriteBatch spriteBatch, bool editing)
        {
            if (!IsActive) return;

            //Vector2 particleSpeed =  new Vector2(
            //    (float)Math.Cos(item.body.Rotation),
            //    (float)Math.Sin(item.body.Rotation)) *item.body.Dir * 0.1f;


            if (!string.IsNullOrWhiteSpace(particles))
            {
                GameMain.ParticleManager.CreateParticle(particles, item.WorldPosition+TransformedBarrelPos, 
                    -item.body.Rotation + ((item.body.Dir>0.0f) ? 0.0f : MathHelper.Pi), ParticleSpeed);
            }
            
            //Vector2 startPos = ConvertUnits.ToDisplayUnits(item.body.Position);
            //Vector2 endPos = ConvertUnits.ToDisplayUnits(pickedPosition);
            //endPos = new Vector2(endPos.X + Game1.localRandom.Next(-2, 2), endPos.Y + Game1.localRandom.Next(-2, 2));

            //GUI.DrawLine(spriteBatch, startPos, endPos, Color.Orange, 0.0f);

            IsActive = false;
        }


    }
}
