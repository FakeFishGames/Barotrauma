using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Voronoi2;

namespace Barotrauma
{
    class WrappingWall
    {

        public const float WallWidth = 20000.0f;

        public VertexPositionTexture[] Vertices;

        private Vector2 midPos;
        private int slot;

        private Vector2 offset;

        private List<VoronoiCell> cells;

        public Vector2 Offset
        {
            get { return offset; }
        }
        
        public List<VoronoiCell> Cells
        {
            get { return cells; }
        }

        public Vector2 MidPos
        {
            get { return midPos; }
        }

        public WrappingWall(List<VoronoiCell> pathCells, List<VoronoiCell> mapCells, float maxY, int dir = -1)
        {
            cells = new List<VoronoiCell>();

            VoronoiCell lowestPathCell = null;
            foreach (VoronoiCell pathCell in pathCells)
            {
                if (lowestPathCell == null || pathCell.Center.Y < lowestPathCell.Center.Y)
                {
                    lowestPathCell = pathCell;
                }
            }

            float bottomY = Math.Max(lowestPathCell.Center.Y, maxY);

            VoronoiCell edgeCell = null;
            foreach (VoronoiCell cell in mapCells)
            {
                if (cell.Center.Y > bottomY) continue;
                if (edgeCell == null
                    || (dir < 0 && cell.Center.X < edgeCell.Center.X)
                    || (dir > 0 && cell.Center.X > edgeCell.Center.X))
                {
                    edgeCell = cell;
                }
            }

            Vector2 wallSectionSize = new Vector2(2000.0f, 2000.0f);
            Vector2 startPos = (dir < 0) ?
                edgeCell.Center + Vector2.UnitX * WallWidth * dir :
                edgeCell.Center + WallWidth * Vector2.UnitX * (dir - 1);

            midPos = startPos + Vector2.UnitX * WallWidth/2;

            List<Vector2> bottomVertices = new List<Vector2>();

            for (float x = 0; x <= WallWidth; x += wallSectionSize.X)
            {
                Vector2 center = new Vector2(startPos.X + x, edgeCell.Center.Y);
                float distFromCenter = Math.Abs(x - WallWidth / 2);
                float distFromEdge = WallWidth / 2 - distFromCenter;
                float normalizedDist = distFromEdge / (WallWidth / 2);

                float variance = 1000.0f * normalizedDist;
                bottomVertices.Add(center + new Vector2(Rand.Range(-variance, variance, false), Rand.Range(-variance, variance, false)*2.0f));
            }

            for (int i = 1; i < bottomVertices.Count; i++)
            {
                Vector2[] vertices = new Vector2[4];
                vertices[0] = bottomVertices[i];
                vertices[1] = bottomVertices[i - 1];
                vertices[2] = vertices[1] + Vector2.UnitY * wallSectionSize.Y;
                vertices[3] = vertices[0] + Vector2.UnitY * wallSectionSize.Y;

                VoronoiCell wallCell = new VoronoiCell(vertices);
                wallCell.edges[1].cell1 = wallCell;
                wallCell.edges[3].cell1 = wallCell;
                if (i > 1)
                {
                    wallCell.edges[1].cell2 = cells[i - 2];
                    cells[i - 2].edges[3].cell2 = wallCell;
                }

                cells.Add(wallCell);
            }
        }


        public static void UpdateWallShift(Vector2 pos, WrappingWall[,] walls)
        {
            if (pos.X < walls[0, 1].midPos.X && walls[0,0].midPos.X > pos.X)
            {
                walls[0, 0].Shift(-2);

                var temp = walls[0, 0];
                walls[0, 0] = walls[0, 1];
                walls[0, 1] = temp;
            }
            else if (pos.X > walls[0, 0].midPos.X && walls[0,1].midPos.X < pos.X && walls[0,1].slot<0)
            {
                walls[0, 1].Shift(2);

                var temp = walls[0, 0];
                walls[0, 0] = walls[0, 1];
                walls[0, 1] = temp;
            }
            else if (pos.X > walls[1, 1].midPos.X && walls[1,0].midPos.X < pos.X)
            {
                walls[1, 0].Shift(2);

                var temp = walls[1, 0];
                walls[1, 0] = walls[1, 1];
                walls[1, 1] = temp;
            }
            else if (pos.X < walls[1, 0].midPos.X && walls[1, 1].midPos.X > pos.X && walls[1, 1].slot > 0)
            {
                walls[1, 1].Shift(-2);

                var temp = walls[0, 0];
                walls[1, 0] = walls[1, 1];
                walls[1, 1] = temp;
            }
        }

        public void Shift(int amount)
        {
            slot += amount;

            Vector2 moveAmount = Vector2.UnitX * WallWidth * amount;
            Vector2 simMoveAmount = ConvertUnits.ToSimUnits(moveAmount);

            foreach (VoronoiCell cell in cells)
            {
                cell.body.SetTransform(cell.body.Position + simMoveAmount, 0.0f);
            }

            midPos += moveAmount;
            offset += moveAmount;
        }
    }
}
