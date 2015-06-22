using FarseerPhysics;
using FarseerPhysics.Collision;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Subsurface
{
    public enum Direction : byte
    {
        None = 0, Left = 1, Right = 2
    }

    class Map
    {
        static string MapFolder;
        MapHash mapHash;

        public static List<Map> SavedMaps = new List<Map>();

        private static Map loaded;

        //public static Map Loaded
        //{
        //    get { return loaded; }
        //    set { loaded = value; }
        //}


        public static readonly Vector2 gridSize = new Vector2(16.0f, 16.0f);

        private static Vector2 lastPickedPosition;
        private static float lastPickedFraction;

        private Rectangle borders;

        private string filePath;
        private string name;

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

        public MapHash MapHash
        {
            get
            {
                if (mapHash != null) return mapHash;

                XDocument doc = OpenDoc(filePath);
                mapHash = new MapHash(doc);

                return mapHash;
            }
        }

        public static Map Loaded
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

        public string FilePath
        {
            get { return filePath; }
        }

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
        }

        public static void DrawBack(SpriteBatch spriteBatch, bool editing = false)
        {

            for (int i = 0; i < MapEntity.mapEntityList.Count(); i++)
            {
                if (MapEntity.mapEntityList[i].sprite == null || MapEntity.mapEntityList[i].sprite.Depth >= 0.5f)
                    MapEntity.mapEntityList[i].Draw(spriteBatch, editing);
            }
        }

        public static Vector2 MouseToWorldGrid(Camera cam)
        {
            Vector2 position = new Vector2(PlayerInput.GetMouseState.X, PlayerInput.GetMouseState.Y);
            position = cam.ScreenToWorld(position);

            return VectorToWorldGrid(position);
        }

        public static Vector2 VectorToWorldGrid(Vector2 position)
        {
            position.X = (float)Math.Floor(Convert.ToDouble(position.X / gridSize.X)) * gridSize.X;
            position.Y = (float)Math.Ceiling(Convert.ToDouble(position.Y / gridSize.Y)) * gridSize.Y;

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
            Game1.world.RayCast((fixture, point, normal, fraction) =>
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


        public static Body CheckVisibility(Vector2 rayStart, Vector2 rayEnd)
        {
            Body closestBody = null;
            float closestFraction = 1.0f;

            if (Vector2.Distance(rayStart,rayEnd)<0.01f)
            {
                closestFraction = 0.01f;
                return null;
            }
            
            Game1.world.RayCast((fixture, point, normal, fraction) =>
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

            Game1.world.QueryAABB(p =>
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

            mapHash = new MapHash(doc);
            doc.Root.Add(new XAttribute("md5hash", mapHash.MD5Hash));

            try
            {
                SaveUtil.CompressStringToFile(filePath, doc.ToString());
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving map ''" + filePath + "'' failed!", e);
            }


            //doc.Save(filePath);
        }

        public static void SaveCurrent(string savePath)
        {
            if (loaded==null)
            {
                loaded = new Map(savePath);
                return;
            }

            loaded.SaveAs(savePath);
        }

        public static void PreloadMaps(string mapFolder)
        {
            MapFolder = mapFolder;

            //string[] mapFilePaths;
            Unload();
            SavedMaps.Clear();

            if (!Directory.Exists(MapFolder))
            {
                try
                {
                    Directory.CreateDirectory(MapFolder);
                }
                catch
                {

                    DebugConsole.ThrowError("Directory ''Content/SavedMaps'' not found and creating the directory failed.");
                    return;
                }
            }

            string[] mapFilePaths;

            try
            {
                mapFilePaths = Directory.GetFiles(MapFolder);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Couldn't open directory ''Content/SavedMaps''!", e);
                return;
            }

            foreach (string mapPath in mapFilePaths)
            {
                //Map savedMap = new Map(mapPath);
                SavedMaps.Add(new Map(mapPath));
            }
        }

        public Map(string filePath, string mapHash="")
        {
            this.filePath = filePath;
            try
            {
                name = Path.GetFileNameWithoutExtension(filePath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error loading map " + filePath + "!", e);
            }


            if (mapHash != "")
            {
                this.mapHash = new MapHash(mapHash);
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

        }

        private XDocument OpenDoc(string file)
        {
            XDocument doc = null;
            string extension = "";

            try
            {
                extension = Path.GetExtension(file);
            }
            catch
            {
                DebugConsole.ThrowError("Couldn't load map ''" + file + "! (Unrecognized file extension)");
                return null;
            }

            if (extension == ".gz")
            {
                Stream stream = SaveUtil.DecompressFiletoStream(file);
                if (stream == null)
                {
                    DebugConsole.ThrowError("Loading map ''" + file + "'' failed!");
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
                    DebugConsole.ThrowError("Loading map ''" + file + "'' failed!");
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
                    DebugConsole.ThrowError("Loading map ''" + file + "'' failed!");
                    return null;
                }
            }
            else
            {
                DebugConsole.ThrowError("Couldn't load map ''" + file + "! (Unrecognized file extension)");
                return null;
            }

            return doc;
        }

        public void Load()
        {
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

            borders = new Rectangle(0, 0, 1, 1);
            foreach (Hull hull in Hull.hullList)
            {
                if (hull.Rect.X < borders.X || borders.X == 0) borders.X = hull.Rect.X;
                if (hull.Rect.Y > borders.Y || borders.Y == 0) borders.Y = hull.Rect.Y;

                if (hull.Rect.X + hull.Rect.Width > borders.X + borders.Width) borders.Width = hull.Rect.X + hull.Rect.Width - borders.X;
                if (hull.Rect.Y - hull.Rect.Height < borders.Y - borders.Height) borders.Height = borders.Y - (hull.Rect.Y - hull.Rect.Height);
            }

            MapEntity.LinkAll();
            foreach (Item item in Item.itemList)
            {
                foreach (ItemComponent ic in item.components)
                {
                    ic.OnMapLoaded();
                }
            }

            loaded = this;
        }

        public static Map Load(string file)
        {
            Unload();

            Map map = new Map(file);
            map.Load();

            return map;
            
        }

        public static void Unload()
        {
            if (loaded == null) return;
            loaded.Clear();
            loaded = null;
        }

        private void Clear()
        {
            if (Game1.GameScreen.Cam != null) Game1.GameScreen.Cam.TargetPos = Vector2.Zero;

            Entity.RemoveAll();

            PhysicsBody.list.Clear();

            Ragdoll.list.Clear();

            Game1.world.Clear();
        }

    }
}
