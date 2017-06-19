using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Voronoi2;

namespace Barotrauma.RuinGeneration
{
    abstract class RuinShape
    {
        protected Rectangle rect;

        public Rectangle Rect
        {
            get { return rect; }
        }


        public int DistanceFromEntrance
        {
            get;
            protected set;
        }

        public Vector2 Center
        {
            get { return rect.Center.ToVector2(); }
        }

        public List<Line> Walls;

        public virtual void CreateWalls() { }

        public Alignment GetLineAlignment(Line line)
        {
            if (line.A.Y == line.B.Y)
            {
                if (line.A.Y > rect.Center.Y && line.B.Y > rect.Center.Y)
                {
                    return Alignment.Bottom;
                }
                else if (line.A.Y < rect.Center.Y && line.B.Y < rect.Center.Y)
                {
                    return Alignment.Top;
                }
            }
            else
            {
                if (line.A.X < rect.Center.X && line.B.X < rect.Center.X)
                {
                    return Alignment.Left;
                }
                else if (line.A.X > rect.Center.X && line.B.X > rect.Center.X)
                {
                    return Alignment.Right;
                }
            }

            return Alignment.Center;
        }

         /// <summary>
        /// Goes through a list of line segments and "clips off" all parts of the lines that are inside the rectangle
        /// </summary>
        public void SplitLines(Rectangle rectangle)
        {
            List<Line> newLines = new List<Line>();

            foreach (Line line in Walls)
            {
                if (line.A.X == line.B.X) //vertical line
                {
                    //line doesn't intersect the rectangle
                    if (rectangle.X > line.A.X || rectangle.Right < line.A.X ||
                        rectangle.Y > line.B.Y || rectangle.Bottom < line.A.Y)
                    {
                        newLines.Add(line);
                    }
                    else if (line.A.Y > rectangle.Y && line.B.Y < rectangle.Bottom)
                    {
                        continue;
                    }
                    //point A is within the rectangle -> cut a portion from the top of the line
                    else if (line.A.Y >= rectangle.Y && line.A.Y <= rectangle.Bottom)
                    {
                        newLines.Add(new Line(new Vector2(line.A.X, rectangle.Bottom), line.B, line.Type));
                    }
                    //point B is within the rectangle -> cut a portion from the bottom of the line
                    else if (line.B.Y >= rectangle.Y && line.B.Y <= rectangle.Bottom)
                    {
                        newLines.Add(new Line(line.A, new Vector2(line.A.X, rectangle.Y), line.Type));
                    }
                    //rect is in between the lines -> split the line into two
                    else
                    {
                        newLines.Add(new Line(line.A, new Vector2(line.A.X, rectangle.Y), line.Type));
                        newLines.Add(new Line(new Vector2(line.A.X, rectangle.Bottom), line.B, line.Type));
                    }
                }
                else if (line.A.Y == line.B.Y) //horizontal line
                {
                    //line doesn't intersect the rectangle
                    if (rectangle.X > line.B.X || rectangle.Right < line.A.X ||
                        rectangle.Y > line.A.Y || rectangle.Bottom < line.A.Y)
                    {

                        newLines.Add(line);
                    }
                    else if (line.A.X > rectangle.X && line.B.X < rectangle.Right)
                    {
                        continue;
                    }
                    //point A is within the rectangle -> cut a portion from the left side of the line
                    else if (line.A.X >= rectangle.X && line.A.X <= rectangle.Right)
                    {
                        newLines.Add(new Line(new Vector2(rectangle.Right, line.A.Y), line.B, line.Type));
                    }
                    //point B is within the rectangle -> cut a portion from the right side of the line
                    else if (line.B.X >= rectangle.X && line.B.X <= rectangle.Right)
                    {
                        newLines.Add(new Line(line.A, new Vector2(rectangle.X, line.A.Y), line.Type));
                    }
                    //rect is in between the lines -> split the line into two
                    else
                    {
                        newLines.Add(new Line(line.A, new Vector2(rectangle.X, line.A.Y), line.Type));
                        newLines.Add(new Line(new Vector2(rectangle.Right, line.A.Y), line.B, line.Type));
                    }
                }
                else
                {
                    DebugConsole.ThrowError("Error in StructureGenerator.SplitLines - lines must be axis aligned");
                }

            }

            Walls = newLines;
        }
    }

    struct Line
    {
        public readonly Vector2 A, B;

        public readonly RuinStructureType Type;

        public Line(Vector2 a, Vector2 b, RuinStructureType type)
        {
            Debug.Assert(a.X <= b.X);
            Debug.Assert(a.Y <= b.Y);

            A = a;
            B = b;
            Type = type;
        }
    }  

    partial class Ruin
    {
        private List<BTRoom> rooms;
        private List<Corridor> corridors;

        private List<Line> walls;

        private List<RuinShape> allShapes;

        public List<RuinShape> RuinShapes
        {
            get { return allShapes; }
        }

        public List<Line> Walls
        {
            get { return walls; }
        }

        public Rectangle Area
        {
            get;
            private set;
        }

        public Ruin(VoronoiCell closestPathCell, List<VoronoiCell> caveCells, Rectangle area)
        {
            Area = area;

            corridors = new List<Corridor>();
            rooms = new List<BTRoom>();

            walls = new List<Line>();

            allShapes = new List<RuinShape>();

            Generate(closestPathCell, caveCells, area);
        }
             
        public void Generate(VoronoiCell closestPathCell, List<VoronoiCell> caveCells, Rectangle area)
        {
            corridors.Clear();
            rooms.Clear();

            //area = new Rectangle(area.X, area.Y - area.Height, area.Width, area.Height);

            int iterations = Rand.Range(3, 4, false);

            float verticalProbability = Rand.Range(0.4f, 0.6f, false);
            
            BTRoom baseRoom = new BTRoom(area);

            rooms = new List<BTRoom> { baseRoom };

            for (int i = 0; i < iterations; i++)
            {
                rooms.ForEach(l => l.Split(0.3f, verticalProbability, 300));

                rooms = baseRoom.GetLeaves();
            }

            foreach (BTRoom leaf in rooms)
            {
                leaf.Scale
                    (
                        new Vector2(Rand.Range(0.5f, 0.9f, false), Rand.Range(0.5f, 0.9f, false))
                    );
            }
            
            baseRoom.GenerateCorridors(200, 256, corridors);

            walls = new List<Line>();

            rooms.ForEach(leaf => 
            { 
                leaf.CreateWalls();
                //walls.AddRange(leaf.Walls); 
            });

            //---------------------------

            BTRoom entranceRoom = null;
            float shortestDistance = 0.0f;
            foreach (BTRoom leaf in rooms)
            {
                float distance = Vector2.Distance(leaf.Rect.Center.ToVector2(), closestPathCell.Center);
                if (entranceRoom == null || distance < shortestDistance)
                {
                    entranceRoom = leaf;
                    shortestDistance = distance;
                }
            }

            rooms.Remove(entranceRoom);

            //---------------------------

            foreach (BTRoom leaf in rooms)
            {
                foreach (Corridor corridor in corridors)
                {
                    leaf.SplitLines(corridor.Rect);
                }

                walls.AddRange(leaf.Walls);
            }


            foreach (Corridor corridor in corridors)
            {
                corridor.CreateWalls();

                foreach (BTRoom leaf in rooms)
                {
                    corridor.SplitLines(leaf.Rect);
                }

                foreach (Corridor corridor2 in corridors)
                {
                    if (corridor == corridor2) continue;
                    corridor.SplitLines(corridor2.Rect);
                }


                walls.AddRange(corridor.Walls);
            }

            //leaves.Remove(entranceRoom);

            BTRoom.CalculateDistancesFromEntrance(entranceRoom, corridors);

            allShapes = GenerateStructures(caveCells);
        }

        private List<RuinShape> GenerateStructures(List<VoronoiCell> caveCells)
        {
            List<RuinShape> shapes = new List<RuinShape>(rooms);
            shapes.AddRange(corridors);
            
            foreach (RuinShape leaf in shapes)
            {
                RuinStructureType wallType = RuinStructureType.Wall;

                if (!(leaf is BTRoom))
                {
                    wallType = RuinStructureType.CorridorWall;
                }
                //rooms further from the entrance are more likely to have hard-to-break walls
                else if (Rand.Range(0.0f, leaf.DistanceFromEntrance, false) > 1.5f)
                {
                    wallType = RuinStructureType.HeavyWall;
                }

                //generate walls  --------------------------------------------------------------
                foreach (Line wall in leaf.Walls)
                {
                    var structurePrefab = RuinStructure.GetRandom(wallType, leaf.GetLineAlignment(wall));
                    if (structurePrefab == null) continue;

                    float radius = (wall.A.X == wall.B.X) ? 
                        (structurePrefab.Prefab as StructurePrefab).Size.X * 0.5f : 
                        (structurePrefab.Prefab as StructurePrefab).Size.Y * 0.5f;

                    Rectangle rect = new Rectangle(
                        (int)(wall.A.X - radius), 
                        (int)(wall.B.Y + radius), 
                        (int)((wall.B.X - wall.A.X) + radius*2.0f), 
                        (int)((wall.B.Y - wall.A.Y) + radius*2.0f));

                    //cut a section off from both ends of a horizontal wall to get nicer looking corners 
                    if (wall.A.Y == wall.B.Y)
                    {
                        rect.Inflate(-32, 0);
                        if (rect.Width < Submarine.GridSize.X) continue;
                    }

                    var structure = new Structure(rect, structurePrefab.Prefab as StructurePrefab, null);
                    structure.MoveWithLevel = true;
                    structure.SetCollisionCategory(Physics.CollisionLevel);
                }

                //generate backgrounds --------------------------------------------------------------
                var background = RuinStructure.GetRandom(RuinStructureType.Back, Alignment.Center);
                if (background == null) continue;

                Rectangle backgroundRect = new Rectangle(leaf.Rect.X, leaf.Rect.Y + leaf.Rect.Height, leaf.Rect.Width, leaf.Rect.Height);

                new Structure(backgroundRect, (background.Prefab as StructurePrefab), null).MoveWithLevel = true;

            }

            //generate props --------------------------------------------------------------
            for (int i = 0; i < shapes.Count*2; i++ )
            {
                Alignment[] alignments = new Alignment[] { Alignment.Top, Alignment.Bottom, Alignment.Right, Alignment.Left, Alignment.Center };

                var prop = RuinStructure.GetRandom(RuinStructureType.Prop, alignments[Rand.Int(alignments.Length, false)]);

                Vector2 size = (prop.Prefab is StructurePrefab) ? ((StructurePrefab)prop.Prefab).Size : Vector2.Zero;

                var shape = shapes[Rand.Int(shapes.Count, false)];

                Vector2 position = shape.Rect.Center.ToVector2();
                if (prop.Alignment.HasFlag(Alignment.Top))
                {
                    position = new Vector2(Rand.Range(shape.Rect.X+size.X, shape.Rect.Right - size.X, false), shape.Rect.Bottom - 64);
                }
                else if (prop.Alignment.HasFlag(Alignment.Bottom))
                {
                    position = new Vector2(Rand.Range(shape.Rect.X + size.X, shape.Rect.Right - size.X, false), shape.Rect.Top + 64);
                }
                else if (prop.Alignment.HasFlag(Alignment.Right))
                {
                    position = new Vector2(shape.Rect.Right - 64, Rand.Range(shape.Rect.Y + size.X, shape.Rect.Bottom - size.Y, false));
                }
                else if (prop.Alignment.HasFlag(Alignment.Left))
                {
                    position = new Vector2(shape.Rect.X + 64, Rand.Range(shape.Rect.Y + size.X, shape.Rect.Bottom - size.Y, false));
                }

                if (prop.Prefab is ItemPrefab)
                {
                    var item = new Item((ItemPrefab)prop.Prefab, position, null);
                    item.MoveWithLevel = true;
                }
                else
                {
                    new Structure(new Rectangle(
                        (int)(position.X - size.X/2.0f), (int)(position.Y + size.Y/2.0f),
                        (int)size.X, (int)size.Y),
                        prop.Prefab as StructurePrefab, null).MoveWithLevel = true;
                }
            }


            //generate doors & sensors that close them -------------------------------------------------------------

            var sensorPrefab = ItemPrefab.list.Find(ip => ip.Name == "Alien Motion Sensor") as ItemPrefab;
            var wirePrefab = ItemPrefab.list.Find(ip => ip.Name == "Wire") as ItemPrefab;           

            foreach (Corridor corridor in corridors)
            {
                var doorPrefab = RuinStructure.GetRandom(corridor.IsHorizontal ? RuinStructureType.Door : RuinStructureType.Hatch, Alignment.Center);
                if (doorPrefab == null) continue;

                //find all walls that are parallel to the corridor
                var suitableWalls = corridor.IsHorizontal ? 
                    corridor.Walls.FindAll(c => c.A.Y == c.B.Y) : corridor.Walls.FindAll(c => c.A.X == c.B.X);

                if (!suitableWalls.Any()) continue;

                Vector2 doorPos = corridor.Center;

                //choose a random wall to place the door next to
                var wall = suitableWalls[Rand.Int(suitableWalls.Count, false)];
                if (corridor.IsHorizontal)
                {
                    doorPos.X = (wall.A.X + wall.B.X) / 2.0f;
                }
                else
                {
                    doorPos.Y = (wall.A.Y + wall.B.Y) / 2.0f;
                }

                var door = new Item(doorPrefab.Prefab as ItemPrefab, doorPos, null);
                door.MoveWithLevel = true;

                door.GetComponent<Items.Components.Door>().IsOpen = Rand.Range(0.0f, 1.0f, false) < 0.8f;

                if (sensorPrefab == null || wirePrefab == null) continue;

                var sensorRoom = corridor.ConnectedRooms.FirstOrDefault(r => r != null && rooms.Contains(r));
                if (sensorRoom == null) continue;

                var sensor = new Item(sensorPrefab, new Vector2(
                    Rand.Range(sensorRoom.Rect.X, sensorRoom.Rect.Right, false), 
                    Rand.Range(sensorRoom.Rect.Y, sensorRoom.Rect.Bottom,false)), null);
                sensor.MoveWithLevel = true;

                var wire = new Item(wirePrefab, sensorRoom.Center, null).GetComponent<Items.Components.Wire>();
                wire.Item.MoveWithLevel = false;

                var conn1 = door.Connections.Find(c => c.Name == "set_state");
                conn1.AddLink(0, wire);
                wire.Connect(conn1, false);

                var conn2 = sensor.Connections.Find(c => c.Name == "state_out");
                conn2.AddLink(0, wire);
                wire.Connect(conn2, false);
            }

            return shapes;
        }
    }
}
