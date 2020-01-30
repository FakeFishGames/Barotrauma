using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;

namespace Barotrauma
{
    public class Camera
    {
        public static bool FollowSub = true;

        private float? defaultZoom;
        public float DefaultZoom
        {
            get { return defaultZoom ?? (GameMain.Config == null || GameMain.Config.EnableMouseLook ? 1.3f : 1.0f); }
            set
            {
                defaultZoom = MathHelper.Clamp(value, 0.5f, 2.0f);
            }
        }

        private float zoomSmoothness = 8.0f;
        public float ZoomSmoothness
        {
            get { return zoomSmoothness; }
            set { zoomSmoothness = Math.Max(value, 0.01f); }
        }
        private float moveSmoothness = 8.0f;
        public float MoveSmoothness
        {
            get { return moveSmoothness; }
            set { moveSmoothness = Math.Max(value, 0.01f); }
        }

        private float minZoom = 0.1f;
        public float MinZoom
        {
            get { return minZoom;}
            set { minZoom = MathHelper.Clamp(value, 0.01f, 10.0f);   }
        }

        private float maxZoom = 2.0f;
        public float MaxZoom
        {
            get { return maxZoom; }
            set { maxZoom = MathHelper.Clamp(value, 1.0f, 10.0f); }
        }

        private float zoom;

        private float offsetAmount;

        private Matrix transform, shaderTransform, viewMatrix;
        private Vector2 position;
        private float rotation;

        private float angularVelocity;
        private float angularDamping;
        private float angularSpring;

        private Vector2 prevPosition;
        private float prevZoom;

        public float Shake;
        private Vector2 shakePosition;
        private float shakeTimer;
        
        //the area of the world inside the camera view
        private Rectangle worldView;

        private float globalZoomScale = 1.0f;

        private Point resolution;

        private Vector2 targetPos;

        //used to smooth out the movement when in freecam
        private float targetZoom;
        private Vector2 velocity;

        public float Zoom
        {
            get { return zoom; }
            set 
            {
                zoom = MathHelper.Clamp(value, GameMain.DebugDraw ? 0.01f : MinZoom, MaxZoom);
                
                Vector2 center = WorldViewCenter;
                float newWidth = resolution.X / zoom;
                float newHeight = resolution.Y / zoom;

                worldView = new Rectangle(
                    (int)(center.X - newWidth / 2.0f),
                    (int)(center.Y + newHeight / 2.0f),
                    (int)newWidth,
                    (int)newHeight);

                //UpdateTransform();
            }
        }

        public float Rotation
        {
            get { return rotation; }
            set
            {
                if (!MathUtils.IsValid(value)) return;
                rotation = value;
            }
        }

        public float AngularVelocity
        {
            get { return angularVelocity; }
            set
            {
                if (!MathUtils.IsValid(value)) return;
                angularVelocity = value;
            }
        }

        public float OffsetAmount
        {
            get { return offsetAmount; }
            set { offsetAmount = value; }
        }

        public Point Resolution
        {
            get { return resolution; }
        }

        public Rectangle WorldView
        {
            get { return worldView; }
        }

        public Vector2 WorldViewCenter
        {
            get
            {
                return new Vector2(
                    worldView.X + worldView.Width / 2.0f,
                    worldView.Y - worldView.Height / 2.0f);
            }
        }

        public Matrix Transform
        {
            get { return transform; }
        }

        public Matrix ShaderTransform
        {
            get { return shaderTransform; }
        }

        public Camera()
        {
            zoom = prevZoom = targetZoom = 1.0f;
            rotation = 0.0f;
            position = Vector2.Zero;

            CreateMatrices();
            GameMain.Instance.OnResolutionChanged += () => { CreateMatrices(); };

            UpdateTransform(false);
        }

        public Vector2 TargetPos
        {
            get { return targetPos; }
            set { targetPos = value; }
        }

        public Vector2 GetPosition()
        {
            return position;
        }
                
        // Auxiliary function to move the camera
        public void Translate(Vector2 amount)
        {
            position += amount;
        }

        public void ClientWrite(IWriteMessage msg)
        {
            if (Character.Controlled != null && !Character.Controlled.IsDead) { return; }

            msg.Write((byte)ClientNetObject.SPECTATING_POS);
            msg.Write(position.X);
            msg.Write(position.Y);
        }

        private void CreateMatrices()
        {
            resolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            worldView = new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            viewMatrix = Matrix.CreateTranslation(new Vector3(GameMain.GraphicsWidth / 2.0f, GameMain.GraphicsHeight / 2.0f, 0));
            
            globalZoomScale = (float)Math.Pow(new Vector2(resolution.X, resolution.Y).Length() / new Vector2(1920, 1080).Length(), 2);
        }

        public void UpdateTransform(bool interpolate = true)
        {
            Vector2 interpolatedPosition = interpolate ? Timing.Interpolate(prevPosition, position) : position;

            float interpolatedZoom = interpolate ? Timing.Interpolate(prevZoom, zoom) : zoom;

            worldView.X = (int)(interpolatedPosition.X - worldView.Width / 2.0);
            worldView.Y = (int)(interpolatedPosition.Y + worldView.Height / 2.0);
            
            transform = Matrix.CreateTranslation(
                new Vector3(-interpolatedPosition.X, interpolatedPosition.Y, 0)) *
                Matrix.CreateScale(new Vector3(interpolatedZoom, interpolatedZoom, 1)) *
                Matrix.CreateRotationZ(rotation) * viewMatrix;

            shaderTransform = Matrix.CreateTranslation(
                new Vector3(
                    -interpolatedPosition.X - resolution.X / interpolatedZoom / 2.0f,
                    -interpolatedPosition.Y - resolution.Y / interpolatedZoom / 2.0f, 0)) *
                Matrix.CreateScale(new Vector3(interpolatedZoom, interpolatedZoom, 1)) *

                viewMatrix * Matrix.CreateRotationZ(-rotation);

            if (Character.Controlled == null)
            {
                GameMain.SoundManager.ListenerPosition = new Vector3(WorldViewCenter.X, WorldViewCenter.Y, -(100.0f / zoom));
            }
            else
            {
                GameMain.SoundManager.ListenerPosition = new Vector3(Character.Controlled.WorldPosition.X, Character.Controlled.WorldPosition.Y, -(100.0f / zoom));
            }
            

            if (!interpolate)
            {
                prevPosition = position;
                prevZoom = zoom;
            }
        }

        private Vector2 previousOffset;
        
        /// <summary>
        /// Resets to false each time the MoveCamera method is called.
        /// </summary>
        public bool Freeze { get; set; }

        public void MoveCamera(float deltaTime, bool allowMove = true, bool allowZoom = true)
        {
            prevPosition = position;
            prevZoom = zoom;

            float moveSpeed = 20.0f / zoom;

            Vector2 moveCam = Vector2.Zero;
            if (targetPos == Vector2.Zero)
            {
                Vector2 moveInput = Vector2.Zero;
                if (allowMove && GUI.KeyboardDispatcher.Subscriber == null)
                {
                    if (PlayerInput.KeyDown(Keys.LeftShift)) moveSpeed *= 2.0f;
                    if (PlayerInput.KeyDown(Keys.LeftControl)) moveSpeed *= 0.5f;

                    if (GameMain.Config.KeyBind(InputType.Left).IsDown())   moveInput.X -= 1.0f;
                    if (GameMain.Config.KeyBind(InputType.Right).IsDown())  moveInput.X += 1.0f;
                    if (GameMain.Config.KeyBind(InputType.Down).IsDown())   moveInput.Y -= 1.0f;
                    if (GameMain.Config.KeyBind(InputType.Up).IsDown())     moveInput.Y += 1.0f;
                }

                velocity = Vector2.Lerp(velocity, moveInput, deltaTime * 10.0f);
                moveCam = velocity * moveSpeed * deltaTime * 60.0f;
                
                if (Screen.Selected == GameMain.GameScreen && FollowSub)
                {
                    var closestSub = Submarine.FindClosest(WorldViewCenter);
                    if (closestSub != null)
                    {
                        moveCam += FarseerPhysics.ConvertUnits.ToDisplayUnits(closestSub.Velocity * deltaTime);
                    }
                }
                 
                if (allowZoom && GUI.MouseOn == null)
                {
                    Vector2 mouseInWorld = ScreenToWorld(PlayerInput.MousePosition);
                    Vector2 diffViewCenter;
                    diffViewCenter = ((mouseInWorld - Position) * Zoom);
                    targetZoom = MathHelper.Clamp(
                        targetZoom + (PlayerInput.ScrollWheelSpeed / 1000.0f) * zoom, 
                        GameMain.DebugDraw ? MinZoom * 0.1f : MinZoom, 
                        MaxZoom);

                    Zoom = MathHelper.Lerp(Zoom, targetZoom, deltaTime * 10.0f);
                    if (!PlayerInput.KeyDown(Keys.F)) Position = mouseInWorld - (diffViewCenter / Zoom);
                }
            }
            else if (allowMove)
            {
                Vector2 mousePos = PlayerInput.MousePosition;
                Vector2 offset = mousePos - resolution.ToVector2() / 2;
                offset.X = offset.X / (resolution.X * 0.4f);
                offset.Y = -offset.Y / (resolution.Y * 0.3f);
                if (offset.LengthSquared() > 1.0f) offset.Normalize();
                offset *= offsetAmount;
                // Freeze the camera movement by default, when the cursor is on top of an ui element.
                // Setting a positive value to the OffsetAmount, will override this behaviour.
                if (GUI.MouseOn != null && offsetAmount > 0)
                {
                    Freeze = true;
                }
                if (CharacterHealth.OpenHealthWindow != null || CrewManager.IsCommandInterfaceOpen)
                {
                    offset *= 0;
                    Freeze = false;
                }
                if (Freeze)
                {
                    offset = previousOffset;
                }
                else
                {
                    previousOffset = offset;
                }
                                
                //how much to zoom out (zoom completely out when offset is 1000)
                float zoomOutAmount = Math.Min(offset.Length() / 1000.0f, 1.0f);                
                //zoom amount when resolution is not taken into account
                float unscaledZoom = MathHelper.Lerp(DefaultZoom, MinZoom, zoomOutAmount);
                //zoom with resolution taken into account (zoom further out on smaller resolutions)
                float scaledZoom = unscaledZoom * globalZoomScale;

                //an ad-hoc way of allowing the players to have roughly the same maximum view distance regardless of the resolution,
                //while still keeping the zoom around 1.0 when not looking further away (because otherwise we'd always be downsampling 
                //on lower resolutions, which doesn't look that good)
                float newZoom = MathHelper.Lerp(unscaledZoom, scaledZoom, (float)Math.Sqrt(zoomOutAmount));

                Zoom += (newZoom - zoom) / ZoomSmoothness;

                //force targetzoom to the current zoom value, so the camera stays at the same zoom when switching to freecam
                targetZoom = Zoom;

                Vector2 diff = (targetPos + offset) - position;

                moveCam = diff / MoveSmoothness;
            }
            rotation += angularVelocity * deltaTime;
            angularVelocity *= (1.0f - angularDamping);
            angularVelocity += -rotation * angularSpring;

            angularDamping = 0.05f;
            angularSpring = 0.2f;

            if (Shake < 0.01f)
            {
                shakePosition = Vector2.Zero;
                shakeTimer = 0.0f;
            }
            else
            {
                shakeTimer += deltaTime * 5.0f;
                Vector2 noisePos = new Vector2((float)PerlinNoise.CalculatePerlin(shakeTimer, shakeTimer, 0) - 0.5f, (float)PerlinNoise.CalculatePerlin(shakeTimer, shakeTimer, 0.5f) - 0.5f);

                shakePosition = noisePos * Shake * 2.0f;
                Shake = MathHelper.Lerp(Shake, 0.0f, deltaTime * 2.0f);
            }

            Translate(moveCam + shakePosition);
            Freeze = false;
        }
        
        public void StopMovement()
        {
            targetZoom = zoom;
            velocity = Vector2.Zero;
            angularVelocity = 0.0f;
            rotation = 0.0f;
        }

        public Vector2 Position
        {
            get { return position; }
            set 
            { 
                if (!MathUtils.IsValid(value))
                {
                    return;
                }
                position = value; 
            }
        }
                
        public Vector2 ScreenToWorld(Vector2 coords)
        {
            Vector2 worldCoords = Vector2.Transform(coords, Matrix.Invert(transform));
            return new Vector2(worldCoords.X, -worldCoords.Y);
        }

        public Vector2 WorldToScreen(Vector2 coords)
        {
            coords.Y = -coords.Y;
            //Vector2 screenCoords = Vector2.Transform(coords, transform);
            return Vector2.Transform(coords, transform);
        }
    }
}
