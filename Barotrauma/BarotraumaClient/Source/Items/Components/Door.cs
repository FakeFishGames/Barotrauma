using Barotrauma.Lights;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;


namespace Barotrauma.Items.Components
{
    partial class Door : Pickable, IDrawableComponent, IServerSerializable
    {
        private ConvexHull convexHull;
        private ConvexHull convexHull2;

        private Vector2[] GetConvexHullCorners(Rectangle rect)
        {
            Vector2[] corners = new Vector2[4];
            corners[0] = new Vector2(rect.X - 1, rect.Y - rect.Height - 1);
            corners[1] = new Vector2(rect.X - 1, rect.Y + 1);
            corners[2] = new Vector2(rect.Right + 1, rect.Y + 1);
            corners[3] = new Vector2(rect.Right + 1, rect.Y - rect.Height - 1);

            return corners;
        }

        private void UpdateConvexHulls()
        {
            doorRect = new Rectangle(
                item.Rect.Center.X - (int)(doorSprite.size.X / 2 * item.Scale),
                item.Rect.Y - item.Rect.Height / 2 + (int)(doorSprite.size.Y / 2.0f * item.Scale),
                (int)(doorSprite.size.X * item.Scale),
                (int)(doorSprite.size.Y * item.Scale));

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
                rect.Height = -(int)(window.Y * item.Scale);

                rect.Y += (int)(doorRect.Height * openState);
                rect.Height = Math.Max(rect.Height - (rect.Y - doorRect.Y), 0);
                rect.Y = Math.Min(doorRect.Y, rect.Y);
                
                if (convexHull2 != null)
                {
                    Rectangle rect2 = doorRect;
                    rect2.Y = rect2.Y + (int)((window.Y * item.Scale - window.Height * item.Scale));

                    rect2.Y += (int)(doorRect.Height * openState);
                    rect2.Y = Math.Min(doorRect.Y, rect2.Y);
                    rect2.Height = rect2.Y - (doorRect.Y - (int)(doorRect.Height * (1.0f - openState)));

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
        
        public void Draw(SpriteBatch spriteBatch, bool editing)
        {
            Color color = (item.IsSelected) ? Color.Green : Color.White;
            if (brokenSprite == null)
            {
                //broken doors turn black if no broken sprite has been configured
                color = color * (item.Condition / item.Prefab.Health);
                color.A = 255;
            }
            
            if (stuck > 0.0f && weldedSprite != null)
            {
                Vector2 weldSpritePos = new Vector2(item.Rect.Center.X, item.Rect.Y - item.Rect.Height / 2.0f);
                if (item.Submarine != null) weldSpritePos += item.Submarine.Position;
                weldSpritePos.Y = -weldSpritePos.Y;

                weldedSprite.Draw(spriteBatch,
                    weldSpritePos, Color.White * (stuck / 100.0f), scale: item.Scale);
            }

            if (openState == 1.0f)
            {
                body.Enabled = false;
                return;
            }

            if (isHorizontal)
            {
                Vector2 pos = new Vector2(item.Rect.X, item.Rect.Y - item.Rect.Height / 2);
                if (item.Submarine != null) pos += item.Submarine.DrawPosition;
                pos.Y = -pos.Y;

                if (brokenSprite == null || item.Health > 0.0f)
                {
                    spriteBatch.Draw(doorSprite.Texture, pos,
                        new Rectangle((int) (doorSprite.SourceRect.X + doorSprite.size.X * openState),
                            (int) doorSprite.SourceRect.Y,
                            (int) (doorSprite.size.X * (1.0f - openState)), (int) doorSprite.size.Y),
                        color, 0.0f, doorSprite.Origin, item.Scale, SpriteEffects.None, doorSprite.Depth);
                }

                if (brokenSprite != null && item.Health < item.Prefab.Health)
                {
                    Vector2 scale = scaleBrokenSprite ? new Vector2(1.0f, 1.0f - item.Health / item.Prefab.Health) : Vector2.One;
                    float alpha = fadeBrokenSprite ? 1.0f - item.Health / item.Prefab.Health : 1.0f;
                    spriteBatch.Draw(brokenSprite.Texture, pos,
                        new Rectangle((int)(brokenSprite.SourceRect.X + brokenSprite.size.X * openState), brokenSprite.SourceRect.Y,
                            (int)(brokenSprite.size.X * (1.0f - openState)), (int)brokenSprite.size.Y),
                        color * alpha, 0.0f, brokenSprite.Origin, scale * item.Scale, SpriteEffects.None,
                        brokenSprite.Depth);
                }
            }
            else
            {
                Vector2 pos = new Vector2(item.Rect.Center.X, item.Rect.Y);
                if (item.Submarine != null) pos += item.Submarine.DrawPosition;
                pos.Y = -pos.Y;

                if (brokenSprite == null || item.Health > 0.0f)
                {
                    spriteBatch.Draw(doorSprite.Texture, pos,
                        new Rectangle(doorSprite.SourceRect.X,
                            (int) (doorSprite.SourceRect.Y + doorSprite.size.Y * openState),
                            (int) doorSprite.size.X, (int) (doorSprite.size.Y * (1.0f - openState))),
                        color, 0.0f, doorSprite.Origin, item.Scale, SpriteEffects.None, doorSprite.Depth);
                }

                if (brokenSprite != null && item.Health < item.Prefab.Health)
                {
                    Vector2 scale = scaleBrokenSprite ? new Vector2(1.0f - item.Health / item.Prefab.Health, 1.0f) : Vector2.One;
                    float alpha = fadeBrokenSprite ? 1.0f - item.Health / item.Prefab.Health : 1.0f;
                    spriteBatch.Draw(brokenSprite.Texture, pos,
                        new Rectangle(brokenSprite.SourceRect.X, (int)(brokenSprite.SourceRect.Y + brokenSprite.size.Y * openState),
                            (int)brokenSprite.size.X, (int)(brokenSprite.size.Y * (1.0f - openState))),
                        color * alpha, 0.0f, brokenSprite.Origin, scale * item.Scale, SpriteEffects.None, brokenSprite.Depth);
                }
            }
        }


        partial void SetState(bool open, bool isNetworkMessage, bool sendNetworkMessage = false)
        {
            if (isStuck ||
                (predictedState == null && isOpen == open) ||
                (predictedState != null && isOpen == predictedState.Value && isOpen == open))
            {
                return;
            }

            if (GameMain.Client != null && !isNetworkMessage)
            {
                bool stateChanged = open != predictedState;

                //clients can "predict" that the door opens/closes when a signal is received
                //the prediction will be reset after 1 second, setting the door to a state
                //sent by the server, or reverting it back to its old state if no msg from server was received
                predictedState = open;
                resetPredictionTimer = CorrectionDelay;
                if (stateChanged) PlaySound(ActionType.OnUse, item.WorldPosition);
            }
            else
            {
                isOpen = open;
                if (!isNetworkMessage || open != predictedState) PlaySound(ActionType.OnUse, item.WorldPosition);
            }

            //opening a partially stuck door makes it less stuck
            if (isOpen) stuck = MathHelper.Clamp(stuck - 30.0f, 0.0f, 100.0f);
            
        }

        public override void ClientRead(ServerNetObject type, Lidgren.Network.NetBuffer msg, float sendingTime)
        {
            base.ClientRead(type, msg, sendingTime);

            SetState(msg.ReadBoolean(), true);
            Stuck = msg.ReadRangedSingle(0.0f, 100.0f, 8);

            predictedState = null;
        }
    }
}
