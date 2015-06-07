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

        //[Initable(new Vector2(0.0f, 0.0f))]
        public Vector2 BarrelPos
        {
            get { return new Vector2(barrelPos.X * item.body.Dir, barrelPos.Y); }
        }

        public Vector2 TransformedBarrelPos
        {
            get
            {
                Matrix bodyTransform = Matrix.CreateRotationZ(item.body.Rotation);
                return (Vector2.Transform(BarrelPos, bodyTransform) + item.body.Position);
            }
        }
                
        public RangedWeapon(Item item, XElement element)
            : base(item, element)
        {
            barrelPos = ToolBox.GetAttributeVector2(element, "barrelpos", Vector2.Zero);
            barrelPos = ConvertUnits.ToSimUnits(barrelPos);
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
        
        public override bool Use(Character character = null)
        {
            if (character == null) return false;
            if (!character.SecondaryKeyDown.State || reload > 0.0f) return false;
            isActive = true;
            reload = 1.0f;
            
            List<Body> limbBodies = new List<Body>();
            foreach (Limb l in character.animController.limbs)
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

                projectileComponent.ignoredBodies = limbBodies;

                projectile.body.ResetDynamics();
                projectile.SetTransform(TransformedBarrelPos, 
                    (item.body.Dir == 1.0f) ? item.body.Rotation : item.body.Rotation - MathHelper.Pi);

                projectile.Use();
                item.RemoveContained(projectile);
                
                //recoil
                item.body.ApplyLinearImpulse(
                    new Vector2((float)Math.Cos(projectile.body.Rotation), (float)Math.Sin(projectile.body.Rotation)) * item.body.Mass);

                Rope rope = item.GetComponent<Rope>();
                if (rope != null) rope.Attach(projectile);

                return true;
            }


            return false;
      
        }
    
    }
}
