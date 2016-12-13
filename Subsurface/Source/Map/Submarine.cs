using FarseerPhysics;
using FarseerPhysics.Common;
using FarseerPhysics.Dynamics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

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

    class Submarine : Entity
    {
        public static string SavePath = "Submarines";

        public static readonly Vector2 HiddenSubStartPosition = new Vector2(-50000.0f, 80000.0f);
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

        public static List<Submarine> SavedSubmarines = new List<Submarine>();
        
        public static readonly Vector2 GridSize = new Vector2(16.0f, 16.0f);

        public static Submarine[] MainSubs = new Submarine[2];
        public static Submarine MainSub
        {
            get { return MainSubs[0]; }
            set { MainSubs[0] = value; }
        }
        private static List<Submarine> loaded = new List<Submarine>();

        private static List<MapEntity> visibleEntities;

        private SubmarineBody subBody;

        public readonly List<Submarine> DockedTo;

        private static Vector2 lastPickedPosition;
        private static float lastPickedFraction;

        private Md5Hash hash;
        
        private string filePath;
        private string name;

        private SubmarineTag tags;

        private Vector2 prevPosition;

        private float networkUpdateTimer;

        private EntityGrid entityGrid = null;
        
        //properties ----------------------------------------------------

        public string Name
        {
            get { return name; }
            set { name = value; }
        }

        public bool OnRadar = true;

        public string Description
        {
            get; 
            set; 
        }

        public static Vector2 LastPickedPosition
        {
            get { return lastPickedPosition; }
        }

        public static float LastPickedFraction
        {
            get { return lastPickedFraction; }
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
                return subBody.Borders;                
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
                return subBody == null ? Vector2.Zero : subBody.Position;
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
            set { filePath = value; }
        }

        public bool AtDamageDepth
        {
            get { return subBody != null && subBody.AtDamageDepth; }
        }

        public override string ToString()
        {
            return "Barotrauma.Submarine ("+name+")";
        }

        //constructors & generation ----------------------------------------------------

        public Submarine(string filePath, string hash = "", bool tryLoad = true) : base(null)
        {
            this.filePath = filePath;
            try
            {
                name = System.IO.Path.GetFileNameWithoutExtension(filePath);
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
                XDocument doc = OpenFile(filePath);

                if (doc != null && doc.Root != null)
                {
                    Description = ToolBox.GetAttributeString(doc.Root, "description", "");
                    Enum.TryParse(ToolBox.GetAttributeString(doc.Root, "tags", ""), out tags);
                }
            }

            DockedTo = new List<Submarine>();


            ID = ushort.MaxValue;
            base.Remove();
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

        /// <summary>
        /// Returns a rect that contains the borders of this sub and all subs docked to it
        /// </summary>
        public Rectangle GetDockedBorders()
        {
            Rectangle dockedBorders = Borders;
            dockedBorders.Y -= dockedBorders.Height;

            foreach (Submarine dockedSub in DockedTo)
            {
                Vector2 diff = dockedSub.Submarine == this ? dockedSub.WorldPosition : dockedSub.WorldPosition - WorldPosition;
                    

                Rectangle dockedSubBorders = dockedSub.Borders;
                dockedSubBorders.Y -= dockedSubBorders.Height;
                dockedSubBorders.Location += diff.ToPoint();

                dockedBorders = Rectangle.Union(dockedBorders, dockedSubBorders);
            }

            dockedBorders.Y += dockedBorders.Height;
            return dockedBorders;
        }
        
        //drawing ----------------------------------------------------

        public static void CullEntities(Camera cam)
        {
            HashSet<Submarine> visibleSubs = new HashSet<Submarine>();
            foreach (Submarine sub in Submarine.Loaded)
            {
                Rectangle worldBorders = new Rectangle(
                    sub.Borders.X + (int)sub.WorldPosition.X - 500,
                    sub.Borders.Y + (int)sub.WorldPosition.Y + 500,
                    sub.Borders.Width + 1000,
                    sub.Borders.Height + 1000);

                if (Submarine.RectsOverlap(worldBorders, cam.WorldView))
                {
                    visibleSubs.Add(sub);
                }
            }

            Rectangle worldView = cam.WorldView;

            visibleEntities = new List<MapEntity>();
            foreach (MapEntity me in MapEntity.mapEntityList)
            {
                if (me.Submarine == null || visibleSubs.Contains(me.Submarine))
                {
                    if (me.IsVisible(worldView)) visibleEntities.Add(me);
                }
            }
        }

        public static void Draw(SpriteBatch spriteBatch, bool editing = false)
        {
            var entitiesToRender = !editing && visibleEntities != null ? visibleEntities : MapEntity.mapEntityList;

            foreach (MapEntity e in entitiesToRender)
            {
                e.Draw(spriteBatch, editing);
            }
        }

        public static void DrawFront(SpriteBatch spriteBatch, bool editing = false, Predicate<MapEntity> predicate = null)
        {
            var entitiesToRender = !editing && visibleEntities != null ? visibleEntities : MapEntity.mapEntityList;
            
            foreach (MapEntity e in entitiesToRender)
            {
                if (!e.DrawOverWater) continue;

                if (predicate != null)
                {
                    if (!predicate(e)) continue;
                }

                e.Draw(spriteBatch, editing, false);
            }

            if (GameMain.DebugDraw)
            {
                foreach (Submarine sub in Submarine.Loaded)
                {
                    Rectangle worldBorders = sub.Borders;
                    worldBorders.Location += sub.WorldPosition.ToPoint();
                    worldBorders.Y = -worldBorders.Y;

                    GUI.DrawRectangle(spriteBatch, worldBorders, Color.White, false, 0, 5);

                    if (sub.subBody.MemPos.Count < 2) continue;

                    Vector2 prevPos = ConvertUnits.ToDisplayUnits(sub.subBody.MemPos[0].Position);
                    prevPos.Y = -prevPos.Y;

                    for (int i = 1; i < sub.subBody.MemPos.Count; i++)
                    {
                        Vector2 currPos = ConvertUnits.ToDisplayUnits(sub.subBody.MemPos[i].Position);
                        currPos.Y = -currPos.Y;

                        GUI.DrawRectangle(spriteBatch, new Rectangle((int)currPos.X - 10, (int)currPos.Y - 10, 20, 20), Color.Blue * 0.6f, true, 0.01f);
                        GUI.DrawLine(spriteBatch, prevPos, currPos, Color.Cyan * 0.5f, 0, 5);

                        prevPos = currPos;
                    }
                }
            }
        }


        public static float DamageEffectCutoff;

        public static void DrawDamageable(SpriteBatch spriteBatch, Effect damageEffect, bool editing = false)
        {
            var entitiesToRender = !editing && visibleEntities != null ? visibleEntities : MapEntity.mapEntityList;

            foreach (MapEntity e in entitiesToRender)
            {
                if (e.DrawDamageEffect)
                    e.DrawDamage(spriteBatch, damageEffect);
            }
            if (damageEffect != null)
            {
                damageEffect.Parameters["aCutoff"].SetValue(0.0f);
                damageEffect.Parameters["cCutoff"].SetValue(0.0f);

                DamageEffectCutoff = 0.0f;
            }
        }


        public static void DrawBack(SpriteBatch spriteBatch, bool editing = false, Predicate<MapEntity> predicate = null)
        {
            var entitiesToRender = !editing && visibleEntities != null ? visibleEntities : MapEntity.mapEntityList;
            
            foreach (MapEntity e in entitiesToRender)
            {
                if (!e.DrawBelowWater) continue;

                if (predicate != null)
                {
                    if (!predicate(e)) continue;
                }
                
                e.Draw(spriteBatch, editing, true);
            }
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
                    fixture.CollisionCategories == Physics.CollisionItem) return -1;

                if (collisionCategory != null && 
                    !fixture.CollisionCategories.HasFlag((Category)collisionCategory) &&
                    !((Category)collisionCategory).HasFlag(fixture.CollisionCategories)) return -1;
      
                if (ignoredBodies != null && ignoredBodies.Contains(fixture.Body)) return -1;
                
                Structure structure = fixture.Body.UserData as Structure;                
                if (structure != null)
                {
                    if (structure.IsPlatform && collisionCategory != null && !((Category)collisionCategory).HasFlag(Physics.CollisionPlatform)) return -1;
                }                    

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
        public static Body CheckVisibility(Vector2 rayStart, Vector2 rayEnd, bool ignoreLevel = false, bool ignoreSubs = false)
        {
            Body closestBody = null;
            float closestFraction = 1.0f;

            if (Vector2.Distance(rayStart, rayEnd) < 0.01f)
            {
                lastPickedPosition = rayEnd;
                return null;
            }
            
            GameMain.World.RayCast((fixture, point, normal, fraction) =>
            {
                if (fixture == null || 
                    (!fixture.CollisionCategories.HasFlag(Physics.CollisionWall) && !fixture.CollisionCategories.HasFlag(Physics.CollisionLevel))) return -1;

                if (ignoreLevel && fixture.CollisionCategories == Physics.CollisionLevel) return -1;
                if (ignoreSubs && fixture.Body.UserData is Submarine) return -1;

                Structure structure = fixture.Body.UserData as Structure;
                if (structure != null)
                {
                    if (structure.IsPlatform || structure.StairDirection != Direction.None) return -1;
                    int sectionIndex = structure.FindSectionIndex(ConvertUnits.ToDisplayUnits(point));
                    if (sectionIndex > -1 && structure.SectionBodyDisabled(sectionIndex)) return -1;
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

        private bool flippedX;
        public bool FlippedX
        {
            get { return flippedX; }
        }

        public void FlipX(List<Submarine> parents=null)
        {
            if (parents == null) parents = new List<Submarine>();
            parents.Add(this);

            flippedX = !flippedX;

            Item.UpdateHulls();

            List<Item> bodyItems = Item.ItemList.FindAll(it => it.Submarine == this && it.body != null);
            
            foreach (MapEntity e in MapEntity.mapEntityList)
            {
                if (e.MoveWithLevel || e.Submarine != this || e is Item) continue;
                
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
                    e.FlipX();
                }
            }

            for (int i = 0; i < MapEntity.mapEntityList.Count; i++)
            {
                if (MapEntity.mapEntityList[i].Submarine != this) continue;
                MapEntity.mapEntityList[i].Move(-HiddenSubPosition);
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

            for (int i = 0; i < MapEntity.mapEntityList.Count; i++)
            {
                if (MapEntity.mapEntityList[i].Submarine != this) continue;
                MapEntity.mapEntityList[i].Move(HiddenSubPosition);
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

                item.FlipX();
            }

            Item.UpdateHulls();
            Gap.UpdateHulls();
        }

        public void Update(float deltaTime)
        {
            //if (PlayerInput.KeyHit(InputType.Crouch) && (this == MainSub)) FlipX();

            if (Level.Loaded == null) return;
            
            if (subBody == null) return;
            
            subBody.Update(deltaTime);

            for (int i = 0; i < 2; i++ )
            {
                if (Submarine.MainSubs[i] == null) continue;
                if (this != Submarine.MainSubs[i] && Submarine.MainSubs[i].DockedTo.Contains(this)) return;
            }

            //send updates more frequently if moving fast
            networkUpdateTimer -= MathHelper.Clamp(Velocity.Length()*10.0f, 0.1f, 5.0f) * deltaTime;

            if (networkUpdateTimer < 0.0f)
            {
                new Networking.NetworkEvent(ID, false);
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

        public static Submarine GetClosest(Vector2 worldPosition)
        {
            Submarine closest = null;
            float closestDist = 0.0f;
            foreach (Submarine sub in loaded)
            {
                float dist = Vector2.Distance(worldPosition, sub.WorldPosition);
                if (closest == null || dist < closestDist)
                {
                    closest = sub;
                    closestDist = dist;
                }
            }

            return closest;
        }
        
        public override bool FillNetworkData(Networking.NetworkEventType type, NetBuffer message, object data)
        {
            if (subBody == null) return false;

            message.Write(PhysicsBody.SimPosition.X);
            message.Write(PhysicsBody.SimPosition.Y);
            
            return true;
        }

        public override bool ReadNetworkData(Networking.NetworkEventType type, NetIncomingMessage message, float sendingTime, out object data)
        {
            data = null;

            if (GameMain.Server != null) return false;


            Vector2 newTargetPosition;
            try
            {
                newTargetPosition = new Vector2(
                    message.ReadFloat(), 
                    message.ReadFloat());
            }

            catch (Exception e)
            {
#if DEBUG
                DebugConsole.ThrowError("invalid network message", e);
#endif
                return false;
            }

            if (!newTargetPosition.IsValid()) return false;

            //already interpolating with more up-to-date data -> ignore
            if (subBody.MemPos.Count > 1 && subBody.MemPos[0].Timestamp > sendingTime)
            {
                return true;
            }

            int index = 0;
            while (index < subBody.MemPos.Count && sendingTime > subBody.MemPos[index].Timestamp)
            {
                index++;
            }

            subBody.MemPos.Insert(index, new PosInfo(newTargetPosition, Direction.Right, sendingTime));

            return true;
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
            SaveToXElement(doc.Root);

            hash = new Md5Hash(doc);
            doc.Root.Add(new XAttribute("md5hash", hash.Hash));

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
            element.Add(new XAttribute("description", Description == null ? "" : Description));

            element.Add(new XAttribute("tags", tags.ToString()));

            foreach (MapEntity e in MapEntity.mapEntityList)
            {
                if (e.MoveWithLevel || e.Submarine != this) continue;
                e.Save(element);
            }
        }

        public static bool SaveCurrent(string filePath)
        {
            if (Submarine.MainSub == null)
            {
                Submarine.MainSub = new Submarine(filePath);
               // return;
            }

            Submarine.MainSub.filePath = filePath;

            return Submarine.MainSub.SaveAs(filePath);
        }

        public void CheckForErrors()
        {
            if (!Hull.hullList.Any())
            {
                DebugConsole.ThrowError("No hulls found in the submarine. Hulls determine the \"borders\" of an individual room and are required for water and air distribution to work correctly.");
            }

            foreach (Item item in Item.ItemList)
            {
                if (item.GetComponent<Barotrauma.Items.Components.Vent>() == null) continue;

                if (!item.linkedTo.Any())
                {
                    DebugConsole.ThrowError("The submarine contains vents which haven't been linked to an oxygen generator. Select a vent and click an oxygen generator while holding space to link them.");
                }
            }

            if (WayPoint.WayPointList.Find(wp => !wp.MoveWithLevel && wp.SpawnType == SpawnType.Path) == null)
            {
                DebugConsole.ThrowError("No waypoints found in the submarine. AI controlled crew members won't be able to navigate without waypoints.");
            }

            if (WayPoint.WayPointList.Find(wp => wp.SpawnType == SpawnType.Cargo) == null)
            {
                DebugConsole.ThrowError("The submarine doesn't have spawnpoints for cargo (which are used for determining where to place bought items). "
                    +"To fix this, create a new spawnpoint and change its \"spawn type\" parameter to \"cargo\".");
            }
        }

        public static void Preload()
        {

            //string[] mapFilePaths;
            //Unload();
            SavedSubmarines.Clear();

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
                SavedSubmarines.Add(new Submarine(path));
            }

            //if (GameMain.NetLobbyScreen!=null) GameMain.NetLobbyScreen.UpdateSubList(Submarine.SavedSubmarines);
        }

        public static XDocument OpenFile(string file)
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
                Stream stream = null;
                try
                {
                    stream = SaveUtil.DecompressFiletoStream(file);
                }
                catch (Exception e)
                {
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
                    DebugConsole.ThrowError("Loading submarine \"" + file + "\" failed! ("+e.Message+")");
                    return null;
                }
            }
            else if (extension == ".xml")
            {
                try
                {
                    ToolBox.IsProperFilenameCase(file);
                    doc = XDocument.Load(file);
                }

                catch (Exception e)
                {
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

        public void Load(bool unloadPrevious, XElement submarineElement = null)
        {
            if (unloadPrevious) Unload();

            Loading = true;

            if (submarineElement == null)
            {
                XDocument doc = OpenFile(filePath);
                if (doc == null || doc.Root == null) return;

                submarineElement = doc.Root;
            }

            Description = ToolBox.GetAttributeString(submarineElement, "description", "");
            Enum.TryParse(ToolBox.GetAttributeString(submarineElement, "tags", ""), out tags);

            HiddenSubPosition = HiddenSubStartPosition;
            foreach (Submarine sub in Submarine.loaded)
            {
                HiddenSubPosition += Vector2.UnitY * (sub.Borders.Height + 5000.0f);
            }

            IdOffset = 0;
            foreach (MapEntity me in MapEntity.mapEntityList)
            {
                IdOffset = Math.Max(IdOffset, me.ID);
            }

            foreach (XElement element in submarineElement.Elements())
            {
                string typeName = element.Name.ToString();

                Type t;
                try
                {
                    t = Type.GetType("Barotrauma." + typeName, true, true);
                    if (t == null)
                    {
                        DebugConsole.ThrowError("Error in " + filePath + "! Could not find a entity of the type \"" + typeName + "\".");
                        continue;
                    }
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error in " + filePath + "! Could not find a entity of the type \"" + typeName + "\".", e);
                    continue;
                }

                try
                {
                    MethodInfo loadMethod = t.GetMethod("Load");
                    loadMethod.Invoke(t, new object[] { element, this });
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Could not find the method \"Load\" in " + t + ".", e);
                }
            }

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

            subBody = new SubmarineBody(this);
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

            MapEntity.MapLoaded(this);
               
            //WayPoint.GenerateSubWaypoints();

            GameMain.LightManager.OnMapLoaded();

            ID = (ushort)(ushort.MaxValue - Submarine.loaded.IndexOf(this));
        }

        public static Submarine Load(XElement element, bool unloadPrevious)
        {
            if (unloadPrevious) Unload();

            //tryload -> false

            Submarine sub = new Submarine(ToolBox.GetAttributeString(element, "name", ""), "", false);
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

            //Entity.dictionary.Add(int.MaxValue, sub);

            return sub;            
        }

        public static bool Unloading
        {
            get;
            private set;
        }

        public static void Unload()
        {
            Unloading = true;

            Sound.OnGameEnd();

            if (GameMain.LightManager != null) GameMain.LightManager.ClearLights();

            foreach (Submarine sub in loaded)
            {
                sub.Remove();
            }

            loaded.Clear();

            visibleEntities = null;

            if (GameMain.GameScreen.Cam != null) GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;

            Entity.RemoveAll();
            
            PhysicsBody.list.Clear();

            Ragdoll.list.Clear();

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

            DockedTo.Clear();
        }
    }

}
