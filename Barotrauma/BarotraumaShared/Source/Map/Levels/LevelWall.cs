using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Voronoi2;

namespace Barotrauma
{
    partial class LevelWall : IDisposable
    {        
        private List<VoronoiCell> cells;
                
        public List<VoronoiCell> Cells
        {
            get { return cells; }
        }

        private List<Body> bodies;
        
        public LevelWall(List<Vector2> edgePositions, Vector2 extendAmount, Color color)
        {
            cells = new List<VoronoiCell>();

            for (int i = 0; i < edgePositions.Count - 1; i++)
            {
                Vector2[] vertices = new Vector2[4];
                vertices[0] = edgePositions[i];
                vertices[1] = edgePositions[i + 1];
                vertices[2] = vertices[0] + extendAmount;
                vertices[3] = vertices[1] + extendAmount;

                VoronoiCell wallCell = new VoronoiCell(vertices);
                wallCell.edges[0].cell1 = wallCell;
                wallCell.edges[1].cell1 = wallCell;
                wallCell.edges[2].cell1 = wallCell;
                wallCell.edges[3].cell1 = wallCell;

                wallCell.edges[0].isSolid = true;

                if (i > 1)
                {
                    wallCell.edges[3].cell2 = cells[i - 1];
                    cells[i - 1].edges[1].cell2 = wallCell;
                }

                cells.Add(wallCell);
            }

            List<Vector2[]> triangles;
            bodies = CaveGenerator.GeneratePolygons(cells, out triangles, false);

#if CLIENT
            List<VertexPositionTexture> bodyVertices = CaveGenerator.GenerateRenderVerticeList(triangles);

            SetBodyVertices(bodyVertices.ToArray(), color);
            SetWallVertices(CaveGenerator.GenerateWallShapes(cells), color);
#endif
        }

        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
#if CLIENT
            if (wallVertices != null)
            {
                wallVertices.Dispose();
                wallVertices = null;
            }
            if (bodyVertices != null)
            {
                BodyVertices.Dispose();
                bodyVertices = null;
            }
#endif

            if (bodies != null)
            {
                bodies.Clear();
                bodies = null;
            }
        }
    }
}
