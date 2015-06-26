using FarseerPhysics;
using FarseerPhysics.Common;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Voronoi2;

namespace Subsurface
{
    class Level
    {
        public static Level Loaded
        {
            get { return loaded; }
        }

        static Level loaded;

        private int seed;

        private int siteInterval;

        const int gridCellWidth = 1000;
        List<VoronoiCell>[,] cellGrid;

        //List<Body> bodies;
        List<VoronoiCell> cells;

        BasicEffect basicEffect;

        private VertexPositionColor[] vertices;
        private VertexBuffer vertexBuffer;

        private Vector2 startPosition;
        private Vector2 endPosition;

        Rectangle borders;

        public Vector2 StartPosition
        {
            get { return startPosition; }
        }

        public Level(int seed, int width, int height, int siteInterval)
        {
            this.seed = seed;

            this.siteInterval = siteInterval;

            borders = new Rectangle(0, 0, width, height);
        }

        public static Level CreateRandom()
        {
           return new Level(100, 100000, 40000, 2000);        
        }

        public void Generate(float minWidth)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            Game1.random = new Random(seed);

            if (loaded != this && loaded != null)
            {
                loaded.Unload();                
            }

            loaded = this;

            Voronoi voronoi = new Voronoi(1.0);

            List<Vector2> sites = new List<Vector2>();
            Random rand = new Random(seed);

            float siteVariance = siteInterval * 0.8f;
            for (int x = siteInterval/2; x < borders.Width; x += siteInterval)
            {
                for (int y = siteInterval / 2; y < borders.Height; y += siteInterval)
                {
                    sites.Add(new Vector2(
                        x + (float)(Game1.random.NextDouble() - 0.5) * siteVariance, 
                        y + (float)(Game1.random.NextDouble() - 0.5) * siteVariance));
                }
            }

            Stopwatch sw2 = new Stopwatch();
            sw2.Start();

            List<GraphEdge> graphEdges = voronoi.MakeVoronoiGraph(sites, borders.Width, borders.Height);


            Debug.WriteLine("MakeVoronoiGraph: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();

            cellGrid = new List<VoronoiCell>[borders.Width / gridCellWidth, borders.Height / gridCellWidth];
            for (int x = 0; x < borders.Width / gridCellWidth; x++)
            {
                for (int y = 0; y < borders.Height / gridCellWidth; y++)
                {
                    cellGrid[x, y] = new List<VoronoiCell>();
                }
            }

            //construct voronoi cells based on the graph edges
            cells = new List<VoronoiCell>();
            foreach (GraphEdge ge in graphEdges)
            {
                for (int i = 0; i < 2; i++)
                {
                    Site site = (i == 0) ? ge.site1 : ge.site2;

                    VoronoiCell cell = cellGrid[
                        (int)Math.Floor(site.coord.x / gridCellWidth), 
                        (int)Math.Floor(site.coord.y / gridCellWidth)].Find(c => c.site == site);

                    if (cell == null)
                    {
                        cell = new VoronoiCell(site);
                        cellGrid[(int)Math.Floor(cell.Center.X / gridCellWidth), (int)Math.Floor(cell.Center.Y / gridCellWidth)].Add(cell);
                        cells.Add(cell);
                    }

                    if (ge.cell1 == null)
                    {
                        ge.cell1 = cell;
                    }
                    else
                    {
                        ge.cell2 = cell;
                    }
                    cell.edges.Add(ge);
                }
            }
            
            Debug.WriteLine("find cells: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();

            //generate a path from the left edge of the map to right edge
            Rectangle pathBorders = new Rectangle(
                borders.X + (int)minWidth, borders.Y + (int)minWidth,
                borders.Right - (int)minWidth, borders.Y + borders.Height - (int)minWidth);

            List<VoronoiCell> pathCells = GeneratePath(
                new Vector2((int)minWidth, Game1.random.Next((int)minWidth, borders.Height - (int)minWidth)),
                new Vector2(borders.Width - (int)minWidth, Game1.random.Next((int)minWidth, borders.Height - (int)minWidth)),
                cells, pathBorders, minWidth);


            //generate a couple of random paths
            for (int i = 0; i < Game1.random.Next() % 3; i++ )
            {
                pathBorders = new Rectangle(
                borders.X + siteInterval * 2, borders.Y - siteInterval * 2,
                borders.Right - siteInterval * 2, borders.Y + borders.Height - siteInterval * 2);

                Vector2 start = pathCells[Game1.random.Next(1,pathCells.Count-2)].Center;
                Vector2 end = new Vector2(ToolBox.RandomFloat(pathBorders.X, pathBorders.Right), ToolBox.RandomFloat(pathBorders.Y, pathBorders.Bottom));

                pathCells.AddRange
                (
                    GeneratePath( start,end, cells, pathBorders, 0.0f)
                );
            }

            Debug.WriteLine("path: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();

            startPosition = pathCells[0].Center;
            endPosition = pathCells[pathCells.Count - 1].Center;

            foreach (VoronoiCell cell in pathCells)
            {
                cells.Remove(cell);
            }

            GenerateLevel(cells);

            Debug.WriteLine("Generatelevel: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();

            vertexBuffer = new VertexBuffer(Game1.CurrGraphicsDevice, VertexPositionColor.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);

            vertexBuffer.SetData(vertices);

            basicEffect = new BasicEffect(Game1.CurrGraphicsDevice);
            basicEffect.VertexColorEnabled = true;

            Debug.WriteLine("Generated a map with "+sites.Count+" sites in "+sw.ElapsedMilliseconds+" ms");
        }

        private List<VoronoiCell> GeneratePath(Vector2 start, Vector2 end, List<VoronoiCell> cells, Microsoft.Xna.Framework.Rectangle limits, float minWidth, float wanderAmount = 0.3f)
        {

            Stopwatch sw2 = new Stopwatch();
            sw2.Start();
            
            //how heavily the path "steers" towards the endpoint
            //lower values will cause the path to "wander" more, higher will make it head straight to the end
            wanderAmount = MathHelper.Clamp(wanderAmount, 0.0f, 1.0f);

            List<VoronoiCell> pathCells = new List<VoronoiCell>();

            VoronoiCell currentCell = cells[FindCellIndex(start)];
            pathCells.Add(currentCell);

            VoronoiCell endCell = cells[FindCellIndex(end)];

            do
            {
                int edgeIndex = 0;

                //steer towards target
                if (Game1.random.NextDouble()>wanderAmount)
                {
                    for (int i = 0; i < currentCell.edges.Count; i++)
                    {
                        if (!IsIntersecting(currentCell.Center, end, currentCell.edges[i].point1, currentCell.edges[i].point2)) continue;
                        edgeIndex = i;
                        break;
                    }
                }
                //choose random edge (ignoring ones where the adjacent cell is outside limits)
                else
                {
                    List<GraphEdge> allowedEdges = new List<GraphEdge>();
                    
                    foreach(GraphEdge edge in currentCell.edges)
                    {
                        if (!limits.Contains(edge.AdjacentCell(currentCell).Center)) continue;
                    
                        allowedEdges.Add(edge);
                    }
                    edgeIndex = (allowedEdges.Count==0) ?
                        0 : currentCell.edges.IndexOf(allowedEdges[Game1.random.Next() % allowedEdges.Count]);
                }

                currentCell = currentCell.edges[edgeIndex].AdjacentCell(currentCell);


                pathCells.Add(currentCell);

            } while (currentCell!=endCell);

            Debug.WriteLine("genpath: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();

            List<VoronoiCell> removedCells = GetTooCloseCells(pathCells, minWidth);
            foreach (VoronoiCell removedCell in removedCells)
            {
                if (pathCells.Contains(removedCell)) continue;
                pathCells.Add(removedCell);
            }

            Debug.WriteLine("gettooclose: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();

            return pathCells;
        }

        private List<VoronoiCell> GetTooCloseCells(List<VoronoiCell> emptyCells, float minDistance)
        {
            List<VoronoiCell> tooCloseCells = new List<VoronoiCell>();
            
            Vector2 position = emptyCells[0].Center;

            if (minDistance == 0.0f) return tooCloseCells;

            float step = 100.0f;

            int targetCellIndex = 1;

            minDistance *= 0.5f;
            do
            {
                for (int x = -1; x<=1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        if (x == 0 && y == 0) continue;
                        Vector2 cornerPos = position + new Vector2(x*minDistance, y*minDistance);
                        
                        int cellIndex = FindCellIndex(cornerPos);
                        if (cellIndex == -1) continue;
                        if (!tooCloseCells.Contains(cells[cellIndex])) 
                        {
                            tooCloseCells.Add(cells[cellIndex]);               
                        }
                    }
                }

                position += Vector2.Normalize(emptyCells[targetCellIndex].Center - position) * step;

                if (Vector2.Distance(emptyCells[targetCellIndex].Center, position) < step * 2.0f) targetCellIndex++;

            } while (Vector2.Distance(position, emptyCells[emptyCells.Count - 1].Center) > step*2.0f);

            return tooCloseCells;        
        }

        /// <summary>
        /// check whether line from a to b is intersecting with line from c to b
        /// </summary>
        bool IsIntersecting(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float denominator = ((b.X - a.X) * (d.Y - c.Y)) - ((b.Y - a.Y) * (d.X - c.X));
            float numerator1 = ((a.Y - c.Y) * (d.X - c.X)) - ((a.X - c.X) * (d.Y - c.Y));
            float numerator2 = ((a.Y - c.Y) * (b.X - a.X)) - ((a.X - c.X) * (b.Y - a.Y));

            if (denominator == 0) return numerator1 == 0 && numerator2 == 0;

            float r = numerator1 / denominator;
            float s = numerator2 / denominator;

            return (r >= 0 && r <= 1) && (s >= 0 && s <= 1);
        }
        
        /// <summary>
        /// find the index of the cell which the point is inside
        /// (actually finds the cell whose center is closest, but it's always the correct cell assuming the point is inside the borders of the diagram)
        /// </summary>
        private int FindCellIndex(Vector2 position)
        {
            float closestDist = 0.0f;
            VoronoiCell closestCell = null;

            int gridPosX = (int)Math.Floor(position.X / gridCellWidth);
            int gridPosY = (int)Math.Floor(position.Y / gridCellWidth);

            int searchOffset = 1;

            for (int x = Math.Max(gridPosX-searchOffset,0); x<=Math.Min(gridPosX+searchOffset, cellGrid.GetLength(0)-1); x++)
            {
                for (int y = Math.Max(gridPosY-searchOffset,0); y<=Math.Min(gridPosY+searchOffset, cellGrid.GetLength(1)-1); y++)
                {
                    for (int i = 0; i < cellGrid[x,y].Count; i++)
                    {
                        float dist = Vector2.Distance(cellGrid[x, y][i].Center, position);
                        if (closestDist != 0.0f && dist > closestDist) continue;

                        closestDist = dist;
                        closestCell = cellGrid[x, y][i];
                    }
                }
            }



            return cells.IndexOf(closestCell);            
        }

        private void GenerateLevel(List<VoronoiCell> cells)
        {
            List<VertexPositionColor> verticeList = new List<VertexPositionColor>();
            //bodies = new List<Body>();


            List<Vector2> tempVertices = new List<Vector2>();

            int n = 0;
            foreach (VoronoiCell cell in cells)
            {
                n = (n + 30) % 255;

                tempVertices.Clear();
                foreach (GraphEdge ge in cell.edges)
                {
                    if (ge.point1 == ge.point2) continue;
                    if (!tempVertices.Contains(ge.point1)) tempVertices.Add(ge.point1);
                    if (!tempVertices.Contains(ge.point2)) tempVertices.Add(ge.point2);
                }

                if (tempVertices.Count < 3) continue;
                
                int triangleCount = tempVertices.Count - 2;

                tempVertices.Sort(new CompareCCW(cell.Center));

                int lastIndex = 1;
                for (int i = 0; i < triangleCount; i++ )
                {
                    List<Vector2> triangleVertices = new List<Vector2>();
                    
                    triangleVertices.Add(tempVertices[0]);
                    for (int j = lastIndex; j<=lastIndex+1; j++)
                    {
                        triangleVertices.Add(tempVertices[j]);
                    }
                    lastIndex += 1;
                    
                    foreach (Vector2 vertex in triangleVertices)
                    {
                        verticeList.Add(new VertexPositionColor(new Vector3(vertex, 0.0f), Color.LightGray*0.8f));//new Color(n,(n*2)%255,(n*3)%255)*0.5f));
                    }

                    bool isSame = false;
                    if (triangleVertices[0].Y == triangleVertices[1].Y && triangleVertices[1].Y == triangleVertices[2].Y) isSame = true;
                    if (triangleVertices[0].X == triangleVertices[1].X && triangleVertices[1].X == triangleVertices[2].X) isSame = true;

                    if (isSame) continue;

                    CreateBody(cell, triangleVertices);
                }


            }

            vertices = verticeList.ToArray();

            //return bodies;
        }

        private void CreateBody(VoronoiCell  cell, List<Vector2> bodyVertices)
        {
            for (int i = 0; i < bodyVertices.Count; i++)
            {
                bodyVertices[i] = ConvertUnits.ToSimUnits(bodyVertices[i]);
            }
            //get farseer 'vertices' from vectors
            Vertices _shapevertices = new Vertices(bodyVertices);
            //_shapevertices.Sort(new CompareCCW(cell.Center));

            //feed vertices array to BodyFactory.CreatePolygon to get a new farseer polygonal body
            Body _newBody = BodyFactory.CreatePolygon(Game1.world, _shapevertices, 15);
            _newBody.BodyType = BodyType.Static;
            _newBody.CollisionCategories = Physics.CollisionWall;

            cell.bodies.Add(_newBody);
        }


        Vector2 position;
        public void Move(Vector2 amount, float deltaTime)
        {
            amount = amount * deltaTime;
            position += amount;

            amount = ConvertUnits.ToSimUnits(amount);
            foreach (VoronoiCell cell in cells)
            {
                foreach (Body b in cell.bodies)
                {
                    b.SetTransform(b.Position+amount, b.Rotation);
                }  
            }
          
        }



        public void SetObserverPosition(Vector2 position)
        {
            position = position - this.position;
            int gridPosX = (int)Math.Floor(position.X / gridCellWidth);
            int gridPosY = (int)Math.Floor(position.Y / gridCellWidth);
            int searchOffset = 1;

            for (int x = 0; x < cellGrid.GetLength(0); x++)
            {
                for (int y = 0; y <cellGrid.GetLength(1); y++)
                {
                    for (int i = 0; i < cellGrid[x, y].Count; i++)
                    {
                        foreach (Body b in cellGrid[x, y][i].bodies)
                        {
                            b.Enabled = false;
                        }


                    }
                }
            }

            for (int x = Math.Max(gridPosX - searchOffset, 0); x <= Math.Min(gridPosX + searchOffset, cellGrid.GetLength(0) - 1); x++)
            {
                for (int y = Math.Max(gridPosY - searchOffset, 0); y <= Math.Min(gridPosY + searchOffset, cellGrid.GetLength(1) - 1); y++)
                {
                    for (int i = 0; i < cellGrid[x, y].Count; i++)
                    {
                        foreach (Body b in cellGrid[x, y][i].bodies)
                        {
                            b.Enabled = true;
                        }   
                    }
                }
            }
        }


        public void RenderLines(SpriteBatch spriteBatch)
        {
            GUI.DrawRectangle(spriteBatch, new Rectangle(borders.X, borders.Y-borders.Height, borders.Width, borders.Height), Color.Cyan);

            foreach (VoronoiCell cell in cells)
            {
                for (int i = 0; i<cell.edges.Count; i++)
                {
                    Vector2 start = cell.edges[i].point1+position;
                    start.Y = -start.Y;

                    Vector2 end = cell.edges[i].point2+position;
                    end.Y = -end.Y;

                    GUI.DrawLine(spriteBatch, start, end, Color.Red);
                }
            }
        }

        public void Render(GraphicsDevice graphicsDevice, Camera cam)
        {
            if (vertices == null) return;
            if (vertices.Length <= 0) return;

            basicEffect.World = Matrix.CreateTranslation(new Vector3(position, 0.0f))*cam.ShaderTransform
                * Matrix.CreateOrthographic(Game1.GraphicsWidth, Game1.GraphicsHeight, -1, 1) * 0.5f;


            basicEffect.CurrentTechnique.Passes[0].Apply();  
         

            graphicsDevice.DrawUserPrimitives<VertexPositionColor>(PrimitiveType.TriangleList, vertices, 0, (int)Math.Floor(vertices.Length / 3.0f));
        }

        private void Unload()
        {
            foreach (VoronoiCell cell in cells)
            {
                foreach (Body b in cell.bodies)
                {
                    Game1.world.RemoveBody(b);
                }
            }


            //bodies = null;

            vertices = null;

            cells = null;

            vertexBuffer.Dispose();
            vertexBuffer = null;
        }
    }
    
    class CompareCCW : IComparer<Vector2>
    {
        private Vector2 center;

        public CompareCCW(Vector2 center)
        {
            this.center = center;
        }
        public int Compare(Vector2 a, Vector2 b)
        {
            if (a.X - center.X >= 0 && b.X - center.X < 0) return -1;
            if (a.X - center.X < 0 && b.X - center.X >= 0) return 1;
            if (a.X - center.X == 0 && b.X - center.X == 0)
            {
                if (a.Y - center.Y >= 0 || b.Y - center.Y >= 0) return Math.Sign(b.Y-a.Y);
                return Math.Sign(a.Y-b.Y);
            }

            // compute the cross product of vectors (center -> a) x (center -> b)
            float det = (a.X - center.X) * (b.Y - center.Y) - (b.X - center.X) * (a.Y - center.Y);
            if (det < 0) return -1;
            if (det > 0) return 1;

            // points a and b are on the same line from the center
            // check which point is closer to the center
            float d1 = (a.X - center.X) * (a.X - center.X) + (a.Y - center.Y) * (a.Y - center.Y);
            float d2 = (b.X - center.X) * (b.X - center.X) + (b.Y - center.Y) * (b.Y - center.Y);
            return Math.Sign(d2-d1);
        }
    }

}
