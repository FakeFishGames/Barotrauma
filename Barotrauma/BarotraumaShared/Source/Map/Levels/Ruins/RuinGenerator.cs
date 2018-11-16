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
            set;
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

        public readonly RuinEntityType Type;

        public bool IsHorizontal
        {
            get { return Math.Abs(A.Y - B.Y) < Math.Abs(A.X - B.X); }
        }

        public Line(Vector2 a, Vector2 b, RuinEntityType type)
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
                rooms.ForEach(l => l.Split(0.3f, verticalProbability, generationParams.MinSplitWidth));
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
            
            BTRoom.CalculateDistancesFromEntrance(entranceRoom, rooms, corridors);

            allShapes = GenerateRuinEntities(caveCells, area, mirror);
        }

        private List<RuinShape> GenerateRuinEntities(List<VoronoiCell> caveCells, Rectangle ruinArea, bool mirror)
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

            int maxDistanceFromEntrance = shapes.Max(s => s.DistanceFromEntrance);
            
            foreach (RuinShape leaf in shapes)
            {
                RuinEntityType wallType = RuinEntityType.Wall;
                RuinEntityConfig.RoomType roomType = GetRoomType(leaf, maxDistanceFromEntrance);

                if (!(leaf is BTRoom))
                {
                    wallType = RuinEntityType.CorridorWall;
                }
                //rooms further from the entrance are more likely to have hard-to-break walls
                else if (Rand.Range(0.0f, leaf.DistanceFromEntrance, Rand.RandSync.Server) > 1.5f)
                {
                    wallType = RuinEntityType.HeavyWall;
                }

                //generate walls  --------------------------------------------------------------
                foreach (Line wall in leaf.Walls)
                {
                    var ruinEntityConfig = generationParams.GetRandomEntity(wallType, leaf.GetLineAlignment(wall), roomType);
                    if (ruinEntityConfig == null) continue;

                    float radius = (wall.A.X == wall.B.X) ?
                        (ruinEntityConfig.Prefab as StructurePrefab).Size.X * 0.5f :
                        (ruinEntityConfig.Prefab as StructurePrefab).Size.Y * 0.5f;

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

                    var structure = new Structure(rect, ruinEntityConfig.Prefab as StructurePrefab, null)
                    {
                        ShouldBeSaved = false
                    };
                    structure.SetCollisionCategory(Physics.CollisionLevel);
                }

                //generate backgrounds --------------------------------------------------------------
                var background = generationParams.GetRandomEntity(RuinEntityType.Back, Alignment.Center, roomType);
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

            //generate doors & hatches -------------------------------------------------------------

            foreach (Corridor corridor in corridors)
            {
                RuinEntityConfig.RoomType corridorType = GetRoomType(corridor, maxDistanceFromEntrance);

                var doorConfig = generationParams.GetRandomEntity(
                    corridor.IsHorizontal ? RuinEntityType.Door : RuinEntityType.Hatch, Alignment.Center, corridorType);
                if (doorConfig == null) continue;

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
                
                var door = new Item(doorConfig.Prefab as ItemPrefab, doorPos, null)
                {
                    ShouldBeSaved = false
                };

                CreateChildEntities(doorConfig, door, corridor);

                door.GetComponent<Items.Components.Door>().IsOpen = Rand.Range(0.0f, 1.0f, Rand.RandSync.Server) < 0.8f;
            }

            //generate props --------------------------------------------------------------
            for (int i = 0; i < shapes.Count * 2; i++)
            {
                RuinShape room = shapes[Rand.Int(shapes.Count, Rand.RandSync.Server)];

                Alignment[] alignments = new Alignment[] { Alignment.Top, Alignment.Bottom, Alignment.Right, Alignment.Left, Alignment.Center };

                var prop = generationParams.GetRandomEntity(
                    RuinEntityType.Prop, 
                    alignments[Rand.Int(alignments.Length, Rand.RandSync.Server)], 
                    GetRoomType(room, maxDistanceFromEntrance));

                if (prop == null) { continue; }

                //if the prop is placed at the center of the room, we have to use a room without a door (because they're also placed at the center)                
                if (!doorlessRooms.Contains(room) && prop.Alignment.HasFlag(Alignment.Center)) continue;

                CreateEntity(prop, room);
            }

            return shapes;
        }

        private RuinEntityConfig.RoomType GetRoomType(RuinShape room, int maxDistanceFromEntrance)
        {
            RuinEntityConfig.RoomType roomType = RuinEntityConfig.RoomType.Any;
            if (room.DistanceFromEntrance <= 1)
            {
                roomType = RuinEntityConfig.RoomType.FirstRoom;
            }
            else if (room.DistanceFromEntrance == maxDistanceFromEntrance)
            {
                roomType = RuinEntityConfig.RoomType.LastRoom;
            }
            return roomType;
        }

        private MapEntity CreateEntity(RuinEntityConfig entityConfig, RuinShape room)
        {
            Alignment[] alignments = new Alignment[] { Alignment.Top, Alignment.Bottom, Alignment.Right, Alignment.Left, Alignment.Center };
            
            Vector2 size = (entityConfig.Prefab is StructurePrefab) ? ((StructurePrefab)entityConfig.Prefab).Size : Vector2.Zero;
            
            Vector2 position = room.Rect.Center.ToVector2();
            if (entityConfig.Alignment.HasFlag(Alignment.Top))
            {
                position = new Vector2(Rand.Range(room.Rect.X + size.X, room.Rect.Right - size.X, Rand.RandSync.Server), room.Rect.Bottom - 64);
            }
            else if (entityConfig.Alignment.HasFlag(Alignment.Bottom))
            {
                position = new Vector2(Rand.Range(room.Rect.X + size.X, room.Rect.Right - size.X, Rand.RandSync.Server), room.Rect.Top + 64);
            }
            else if (entityConfig.Alignment.HasFlag(Alignment.Right))
            {
                position = new Vector2(room.Rect.Right - 64, Rand.Range(room.Rect.Y + size.X, room.Rect.Bottom - size.Y, Rand.RandSync.Server));
            }
            else if (entityConfig.Alignment.HasFlag(Alignment.Left))
            {
                position = new Vector2(room.Rect.X + 64, Rand.Range(room.Rect.Y + size.X, room.Rect.Bottom - size.Y, Rand.RandSync.Server));
            }

            MapEntity entity = null;
            if (entityConfig.Prefab is ItemPrefab)
            {
                entity = new Item((ItemPrefab)entityConfig.Prefab, position, null);
            }
            else if (entityConfig.Prefab is ItemAssemblyPrefab itemAssemblyPrefab)
            {
                var entities = itemAssemblyPrefab.CreateInstance(position);
                foreach (MapEntity e in entities)
                {
                    if (e is Structure) e.ShouldBeSaved = false;
                }
            }
            else
            {
                entity = new Structure(new Rectangle(
                    (int)(position.X - size.X / 2.0f), (int)(position.Y + size.Y / 2.0f),
                    (int)size.X, (int)size.Y),
                    entityConfig.Prefab as StructurePrefab, null)
                {
                    ShouldBeSaved = false
                };
            }

            CreateChildEntities(entityConfig, entity, room);
            return entity;
        }

        private void CreateChildEntities(RuinEntityConfig parentEntityConfig, MapEntity parentEntity, RuinShape room)
        {
            foreach (RuinEntityConfig childEntity in parentEntityConfig.ChildEntities)
            {
                MapEntity createdEntity = null;
                switch (childEntity.RoomPlacement)
                {
                    case RuinEntityConfig.RoomType.SameRoom:
                        createdEntity = CreateEntity(childEntity, room);
                        break;
                    case RuinEntityConfig.RoomType.NextRoom:
                        var nextRoom = rooms.Find(r => r.DistanceFromEntrance == room.DistanceFromEntrance + 1);
                        if (nextRoom != null) { createdEntity = CreateEntity(childEntity, nextRoom); };
                        break;
                    case RuinEntityConfig.RoomType.PreviousRoom:
                        var prevRoom = rooms.Find(r => r.DistanceFromEntrance == room.DistanceFromEntrance - 1);
                        if (prevRoom != null) { createdEntity = CreateEntity(childEntity, prevRoom); };
                        break;
                    case RuinEntityConfig.RoomType.FirstRoom:
                        var firstRoom = rooms.Find(r => r.DistanceFromEntrance <= 1);
                        if (firstRoom != null) { createdEntity = CreateEntity(childEntity, firstRoom); };
                        break;
                    case RuinEntityConfig.RoomType.LastRoom:
                        int maxDistFromEntrance = rooms.Max(r => r.DistanceFromEntrance);
                        var lastRoom = rooms.Find(r => r.DistanceFromEntrance == maxDistFromEntrance);
                        if (lastRoom != null) { createdEntity = CreateEntity(childEntity, lastRoom); };
                        break;
                }

                if (createdEntity == null) continue;
                
                if (childEntity.LinkToParent)
                {
                    createdEntity.linkedTo.Add(parentEntity);
                    parentEntity.linkedTo.Add(createdEntity);
                }

                if (childEntity.WireToParent.Count > 0)
                {
                    Item item = createdEntity as Item;
                    if (item == null)
                    {
                        DebugConsole.ThrowError("Could not connect a wire to the ruin entity \"" + createdEntity.Name + "\" - the entity is not an item.");
                        return;
                    }
                    else if (item.Connections == null)
                    {
                        DebugConsole.ThrowError("Could not connect a wire to the ruin entity \"" + createdEntity.Name + "\" - the item does not have a connection panel component.");
                        return;
                    }

                    Item parentItem = parentEntity as Item;
                    if (parentItem == null)
                    {
                        DebugConsole.ThrowError("Could not connect a wire to the ruin entity \"" + parentItem.Name + "\" - the entity is not an item.");
                        return;
                    }
                    else if (parentItem.Connections == null)
                    {
                        DebugConsole.ThrowError("Could not connect a wire to the ruin entity \"" + parentItem.Name + "\" - the item does not have a connection panel component.");
                        return;
                    }

                    //TODO: alien wire prefab w/ custom sprite?
                    var wirePrefab = MapEntityPrefab.Find(null, "blackwire") as ItemPrefab;
                    foreach (Pair<string, string> wireToParent in childEntity.WireToParent)
                    {
                        var conn1 = item.Connections.Find(c => c.Name == wireToParent.First);
                        if (conn1 == null)
                        {
                            DebugConsole.ThrowError("Could not connect a wire to the ruin entity \"" + item.Name + "\" - the item does not have a connection named \"" + wireToParent.First + "\".");
                            continue;
                        }
                        var conn2 = parentItem.Connections.Find(c => c.Name == wireToParent.Second);
                        if (conn2 == null)
                        {
                            DebugConsole.ThrowError("Could not connect a wire to the ruin entity \"" + parentItem.Name + "\" - the item does not have a connection named \"" + wireToParent.Second + "\".");
                            continue;
                        }

                        var wire = new Item(wirePrefab, parentItem.WorldPosition, null).GetComponent<Items.Components.Wire>();
                        wire.Item.ShouldBeSaved = false;
                        conn1.TryAddLink(wire);
                        wire.Connect(conn1, true);
                        conn2.TryAddLink(wire);
                        wire.Connect(conn2, true);
                    }
                }
            }
        }
    }
}
