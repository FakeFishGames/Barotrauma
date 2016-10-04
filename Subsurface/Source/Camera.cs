using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System.Linq;

namespace Barotrauma
{
    public class Camera
    {
        const float DefaultZoom = 1.0f;
        const float ZoomSmoothness = 8.0f;
        const float MoveSmoothness = 8.0f;

        private float zoom;

        private float offsetAmount;

        private Matrix transform, shaderTransform, viewMatrix;
        private Vector2 position;
        private float rotation;

        private Vector2 prevPosition;
        private float prevZoom;

        public float Shake;
        private Vector2 shakePosition;
        private Vector2 shakeTargetPosition;
        
        //the area of the world inside the camera view
        private Rectangle worldView;

        private Point resolution;

        private Vector2 targetPos;

        public float Zoom
        {
            get { return zoom; }
            set 
            {
                zoom = Math.Max(value, GameMain.DebugDraw ? 0.01f : 0.1f);
                
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
            set { rotation = value; }
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
            zoom = 1.0f;
            rotation = 0.0f;
            position = Vector2.Zero;

            worldView = new Rectangle(0,0, 
                GameMain.GraphicsWidth,
                GameMain.GraphicsHeight);

            resolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);

            viewMatrix = 
                Matrix.CreateTranslation(new Vector3(GameMain.GraphicsWidth / 2.0f, GameMain.GraphicsHeight / 2.0f, 0));
        }

        public Vector2 TargetPos
        {
            get { return targetPos; }
            set { targetPos = value; }
        }
                
        // Auxiliary function to move the camera
        public void Translate(Vector2 amount)
        {
            position += amount;

        }

        public void UpdateTransform(bool interpolate = true, bool clampPos = false)
        {
            Vector2 interpolatedPosition = interpolate ? Timing.Interpolate(prevPosition, position) : position;

            float interpolatedZoom = interpolate ? Timing.Interpolate(prevZoom, zoom) : zoom;

            worldView.X = (int)(interpolatedPosition.X - worldView.Width / 2.0);
            worldView.Y = (int)(interpolatedPosition.Y + worldView.Height / 2.0);

            if (Level.Loaded != null && clampPos)
            {
                position.Y -= Math.Max(worldView.Y - Level.Loaded.Size.Y, 0.0f);
                interpolatedPosition.Y -= Math.Max(worldView.Y - Level.Loaded.Size.Y, 0.0f);

                worldView.Y = Math.Min((int)Level.Loaded.Size.Y, worldView.Y); 
            }

            transform = Matrix.CreateTranslation(
                new Vector3(-interpolatedPosition.X, interpolatedPosition.Y, 0)) *
                Matrix.CreateScale(new Vector3(interpolatedZoom, interpolatedZoom, 1)) *
                viewMatrix;

            shaderTransform = Matrix.CreateTranslation(
                new Vector3(
                    -interpolatedPosition.X - resolution.X / interpolatedZoom / 2.0f,
                    -interpolatedPosition.Y - resolution.Y / interpolatedZoom / 2.0f, 0)) *
                Matrix.CreateScale(new Vector3(interpolatedZoom, interpolatedZoom, 1)) *
                viewMatrix;
            
            Sound.CameraPos = new Vector3(WorldViewCenter.X, WorldViewCenter.Y, 0.0f);

            if (!interpolate)
            {
                prevPosition = position;
                prevZoom = zoom;
            }
        }

        public void MoveCamera(float deltaTime)
        {            
            prevPosition = position;
            prevZoom = zoom;

            float moveSpeed = 20.0f/zoom;

            Vector2 moveCam = Vector2.Zero;
            if (targetPos == Vector2.Zero)
            {
                if (GUIComponent.KeyboardDispatcher.Subscriber == null)
                {
                    if (PlayerInput.KeyDown(Keys.LeftShift)) moveSpeed *= 2.0f;
                    if (PlayerInput.KeyDown(Keys.LeftControl)) moveSpeed *= 0.5f;

                    if (GameMain.Config.KeyBind(InputType.Left).IsDown())   moveCam.X -= moveSpeed;
                    if (GameMain.Config.KeyBind(InputType.Right).IsDown())  moveCam.X += moveSpeed;
                    if (GameMain.Config.KeyBind(InputType.Down).IsDown())   moveCam.Y -= moveSpeed;
                    if (GameMain.Config.KeyBind(InputType.Up).IsDown())     moveCam.Y += moveSpeed;
                }

                if (Screen.Selected == GameMain.GameScreen)
                {
                    var closestSub = Submarine.GetClosest(WorldViewCenter);
                    if (closestSub != null)
                    {
                        moveCam += FarseerPhysics.ConvertUnits.ToDisplayUnits(closestSub.Velocity * deltaTime);
                    }
                }
                 
                moveCam = moveCam * deltaTime * 60.0f; 

                Zoom = MathHelper.Clamp(zoom + (PlayerInput.ScrollWheelSpeed / 1000.0f) * zoom, GameMain.DebugDraw ? 0.01f : 0.1f, 2.0f); 
            }
            else
            {
                Vector2 mousePos = PlayerInput.MousePosition;

                Vector2 offset = mousePos - new Vector2(resolution.X / 2.0f, resolution.Y / 2.0f);

                offset.X = offset.X / (resolution.X * 0.4f);
                offset.Y = -offset.Y / (resolution.Y * 0.3f);

                if (offset.Length() > 1.0f) offset.Normalize();

                offset = offset * offsetAmount;

                float newZoom = Math.Min(DefaultZoom - Math.Min(offset.Length() / resolution.Y, 1.0f),1.0f);
                Zoom += (newZoom - zoom) / ZoomSmoothness;

                Vector2 diff = (targetPos + offset) - position;

                moveCam = diff / MoveSmoothness;
            }

            shakeTargetPosition = Rand.Vector(Shake);
            shakePosition = Vector2.Lerp(shakePosition, shakeTargetPosition, 0.5f);
            Shake = MathHelper.Lerp(Shake, 0.0f, deltaTime * 2.0f);

            Translate(moveCam + shakePosition);
        }
        
        public Vector2 Position
        {
            get { return position; }
            set { position = value; }
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
