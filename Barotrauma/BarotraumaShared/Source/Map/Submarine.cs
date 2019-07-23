using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.RuinGeneration;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Voronoi2;

namespace Barotrauma
{
    public enum Direction : byte
    {
        None = 0, Left = 1, Right = 2
    }

    [Flags]
    public enum SubmarineTag
    {
        [Description("Shuttle")]
        Shuttle = 1,
        [Description("Hide in menus")]
        HideInMenus = 2
    }

    partial class Submarine : Entity, IServerSerializable
    {
        public Character.TeamType TeamID = Character.TeamType.None;

        public static string SavePath = "Submarines";

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

        private static List<Submarine> savedSubmarines = new List<Submarine>();
        public static IEnumerable<Submarine> SavedSubmarines
        {
            get { return savedSubmarines; }
        }

        public static readonly Vector2 GridSize = new Vector2(16.0f, 16.0f);

        public static readonly Submarine[] MainSubs = new Submarine[2];
        public static Submarine MainSub
        {
            get { return MainSubs[0]; }
            set { MainSubs[0] = value; }
        }
        private static List<Submarine> loaded = new List<Submarine>();

        private static List<MapEntity> visibleEntities;
        public static IEnumerable<MapEntity> VisibleEntities
        {
            get { return visibleEntities; }
        }

        private SubmarineBody subBody;

        public readonly List<Submarine> DockedTo;

        private static Vector2 lastPickedPosition;
        private static float lastPickedFraction;
        private static Vector2 lastPickedNormal;

        private Md5Hash hash;
        
        private string filePath;
        private string name;

        private SubmarineTag tags;

        private Vector2 prevPosition;

        private float networkUpdateTimer;

        private EntityGrid entityGrid = null;

        public int RecommendedCrewSizeMin = 1, RecommendedCrewSizeMax = 2;
        public string RecommendedCrewExperience;

        public HashSet<string> RequiredContentPackages = new HashSet<string>();
        
        //properties ----------------------------------------------------

        public string Name
        {
            get { return name; }
            set { name = value; }
        }

        private string displayName;
        public string DisplayName
        {
            get { return displayName; }
        }

        public bool ShowSonarMarker = true;

        public string Description
        {
            get; 
            set; 
        }

        public Version GameVersion
        {
            get;
            private set;
        }

        public bool IsOutpost
        {
            get;
            private set;
        }
        
        public static Vector2 LastPickedPosition
        {
            get { return lastPickedPosition; }
        }

        public static float LastPickedFraction
        {
            get { return lastPickedFraction; }
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

        public Md5Hash MD5Hash
        {
            get
            {
                if (hash != null) return hash;

                XDocument doc = OpenFile(filePath);
                hash = new Md5Hash(doc);

                return hash;
            }
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
            get { return subBody.Body; }
        }

        public Rectangle Borders
        {
            get 
            {
                return subBody == null ? Rectangle.Empty : subBody.Borders;
            }
        }

        public Vector2 Dimensions
        {
            get;
            private set;
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

        public bool AtEndPosition
        {
            get 
            {
                if (Level.Loaded == null) { return false; }
                if (Level.Loaded.EndOutpost != null && DockedTo.Contains(Level.Loaded.EndOutpost))
                {
                    return true;
                }
                return (Vector2.DistanceSquared(Position + HiddenSubPosition, Level.Loaded.EndPosition) < Level.ExitDistance * Level.ExitDistance);
            }
        }

        public bool AtStartPosition
        {
            get
            {
                if (Level.Loaded == null) { return false; }
                if (Level.Loaded.StartOutpost != null && DockedTo.Contains(Level.Loaded.StartOutpost))
                {
                    return true;
                }
                return (Vector2.DistanceSquared(Position + HiddenSubPosition, Level.Loaded.StartPosition) < Level.ExitDistance * Level.ExitDistance);
            }
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
                if (subBody == null) return;
                subBody.Velocity = value;
            }
        }

        public List<Vector2> HullVertices
        {
            get { return subBody.HullVertices; }
        }


        public string FilePath
        {
            get { return filePath; }
            set { filePath = value; }
        }

        public bool AtDamageDepth
        {
            get { return subBody != null && subBody.AtDamageDepth; }
        }

        public override string ToString()
        {
            return "Barotrauma.Submarine (" + name + ")";
        }

        public override bool Removed
        {
            get
            {
                return !loaded.Contains(this);
            }
        }

        public bool IsFileCorrupted
        {
            get;
            private set;
        }

        //constructors & generation ----------------------------------------------------

        public Submarine(string filePath, string hash = "", bool tryLoad = true) : base(null)
        {
            this.filePath = filePath;
            try
            {
                name = displayName = Path.GetFileNameWithoutExtension(filePath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error loading submarine " + filePath + "!", e);
            }

            if (hash != "")
            {
                this.hash = new Md5Hash(hash);
            }

            if (tryLoad)
            {
                XDocument doc = null;
                int maxLoadRetries = 4;
                for (int i = 0; i <= maxLoadRetries; i++)
                {
                    doc = OpenFile(filePath, out Exception e);
                    if (e != null && !(e is IOException)) { break; }
                    if (doc != null || i == maxLoadRetries || !File.Exists(filePath)) { break; }
                    DebugConsole.NewMessage("Opening submarine file \"" + filePath + "\" failed, retrying in 250 ms...");
                    Thread.Sleep(250);
                }
                if (doc == null || doc.Root == null)
                {
                    IsFileCorrupted = true;
                    return;
                }

                if (doc != null && doc.Root != null)
                {
                    displayName = TextManager.Get("Submarine.Name." + name, true);
                    if (displayName == null || displayName.Length == 0) displayName = name;

                    Description = TextManager.Get("Submarine.Description." + name, true);
                    if (Description == null || Description.Length == 0) Description = doc.Root.GetAttributeString("description", "");

                    GameVersion = new Version(doc.Root.GetAttributeString("gameversion", "0.0.0.0"));
                    Enum.TryParse(doc.Root.GetAttributeString("tags", ""), out tags);
                    Dimensions = doc.Root.GetAttributeVector2("dimensions", Vector2.Zero);
                    RecommendedCrewSizeMin = doc.Root.GetAttributeInt("recommendedcrewsizemin", 0);
                    RecommendedCrewSizeMax = doc.Root.GetAttributeInt("recommendedcrewsizemax", 0);
                    RecommendedCrewExperience = doc.Root.GetAttributeString("recommendedcrewexperience", "Unknown");

                    //backwards compatibility (use text tags instead of the actual text)
                    if (RecommendedCrewExperience == "Beginner")
                        RecommendedCrewExperience = "CrewExperienceLow";
                    else if (RecommendedCrewExperience == "Intermediate")
                        RecommendedCrewExperience = "CrewExperienceMid";
                    else if (RecommendedCrewExperience == "Experienced")
                        RecommendedCrewExperience = "CrewExperienceHigh";
                    
                    string[] contentPackageNames = doc.Root.GetAttributeStringArray("requiredcontentpackages", new string[0]);
                    foreach (string contentPackageName in contentPackageNames)
                    {
                        RequiredContentPackages.Add(contentPackageName);
                    }
#if CLIENT                    
                    string previewImageData = doc.Root.GetAttributeString("previewimage", "");
                    if (!string.IsNullOrEmpty(previewImageData))
                    {
                        try
                        {
                            using (MemoryStream mem = new MemoryStream(Convert.FromBase64String(previewImageData)))
                            {
                                var texture = TextureLoader.FromStream(mem, preMultiplyAlpha: false, path: filePath);
                                if (texture == null) { throw new Exception("PreviewImage texture returned null"); }
                                PreviewImage = new Sprite(texture, null, null);
                            }
                        }
                        catch (Exception e)
                        {
                            DebugConsole.ThrowError("Loading the preview image of the submarine \"" + Name + "\" failed. The file may be corrupted.", e);
                            GameAnalyticsManager.AddErrorEventOnce("Submarine..ctor:PreviewImageLoadingFailed", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, 
                                "Loading the preview image of the submarine \"" + Name + "\" failed. The file may be corrupted.");
                            PreviewImage = null;
                        }
                    }
#endif
                }
            }

            DockedTo = new List<Submarine>();

            FreeID();
        }

        public bool HasTag(SubmarineTag tag)
        {
            return tags.HasFlag(tag);
        }

        public void AddTag(SubmarineTag tag)
        {
            if (tags.HasFlag(tag)) return;

            tags |= tag;
        }

        public void RemoveTag(SubmarineTag tag)
        {
            if (!tags.HasFlag(tag)) return;

            tags &= ~tag;
        }

        public void MakeOutpost()
        {
            IsOutpost = true;
            ShowSonarMarker = false;
            PhysicsBody.FarseerBody.IsStatic = true;
            TeamID = Character.TeamType.FriendlyNPC;

            foreach (MapEntity me in MapEntity.mapEntityList)
            {
                if (me.Submarine != this) { continue; }
                if (me is Item item)
                {
                    if (item.GetComponent<Repairable>() != null)
                    {
                        item.Indestructible = true;
                    }
                    foreach (ItemComponent ic in item.Components)
                    {
                        if (ic is ConnectionPanel connectionPanel)
                        {
                            //prevent rewiring
                            connectionPanel.Locked = true;
                        }
                        else if (ic is Holdable holdable && holdable.Attached)
                        {
                            //prevent deattaching items from walls
#if CLIENT
                            if (GameMain.GameSession?.GameMode is TutorialMode)
                            {
                                continue;
                            }
#endif
                            holdable.CanBePicked = false;
                            holdable.CanBeSelected = false;
                        }
                    }
                }
                else if (me is Structure structure)
                {
                    structure.Indestructible = true;
                }
            }
        }

        /// <summary>
        /// Returns a rect that contains the borders of this sub and all subs docked to it
        /// </summary>
        public Rectangle GetDockedBorders()
        {
            Rectangle dockedBorders = Borders;
            dockedBorders.Y -= dockedBorders.Height;

            var connectedSubs = GetConnectedSubs();

            foreach (Submarine dockedSub in connectedSubs)
            {
                if (dockedSub == this) continue;

                Vector2 diff = dockedSub.Submarine == this ? dockedSub.WorldPosition : dockedSub.WorldPosition - WorldPosition;

                Rectangle dockedSubBorders = dockedSub.Borders;
                dockedSubBorders.Y -= dockedSubBorders.Height;
                dockedSubBorders.Location += MathUtils.ToPoint(diff);

                dockedBorders = Rectangle.Union(dockedBorders, dockedSubBorders);
            }

            dockedBorders.Y += dockedBorders.Height;
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

        public Vector2 FindSpawnPos(Vector2 spawnPos, Point? submarineSize = null, float subDockingPortOffset = 0.0f)
        {
            Rectangle dockedBorders = GetDockedBorders();
            Vector2 diffFromDockedBorders = 
                new Vector2(dockedBorders.Center.X, dockedBorders.Y - dockedBorders.Height / 2)
                - new Vector2(Borders.Center.X, Borders.Y - Borders.Height / 2);

            int minWidth = Math.Max(submarineSize.HasValue ? submarineSize.Value.X : dockedBorders.Width, 500);
            int minHeight = Math.Max(submarineSize.HasValue ? submarineSize.Value.Y : dockedBorders.Height, 1000);
            //a bit of extra padding to prevent the sub from spawning in a super tight gap between walls
            minHeight += 500;

            float minX = float.MinValue, maxX = float.MaxValue;
            foreach (VoronoiCell cell in Level.Loaded.GetAllCells())
            {
                if (cell.Edges.All(e => e.Point1.Y < Level.Loaded.Size.Y - minHeight && e.Point2.Y < Level.Loaded.Size.Y - minHeight)) { continue; }

                //find the closest wall at the left and right side of the spawnpos
                if (cell.Site.Coord.X < spawnPos.X)
                {
                    minX = Math.Max(minX, cell.Edges.Max(e => Math.Max(e.Point1.X, e.Point2.X)));
                }
                else
                {
                    maxX = Math.Min(maxX, cell.Edges.Min(e => Math.Min(e.Point1.X, e.Point2.X)));
                }
            }

            foreach (var ruin in Level.Loaded.Ruins)
            {
                if (ruin.Area.Y + ruin.Area.Height < Level.Loaded.Size.Y - minHeight) { continue; }
                if (ruin.Area.X < spawnPos.X)
                {
                    minX = Math.Max(minX, ruin.Area.Right + 100.0f);
                }
                else
                {
                    maxX = Math.Min(maxX, ruin.Area.X - 100.0f);
                }
            }
            
            if (minX < 0.0f && maxX > Level.Loaded.Size.X)
            {
                //no walls found at either side, just use the initial spawnpos and hope for the best
            }
            else if (minX < 0)
            {
                //no wall found at the left side, spawn to the left from the right-side wall
                spawnPos.X = maxX - minWidth - 100.0f + subDockingPortOffset;
            }
            else if (maxX > Level.Loaded.Size.X)
            {
                //no wall found at right side, spawn to the right from the left-side wall
                spawnPos.X = minX + minWidth + 100.0f + subDockingPortOffset;
            }
            else
            {
                //walls found at both sides, use their midpoint
                spawnPos.X = (minX + maxX) / 2 + subDockingPortOffset;
            }
            
            spawnPos.Y = Math.Min(spawnPos.Y, Level.Loaded.Size.Y - dockedBorders.Height / 2 - 10);
            return spawnPos - diffFromDockedBorders;
        }

        public void UpdateTransform()
        {
            DrawPosition = Timing.Interpolate(prevPosition, Position);
        }

        //math/physics stuff ----------------------------------------------------

        public static Vector2 MouseToWorldGrid(Camera cam, Submarine sub)
        {
            Vector2 position = PlayerInput.MousePosition;
            position = cam.ScreenToWorld(position);

            Vector2 worldGridPos = VectorToWorldGrid(position);

            if (sub != null)
            {
                worldGridPos.X += sub.Position.X % GridSize.X;
                worldGridPos.Y += sub.Position.Y % GridSize.Y;
            }

            return worldGridPos;
        }

        public static Vector2 VectorToWorldGrid(Vector2 position)
        {
            position.X = (float)Math.Floor(position.X / GridSize.X) * GridSize.X;
            position.Y = (float)Math.Ceiling(position.Y / GridSize.Y) * GridSize.Y;

            return position;
        }

        public Rectangle CalculateDimensions(bool onlyHulls = true)
        {
            List<MapEntity> entities = onlyHulls ?
                Hull.hullList.FindAll(h => h.Submarine == this).Cast<MapEntity>().ToList() :
                MapEntity.mapEntityList.FindAll(me => me.Submarine == this);

            //ignore items whose body is disabled (wires, items inside cabinets)
            entities.RemoveAll(e =>
            {
                if (e is Item item)
                {
                    if (item.body != null && !item.body.Enabled) { return true; }
                }
                return false;
            });

            if (entities.Count == 0) return Rectangle.Empty;

            float minX = entities[0].Rect.X, minY = entities[0].Rect.Y - entities[0].Rect.Height;
            float maxX = entities[0].Rect.Right, maxY = entities[0].Rect.Y;

            for (int i = 1; i < entities.Count; i++)
            {
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
            if (Vector2.DistanceSquared(rayStart, rayEnd) < 0.00001f)
            {
                rayEnd += Vector2.UnitX * 0.001f;
            }

            float closestFraction = 1.0f;
            Vector2 closestNormal = Vector2.Zero;
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
                    if (fixture.Body != null) closestBody = fixture.Body;                    
                    return false;
                }, ref aabb);
                if (closestFraction <= 0.0f)
                {
                    lastPickedPosition = rayStart;
                    lastPickedFraction = closestFraction;
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
                    if (fixture.Body != null) closestBody = fixture.Body;
                }
                return fraction;
            }
            , rayStart, rayEnd);

            lastPickedPosition = rayStart + (rayEnd - rayStart) * closestFraction;
            lastPickedFraction = closestFraction;
            lastPickedNormal = closestNormal;
            
            return closestBody;
        }

        private static readonly Dictionary<Body, float> bodyDist = new Dictionary<Body, float>();
        private static readonly List<Body> bodies = new List<Body>();

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
                }
                //continue
                return -1;
            }, rayStart, rayEnd);

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
        public static Body CheckVisibility(Vector2 rayStart, Vector2 rayEnd, bool ignoreLevel = false, bool ignoreSubs = false, bool ignoreSensors = true)
        {
            Body closestBody = null;
            float closestFraction = 1.0f;
            Vector2 closestNormal = Vector2.Zero;

            if (Vector2.Distance(rayStart, rayEnd) < 0.01f)
            {
                lastPickedPosition = rayEnd;
                return null;
            }
            
            GameMain.World.RayCast((fixture, point, normal, fraction) =>
            {
                if (fixture == null ||
                    (ignoreSensors && fixture.IsSensor) ||
                    (!fixture.CollisionCategories.HasFlag(Physics.CollisionWall) && !fixture.CollisionCategories.HasFlag(Physics.CollisionLevel))) return -1;

                if (ignoreLevel && fixture.CollisionCategories == Physics.CollisionLevel) return -1;
                if (ignoreSubs && fixture.Body.UserData is Submarine) return -1;

                if (fixture.Body.UserData is Structure structure)
                {
                    if (structure.IsPlatform || structure.StairDirection != Direction.None) return -1;
                    int sectionIndex = structure.FindSectionIndex(ConvertUnits.ToDisplayUnits(point));
                    if (sectionIndex > -1 && structure.SectionBodyDisabled(sectionIndex)) return -1;
                }

                if (fraction < closestFraction)
                {
                    closestBody = fixture.Body;
                    closestFraction = fraction;
                    closestNormal = normal;
                }
                return closestFraction;
            }
            , rayStart, rayEnd);


            lastPickedPosition = rayStart + (rayEnd - rayStart) * closestFraction;
            lastPickedFraction = closestFraction;
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
            if (parents == null) parents = new List<Submarine>();
            parents.Add(this);

            flippedX = !flippedX;

            Item.UpdateHulls();

            List<Item> bodyItems = Item.ItemList.FindAll(it => it.Submarine == this && it.body != null);

            List<MapEntity> subEntities = MapEntity.mapEntityList.FindAll(me => me.Submarine == this);

            foreach (MapEntity e in subEntities)
            {
                if (e is Item) continue;
                if (e is LinkedSubmarine)
                {
                    Submarine sub = ((LinkedSubmarine)e).Sub;
                    if (!parents.Contains(sub))
                    {
                        Vector2 relative1 = sub.SubBody.Position - SubBody.Position;
                        relative1.X = -relative1.X;
                        sub.SetPosition(relative1 + SubBody.Position);
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

            Vector2 pos = new Vector2(subBody.Position.X, subBody.Position.Y);
            subBody.Body.Remove();
            subBody = new SubmarineBody(this);
            SetPosition(pos);

            if (entityGrid != null)
            {
                Hull.EntityGrids.Remove(entityGrid);
                entityGrid = null;
            }
            entityGrid = Hull.GenerateEntityGrid(this);

            foreach (MapEntity mapEntity in subEntities)
            {
                mapEntity.Move(HiddenSubPosition);
            }

            foreach (Item item in Item.ItemList)
            {
                if (bodyItems.Contains(item))
                {
                    item.Submarine = this;
                    if (Position == Vector2.Zero) item.Move(-HiddenSubPosition);
                }
                else if (item.Submarine != this)
                {
                    continue;
                }

                item.FlipX(true);
            }

            Item.UpdateHulls();
            Gap.UpdateHulls();
        }

        public void Update(float deltaTime)
        {
            //if (PlayerInput.KeyHit(InputType.Crouch) && (this == MainSub)) FlipX();

            if (Level.Loaded == null || subBody == null) return;

            if (WorldPosition.Y < Level.MaxEntityDepth &&
                subBody.Body.Enabled &&
                (GameMain.NetworkMember?.RespawnManager == null || this != GameMain.NetworkMember.RespawnManager.RespawnShuttle))
            {
                subBody.Body.ResetDynamics();
                subBody.Body.Enabled = false;

                foreach (MapEntity e in MapEntity.mapEntityList)
                {
                    if (e.Submarine == this)
                    {
                        Spawner.AddToRemoveQueue(e);
                    }
                }

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
                if (MainSubs[i] == null) continue;
                if (this != MainSubs[i] && MainSubs[i].DockedTo.Contains(this)) return;
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

        public void SetPrevTransform(Vector2 position)
        {
            prevPosition = position;
        }

        public void SetPosition(Vector2 position)
        {
            if (!MathUtils.IsValid(position)) return;
            
            subBody.SetPosition(position);

            foreach (Submarine sub in loaded)
            {
                if (sub != this && sub.Submarine == this)
                {
                    sub.SetPosition(position + sub.WorldPosition);
                    sub.Submarine = null;
                }

            }
            //Level.Loaded.SetPosition(-position);
            //prevPosition = position;
        }

        public void Translate(Vector2 amount)
        {
            if (amount == Vector2.Zero || !MathUtils.IsValid(amount)) return;

            subBody.SetPosition(subBody.Position + amount);

            //Level.Loaded.Move(-amount);
        }

        public static Submarine FindClosest(Vector2 worldPosition, bool ignoreOutposts = false)
        {
            Submarine closest = null;
            float closestDist = 0.0f;
            foreach (Submarine sub in loaded)
            {
                if (ignoreOutposts && sub.IsOutpost)
                {
                    continue;
                }
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

        public List<Hull> GetHulls(bool alsoFromConnectedSubs) => GetEntities(alsoFromConnectedSubs, Hull.hullList);
        public List<Gap> GetGaps(bool alsoFromConnectedSubs) => GetEntities(alsoFromConnectedSubs, Gap.GapList);
        public List<Item> GetItems(bool alsoFromConnectedSubs) => GetEntities(alsoFromConnectedSubs, Item.ItemList);

        public List<T> GetEntities<T>(bool includingConnectedSubs, List<T> list) where T : MapEntity
        {
            return list.FindAll(e => IsEntityFoundOnThisSub(e, includingConnectedSubs));
        }

        public bool IsEntityFoundOnThisSub(MapEntity entity, bool includingConnectedSubs)
        {
            if (entity == null) { return false; }
            if (entity.Submarine == this) { return true; }
            if (entity.Submarine == null) { return false; }
            if (includingConnectedSubs)
            {
                return GetConnectedSubs().Any(s => s == entity.Submarine && entity.Submarine.TeamID == TeamID);
            }
            return false;
        }

        /// <summary>
        /// Finds the sub whose borders contain the position
        /// </summary>
        public static Submarine FindContaining(Vector2 position)
        {
            foreach (Submarine sub in Submarine.Loaded)
            {
                Rectangle subBorders = sub.Borders;
                subBorders.Location += MathUtils.ToPoint(sub.HiddenSubPosition) - new Microsoft.Xna.Framework.Point(0, sub.Borders.Height);

                subBorders.Inflate(500.0f, 500.0f);

                if (subBorders.Contains(position)) return sub;
            }

            return null;
        }

        //saving/loading ----------------------------------------------------

        public static void AddToSavedSubs(Submarine sub)
        {
            savedSubmarines.Add(sub);
        }


        public static void RefreshSavedSub(string filePath)
        {
            string fullPath = Path.GetFullPath(filePath);
            for (int i = savedSubmarines.Count - 1; i >= 0; i--)
            {
                if (Path.GetFullPath(savedSubmarines[i].filePath) == fullPath)
                {
                    savedSubmarines[i].Dispose();
                }
            }
            var sub = new Submarine(filePath);
            if (!sub.IsFileCorrupted)
            {
                savedSubmarines.Add(sub);
            }
            savedSubmarines = savedSubmarines.OrderBy(s => s.filePath ?? "").ToList();
        }

        public static void RefreshSavedSubs()
        {
            for (int i = savedSubmarines.Count - 1; i>= 0; i--)
            {
                savedSubmarines[i].Dispose();
            }
            System.Diagnostics.Debug.Assert(savedSubmarines.Count == 0);

            if (!Directory.Exists(SavePath))
            {
                try
                {
                    Directory.CreateDirectory(SavePath);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Directory \"" + SavePath + "\" not found and creating the directory failed.", e);
                    return;
                }
            }

            List<string> filePaths;
            string[] subDirectories;

            try
            {
                filePaths = Directory.GetFiles(SavePath).ToList();
                subDirectories = Directory.GetDirectories(SavePath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Couldn't open directory \"" + SavePath + "\"!", e);
                return;
            }

            foreach (string subDirectory in subDirectories)
            {
                try
                {
                    filePaths.AddRange(Directory.GetFiles(subDirectory).ToList());
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Couldn't open subdirectory \"" + subDirectory + "\"!", e);
                    return;
                }
            }

            foreach (string path in filePaths)
            {
                var sub = new Submarine(path);
                if (!sub.IsFileCorrupted)
                {
                    savedSubmarines.Add(sub);
                }
            }
        }

        static readonly string TempFolder = Path.Combine("Submarine", "Temp");

        public static XDocument OpenFile(string file)
        {
            return OpenFile(file, out _);
        }

        public static XDocument OpenFile(string file, out Exception exception)
        {
            XDocument doc = null;
            string extension = "";
            exception = null;

            try
            {
                extension = System.IO.Path.GetExtension(file);
            }
            catch
            {
                //no file extension specified: try using the default one
                file += ".sub";
            }

            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".sub";
                file += ".sub";
            }

            if (extension == ".sub")
            {
                Stream stream = null;
                try
                {
                    stream = SaveUtil.DecompressFiletoStream(file);
                }
                catch (Exception e) 
                {
                    exception = e;
                    DebugConsole.ThrowError("Loading submarine \"" + file + "\" failed!", e);
                    return null;
                }                

                try
                {
                    stream.Position = 0;
                    doc = XDocument.Load(stream); //ToolBox.TryLoadXml(file);
                    stream.Close();
                    stream.Dispose();
                }

                catch (Exception e)
                {
                    exception = e;
                    DebugConsole.ThrowError("Loading submarine \"" + file + "\" failed! (" + e.Message + ")");
                    return null;
                }
            }
            else if (extension == ".xml")
            {
                try
                {
                    ToolBox.IsProperFilenameCase(file);
                    doc = XDocument.Load(file, LoadOptions.SetBaseUri);
                }

                catch (Exception e)
                {
                    exception = e;
                    DebugConsole.ThrowError("Loading submarine \"" + file + "\" failed! (" + e.Message + ")");
                    return null;
                }
            }
            else
            {
                DebugConsole.ThrowError("Couldn't load submarine \"" + file + "! (Unrecognized file extension)");
                return null;
            }

            return doc;
        }

        public void Load(bool unloadPrevious, XElement submarineElement = null, bool showWarningMessages = true)
        {
            if (unloadPrevious) Unload();

            Loading = true;

            if (submarineElement == null)
            {
                XDocument doc = null;
                int maxLoadRetries = 4;
                for (int i = 0; i <= maxLoadRetries; i++)
                {
                    doc = OpenFile(filePath);
                    if (doc != null || i == maxLoadRetries || !File.Exists(filePath)) { break; }
                    DebugConsole.NewMessage("Loading the submarine \"" + Name + "\" failed, retrying in 250 ms...");
                    Thread.Sleep(250);
                }
                if (doc == null || doc.Root == null) { return; }
                submarineElement = doc.Root;
            }

            GameVersion = GameVersion ?? new Version(submarineElement.GetAttributeString("gameversion", "0.0.0.0"));
            Description = submarineElement.GetAttributeString("description", "");
            Enum.TryParse(submarineElement.GetAttributeString("tags", ""), out tags);
            
            //place the sub above the top of the level
            HiddenSubPosition = HiddenSubStartPosition;
            if (GameMain.GameSession != null && GameMain.GameSession.Level != null)
            {
                HiddenSubPosition += Vector2.UnitY * GameMain.GameSession.Level.Size.Y;
            }

            foreach (Submarine sub in Submarine.loaded)
            {
                HiddenSubPosition += Vector2.UnitY * (sub.Borders.Height + 5000.0f);
            }

            IdOffset = 0;
            foreach (MapEntity me in MapEntity.mapEntityList)
            {
                IdOffset = Math.Max(IdOffset, me.ID);
            }
            
            var newEntities = MapEntity.LoadAll(this, submarineElement, filePath);

            Vector2 center = Vector2.Zero;
            var matchingHulls = Hull.hullList.FindAll(h => h.Submarine == this);

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

                if (center != Vector2.Zero)
                {
                    foreach (Item item in Item.ItemList)
                    {
                        if (item.Submarine != this) continue;

                        var wire = item.GetComponent<Items.Components.Wire>();
                        if (wire != null)
                        {
                            wire.MoveNodes(-center);
                        }
                    }

                    for (int i = 0; i < MapEntity.mapEntityList.Count; i++)
                    {
                        if (MapEntity.mapEntityList[i].Submarine != this) continue;

                        MapEntity.mapEntityList[i].Move(-center);
                    }
                }
            }

            subBody = new SubmarineBody(this, showWarningMessages);
            subBody.SetPosition(HiddenSubPosition);

            loaded.Add(this);

            if (entityGrid != null)
            {
                Hull.EntityGrids.Remove(entityGrid);
                entityGrid = null;
            }
            entityGrid = Hull.GenerateEntityGrid(this);

            for (int i = 0; i < MapEntity.mapEntityList.Count; i++)
            {
                if (MapEntity.mapEntityList[i].Submarine != this) continue;
                MapEntity.mapEntityList[i].Move(HiddenSubPosition);
            }

            Loading = false;

            MapEntity.MapLoaded(newEntities, true);

            foreach (Hull hull in matchingHulls)
            {
                if (string.IsNullOrEmpty(hull.RoomName) || !hull.RoomName.ToLowerInvariant().Contains("roomname."))
                {
                    hull.RoomName = hull.CreateRoomName();
                }
            }

#if CLIENT
            GameMain.LightManager.OnMapLoaded();
#endif
            //if the sub was made using an older version, 
            //halve the brightness of the lights to make them look (almost) right on the new lighting formula
            if (showWarningMessages && Screen.Selected != GameMain.SubEditorScreen && (GameVersion == null || GameVersion < new Version("0.8.9.0")))
            {
                DebugConsole.ThrowError("The submarine \"" + Name + "\" was made using an older version of the Barotrauma that used a different formula to calculate the lighting. "
                    + "The game automatically adjusts the lights make them look better with the new formula, but it's recommended to open the submarine in the submarine editor and make sure everything looks right after the automatic conversion.");
                foreach (Item item in Item.ItemList)
                {
                    if (item.Submarine != this) continue;
                    if (item.ParentInventory != null || item.body != null) continue;
                    var lightComponent = item.GetComponent<Items.Components.LightComponent>();
                    if (lightComponent != null) lightComponent.LightColor = new Color(lightComponent.LightColor, lightComponent.LightColor.A / 255.0f * 0.5f);
                }
            }


            ID = (ushort)(ushort.MaxValue - 1 - Submarine.loaded.IndexOf(this));
        }

        public static Submarine Load(XElement element, bool unloadPrevious)
        {
            if (unloadPrevious) Unload();

            //tryload -> false

            Submarine sub = new Submarine(element.GetAttributeString("name", ""), "", false);
            sub.Load(unloadPrevious, element);

            return sub;
        }

        public static Submarine Load(string fileName, bool unloadPrevious)
        {
            return Load(fileName, SavePath, unloadPrevious);
        }

        public static Submarine Load(string fileName, string folder, bool unloadPrevious)
        {
            if (unloadPrevious) Unload();

            string path = string.IsNullOrWhiteSpace(folder) ? fileName : System.IO.Path.Combine(SavePath, fileName);

            Submarine sub = new Submarine(path);
            sub.Load(unloadPrevious);

            return sub;            
        }
        
        public bool SaveAs(string filePath, MemoryStream previewImage = null)
        {
            name = Path.GetFileNameWithoutExtension(filePath);

            XDocument doc = new XDocument(new XElement("Submarine"));
            SaveToXElement(doc.Root);

            hash = new Md5Hash(doc);
            doc.Root.Add(new XAttribute("md5hash", hash.Hash));
            if (previewImage != null)
            {
                doc.Root.Add(new XAttribute("previewimage", Convert.ToBase64String(previewImage.ToArray())));
            }

            try
            {
                SaveUtil.CompressStringToFile(filePath, doc.ToString());
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving submarine \"" + filePath + "\" failed!", e);
                return false;
            }

            return true;
        }

        public void SaveToXElement(XElement element)
        {
            element.Add(new XAttribute("name", name));
            element.Add(new XAttribute("description", Description ?? ""));
            element.Add(new XAttribute("tags", tags.ToString()));
            element.Add(new XAttribute("gameversion", GameMain.Version.ToString()));

            Rectangle dimensions = CalculateDimensions();
            element.Add(new XAttribute("dimensions", XMLExtensions.Vector2ToString(dimensions.Size.ToVector2())));
            element.Add(new XAttribute("recommendedcrewsizemin", RecommendedCrewSizeMin));
            element.Add(new XAttribute("recommendedcrewsizemax", RecommendedCrewSizeMax));
            element.Add(new XAttribute("recommendedcrewexperience", RecommendedCrewExperience ?? ""));
            element.Add(new XAttribute("requiredcontentpackages", string.Join(", ", RequiredContentPackages)));
            
            foreach (MapEntity e in MapEntity.mapEntityList)
            {
                if (e.Submarine != this || !e.ShouldBeSaved) continue;
                e.Save(element);
            }
        }


        public static bool Unloading
        {
            get;
            private set;
        }

        public static void Unload()
        {
            Unloading = true;

#if CLIENT
            RemoveAllRoundSounds(); //Sound.OnGameEnd();

            if (GameMain.LightManager != null) GameMain.LightManager.ClearLights();
#endif

            foreach (Submarine sub in loaded)
            {
                sub.Remove();
            }

            loaded.Clear();

            visibleEntities = null;

            if (GameMain.GameScreen.Cam != null) GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;

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

            GameMain.World.Clear();            

            Unloading = false;
        }

        public override void Remove()
        {
            base.Remove();

            subBody = null;

            visibleEntities = null;

            if (MainSub == this) MainSub = null;
            if (MainSubs[1] == this) MainSubs[1] = null;

            DockedTo?.Clear();
        }

        public void Dispose()
        {
            savedSubmarines.Remove(this);
#if CLIENT
            PreviewImage?.Remove();
            PreviewImage = null;
#endif
        }

        private List<PathNode> outdoorNodes;
        private List<PathNode> OutdoorNodes
        {
            get
            {
                if (outdoorNodes == null)
                {
                    outdoorNodes = PathNode.GenerateNodes(WayPoint.WayPointList.FindAll(wp => wp.SpawnType == SpawnType.Path && wp.Submarine == this && wp.CurrentHull == null));
                }
                return outdoorNodes;
            }
        }
        private HashSet<PathNode> obstructedNodes = new HashSet<PathNode>();

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
                if (wp.isObstructed) { continue; }
                foreach (var connection in node.connections)
                {
                    bool isObstructed = false;
                    var connectedWp = connection.Waypoint;
                    if (connectedWp.isObstructed) { continue; }
                    Vector2 start = ConvertUnits.ToSimUnits(wp.WorldPosition);
                    Vector2 end = ConvertUnits.ToSimUnits(connectedWp.WorldPosition);
                    var body = Submarine.PickBody(start, end, null, Physics.CollisionLevel, allowInsideFixture: false);
                    if (body != null)
                    {
                        connectedWp.isObstructed = true;
                        wp.isObstructed = true;
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
            // Check collisions to other subs. Currently only walls are taken into account.
            foreach (var node in OutdoorNodes)
            {
                if (node == null || node.Waypoint == null) { continue; }
                var wp = node.Waypoint;
                if (wp.isObstructed) { continue; }
                foreach (var connection in node.connections)
                {
                    bool isObstructed = false;
                    var connectedWp = connection.Waypoint;
                    if (connectedWp.isObstructed) { continue; }
                    Vector2 start = ConvertUnits.ToSimUnits(wp.WorldPosition) - otherSub.SimPosition;
                    Vector2 end = ConvertUnits.ToSimUnits(connectedWp.WorldPosition) - otherSub.SimPosition;
                    var body = Submarine.PickBody(start, end, null, Physics.CollisionWall, allowInsideFixture: false);
                    if (body != null && body.UserData is Structure && !((Structure)body.UserData).IsPlatform)
                    {
                        connectedWp.isObstructed = true;
                        wp.isObstructed = true;
                        obstructedNodes.Add(node);
                        obstructedNodes.Add(connection);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Only affects temporarily disabled waypoints.
        /// </summary>
        public void EnableObstructedWaypoints()
        {
            foreach (var node in obstructedNodes)
            {
                node.Waypoint.isObstructed = false;
            }
            obstructedNodes.Clear();
        }
    }

}
