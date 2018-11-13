using FarseerPhysics;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using System;
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
            if (line.IsHorizontal)
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
        /// Goes through all the walls of the ruin shape and clips off parts that are inside the rectangle
        /// </summary>
        public void SplitWalls(Rectangle rectangle)
        {
            List<Line> newLines = new List<Line>();

            foreach (Line line in Walls)
            {
                if (!line.IsHorizontal) //vertical line
                {
                    //line doesn't intersect the rectangle
                    if (rectangle.X > line.A.X || rectangle.Right < line.A.X ||
                        rectangle.Y > line.B.Y || rectangle.Bottom < line.A.Y)
                    {
                        newLines.Add(line);
                    }
                    //line completely inside the rectangle, no need to create a wall at all
                    else if (line.A.Y >= rectangle.Y && line.B.Y <= rectangle.Bottom)
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
                else
                {
                    //line doesn't intersect the rectangle
                    if (rectangle.X > line.B.X || rectangle.Right < line.A.X ||
                        rectangle.Y > line.A.Y || rectangle.Bottom < line.A.Y)
                    {

                        newLines.Add(line);
                    }
                    else if (line.A.X >= rectangle.X && line.B.X <= rectangle.Right)
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
            }

            Walls = newLines;
        }

        public void MirrorX(Vector2 mirrorOrigin)
        {
            rect.X = (int)(mirrorOrigin.X + (mirrorOrigin.X - rect.Right));
            for (int i = 0; i < Walls.Count; i++)
            {
                Walls[i].A = new Vector2(mirrorOrigin.X + (mirrorOrigin.X - Walls[i].A.X), Walls[i].A.Y);
                Walls[i].B = new Vector2(mirrorOrigin.X + (mirrorOrigin.X - Walls[i].B.X), Walls[i].B.Y);

                if (Walls[i].B.X < Walls[i].A.X)
                {
                    var temp = Walls[i].A.X;
                    Walls[i].A.X = Walls[i].B.X;
                    Walls[i].B.X = temp;
                }
            }
        }
    }

    class Line
    {
        public Vector2 A, B;

        public readonly RuinStructureType Type;

        public bool IsHorizontal
        {
            get { return Math.Abs(A.Y - B.Y) < Math.Abs(A.X - B.X); }
        }

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

        private RuinGenerationParams generationParams;

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

        public Ruin(VoronoiCell closestPathCell, List<VoronoiCell> caveCells, Rectangle area, bool mirror = false)
        {
            generationParams = RuinGenerationParams.GetRandom();

            Area = area;
            corridors = new List<Corridor>();
            rooms = new List<BTRoom>();
            walls = new List<Line>();
            allShapes = new List<RuinShape>();
            Generate(closestPathCell, caveCells, area, mirror);
        }
             
        public void Generate(VoronoiCell closestPathCell, List<VoronoiCell> caveCells, Rectangle area, bool mirror = false)
        {
            corridors.Clear();
            rooms.Clear();
            
            int iterations = Rand.Range(generationParams.RoomDivisionIterationsMin, generationParams.RoomDivisionIterationsMax, Rand.RandSync.Server);
            float verticalProbability = generationParams.VerticalSplitProbability;

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
                        new Vector2(
                            Rand.Range(generationParams.RoomWidthRange.X, generationParams.RoomWidthRange.Y, Rand.RandSync.Server), 
                            Rand.Range(generationParams.RoomHeightRange.X, generationParams.RoomHeightRange.Y, Rand.RandSync.Server))
                    );
            }

            baseRoom.GenerateCorridors(generationParams.CorridorWidthRange.X, generationParams.CorridorWidthRange.Y, corridors);

            walls = new List<Line>();
            rooms.ForEach(leaf => { leaf.CreateWalls(); });

            //---------------------------

            BTRoom entranceRoom = null;
            float shortestDistance = 0.0f;
            foreach (BTRoom leaf in rooms)
            {
                Vector2 leafPos = leaf.Rect.Center.ToVector2();
                if (mirror)
                {
                    leafPos.X = area.Center.X + (area.Center.X - leafPos.X);
                }
                float distance = Vector2.Distance(leafPos, closestPathCell.Center);
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
                    leaf.SplitWalls(corridor.Rect);
                }

                walls.AddRange(leaf.Walls);
            }


            foreach (Corridor corridor in corridors)
            {
                corridor.CreateWalls();

                foreach (BTRoom leaf in rooms)
                {
                    corridor.SplitWalls(leaf.Rect);
                }

                foreach (Corridor corridor2 in corridors)
                {
                    if (corridor == corridor2) continue;
                    corridor.SplitWalls(corridor2.Rect);
                }
                walls.AddRange(corridor.Walls);
            }
            
            BTRoom.CalculateDistancesFromEntrance(entranceRoom, corridors);

            allShapes = GenerateStructures(caveCells, area, mirror);
        }

        private List<RuinShape> GenerateStructures(List<VoronoiCell> caveCells, Rectangle ruinArea, bool mirror)
        {
            List<RuinShape> shapes = new List<RuinShape>(rooms);
            shapes.AddRange(corridors);

            if (mirror)
            {
                foreach (RuinShape shape in shapes)
                {
                    shape.MirrorX(ruinArea.Center.ToVector2());
                }
            }
            
            foreach (RuinShape leaf in shapes)
            {
                RuinStructureType wallType = RuinStructureType.Wall;

                if (!(leaf is BTRoom))
                {
                    wallType = RuinStructureType.CorridorWall;
                }
                //rooms further from the entrance are more likely to have hard-to-break walls
                else if (Rand.Range(0.0f, leaf.DistanceFromEntrance, Rand.RandSync.Server) > 1.5f)
                {
                    wallType = RuinStructureType.HeavyWall;
                }

                //generate walls  --------------------------------------------------------------
                foreach (Line wall in leaf.Walls)
                {
                    var structurePrefab = generationParams.GetRandomStructure(wallType, leaf.GetLineAlignment(wall));
                    if (structurePrefab == null) continue;

                    float radius = (wall.A.X == wall.B.X) ?
                        (structurePrefab.Prefab as StructurePrefab).Size.X * 0.5f :
                        (structurePrefab.Prefab as StructurePrefab).Size.Y * 0.5f;

                    Rectangle rect = new Rectangle(
                        (int)(wall.A.X - radius),
                        (int)(wall.B.Y + radius),
                        (int)((wall.B.X - wall.A.X) + radius * 2.0f),
                        (int)((wall.B.Y - wall.A.Y) + radius * 2.0f));

                    //cut a section off from both ends of a horizontal wall to get nicer looking corners 
                    if (wall.A.Y == wall.B.Y)
                    {
                        rect.Inflate(-32, 0);
                        if (rect.Width < Submarine.GridSize.X) continue;
                    }

                    var structure = new Structure(rect, structurePrefab.Prefab as StructurePrefab, null)
                    {
                        ShouldBeSaved = false
                    };
                    structure.SetCollisionCategory(Physics.CollisionLevel);
                }

                //generate backgrounds --------------------------------------------------------------
                var background = generationParams.GetRandomStructure(RuinStructureType.Back, Alignment.Center);
                if (background == null) continue;

                Rectangle backgroundRect = new Rectangle(leaf.Rect.X, leaf.Rect.Y + leaf.Rect.Height, leaf.Rect.Width, leaf.Rect.Height);

                new Structure(backgroundRect, (background.Prefab as StructurePrefab), null)
                {
                    ShouldBeSaved = false
                };

                var submarineBlocker = BodyFactory.CreateRectangle(GameMain.World,
                    ConvertUnits.ToSimUnits(leaf.Rect.Width),
                    ConvertUnits.ToSimUnits(leaf.Rect.Height),
                    1, ConvertUnits.ToSimUnits(leaf.Center));

                submarineBlocker.IsStatic = true;
                submarineBlocker.CollisionCategories = Physics.CollisionWall;
                submarineBlocker.CollidesWith = Physics.CollisionWall;
            }

            List<RuinShape> doorlessRooms = new List<RuinShape>(shapes);

            //generate doors & sensors that close them -------------------------------------------------------------

            var sensorPrefab = MapEntityPrefab.Find(null, "alienmotionsensor") as ItemPrefab;
            var wirePrefab = MapEntityPrefab.Find(null, "wire") as ItemPrefab;

            foreach (Corridor corridor in corridors)
            {
                var doorPrefab = generationParams.GetRandomStructure(corridor.IsHorizontal ? RuinStructureType.Door : RuinStructureType.Hatch, Alignment.Center);
                if (doorPrefab == null) continue;

                //find all walls that are parallel to the corridor
                var suitableWalls = corridor.IsHorizontal ?
                    corridor.Walls.FindAll(c => c.A.Y == c.B.Y) : corridor.Walls.FindAll(c => c.A.X == c.B.X);

                if (!suitableWalls.Any()) continue;

                doorlessRooms.Remove(corridor);
                Vector2 doorPos = corridor.Center;

                //choose a random wall to place the door next to
                var wall = suitableWalls[Rand.Int(suitableWalls.Count, Rand.RandSync.Server)];
                if (corridor.IsHorizontal)
                {
                    doorPos.X = (wall.A.X + wall.B.X) / 2.0f;
                }
                else
                {
                    doorPos.Y = (wall.A.Y + wall.B.Y) / 2.0f;
                }

                var door = new Item(doorPrefab.Prefab as ItemPrefab, doorPos, null)
                {
                    ShouldBeSaved = false
                };

                door.GetComponent<Items.Components.Door>().IsOpen = Rand.Range(0.0f, 1.0f, Rand.RandSync.Server) < 0.8f;

                if (sensorPrefab == null || wirePrefab == null) continue;

                var sensorRoom = corridor.ConnectedRooms.FirstOrDefault(r => r != null && rooms.Contains(r));
                if (sensorRoom == null) continue;

                var sensor = new Item(sensorPrefab, new Vector2(
                    Rand.Range(sensorRoom.Rect.X, sensorRoom.Rect.Right, Rand.RandSync.Server),
                    Rand.Range(sensorRoom.Rect.Y, sensorRoom.Rect.Bottom, Rand.RandSync.Server)), null)
                {
                    ShouldBeSaved = false
                };

                var wire = new Item(wirePrefab, sensorRoom.Center, null).GetComponent<Items.Components.Wire>();
                wire.Item.ShouldBeSaved = false;

                var conn1 = door.Connections.Find(c => c.Name == "set_state");
                conn1.SetWire(0, wire);
                wire.Connect(conn1, false);

                var conn2 = sensor.Connections.Find(c => c.Name == "state_out");
                conn2.SetWire(0, wire);
                wire.Connect(conn2, false);
            }


            //generate props --------------------------------------------------------------
            for (int i = 0; i < shapes.Count * 2; i++)
            {
                Alignment[] alignments = new Alignment[] { Alignment.Top, Alignment.Bottom, Alignment.Right, Alignment.Left, Alignment.Center };

                var prop = generationParams.GetRandomStructure(RuinStructureType.Prop, alignments[Rand.Int(alignments.Length, Rand.RandSync.Server)]);
                if (prop == null) continue;

                Vector2 size = (prop.Prefab is StructurePrefab) ? ((StructurePrefab)prop.Prefab).Size : Vector2.Zero;

                //if the prop is placed at the center of the room, we have to use a room without a door (because they're also placed at the center)
                var shape = prop.Alignment.HasFlag(Alignment.Center) ?
                    doorlessRooms[Rand.Int(doorlessRooms.Count, Rand.RandSync.Server)] :
                    shapes[Rand.Int(shapes.Count, Rand.RandSync.Server)];

                Vector2 position = shape.Rect.Center.ToVector2();
                if (prop.Alignment.HasFlag(Alignment.Top))
                {
                    position = new Vector2(Rand.Range(shape.Rect.X + size.X, shape.Rect.Right - size.X, Rand.RandSync.Server), shape.Rect.Bottom - 64);
                }
                else if (prop.Alignment.HasFlag(Alignment.Bottom))
                {
                    position = new Vector2(Rand.Range(shape.Rect.X + size.X, shape.Rect.Right - size.X, Rand.RandSync.Server), shape.Rect.Top + 64);
                }
                else if (prop.Alignment.HasFlag(Alignment.Right))
                {
                    position = new Vector2(shape.Rect.Right - 64, Rand.Range(shape.Rect.Y + size.X, shape.Rect.Bottom - size.Y, Rand.RandSync.Server));
                }
                else if (prop.Alignment.HasFlag(Alignment.Left))
                {
                    position = new Vector2(shape.Rect.X + 64, Rand.Range(shape.Rect.Y + size.X, shape.Rect.Bottom - size.Y, Rand.RandSync.Server));
                }

                if (prop.Prefab is ItemPrefab)
                {
                    new Item((ItemPrefab)prop.Prefab, position, null);
                }
                else
                {
                    new Structure(new Rectangle(
                        (int)(position.X - size.X / 2.0f), (int)(position.Y + size.Y / 2.0f),
                        (int)size.X, (int)size.Y),
                        prop.Prefab as StructurePrefab, null)
                    {
                        ShouldBeSaved = false
                    };
                }
            }

            return shapes;
        }
    }
}
