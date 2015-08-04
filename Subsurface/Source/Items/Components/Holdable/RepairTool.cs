using System;
using System.Collections.Generic;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Subsurface.Particles;

namespace Subsurface.Items.Components
{
    class RepairTool : ItemComponent
    {
        List<string> fixableEntities;

        float range;

        Vector2 pickedPosition;

        Vector2 barrelPos;

        private string particles;

        float structureFixAmount, limbFixAmount;

        [HasDefaultValue(0.0f, false)]
        public float Range
        {
            get { return ConvertUnits.ToDisplayUnits(range); }
            set { range = ConvertUnits.ToSimUnits(value); }
        }

        [HasDefaultValue(0.0f, false)]
        public float StructureFixAmount
        {
            get { return structureFixAmount; }
            set { structureFixAmount = value; }
        }

        [HasDefaultValue(0.0f, false)]
        public float LimbFixAmount
        {
            get { return limbFixAmount; }
            set { limbFixAmount = value; }
        }

        [HasDefaultValue("", false)]
        public string Particles
        {
            get { return particles; }
            set { particles = value; }
        }

        [HasDefaultValue("0.0,0.0", false)]
        public string BarrelPos
        {
            get { return ToolBox.Vector2ToString(ConvertUnits.ToDisplayUnits(barrelPos)); }
            set { barrelPos = ConvertUnits.ToSimUnits(ToolBox.ParseToVector2(value)); }
        }

        public Vector2 TransformedBarrelPos
        {
            get
            {
                Matrix bodyTransform = Matrix.CreateRotationZ(item.body.Rotation);
                Vector2 flippedPos = barrelPos;
                if (item.body.Dir < 0.0f) flippedPos.X = -flippedPos.X;
                return (Vector2.Transform(flippedPos, bodyTransform) + item.body.Position);
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
            if (!character.SecondaryKeyDown.State) return false;

            if (DoesUseFail(character)) return false;

            isActive = true;

            Vector2 targetPosition = item.body.Position;
            //targetPosition = targetPosition.X, -targetPosition.Y);

            targetPosition += new Vector2(
                (float)Math.Cos(item.body.Rotation),
                (float)Math.Sin(item.body.Rotation)) * range * item.body.Dir;

            List<Body> ignoredBodies = new List<Body>();
            foreach (Limb limb in character.AnimController.limbs)
            {
                ignoredBodies.Add(limb.body.FarseerBody);
            }

            Body targetBody = Submarine.PickBody(TransformedBarrelPos, targetPosition, ignoredBodies);
            pickedPosition = Submarine.LastPickedPosition;

            if (targetBody == null || targetBody.UserData == null) return true;

            //ApplyStatusEffects(ActionType.OnUse, 1.0f, character);


            Structure targetStructure;
            Limb targetLimb;
            Item targetItem;
            if ((targetStructure = (targetBody.UserData as Structure)) != null)
            {
                if (!fixableEntities.Contains(targetStructure.Name)) return true;

                int sectionIndex = targetStructure.FindSectionIndex(ConvertUnits.ToDisplayUnits(pickedPosition));
                if (sectionIndex < 0) return true;

                targetStructure.HighLightSection(sectionIndex);

                targetStructure.AddDamage(sectionIndex, -structureFixAmount);

            }
            else if ((targetLimb = (targetBody.UserData as Limb)) != null)
            {
                if (character.SecondaryKeyDown.State)
                {
                    targetLimb.character.Health += limbFixAmount;
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

                //if (character.SecondaryKeyDown.State)
                //{
                //    IPropertyObject propertyObject = targetBody.UserData as IPropertyObject;
                //    if (propertyObject!=null) ApplyStatusEffects(ActionType.OnUse, 1.0f, item.SimPosition, propertyObject);
                //    //isActive = true;
                //}

            return true;
        }
        
        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            //isActive = true;
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing)
        {
            if (!isActive) return;

            //Vector2 particleSpeed =  new Vector2(
            //    (float)Math.Cos(item.body.Rotation),
            //    (float)Math.Sin(item.body.Rotation)) *item.body.Dir * 0.1f;


            if (!string.IsNullOrWhiteSpace(particles))
            {
                Game1.ParticleManager.CreateParticle(particles, TransformedBarrelPos, 
                    -item.body.Rotation + ((item.body.Dir>0.0f) ? 0.0f : MathHelper.Pi), 0.0f);
            }
            
            //Vector2 startPos = ConvertUnits.ToDisplayUnits(item.body.Position);
            //Vector2 endPos = ConvertUnits.ToDisplayUnits(pickedPosition);
            //endPos = new Vector2(endPos.X + Game1.localRandom.Next(-2, 2), endPos.Y + Game1.localRandom.Next(-2, 2));

            //GUI.DrawLine(spriteBatch, startPos, endPos, Color.Orange, 0.0f);

            isActive = false;
        }


    }
}
