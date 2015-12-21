using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    class EntityGrid
    {
        private List<MapEntity>[,] entities;

        private Rectangle limits;

        private float cellSize;

        public EntityGrid(Rectangle limits, float cellSize)
        {
            this.limits = limits;
            this.cellSize = cellSize;

            entities = new List<MapEntity>[(int)Math.Ceiling(limits.Width / cellSize),(int)Math.Ceiling(limits.Height / cellSize)];
            for (int x = 0; x<entities.GetLength(0); x++)
            {
                for (int y=0; y<entities.GetLength(1); y++)
                {
                    entities[x, y] = new List<MapEntity>();
                }
            }
        }

        public void InsertEntity(MapEntity entity)
        {
            Rectangle rect = entity.Rect;
            //if (Submarine.Loaded != null) rect.Offset(-Submarine.HiddenSubPosition);
            Rectangle indices = GetIndices(rect);
            if (indices.X<0 || indices.Width>=entities.GetLength(0) ||
                indices.Y<0 || indices.Height>=entities.GetLength(1))
            {
                DebugConsole.ThrowError("Error in EntityGrid.InsertEntity: "+entity+" is outside the grid");
                return;
            }

            for (int x=indices.X; x<=indices.Width; x++)
            {
                for (int y = indices.Y; y<=indices.Height; y++)
                {
                    entities[x, y].Add(entity);
                }
            }
        }
        
        public void RemoveEntity(MapEntity entity)
        {
            Rectangle indices = GetIndices(entity.Rect);
            if (indices.X < 0 || indices.Width >= entities.GetLength(0) ||
                indices.Y < 0 || indices.Height >= entities.GetLength(1))
            {
                DebugConsole.ThrowError("Error in EntityGrid.RemoveEntity: " + entity + " is outside the grid");
                return;
            }

            for (int x = indices.X; x <= indices.Width; x++)
            {
                for (int y = indices.Y; y <= indices.Height; y++)
                {
                    entities[x, y].Remove(entity);
                }
            }
        }

        public void Clear()
        {
            for (int x = 0; x < entities.GetLength(0); x++)
            {
                for (int y = 0; y < entities.GetLength(1); y++)
                {
                    entities[x, y].Clear();
                }
            }
        }

        public List<MapEntity> GetEntities(Vector2 position)
        {
            if (Submarine.Loaded != null) position -= Submarine.HiddenSubPosition;

            if (position.X < limits.X || position.Y > limits.Y ||
                position.X > limits.Right || position.Y < limits.Y - limits.Height)
            {
                return new List<MapEntity>();
            }

            Point indices = GetIndices(position);
            
            return entities[indices.X, indices.Y];
        }

        public Rectangle GetIndices(Rectangle rect)
        {
            Rectangle indices = Rectangle.Empty;
            indices.X = (int)Math.Floor((rect.X - limits.X) / cellSize);
            indices.Y = (int)Math.Floor((limits.Y - rect.Y)/cellSize);

            indices.Width = (int)Math.Floor((rect.Right - limits.X) / cellSize);
            indices.Height = (int)Math.Floor((limits.Y - (rect.Y-rect.Height)) / cellSize);

            return indices;
        }

        public Point GetIndices(Vector2 position)
        {
            return new Point(
                (int)Math.Floor((position.X - limits.X) / cellSize),
                (int)Math.Floor((limits.Y - position.Y) / cellSize));            
        }
    }
}
