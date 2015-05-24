using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FarseerPhysics.Collision;

namespace Subsurface
{
    public enum Direction : byte
    {
        None = 0, Left = 1, Right = 2
    }

    static class Map
    {
        public static Vector2 gridSize = new Vector2(16.0f, 16.0f);

        private static Vector2 lastPickedPosition;
        private static float lastPickedFraction;

        private static Rectangle borders;

        private static string filePath;

        public static Vector2 LastPickedPosition
        {
            get { return lastPickedPosition; }
        }

        public static float LastPickedFraction
        {
            get { return lastPickedFraction; }
        }

        public static Rectangle Borders
        {
            get { return borders; }
        }

        public static string FilePath
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

        public static bool RectsOverlap(Rectangle rect1, Rectangle rect2)
        {
            return !(rect1.X > rect2.X + rect2.Width || rect1.X + rect1.Width < rect2.X ||
                rect1.Y < rect2.Y - rect2.Height || rect1.Y - rect1.Height > rect2.Y);
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


        public static Structure CheckVisibility(Vector2 rayStart, Vector2 rayEnd)
        {
            Structure closestStructure = null;
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
                    if (structure != null) closestStructure = structure;
                    closestFraction = fraction;
                }
                return closestFraction;
            }
            , rayStart, rayEnd);


            lastPickedPosition = rayStart + (rayEnd - rayStart) * closestFraction;
            lastPickedFraction = closestFraction;
            return closestStructure;
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
            Body foundBody = Map.PickBody(point);
            if (foundBody==null) return false;

            Structure wall = foundBody.UserData as Structure;
            if (wall == null || wall.IsPlatform) return false;
            
            return true;
        }

        public static void Save(string filePath, string fileName)
        {
            if (fileName==null)
            {
                DebugConsole.ThrowError("No save file selected");
                return;
            }
            XDocument doc = new XDocument(
                new XElement((XName)fileName));

            foreach (MapEntity e in MapEntity.mapEntityList)
            {
                e.Save(doc);
            }

            try
            {
                string docString = doc.ToString();
                ToolBox.CompressStringToFile(filePath+fileName+".gz", doc.ToString());
            }
            catch
            {
                DebugConsole.ThrowError("Saving map ''" + filePath + fileName + "'' failed!");
            }


            //doc.Save(filePath + fileName);
        }

        public static string[] GetMapFilePaths()
        {
            string[] mapFilePaths;
            try
            {
                mapFilePaths = Directory.GetFiles("Content/SavedMaps");
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Couldn't open directory ''Content/SavedMaps''!", e);
                return null;
            }

            return mapFilePaths;
        }

        public static void Load(string filePath, string fileName)
        {
            Load(filePath + fileName);
        }

        public static void Load(string file)
        {
            Clear();
            filePath = file;
            XDocument doc = null;

            string extension = Path.GetExtension(file);

            if (extension==".gz")
            {
                Stream stream = ToolBox.DecompressFiletoStream(file);
                if (stream == null)
                {
                    DebugConsole.ThrowError("Loading map ''" + file + "'' failed!");
                    return;
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
                    return;
                }
            }
            else if (extension ==".xml")
            {
                doc = XDocument.Load(file);
            }
            else
            {
                DebugConsole.ThrowError("Couldn't load map ''"+file+"! (Unrecognized file extension)");
                return;
            }
            

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
                        DebugConsole.ThrowError("Error in " + file + "! Could not find a entity of the type ''" + typeName + "''.");
                        continue;
                    }
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error in " + file + "! Could not find a entity of the type ''" + typeName + "''.", e);
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
        }



        public static void Clear()
        {
            Map.filePath = "";

            if (Game1.gameScreen.Cam != null) Game1.gameScreen.Cam.TargetPos = Vector2.Zero;

            Entity.RemoveAll();
            
            if (Game1.gameSession!=null)
            Game1.gameSession.crewManager.EndShift();

            PhysicsBody.list.Clear();

            Ragdoll.list.Clear();

            Game1.world.Clear();
        }

    }
}
