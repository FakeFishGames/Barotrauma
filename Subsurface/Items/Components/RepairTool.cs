using System;
using System.Collections.Generic;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Subsurface.Items.Components
{
    class RepairTool : ItemComponent
    {
        List<string> fixableEntities;

        float range;

        Vector2 pickedPosition;

        float structureFixAmount, limbFixAmount;

        [HasDefaultValue(100.0f, false)]
        private float Range
        {
            set { range = ConvertUnits.ToSimUnits(value); }
        }

        [HasDefaultValue(1.0f, false)]
        private float StructureFixAmount
        {
            set { structureFixAmount = value; }
        }

        [HasDefaultValue(1.0f, false)]
        private float LimbFixAmount
        {
            set { limbFixAmount = value; }
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

        public override bool Use(Character character = null)
        {
            if (character == null) return false;

            Vector2 targetPosition = item.body.Position;
            //targetPosition = targetPosition.X, -targetPosition.Y);

            targetPosition += new Vector2(
                (float)Math.Cos(item.body.Rotation) * range,
                (float)Math.Sin(item.body.Rotation) * range) * item.body.Dir;

            List<Body> ignoredBodies = new List<Body>();
            foreach (Limb limb in character.animController.limbs)
            {
                ignoredBodies.Add(limb.body.FarseerBody);
            }


            Body targetBody = Map.PickBody(item.body.Position, targetPosition, ignoredBodies);
            pickedPosition = Map.LastPickedPosition;

            if (targetBody==null || targetBody.UserData==null) return false;

            ApplyStatusEffects(ActionType.OnUse, 1.0f, character);


            Structure targetStructure;
            Limb targetLimb;
            Item targetItem;
            if ((targetStructure = (targetBody.UserData as Structure)) != null)
            {
                if (!fixableEntities.Contains(targetStructure.Name)) return false;

                int sectionIndex = targetStructure.FindSectionIndex(ConvertUnits.ToDisplayUnits(pickedPosition));
                if (sectionIndex < 0) return false;

                targetStructure.HighLightSection(sectionIndex);

                if (character.SecondaryKeyDown.State)
                {
                    targetStructure.AddDamage(sectionIndex, -structureFixAmount);
                    isActive = true;
                }

            }
            else if ((targetLimb = (targetBody.UserData as Limb)) != null)
            {
                if (character.SecondaryKeyDown.State)
                {
                    targetLimb.Damage -= limbFixAmount;
                    isActive = true;
                }
            }
            else if ((targetItem = (targetBody.UserData as Item)) !=null)
            {
                targetItem.Condition -= structureFixAmount;
            }

            return true;
        }
        
        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!isActive) return;

            Vector2 startPos = ConvertUnits.ToDisplayUnits(item.body.Position);
            Vector2 endPos = ConvertUnits.ToDisplayUnits(pickedPosition);
            endPos = new Vector2(endPos.X + Game1.localRandom.Next(-2, 2), endPos.Y + Game1.localRandom.Next(-2, 2));

            GUI.DrawLine(spriteBatch, startPos, endPos, Color.Orange, 0.0f);

            isActive = false;
        }


    }
}
