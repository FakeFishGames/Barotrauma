using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.Extensions;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Barotrauma.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Voronoi2;

namespace Barotrauma
{
    public enum Direction : byte
    {
        None = 0, Left = 1, Right = 2
    }

    partial class Submarine : Entity, IServerPositionSync
    {
        public SubmarineInfo Info { get; private set; }

        public CharacterTeamType TeamID = CharacterTeamType.None;

        public static readonly Vector2 HiddenSubStartPosition = new Vector2(-50000.0f, 10000.0f);
        //position of the "actual submarine" which is rendered wherever the SubmarineBody is 
        //should be in an unreachable place
        public Vector2 HiddenSubPosition
        {
            get;
            private set;
        }

        public ushort IdOffset
        {
            get;
            private set;
        }

        public static bool LockX, LockY;

        public static readonly Vector2 GridSize = new Vector2(16.0f, 16.0f);

        public static readonly Submarine[] MainSubs = new Submarine[2];
        public static Submarine MainSub
        {
            get { return MainSubs[0]; }
            set { MainSubs[0] = value; }
        }
        private static readonly List<Submarine> loaded = new List<Submarine>();

        private readonly Identifier upgradeEventIdentifier;

        private static List<MapEntity> visibleEntities;
        public static IEnumerable<MapEntity> VisibleEntities
        {
            get { return visibleEntities; }
        }

        private SubmarineBody subBody;

        public readonly Dictionary<Submarine, DockingPort> ConnectedDockingPorts;
        public IEnumerable<Submarine> DockedTo
        {
            get
            {
                if (ConnectedDockingPorts == null) { yield break; }
                foreach (Submarine sub in ConnectedDockingPorts.Keys)
                {
                    yield return sub;
                }
            }
        }

        private static Vector2 lastPickedPosition;
        private static float lastPickedFraction;
        private static Fixture lastPickedFixture;
        private static Vector2 lastPickedNormal;

        private Vector2 prevPosition;

        private float networkUpdateTimer;

        private EntityGrid entityGrid = null;

        //properties ----------------------------------------------------

        public bool ShowSonarMarker = true;

        public static Vector2 LastPickedPosition
        {
            get { return lastPickedPosition; }
        }

        public static float LastPickedFraction
        {
            get { return lastPickedFraction; }
        }

        public static Fixture LastPickedFixture
        {
            get { return lastPickedFixture; }
        }

        public static Vector2 LastPickedNormal
        {
            get { return lastPickedNormal; }
        }

        public bool Loading
        {
            get;
            private set;
        }

        public bool GodMode
        {
            get;
            set;
        }

        public static List<Submarine> Loaded
        {
            get { return loaded; }
        }

        public SubmarineBody SubBody
        {
            get { return subBody; }
        }

        public PhysicsBody PhysicsBody
        {
            get { return subBody?.Body; }
        }

        /// <summary>
        /// Extents of the solid items/structures (ones with a physics body) and hulls
        /// </summary>
        public Rectangle Borders
        {
            get
            {
                return subBody == null ? Rectangle.Empty : subBody.Borders;
            }
        }

        /// <summary>
        /// Extents of all the visible items/structures/hulls (including ones without a physics body)
        /// </summary>
        public Rectangle VisibleBorders
        {
            get
            {
                return subBody == null ? Rectangle.Empty : subBody.VisibleBorders;
            }
        }

        public override Vector2 Position
        {
            get { return subBody == null ? Vector2.Zero : subBody.Position - HiddenSubPosition; }
        }

        public override Vector2 WorldPosition
        {
            get
            {
                return subBody == null ? Vector2.Zero : subBody.Position;
            }
        }

        private float? realWorldCrushDepth;
        public float RealWorldCrushDepth
        {
            get
            {
                if (!realWorldCrushDepth.HasValue)
                {
                    realWorldCrushDepth = float.PositiveInfinity;
                    foreach (Structure structure in Structure.WallList)
                    {
                        if (structure.Submarine != this || !structure.HasBody || structure.Indestructible) { continue; }
                        realWorldCrushDepth = Math.Min(structure.CrushDepth, realWorldCrushDepth.Value);
                    }
                }
                return realWorldCrushDepth.Value;
            }
            set { realWorldCrushDepth = value; }
        }

        /// <summary>
        /// How deep down the sub is from the surface of Europa in meters (affected by level type, does not correspond to "actual" coordinate systems)
        /// </summary>
        public float RealWorldDepth
        {
            get
            {
                if (Level.Loaded?.GenerationParams == null)
                {
                    return -WorldPosition.Y * Physics.DisplayToRealWorldRatio;
                }
                return Level.Loaded.GetRealWorldDepth(WorldPosition.Y);
            }
        }

        public bool AtEndExit
        {
            get
            {
                if (Level.Loaded == null) { return false; }
                if (Level.Loaded.EndOutpost != null)
                {
                    if (DockedTo.Contains(Level.Loaded.EndOutpost))
                    {
                        return true;
                    }
                    else if (Level.Loaded.EndOutpost.exitPoints.Any())
                    {
                        return IsAtOutpostExit(Level.Loaded.EndOutpost);
                    }
                }
                else if (Level.Loaded.Type == LevelData.LevelType.Outpost && Level.Loaded.StartOutpost != null)
                {
                    //in outpost levels, the outpost is always the start outpost: check it if has an exit
                    return IsAtOutpostExit(Level.Loaded.StartOutpost);
                }
                return (Vector2.DistanceSquared(Position + HiddenSubPosition, Level.Loaded.EndExitPosition) < Level.ExitDistance * Level.ExitDistance);
            }
        }

        public bool AtStartExit
        {
            get
            {
                if (Level.Loaded == null) { return false; }
                if (Level.Loaded.StartOutpost != null)
                {
                    if (DockedTo.Contains(Level.Loaded.StartOutpost))
                    {
                        return true;
                    }
                    else if (Level.Loaded.StartOutpost.exitPoints.Any())
                    {
                        return IsAtOutpostExit(Level.Loaded.StartOutpost);
                    }
                }
                return (Vector2.DistanceSquared(Position + HiddenSubPosition, Level.Loaded.StartExitPosition) < Level.ExitDistance * Level.ExitDistance);
            }
        }

        public bool AtEitherExit => AtStartExit || AtEndExit;

        private bool IsAtOutpostExit(Submarine outpost)
        {
            if (outpost.exitPoints.Any())
            {
                Rectangle worldBorders = Borders;
                worldBorders.Location += WorldPosition.ToPoint();
                foreach (var exitPoint in outpost.exitPoints)
                {
                    if (exitPoint.ExitPointSize != Point.Zero)
                    {
                        if (RectsOverlap(worldBorders, exitPoint.ExitPointWorldRect)) { return true; }
                    }
                    else
                    {
                        if (RectContains(worldBorders, exitPoint.WorldPosition)) { return true; }
                    }
                }
            }
            return false;
        }


        public new Vector2 DrawPosition
        {
            get;
            private set;
        }

        public override Vector2 SimPosition
        {
            get
            {
                return ConvertUnits.ToSimUnits(Position);
            }
        }

        public Vector2 Velocity
        {
            get { return subBody == null ? Vector2.Zero : subBody.Velocity; }
            set
            {
                if (subBody == null) { return; }
                subBody.Velocity = value;
            }
        }

        public List<Vector2> HullVertices
        {
            get { return subBody?.HullVertices; }
        }

        private int? submarineSpecificIDTag;
        public int SubmarineSpecificIDTag
        {
            get
            {
                submarineSpecificIDTag ??= ToolBox.StringToInt((Level.Loaded?.Seed ?? "") + Info.Name);
                return submarineSpecificIDTag.Value;
            }
        }


        public bool AtDamageDepth
        {
            get
            {
                if (Level.Loaded == null || subBody == null) { return false; }
                return RealWorldDepth > Level.Loaded.RealWorldCrushDepth && RealWorldDepth > RealWorldCrushDepth;
            }
        }

        private readonly List<WayPoint> exitPoints = new List<WayPoint>();
        public IReadOnlyList<WayPoint> ExitPoints { get { return exitPoints; } }

        public override string ToString()
        {
            return "Barotrauma.Submarine (" + (Info?.Name ?? "[NULL INFO]") + ", " + IdOffset + ")";
        }

        public int CalculateBasePrice()
        {
            int minPrice = 1000;
            float volume = Hull.HullList.Where(h => h.Submarine == this).Sum(h => h.Volume);
            float itemValue = Item.ItemList.Where(it => it.Submarine == this).Sum(it => it.Prefab.GetMinPrice() ?? 0);
            float price = volume / 500.0f + itemValue / 100.0f;
            System.Diagnostics.Debug.Assert(price >= 0);
            return Math.Max(minPrice, (int)price);
        }

        private float ballastFloraTimer;
        public bool ImmuneToBallastFlora { get; set; }
        public void AttemptBallastFloraInfection(Identifier identifier, float deltaTime, float probability)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (ImmuneToBallastFlora) { return; }

            if (ballastFloraTimer < 1f)
            {
                ballastFloraTimer += deltaTime;
                return;
            }

            ballastFloraTimer = 0;
            if (Rand.Range(0f, 1f, Rand.RandSync.Unsynced) >= probability) { return; }

            List<Pump> pumps = new List<Pump>();
            List<Item> allItems = GetItems(true);

            bool anyHasTag = allItems.Any(i => i.HasTag("ballast"));

            foreach (Item item in allItems)
            {
                if ((!anyHasTag || item.HasTag("ballast")) && item.GetComponent<Pump>() is { } pump)
                {
                    pumps.Add(pump);
                }
            }

            if (!pumps.Any()) { return; }

            Pump randomPump = pumps.GetRandom(Rand.RandSync.Unsynced);
            if (randomPump.IsOn && randomPump.HasPower && randomPump.FlowPercentage > 0 && randomPump.Item.Condition > 0.0f)
            {
                randomPump.InfectBallast(identifier);
#if SERVER
                randomPump.Item.CreateServerEvent(randomPump);
#endif
            }
        }

        public void MakeWreck()
        {
            Info.Type = SubmarineType.Wreck;
            ShowSonarMarker = false;
            DockedTo.ForEach(s => s.ShowSonarMarker = false);
            PhysicsBody.FarseerBody.BodyType = BodyType.Static;
            TeamID = CharacterTeamType.None;
        }

        public WreckAI WreckAI { get; private set; }
        public SubmarineTurretAI TurretAI { get; private set; }

        public bool CreateWreckAI()
        {
            WreckAI = WreckAI.Create(this);
            return WreckAI != null;
        }

        /// <summary>
        /// Creates an AI that operates all the turrets on a sub, same as Thalamus but only operates the turrets.
        /// </summary>
        public bool CreateTurretAI()
        {
            TurretAI = new SubmarineTurretAI(this);
            return TurretAI != null;
        }

        public void DisableWreckAI()
        {
            if (WreckAI == null)
            {
                WreckAI.RemoveThalamusItems(this);
            }
            else
            {
                WreckAI?.Remove();
                WreckAI = null;
            }
        }

        /// <summary>
        /// Returns a rect that contains the borders of this sub and all subs docked to it
        /// </summary>
        public Rectangle GetDockedBorders(List<Submarine> checkd = null)
        {
            if (checkd == null) { checkd = new List<Submarine>(); }
            checkd.Add(this);

            Rectangle dockedBorders = Borders;

            var connectedSubs = DockedTo.Where(s => !checkd.Contains(s) && !s.Info.IsOutpost).ToList();

            foreach (Submarine dockedSub in connectedSubs)
            {
                //use docking ports instead of world position to determine
                //borders, as world position will not necessarily match where
                //the subs are supposed to go
                Vector2? expectedLocation = CalculateDockOffset(this, dockedSub);
                if (expectedLocation == null) { continue; }

                Rectangle dockedSubBorders = dockedSub.GetDockedBorders(checkd);
                dockedSubBorders.Location += MathUtils.ToPoint(expectedLocation.Value);

                dockedBorders.Y = -dockedBorders.Y;
                dockedSubBorders.Y = -dockedSubBorders.Y;
                dockedBorders = Rectangle.Union(dockedBorders, dockedSubBorders);
                dockedBorders.Y = -dockedBorders.Y;
            }

            return dockedBorders;
        }

        /// <summary>
        /// Don't use this directly, because the list is updated only when GetConnectedSubs() is called. The method is called so frequently that we don't want to create new list here.
        /// </summary>
        private readonly List<Submarine> connectedSubs = new List<Submarine>(2);
        /// <summary>
        /// Returns a list of all submarines that are connected to this one via docking ports, including this sub.
        /// </summary>
        public List<Submarine> GetConnectedSubs()
        {
            connectedSubs.Clear();
            connectedSubs.Add(this);
            GetConnectedSubsRecursive(connectedSubs);

            return connectedSubs;
        }

        private void GetConnectedSubsRecursive(List<Submarine> subs)
        {
            foreach (Submarine dockedSub in DockedTo)
            {
                if (subs.Contains(dockedSub)) continue;

                subs.Add(dockedSub);
                dockedSub.GetConnectedSubsRecursive(subs);
            }
        }

        /// <summary>
        /// Attempt to find a spawn position close to the specified position where the sub doesn't collide with walls/ruins
        /// </summary>
        public Vector2 FindSpawnPos(Vector2 spawnPos, Point? submarineSize = null, float subDockingPortOffset = 0.0f, int verticalMoveDir = 0)
        {
            Rectangle dockedBorders = GetDockedBorders();
            Vector2 diffFromDockedBorders =
                new Vector2(dockedBorders.Center.X, dockedBorders.Y - dockedBorders.Height / 2)
                - new Vector2(Borders.Center.X, Borders.Y - Borders.Height / 2);

            int minWidth = Math.Max(submarineSize.HasValue ? submarineSize.Value.X : dockedBorders.Width, 500);
            int minHeight = Math.Max(submarineSize.HasValue ? submarineSize.Value.Y : dockedBorders.Height, 1000);
            //a bit of extra padding to prevent the sub from spawning in a super tight gap between walls
            int padding = 100;
            minWidth += padding;
            minHeight += padding;

            Vector2 limits = GetHorizontalLimits(spawnPos, minWidth, minHeight, 0);
            if (verticalMoveDir != 0)
            {
                verticalMoveDir = Math.Sign(verticalMoveDir);
                //do a raycast towards the top/bottom of the level depending on direction
                Vector2 potentialPos = new Vector2(spawnPos.X, verticalMoveDir > 0 ? Level.Loaded.Size.Y : 0);

                //3 raycasts (left, middle and right side of the sub, so we don't accidentally raycast up a passage too narrow for the sub)
                for (int x = -1; x <= 1; x++)
                {
                    Vector2 xOffset = Vector2.UnitX * minWidth / 2 * x;
                    if (PickBody(
                        ConvertUnits.ToSimUnits(spawnPos + xOffset),
                        ConvertUnits.ToSimUnits(potentialPos + xOffset),
                        collisionCategory: Physics.CollisionLevel | Physics.CollisionWall) != null)
                    {
                        int offsetFromWall = 10 * -verticalMoveDir;
                        //if the raycast hit a wall, attempt to place the spawnpos there
                        if (verticalMoveDir > 0)
                        {
                            potentialPos.Y = Math.Min(potentialPos.Y, ConvertUnits.ToDisplayUnits(LastPickedPosition.Y) + offsetFromWall);
                        }
                        else
                        {
                            potentialPos.Y = Math.Max(potentialPos.Y, ConvertUnits.ToDisplayUnits(LastPickedPosition.Y) + offsetFromWall);
                        }
                    }
                }

                //step away from the top/bottom of the level, or from whatever wall the raycast hit,
                //until we found a spot where there's enough room to place the sub
                float dist = Math.Abs(potentialPos.Y - spawnPos.Y);
                for (float d = dist; d > 0; d -= 100.0f)
                {
                    float y = spawnPos.Y + verticalMoveDir * d;
                    limits = GetHorizontalLimits(new Vector2(spawnPos.X, y), minWidth, minHeight, verticalMoveDir);
                    if (limits.Y - limits.X > minWidth)
                    {
                        spawnPos = new Vector2(spawnPos.X, y - (dockedBorders.Height * 0.5f * verticalMoveDir));
                        break;
                    }
                }
            }

            static Vector2 GetHorizontalLimits(Vector2 spawnPos, float minWidth, float minHeight, int verticalMoveDir)
            {
                Vector2 refPos = spawnPos - Vector2.UnitY * minHeight * 0.5f * Math.Sign(verticalMoveDir);

                float minX = float.MinValue, maxX = float.MaxValue;
                foreach (VoronoiCell cell in Level.Loaded.GetAllCells())
                {
                    foreach (GraphEdge e in cell.Edges)
                    {
                        if ((e.Point1.Y < refPos.Y - minHeight * 0.5f && e.Point2.Y < refPos.Y - minHeight * 0.5f) ||
                            (e.Point1.Y > refPos.Y + minHeight * 0.5f && e.Point2.Y > refPos.Y + minHeight * 0.5f))
                        {
                            continue;
                        }

                        if (cell.Site.Coord.X < refPos.X)
                        {
                            minX = Math.Max(minX, Math.Max(e.Point1.X, e.Point2.X));
                        }
                        else
                        {
                            maxX = Math.Min(maxX, Math.Min(e.Point1.X, e.Point2.X));
                        }
                    }
                }

                foreach (var ruin in Level.Loaded.Ruins)
                {
                    if (Math.Abs(ruin.Area.Center.Y - refPos.Y) > (minHeight + ruin.Area.Height) * 0.5f) { continue; }
                    if (ruin.Area.Center.X < refPos.X)
                    {
                        minX = Math.Max(minX, ruin.Area.Right + 100.0f);
                    }
                    else
                    {
                        maxX = Math.Min(maxX, ruin.Area.X - 100.0f);
                    }
                }
                return new Vector2(Math.Max(minX, spawnPos.X - minWidth), Math.Min(maxX, spawnPos.X + minWidth));
            }

            if (limits.X < 0.0f && limits.Y > Level.Loaded.Size.X)
            {
                //no walls found at either side, just use the initial spawnpos and hope for the best
            }
            else if (limits.X < 0)
            {
                //no wall found at the left side, spawn to the left from the right-side wall
                spawnPos.X = limits.Y - minWidth * 0.5f - 100.0f + subDockingPortOffset;
            }
            else if (limits.Y > Level.Loaded.Size.X)
            {
                //no wall found at right side, spawn to the right from the left-side wall
                spawnPos.X = limits.X + minWidth * 0.5f + 100.0f + subDockingPortOffset;
            }
            else
            {
                //walls found at both sides, use their midpoint
                spawnPos.X = (limits.X + limits.Y) / 2 + subDockingPortOffset;
            }

            spawnPos.Y = MathHelper.Clamp(spawnPos.Y, dockedBorders.Height / 2 + 10, Level.Loaded.Size.Y - dockedBorders.Height / 2 - padding * 2);
            return spawnPos - diffFromDockedBorders;
        }

        public void UpdateTransform(bool interpolate = true)
        {
            DrawPosition = interpolate ?
                Timing.Interpolate(prevPosition, Position) :
                Position;
            if (!interpolate) { prevPosition = Position; }
        }

        //math/physics stuff ----------------------------------------------------

        public static Vector2 VectorToWorldGrid(Vector2 position)
        {
            position.X = (float)Math.Floor(position.X / GridSize.X) * GridSize.X;
            position.Y = (float)Math.Ceiling(position.Y / GridSize.Y) * GridSize.Y;

            return position;
        }

        public Rectangle CalculateDimensions(bool onlyHulls = true)
        {
            List<MapEntity> entities = onlyHulls ?
                Hull.HullList.FindAll(h => h.Submarine == this).Cast<MapEntity>().ToList() :
                MapEntity.mapEntityList.FindAll(me => me.Submarine == this);

            //ignore items whose body is disabled (wires, items inside cabinets)
            entities.RemoveAll(e =>
            {
                if (e is Item item)
                {
                    if (item.GetComponent<Turret>() != null) { return false; }
                    if (item.body != null && !item.body.Enabled) { return true; }
                }
                if (e.HiddenInGame) { return true; }
                return false;
            });

            if (entities.Count == 0) { return Rectangle.Empty; }

            float minX = entities[0].Rect.X, minY = entities[0].Rect.Y - entities[0].Rect.Height;
            float maxX = entities[0].Rect.Right, maxY = entities[0].Rect.Y;

            for (int i = 1; i < entities.Count; i++)
            {
                if (entities[i] is Item item)
                {
                    var turret = item.GetComponent<Turret>();
                    if (turret != null)
                    {
                        minX = Math.Min(minX, entities[i].Rect.X + turret.TransformedBarrelPos.X * 2f);
                        minY = Math.Min(minY, entities[i].Rect.Y - entities[i].Rect.Height - turret.TransformedBarrelPos.Y * 2f);
                        maxX = Math.Max(maxX, entities[i].Rect.Right + turret.TransformedBarrelPos.X * 2f);
                        maxY = Math.Max(maxY, entities[i].Rect.Y - turret.TransformedBarrelPos.Y * 2f);
                    }
                }
                minX = Math.Min(minX, entities[i].Rect.X);
                minY = Math.Min(minY, entities[i].Rect.Y - entities[i].Rect.Height);
                maxX = Math.Max(maxX, entities[i].Rect.Right);
                maxY = Math.Max(maxY, entities[i].Rect.Y);
            }

            return new Rectangle((int)minX, (int)minY, (int)(maxX - minX), (int)(maxY - minY));
        }

        public static Rectangle AbsRect(Vector2 pos, Vector2 size)
        {
            if (size.X < 0.0f)
            {
                pos.X += size.X;
                size.X = -size.X;
            }
            if (size.Y < 0.0f)
            {
                pos.Y -= size.Y;
                size.Y = -size.Y;
            }

            return new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y);
        }

        public static bool RectContains(Rectangle rect, Vector2 pos, bool inclusive = false)
        {
            if (inclusive)
            {
                return (pos.X >= rect.X && pos.X <= rect.X + rect.Width
                    && pos.Y <= rect.Y && pos.Y >= rect.Y - rect.Height);
            }
            else
            {
                return (pos.X > rect.X && pos.X < rect.X + rect.Width
                    && pos.Y < rect.Y && pos.Y > rect.Y - rect.Height);
            }
        }

        public static bool RectsOverlap(Rectangle rect1, Rectangle rect2, bool inclusive = true)
        {
            if (inclusive)
            {
                return !(rect1.X > rect2.X + rect2.Width || rect1.X + rect1.Width < rect2.X ||
                    rect1.Y < rect2.Y - rect2.Height || rect1.Y - rect1.Height > rect2.Y);
            }
            else
            {
                return !(rect1.X >= rect2.X + rect2.Width || rect1.X + rect1.Width <= rect2.X ||
                    rect1.Y <= rect2.Y - rect2.Height || rect1.Y - rect1.Height >= rect2.Y);
            }
        }

        public static Body PickBody(Vector2 rayStart, Vector2 rayEnd, IEnumerable<Body> ignoredBodies = null, Category? collisionCategory = null, bool ignoreSensors = true, Predicate<Fixture> customPredicate = null, bool allowInsideFixture = false)
        {
            if (Vector2.DistanceSquared(rayStart, rayEnd) < 0.0001f)
            {
                return null;
            }

            float closestFraction = 1.0f;
            Vector2 closestNormal = Vector2.Zero;
            Fixture closestFixture = null;
            Body closestBody = null;
            if (allowInsideFixture)
            {
                var aabb = new FarseerPhysics.Collision.AABB(rayStart - Vector2.One * 0.001f, rayStart + Vector2.One * 0.001f);
                GameMain.World.QueryAABB((fixture) =>
                {
                    if (!CheckFixtureCollision(fixture, ignoredBodies, collisionCategory, ignoreSensors, customPredicate)) { return true; }

                    fixture.Body.GetTransform(out FarseerPhysics.Common.Transform transform);
                    if (!fixture.Shape.TestPoint(ref transform, ref rayStart)) { return true; }

                    closestFraction = 0.0f;
                    closestNormal = Vector2.Normalize(rayEnd - rayStart);
                    closestFixture = fixture;
                    if (fixture.Body != null) { closestBody = fixture.Body; }
                    return false;
                }, ref aabb);
                if (closestFraction <= 0.0f)
                {
                    lastPickedPosition = rayStart;
                    lastPickedFraction = closestFraction;
                    lastPickedFixture = closestFixture;
                    lastPickedNormal = closestNormal;
                    return closestBody;
                }
            }

            GameMain.World.RayCast((fixture, point, normal, fraction) =>
            {
                if (!CheckFixtureCollision(fixture, ignoredBodies, collisionCategory, ignoreSensors, customPredicate)) { return -1; }

                if (fraction < closestFraction)
                {
                    closestFraction = fraction;
                    closestNormal = normal;
                    closestFixture = fixture;
                    if (fixture.Body != null) closestBody = fixture.Body;
                }
                return fraction;
            }, rayStart, rayEnd, collisionCategory ?? Category.All);

            lastPickedPosition = rayStart + (rayEnd - rayStart) * closestFraction;
            lastPickedFraction = closestFraction;
            lastPickedFixture = closestFixture;
            lastPickedNormal = closestNormal;

            return closestBody;
        }

        private static readonly Dictionary<Body, float> bodyDist = new Dictionary<Body, float>();
        private static readonly List<Body> bodies = new List<Body>();

        public static float LastPickedBodyDist(Body body)
        {
            if (!bodyDist.ContainsKey(body)) { return 0.0f; }
            return bodyDist[body];
        }

        /// <summary>
        /// Returns a list of physics bodies the ray intersects with, sorted according to distance (the closest body is at the beginning of the list).
        /// </summary>
        /// <param name="customPredicate">Can be used to filter the bodies based on some condition. If the predicate returns false, the body isignored.</param>
        /// <param name="allowInsideFixture">Should fixtures that the start of the ray is inside be returned</param>
        public static IEnumerable<Body> PickBodies(Vector2 rayStart, Vector2 rayEnd, IEnumerable<Body> ignoredBodies = null, Category? collisionCategory = null, bool ignoreSensors = true, Predicate<Fixture> customPredicate = null, bool allowInsideFixture = false)
        {
            if (Vector2.DistanceSquared(rayStart, rayEnd) < 0.00001f)
            {
                rayEnd += Vector2.UnitX * 0.001f;
            }

            float closestFraction = 1.0f;
            bodies.Clear();
            bodyDist.Clear();
            GameMain.World.RayCast((fixture, point, normal, fraction) =>
            {
                if (!CheckFixtureCollision(fixture, ignoredBodies, collisionCategory, ignoreSensors, customPredicate)) { return -1; }

                if (fixture.Body != null)
                {
                    bodies.Add(fixture.Body);
                    bodyDist[fixture.Body] = fraction;
                }
                if (fraction < closestFraction)
                {
                    lastPickedPosition = rayStart + (rayEnd - rayStart) * fraction;
                    lastPickedFraction = fraction;
                    lastPickedNormal = normal;
                    lastPickedFixture = fixture;
                }
                //continue
                return -1;
            }, rayStart, rayEnd, collisionCategory ?? Category.All);

            if (allowInsideFixture)
            {
                var aabb = new FarseerPhysics.Collision.AABB(rayStart - Vector2.One * 0.001f, rayStart + Vector2.One * 0.001f);
                GameMain.World.QueryAABB((fixture) =>
                {
                    if (bodies.Contains(fixture.Body) || fixture.Body == null) { return true; }
                    if (!CheckFixtureCollision(fixture, ignoredBodies, collisionCategory, ignoreSensors, customPredicate)) { return true; }

                    fixture.Body.GetTransform(out FarseerPhysics.Common.Transform transform);
                    if (!fixture.Shape.TestPoint(ref transform, ref rayStart)) { return true; }

                    closestFraction = 0.0f;
                    lastPickedPosition = rayStart;
                    lastPickedFraction = 0.0f;
                    lastPickedNormal = Vector2.Normalize(rayEnd - rayStart);
                    lastPickedFixture = fixture;
                    bodies.Add(fixture.Body);
                    bodyDist[fixture.Body] = 0.0f;
                    return false;
                }, ref aabb);
            }

            bodies.Sort((b1, b2) => { return bodyDist[b1].CompareTo(bodyDist[b2]); });
            return bodies;
        }

        private static bool CheckFixtureCollision(Fixture fixture, IEnumerable<Body> ignoredBodies = null, Category? collisionCategory = null, bool ignoreSensors = true, Predicate<Fixture> customPredicate = null)
        {
            if (fixture == null ||
                (ignoreSensors && fixture.IsSensor) ||
                fixture.CollisionCategories == Category.None ||
                fixture.CollisionCategories == Physics.CollisionItem)
            {
                return false;
            }

            if (customPredicate != null && !customPredicate(fixture))
            {
                return false;
            }

            if (collisionCategory != null &&
                !fixture.CollisionCategories.HasFlag((Category)collisionCategory) &&
                !((Category)collisionCategory).HasFlag(fixture.CollisionCategories))
            {
                return false;
            }

            if (ignoredBodies != null && ignoredBodies.Contains(fixture.Body))
            {
                return false;
            }

            if (fixture.Body.UserData is Structure structure)
            {
                if (structure.IsPlatform && collisionCategory != null && !((Category)collisionCategory).HasFlag(Physics.CollisionPlatform))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// check visibility between two points (in sim units)
        /// </summary>
        /// <returns>a physics body that was between the points (or null)</returns>
        public static Body CheckVisibility(Vector2 rayStart, Vector2 rayEnd, bool ignoreLevel = false, bool ignoreSubs = false, bool ignoreSensors = true, bool ignoreDisabledWalls = true, bool ignoreBranches = true)
        {
            Body closestBody = null;
            float closestFraction = 1.0f;
            Fixture closestFixture = null;
            Vector2 closestNormal = Vector2.Zero;

            if (Vector2.DistanceSquared(rayStart, rayEnd) < 0.01f)
            {
                lastPickedPosition = rayEnd;
                return null;
            }

            GameMain.World.RayCast((fixture, point, normal, fraction) =>
            {
                if (fixture == null) { return -1; }
                if (ignoreSensors && fixture.IsSensor) { return -1; }
                if (ignoreLevel && fixture.CollisionCategories.HasFlag(Physics.CollisionLevel)) { return -1; }
                if (!fixture.CollisionCategories.HasFlag(Physics.CollisionLevel)
                    && !fixture.CollisionCategories.HasFlag(Physics.CollisionWall)
                    && !fixture.CollisionCategories.HasFlag(Physics.CollisionRepair)) { return -1; }
                if (ignoreSubs && fixture.Body.UserData is Submarine) { return -1; }
                if (ignoreBranches && fixture.Body.UserData is VineTile) { return -1; }
                if (fixture.Body.UserData as string == "ruinroom") { return -1; }
                //the hulls have solid fixtures in the submarine's world space collider, ignore them
                if (fixture.UserData is Hull) { return -1; }
                if (fixture.Body.UserData is Structure structure)
                {
                    if (structure.IsPlatform || structure.StairDirection != Direction.None) { return -1; }
                    if (ignoreDisabledWalls)
                    {
                        int sectionIndex = structure.FindSectionIndex(ConvertUnits.ToDisplayUnits(point));
                        if (sectionIndex > -1 && structure.SectionBodyDisabled(sectionIndex)) { return -1; }
                    }
                }

                if (fraction < closestFraction)
                {
                    closestBody = fixture.Body;
                    closestFraction = fraction;
                    closestFixture = fixture;
                    closestNormal = normal;
                }
                return closestFraction;
            }
            , rayStart, rayEnd);


            lastPickedPosition = rayStart + (rayEnd - rayStart) * closestFraction;
            lastPickedFraction = closestFraction;
            lastPickedFixture = closestFixture;
            lastPickedNormal = closestNormal;
            return closestBody;
        }

        //movement ----------------------------------------------------

        private bool flippedX;
        public bool FlippedX
        {
            get { return flippedX; }
        }

        public void FlipX(List<Submarine> parents = null)
        {
            if (parents == null) { parents = new List<Submarine>(); }
            parents.Add(this);

            flippedX = !flippedX;

            Item.UpdateHulls();

            List<Item> bodyItems = Item.ItemList.FindAll(it => it.Submarine == this && it.body != null);
            List<MapEntity> subEntities = MapEntity.mapEntityList.FindAll(me => me.Submarine == this);

            foreach (MapEntity e in subEntities)
            {
                if (e is Item) continue;
                if (e is LinkedSubmarine linkedSub)
                {
                    Submarine sub = linkedSub.Sub;
                    if (sub == null)
                    {
                        Vector2 relative1 = linkedSub.Position - SubBody.Position;
                        relative1.X = -relative1.X;
                        linkedSub.Rect = new Rectangle((relative1 + SubBody.Position).ToPoint(), linkedSub.Rect.Size);
                    }
                    else if (!parents.Contains(sub))
                    {
                        Vector2 relative1 = sub.SubBody.Position - SubBody.Position;
                        relative1.X = -relative1.X;
                        sub.SetPosition(relative1 + SubBody.Position, new List<Submarine>(parents));
                        sub.FlipX(parents);
                    }
                }
                else
                {
                    e.FlipX(true);
                }
            }

            foreach (MapEntity mapEntity in subEntities)
            {
                mapEntity.Move(-HiddenSubPosition);
            }

            var prevBodyType = subBody.Body.BodyType;
            Vector2 pos = new Vector2(subBody.Position.X, subBody.Position.Y);
            subBody.Body.Remove();
            subBody = new SubmarineBody(this);
            subBody.Body.BodyType = prevBodyType;
            SetPosition(pos, new List<Submarine>(parents.Where(p => p != this)));

            if (entityGrid != null)
            {
                Hull.EntityGrids.Remove(entityGrid);
                entityGrid = null;
            }
            entityGrid = Hull.GenerateEntityGrid(this);

            SubBody.FlipX();

            foreach (MapEntity mapEntity in subEntities)
            {
                mapEntity.Move(HiddenSubPosition);
            }

            for (int i = 0; i < 2; i++)
            {
                foreach (Item item in Item.ItemList)
                {
                    //two passes: flip docking ports on the 2nd pass because the doors need to be correctly flipped for the port's orientation to be determined correctly
                    if ((item.GetComponent<DockingPort>() != null) == (i == 0)) { continue; }
                    if (bodyItems.Contains(item))
                    {
                        item.Submarine = this;
                        if (Position == Vector2.Zero) { item.Move(-HiddenSubPosition); }
                    }
                    else if (item.Submarine != this)
                    {
                        continue;
                    }
                    item.FlipX(true);
                }
            }

            Item.UpdateHulls();
            Gap.UpdateHulls();
#if CLIENT
            Lights.ConvexHull.RecalculateAll(this);
#endif
        }

        public void Update(float deltaTime)
        {
            if (Info.IsWreck)
            {
                WreckAI?.Update(deltaTime);
            }
            TurretAI?.Update(deltaTime);

            if (subBody?.Body == null) { return; }

            if (Level.Loaded != null &&
                WorldPosition.Y < Level.MaxEntityDepth &&
                subBody.Body.Enabled &&
                (GameMain.NetworkMember?.RespawnManager == null || this != GameMain.NetworkMember.RespawnManager.RespawnShuttle))
            {
                subBody.Body.ResetDynamics();
                subBody.Body.Enabled = false;

                foreach (Character c in Character.CharacterList)
                {
                    if (c.Submarine == this)
                    {
                        c.Kill(CauseOfDeathType.Pressure, null);
                        c.Enabled = false;
                    }
                }

                return;
            }


            subBody.Body.LinearVelocity = new Vector2(
                LockX ? 0.0f : subBody.Body.LinearVelocity.X,
                LockY ? 0.0f : subBody.Body.LinearVelocity.Y);

            subBody.Update(deltaTime);

            for (int i = 0; i < 2; i++)
            {
                if (MainSubs[i] == null) { continue; }
                if (this != MainSubs[i] && MainSubs[i].DockedTo.Contains(this)) { return; }
            }

            //send updates more frequently if moving fast
            networkUpdateTimer -= MathHelper.Clamp(Velocity.Length() * 10.0f, 0.1f, 5.0f) * deltaTime;

            if (networkUpdateTimer < 0.0f)
            {
                networkUpdateTimer = 1.0f;
            }
        }

        public void ApplyForce(Vector2 force)
        {
            if (subBody != null) subBody.ApplyForce(force);
        }

        public void EnableMaintainPosition()
        {
            foreach (Item item in Item.ItemList)
            {
                if (item.Submarine != this) { continue; }
                var steering = item.GetComponent<Steering>();
                if (steering == null || item.Connections == null) { continue; }

                //find all the engines and pumps the nav terminal is connected to
                List<Item> connectedItems = new List<Item>();
                foreach (Connection c in item.Connections)
                {
                    if (c.IsPower) { continue; }
                    connectedItems.AddRange(item.GetConnectedComponentsRecursive<Engine>(c).Select(engine => engine.Item));
                    connectedItems.AddRange(item.GetConnectedComponentsRecursive<Pump>(c).Select(pump => pump.Item));
                }

                //if more than 50% of the connected engines/pumps are in another sub, 
                //assume this terminal is used to remotely control something and don't automatically enable autopilot
                if (connectedItems.Count(it => it.Submarine != item.Submarine) > connectedItems.Count / 2)
                {
                    continue;
                }

                steering.MaintainPos = true;
                steering.PosToMaintain = WorldPosition;
                steering.AutoPilot = true;
#if SERVER
                steering.UnsentChanges = true;
#endif
            }
        }

        public void NeutralizeBallast()
        {
            float neutralBallastLevel = 0.5f;
            int selectedSteeringValue = 0;
            foreach (Item item in Item.ItemList)
            {
                if (item.Submarine != this) { continue; }
                var steering = item.GetComponent<Steering>();
                if (steering == null) { continue; }

                //find how many pumps/engines in this sub the steering item is connected to
                int steeringValue = 1;
                Connection connectionX = item.GetComponent<ConnectionPanel>()?.Connections.Find(c => c.Name == "velocity_x_out");
                Connection connectionY = item.GetComponent<ConnectionPanel>()?.Connections.Find(c => c.Name == "velocity_y_out");
                if (connectionX != null)
                {
                    foreach (Engine engine in steering.Item.GetConnectedComponentsRecursive<Engine>(connectionX))
                    {
                        if (engine.Item.Submarine == this) { steeringValue++; }
                    }
                }
                if (connectionY != null)
                {
                    foreach (Pump pump in steering.Item.GetConnectedComponentsRecursive<Pump>(connectionY))
                    {
                        if (pump.Item.Submarine == this) { steeringValue++; }
                    }
                }
                //the nav terminal that's connected to the most engines/pumps in the sub most likely controls the sub (instead of a shuttle or some other system)
                if (steeringValue > selectedSteeringValue)
                {
                    neutralBallastLevel = steering.NeutralBallastLevel;
                }
            }

            HashSet<Hull> ballastHulls = new HashSet<Hull>();
            foreach (Item item in Item.ItemList)
            {
                if (item.Submarine != this) { continue; }
                var pump = item.GetComponent<Pump>();
                if (pump == null || item.CurrentHull == null) { continue; }
                if (!item.HasTag("ballast") && !item.CurrentHull.RoomName.Contains("ballast", StringComparison.OrdinalIgnoreCase)) { continue; }
                pump.FlowPercentage = 0.0f;
                ballastHulls.Add(item.CurrentHull);
            }

            float waterVolume = 0.0f;
            float volume = 0.0f;
            float excessWater = 0.0f;
            foreach (Hull hull in Hull.HullList)
            {
                if (hull.Submarine != this) { continue; }
                waterVolume += hull.WaterVolume;
                volume += hull.Volume;
                if (!ballastHulls.Contains(hull)) { excessWater += hull.WaterVolume; }
            }

            neutralBallastLevel -= excessWater / volume;
            //reduce a bit to be on the safe side (better to float up than sink)
            neutralBallastLevel *= 0.9f;

            foreach (Hull hull in ballastHulls)
            {
                hull.WaterVolume = hull.Volume * neutralBallastLevel;
            }
        }

        public void SetPrevTransform(Vector2 position)
        {
            prevPosition = position;
        }

        public void SetPosition(Vector2 position, List<Submarine> checkd = null, bool forceUndockFromStaticSubmarines = true)
        {
            if (!MathUtils.IsValid(position)) { return; }

            if (checkd == null) { checkd = new List<Submarine>(); }
            if (checkd.Contains(this)) { return; }

            checkd.Add(this);

            subBody.SetPosition(position);
            UpdateTransform(interpolate: false);

            foreach (Submarine dockedSub in DockedTo)
            {
                if (dockedSub.PhysicsBody.BodyType == BodyType.Static && forceUndockFromStaticSubmarines)
                {
                    if (ConnectedDockingPorts.TryGetValue(dockedSub, out DockingPort port))
                    {
                        port.Undock(applyEffects: false);
                        continue;
                    }
                }
                Vector2? expectedLocation = CalculateDockOffset(this, dockedSub);
                if (expectedLocation == null) { continue; }
                dockedSub.SetPosition(position + expectedLocation.Value, checkd, forceUndockFromStaticSubmarines);
                dockedSub.UpdateTransform(interpolate: false);
            }
        }

        public static Vector2? CalculateDockOffset(Submarine sub, Submarine dockedSub)
        {
            Item myPort = sub.ConnectedDockingPorts.ContainsKey(dockedSub) ? sub.ConnectedDockingPorts[dockedSub].Item : null;
            if (myPort == null) { return null; }
            Item theirPort = dockedSub.ConnectedDockingPorts.ContainsKey(sub) ? dockedSub.ConnectedDockingPorts[sub].Item : null;
            if (theirPort == null) { return null; }
            return (myPort.Position - sub.HiddenSubPosition) - (theirPort.Position - dockedSub.HiddenSubPosition);
        }

        public void Translate(Vector2 amount)
        {
            if (amount == Vector2.Zero || !MathUtils.IsValid(amount)) return;

            subBody.SetPosition(subBody.Position + amount);
        }

        /// <param name="teamType">If has value, the sub must match the team type.</param>
        public static Submarine FindClosest(Vector2 worldPosition, bool ignoreOutposts = false, bool ignoreOutsideLevel = true, bool ignoreRespawnShuttle = false, CharacterTeamType? teamType = null)
        {
            Submarine closest = null;
            float closestDist = 0.0f;
            foreach (Submarine sub in loaded)
            {
                if (ignoreOutposts && sub.Info.IsOutpost) { continue; }
                if (ignoreOutsideLevel && Level.Loaded != null && sub.WorldPosition.Y > Level.Loaded.Size.Y) { continue; }
                if (ignoreRespawnShuttle)
                {
                    if (sub == GameMain.NetworkMember?.RespawnManager?.RespawnShuttle) { continue; }
                }
                if (teamType.HasValue && sub.TeamID != teamType) { continue; }
                float dist = Vector2.DistanceSquared(worldPosition, sub.WorldPosition);
                if (closest == null || dist < closestDist)
                {
                    closest = sub;
                    closestDist = dist;
                }
            }

            return closest;
        }

        /// <summary>
        /// Returns true if the sub is same as the other.
        /// </summary>
        public bool IsConnectedTo(Submarine otherSub) => this == otherSub || GetConnectedSubs().Contains(otherSub);

        public List<Hull> GetHulls(bool alsoFromConnectedSubs) => GetEntities(alsoFromConnectedSubs, Hull.HullList);
        public List<Gap> GetGaps(bool alsoFromConnectedSubs) => GetEntities(alsoFromConnectedSubs, Gap.GapList);
        public List<Item> GetItems(bool alsoFromConnectedSubs) => GetEntities(alsoFromConnectedSubs, Item.ItemList);
        public List<WayPoint> GetWaypoints(bool alsoFromConnectedSubs) => GetEntities(alsoFromConnectedSubs, WayPoint.WayPointList);
        public List<Structure> GetWalls(bool alsoFromConnectedSubs) => GetEntities(alsoFromConnectedSubs, Structure.WallList);

        public List<T> GetEntities<T>(bool includingConnectedSubs, List<T> list) where T : MapEntity
        {
            return list.FindAll(e => IsEntityFoundOnThisSub(e, includingConnectedSubs));
        }

        public List<(ItemContainer container, int freeSlots)> GetCargoContainers()
        {
            List<(ItemContainer container, int freeSlots)> containers = new List<(ItemContainer container, int freeSlots)>();
            var connectedSubs = GetConnectedSubs().Where(sub => sub.Info?.Type == Info.Type);
            foreach (Item item in Item.ItemList.ToList())
            {
                if (!connectedSubs.Contains(item.Submarine)) { continue; }
                if (!item.HasTag("cargocontainer")) { continue; }
                if (item.NonInteractable || item.HiddenInGame) { continue; }
                var itemContainer = item.GetComponent<ItemContainer>();
                if (itemContainer == null) { continue; }
                int emptySlots = 0;
                for (int i = 0; i < itemContainer.Inventory.Capacity; i++)
                {
                    if (itemContainer.Inventory.GetItemAt(i) == null) { emptySlots++; }
                }
                containers.Add((itemContainer, emptySlots));
            }
            return containers;
        }

        public IEnumerable<T> GetEntities<T>(bool includingConnectedSubs, IEnumerable<T> list) where T : MapEntity
        {
            return list.Where(e => IsEntityFoundOnThisSub(e, includingConnectedSubs));
        }

        public bool IsEntityFoundOnThisSub(MapEntity entity, bool includingConnectedSubs, bool allowDifferentTeam = false, bool allowDifferentType = false)
        {
            if (entity == null) { return false; }
            if (entity.Submarine == this) { return true; }
            if (entity.Submarine == null) { return false; }
            if (includingConnectedSubs)
            {
                return GetConnectedSubs().Any(s => s == entity.Submarine && (allowDifferentTeam || entity.Submarine.TeamID == TeamID) && (allowDifferentType || entity.Submarine.Info.Type == Info.Type));
            }
            return false;
        }

        /// <summary>
        /// Finds the sub whose borders contain the position
        /// </summary>
        public static Submarine FindContaining(Vector2 position)
        {
            foreach (Submarine sub in Loaded)
            {
                Rectangle subBorders = sub.Borders;
                subBorders.Location += MathUtils.ToPoint(sub.HiddenSubPosition) - new Microsoft.Xna.Framework.Point(0, sub.Borders.Height);

                subBorders.Inflate(500.0f, 500.0f);

                if (subBorders.Contains(position)) return sub;
            }

            return null;
        }
        public static Rectangle GetBorders(XElement submarineElement)
        {
            Vector4 bounds = Vector4.Zero;
            foreach (XElement element in submarineElement.Elements())
            {
                if (element.Name != "Structure") { continue; }

                string name = element.GetAttributeString("name", "");
                Identifier identifier = element.GetAttributeIdentifier("identifier", "");
                StructurePrefab prefab = Structure.FindPrefab(name, identifier);
                if (prefab == null || !prefab.Body) { continue; }

                var rect = element.GetAttributeRect("rect", Rectangle.Empty);
                bounds = new Vector4(
                    Math.Min(rect.X, bounds.X),
                    Math.Max(rect.Y, bounds.Y),
                    Math.Max(rect.Right, bounds.Z),
                    Math.Min(rect.Y - rect.Height, bounds.W));
            }

            return new Rectangle((int)bounds.X, (int)bounds.Y, (int)(bounds.Z - bounds.X), (int)(bounds.Y - bounds.W));
        }

        public Submarine(SubmarineInfo info, bool showErrorMessages = true, Func<Submarine, List<MapEntity>> loadEntities = null, IdRemap linkedRemap = null) : base(null, Entity.NullEntityID)
        {
            upgradeEventIdentifier = new Identifier($"Submarine{ID}");
            Loading = true;
            GameMain.World.Enabled = false;
            try
            {
                loaded.Add(this);

                Info = new SubmarineInfo(info);

                ConnectedDockingPorts = new Dictionary<Submarine, DockingPort>();

                //place the sub above the top of the level
                HiddenSubPosition = HiddenSubStartPosition;
                if (GameMain.GameSession?.LevelData != null)
                {
                    HiddenSubPosition += Vector2.UnitY * GameMain.GameSession.LevelData.Size.Y;
                }

                for (int i = 0; i < loaded.Count; i++)
                {
                    Submarine sub = loaded[i];
                    HiddenSubPosition =
                        new Vector2(
                            //1st sub on the left side, 2nd on the right, etc
                            HiddenSubPosition.X * (i % 2 == 0 ? 1 : -1),
                            HiddenSubPosition.Y + sub.Borders.Height + 5000.0f);
                }

                IdOffset = IdRemap.DetermineNewOffset();

                List<MapEntity> newEntities = new List<MapEntity>();
                if (loadEntities == null)
                {
                    if (Info.SubmarineElement != null)
                    {
                        newEntities = MapEntity.LoadAll(this, Info.SubmarineElement, Info.FilePath, IdOffset);
                    }
                }
                else
                {
                    newEntities = loadEntities(this);
                    newEntities.ForEach(me => me.Submarine = this);
                }

                if (newEntities != null)
                {
                    foreach (var e in newEntities)
                    {
                        if (linkedRemap != null) { e.ResolveLinks(linkedRemap); }
                        e.unresolvedLinkedToID = null;
                    }
                }

                Vector2 center = Vector2.Zero;
                var matchingHulls = Hull.HullList.FindAll(h => h.Submarine == this);

                if (matchingHulls.Any())
                {
                    Vector2 topLeft = new Vector2(matchingHulls[0].Rect.X, matchingHulls[0].Rect.Y);
                    Vector2 bottomRight = new Vector2(matchingHulls[0].Rect.X, matchingHulls[0].Rect.Y);
                    foreach (Hull hull in matchingHulls)
                    {
                        if (hull.Rect.X < topLeft.X) topLeft.X = hull.Rect.X;
                        if (hull.Rect.Y > topLeft.Y) topLeft.Y = hull.Rect.Y;

                        if (hull.Rect.Right > bottomRight.X) bottomRight.X = hull.Rect.Right;
                        if (hull.Rect.Y - hull.Rect.Height < bottomRight.Y) bottomRight.Y = hull.Rect.Y - hull.Rect.Height;
                    }

                    center = (topLeft + bottomRight) / 2.0f;
                    center.X -= center.X % GridSize.X;
                    center.Y -= center.Y % GridSize.Y;

                    RepositionEntities(-center, MapEntity.mapEntityList.Where(me => me.Submarine == this));
                }

                subBody = new SubmarineBody(this, showErrorMessages);
                Vector2 pos = ConvertUnits.ToSimUnits(HiddenSubPosition);
                subBody.Body.FarseerBody.SetTransformIgnoreContacts(ref pos, 0.0f);

                if (info.IsOutpost)
                {
                    ShowSonarMarker = false;
                    PhysicsBody.FarseerBody.BodyType = BodyType.Static;
                    TeamID = CharacterTeamType.FriendlyNPC;

                    bool indestructible =
                        GameMain.NetworkMember != null &&
                        !GameMain.NetworkMember.ServerSettings.DestructibleOutposts &&
                        !(info.OutpostGenerationParams?.AlwaysDestructible ?? false);

                    foreach (MapEntity me in MapEntity.mapEntityList)
                    {
                        if (me.Submarine != this) { continue; }
                        if (me is Item item)
                        {
                            item.SpawnedInCurrentOutpost = info.OutpostGenerationParams != null;
                            item.AllowStealing = info.OutpostGenerationParams?.AllowStealing ?? true;
                            if (item.GetComponent<Repairable>() != null && indestructible)
                            {
                                item.Indestructible = true;
                            }
                            foreach (ItemComponent ic in item.Components)
                            {
                                if (ic is ConnectionPanel connectionPanel)
                                {
                                    //prevent rewiring
                                    if (info.OutpostGenerationParams != null && !info.OutpostGenerationParams.AlwaysRewireable)
                                    {
                                        connectionPanel.Locked = true;
                                    }
                                }
                                else if (ic is Holdable holdable && holdable.Attached && item.GetComponent<LevelResource>() == null)
                                {
                                    //prevent deattaching items from walls
#if CLIENT
                                        if (GameMain.GameSession?.GameMode is TutorialMode) { continue; }
#endif
                                    holdable.CanBePicked = false;
                                    holdable.CanBeSelected = false;
                                }
                            }
                        }
                        else if (me is Structure structure && structure.Prefab.IndestructibleInOutposts && indestructible)
                        {
                            structure.Indestructible = true;
                        }
                    }
                }
                else if (info.IsRuin)
                {
                    ShowSonarMarker = false;
                    PhysicsBody.FarseerBody.BodyType = BodyType.Static;
                }

                if (entityGrid != null)
                {
                    Hull.EntityGrids.Remove(entityGrid);
                    entityGrid = null;
                }
                entityGrid = Hull.GenerateEntityGrid(this);

                for (int i = 0; i < MapEntity.mapEntityList.Count; i++)
                {
                    if (MapEntity.mapEntityList[i].Submarine != this) { continue; }
                    MapEntity.mapEntityList[i].Move(HiddenSubPosition, ignoreContacts: true);
                }

                Loading = false;

                MapEntity.MapLoaded(newEntities, true);
                foreach (MapEntity me in MapEntity.mapEntityList)
                {
                    if (me.Submarine != this) { continue; }
                    if (me is LinkedSubmarine linkedSub)
                    {
                        linkedSub.LinkDummyToMainSubmarine();
                    }
                    else if (me is WayPoint wayPoint && wayPoint.SpawnType.HasFlag(SpawnType.ExitPoint))
                    {
                        exitPoints.Add(wayPoint);
                    }
                }

                foreach (Hull hull in matchingHulls)
                {
                    if (string.IsNullOrEmpty(hull.RoomName))// || !hull.RoomName.Contains("roomname.", StringComparison.OrdinalIgnoreCase))
                    {
                        hull.RoomName = hull.CreateRoomName();
                    }
                }

                GameMain.GameSession?.Campaign?.UpgradeManager?.OnUpgradesChanged.Register(upgradeEventIdentifier, _ => ResetCrushDepth());

#if CLIENT
                GameMain.LightManager.OnMapLoaded();
#endif
                //if the sub was made using an older version, 
                //halve the brightness of the lights to make them look (almost) right on the new lighting formula
                if (showErrorMessages &&
                    !string.IsNullOrEmpty(Info.FilePath) &&
                    Screen.Selected != GameMain.SubEditorScreen &&
                    (Info.GameVersion == null || Info.GameVersion < new Version("0.8.9.0")))
                {
                    DebugConsole.ThrowError("The submarine \"" + Info.Name + "\" was made using an older version of the Barotrauma that used a different formula to calculate the lighting. "
                        + "The game automatically adjusts the lights make them look better with the new formula, but it's recommended to open the submarine in the submarine editor and make sure everything looks right after the automatic conversion.");
                    foreach (Item item in Item.ItemList)
                    {
                        if (item.Submarine != this) continue;
                        if (item.ParentInventory != null || item.body != null) continue;
                        foreach (var light in item.GetComponents<LightComponent>())
                        {
                            light.LightColor = new Color(light.LightColor, light.LightColor.A / 255.0f * 0.5f);
                        }
                    }
                }
                GenerateOutdoorNodes();
            }
            finally
            {
                Loading = false;
                GameMain.World.Enabled = true;
            }
        }

        protected override ushort DetermineID(ushort id, Submarine submarine)
        {
            return (ushort)(ReservedIDStart - Submarine.loaded.Count);
        }

        public static Submarine Load(SubmarineInfo info, bool unloadPrevious, IdRemap linkedRemap = null)
        {
            if (unloadPrevious) { Unload(); }
            return new Submarine(info, false, linkedRemap: linkedRemap);
        }

        private void ResetCrushDepth()
        {
            realWorldCrushDepth = null;
        }

        public static void RepositionEntities(Vector2 moveAmount, IEnumerable<MapEntity> entities)
        {
            if (moveAmount.LengthSquared() < 0.00001f) { return; }
            foreach (MapEntity entity in entities)
            {
                if (entity is Item item)
                {
                    item.GetComponent<Wire>()?.MoveNodes(moveAmount);
                }
                entity.Move(moveAmount);
            }
        }

        public bool CheckFuel()
        {
            float fuel = GetItems(true).Where(i => i.HasTag("reactorfuel")).Sum(i => i.Condition);
            Info.LowFuel = fuel < 200;
            return !Info.LowFuel;
        }

        public void SaveToXElement(XElement element)
        {
            element.Add(new XAttribute("name", Info.Name));
            element.Add(new XAttribute("description", Info.Description ?? ""));
            element.Add(new XAttribute("checkval", Rand.Int(int.MaxValue)));
            element.Add(new XAttribute("price", Info.Price));
            element.Add(new XAttribute("tier", Info.Tier));
            element.Add(new XAttribute("initialsuppliesspawned", Info.InitialSuppliesSpawned));
            element.Add(new XAttribute("noitems", Info.NoItems));
            element.Add(new XAttribute("lowfuel", !CheckFuel()));
            element.Add(new XAttribute("type", Info.Type.ToString()));
            element.Add(new XAttribute("ismanuallyoutfitted", Info.IsManuallyOutfitted));
            if (Info.IsPlayer && !Info.HasTag(SubmarineTag.Shuttle))
            {
                element.Add(new XAttribute("class", Info.SubmarineClass.ToString()));
            }
            element.Add(new XAttribute("tags", Info.Tags.ToString()));
            element.Add(new XAttribute("gameversion", GameMain.Version.ToString()));

            Rectangle dimensions = VisibleBorders;
            element.Add(new XAttribute("dimensions", XMLExtensions.Vector2ToString(dimensions.Size.ToVector2())));
            var cargoContainers = GetCargoContainers();
            int cargoCapacity = cargoContainers.Sum(c => c.container.Capacity);
            foreach (MapEntity me in MapEntity.mapEntityList)
            {
                if (me is LinkedSubmarine linkedSub && linkedSub.Submarine == this)
                {
                    cargoCapacity += linkedSub.CargoCapacity;
                }
            }

            element.Add(new XAttribute("cargocapacity", cargoCapacity));
            element.Add(new XAttribute("recommendedcrewsizemin", Info.RecommendedCrewSizeMin));
            element.Add(new XAttribute("recommendedcrewsizemax", Info.RecommendedCrewSizeMax));
            element.Add(new XAttribute("recommendedcrewexperience", Info.RecommendedCrewExperience.ToString()));
            element.Add(new XAttribute("requiredcontentpackages", string.Join(", ", Info.RequiredContentPackages)));

            if (Info.Type == SubmarineType.OutpostModule)
            {
                Info.OutpostModuleInfo?.Save(element);
            }

            foreach (Item item in Item.ItemList)
            {
                if (item.PendingItemSwap?.SwappableItem?.ConnectedItemsToSwap == null) { continue; }
                foreach (var (requiredTag, swapTo) in item.PendingItemSwap.SwappableItem.ConnectedItemsToSwap)
                {
                    List<Item> itemsToSwap = new List<Item>();
                    itemsToSwap.AddRange(item.linkedTo.Where(lt => (lt as Item)?.HasTag(requiredTag) ?? false).Cast<Item>());
                    var connectionPanel = item.GetComponent<ConnectionPanel>();
                    if (connectionPanel != null)
                    {
                        foreach (Connection c in connectionPanel.Connections)
                        {
                            foreach (var connectedComponent in item.GetConnectedComponentsRecursive<ItemComponent>(c))
                            {
                                if (!itemsToSwap.Contains(connectedComponent.Item) && connectedComponent.Item.HasTag(requiredTag))
                                {
                                    itemsToSwap.Add(connectedComponent.Item);
                                }
                            }
                        }
                    }
                    ItemPrefab itemPrefab = ItemPrefab.Find("", swapTo);
                    if (itemPrefab == null) 
                    {
                        DebugConsole.ThrowError($"Failed to swap an item connected to \"{item.Name}\" into \"{swapTo}\".");
                        continue;
                    }
                    foreach (Item itemToSwap in itemsToSwap)
                    {
                        itemToSwap.PurchasedNewSwap = item.PurchasedNewSwap;
                        if (itemPrefab != itemToSwap.Prefab) { itemToSwap.PendingItemSwap = itemPrefab; }                       
                    }
                }
            }

            foreach (MapEntity e in MapEntity.mapEntityList.OrderBy(e => e.ID))
            {
                if (!e.ShouldBeSaved) { continue; }
                if (e is Item item)
                {
                    if (item.FindParentInventory(inv => inv is CharacterInventory) != null) { continue; }
#if CLIENT
                    if (Screen.Selected == GameMain.SubEditorScreen)
                    {
                        e.Submarine = this;
                    }
#endif
                    if (e.Submarine != this) { continue; }
                    var rootContainer = item.GetRootContainer();
                    if (rootContainer != null && rootContainer.Submarine != this) { continue; }
                }
                else
                {
                    if (e.Submarine != this) { continue; }
                }

                e.Save(element);
            }
            Info.CheckSubsLeftBehind(element);
        }

        public bool TrySaveAs(string filePath, System.IO.MemoryStream previewImage = null)
        {
            var newInfo = new SubmarineInfo(this)
            {
                Type = Info.Type,
                FilePath = filePath,
                OutpostModuleInfo = Info.OutpostModuleInfo != null ? new OutpostModuleInfo(Info.OutpostModuleInfo) : null,
                BeaconStationInfo = Info.BeaconStationInfo != null ? new BeaconStationInfo(Info.BeaconStationInfo) : null,
                Name = Path.GetFileNameWithoutExtension(filePath)
            };
#if CLIENT
            //remove reference to the preview image from the old info, so we don't dispose it (the new info still uses the texture)
            Info.PreviewImage = null;
#endif
            Info.Dispose();
            Info = newInfo;

            try
            {
                newInfo.SaveAs(filePath, previewImage);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Saving submarine \"{filePath}\" failed!", e);
                return false;
            }
            return true;
        }

        public static bool Unloading
        {
            get;
            private set;
        }

        public static void Unload()
        {
            if (Unloading) 
            { 
                DebugConsole.AddWarning($"Called {nameof(Submarine.Unload)} when already unloading.");
                return;
            }

            Unloading = true;
            try
            {

#if CLIENT
                RoundSound.RemoveAllRoundSounds();
                GameMain.LightManager?.ClearLights();
#endif

                var _loaded = new List<Submarine>(loaded);
                foreach (Submarine sub in _loaded)
                {
                    sub.Remove();
                }

                loaded.Clear();

                visibleEntities = null;

                if (GameMain.GameScreen.Cam != null) { GameMain.GameScreen.Cam.TargetPos = Vector2.Zero; }

                RemoveAll();

                if (Item.ItemList.Count > 0)
                {
                    List<Item> items = new List<Item>(Item.ItemList);
                    foreach (Item item in items)
                    {
                        DebugConsole.ThrowError("Error while unloading submarines - item \"" + item.Name + "\" (ID:" + item.ID + ") not removed");
                        try
                        {
                            item.Remove();
                        }
                        catch (Exception e)
                        {
                            DebugConsole.ThrowError("Error while removing \"" + item.Name + "\"!", e);
                        }
                    }
                    Item.ItemList.Clear();
                }

                Ragdoll.RemoveAll();
                PhysicsBody.RemoveAll();
                GameMain.World = null;

                Powered.Grids.Clear();

                GC.Collect();

            }
            finally
            {
                Unloading = false;
            }
        }

        public override void Remove()
        {
            base.Remove();

            subBody?.Remove();
            subBody = null;

            outdoorNodes?.Clear();
            outdoorNodes = null;

            GameMain.GameSession?.Campaign?.UpgradeManager?.OnUpgradesChanged?.TryDeregister(upgradeEventIdentifier);

            if (entityGrid != null)
            {
                Hull.EntityGrids.Remove(entityGrid);
                entityGrid = null;
            }

            visibleEntities = null;

            if (MainSub == this) { MainSub = null; }
            if (MainSubs[1] == this) { MainSubs[1] = null; }

            ConnectedDockingPorts?.Clear();

            loaded.Remove(this);
        }

        public void Dispose()
        {
            Remove();
        }

        private List<PathNode> outdoorNodes;
        private List<PathNode> OutdoorNodes
        {
            get
            {
                if (outdoorNodes == null)
                {
                    GenerateOutdoorNodes();
                }
                return outdoorNodes;
            }
        }

        private void GenerateOutdoorNodes()
        {
            var waypoints = WayPoint.WayPointList.FindAll(wp => wp.SpawnType == SpawnType.Path && wp.Submarine == this && wp.CurrentHull == null);
            outdoorNodes = PathNode.GenerateNodes(waypoints, removeOrphans: false);
        }

        private readonly Dictionary<Submarine, HashSet<PathNode>> obstructedNodes = new Dictionary<Submarine, HashSet<PathNode>>();

        /// <summary>
        /// Permanently disables obstructed waypoints obstructed by the level.
        /// </summary>
        public void DisableObstructedWayPoints()
        {
            // Check collisions to level
            foreach (var node in OutdoorNodes)
            {
                if (node == null || node.Waypoint == null) { continue; }
                var wp = node.Waypoint;
                if (wp.IsObstructed) { continue; }
                foreach (var connection in node.connections)
                {
                    var connectedWp = connection.Waypoint;
                    if (connectedWp.IsObstructed) { continue; }
                    Vector2 start = ConvertUnits.ToSimUnits(wp.WorldPosition);
                    Vector2 end = ConvertUnits.ToSimUnits(connectedWp.WorldPosition);
                    var body = PickBody(start, end, null, Physics.CollisionLevel, allowInsideFixture: false);
                    if (body != null)
                    {
                        connectedWp.IsObstructed = true;
                        wp.IsObstructed = true;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Temporarily disables waypoints obstructed by the other sub.
        /// </summary>
        public void DisableObstructedWayPoints(Submarine otherSub)
        {
            if (otherSub == null) { return; }
            if (otherSub == this) { return; }
            // Check collisions to other subs.
            foreach (var node in OutdoorNodes)
            {
                if (node == null || node.Waypoint == null) { continue; }
                var wp = node.Waypoint;
                if (wp.IsObstructed) { continue; }
                foreach (var connection in node.connections)
                {
                    var connectedWp = connection.Waypoint;
                    if (connectedWp.IsObstructed || connectedWp.Ladders != null) { continue; }
                    Vector2 start = ConvertUnits.ToSimUnits(wp.WorldPosition) - otherSub.SimPosition;
                    Vector2 end = ConvertUnits.ToSimUnits(connectedWp.WorldPosition) - otherSub.SimPosition;
                    var body = PickBody(start, end, null, Physics.CollisionWall, allowInsideFixture: true);
                    if (body != null)
                    {
                        if (body.UserData is Structure wall && !wall.IsPlatform || body.UserData is Item && body.FixtureList[0].CollisionCategories.HasFlag(Physics.CollisionWall))
                        {
                            connectedWp.IsObstructed = true;
                            wp.IsObstructed = true;
                            if (!obstructedNodes.TryGetValue(otherSub, out HashSet<PathNode> nodes))
                            {
                                nodes = new HashSet<PathNode>();
                                obstructedNodes.Add(otherSub, nodes);
                            }
                            nodes.Add(node);
                            nodes.Add(connection);
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Only affects temporarily disabled waypoints.
        /// </summary>
        public void EnableObstructedWaypoints(Submarine otherSub)
        {
            if (obstructedNodes.TryGetValue(otherSub, out HashSet<PathNode> nodes))
            {
                nodes.ForEach(n => n.Waypoint.IsObstructed = false);
                nodes.Clear();
                obstructedNodes.Remove(otherSub);
            }
        }

        public void RefreshOutdoorNodes() => OutdoorNodes.ForEach(n => n?.Waypoint?.FindHull());

        public Item FindContainerFor(Item item, bool onlyPrimary, bool checkTransferConditions = false, bool allowConnectedSubs = false)
        {
            var connectedSubs = GetConnectedSubs().Where(s => s.Info.Type == SubmarineType.Player).ToHashSet();
            Item selectedContainer = null;
            foreach (Item potentialContainer in Item.ItemList)
            {
                if (potentialContainer.Removed) { continue; }
                if (potentialContainer.NonInteractable) { continue; }
                if (potentialContainer.HiddenInGame) { continue; }
                if (allowConnectedSubs)
                {
                    if (!connectedSubs.Contains(potentialContainer.Submarine)) { continue; }
                }
                else
                {
                    if (potentialContainer.Submarine != this) { continue; }
                }
                if (potentialContainer == item) { continue; }
                if (potentialContainer.Condition <= 0) { continue; }
                if (potentialContainer.OwnInventory == null) { continue; }
                if (potentialContainer.GetRootInventoryOwner() != potentialContainer) { continue; }
                var container = potentialContainer.GetComponent<ItemContainer>();
                if (container == null) { continue; }
                if (!potentialContainer.OwnInventory.CanBePut(item)) { continue; }
                if (!container.ShouldBeContained(item, out _)) { continue; }
                if (!item.Prefab.IsContainerPreferred(item, container, out bool isPreferencesDefined, out bool isSecondary, checkTransferConditions: checkTransferConditions) || !isPreferencesDefined || onlyPrimary && isSecondary) { continue; }
                if (potentialContainer.Submarine == this && !isSecondary)
                {
                    //valid primary container in the same sub -> perfect, let's use that one
                    return potentialContainer;               
                }
                selectedContainer = potentialContainer;
                
            }
            return selectedContainer;
        }
    }
}
