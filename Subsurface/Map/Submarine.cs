using FarseerPhysics;
using FarseerPhysics.Collision;
using FarseerPhysics.Common;
using FarseerPhysics.Common.Decomposition;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Factories;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Subsurface.Items.Components;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Voronoi2;

namespace Subsurface
{
    public enum Direction : byte
    {
        None = 0, Left = 1, Right = 2
    }

    class Submarine : Entity
    {
        public static List<Submarine> SavedSubmarines = new List<Submarine>();
        
        public static readonly Vector2 GridSize = new Vector2(16.0f, 16.0f);

        private static Submarine loaded;

        private static Vector2 lastPickedPosition;
        private static float lastPickedFraction;

        static string SaveFolder;
        Md5Hash hash;
        
        Vector2 speed;

        Vector2 targetPosition;

        private Rectangle borders;

        private Body hullBody;

        private string filePath;
        private string name;


        private double lastNetworkUpdate;


        //properties ----------------------------------------------------

        public string Name
        {
            get { return name; }
        }

        public static Vector2 LastPickedPosition
        {
            get { return lastPickedPosition; }
        }

        public static float LastPickedFraction
        {
            get { return lastPickedFraction; }
        }

        public List<Vector2> HullVertices
        {
            get;
            private set;
        }

        public Md5Hash Hash
        {
            get
            {
                if (hash != null) return hash;

                XDocument doc = OpenDoc(filePath);
                hash = new Md5Hash(doc);

                return hash;
            }
        }

        public static Submarine Loaded
        {
            get { return loaded; }
        }

        public static Rectangle Borders
        {
            get 
            { 
                return (loaded==null) ? Rectangle.Empty : loaded.borders;                
            }
        }

        public Vector2 Center
        {
            get { return new Vector2(borders.X+borders.Width/2, borders.Y - borders.Height/2); }
        }

        public Vector2 Position
        {
            get { return (Level.Loaded == null) ? Vector2.Zero : -Level.Loaded.Position; }
        }

        public Vector2 Speed
        {
            get { return speed; }
        }

        public string FilePath
        {
            get { return filePath; }
        }

        //constructors & generation ----------------------------------------------------

        public Submarine(string filePath, string hash = "")
        {
            this.filePath = filePath;
            try
            {
                name = System.IO.Path.GetFileNameWithoutExtension(filePath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error loading map " + filePath + "!", e);
            }

            if (hash != "")
            {
                this.hash = new Md5Hash(hash);
            }
            else
            {
                //XDocument doc = OpenDoc(filePath);

                //string md5Hash = ToolBox.GetAttributeString(doc.Root, "md5hash", "");
                //if (md5Hash == "" || md5Hash.Length < 16)
                //{
                //    DebugConsole.ThrowError("Couldn't find a valid MD5 hash in the map file");
                //}

                //this.mapHash = new MapHash(md5Hash);
            }

            base.Remove();
            ID = -1;
        }

        private List<Vector2> GenerateConvexHull()
        {
            List<Vector2> points = new List<Vector2>();
            
            Vector2 leftMost = Vector2.Zero;

            foreach (Structure wall in Structure.wallList)
            {
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        Vector2 corner = new Vector2(wall.Rect.X + wall.Rect.Width / 2.0f, wall.Rect.Y - wall.Rect.Height / 2.0f);
                        corner.X += x * wall.Rect.Width / 2.0f;
                        corner.Y += y * wall.Rect.Height / 2.0f;

                        if (points.Contains(corner)) continue;

                        points.Add(corner);
                        if (leftMost == Vector2.Zero || corner.X < leftMost.X) leftMost = corner;
                    }
                }
            }

            List<Vector2> hullPoints = new List<Vector2>();

            Vector2 currPoint = leftMost;
            Vector2 endPoint;
            do
            {
                hullPoints.Add(currPoint);
                endPoint = points[0];

                for (int i = 1; i < points.Count; i++)
                {
                    if ((currPoint == endPoint)
                        || (Orientation(currPoint, endPoint, points[i]) == -1))
                    {
                        endPoint = points[i];
                    }
                }

                currPoint = endPoint;

            }
            while (endPoint != hullPoints[0]);

            return hullPoints;
        }

        private static int Orientation(Vector2 p1, Vector2 p2, Vector2 p)
        {
            // Determinant
            float Orin = (p2.X - p1.X) * (p.Y - p1.Y) - (p.X - p1.X) * (p2.Y - p1.Y);

            if (Orin > 0)
                return -1; //          (* Orientation is to the left-hand side  *)
            if (Orin < 0)
                return 1; // (* Orientation is to the right-hand side *)

            return 0; //  (* Orientation is neutral aka collinear  *)
        }

        //drawing ----------------------------------------------------

        public static void Draw(SpriteBatch spriteBatch, bool editing = false)
        {
            for (int i = 0; i < MapEntity.mapEntityList.Count(); i++ )
            {
                MapEntity.mapEntityList[i].Draw(spriteBatch, editing);
            }
        }

        public static void DrawFront(SpriteBatch spriteBatch, bool editing = false)
        {
            for (int i = 0; i < MapEntity.mapEntityList.Count(); i++)
            {
                if (MapEntity.mapEntityList[i].sprite == null || MapEntity.mapEntityList[i].sprite.Depth < 0.5f)
                    MapEntity.mapEntityList[i].Draw(spriteBatch, editing);
            }

            if (loaded == null) return;

            //foreach (HullBody hb in loaded.hullBodies)
            //{
            //    spriteBatch.Draw(
            //        hb.shapeTexture,
            //        ConvertUnits.ToDisplayUnits(new Vector2(hb.body.Position.X, -hb.body.Position.Y)),
            //        null,
            //        Color.White,
            //        -hb.body.Rotation,
            //        new Vector2(hb.shapeTexture.Width / 2, hb.shapeTexture.Height / 2), 1.0f, SpriteEffects.None, 0.0f);
            //}
        }

        public static void DrawBack(SpriteBatch spriteBatch, bool editing = false)
        {
            for (int i = 0; i < MapEntity.mapEntityList.Count(); i++)
            {
                if (MapEntity.mapEntityList[i].sprite == null || MapEntity.mapEntityList[i].sprite.Depth >= 0.5f)
                    MapEntity.mapEntityList[i].Draw(spriteBatch, editing);
            }
        }

        //math/physics stuff ----------------------------------------------------

        public static Vector2 MouseToWorldGrid(Camera cam)
        {
            Vector2 position = new Vector2(PlayerInput.GetMouseState.X, PlayerInput.GetMouseState.Y);
            position = cam.ScreenToWorld(position);

            return VectorToWorldGrid(position);
        }

        public static Vector2 VectorToWorldGrid(Vector2 position)
        {
            position.X = (float)Math.Floor(Convert.ToDouble(position.X / GridSize.X)) * GridSize.X;
            position.Y = (float)Math.Ceiling(Convert.ToDouble(position.Y / GridSize.Y)) * GridSize.Y;

            return position;
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

        public static bool RectContains(Rectangle rect, Vector2 pos)
        {
            return (pos.X > rect.X && pos.X < rect.X + rect.Width
                && pos.Y < rect.Y && pos.Y > rect.Y - rect.Height);
        }

        public static bool RectsOverlap(Rectangle rect1, Rectangle rect2, bool inclusive=true)
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

        public static Body PickBody(Vector2 rayStart, Vector2 rayEnd, List<Body> ignoredBodies = null)
        {


            float closestFraction = 1.0f;
            Body closestBody = null;
            Game1.World.RayCast((fixture, point, normal, fraction) =>
            {
                if (fixture == null || fixture.CollisionCategories == Category.None) return -1;                
                if (ignoredBodies != null && ignoredBodies.Contains(fixture.Body)) return -1;

                Structure structure = fixture.Body.UserData as Structure;                
                if (structure != null && (structure.IsPlatform || !structure.HasBody)) return -1;

                if (fraction < closestFraction)
                {
                    closestFraction = fraction;
                    if (fixture.Body!=null) closestBody = fixture.Body;
                }
                return fraction;
            }
            , rayStart, rayEnd);

            lastPickedPosition = rayStart + (rayEnd - rayStart) * closestFraction;
            lastPickedFraction = closestFraction;
            return closestBody;
        }
        
        /// <summary>
        /// check visibility between two points (in sim units)
        /// </summary>
        /// <returns>a physics body that was between the points (or null)</returns>
        public static Body CheckVisibility(Vector2 rayStart, Vector2 rayEnd)
        {
            Body closestBody = null;
            float closestFraction = 1.0f;

            if (Vector2.Distance(rayStart, rayEnd) < 0.01f)
            {
                closestFraction = 0.01f;
                return null;
            }
            
            Game1.World.RayCast((fixture, point, normal, fraction) =>
            {
                if (fixture == null || fixture.CollisionCategories != Physics.CollisionWall) return -1;

                Structure structure = fixture.Body.UserData as Structure;
                if (structure != null)
                {
                    if (structure.IsPlatform || structure.StairDirection != Direction.None) return -1;
                    int sectionIndex = structure.FindSectionIndex(ConvertUnits.ToDisplayUnits(point));
                    if (sectionIndex > -1 && structure.SectionHasHole(sectionIndex)) return -1;
                }

                if (fraction < closestFraction)
                {
                    closestBody = fixture.Body;
                    closestFraction = fraction;
                }
                return closestFraction;
            }
            , rayStart, rayEnd);


            lastPickedPosition = rayStart + (rayEnd - rayStart) * closestFraction;
            lastPickedFraction = closestFraction;
            return closestBody;
        }

        public static Body PickBody(Vector2 point)
        {
            Body foundBody = null;
            AABB aabb = new AABB(point, point);

            Game1.World.QueryAABB(p =>
            {
                foundBody = p.Body;

                return true;

            }, ref aabb);

            return foundBody;
        }

        public static bool InsideWall(Vector2 point)
        {
            Body foundBody = PickBody(point);
            if (foundBody==null) return false;

            Structure wall = foundBody.UserData as Structure;
            if (wall == null || wall.IsPlatform) return false;
            
            return true;
        }
        
        //movement ----------------------------------------------------

        float collisionRigidness = 1.0f;

        public void Update(float deltaTime)
        {
            Vector2 translateAmount = speed * deltaTime;
            translateAmount += ConvertUnits.ToDisplayUnits(hullBody.Position) * collisionRigidness;

            if (targetPosition != Vector2.Zero && Vector2.Distance(targetPosition, Position) > 50.0f)
            {
                translateAmount += (targetPosition - Position) * 0.01f;
            }
            else
            {
                targetPosition = Vector2.Zero;
            }

            Translate(translateAmount);
            
            ApplyForce(CalculateBuoyancy());

            float dragCoefficient = 0.00001f;

            float speedLength = speed.Length();
            float drag = speedLength * speedLength * dragCoefficient * mass;

            if (speed != Vector2.Zero)
            {
                ApplyForce(-Vector2.Normalize(speed) * drag);
            }
            //hullBodies[0].body.LinearVelocity = -hullBodies[0].body.Position;
            
            //hullBody.SetTransform(Vector2.Zero , 0.0f);
            hullBody.LinearVelocity = -hullBody.Position/(float)Physics.step;

            if (collidingCell == null)
            {
                collisionRigidness = MathHelper.Lerp(collisionRigidness, 1.0f, 0.1f);
                return;
            }

            foreach (GraphEdge ge in collidingCell.edges)
            {
                Body body = PickBody(
                    ConvertUnits.ToSimUnits(ge.point1+ Game1.GameSession.Level.Position), 
                    ConvertUnits.ToSimUnits(ge.point2 + Game1.GameSession.Level.Position), new List<Body>(){collidingCell.body});
                if (body == null || body.UserData == null) continue;

                Structure structure = body.UserData as Structure;
                if (structure == null) continue;
                structure.AddDamage(lastPickedPosition, DamageType.Blunt, 50.0f, 0.0f, 0.0f, true);
            }

            //hullBodies[0].body.SetTransform(Vector2.Zero, 0.0f);

            //position = hullBodies[0].body.Position;

            //Level.Loaded.Move(-ConvertUnits.ToDisplayUnits(position - prevPosition));

            //prevPosition = hullBodies[0].body.Position;

        }

        private Vector2 CalculateBuoyancy()
        {
            float waterVolume = 0.0f;
            float volume = 0.0f;
            foreach (Hull hull in Hull.hullList)
            {
                waterVolume += hull.Volume;
                volume += hull.FullVolume;
            }

            float waterPercentage = waterVolume / volume;

            float neutralPercentage = 0.07f;

            float buoyancy = neutralPercentage-waterPercentage;
            buoyancy *= mass * 30.0f;

            return new Vector2(0.0f, buoyancy);
        }

        public void SetPosition(Vector2 position)
        {
            //hullBodies[0].body.SetTransform(position, 0.0f);
            Level.Loaded.SetPosition(-position);
            //prevPosition = position;
        }

        private void Translate(Vector2 amount)
        {
            if (amount == Vector2.Zero) return;

            Level.Loaded.Move(-amount);
        }

        float mass = 10000.0f;
        public void ApplyForce(Vector2 force)
        {
            speed += force/mass;
        }

        //public void Move(Vector2 amount)
        //{
        //    speed = Vector2.Lerp(speed, amount, 0.05f);
        //}

        VoronoiCell collidingCell;
        public bool OnCollision(Fixture f1, Fixture f2, Contact contact)
        {
            System.Diagnostics.Debug.WriteLine("colliding");
            VoronoiCell cell = f2.Body.UserData as VoronoiCell;
            if (cell==null) return true;

            Vector2 normal = -contact.Manifold.LocalNormal;
            Vector2 simSpeed = ConvertUnits.ToSimUnits(speed);
            float impact = -Vector2.Dot(simSpeed, normal);

            Vector2 u = Vector2.Dot(simSpeed, normal)*normal;
            Vector2 w = simSpeed - u;


            System.Diagnostics.Debug.WriteLine("IMPACT:"+impact);
            if (impact < 5.0f)
            {
                speed = ConvertUnits.ToDisplayUnits(w * 0.9f - u * 0.2f);
                return true;
            }
            else
            {
                speed = ConvertUnits.ToDisplayUnits(w * 0.9f + u * 0.5f);
            }

            collisionRigidness = 0.8f;

            collidingCell = cell;

            return true;
        }

        public void OnSeparation(Fixture f1, Fixture f2)
        {            
            collidingCell = null;
        }

        public override void FillNetworkData(Networking.NetworkEventType type, NetOutgoingMessage message, object data)
        {
            message.Write(NetTime.Now);
            message.Write(Position.X);
            message.Write(Position.Y);

            message.Write(speed.X);
            message.Write(speed.Y);

        }

        public override void ReadNetworkData(Networking.NetworkEventType type, NetIncomingMessage message)
        {
            double sendingTime;
            Vector2 newTargetPosition, newSpeed;
            try
            {
                sendingTime = message.ReadDouble();

                if (sendingTime <= lastNetworkUpdate) return;

                newTargetPosition = new Vector2(message.ReadFloat(), message.ReadFloat());
                newSpeed = new Vector2(message.ReadFloat(), message.ReadFloat());
            }

            catch
            {
                return;
            }

            //newTargetPosition = newTargetPosition + newSpeed * (float)(NetTime.Now - sendingTime);

            targetPosition = newTargetPosition;
            speed = newSpeed;

            lastNetworkUpdate = sendingTime;
        }
    
        

        //saving/loading ----------------------------------------------------

        public void Save()
        {
            SaveAs(filePath);
        }

        public void SaveAs(string filePath)
        {
            //if (filePath=="")
            //{
            //    DebugConsole.ThrowError("No save file selected");
            //    return;
            //}
            XDocument doc = new XDocument(new XElement((XName)name));

            foreach (MapEntity e in MapEntity.mapEntityList)
            {
                e.Save(doc);
            }

            hash = new Md5Hash(doc);
            doc.Root.Add(new XAttribute("md5hash", hash.Hash));

            try
            {
                SaveUtil.CompressStringToFile(filePath, doc.ToString());
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving submarine ''" + filePath + "'' failed!", e);
            }


            //doc.Save(filePath);
        }

        public static void SaveCurrent(string savePath)
        {
            if (loaded==null)
            {
                loaded = new Submarine(savePath);
               // return;
            }

            loaded.SaveAs(savePath);
        }

        public static void Preload(string folder)
        {
            SaveFolder = folder;

            //string[] mapFilePaths;
            Unload();
            SavedSubmarines.Clear();

            if (!Directory.Exists(SaveFolder))
            {
                try
                {
                    Directory.CreateDirectory(SaveFolder);
                }
                catch
                {

                    DebugConsole.ThrowError("Directory ''"+SaveFolder+"'' not found and creating the directory failed.");
                    return;
                }
            }

            string[] filePaths;

            try
            {
                filePaths = Directory.GetFiles(SaveFolder);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Couldn't open directory ''" + SaveFolder + "''!", e);
                return;
            }

            foreach (string path in filePaths)
            {
                //Map savedMap = new Map(mapPath);
                SavedSubmarines.Add(new Submarine(path));
            }
        }

        private XDocument OpenDoc(string file)
        {
            XDocument doc = null;
            string extension = "";

            try
            {
                extension = System.IO.Path.GetExtension(file);
            }
            catch
            {
                DebugConsole.ThrowError("Couldn't load submarine ''" + file + "! (Unrecognized file extension)");
                return null;
            }

            if (extension == ".gz")
            {
                Stream stream = SaveUtil.DecompressFiletoStream(file);
                if (stream == null)
                {
                    DebugConsole.ThrowError("Loading submarine ''" + file + "'' failed!");
                    return null;
                }

                try
                {
                    stream.Position = 0;
                    doc = XDocument.Load(stream); //ToolBox.TryLoadXml(file);
                    stream.Close();
                    stream.Dispose();
                }

                catch
                {
                    DebugConsole.ThrowError("Loading submarine ''" + file + "'' failed!");
                    return null;
                }
            }
            else if (extension == ".xml")
            {
                try
                {
                    doc = XDocument.Load(file);
                }

                catch
                {
                    DebugConsole.ThrowError("Loading submarine ''" + file + "'' failed!");
                    return null;
                }
            }
            else
            {
                DebugConsole.ThrowError("Couldn't load submarine ''" + file + "! (Unrecognized file extension)");
                return null;
            }

            return doc;
        }

        public void Load()
        {
            Unload();
            //string file = filePath;

            XDocument doc = OpenDoc(filePath);
            if (doc == null) return;

            foreach (XElement element in doc.Root.Elements())
            {
                string typeName = element.Name.ToString();

                Type t;
                try
                {
                    // Get the type of a specified class.
                    t = Type.GetType("Subsurface." + typeName + ", Subsurface", true, true);
                    if (t == null)
                    {
                        DebugConsole.ThrowError("Error in " + filePath + "! Could not find a entity of the type ''" + typeName + "''.");
                        continue;
                    }
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error in " + filePath + "! Could not find a entity of the type ''" + typeName + "''.", e);
                    continue;
                }

                try
                {
                    MethodInfo loadMethod = t.GetMethod("Load");
                    loadMethod.Invoke(t, new object[] { element });
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Could not find the method ''Load'' in " + t + ".", e);
                }

            }

            List<Vector2> convexHull = GenerateConvexHull();

            HullVertices = convexHull;

            for (int i = 0; i < convexHull.Count; i++)
            {
                convexHull[i] = ConvertUnits.ToSimUnits(convexHull[i]);
            }

            convexHull.Reverse();

            //get farseer 'vertices' from vectors
            Vertices shapevertices = new Vertices(convexHull);
            
            AABB hullAABB = shapevertices.GetAABB();

            borders = new Rectangle(
                (int)ConvertUnits.ToDisplayUnits(hullAABB.LowerBound.X),
                (int)ConvertUnits.ToDisplayUnits(hullAABB.UpperBound.Y),
                (int)ConvertUnits.ToDisplayUnits(hullAABB.Extents.X * 2.0f),
                (int)ConvertUnits.ToDisplayUnits(hullAABB.Extents.Y * 2.0f));

            
            var triangulatedVertices = Triangulate.ConvexPartition(shapevertices, TriangulationAlgorithm.Bayazit);

            hullBody = BodyFactory.CreateCompoundPolygon(Game1.World, triangulatedVertices, 5.0f);
            hullBody.BodyType = BodyType.Dynamic;

            hullBody.CollisionCategories = Physics.CollisionMisc;
            hullBody.CollidesWith = Physics.CollisionLevel;
            hullBody.FixedRotation = true;
            hullBody.Awake = true;
            hullBody.SleepingAllowed = false;
            hullBody.GravityScale = 0.0f;
            hullBody.OnCollision += OnCollision;
            hullBody.OnSeparation += OnSeparation;
            
            MapEntity.LinkAll();
            
            foreach (Item item in Item.itemList)
            {
                System.Diagnostics.Debug.WriteLine(item.ID);
                foreach (ItemComponent ic in item.components)
                {
                    ic.OnMapLoaded();
                }
            }

            ID = int.MaxValue-10;

            loaded = this;
        }

        public static Submarine Load(string file)
        {
            Unload();            

            Submarine sub = new Submarine(file);
            sub.Load();

            //Entity.dictionary.Add(int.MaxValue, sub);

            return sub;            
        }

        public static void Unload()
        {
            if (loaded == null) return;
            
            loaded.Remove();

            loaded.Clear();
            loaded = null;
        }

        private void Clear()
        {
            if (Game1.GameScreen.Cam != null) Game1.GameScreen.Cam.TargetPos = Vector2.Zero;

            Entity.RemoveAll();

            PhysicsBody.list.Clear();
            
            Ragdoll.list.Clear();

            Game1.World.Clear();
        }

    }

}
