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

        public readonly Submarine Submarine;

        public EntityGrid(Submarine submarine, float cellSize)
        {
            //make the grid slightly larger than the borders of the submarine,
            //because docking ports may create gaps and hulls outside the borders
            int padding = 128;

            this.limits = new Rectangle(
                submarine.Borders.X - padding, 
                submarine.Borders.Y + padding, 
                submarine.Borders.Width + padding * 2, 
                submarine.Borders.Height + padding * 2);
            this.Submarine = submarine;
            this.cellSize = cellSize;
            InitializeGrid();
        }

        public EntityGrid(Rectangle worldRect, float cellSize)
        {
            this.limits = worldRect;
            this.cellSize = cellSize;
            InitializeGrid();
        }

        private void InitializeGrid()
        {
            entities = new List<MapEntity>[(int)Math.Ceiling(limits.Width / cellSize), (int)Math.Ceiling(limits.Height / cellSize)];
            for (int x = 0; x < entities.GetLength(0); x++)
            {
                for (int y = 0; y < entities.GetLength(1); y++)
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

            if (indices.Width < 0 || indices.X >= entities.GetLength(0) ||
                indices.Height < 0 || indices.Y >= entities.GetLength(1))
            {
                DebugConsole.ThrowError("Error in EntityGrid.InsertEntity: " + entity + " is outside the grid");
                return;
            }

            for (int x = Math.Max(indices.X, 0); x <= Math.Min(indices.Width, entities.GetLength(0)-1); x++)
            {
                for (int y = Math.Max(indices.Y,0); y <= Math.Min(indices.Height, entities.GetLength(1)-1); y++)
                {
                    entities[x, y].Add(entity);
                }
            }
        }

        public void RemoveEntity(MapEntity entity)
        {
            for (int x = 0; x < entities.GetLength(0); x++)
            {
                for (int y = 0; y < entities.GetLength(1); y++)
                {
                    if (entities[x, y].Contains(entity)) entities[x, y].Remove(entity);
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
            if (!MathUtils.IsValid(position)) return null;

            if (Submarine != null) position -= Submarine.HiddenSubPosition;
            Point indices = GetIndices(position);
            if (indices.X < 0 || indices.Y < 0 || indices.X >= entities.GetLength(0) || indices.Y >= entities.GetLength(1))
            {
                return null;
            }
            return entities[indices.X, indices.Y];
        }

        public Rectangle GetIndices(Rectangle rect)
        {
            Rectangle indices = Rectangle.Empty;
            indices.X = (int)Math.Floor((rect.X - limits.X) / cellSize);
            indices.Y = (int)Math.Floor((limits.Y - rect.Y) / cellSize);

            indices.Width = (int)Math.Floor((rect.Right - limits.X) / cellSize);
            indices.Height = (int)Math.Floor((limits.Y - (rect.Y - rect.Height)) / cellSize);

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
