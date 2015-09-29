using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.ObjectModel;

namespace Subsurface
{
    public enum SpawnType { None, Human, Enemy, Cargo };
    class WayPoint : MapEntity
    {
        public static List<WayPoint> WayPointList = new List<WayPoint>();

        private SpawnType spawnType;

        //characters spawning at the waypoint will be given an ID card with these tags
        private string[] idCardTags;

        //only characters with this job will be spawned at the waypoint
        private JobPrefab assignedJob;

        public SpawnType SpawnType
        {
            get { return spawnType; }
            set { spawnType = value; }
        }

        public string[] IdCardTags
        {
            get { return idCardTags; }
            private set
            {
                idCardTags = value;
                for (int i = 0; i<idCardTags.Length; i++)
                {
                    idCardTags[i] = idCardTags[i].Trim();
                }
            }
        }

        public WayPoint(Rectangle newRect)
        {
            rect = newRect;
            linkedTo = new ObservableCollection<MapEntity>();
            idCardTags = new string[0];

            InsertToList();
            WayPointList.Add(this);
        }


        public override void Draw(SpriteBatch spriteBatch, bool editing)
        {
            if (!editing && !GameMain.DebugDraw) return;

            Point pos = new Point((int)Position.X, (int)Position.Y);

            Color clr = (isSelected) ? Color.Red : Color.LightGreen;
            GUI.DrawRectangle(spriteBatch, new Rectangle(pos.X - rect.Width / 2, -pos.Y - rect.Height / 2, rect.Width, rect.Height), clr, true);

            foreach (MapEntity e in linkedTo)
            {
                GUI.DrawLine(spriteBatch,
                    new Vector2(pos.X, -pos.Y),
                    new Vector2(e.Position.X + e.Rect.Width / 2, -e.Position.Y + e.Rect.Height / 2),
                    Color.Green);
            }
        }

        public override void DrawEditing(SpriteBatch spriteBatch, Camera cam)
        {
            if (editingHUD == null || editingHUD.UserData != this)
            {
                editingHUD = CreateEditingHUD();
            }

            editingHUD.Update((float)Physics.step);
            editingHUD.Draw(spriteBatch);

            if (!PlayerInput.LeftButtonClicked()) return;

            Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);

            foreach (MapEntity e in mapEntityList)
            {                
                if (e.GetType()!=typeof(WayPoint)) continue;
                if (e == this) continue;

                if (!Submarine.RectContains(e.Rect, position)) continue;

                linkedTo.Add(e);
                e.linkedTo.Add(this);
            }
        }

        private bool ChangeSpawnType(GUIButton button, object obj)
        {
            GUITextBlock spawnTypeText = button.Parent as GUITextBlock;

            spawnType += (int)button.UserData;

            if (spawnType > SpawnType.Cargo) spawnType = SpawnType.None;
            if (spawnType < SpawnType.None) spawnType = SpawnType.Enemy;

            spawnTypeText.Text = spawnType.ToString();

            return true;
        }

        private bool EnterIDCardTags(GUITextBox textBox, string text)
        {
            IdCardTags = text.Split(',');
            textBox.Text = text;
            textBox.Color = Color.White;

            return true;
        }

        private bool EnterAssignedJob(GUITextBox textBox, string text)
        {
            string trimmedName = text.ToLower().Trim();
            assignedJob = JobPrefab.List.Find(jp => jp.Name.ToLower() == trimmedName);

            if (assignedJob !=null && trimmedName!="none")
            {
                textBox.Color = Color.White;
                textBox.Text = (assignedJob == null) ? "None" : assignedJob.Name;
            }

            return true;
        }

        private bool TextBoxChanged(GUITextBox textBox, string text)
        {
            textBox.Color = Color.Red;

            return true;
        }

        private GUIComponent CreateEditingHUD(bool inGame = false)
        {
            int width = 500;
            int x = GameMain.GraphicsWidth / 2 - width / 2, y = 10;

            editingHUD = new GUIFrame(new Rectangle(x, y, width, 150), Color.Black * 0.5f);
            editingHUD.Padding = new Vector4(10, 10, 0, 0);
            editingHUD.UserData = this;

            new GUITextBlock(new Rectangle(0, 0, 100, 20), "Editing waypoint", GUI.Style, editingHUD);
            new GUITextBlock(new Rectangle(0, 20, 100, 20), "Hold space to link to another entity", GUI.Style, editingHUD);
            new GUITextBlock(new Rectangle(0, 40, 100, 20), "Spawnpoint: ", GUI.Style, editingHUD);

            var spawnTypeText = new GUITextBlock(new Rectangle(0, 40, 200, 20), spawnType.ToString(), GUI.Style, Alignment.Right, Alignment.TopLeft, editingHUD);

            var button = new GUIButton(new Rectangle(-30,0,20,20), "-", Alignment.Right, GUI.Style, spawnTypeText);
            button.UserData = -1;
            button.OnClicked = ChangeSpawnType;

            button = new GUIButton(new Rectangle(0, 0, 20, 20), "+", Alignment.Right, GUI.Style, spawnTypeText);
            button.UserData = 1;
            button.OnClicked = ChangeSpawnType;

            //spriteBatch.DrawString(GUI.font, "Spawnpoint: " + spawnType.ToString() + " +/-", new Vector2(x, y + 40), Color.Black);

            y = 40+20;                    

            new GUITextBlock(new Rectangle(0, y, 100, 20), "ID Card tags:", Color.Transparent, Color.Black, Alignment.TopLeft, null, editingHUD);
            GUITextBox propertyBox = new GUITextBox(new Rectangle(100, y, 200, 20), GUI.Style, editingHUD);
            propertyBox.Text = string.Join(", ", idCardTags);
            propertyBox.OnEnter = EnterIDCardTags;
            propertyBox.OnTextChanged = TextBoxChanged;
            y = y + 30;

            new GUITextBlock(new Rectangle(0, y, 100, 20), "Assigned job:", Color.Transparent, Color.Black, Alignment.TopLeft, null, editingHUD);
            propertyBox = new GUITextBox(new Rectangle(100, y, 200, 20), GUI.Style, editingHUD);
            propertyBox.Text = (assignedJob == null) ? "None" : assignedJob.Name;

            propertyBox.OnEnter = EnterAssignedJob;
            propertyBox.OnTextChanged = TextBoxChanged;
            y = y + 30;
            
            return editingHUD;
        }

        public static WayPoint GetRandom(SpawnType spawnType = SpawnType.None, Job assignedJob = null)
        {
            List<WayPoint> wayPoints = new List<WayPoint>();

            foreach (WayPoint wp in WayPointList)
            {
                if (spawnType != SpawnType.None && wp.spawnType != spawnType) continue;
                if (assignedJob != null && wp.assignedJob != assignedJob.Prefab) continue;

                wayPoints.Add(wp);
            }

            if (!wayPoints.Any()) return null;

            return wayPoints[Rand.Int(wayPoints.Count())];
        }

        public static WayPoint[] SelectCrewSpawnPoints(List<CharacterInfo> crew)
        {
            List<WayPoint> unassignedWayPoints = new List<WayPoint>();
            foreach (WayPoint wp in WayPointList)
            {
                if (wp.spawnType == SpawnType.Human) unassignedWayPoints.Add(wp);
            }

            WayPoint[] assignedWayPoints = new WayPoint[crew.Count];

            for (int i = 0; i < crew.Count; i++ )
            {
                //try to give the crew member a spawnpoint that hasn't been assigned to anyone and matches their job                
                for (int n = 0; n < unassignedWayPoints.Count; n++)
                {

                    if (crew[i].Job.Prefab != unassignedWayPoints[n].assignedJob) continue;
                    assignedWayPoints[i] = unassignedWayPoints[n];
                    unassignedWayPoints.RemoveAt(n);

                    break;
                }                
            }

            //go through the crewmembers that don't have a spawnpoint yet (if any)
            for (int i = 0; i < crew.Count; i++)
            {
                if (assignedWayPoints[i] != null) continue;

                //try to assign a spawnpoint that matches the job, even if the spawnpoint is already assigned to someone else
                foreach (WayPoint wp in WayPointList)
                {
                    if (wp.spawnType != SpawnType.Human || wp.assignedJob != crew[i].Job.Prefab) continue;

                    assignedWayPoints[i] = wp;
                    break;
                }

                if (assignedWayPoints[i] != null) continue;

                //everything else failed -> just give a random spawnpoint
                assignedWayPoints[i] = GetRandom(SpawnType.Human);
            }

            
            return assignedWayPoints;
        }


        public override XElement Save(XDocument doc)
        {
            if (MoveWithLevel) return null;
            XElement element = new XElement("WayPoint");

            element.Add(new XAttribute("ID", ID),
                new XAttribute("x", rect.X),
                new XAttribute("y", rect.Y),
                new XAttribute("spawn", spawnType));

            if (idCardTags.Length > 0)
            {
                element.Add(new XAttribute("idcardtags", string.Join(",", idCardTags)));
            }

            if (assignedJob != null)
            {
                element.Add(new XAttribute("job", assignedJob.Name));
            }

            doc.Root.Add(element);

            if (linkedTo != null)
            {
                int i = 0;
                foreach (MapEntity e in linkedTo)
                {
                    element.Add(new XAttribute("linkedto" + i, e.ID));
                    i += 1;
                }
            }

            return element;
        }

        public static void Load(XElement element)
        {
            Rectangle rect = new Rectangle(
                int.Parse(element.Attribute("x").Value),
                int.Parse(element.Attribute("y").Value),
                (int)Submarine.GridSize.X, (int)Submarine.GridSize.Y);

            WayPoint w = new WayPoint(rect);

            w.ID = int.Parse(element.Attribute("ID").Value);
            w.spawnType = (SpawnType)Enum.Parse(typeof(SpawnType), 
                ToolBox.GetAttributeString(element, "spawn", "None"));

            string idCardTagString = ToolBox.GetAttributeString(element, "idcardtags", "");
            if (!string.IsNullOrWhiteSpace(idCardTagString))
            {
                w.IdCardTags = idCardTagString.Split(',');
            }

            string jobName = ToolBox.GetAttributeString(element, "job", "").ToLower();
            if (!string.IsNullOrWhiteSpace(jobName))
            {
                w.assignedJob = JobPrefab.List.Find(jp => jp.Name.ToLower() == jobName);
            }

            w.linkedToID = new List<int>();
            int i = 0;
            while (element.Attribute("linkedto" + i) != null)
            {
                w.linkedToID.Add(int.Parse(element.Attribute("linkedto" + i).Value));
                i += 1;
            }
        }

        public override void Remove()
        {
            base.Remove();

            WayPointList.Remove(this);
        }
    
    }
}
