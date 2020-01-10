using Microsoft.Xna.Framework;
using System;
//TODO: this class still does things that the server doesn't need, cleanup

namespace Barotrauma
{
    public class Camera
    {
        public static Camera Instance = new Camera();

        public static bool FollowSub = true;

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
                1,
                1);

            resolution = new Point(1,1);

            viewMatrix = 
                Matrix.CreateTranslation(new Vector3(0.5f, 0.5f, 0));

            UpdateTransform();
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
            
            if (!interpolate)
            {
                prevPosition = position;
                prevZoom = zoom;
            }
        }

        public void MoveCamera(float deltaTime, bool allowMove = true, bool allowZoom = true)
        {
            return;
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
