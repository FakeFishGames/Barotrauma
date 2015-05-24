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
                openState = (isOpen) ? 1.0f : 0.0f;
            }
        }
        
        public float OpenState
        {
            get { return openState; }
            set 
            {
                if (openState == value) return;
                openState = MathHelper.Clamp(value, 0.0f, 1.0f);



                if (window==null)
                {
                    Rectangle rect = item.Rect;
                    rect.Height = (int)(rect.Height * (1.0f - openState));

                }
                else
                {
                    //Rectangle rect = item.Rect;
                    //rect.Height = (int)(rect.Height * (1.0f - openState));

                    Rectangle rect1 = item.Rect;
                    rect1.Height = -window.Y;

                    rect1.Y += (int)(item.Rect.Height * openState);
                    rect1.Height = Math.Max(rect1.Height - (rect1.Y - item.Rect.Y), 0);
                    rect1.Y = Math.Min(item.Rect.Y, rect1.Y);

                    Rectangle rect2 = item.Rect;
                    rect2.Y = rect2.Y + window.Y - window.Height;
                    
                    rect2.Y += (int)(item.Rect.Height * openState);
                    //rect2.Height = Math.Max(rect2.Height - (rect2.Y - item.Rect.Y), 0);
                    rect2.Y = Math.Min(item.Rect.Y, rect2.Y);
                    rect2.Height = rect2.Y - (item.Rect.Y - (int)(item.Rect.Height * (1.0f - openState)));
                    
                    convexHull.SetVertices(GetConvexHullCorners(rect1));
                    if (convexHull2!=null) convexHull2.SetVertices(GetConvexHullCorners(rect2));
                }

            }
        }

        PhysicsBody body;

        Sprite doorSprite;

        public Door(Item item, XElement element)
            : base(item, element)
        {
            //Vector2 position = new Vector2(newRect.X, newRect.Y);
            
           // isOpen = false;
            
            body = new PhysicsBody(BodyFactory.CreateRectangle(Game1.world,
                ConvertUnits.ToSimUnits(Math.Max(item.Rect.Width, 1)),
                ConvertUnits.ToSimUnits(Math.Max(item.Rect.Height, 1)),
                1.5f));
            
            body.BodyType = BodyType.Static;
            body.SetTransform(
                ConvertUnits.ToSimUnits(new Vector2(item.Rect.X + item.Rect.Width / 2, item.Rect.Y - item.Rect.Height / 2)),
                0.0f);
            body.Friction = 0.5f;


            //string spritePath = Path.GetDirectoryName(item.Prefab.ConfigFile) + "\\"+ ToolBox.GetAttributeString(element, "sprite", "");

                        foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLower() != "sprite") continue;
                doorSprite = new Sprite(subElement, Path.GetDirectoryName(item.Prefab.ConfigFile));
                break;
            }

            isActive = true;

            Vector2[] corners = GetConvexHullCorners(item.Rect);

            convexHull = new ConvexHull(corners, Color.Black);            
            if (window!=null) convexHull2 = new ConvexHull(corners, Color.Black);

            OpenState = openState;
            //powerConsumption = -100.0f;

            //LinkedGap.Open = openState;
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

            body.SetTransform(body.Position + ConvertUnits.ToSimUnits(amount), 0.0f);

            convexHull.Move(amount);
        }


        public override bool Pick(Character picker)
        {
            isActive = true;
            isOpen = !isOpen;

            return true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            OpenState += deltaTime * ((isOpen) ? 3.0f : -3.0f);

            item.SendSignal((isOpen) ? "1" : "0", "state_out");
        }
        
        public override void Draw(SpriteBatch spriteBatch)
        {           

            LinkedGap.Open = openState;

            Color color = (item.IsSelected) ? Color.Green : Color.White;

            //prefab.sprite.Draw(spriteBatch, new Vector2(rect.X, -rect.Y), new Vector2(rect.Width, rect.Height), color);

            if (openState == 1.0f)
            {
                body.Enabled = false;
                convexHull.Enabled = false;
            }
            else
            {
                spriteBatch.Draw(doorSprite.Texture, new Vector2(item.Rect.Center.X, -item.Rect.Y),
                    new Rectangle(0, (int)(doorSprite.size.Y * openState),
                    (int)doorSprite.size.X, (int)(doorSprite.size.Y * (1.0f - openState))),
                    color, 0.0f, doorSprite.Origin, 1.0f, SpriteEffects.None, doorSprite.Depth);

                convexHull.Enabled = true;

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

                    foreach (Character c in Character.characterList)
                    {
                        int dir = Math.Sign(c.animController.limbs[0].SimPosition.X - simPos.X);
                        foreach (Limb l in c.animController.limbs)
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

            Game1.world.RemoveBody(body.FarseerBody);

            doorSprite.Remove();

            convexHull.Remove();
            if (convexHull2 != null) convexHull2.Remove();
        }

        public override void ReceiveSignal(string signal, Connection connection, Item sender)
        {
            isActive = true;
            if (connection.name=="toggle")
            {
                isOpen = !isOpen;
            }
            else if (connection.name == "set_state")
            {
                isOpen = (signal!="0");                
            }            
        }
    }
}
