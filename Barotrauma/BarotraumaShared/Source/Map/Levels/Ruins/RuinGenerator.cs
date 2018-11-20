using FarseerPhysics;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Voronoi2;
using Barotrauma.Extensions;

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

        public RuinRoom RoomType;

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
                        newLines.Add(new Line(new Vector2(line.A.X, rectangle.Bottom), line.B));
                    }
                    //point B is within the rectangle -> cut a portion from the bottom of the line
                    else if (line.B.Y >= rectangle.Y && line.B.Y <= rectangle.Bottom)
                    {
                        newLines.Add(new Line(line.A, new Vector2(line.A.X, rectangle.Y)));
                    }
                    //rect is in between the lines -> split the line into two
                    else
                    {
                        newLines.Add(new Line(line.A, new Vector2(line.A.X, rectangle.Y)));
                        newLines.Add(new Line(new Vector2(line.A.X, rectangle.Bottom), line.B));
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
                        newLines.Add(new Line(new Vector2(rectangle.Right, line.A.Y), line.B));
                    }
                    //point B is within the rectangle -> cut a portion from the right side of the line
                    else if (line.B.X >= rectangle.X && line.B.X <= rectangle.Right)
                    {
                        newLines.Add(new Line(line.A, new Vector2(rectangle.X, line.A.Y)));
                    }
                    //rect is in between the lines -> split the line into two
                    else
                    {
                        newLines.Add(new Line(line.A, new Vector2(rectangle.X, line.A.Y)));
                        newLines.Add(new Line(new Vector2(rectangle.Right, line.A.Y), line.B));
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
        
        public bool IsHorizontal
        {
            get { return Math.Abs(A.Y - B.Y) < Math.Abs(A.X - B.X); }
        }

        public Line(Vector2 a, Vector2 b)
        {
            Debug.Assert(a.X <= b.X);
            Debug.Assert(a.Y <= b.Y);

            A = a;
            B = b;
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


        class RuinEntity
        {
            public readonly RuinEntityConfig Config;
            public readonly MapEntity Entity;
            public readonly MapEntity Parent;
            public readonly RuinShape Room;

            public RuinEntity(RuinEntityConfig config, MapEntity entity, RuinShape room, MapEntity parent = null)
            {
                Config = config;
                Entity = entity;
                Room = room;
                Parent = parent;
            }
        }
        private List<RuinEntity> ruinEntities = new List<RuinEntity>();

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
            
            //assign the room types for the first and last rooms
            foreach (RuinRoom roomType in generationParams.RoomTypeList)
            {
                switch (roomType.Placement)
                {
                    case RuinRoom.RoomPlacement.FirstRoom:
                        {
                            var selectedShape = shapes.GetRandom(s =>
                                s is BTRoom && s.DistanceFromEntrance == 1 + roomType.PlacementOffset,
                                Rand.RandSync.Server);
                            if (selectedShape != null)
                            {
                                selectedShape.RoomType = roomType;
                            }
                            break;
                        }
                    case RuinRoom.RoomPlacement.LastRoom:
                        {
                            var selectedShape = shapes.GetRandom(s =>
                                s is BTRoom && s.DistanceFromEntrance == maxDistanceFromEntrance + roomType.PlacementOffset,
                                Rand.RandSync.Server);
                            if (selectedShape != null)
                            {
                                selectedShape.RoomType = roomType;
                            }
                            break;
                        }
                }
            }

            //go through the unassigned rooms
            foreach (RuinShape room in shapes)
            {
                if (room.RoomType == null)
                {
                    room.RoomType = generationParams.RoomTypeList.GetRandom(rt =>
                        rt.IsCorridor == room is Corridor &&
                        rt.Placement == RuinRoom.RoomPlacement.Any,
                        Rand.RandSync.Server);

                    if (room.RoomType == null)
                    {
                        DebugConsole.ThrowError("Could not find a suitable room type for a room (is corridor: " + (room is Corridor) + ")");
                    }
                }
            }

            foreach (RuinShape room in shapes)
            {
                if (room.RoomType == null) continue;
                //generate walls  --------------------------------------------------------------
                foreach (Line wall in room.Walls)
                {
                    var ruinEntityConfig = room.RoomType.GetRandomEntity(RuinEntityType.Wall, room.GetLineAlignment(wall));
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
                    CreateChildEntities(ruinEntityConfig, structure, room);
                    ruinEntities.Add(new RuinEntity(ruinEntityConfig, structure, room));
                }

                //generate backgrounds --------------------------------------------------------------
                var backgroundConfig = room.RoomType.GetRandomEntity(RuinEntityType.Back, Alignment.Center);
                if (backgroundConfig != null)
                {
                    Rectangle backgroundRect = new Rectangle(room.Rect.X, room.Rect.Y + room.Rect.Height, room.Rect.Width, room.Rect.Height);
                    var backgroundStructure = new Structure(backgroundRect, (backgroundConfig.Prefab as StructurePrefab), null)
                    {
                        ShouldBeSaved = false
                    };
                    CreateChildEntities(backgroundConfig, backgroundStructure, room);
                    ruinEntities.Add(new RuinEntity(backgroundConfig, backgroundStructure, room));
                }

                var submarineBlocker = BodyFactory.CreateRectangle(GameMain.World,
                    ConvertUnits.ToSimUnits(room.Rect.Width),
                    ConvertUnits.ToSimUnits(room.Rect.Height),
                    1, ConvertUnits.ToSimUnits(room.Center));

                submarineBlocker.IsStatic = true;
                submarineBlocker.CollisionCategories = Physics.CollisionWall;
                submarineBlocker.CollidesWith = Physics.CollisionWall;

                //generate doors --------------------------------------------------------------
                if (room is Corridor corridor)
                {
                    var doorConfig = room.RoomType.GetRandomEntity(corridor.IsHorizontal ? RuinEntityType.Door : RuinEntityType.Hatch, Alignment.Center);
                    if (corridor != null)
                    {
                        //find all walls that are parallel to the corridor
                        var suitableWalls = corridor.IsHorizontal ?
                            corridor.Walls.FindAll(c => c.A.Y == c.B.Y) : corridor.Walls.FindAll(c => c.A.X == c.B.X);

                        if (suitableWalls.Any())
                        {
                            //choose a random wall to place the door next to
                            Vector2 doorPos = corridor.Center;
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
                            ruinEntities.Add(new RuinEntity(doorConfig, door, room));
                        }
                    }
                }

                //generate props --------------------------------------------------------------
                var props = room.RoomType.GetPropList();
                foreach (RuinEntityConfig prop in props)
                {
                    CreateEntity(prop, room, parent: null);
                }

                //create connections between all generated entities ---------------------------
                foreach (RuinEntity ruinEntity in ruinEntities)
                {
                    CreateConnections(ruinEntity);
                }
            }
            return shapes;
        }
        
        private MapEntity CreateEntity(RuinEntityConfig entityConfig, RuinShape room, MapEntity parent)
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
            ruinEntities.Add(new RuinEntity(entityConfig, entity, room, parent));
            return entity;
        }

        private void CreateChildEntities(RuinEntityConfig parentEntityConfig, MapEntity parentEntity, RuinShape room)
        {
            foreach (RuinEntityConfig childEntity in parentEntityConfig.ChildEntities)
            {
                switch (childEntity.PlacementRelativeToParent)
                {
                    case RuinEntityConfig.RelativePlacement.SameRoom:
                        CreateEntity(childEntity, room, parentEntity);
                        break;
                    case RuinEntityConfig.RelativePlacement.NextRoom:
                        var nextRoom = rooms.Find(r => r.DistanceFromEntrance == room.DistanceFromEntrance + 1);
                        CreateEntity(childEntity, nextRoom, parentEntity);
                        break;
                    case RuinEntityConfig.RelativePlacement.PreviousRoom:
                        var prevRoom = rooms.Find(r => r.DistanceFromEntrance == room.DistanceFromEntrance - 1);
                        CreateEntity(childEntity, prevRoom, parentEntity);
                        break;
                    case RuinEntityConfig.RelativePlacement.FirstRoom:
                        var firstRoom = rooms.Find(r => r.DistanceFromEntrance <= 1);
                        CreateEntity(childEntity, firstRoom, parentEntity);
                        break;
                    case RuinEntityConfig.RelativePlacement.LastRoom:
                        int maxDistFromEntrance = rooms.Max(r => r.DistanceFromEntrance);
                        var lastRoom = rooms.Find(r => r.DistanceFromEntrance == maxDistFromEntrance);
                        CreateEntity(childEntity, lastRoom, parentEntity);
                        break;
                }
            }
        }

        private void CreateConnections(RuinEntity entity)
        {
            foreach (RuinEntityConfig.EntityConnection connection in entity.Config.EntityConnections)
            {
                MapEntity targetEntity = null;
                if (connection.TargetEntityIdentifier == "parent")
                {
                    targetEntity = entity.Parent;
                }
                else if (!string.IsNullOrEmpty(connection.RoomName))
                {
                    RuinShape targetRoom = null;
                    switch (connection.RoomName)
                    {
                        case "sameroom":
                            targetRoom = entity.Room;
                            break;
                        case "firstroom":
                            targetRoom = allShapes.Find(s => s.DistanceFromEntrance == 1);
                            break;
                        case "lastroom":
                            int maxDistFromEntrance = rooms.Max(r => r.DistanceFromEntrance);
                            targetRoom = allShapes.Find(s => s.DistanceFromEntrance == maxDistFromEntrance);
                            break;
                        case "nextroom":
                            targetRoom = allShapes.Find(s => s.DistanceFromEntrance == entity.Room.DistanceFromEntrance + 1);
                            break;
                        case "previousroom":
                            targetRoom = allShapes.Find(s => s.DistanceFromEntrance == entity.Room.DistanceFromEntrance - 1);
                            break;
                        default:
                            targetRoom = allShapes.Find(s => s.RoomType?.Name == connection.RoomName);
                            break;
                    }

                    if (targetRoom == null)
                    {
                        DebugConsole.ThrowError("Error while generating ruins - could not find a room of the type \"" + connection.RoomName + "\".");
                    }
                    else
                    {
                        targetEntity = ruinEntities.GetRandom(e => 
                            e.Room == targetRoom && 
                            e.Entity.prefab?.Identifier == connection.TargetEntityIdentifier)?.Entity;
                    }
                }
                else
                {
                    targetEntity = ruinEntities.GetRandom(e => e.Entity.prefab?.Identifier == connection.TargetEntityIdentifier)?.Entity;
                }

                if (targetEntity == null) continue;

                if (connection.WireConnection != null)
                {
                    Item item = entity.Entity as Item;
                    if (item == null)
                    {
                        DebugConsole.ThrowError("Could not connect a wire to the ruin entity \"" + entity.Entity.Name + "\" - the entity is not an item.");
                        return;
                    }
                    else if (item.Connections == null)
                    {
                        DebugConsole.ThrowError("Could not connect a wire to the ruin entity \"" + entity.Entity.Name + "\" - the item does not have a connection panel component.");
                        return;
                    }

                    Item parentItem = entity.Parent as Item;
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

                    var conn1 = item.Connections.Find(c => c.Name == connection.WireConnection.First);
                    if (conn1 == null)
                    {
                        DebugConsole.ThrowError("Could not connect a wire to the ruin entity \"" + item.Name +
                            "\" - the item does not have a connection named \"" + connection.WireConnection.First + "\".");
                        continue;
                    }
                    var conn2 = parentItem.Connections.Find(c => c.Name == connection.WireConnection.Second);
                    if (conn2 == null)
                    {
                        DebugConsole.ThrowError("Could not connect a wire to the ruin entity \"" + parentItem.Name +
                            "\" - the item does not have a connection named \"" + connection.WireConnection.Second + "\".");
                        continue;
                    }

                    var wire = new Item(wirePrefab, parentItem.WorldPosition, null).GetComponent<Items.Components.Wire>();
                    wire.Item.ShouldBeSaved = false;
                    conn1.TryAddLink(wire);
                    wire.Connect(conn1, true);
                    conn2.TryAddLink(wire);
                    wire.Connect(conn2, true);                    
                }
                else
                {
                    entity.Entity.linkedTo.Add(targetEntity);
                    targetEntity.linkedTo.Add(entity.Entity);
                }
            }
        }
    }
}
