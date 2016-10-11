using System;
using System.IO;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Lights;

namespace Barotrauma.Items.Components
{
    class Door : ItemComponent, IDrawableComponent
    {
        private Gap linkedGap;

        private Rectangle window;

        private ConvexHull convexHull;
        private ConvexHull convexHull2;

        private bool isOpen;

        private float openState;

        private PhysicsBody body;

        private Sprite doorSprite, weldedSprite;

        private bool isHorizontal;

        private bool isStuck;

        private float lastReceivedMessage;

        public PhysicsBody Body
        {
            get { return body; }
        }

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

        public Gap LinkedGap
        {
            get
            {
                if (linkedGap != null) return linkedGap;

                foreach (MapEntity e in item.linkedTo)
                {
                    linkedGap = e as Gap;
                    if (linkedGap != null)
                    {
                        linkedGap.PassAmbientLight = window != Rectangle.Empty;
                        return linkedGap;
                    }
                }
                Rectangle rect = item.Rect;
                if (isHorizontal)
                {
                    rect.Y += 5;
                    rect.Height += 10;
                }
                else
                {
                    rect.X -= 5;
                    rect.Width += 10;
                }

                linkedGap = new Gap(rect, Item.Submarine);
                linkedGap.Submarine = item.Submarine;
                linkedGap.PassAmbientLight = window != Rectangle.Empty;
                linkedGap.Open = openState;
                item.linkedTo.Add(linkedGap);
                return linkedGap;
            }
        }
        
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

        public Rectangle WindowRect
        {
            get { return window; }
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
        
        public Door(Item item, XElement element)
            : base(item, element)
        {
            //Vector2 position = new Vector2(newRect.X, newRect.Y);

            isHorizontal = ToolBox.GetAttributeBool(element, "horizontal", false);

           // isOpen = false;
            foreach (XElement subElement in element.Elements())
            {
                string texturePath = ToolBox.GetAttributeString(subElement, "texture", "");
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        doorSprite = new Sprite(subElement, texturePath.Contains("/") ? "" : Path.GetDirectoryName(item.Prefab.ConfigFile));
                        break;
                    case "weldedsprite":
                        weldedSprite = new Sprite(subElement, texturePath.Contains("/") ? "" : Path.GetDirectoryName(item.Prefab.ConfigFile));
                        break;
                }
            }

            doorRect = new Rectangle(
                item.Rect.Center.X - (int)(doorSprite.size.X / 2),
                item.Rect.Y - item.Rect.Height/2 + (int)(doorSprite.size.Y / 2.0f),
                (int)doorSprite.size.X,
                (int)doorSprite.size.Y);

            body = new PhysicsBody(
                ConvertUnits.ToSimUnits(Math.Max(doorRect.Width, 1)),
                ConvertUnits.ToSimUnits(Math.Max(doorRect.Height, 1)),
                0.0f,
                1.5f);

            body.UserData = item;
            body.CollisionCategories = Physics.CollisionWall;
            body.BodyType = BodyType.Static;
            body.SetTransform(
                ConvertUnits.ToSimUnits(new Vector2(doorRect.Center.X, doorRect.Y - doorRect.Height / 2)),
                0.0f);
            body.Friction = 0.5f;


            //string spritePath = Path.GetDirectoryName(item.Prefab.ConfigFile) + "\\"+ ToolBox.GetAttributeString(element, "sprite", "");
            
            IsActive = true;
        }

        private void UpdateConvexHulls()
        {
            doorRect = new Rectangle(
                item.Rect.Center.X - (int)(doorSprite.size.X / 2),
                item.Rect.Y - item.Rect.Height / 2 + (int)(doorSprite.size.Y / 2.0f),
                (int)doorSprite.size.X,
                (int)doorSprite.size.Y);

            Rectangle rect = doorRect;
            if (isHorizontal)
            {
                rect.Width = (int)(rect.Width * (1.0f - openState));
            }
            else
            {
                rect.Height = (int)(rect.Height * (1.0f - openState));
            }

            if (window.Height > 0 && window.Width > 0)
            {
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

            if (convexHull == null) return;

            if (rect.Height == 0 || rect.Width == 0)
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

            UpdateConvexHulls();

            //convexHull.Move(amount);
            //if (convexHull2 != null) convexHull2.Move(amount);
        }


        public override bool Pick(Character picker)
        {
            isOpen = !isOpen;

            return true;
        }

        public override bool Select(Character character)
        {
            //can only be selected if the item is broken
            return item.Condition <= 0.0f;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (!isStuck)
            {
                OpenState += deltaTime * ((isOpen) ? 2.0f : -2.0f);
                LinkedGap.Open = openState;
            }
            
            if (openState > 0.0f && openState < 1.0f && !isOpen)
            {
                PushCharactersAway();
            }
            else
            {

                body.Enabled = openState < 1.0f;
            }

            
            item.SendSignal(0, (isOpen) ? "1" : "0", "state_out");
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            body.Enabled = false;

            linkedGap.Open = 1.0f;
        }
        
        public void Draw(SpriteBatch spriteBatch, bool editing)
        {           
            Color color = (item.IsSelected) ? Color.Green : Color.White;
            color = color * (item.Condition / 100.0f);
            color.A = 255;

            //prefab.sprite.Draw(spriteBatch, new Vector2(rect.X, -rect.Y), new Vector2(rect.Width, rect.Height), color);

            if (stuck>0.0f && weldedSprite!=null)
            {
                Vector2 weldSpritePos = new Vector2(item.Rect.Center.X, item.Rect.Y-item.Rect.Height/2.0f);
                if (item.Submarine != null) weldSpritePos += item.Submarine.Position;
                weldSpritePos.Y = -weldSpritePos.Y;

                weldedSprite.Draw(spriteBatch,
                    weldSpritePos, Color.White*(stuck/100.0f), 0.0f, 1.0f);
            }

            if (openState == 1.0f)
            {
                body.Enabled = false;
                return;
            }



            if (isHorizontal)
            {
                Vector2 pos = new Vector2(item.Rect.X, item.Rect.Y - item.Rect.Height/2);
                if (item.Submarine != null) pos += item.Submarine.DrawPosition;
                pos.Y = -pos.Y;

                spriteBatch.Draw(doorSprite.Texture, pos,
                    new Rectangle((int)(doorSprite.SourceRect.X + doorSprite.size.X * openState), (int)doorSprite.SourceRect.Y, 
                     (int)(doorSprite.size.X * (1.0f - openState)),(int)doorSprite.size.Y),
                    color, 0.0f, doorSprite.Origin, 1.0f, SpriteEffects.None, doorSprite.Depth);  
            }
            else
            {
                Vector2 pos = new Vector2(item.Rect.Center.X, item.Rect.Y);
                if (item.Submarine != null) pos += item.Submarine.DrawPosition;
                pos.Y = -pos.Y;

                spriteBatch.Draw(doorSprite.Texture, pos,
                    new Rectangle(doorSprite.SourceRect.X, (int)(doorSprite.SourceRect.Y + doorSprite.size.Y * openState),
                    (int)doorSprite.size.X, (int)(doorSprite.size.Y * (1.0f - openState))),
                    color, 0.0f, doorSprite.Origin, 1.0f, SpriteEffects.None, doorSprite.Depth);  
            }
          
        }

        public override void OnMapLoaded()
        {
            LinkedGap.ConnectedDoor = this;
            LinkedGap.Open = openState;

            Vector2[] corners = GetConvexHullCorners(doorRect);

            convexHull = new ConvexHull(corners, Color.Black, item);
            if (window != Rectangle.Empty) convexHull2 = new ConvexHull(corners, Color.Black, item);

            UpdateConvexHulls();
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();

            GameMain.World.RemoveBody(body.FarseerBody);

            if (linkedGap!=null) linkedGap.Remove();

            doorSprite.Remove();
            if (weldedSprite != null) weldedSprite.Remove();

            if (convexHull!=null) convexHull.Remove();
            if (convexHull2 != null) convexHull2.Remove();
        }

        private void PushCharactersAway()
        {
            //push characters out of the doorway when the door is closing/opening
            Vector2 simPos = ConvertUnits.ToSimUnits(new Vector2(item.Rect.X, item.Rect.Y));

            Vector2 currSize = isHorizontal ?
                new Vector2(item.Rect.Width * (1.0f - openState), doorSprite.size.Y) :
                new Vector2(doorSprite.size.X, item.Rect.Height * (1.0f - openState));

            Vector2 simSize = ConvertUnits.ToSimUnits(currSize);

            foreach (Character c in Character.CharacterList)
            {
                int dir = isHorizontal ? Math.Sign(c.SimPosition.Y - item.SimPosition.Y) : Math.Sign(c.SimPosition.X - item.SimPosition.X);

                foreach (Limb l in c.AnimController.Limbs)
                {
                    float diff = 0.0f;

                    if (isHorizontal)
                    {
                        if (l.SimPosition.X < simPos.X || l.SimPosition.X > simPos.X + simSize.X) continue;

                        diff = l.SimPosition.Y - item.SimPosition.Y;
                    }
                    else
                    {
                        if (l.SimPosition.Y > simPos.Y || l.SimPosition.Y < simPos.Y - simSize.Y) continue;

                        diff = l.SimPosition.X - item.SimPosition.X;
                    }
                   
                    if (Math.Sign(diff) != dir)
                    {
                        SoundPlayer.PlayDamageSound(DamageSoundType.LimbBlunt, 1.0f, l.body);

                        if (isHorizontal)
                        {
                            l.body.SetTransform(new Vector2(l.SimPosition.X, item.SimPosition.Y + dir * simSize.Y * 2.0f), l.body.Rotation);
                            l.body.ApplyLinearImpulse(new Vector2(isOpen ? 0.0f : 1.0f, dir * 2.0f));
                        }
                        else
                        {
                            l.body.SetTransform(new Vector2(item.SimPosition.X + dir * simSize.X * 1.2f, l.SimPosition.Y), l.body.Rotation);
                            l.body.ApplyLinearImpulse(new Vector2(dir * 0.5f, isOpen ? 0.0f : -1.0f));
                        }
                    }

                    if (isHorizontal)
                    {
                        if (Math.Abs(l.SimPosition.Y - item.SimPosition.Y) > simSize.Y * 0.5f) continue;

                        l.body.ApplyLinearImpulse(new Vector2(isOpen ? 0.0f : 1.0f, dir * 0.5f));
                    }
                    else
                    {
                        if (Math.Abs(l.SimPosition.X - item.SimPosition.X) > simSize.X * 0.5f) continue;

                        l.body.ApplyLinearImpulse(new Vector2(dir * 0.5f, isOpen ? 0.0f : -1.0f));
                    }

                    c.StartStun(0.2f);
                }
            }
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item sender, float power=0.0f)
        {
            if (isStuck || GameMain.Client != null) return;

            if (connection.Name=="toggle")
            {
                SetState(!isOpen, false, true);
            }
            else if (connection.Name == "set_state")
            {
                SetState(signal != "0", false, true);
            }
        }

        public void SetState(bool open, bool isNetworkMessage, bool sendNetworkMessage = false)
        {
            if (GameMain.Client != null && !isNetworkMessage) return;

            if (isStuck || isOpen == open) return;

            PlaySound(ActionType.OnUse, item.WorldPosition);

            isOpen = open;

            //opening a partially stuck door makes it less stuck
            if (isOpen) stuck = MathHelper.Clamp(stuck - 30.0f, 0.0f, 100.0f);

            if (sendNetworkMessage)
            {
                item.NewComponentEvent(this, false, true);
            }
        }

        public override void ServerWrite(Lidgren.Network.NetOutgoingMessage msg, Barotrauma.Networking.Client c)
        {
            msg.Write(isOpen);
            msg.WriteRangedSingle(stuck, 0.0f, 100.0f, 8);        
        }

        public override void ClientRead(Lidgren.Network.NetIncomingMessage msg)
        {
            SetState(msg.ReadBoolean(), true);
            Stuck = msg.ReadRangedSingle(0.0f, 100.0f, 8);
        }
        
    }
}
