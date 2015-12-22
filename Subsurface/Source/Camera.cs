using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

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
                zoom = value; 
                if (zoom < 0.1f) zoom = 0.1f;

                //if (prevZoom == zoom) return;

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
            //UpdateTransform();
        }

        public void UpdateTransform()
        {
            Vector2 interpolatedPosition = Physics.Interpolate(prevPosition, position);

            float interpolatedZoom =  Physics.Interpolate(prevZoom, zoom);

            worldView.X = (int)(interpolatedPosition.X - worldView.Width / 2.0);
            worldView.Y = (int)(interpolatedPosition.Y + worldView.Height / 2.0);

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

                    if (PlayerInput.KeyDown(Keys.A)) moveCam.X -= moveSpeed;
                    if (PlayerInput.KeyDown(Keys.D)) moveCam.X += moveSpeed;
                    if (PlayerInput.KeyDown(Keys.S)) moveCam.Y -= moveSpeed;
                    if (PlayerInput.KeyDown(Keys.W)) moveCam.Y += moveSpeed;

                    if (Submarine.Loaded!=null && Screen.Selected == GameMain.GameScreen)
                    {
                        moveCam += FarseerPhysics.ConvertUnits.ToDisplayUnits(Submarine.Loaded.Velocity*deltaTime);
                    }
                 
                    moveCam = moveCam * deltaTime * 60.0f;
                }


                Zoom = MathHelper.Clamp(zoom + (PlayerInput.ScrollWheelSpeed / 1000.0f) * zoom, 0.1f, 2.0f); 
            }
            else
            {
                Vector2 mousePos = new Vector2(PlayerInput.GetMouseState.X, PlayerInput.GetMouseState.Y);

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
            Shake = MathHelper.Lerp(Shake, 0.0f, 0.03f);

            Translate(moveCam+shakePosition);
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
