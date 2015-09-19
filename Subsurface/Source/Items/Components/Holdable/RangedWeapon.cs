using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;

namespace Subsurface.Items.Components
{
    class RangedWeapon : ItemComponent
    {
        private float reload;

        private Vector2 barrelPos;

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
                return (Vector2.Transform(flippedPos, bodyTransform) + item.body.SimPosition);
            }
        }
                
        public RangedWeapon(Item item, XElement element)
            : base(item, element)
        {
            //barrelPos = ToolBox.GetAttributeVector2(element, "barrelpos", Vector2.Zero);
            //barrelPos = ConvertUnits.ToSimUnits(barrelPos);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            reload -= deltaTime;

            if (reload < 0.0f)
            {
                reload = 0.0f;
                isActive = false;
            }
        }
        
        public override bool Use(float deltaTime, Character character = null)
        {
            if (character == null) return false;
            if (!character.GetInputState(InputType.SecondaryHeld) || reload > 0.0f) return false;
            isActive = true;
            reload = 1.0f;
            
            bool failed = DoesUseFail(character);

            List<Body> limbBodies = new List<Body>();
            foreach (Limb l in character.AnimController.Limbs)
            {
                limbBodies.Add(l.body.FarseerBody);
            }

            Item[] containedItems = item.ContainedItems;
            if (containedItems == null || !containedItems.Any()) return false;

            foreach (Item projectile in containedItems)
            {
                if (projectile == null) continue;
                //find the projectile-itemcomponent of the projectile,
                //and add the limbs of the shooter to the list of bodies to be ignored
                //so that the player can't shoot himself
                Projectile projectileComponent= projectile.GetComponent<Projectile>();
                if (projectileComponent == null) continue;

                projectile.body.ResetDynamics();
                projectile.SetTransform(TransformedBarrelPos, 
                    (item.body.Dir == 1.0f) ? item.body.Rotation : item.body.Rotation - MathHelper.Pi);

                projectile.Use(deltaTime);


                if (failed)
                {
                    Vector2 modifiedVelocity = projectile.body.LinearVelocity;
                    modifiedVelocity.X *= Rand.Range(0.0f, 0.5f);
                    modifiedVelocity.Y *= Rand.Range(0.0f, 0.5f);

                    projectile.body.LinearVelocity = modifiedVelocity;
                    projectile.body.ApplyTorque(projectile.body.Mass * Rand.Range(-10.0f, 10.0f));

                    //recoil
                    //item.body.ApplyLinearImpulse(
                    //    new Vector2((float)Math.Cos(projectile.body.Rotation), (float)Math.Sin(projectile.body.Rotation)) * item.body.Mass * -10.0f);
                }
                else
                {
                    projectileComponent.ignoredBodies = limbBodies;

                    //recoil
                    //item.body.ApplyLinearImpulse(
                    //    new Vector2((float)Math.Cos(projectile.body.Rotation), (float)Math.Sin(projectile.body.Rotation)) * -item.body.Mass);
                }

                item.RemoveContained(projectile);
                


                Rope rope = item.GetComponent<Rope>();
                if (rope != null) rope.Attach(projectile);

                return true;
            }


            return false;
      
        }
    
    }
}
