using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Voronoi2;
using System.Linq;
#if CLIENT
using Microsoft.Xna.Framework.Graphics;
#endif

namespace Barotrauma
{
    partial class LevelWall : IDisposable
    {
        public List<VoronoiCell> Cells { get; private set; }

        public Body Body { get; private set; }

        protected readonly Level level;

        private readonly List<Vector2[]> triangles;
        private readonly Color color;

        private float moveState;
        private float moveLength;

        private Vector2 moveAmount;
        public Vector2 MoveAmount
        {
            get { return moveAmount; }
            set
            {
                moveAmount = value;
                moveLength = moveAmount.Length();
            }
        }

        private float wallDamageOnTouch;
        public float WallDamageOnTouch
        {
            get { return wallDamageOnTouch; }
            set 
            {
                Cells.ForEach(c => c.DoesDamage = !MathUtils.NearlyEqual(value, 0.0f));
                wallDamageOnTouch = value; 
            }
        }

        public float MoveSpeed;

        private Vector2? originalPos;

        public float MoveState
        {
            get { return moveState; }
            set { moveState = MathHelper.Clamp(value, 0.0f, MathHelper.TwoPi); }
        }

        public LevelWall(List<Vector2> vertices, Color color, Level level, bool giftWrap = false)
        {
            this.level = level;
            this.color = color;
            List<Vector2> originalVertices = new List<Vector2>(vertices);
            if (giftWrap) { vertices = MathUtils.GiftWrap(vertices); }
            if (vertices.Count < 3)
            {
                throw new ArgumentException("Failed to generate a wall (not enough vertices). Original vertices: " + string.Join(", ", originalVertices.Select(v => v.ToString())));
            }
            VoronoiCell wallCell = new VoronoiCell(vertices.ToArray());
            for (int i = 0; i < wallCell.Edges.Count; i++)
            {
                wallCell.Edges[i].Cell1 = wallCell;
                wallCell.Edges[i].IsSolid = true;
            }
            Cells = new List<VoronoiCell>() { wallCell };
            Body = CaveGenerator.GeneratePolygons(Cells, level, out triangles);
            if (triangles.Count == 0)
            {
                throw new ArgumentException("Failed to generate a wall (not enough triangles). Original vertices: " + string.Join(", ", originalVertices.Select(v => v.ToString())));
            }
#if CLIENT
            GenerateVertices();
#endif
        }

        public LevelWall(List<Vector2> edgePositions, Vector2 extendAmount, Color color, Level level)
        {
            this.level = level;
            this.color = color;
            Cells = new List<VoronoiCell>();
            for (int i = 0; i < edgePositions.Count - 1; i++)
            {
                Vector2[] vertices = new Vector2[4];
                vertices[0] = edgePositions[i];
                vertices[1] = edgePositions[i + 1];
                vertices[2] = vertices[1] + extendAmount;
                vertices[3] = vertices[0] + extendAmount;

                VoronoiCell wallCell = new VoronoiCell(vertices)
                {
                    CellType = CellType.Solid
                };
                wallCell.Edges[0].Cell1 = wallCell;
                wallCell.Edges[1].Cell1 = wallCell;
                wallCell.Edges[2].Cell1 = wallCell;
                wallCell.Edges[3].Cell1 = wallCell;
                wallCell.Edges[0].IsSolid = true;

                if (i > 1)
                {
                    wallCell.Edges[3].Cell2 = Cells[i - 1];
                    Cells[i - 1].Edges[1].Cell2 = wallCell;
                }

                Cells.Add(wallCell);
            }
            
            Body = CaveGenerator.GeneratePolygons(Cells, level, out triangles);
            Body.CollisionCategories = Physics.CollisionLevel;
#if CLIENT
            GenerateVertices();
#endif
        }

        public virtual void Update(float deltaTime)
        {
            if (Body.BodyType == BodyType.Static) { return; }

            Vector2 bodyPos = ConvertUnits.ToDisplayUnits(Body.Position);
            Cells.ForEach(c => c.Translation = bodyPos);

            if (!originalPos.HasValue) { originalPos = bodyPos; }

            if (moveLength > 0.0f && MoveSpeed > 0.0f)
            {
                moveState += MoveSpeed / moveLength * deltaTime;
                moveState %= MathHelper.TwoPi;

                Vector2 targetPos = ConvertUnits.ToSimUnits(originalPos.Value + moveAmount * (float)Math.Sin(moveState));            
                Body.ApplyForce((targetPos - Body.Position).ClampLength(1.0f) * Body.Mass);
            }
        }

        public bool IsPointInside(Vector2 point)
        {
            return Cells.Any(c => c.IsPointInside(point));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
#if CLIENT
            VertexBuffer?.Dispose();
            VertexBuffer = null;
#endif
        }
    }
}
