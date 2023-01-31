using Barotrauma.Extensions;
using Barotrauma.Lights;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class Door : Pickable, IDrawableComponent, IServerSerializable
    {
        private ConvexHull convexHull;
        private ConvexHull convexHull2;

        private float shake;
        private float shakeTimer;
        private Vector2 shakePos;

        //openState when the vertices of the convex hull were last calculated
        private float lastConvexHullState;

        [Serialize("1,1", IsPropertySaveable.No, description: "The scale of the shadow-casting area of the door (relative to the actual size of the door).")]
        public Vector2 ShadowScale
        {
            get;
            set;
        }

        public Vector2 DrawSize
        {
            //use the extents of the item as the draw size
            get { return Vector2.Zero; }
        }

        private Vector2[] GetConvexHullCorners(Rectangle rect)
        {
            Point shadowSize = rect.Size.Multiply(ShadowScale);
            Vector2 center = new Vector2(rect.Center.X, rect.Y - rect.Height / 2);

            Vector2[] corners = new Vector2[4];
            corners[0] = center + new Vector2(-shadowSize.X, -shadowSize.Y) / 2;
            corners[1] = center + new Vector2(-shadowSize.X, shadowSize.Y) / 2;
            corners[2] = center + new Vector2(shadowSize.X, shadowSize.Y) / 2;
            corners[3] = center + new Vector2(shadowSize.X, -shadowSize.Y) / 2;

            if (IsHorizontal)
            {
                if (item.FlippedX)
                {
                    Vector2 itemCenter = new Vector2(item.Rect.Center.X, item.Rect.Y - item.Rect.Height / 2);
                    for (int i = 0; i < corners.Length; i++)
                    {
                        corners[i].X = itemCenter.X * 2 - corners[i].X;
                    }
                    Array.Reverse(corners);
                }
            }
            else
            {
                if (item.FlippedY)
                {
                    Vector2 itemCenter = new Vector2(item.Rect.Center.X, item.Rect.Y - item.Rect.Height / 2);
                    for (int i = 0; i < corners.Length; i++)
                    {
                        corners[i].Y = itemCenter.Y * 2 - corners[i].Y;
                    }
                    Array.Reverse(corners);
                }
            }

            return corners;
        }

        private void UpdateConvexHulls()
        {
            if (item.Removed) { return; }

            doorRect = new Rectangle(
                item.Rect.Center.X - (int)(doorSprite.size.X / 2 * item.Scale),
                item.Rect.Y - item.Rect.Height / 2 + (int)(doorSprite.size.Y / 2.0f * item.Scale),
                (int)(doorSprite.size.X * item.Scale),
                (int)(doorSprite.size.Y * item.Scale));

            Rectangle rect = doorRect;
            if (IsHorizontal)
            {
                rect.Width = (int)(rect.Width * (1.0f - openState));
            }
            else
            {
                rect.Height = (int)(rect.Height * (1.0f - openState));
            }

            //only merge the door's convex hull with overlapping wall segments if it's fully open or fully closed
            //it's the heaviest part of changing the convex hull, and doesn't need to be done while the door is still in motion
            bool mergeOverlappingSegments = openState <= 0.0f || openState >= 1.0f;
            if (Window.Height > 0 && Window.Width > 0)
            {
                if (IsHorizontal)
                {
                    rect.Width = (int)(Window.X * item.Scale);
                    rect.X -= (int)(doorRect.Width * openState);
                    rect.Width = Math.Max(rect.Width - (doorRect.X - rect.X), 0);
                    rect.X = Math.Max(doorRect.X, rect.X);
                    if (convexHull2 != null)
                    {
                        Rectangle rect2 = doorRect;
                        rect2.X += (int)(Window.Right * item.Scale);
                        rect2.X -= (int)(doorRect.Width * openState);
                        rect2.X = Math.Max(doorRect.X, rect2.X);
                        rect2.Width = doorRect.Right - (int)(doorRect.Width * openState) - rect2.X;
                        if (rect2.Width == 0)
                        {
                            convexHull2.Enabled = false;
                        }
                        else
                        {
                            convexHull2.Enabled = true;
                            convexHull2.SetVertices(GetConvexHullCorners(rect2), mergeOverlappingSegments);
                        }
                    }
                }
                else
                {
                    rect.Height = -(int)(Window.Y * item.Scale);
                    rect.Y += (int)(doorRect.Height * openState);
                    rect.Height = Math.Max(rect.Height - (rect.Y - doorRect.Y), 0);
                    rect.Y = Math.Min(doorRect.Y, rect.Y);                
                    if (convexHull2 != null)
                    {
                        Rectangle rect2 = doorRect;
                        rect2.Y += (int)(Window.Y * item.Scale - Window.Height * item.Scale);
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
                            convexHull2.SetVertices(GetConvexHullCorners(rect2), mergeOverlappingSegments);
                        }
                    }
                }
            }

            if (convexHull == null) { return; }

            if (rect.Height == 0 || rect.Width == 0)
            {
                convexHull.Enabled = false;
            }
            else
            {
                convexHull.Enabled = true;
                convexHull.SetVertices(GetConvexHullCorners(rect), mergeOverlappingSegments);
            }
        }


        partial void UpdateProjSpecific(float deltaTime)
        {
            if (shakeTimer > 0.0f)
            {				
                shakeTimer -= deltaTime;
                Vector2 noisePos = new Vector2((float)PerlinNoise.CalculatePerlin(shakeTimer * 10.0f, shakeTimer * 10.0f, 0) - 0.5f, (float)PerlinNoise.CalculatePerlin(shakeTimer * 10.0f, shakeTimer * 10.0f, 0.5f) - 0.5f);
                shakePos = noisePos * shake * 2.0f;
                shake = Math.Min(shake, shakeTimer * 10.0f);
            }
            else
            {
                shakePos = Vector2.Zero;
            }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1)
        {
            Color color = item.GetSpriteColor(withHighlight: true);
            if (brokenSprite == null)
            {
                //broken doors turn black if no broken sprite has been configured
                color *= (item.Condition / item.MaxCondition);
                color.A = 255;
            }
            
            if (stuck > 0.0f && weldedSprite != null)
            {
                Vector2 weldSpritePos = new Vector2(item.Rect.Center.X, item.Rect.Y - item.Rect.Height / 2.0f) + shakePos;
                if (item.Submarine != null) { weldSpritePos += item.Submarine.DrawPosition; }
                weldSpritePos.Y = -weldSpritePos.Y;

                weldedSprite.Draw(spriteBatch,
                    weldSpritePos, item.SpriteColor * (stuck / 100.0f), scale: item.Scale);
            }

            if (openState >= 1.0f) { return; }

            Vector2 pos;
            if (IsHorizontal)
            {
                pos = new Vector2(item.Rect.X, item.Rect.Y - item.Rect.Height / 2);
                if (item.FlippedX) { pos.X += (int)(doorSprite.size.X * item.Scale * openState); }
            }
            else
            {
                pos = new Vector2(item.Rect.Center.X, item.Rect.Y);
                if (item.FlippedY) { pos.Y -= (int)(doorSprite.size.Y * item.Scale * openState); }
            }

            pos += shakePos;
            if (item.Submarine != null) { pos += item.Submarine.DrawPosition; }
            pos.Y = -pos.Y;

            if (brokenSprite == null || !IsBroken)
            {
                if (doorSprite?.Texture != null)
                {
                    spriteBatch.Draw(doorSprite.Texture, pos,
                        getSourceRect(doorSprite, openState, IsHorizontal),
                        color, 0.0f, doorSprite.Origin, item.Scale, item.SpriteEffects, doorSprite.Depth);
                }
            }

            float maxCondition = item.Repairables.Any() ? 
                item.Repairables.Min(r => r.RepairThreshold) / 100.0f * item.MaxCondition : 
                item.MaxCondition;
            float healthRatio = item.Health / maxCondition;
            if (brokenSprite?.Texture != null && healthRatio < 1.0f)
            {
                Vector2 scale = scaleBrokenSprite ? new Vector2(1.0f - healthRatio) : Vector2.One;
                if (IsHorizontal) { scale.X = 1; } else { scale.Y = 1; }
                float alpha = fadeBrokenSprite ? 1.0f - healthRatio : 1.0f;
                spriteBatch.Draw(brokenSprite.Texture, pos,
                    getSourceRect(brokenSprite, openState, IsHorizontal),
                    color * alpha, 0.0f, brokenSprite.Origin, scale * item.Scale, item.SpriteEffects,
                    brokenSprite.Depth);
            }

            static Rectangle getSourceRect(Sprite sprite, float openState, bool horizontal)
            {
                if (horizontal)
                {
                    return new Rectangle(
                        (int)(sprite.SourceRect.X + sprite.size.X * openState),
                        sprite.SourceRect.Y,
                        (int)(sprite.size.X * (1.0f - openState)),
                        (int)sprite.size.Y);
                }
                else
                {
                    return new Rectangle(
                        sprite.SourceRect.X,
                        (int)(sprite.SourceRect.Y + sprite.size.Y * openState),
                        (int)sprite.size.X,
                        (int)(sprite.size.Y * (1.0f - openState)));
                }
            }            
        }

        partial void OnFailedToOpen()
        {
            if (shakeTimer <= 0.0f)
            {
                PlaySound(ActionType.OnFailure);
                shake = 5.0f;
                shakeTimer = 1.0f;
            }
        }

        partial void SetState(bool open, bool isNetworkMessage, bool sendNetworkMessage, bool forcedOpen)
        {
            if ((IsStuck && !isNetworkMessage) ||
                (PredictedState == null && isOpen == open) ||
                (PredictedState != null && isOpen == PredictedState.Value && isOpen == open))
            {
                return;
            }

            if (GameMain.Client != null && !isNetworkMessage)
            {
                bool stateChanged = open != PredictedState;

                //clients can "predict" that the door opens/closes when a signal is received
                //the prediction will be reset after 1 second, setting the door to a state
                //sent by the server, or reverting it back to its old state if no msg from server was received
                PredictedState = open;
                resetPredictionTimer = CorrectionDelay;
                if (stateChanged && !IsBroken)
                {
                    PlayInteractionSound();
                }
            }
            else
            {
                isOpen = open;
                if (!isNetworkMessage || open != PredictedState)
                {
                    StopPicking(null); 
                    if (!IsBroken)
                    {
                        PlayInteractionSound();
                    }
                    if (isOpen) { stuck = MathHelper.Clamp(stuck - StuckReductionOnOpen, 0.0f, 100.0f); }
                }
            }
            
            void PlayInteractionSound()
            {
                ActionType actionType = ActionType.OnUse;
                if (forcedOpen)
                {
                    actionType = ActionType.OnPicked;
                }
                else
                {
                    if (open && HasSoundsOfType[(int)ActionType.OnOpen])
                    {
                        actionType = ActionType.OnOpen;
                    }
                    else if (!open && HasSoundsOfType[(int)ActionType.OnClose])
                    {
                        actionType = ActionType.OnClose;
                    }
                }
                PlaySound(actionType);
            }
        }

        public override void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            base.ClientEventRead(msg, sendingTime);

            bool open       = msg.ReadBoolean();
            bool broken     = msg.ReadBoolean();
            bool forcedOpen = msg.ReadBoolean();
            bool isStuck    = msg.ReadBoolean();
            bool isJammed   = msg.ReadBoolean();
            SetState(open, isNetworkMessage: true, sendNetworkMessage: false, forcedOpen: forcedOpen);
            stuck = msg.ReadRangedSingle(0.0f, 100.0f, 8);
            UInt16 lastUserID = msg.ReadUInt16();
            Character user = lastUserID == 0 ? null : Entity.FindEntityByID(lastUserID) as Character;
            if (user != lastUser)
            {
                lastUser = user;
                toggleCooldownTimer = ToggleCoolDown;
            }
            this.isStuck = isStuck;
            this.isJammed = isJammed;
            if (isStuck) { OpenState = 0.0f; }
            IsBroken = broken;
            PredictedState = null;
        }
    }
}
