using System;
using System.IO;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Subsurface.Lights;

namespace Subsurface.Items.Components
{
    class Door : ItemComponent
    {
        Gap linkedGap;

        Rectangle window;

        ConvexHull convexHull;
        ConvexHull convexHull2;

        private float stuck;
        public float Stuck
        {
            get { return stuck; }
            set 
            {
                if (isOpen) return;
                stuck = MathHelper.Clamp(value, 0.0f, 100.0f);
                if (stuck == 0.0f) isStuck = false;
                if (stuck == 100.0f) isStuck = true;
            }
        }

        private bool isStuck;

        Gap LinkedGap
        {
            get
            {
                if (linkedGap != null) return linkedGap;
                foreach (MapEntity e in item.linkedTo)
                {
                    linkedGap = e as Gap;
                    if (linkedGap != null) return linkedGap;
                }
                linkedGap = new Gap(item.Rect);
                linkedGap.Open = openState;
                item.linkedTo.Add(linkedGap);
                return linkedGap;
            }
        }
        
        bool isOpen;
        
        float openState;

        [HasDefaultValue("0.0,0.0,0.0,0.0", false)]
        public string Window
        {
            get { return ToolBox.Vector4ToString(new Vector4(window.X, window.Y, window.Width, window.Height)); }
            set
            {
                Vector4 vector = ToolBox.ParseToVector4(value);
                if (vector.Z!=0.0f || vector.W !=0.0f)
                {
                    window = new Rectangle((int)vector.X, (int)vector.Y, (int)vector.Z, (int)vector.W);
                }
            }
        }

        [Editable, HasDefaultValue(false, true)]
        public bool IsOpen
        {
            get { return isOpen; }
            set 
            {
                isOpen = value;
                OpenState = (isOpen) ? 1.0f : 0.0f;
            }
        }

        private Rectangle doorRect;
                
        public float OpenState
        {
            get { return openState; }
            set 
            {
                
                float prevValue = openState;
                openState = MathHelper.Clamp(value, 0.0f, 1.0f);
                if (openState == prevValue) return;

                UpdateConvexHulls();
            }
        }

        PhysicsBody body;

        Sprite doorSprite, weldedSprite;
        
        public Door(Item item, XElement element)
            : base(item, element)
        {
            //Vector2 position = new Vector2(newRect.X, newRect.Y);
            
           // isOpen = false;
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "sprite":
                        doorSprite = new Sprite(subElement, Path.GetDirectoryName(item.Prefab.ConfigFile));
                        break;
                    case "weldedsprite":
                        weldedSprite = new Sprite(subElement, Path.GetDirectoryName(item.Prefab.ConfigFile));
                        break;
                }
            }

            doorRect = new Rectangle(
                item.Rect.Center.X - (int)(doorSprite.size.X / 2),
                item.Rect.Y,
                (int)doorSprite.size.X,
                (int)doorSprite.size.Y);

            body = new PhysicsBody(BodyFactory.CreateRectangle(GameMain.World,
                ConvertUnits.ToSimUnits(Math.Max(doorRect.Width, 1)),
                ConvertUnits.ToSimUnits(Math.Max(doorRect.Height, 1)),
                1.5f));

            body.CollisionCategories = Physics.CollisionWall;
            body.UserData = item;
            body.BodyType = BodyType.Static;
            body.SetTransform(
                ConvertUnits.ToSimUnits(new Vector2(doorRect.Center.X, doorRect.Y - doorRect.Height / 2)),
                0.0f);
            body.Friction = 0.5f;


            //string spritePath = Path.GetDirectoryName(item.Prefab.ConfigFile) + "\\"+ ToolBox.GetAttributeString(element, "sprite", "");
            
            Vector2[] corners = GetConvexHullCorners(doorRect);

            convexHull = new ConvexHull(corners, Color.Black);            
            if (window!=Rectangle.Empty) convexHull2 = new ConvexHull(corners, Color.Black);

            UpdateConvexHulls();

            IsActive = true;
        }

        private void UpdateConvexHulls()
        {
            Rectangle rect = doorRect;

            rect.Height = (int)(rect.Height * (1.0f - openState));
            if (window.Height == 0 || window.Width == 0)
            {

            }
            else
            {
                //Rectangle rect = item.Rect;
                //rect.Height = (int)(rect.Height * (1.0f - openState));

                rect.Height = -window.Y;

                rect.Y += (int)(doorRect.Height * openState);
                rect.Height = Math.Max(rect.Height - (rect.Y - doorRect.Y), 0);
                rect.Y = Math.Min(doorRect.Y, rect.Y);


                if (convexHull2 != null)
                {
                    Rectangle rect2 = doorRect;
                    rect2.Y = rect2.Y + window.Y - window.Height;

                    rect2.Y += (int)(doorRect.Height * openState);
                    rect2.Y = Math.Min(doorRect.Y, rect2.Y);
                    rect2.Height = rect2.Y - (doorRect.Y - (int)(doorRect.Height * (1.0f - openState)));
                    //convexHull2.SetVertices(GetConvexHullCorners(rect2));

                    if (rect2.Height == 0)
                    {
                        convexHull2.Enabled = false;
                    }
                    else
                    {
                        convexHull2.Enabled = true;
                        convexHull2.SetVertices(GetConvexHullCorners(rect2));
                    }
                }
            }

            if (rect.Height == 0)
            {
                convexHull.Enabled = false;
            }
            else
            {
                convexHull.Enabled = true;
                convexHull.SetVertices(GetConvexHullCorners(rect));
            }
        }

        private Vector2[] GetConvexHullCorners(Rectangle rect)
        {
            Vector2[] corners = new Vector2[4];
            corners[0] = new Vector2(rect.X, rect.Y - rect.Height);
            corners[1] = new Vector2(rect.X, rect.Y);
            corners[2] = new Vector2(rect.Right, rect.Y);
            corners[3] = new Vector2(rect.Right, rect.Y - rect.Height);

            return corners;
        }

        public override void Move(Vector2 amount)
        {
            base.Move(amount);

            //LinkedGap.Move(amount);

            body.SetTransform(body.SimPosition + ConvertUnits.ToSimUnits(amount), 0.0f);

            convexHull.Move(amount);
            if (convexHull2 != null) convexHull2.Move(amount);
        }


        public override bool Pick(Character picker)
        {
            isOpen = !isOpen;

            return true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (!isStuck)
            {
                OpenState += deltaTime * ((isOpen) ? 2.0f : -1.0f);
                LinkedGap.Open = openState;
            }

            
            item.SendSignal((isOpen) ? "1" : "0", "state_out");
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            body.Enabled = false;
            //convexHull.Enabled = false;
            linkedGap.Open = 1.0f;
            //if (convexHull2 != null) convexHull2.Enabled = false;
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing)
        {           
            Color color = (item.IsSelected) ? Color.Green : Color.White;
            color = color * (item.Condition / 100.0f);
            color.A = 255;

            //prefab.sprite.Draw(spriteBatch, new Vector2(rect.X, -rect.Y), new Vector2(rect.Width, rect.Height), color);

            if (stuck>0.0f && weldedSprite!=null)
            {
                weldedSprite.Draw(spriteBatch, new Vector2(item.Rect.X, -item.Rect.Y), Color.White*(stuck/100.0f), 0.0f, 1.0f);
            }

            if (openState == 1.0f)
            {
                body.Enabled = false;
            }
            else
            {
                spriteBatch.Draw(doorSprite.Texture, new Vector2(item.Rect.Center.X, -item.Rect.Y),
                    new Rectangle(doorSprite.SourceRect.X, (int)(doorSprite.size.Y * openState),
                    (int)doorSprite.size.X, (int)(doorSprite.size.Y * (1.0f - openState))),
                    color, 0.0f, doorSprite.Origin, 1.0f, SpriteEffects.None, doorSprite.Depth);

                if (openState == 0.0f)
                {
                    body.Enabled = true;
                }
                else
                {
                    //push characters out of the doorway when the door is closing/opening
                    Vector2 simPos = ConvertUnits.ToSimUnits(new Vector2(item.Rect.X, item.Rect.Y));
                    Vector2 simSize = ConvertUnits.ToSimUnits(new Vector2(item.Rect.Width,
                    item.Rect.Height * (1.0f - openState)));

                    foreach (Character c in Character.CharacterList)
                    {
                        int dir = Math.Sign(c.AnimController.Limbs[0].SimPosition.X - simPos.X);
                        foreach (Limb l in c.AnimController.Limbs)
                        {
                            if (l.SimPosition.Y < simPos.Y || l.SimPosition.Y > simPos.Y - simSize.Y) continue;
                            if (Math.Abs(l.SimPosition.X - simPos.X) > simSize.X * 2.0f) continue;

                            l.body.ApplyForce(new Vector2(dir * 10.0f, 0.0f));
                        }
                    }
                }
            }
        }

        public override void Remove()
        {
            base.Remove();

            GameMain.World.RemoveBody(body.FarseerBody);

            if (linkedGap!=null) linkedGap.Remove();

            doorSprite.Remove();

            convexHull.Remove();
            if (convexHull2 != null) convexHull2.Remove();
        }

        public override void ReceiveSignal(string signal, Connection connection, Item sender, float power=0.0f)
        {
            if (isStuck) return;

            if (connection.Name=="toggle")
            {
                isOpen = !isOpen;
                PlaySound(ActionType.OnUse, item.Position);
            }
            else if (connection.Name == "set_state")
            {
                bool newState = (signal!="0");
                if (isOpen!=newState) PlaySound(ActionType.OnUse, item.Position);
                isOpen = newState;             
            }

            //opening a partially stuck door makes it less stuck
            if (isOpen) stuck = MathHelper.Clamp(stuck-30.0f, 0.0f, 100.0f); ;
        }
    }
}
