using FarseerPhysics;
using FarseerPhysics.Collision;
using FarseerPhysics.Common;
using FarseerPhysics.Dynamics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Barotrauma.Lights;

namespace Barotrauma
{
    public enum Direction : byte
    {
        None = 0, Left = 1, Right = 2
    }

    class Submarine : Entity
    {
        public static string SavePath = "Data" + System.IO.Path.DirectorySeparatorChar + "SavedSubs";

        //position of the "actual submarine" which is rendered wherever the SubmarineBody is 
        //should be in an unreachable place
        public static readonly Vector2 HiddenSubPosition = new Vector2(0.0f, 50000.0f);

        public static List<Submarine> SavedSubmarines = new List<Submarine>();
        
        public static readonly Vector2 GridSize = new Vector2(16.0f, 16.0f);

        private static Submarine loaded;

        private SubmarineBody subBody;

        private static Vector2 lastPickedPosition;
        private static float lastPickedFraction;

        Md5Hash hash;
        
        private string filePath;
        private string name;

        private Vector2 prevPosition;

        private float lastNetworkUpdate;


        //properties ----------------------------------------------------

        public string Name
        {
            get { return name; }
            set { name = value; }
        }

        public static Vector2 LastPickedPosition
        {
            get { return lastPickedPosition; }
        }

        public static float LastPickedFraction
        {
            get { return lastPickedFraction; }
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
                return (loaded==null) ? Rectangle.Empty : Loaded.subBody.Borders;                
            }
        }
        
        public override Vector2 Position
        {
            get { return subBody==null ? Vector2.Zero : subBody.Position - HiddenSubPosition; }
        }

        public override Vector2 WorldPosition
        {
            get
            {
                return subBody.Position;
            }
        }

        public bool AtEndPosition
        {
            get 
            { 
                if (Level.Loaded == null) return false;
                return (Vector2.Distance(Position + HiddenSubPosition, Level.Loaded.EndPosition) < Level.ExitDistance);
            }
        }

        public bool AtStartPosition
        {
            get
            {
                if (Level.Loaded == null) return false;
                return (Vector2.Distance(Position + HiddenSubPosition, Level.Loaded.StartPosition) < Level.ExitDistance);
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
            get { return subBody==null ? Vector2.Zero : subBody.Velocity; }
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
        }

        public bool AtDamageDepth
        {
            get { return subBody == null ? false : subBody.AtDamageDepth; }
        }

        //constructors & generation ----------------------------------------------------

        public Submarine(string filePath, string hash = "") : base(null)
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
            ID = ushort.MaxValue;
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
                if (MapEntity.mapEntityList[i].Sprite == null || MapEntity.mapEntityList[i].Sprite.Depth < 0.5f)
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
                if (MapEntity.mapEntityList[i].Sprite == null || MapEntity.mapEntityList[i].Sprite.Depth >= 0.5f)
                    MapEntity.mapEntityList[i].Draw(spriteBatch, editing);
            }
        }

        public void UpdateTransform()
        {
            DrawPosition = Physics.Interpolate(prevPosition, Position);            
        }

        //math/physics stuff ----------------------------------------------------

        public static Vector2 MouseToWorldGrid(Camera cam)
        {
            Vector2 position = PlayerInput.MousePosition;
            position = cam.ScreenToWorld(position);

            return VectorToWorldGrid(position);
        }

        public static Vector2 VectorToWorldGrid(Vector2 position)
        {
            position.X = (float)Math.Floor(position.X / GridSize.X) * GridSize.X;
            position.Y = (float)Math.Ceiling(position.Y / GridSize.Y) * GridSize.Y;

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

        public static Body PickBody(Vector2 rayStart, Vector2 rayEnd, List<Body> ignoredBodies = null, Category? collisionCategory = null)
        {
            if (Vector2.DistanceSquared(rayStart, rayEnd) < 0.00001f)
            {
                rayEnd += Vector2.UnitX * 0.001f;
            }

            float closestFraction = 1.0f;
            Body closestBody = null;
            GameMain.World.RayCast((fixture, point, normal, fraction) =>
            {
                if (fixture == null || 
                    fixture.CollisionCategories == Category.None || 
                    fixture.CollisionCategories == Physics.CollisionMisc) return -1;

                if (collisionCategory != null && !fixture.CollisionCategories.HasFlag((Category)collisionCategory)) return -1;
      
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
        public static Body CheckVisibility(Vector2 rayStart, Vector2 rayEnd, bool ignoreLevel = false)
        {
            Body closestBody = null;
            float closestFraction = 1.0f;

            if (Vector2.Distance(rayStart, rayEnd) < 0.01f)
            {
                closestFraction = 0.01f;
                return null;
            }
            
            GameMain.World.RayCast((fixture, point, normal, fraction) =>
            {
                if (fixture == null || 
                    (fixture.CollisionCategories != Physics.CollisionWall && fixture.CollisionCategories != Physics.CollisionLevel)) return -1;

                if (ignoreLevel && fixture.CollisionCategories == Physics.CollisionLevel) return -1;

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
                
        //movement ----------------------------------------------------


        public void Update(float deltaTime)
        {
            if (Level.Loaded == null) return;

            if (subBody!=null) subBody.Update(deltaTime);
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
            //Level.Loaded.SetPosition(-position);
            //prevPosition = position;
        }

        public void Translate(Vector2 amount)
        {
            if (amount == Vector2.Zero || !MathUtils.IsValid(amount)) return;

            subBody.SetPosition(subBody.Position + amount);

            //Level.Loaded.Move(-amount);
        }

        public override bool FillNetworkData(Networking.NetworkEventType type, NetBuffer message, object data)
        {
            if (subBody == null) return false;

            message.Write(subBody.Position.X);
            message.Write(subBody.Position.Y);

            message.Write(Velocity.X);
            message.Write(Velocity.Y);

            return true;
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, NetIncomingMessage message, float sendingTime, out object data)
        {
            data = null;

            Vector2 newTargetPosition, newSpeed;
            try
            {
                if (sendingTime <= lastNetworkUpdate) return;

                newTargetPosition = new Vector2(message.ReadFloat(), message.ReadFloat());
                newSpeed = new Vector2(message.ReadFloat(), message.ReadFloat());
            }

            catch (Exception e)
            {
#if DEBUG
                DebugConsole.ThrowError("invalid network message", e);
#endif
                return;
            }

            if (!newSpeed.IsValid() || !newTargetPosition.IsValid()) return;

            //newTargetPosition = newTargetPosition + newSpeed * (float)(NetTime.Now - sendingTime);

            subBody.TargetPosition = newTargetPosition;
            subBody.Velocity = newSpeed;

            lastNetworkUpdate = sendingTime;
        }
            

        //saving/loading ----------------------------------------------------

        public bool Save()
        {
            return SaveAs(filePath);
        }

        public bool SaveAs(string filePath)
        {
            name = System.IO.Path.GetFileNameWithoutExtension(filePath);

            XDocument doc = new XDocument(new XElement("Submarine"));
            doc.Root.Add(new XAttribute("name", name));

            foreach (MapEntity e in MapEntity.mapEntityList)
            {
                if (e.MoveWithLevel) continue;
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
                return false;
            }

            return true;
        }

        public static bool SaveCurrent(string fileName)
        {
            if (loaded==null)
            {
                loaded = new Submarine(fileName);
               // return;
            }

            return loaded.SaveAs(SavePath+System.IO.Path.DirectorySeparatorChar+fileName);
        }

        public static void Preload()
        {

            //string[] mapFilePaths;
            Unload();
            SavedSubmarines.Clear();

            if (!Directory.Exists(SavePath))
            {
                try
                {
                    Directory.CreateDirectory(SavePath);
                }
                catch (Exception e)
                {

                    DebugConsole.ThrowError("Directory ''" + SavePath + "'' not found and creating the directory failed.", e);
                    return;
                }
            }

            string[] filePaths;

            try
            {
                filePaths = Directory.GetFiles(SavePath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Couldn't open directory ''" + SavePath + "''!", e);
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

                catch (Exception e)
                {
                    DebugConsole.ThrowError("Loading submarine ''" + file + "'' failed! ("+e.Message+")");
                    return null;
                }
            }
            else if (extension == ".xml")
            {
                try
                {
                    doc = XDocument.Load(file);
                }

                catch (Exception e)
                {
                    DebugConsole.ThrowError("Loading submarine ''" + file + "'' failed! (" + e.Message + ")");
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

            //name = ToolBox.GetAttributeString(doc.Root, "name", name);

            foreach (XElement element in doc.Root.Elements())
            {
                string typeName = element.Name.ToString();

                Type t;
                try
                {
                    t = Type.GetType("Barotrauma." + typeName, true, true);
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
                    loadMethod.Invoke(t, new object[] { element, this });
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Could not find the method ''Load'' in " + t + ".", e);
                }

            }

            subBody = new SubmarineBody(this);
            subBody.SetPosition(HiddenSubPosition);
            
            loaded = this;

            Hull.GenerateEntityGrid();

            MapEntity.MapLoaded();
               
            //WayPoint.GenerateSubWaypoints();

            GameMain.LightManager.OnMapLoaded();

            ID = ushort.MaxValue-10;

        }

        public static Submarine Load(string fileName)
        {
           return Load(fileName, SavePath);
        }

        public static Submarine Load(string fileName, string folder)
        {
            Unload();

            string path = string.IsNullOrWhiteSpace(folder) ? fileName : System.IO.Path.Combine(SavePath, fileName);

            Submarine sub = new Submarine(path);
            sub.Load();

            //Entity.dictionary.Add(int.MaxValue, sub);

            return sub;            
        }

        public static void Unload()
        {
            if (loaded == null) return;

            Sound.OnGameEnd();

            if (GameMain.LightManager != null) GameMain.LightManager.ClearLights();
            
            loaded.Remove();

            loaded.Clear();
            loaded = null;
        }

        private void Clear()
        {
            if (GameMain.GameScreen.Cam != null) GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;

            Entity.RemoveAll();

            subBody = null;

            PhysicsBody.list.Clear();
            
            Ragdoll.list.Clear();

            GameMain.World.Clear();
        }

    }

}
