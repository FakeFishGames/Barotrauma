using Barotrauma.Items.Components;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    [Flags]
    public enum SpawnType { Path = 0, Human = 1, Enemy = 2, Cargo = 4, Corpse = 8 };

    partial class WayPoint : MapEntity
    {
        public static List<WayPoint> WayPointList = new List<WayPoint>();

        public static bool ShowWayPoints = true, ShowSpawnPoints = true;

        public const float LadderWaypointInterval = 70.0f;

        protected SpawnType spawnType;
        private string[] idCardTags;
        private ushort ladderId;
        public Ladder Ladders;
        public Structure Stairs;

        private HashSet<Identifier> tags;

        public bool isObstructed;

        private ushort gapId;
        public Gap ConnectedGap
        {
            get;
            set;
        }

        public Door ConnectedDoor
        {
            get { return ConnectedGap?.ConnectedDoor; }
        }

        public Hull CurrentHull { get; private set; }

        public Level.Tunnel Tunnel;
        public RuinGeneration.Ruin Ruin;

        public SpawnType SpawnType
        {
            get { return spawnType; }
            set { spawnType = value; }
        }

        public Action<WayPoint> OnLinksChanged { get; set; }

        public override string Name
        {
            get
            {
                return spawnType == SpawnType.Path ? "WayPoint" : "SpawnPoint";
            }
        }

        public string IdCardDesc { get; private set; }
        public string[] IdCardTags
        {
            get { return idCardTags; }
            private set
            {
                idCardTags = value;
                for (int i = 0; i < idCardTags.Length; i++)
                {
                    idCardTags[i] = idCardTags[i].Trim().ToLowerInvariant();
                }
            }
        }

        public IEnumerable<Identifier> Tags => tags;

        public JobPrefab AssignedJob { get; private set; }

        public WayPoint(Vector2 position, SpawnType spawnType, Submarine submarine, Gap gap = null)
            : this(new Rectangle((int)position.X - 3, (int)position.Y + 3, 6, 6), submarine)
        {
            this.spawnType = spawnType;
            ConnectedGap = gap;
        }

        public WayPoint(MapEntityPrefab prefab, Rectangle rectangle)
           : this (rectangle, Submarine.MainSub)
        { 
            if (prefab.Identifier.Contains("spawn"))
            {
                spawnType = SpawnType.Human;
            }
            else
            {
                SpawnType = SpawnType.Path;
            }

#if CLIENT
            if (SubEditorScreen.IsSubEditor())
            {
                SubEditorScreen.StoreCommand(new AddOrDeleteCommand(new List<MapEntity> { this }, false));
            }
#endif
        }

        public enum Type
        {
            WayPoint,
            SpawnPoint
        }
        
        public WayPoint(Rectangle newRect, Submarine submarine)
            : this (Type.WayPoint, newRect, submarine)
        {
        }

        public WayPoint(Type type, Rectangle newRect, Submarine submarine, ushort id = Entity.NullEntityID)
            : base (type is Type.WayPoint
                ? CoreEntityPrefab.WayPointPrefab
                : CoreEntityPrefab.SpawnPointPrefab, submarine, id)
        {
            rect = newRect;
            idCardTags = Array.Empty<string>();
            tags = new HashSet<Identifier>();

#if CLIENT
            if (iconSprites == null)
            {
                iconSprites = new Dictionary<string, Sprite>()
                {
                    { "Path", new Sprite("Content/UI/MainIconsAtlas.png", new Rectangle(0,0,128,128)) },
                    { "Human", new Sprite("Content/UI/MainIconsAtlas.png", new Rectangle(128,0,128,128)) },
                    { "Enemy", new Sprite("Content/UI/MainIconsAtlas.png", new Rectangle(256,0,128,128)) },
                    { "Cargo", new Sprite("Content/UI/MainIconsAtlas.png", new Rectangle(384,0,128,128)) },
                    { "Corpse", new Sprite("Content/UI/MainIconsAtlas.png", new Rectangle(512,0,128,128)) },
                    { "Ladder", new Sprite("Content/UI/MainIconsAtlas.png", new Rectangle(0,128,128,128)) },
                    { "Door", new Sprite("Content/UI/MainIconsAtlas.png", new Rectangle(128,128,128,128)) }
                };
            }
#endif

            InsertToList();
            WayPointList.Add(this);

            DebugConsole.Log("Created waypoint (" + ID + ")");

            FindHull();
        }

        public override MapEntity Clone()
        {
            var clone = new WayPoint(rect, Submarine)
            {
                IdCardDesc = IdCardDesc,
                idCardTags = idCardTags,
                tags = tags,
                spawnType = spawnType,
                AssignedJob = AssignedJob
            };

            return clone;
        }

        public static bool GenerateSubWaypoints(Submarine submarine)
        {
            if (!Hull.HullList.Any())
            {
                DebugConsole.ThrowError("Couldn't generate waypoints: no hulls found.");
                return false;
            }

            List<WayPoint> existingWaypoints = WayPointList.FindAll(wp => wp.spawnType == SpawnType.Path);
            foreach (WayPoint wayPoint in existingWaypoints)
            {
                wayPoint.Remove();
            }

            //find all open doors and temporarily activate their bodies to prevent visibility checks
            //from ignoring the doors and generating waypoint connections that go straight through the door
            List<Door> openDoors = new List<Door>();
            foreach (Item item in Item.ItemList)
            {
                var door = item.GetComponent<Door>();
                if (door != null && !door.Body.Enabled)
                {
                    openDoors.Add(door);
                    door.Body.Enabled = true;
                }
            }
            bool isFlooded = submarine.Info.IsRuin || submarine.Info.Type == SubmarineType.OutpostModule && submarine.Info.OutpostModuleInfo.ModuleFlags.Contains("ruin".ToIdentifier());
            float diffFromHullEdge = 50;
            float minDist = 100.0f;
            float heightFromFloor = 110.0f;
            float hullMinHeight = 100;

            var removals = new HashSet<WayPoint>();
            foreach (Hull hull in Hull.HullList)
            {
                if (isFlooded)
                {
                    diffFromHullEdge = 75;
                    var hullWaypoints = new List<WayPoint>();
                    float top = hull.Rect.Y;
                    float bottom = hull.Rect.Y - hull.Rect.Height;
                    if (hull.Rect.Width < 300 || hull.Rect.Height < 300)
                    {
                        // For narrow hulls, create one line of waypoints either horizontally or vertically
                        if (hull.Rect.Width > hull.Rect.Height)
                        {
                            // Horizontal
                            float y = hull.Rect.Y - hull.Rect.Height / 2;
                            for (float x = hull.Rect.X + diffFromHullEdge; x <= hull.Rect.Right - diffFromHullEdge; x += minDist)
                            {
                                hullWaypoints.Add(new WayPoint(new Vector2(x, y), SpawnType.Path, submarine));
                            }
                        }
                        else
                        {
                            // Vertical
                            float x = hull.Rect.X + hull.Rect.Width / 2;
                            for (float y = top - diffFromHullEdge; y >= bottom + diffFromHullEdge; y -= minDist)
                            {
                                hullWaypoints.Add(new WayPoint(new Vector2(x, y), SpawnType.Path, submarine));
                            }
                        }
                    }
                    if (hullWaypoints.None())
                    {
                        // Try to create a grid-like network of waypoints
                        for (float x = hull.Rect.X + diffFromHullEdge; x <= hull.Rect.Right - diffFromHullEdge; x += minDist)
                        {
                            for (float y = top - diffFromHullEdge; y >= bottom + diffFromHullEdge; y -= minDist)
                            {
                                hullWaypoints.Add(new WayPoint(new Vector2(x, y), SpawnType.Path, submarine));
                            }
                        }
                        if (hullWaypoints.None())
                        {
                            // If that fails, just create one waypoint at the center.
                            hullWaypoints.Add(new WayPoint(new Vector2(hull.Rect.X + hull.Rect.Width / 2.0f, hull.Rect.Y - hull.Rect.Height / 2), SpawnType.Path, submarine));
                        }
                        foreach (WayPoint wp in hullWaypoints)
                        {
                            foreach (Structure wall in Structure.WallList)
                            {
                                if (wall.HasBody)
                                {
                                    // Remove waypoints that are too close/inside the walls.
                                    Rectangle rect = wall.Rect;
                                    rect.Inflate(10, 10);
                                    if (rect.ContainsWorld(wp.Position))
                                    {
                                        removals.Add(wp);
                                    }
                                }
                            }
                        }
                    }
                    // Connect the waypoints
                    foreach (var wayPoint in hullWaypoints)
                    {
                        for (int dir = -1; dir <= 1; dir += 2)
                        {
                            WayPoint closest = wayPoint.FindClosest(dir, horizontalSearch: true, new Vector2(minDist * 1.9f, minDist));
                            if (closest != null && closest.CurrentHull == wayPoint.CurrentHull)
                            {
                                wayPoint.ConnectTo(closest);
                            }
                            closest = wayPoint.FindClosest(dir, horizontalSearch: false, new Vector2(minDist, minDist * 1.9f));
                            if (closest != null && closest.CurrentHull == wayPoint.CurrentHull)
                            {
                                wayPoint.ConnectTo(closest);
                            }
                        }
                    }
                }
                else
                {
                    if (hull.Rect.Height < hullMinHeight) { continue; }
                    // Do five raycasts to check if there's a floor. Don't create waypoints unless we can find a floor.
                    Body floor = null;
                    for (int i = 0; i < 5; i++)
                    {
                        float horizontalOffset = 0;
                        switch (i)
                        {
                            case 1:
                                horizontalOffset = hull.RectWidth * 0.2f;
                                break;
                            case 2:
                                horizontalOffset = hull.RectWidth * 0.4f;
                                break;
                            case 3:
                                horizontalOffset = -hull.RectWidth * 0.2f;
                                break;
                            case 4:
                                horizontalOffset = -hull.RectWidth * 0.4f;
                                break;
                        }
                        horizontalOffset = ConvertUnits.ToSimUnits(horizontalOffset);
                        Vector2 floorPos = new Vector2(hull.SimPosition.X + horizontalOffset, ConvertUnits.ToSimUnits(hull.Rect.Y - hull.RectHeight - 50));
                        floor = Submarine.PickBody(new Vector2(hull.SimPosition.X + horizontalOffset, hull.SimPosition.Y), floorPos, collisionCategory: Physics.CollisionWall | Physics.CollisionPlatform, customPredicate: f => !(f.Body.UserData is Submarine));
                        if (floor != null) { break; }
                    }
                    if (floor == null) { continue; }
                    float waypointHeight = hull.Rect.Height > heightFromFloor * 2 ? heightFromFloor : hull.Rect.Height / 2;
                    if (hull.Rect.Width < diffFromHullEdge * 3.0f)
                    {
                        new WayPoint(new Vector2(hull.Rect.X + hull.Rect.Width / 2.0f, hull.Rect.Y - hull.Rect.Height + waypointHeight), SpawnType.Path, submarine);
                    }
                    else
                    {
                        WayPoint previousWaypoint = null;
                        for (float x = hull.Rect.X + diffFromHullEdge; x <= hull.Rect.Right - diffFromHullEdge; x += minDist)
                        {
                            var wayPoint = new WayPoint(new Vector2(x, hull.Rect.Y - hull.Rect.Height + waypointHeight), SpawnType.Path, submarine);
                            // Too close to stairs, will be assigned as a stair point -> remove
                            if (wayPoint.FindStairs() != null)
                            {
                                removals.Add(wayPoint);
                                continue;
                            }
                            if (previousWaypoint != null)
                            {
                                wayPoint.ConnectTo(previousWaypoint);
                            }
                            previousWaypoint = wayPoint;
                        }
                        if (previousWaypoint == null)
                        {
                            // Ensure that we always create at least one waypoint per hull.
                            new WayPoint(new Vector2(hull.Rect.X + hull.Rect.Width / 2.0f, hull.Rect.Y - hull.Rect.Height + waypointHeight), SpawnType.Path, submarine);
                        }
                    }
                }
            }

            // Platforms
            foreach (Structure platform in Structure.WallList)
            {
                if (!platform.IsPlatform) { continue; }
                float waypointHeight = heightFromFloor;
                WayPoint prevWaypoint = null;
                for (float x = platform.Rect.X + diffFromHullEdge; x <= platform.Rect.Right - diffFromHullEdge; x += minDist)
                {
                    WayPoint wayPoint = new WayPoint(new Vector2(x, platform.Rect.Y + waypointHeight), SpawnType.Path, submarine);
                    if (prevWaypoint != null)
                    {
                        wayPoint.ConnectTo(prevWaypoint);
                    }
                    // If the waypoint is close to hull waypoints, remove it.
                    if (wayPoint != null)
                    {
                        for (int dir = -1; dir <= 1; dir += 2)
                        {
                            if (wayPoint.FindClosest(dir, horizontalSearch: true, tolerance: new Vector2(minDist, heightFromFloor), ignored: prevWaypoint.ToEnumerable()) != null)
                            {
                                wayPoint.Remove();
                                wayPoint = null;
                                break;
                            }
                        }
                    }
                    prevWaypoint = wayPoint;
                }
            }

            float outSideWaypointInterval = 100.0f;
            if (!isFlooded && submarine.Info.Type != SubmarineType.OutpostModule)
            {
                List<(WayPoint, int)> outsideWaypoints = new List<(WayPoint, int)>();

                Rectangle borders = Hull.GetBorders();
                int originalWidth = borders.Width;
                int originalHeight = borders.Height;
                borders.X -= Math.Min(500, originalWidth / 4);
                borders.Y += Math.Min(500, originalHeight / 4);
                borders.Width += Math.Min(1500, originalWidth / 2);
                borders.Height += Math.Min(1000, originalHeight / 2);
                borders.Location -= MathUtils.ToPoint(submarine.HiddenSubPosition);

                if (borders.Width <= outSideWaypointInterval * 2)
                {
                    borders.Inflate(outSideWaypointInterval * 2 - borders.Width, 0);
                }

                if (borders.Height <= outSideWaypointInterval * 2)
                {
                    int inflateAmount = (int)(outSideWaypointInterval * 2) - borders.Height;
                    borders.Y += inflateAmount / 2;
                    borders.Height += inflateAmount;
                }

                WayPoint[,] cornerWaypoint = new WayPoint[2, 2];
                for (int i = 0; i < 2; i++)
                {
                    for (float x = borders.X + outSideWaypointInterval; x < borders.Right - outSideWaypointInterval; x += outSideWaypointInterval)
                    {
                        var wayPoint = new WayPoint(
                            new Vector2(x, borders.Y - borders.Height * i) + submarine.HiddenSubPosition,
                            SpawnType.Path, submarine);

                        outsideWaypoints.Add((wayPoint, i));

                        if (x == borders.X + outSideWaypointInterval)
                        {
                            cornerWaypoint[i, 0] = wayPoint;
                        }
                        else
                        {
                            wayPoint.ConnectTo(WayPointList[WayPointList.Count - 2]);
                        }
                    }

                    cornerWaypoint[i, 1] = WayPointList[WayPointList.Count - 1];
                }

                for (int i = 0; i < 2; i++)
                {
                    WayPoint wayPoint = null;
                    for (float y = borders.Y - borders.Height; y < borders.Y; y += outSideWaypointInterval)
                    {
                        wayPoint = new WayPoint(
                            new Vector2(borders.X + borders.Width * i, y) + submarine.HiddenSubPosition,
                            SpawnType.Path, submarine);

                        outsideWaypoints.Add((wayPoint, i));

                        if (y == borders.Y - borders.Height)
                        {
                            wayPoint.ConnectTo(cornerWaypoint[1, i]);
                        }
                        else
                        {
                            wayPoint.ConnectTo(WayPointList[WayPointList.Count - 2]);
                        }
                    }

                    wayPoint.ConnectTo(cornerWaypoint[0, i]);
                }

                Vector2 center = ConvertUnits.ToSimUnits(submarine.HiddenSubPosition);
                float halfHeight = ConvertUnits.ToSimUnits(borders.Height / 2);
                // Try to move the waypoints so that they are near the walls, roughly following the shape of the sub.
                foreach (var wayPoint in outsideWaypoints)
                {
                    WayPoint wp = wayPoint.Item1;
                    float xDiff = center.X - wp.SimPosition.X;
                    Vector2 targetPos = new Vector2(center.X - xDiff * 0.5f, center.Y);
                    Body wall = Submarine.PickBody(wp.SimPosition, targetPos, collisionCategory: Physics.CollisionWall, customPredicate: f => !(f.Body.UserData is Submarine));
                    if (wall == null)
                    {
                        // Try again, and shoot to the center now. It happens with some subs that the first, offset raycast don't hit the walls.
                        targetPos = new Vector2(center.X - xDiff, center.Y);
                        wall = Submarine.PickBody(wp.SimPosition, targetPos, collisionCategory: Physics.CollisionWall, customPredicate: f => !(f.Body.UserData is Submarine));
                    }
                    if (wall != null)
                    {
                        float distanceFromWall = 1;
                        if (xDiff > 0 && !submarine.Info.HasTag(SubmarineTag.Shuttle))
                        {
                            // We don't want to move the waypoints near the tail too close to the engine.
                            float yDist = Math.Abs(center.Y - wp.SimPosition.Y);
                            distanceFromWall = MathHelper.Lerp(1, 3, MathUtils.InverseLerp(halfHeight, 0, yDist));
                        }
                        Vector2 newPos = Submarine.LastPickedPosition + Submarine.LastPickedNormal * distanceFromWall;
                        wp.rect = new Rectangle(ConvertUnits.ToDisplayUnits(newPos).ToPoint(), wp.rect.Size);
                        wp.FindHull();
                    }
                }
                // Remove unwanted points
                WayPoint previous = null;
                float tooClose = outSideWaypointInterval / 2;
                foreach (var wayPoint in outsideWaypoints)
                {
                    WayPoint wp = wayPoint.Item1;
                    if (wp.CurrentHull != null ||
                        Submarine.PickBody(wp.SimPosition, wp.SimPosition + Vector2.Normalize(center - wp.SimPosition) * 0.1f, collisionCategory: Physics.CollisionWall | Physics.CollisionItem, customPredicate: f => !(f.Body.UserData is Submarine), allowInsideFixture: true) != null)
                    {
                        // Remove waypoints that got inside/too near the sub.
                        removals.Add(wp);
                        previous = wp;
                        continue;
                    }
                    foreach (var otherWayPoint in outsideWaypoints)
                    {
                        WayPoint otherWp = otherWayPoint.Item1;
                        if (otherWp == wp) { continue; }
                        if (removals.Contains(otherWp)) { continue; }
                        float sqrDist = Vector2.DistanceSquared(wp.Position, otherWp.Position);
                        // Remove waypoints that are too close to each other.
                        if (!removals.Contains(previous) && sqrDist < tooClose * tooClose)
                        {
                            removals.Add(wp);
                        }
                    }
                    previous = wp;
                }
                foreach (WayPoint wp in removals)
                {
                    outsideWaypoints.RemoveAll(w => w.Item1 == wp);
                }
                removals.ForEach(wp => wp.Remove());
                for (int i = 0; i < outsideWaypoints.Count; i++)
                {
                    WayPoint current = outsideWaypoints[i].Item1;
                    if (current.linkedTo.Count(l => !removals.Contains(l)) > 1) { continue; }
                    WayPoint next = null;
                    int maxConnections = 2;
                    float tooFar = outSideWaypointInterval * 5;
                    for (int j = 0; j < maxConnections; j++)
                    {
                        if (current.linkedTo.Count >= maxConnections) { break; }
                        tooFar /= current.linkedTo.Count(l => !removals.Contains(l));
                        next = current.FindClosestOutside(outsideWaypoints, tolerance: tooFar, filter: wp => wp.Item1 != next && wp.Item1.linkedTo.None(e => current.linkedTo.Contains(e)) && wp.Item1.linkedTo.Count < 2 && wp.Item2 < i);
                        if (next != null)
                        {
                            current.ConnectTo(next);
                        }
                    }
                }
            }
            removals.ForEach(wp => wp.Remove());
            removals.Clear();
            // Stairs
            foreach (MapEntity mapEntity in mapEntityList.ToList())
            {
                if (!(mapEntity is Structure structure)) { continue; }
                if (structure.StairDirection == Direction.None) { continue; }
                WayPoint[] stairPoints = new WayPoint[3];
                float margin = -32;

                stairPoints[0] = new WayPoint(new Vector2(
                        structure.Rect.X + 5,
                        structure.Rect.Y - (structure.StairDirection == Direction.Left ? margin : structure.Rect.Height - 100)), SpawnType.Path, submarine);

                stairPoints[1] = new WayPoint(new Vector2(
                        structure.Rect.Right - 5,
                        structure.Rect.Y - (structure.StairDirection == Direction.Left ? structure.Rect.Height - 100 : margin)), SpawnType.Path, submarine);

                for (int i = 0; i < 2; i++)
                {
                    for (int dir = -1; dir <= 1; dir += 2)
                    {
                        WayPoint closest = stairPoints[i].FindClosest(dir, horizontalSearch: true, new Vector2(minDist * 1.5f, minDist / 2));
                        if (closest == null) { continue; }
                        stairPoints[i].ConnectTo(closest);
                    }
                }

                stairPoints[2] = new WayPoint((stairPoints[0].Position + stairPoints[1].Position) / 2, SpawnType.Path, submarine);
                stairPoints[0].ConnectTo(stairPoints[2]);
                stairPoints[2].ConnectTo(stairPoints[1]);
                stairPoints.ForEach(wp => wp.FindStairs());
            }

            foreach (Item item in Item.ItemList)
            {
                var ladders = item.GetComponent<Ladder>();
                if (ladders == null) { continue; }

                Vector2 bottomPoint = new Vector2(item.Rect.Center.X, item.Rect.Top - item.Rect.Height + 10);
                List<WayPoint> ladderPoints = new List<WayPoint>
                {
                    new WayPoint(bottomPoint, SpawnType.Path, submarine),
                };

                List<Body> ignoredBodies = new List<Body>();
                // Lowest point is only meaningful for hanging ladders inside the sub, but it shouldn't matter in other cases either.
                // Start point is where the bots normally grasp the ladder when they stand on ground.
                WayPoint lowestPoint = ladderPoints[0];
                WayPoint prevPoint = lowestPoint;
                Vector2 prevPos = prevPoint.SimPosition;
                Body ground = Submarine.PickBody(lowestPoint.SimPosition, lowestPoint.SimPosition - Vector2.UnitY, ignoredBodies, 
                    collisionCategory: Physics.CollisionWall | Physics.CollisionPlatform | Physics.CollisionStairs, 
                    customPredicate: f => !(f.Body.UserData is Submarine));
                float startHeight = ground != null ? ConvertUnits.ToDisplayUnits(ground.Position.Y) : bottomPoint.Y;
                startHeight += heightFromFloor;
                WayPoint startPoint = lowestPoint;
                Vector2 nextPos = new Vector2(item.Rect.Center.X, startHeight);
                // Don't create the start point if it's too close to the lowest point or if it's outside of the sub.
                // If we skip creating the start point, the lowest point is used instead.
                if (lowestPoint == null || Math.Abs(startPoint.Position.Y - startHeight) > 40 && Hull.FindHull(nextPos) != null)
                {
                    startPoint = new WayPoint(nextPos, SpawnType.Path, submarine);
                    ladderPoints.Add(startPoint);
                    if (lowestPoint != null)
                    {
                        startPoint.ConnectTo(lowestPoint);
                    }
                    prevPoint = startPoint;
                    prevPos = prevPoint.SimPosition;
                }
                for (float y = startPoint.Position.Y + LadderWaypointInterval; y < item.Rect.Y - 1.0f; y += LadderWaypointInterval)
                {
                    //first check if there's a door in the way
                    //(we need to create a waypoint linked to the door for NPCs to open it)
                    Body pickedBody = Submarine.PickBody(
                        ConvertUnits.ToSimUnits(new Vector2(startPoint.Position.X, y)),
                        prevPos, ignoredBodies, Physics.CollisionWall, false,
                        (Fixture f) => f.Body.UserData is Item pickedItem && pickedItem.GetComponent<Door>() != null);

                    Door pickedDoor = null;
                    if (pickedBody != null)
                    {
                        pickedDoor = (pickedBody?.UserData as Item).GetComponent<Door>();
                    }
                    else
                    {
                        //no door, check for walls
                        pickedBody = Submarine.PickBody(
                            ConvertUnits.ToSimUnits(new Vector2(startPoint.Position.X, y)), prevPos, ignoredBodies, null, false,
                            (Fixture f) => f.Body.UserData is Structure);
                    }

                    if (pickedBody == null)
                    {
                        prevPos = Submarine.LastPickedPosition;
                        continue;
                    }
                    else
                    {
                        ignoredBodies.Add(pickedBody);
                    }

                    if (pickedDoor != null)
                    {
                        WayPoint newPoint = new WayPoint(pickedDoor.Item.Position, SpawnType.Path, submarine);
                        ladderPoints.Add(newPoint);
                        newPoint.ConnectedGap = pickedDoor.LinkedGap;
                        newPoint.ConnectTo(prevPoint);
                        prevPoint = newPoint;
                        prevPos = new Vector2(prevPos.X, ConvertUnits.ToSimUnits(pickedDoor.Item.Position.Y - pickedDoor.Item.Rect.Height));
                    }
                    else
                    {
                        WayPoint newPoint = new WayPoint(ConvertUnits.ToDisplayUnits(Submarine.LastPickedPosition) + Vector2.UnitY * heightFromFloor, SpawnType.Path, submarine);
                        ladderPoints.Add(newPoint);
                        newPoint.ConnectTo(prevPoint);
                        prevPoint = newPoint;
                        prevPos = ConvertUnits.ToSimUnits(newPoint.Position);
                    }
                }

                // Cap
                if (prevPoint.rect.Y < item.Rect.Y - 40)
                {
                    WayPoint wayPoint = new WayPoint(new Vector2(item.Rect.Center.X, item.Rect.Y - 1.0f), SpawnType.Path, submarine);
                    ladderPoints.Add(wayPoint);
                    wayPoint.ConnectTo(prevPoint);
                }

                // Connect ladder waypoints to hull points at the right and left side
                foreach (WayPoint ladderPoint in ladderPoints)
                {
                    ladderPoint.Ladders = ladders;
                    bool isHatch = ladderPoint.ConnectedGap != null && !ladderPoint.ConnectedGap.IsRoomToRoom;
                    for (int dir = -1; dir <= 1; dir += 2)
                    {
                        WayPoint closest = null;
                        if (isHatch)
                        {
                            closest = ladderPoint.FindClosest(dir, horizontalSearch: true, new Vector2(500, 1000), ladderPoint.ConnectedGap?.ConnectedDoor?.Body.FarseerBody, filter: wp => wp.CurrentHull == null, ignored: ladderPoints);
                        }
                        else
                        {
                            closest = ladderPoint.FindClosest(dir, horizontalSearch: true, new Vector2(150, 100), ladderPoint.ConnectedGap?.ConnectedDoor?.Body.FarseerBody, ignored: ladderPoints);
                        }
                        if (closest == null) { continue; }
                        ladderPoint.ConnectTo(closest);
                    }
                }
            }

            // Another pass: connect cap and bottom points with other ladders when they are vertically adjacent to another (double ladders)
            foreach (Item item in Item.ItemList)
            {
                var ladders = item.GetComponent<Ladder>();
                if (ladders == null) { continue; }
                var wps = WayPointList.Where(wp => wp.Ladders == ladders).OrderByDescending(wp => wp.Rect.Y);
                WayPoint cap = wps.First();
                WayPoint above = cap.FindClosest(1, horizontalSearch: false, tolerance: new Vector2(25, 50), filter: wp => wp.Ladders != null && wp.Ladders != ladders);
                above?.ConnectTo(cap);
                WayPoint bottom = wps.Last();
                WayPoint below = bottom.FindClosest(-1, horizontalSearch: false, tolerance: new Vector2(25, 50), filter: wp => wp.Ladders != null && wp.Ladders != ladders);
                below?.ConnectTo(bottom);
            }

            foreach (Gap gap in Gap.GapList)
            {
                if (gap.IsHorizontal)
                {
                    if ( isFlooded)
                    {
                        // Too small to swim through
                        if (gap.Rect.Height < 50) { continue; }
                    }
                    else
                    {
                        // Too small to walk through
                        if (gap.Rect.Height < hullMinHeight) { continue; }
                    }

                    Vector2 pos = new Vector2(gap.Rect.Center.X, gap.Rect.Y - gap.Rect.Height + heightFromFloor);
                    if (isFlooded)
                    {
                        pos.Y = gap.Rect.Y - gap.Rect.Height / 2;
                    }
                    var wayPoint = new WayPoint(pos, SpawnType.Path, submarine, gap);
                    // The closest waypoint can be quite far if the gap is at an exterior door.
                    Vector2 tolerance = gap.IsRoomToRoom && !isFlooded ? new Vector2(150, 70) : new Vector2(1000, 1000);
                    for (int dir = -1; dir <= 1; dir += 2)
                    {
                        WayPoint closest = wayPoint.FindClosest(dir, horizontalSearch: true, tolerance, gap.ConnectedDoor?.Body.FarseerBody);
                        if (closest != null)
                        {
                            wayPoint.ConnectTo(closest);
                        }
                    }
                }
                else
                {
                    // Create waypoints on vertical gaps on the outer walls, also hatches.
                    if (!isFlooded && (gap.IsRoomToRoom || gap.linkedTo.None(l => l is Hull))) { continue; }
                    // Too small to swim through
                    if (gap.Rect.Width < 50.0f) { continue; }
                    Vector2 pos = new Vector2(gap.Rect.Center.X, gap.Rect.Y - gap.Rect.Height / 2);
                    // Some hatches are created in the block above where we handle the ladder waypoints. So we need to check for duplicates.
                    if (WayPointList.Any(wp => wp.ConnectedGap == gap)) { continue; }
                    var wayPoint = new WayPoint(pos, SpawnType.Path, submarine, gap);
                    Hull connectedHull = (Hull)gap.linkedTo.First(l => l is Hull);
                    int dir = Math.Sign(connectedHull.Position.Y - gap.Position.Y);
                    WayPoint closest = wayPoint.FindClosest(dir, horizontalSearch: false, isFlooded ? new Vector2(500, 500) : new Vector2(50, 100));
                    if (closest != null)
                    {
                        wayPoint.ConnectTo(closest);
                    }
                    if (isFlooded)
                    {
                        closest = wayPoint.FindClosest(-dir, horizontalSearch: false, isFlooded ? new Vector2(500, 500) : new Vector2(50, 100));
                        if (closest != null)
                        {
                            wayPoint.ConnectTo(closest);
                        }
                    }
                    // Link to outside
                    for (dir = -1; dir <= 1; dir += 2)
                    {
                        closest = wayPoint.FindClosest(dir, horizontalSearch: true, new Vector2(500, 1000), gap.ConnectedDoor?.Body.FarseerBody, filter: wp => wp.CurrentHull == null);
                        if (closest != null)
                        {
                            wayPoint.ConnectTo(closest);
                        }
                    }
                }
            }

            var orphans = WayPointList.FindAll(w => w.spawnType == SpawnType.Path && w.linkedTo.None());
            foreach (WayPoint wp in orphans)
            {
                wp.Remove();
            }

            foreach (WayPoint wp in WayPointList)
            {
                if (wp.SpawnType == SpawnType.Path && wp.CurrentHull == null && wp.Ladders == null && wp.linkedTo.Count < 2)
                {
                    DebugConsole.ThrowError($"Couldn't automatically link the waypoint {wp.ID} outside of the submarine. You should do it manually. The waypoint ID is shown in red color.");
                }
            }

            //re-disable the bodies of the doors that are supposed to be open
            foreach (Door door in openDoors)
            {
                door.Body.Enabled = false;
            }

            return true;
        }

        private WayPoint FindClosestOutside(IEnumerable<(WayPoint, int)> waypointList, float tolerance, Body ignoredBody = null, IEnumerable<WayPoint> ignored = null, Func<(WayPoint, int), bool> filter = null)
        {
            float closestDist = 0;
            WayPoint closest = null;
            foreach (var wayPoint in waypointList)
            {
                WayPoint wp = wayPoint.Item1;
                if (wp.SpawnType != SpawnType.Path || wp == this) { continue; }
                // Ignore if already linked
                if (linkedTo.Contains(wp)) { continue; }
                if (ignored != null && ignored.Contains(wp)) { continue; }
                if (filter != null && !filter(wayPoint)) { continue; }
                float sqrDist = Vector2.DistanceSquared(Position, wp.Position);
                if (sqrDist > tolerance * tolerance) { continue; }
                if (closest == null || sqrDist < closestDist)
                {
                    var body = Submarine.CheckVisibility(SimPosition, wp.SimPosition, ignoreLevel: true, ignoreSubs: true, ignoreSensors: false);
                    if (body != null && body != ignoredBody && !(body.UserData is Submarine))
                    {
                        if (body.UserData is Structure || body.FixtureList[0].CollisionCategories.HasFlag(Physics.CollisionWall))
                        {
                            continue;
                        }
                    }
                    closestDist = sqrDist;
                    closest = wp;
                }
            }
            return closest;
        }

        private WayPoint FindClosest(int dir, bool horizontalSearch, Vector2 tolerance, Body ignoredBody = null, IEnumerable<WayPoint> ignored = null, Func<WayPoint, bool> filter = null)
        {
            if (dir != -1 && dir != 1) { return null; }

            float closestDist = 0.0f;
            WayPoint closest = null;

            foreach (WayPoint wp in WayPointList)
            {
                if (wp.SpawnType != SpawnType.Path || wp == this) { continue; }

                float xDiff = wp.Position.X - Position.X;
                float yDiff = wp.Position.Y - Position.Y;
                float xDist = Math.Abs(xDiff);
                float yDist = Math.Abs(yDiff);
                if (tolerance.X < xDist) { continue; }
                if (tolerance.Y < yDist) { continue; }

                float dist = 0.0f;
                float diff = 0.0f;
                if (horizontalSearch)
                {
                    diff = xDiff;
                    dist = xDist + yDist / 5.0f;
                }
                else
                {
                    diff = yDiff;
                    dist = yDist + xDist / 5.0f;
                    //prefer ladder waypoints when moving vertically
                    if (wp.Ladders != null) { dist *= 0.5f; }
                }

                if (Math.Sign(diff) != dir) { continue; }
                // Ignore if already linked
                if (linkedTo.Contains(wp)) { continue; }
                if (ignored != null && ignored.Contains(wp)) { continue; }
                if (filter != null && !filter(wp)) { continue; }

                if (closest == null || dist < closestDist)
                {
                    var body = Submarine.CheckVisibility(SimPosition, wp.SimPosition, ignoreLevel: true, ignoreSubs: true, ignoreSensors: false);
                    if (body != null && body != ignoredBody && !(body.UserData is Submarine))
                    {
                        if (body.UserData is Structure)
                        {
                            continue;
                        }
                        if (body.FixtureList[0].CollisionCategories.HasFlag(Physics.CollisionWall) && body.UserData is Item i && i.GetComponent<Door>() != null)
                        {
                            continue;
                        }
                    }

                    closestDist = dist;
                    closest = wp;
                }
            }
            
            return closest;
        }

        public void ConnectTo(WayPoint wayPoint2)
        {
            System.Diagnostics.Debug.Assert(this != wayPoint2);
            if (!linkedTo.Contains(wayPoint2)) 
            {
                linkedTo.Add(wayPoint2);
                OnLinksChanged?.Invoke(this);
            }
            if (!wayPoint2.linkedTo.Contains(this)) 
            {
                wayPoint2.linkedTo.Add(this);
                wayPoint2.OnLinksChanged?.Invoke(wayPoint2);
            }
        }

        public static WayPoint GetRandom(SpawnType spawnType = SpawnType.Human, JobPrefab assignedJob = null, Submarine sub = null, bool useSyncedRand = false, string spawnPointTag = null, bool ignoreSubmarine = false)
        {
            return WayPointList.GetRandom(wp =>
                (ignoreSubmarine || wp.Submarine == sub) && 
                wp.spawnType == spawnType &&
                (spawnPointTag.IsNullOrEmpty() || wp.Tags.Any(t => t == spawnPointTag)) &&
                (assignedJob == null || (assignedJob != null && wp.AssignedJob == assignedJob)), 
                useSyncedRand ? Rand.RandSync.ServerAndClient : Rand.RandSync.Unsynced);
        }

        public static WayPoint[] SelectCrewSpawnPoints(List<CharacterInfo> crew, Submarine submarine)
        {
            List<WayPoint> subWayPoints = WayPointList.FindAll(wp => wp.Submarine == submarine);
            subWayPoints.Shuffle();

            List<WayPoint> unassignedWayPoints = subWayPoints.FindAll(wp => wp.spawnType == SpawnType.Human);

            WayPoint[] assignedWayPoints = new WayPoint[crew.Count];

            for (int i = 0; i < crew.Count; i++ )
            {
                //try to give the crew member a spawnpoint that hasn't been assigned to anyone and matches their job                
                for (int n = 0; n < unassignedWayPoints.Count; n++)
                {
                    if (crew[i].Job.Prefab != unassignedWayPoints[n].AssignedJob) { continue; }
                    assignedWayPoints[i] = unassignedWayPoints[n];
                    unassignedWayPoints.RemoveAt(n);

                    break;
                }                
            }

            //go through the crewmembers that don't have a spawnpoint yet (if any)
            for (int i = 0; i < crew.Count; i++)
            {
                if (assignedWayPoints[i] != null) { continue; }

                //try to assign a spawnpoint that matches the job, even if the spawnpoint is already assigned to someone else
                foreach (WayPoint wp in subWayPoints)
                {
                    if (wp.spawnType != SpawnType.Human || wp.AssignedJob != crew[i].Job.Prefab) { continue; }

                    assignedWayPoints[i] = wp;
                    break;
                }
                if (assignedWayPoints[i] != null) { continue; }

                //try to assign a spawnpoint that isn't meant for any specific job
                var nonJobSpecificPoints = subWayPoints.FindAll(wp => wp.spawnType == SpawnType.Human && wp.AssignedJob == null);
                if (nonJobSpecificPoints.Any())
                {
                    assignedWayPoints[i] = nonJobSpecificPoints[Rand.Int(nonJobSpecificPoints.Count, Rand.RandSync.ServerAndClient)];
                }

                if (assignedWayPoints[i] != null) { continue; }

                //everything else failed -> just give a random spawnpoint inside the sub
                assignedWayPoints[i] = GetRandom(SpawnType.Human, null, submarine, useSyncedRand: true);
            }

            for (int i = 0; i < assignedWayPoints.Length; i++)
            {
                if (assignedWayPoints[i] == null)
                {
                    DebugConsole.AddWarning("Couldn't find a waypoint for " + crew[i].Name + "!");
                    assignedWayPoints[i] = WayPointList[0];
                }
            }

            return assignedWayPoints;
        }

        public void FindHull()
        {
            CurrentHull = Hull.FindHull(WorldPosition, CurrentHull);
#if CLIENT
            //we may not be able to find the hull with the optimized method in the sub editor if new hulls have been added, use the unoptimized method
            if (Screen.Selected == GameMain.SubEditorScreen)
            {
                CurrentHull ??= Hull.FindHullUnoptimized(WorldPosition);
            }
#endif
        }

        public override void OnMapLoaded()
        {
            InitializeLinks();
            FindHull();
            FindStairs();
        }

        private Structure FindStairs()
        {
            Stairs = null;
            Body pickedBody = Submarine.PickBody(SimPosition, SimPosition - new Vector2(0, 1.2f), null, Physics.CollisionStairs);
            if (pickedBody != null && pickedBody.UserData is Structure structure && structure.StairDirection != Direction.None)
            {
                Stairs = structure;
            }
            return Stairs;
        }

        public void InitializeLinks()
        {
            if (gapId > 0) 
            { 
                ConnectedGap = FindEntityByID(gapId) as Gap;
                gapId = 0;
            }
            if (ladderId > 0)
            {
                if (FindEntityByID(ladderId) is Item ladderItem) { Ladders = ladderItem.GetComponent<Ladder>(); }
                ladderId = 0;
            }
        }

        public static WayPoint Load(ContentXElement element, Submarine submarine, IdRemap idRemap)
        {
            Rectangle rect = new Rectangle(
                int.Parse(element.GetAttribute("x").Value),
                int.Parse(element.GetAttribute("y").Value),
                (int)Submarine.GridSize.X, (int)Submarine.GridSize.Y);


            Enum.TryParse(element.GetAttributeString("spawn", "Path"), out SpawnType spawnType);
            WayPoint w = new WayPoint(spawnType == SpawnType.Path ? Type.WayPoint : Type.SpawnPoint, rect, submarine, idRemap.GetOffsetId(element))
            {
                spawnType = spawnType
            };

            string idCardDescString = element.GetAttributeString("idcarddesc", "");
            if (!string.IsNullOrWhiteSpace(idCardDescString))
            {
                w.IdCardDesc = idCardDescString;
            }
            string idCardTagString = element.GetAttributeString("idcardtags", "");
            if (!string.IsNullOrWhiteSpace(idCardTagString))
            {
                w.IdCardTags = idCardTagString.Split(',');
            }

            w.tags = element.GetAttributeIdentifierArray("tags", Array.Empty<Identifier>()).ToHashSet();

            string jobIdentifier = element.GetAttributeString("job", "").ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(jobIdentifier))
            {
                w.AssignedJob = 
                    JobPrefab.Get(jobIdentifier) ??
                    JobPrefab.Prefabs.Find(jp => jp.Name.Equals(jobIdentifier, StringComparison.OrdinalIgnoreCase));                
            }

            w.linkedToID = new List<ushort>();
            w.ladderId = idRemap.GetOffsetId(element.GetAttributeInt("ladders", 0));
            w.gapId = idRemap.GetOffsetId(element.GetAttributeInt("gap", 0));

            int i = 0;
            while (element.GetAttribute("linkedto" + i) != null)
            {
                int srcId = int.Parse(element.GetAttribute("linkedto" + i).Value);
                int destId = idRemap.GetOffsetId(srcId);
                if (destId > 0)
                {
                    w.linkedToID.Add((ushort)destId);
                }
                else
                {
                    w.unresolvedLinkedToID ??= new List<ushort>();
                    w.unresolvedLinkedToID.Add((ushort)srcId);
                }
                i += 1;
            }
            return w;
        }

        public override XElement Save(XElement parentElement)
        {
            if (!ShouldBeSaved) return null;
            XElement element = new XElement("WayPoint");

            element.Add(new XAttribute("ID", ID),
                new XAttribute("x", (int)(rect.X - Submarine.HiddenSubPosition.X)),
                new XAttribute("y", (int)(rect.Y - Submarine.HiddenSubPosition.Y)),
                new XAttribute("spawn", spawnType));

            if (!string.IsNullOrWhiteSpace(IdCardDesc)) element.Add(new XAttribute("idcarddesc", IdCardDesc));            
            if (idCardTags.Length > 0)
            {
                element.Add(new XAttribute("idcardtags", string.Join(",", idCardTags)));
            }
            if (tags.Count > 0)
            {
                element.Add(new XAttribute("tags", string.Join(",", tags)));
            }

            if (AssignedJob != null) element.Add(new XAttribute("job", AssignedJob.Identifier));
            if (ConnectedGap != null) element.Add(new XAttribute("gap", ConnectedGap.ID));
            if (Ladders != null) element.Add(new XAttribute("ladders", Ladders.Item.ID));

            parentElement.Add(element);

            if (linkedTo != null)
            {
                int i = 0;
                foreach (MapEntity e in linkedTo)
                {
                    if (!e.ShouldBeSaved || (e.Removed != Removed)) { continue; }
                    if (e.Submarine?.Info.Type != Submarine?.Info.Type) { continue; }
                    element.Add(new XAttribute("linkedto" + i, e.ID));
                    i += 1;
                }
            }

            return element;
        }

        public override void ShallowRemove()
        {
            base.ShallowRemove();
            WayPointList.Remove(this);
        }

        public override void Remove()
        {
            base.Remove();
            CurrentHull = null;
            ConnectedGap = null;
            Tunnel = null;
            Ruin = null;
            Stairs = null;
            Ladders = null;
            OnLinksChanged = null;
            WayPointList.Remove(this);
        } 
    }
}
