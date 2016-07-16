using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Barotrauma
{

    class LinkedSubmarinePrefab : MapEntityPrefab
    {
        public readonly Submarine mainSub;
        
        public LinkedSubmarinePrefab(Submarine submarine)
        {
            this.mainSub = submarine;
        }

        protected override void CreateInstance(Rectangle rect)
        {
            System.Diagnostics.Debug.Assert(Submarine.MainSub != null);

            LinkedSubmarine.CreateDummy(Submarine.MainSub, mainSub.FilePath, rect.Location.ToVector2());
        }
    }

    class LinkedSubmarine : MapEntity
    {
        private List<Vector2> wallVertices;

        private string filePath;

        private bool loadSub;
        private Submarine sub;

        private XElement saveElement;

        public override bool IsLinkable
        {
            get
            {
                return true;
            }
        }

        public LinkedSubmarine(Submarine submarine)
            : base(null, submarine) 
        {
            linkedTo = new System.Collections.ObjectModel.ObservableCollection<MapEntity>();
            linkedToID = new List<ushort>();

            InsertToList();
        }

        public static LinkedSubmarine CreateDummy(Submarine mainSub, Submarine linkedSub)
        {
            LinkedSubmarine sl = new LinkedSubmarine(mainSub);
            sl.sub = linkedSub;

            return sl;
        }
        
        public static LinkedSubmarine CreateDummy(Submarine mainSub, string filePath, Vector2 position)
        {
            XDocument doc = Submarine.OpenFile(filePath);
            if (doc == null || doc.Root == null) return null;

            LinkedSubmarine sl = CreateDummy(mainSub, doc.Root, position);
            sl.filePath = filePath;

            return sl;
        }

        public static LinkedSubmarine CreateDummy(Submarine mainSub, XElement element, Vector2 position)
        {
            LinkedSubmarine sl = new LinkedSubmarine(mainSub);
            sl.GenerateWallVertices(element);

            sl.Rect = new Rectangle(
                (int)sl.wallVertices.Min(v => v.X + position.X),
                (int)sl.wallVertices.Max(v => v.Y + position.Y),
                (int)sl.wallVertices.Max(v => v.X + position.X),
                (int)sl.wallVertices.Min(v => v.Y + position.Y));

            int width = sl.rect.Width - sl.rect.X;
            int height = sl.rect.Y - sl.rect.Height;

            sl.rect = new Rectangle((int)position.X, (int)position.Y, 1, 1);

            return sl;
        }

        public override bool IsMouseOn(Vector2 position)
        {
            return Vector2.Distance(position, WorldPosition) < 50.0f;
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing, bool back = true)
        {
            if (!editing || wallVertices == null) return;

            Color color = (isHighlighted) ? Color.Orange : Color.Green;
            if (isSelected) color = Color.Red;
            
            Vector2 pos = Position;

            for (int i = 0; i < wallVertices.Count; i++)
            {
                Vector2 startPos = wallVertices[i] + pos;
                startPos.Y = -startPos.Y;

                Vector2 endPos = wallVertices[(i + 1) % wallVertices.Count] + pos;
                endPos.Y = -endPos.Y;

                GUI.DrawLine(spriteBatch, 
                    startPos, 
                    endPos, 
                    color, 0.0f, 5);
            }

            pos.Y = -pos.Y;
            GUI.DrawLine(spriteBatch, pos + Vector2.UnitY * 50.0f, pos - Vector2.UnitY * 50.0f, color, 0.0f, 5);
            GUI.DrawLine(spriteBatch, pos + Vector2.UnitX * 50.0f, pos - Vector2.UnitX * 50.0f, color, 0.0f, 5);

            Rectangle drawRect = rect;
            drawRect.Y = -rect.Y;
            GUI.DrawRectangle(spriteBatch, drawRect, Color.Red, true);

            foreach (MapEntity e in linkedTo)
            {
                GUI.DrawLine(spriteBatch,
                    new Vector2(WorldPosition.X, -WorldPosition.Y),
                     new Vector2(e.WorldPosition.X, -e.WorldPosition.Y),
                    Color.Red * 0.3f);
            }
        }

        public override void DrawEditing(SpriteBatch spriteBatch, Camera cam)
        {
            if (editingHUD == null || editingHUD.UserData as LinkedSubmarine != this)
            {
                editingHUD = CreateEditingHUD();
            }

            editingHUD.Draw(spriteBatch);
            editingHUD.Update((float)Physics.step);
            
            if (!PlayerInput.LeftButtonClicked() || !PlayerInput.KeyDown(Keys.Space)) return;

            Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);

            foreach (MapEntity entity in mapEntityList)
            {
                if (entity == this || !entity.IsHighlighted || !(entity is Item) || !entity.IsMouseOn(position)) continue;
                if (((Item)entity).GetComponent<DockingPort>() == null) continue;
                if (linkedTo.Contains(entity))
                {
                    linkedTo.Remove(entity);
                }
                else
                {
                    linkedTo.Add(entity);
                }
            }
        }


        private GUIComponent CreateEditingHUD(bool inGame = false)
        {
            int width = 450;
            int x = GameMain.GraphicsWidth / 2 - width / 2, y = 10;
            
            editingHUD = new GUIFrame(new Rectangle(x, y, width, 100), GUI.Style);
            editingHUD.Padding = new Vector4(10, 10, 0, 0);
            editingHUD.UserData = this;

            new GUITextBlock(new Rectangle(0, 0, 100, 20), "Linked submarine", GUI.Style,
                Alignment.TopLeft, Alignment.TopLeft, editingHUD, false, GUI.LargeFont);

            var pathBox = new GUITextBox(new Rectangle(10,30,300,20), GUI.Style, editingHUD);
            pathBox.Font = GUI.SmallFont;
            pathBox.Text = filePath;

            var reloadButton = new GUIButton(new Rectangle(320,30,80,20), "Refresh", GUI.Style, editingHUD);
            reloadButton.OnClicked = Reload;
            reloadButton.UserData = pathBox;

            reloadButton.ToolTip = "Reload the linked submarine from the specified file";

            y += 20;

            if (!inGame)
            {
                new GUITextBlock(new Rectangle(0, 0, 0, 20), "Hold space to link to a docking port",
                    GUI.Style, Alignment.TopRight, Alignment.TopRight, editingHUD, false, GUI.SmallFont);
                y += 25;
                
            }
            return editingHUD;
        }


        private bool Reload(GUIButton button, object obj)
        {
            var pathBox = obj as GUITextBox;

            if (!File.Exists(pathBox.Text))
            {
                new GUIMessageBox("Error", "Submarine file ''" + pathBox.Text + "'' not found!");
                pathBox.Flash(Color.Red);
                pathBox.Text = filePath;
                return false;
            }

            XDocument doc = Submarine.OpenFile(pathBox.Text);
            if (doc == null || doc.Root == null) return false;

            pathBox.Flash(Color.Green);

            GenerateWallVertices(doc.Root);
            saveElement = doc.Root;
            saveElement.Name = "LinkedSubmarine";

            filePath = pathBox.Text;

            return true;
        }

        private void GenerateWallVertices(XElement rootElement)
        {
            List<Vector2> points = new List<Vector2>();

            var wallPrefabs =
                MapEntityPrefab.list.FindAll(mp => (mp is StructurePrefab) && ((StructurePrefab)mp).HasBody);

            foreach (XElement element in rootElement.Elements())
            {
                if (element.Name != "Structure") continue;

                string name = ToolBox.GetAttributeString(element, "name", "");
                if (!wallPrefabs.Any(wp => wp.Name == name)) continue;

                var rect = ToolBox.GetAttributeVector4(element, "rect", Vector4.Zero);
                
                points.Add(new Vector2(rect.X, rect.Y));
                points.Add(new Vector2(rect.X + rect.Z, rect.Y));
                points.Add(new Vector2(rect.X, rect.Y - rect.W));
                points.Add(new Vector2(rect.X + rect.Z, rect.Y - rect.W));
            }

            wallVertices = MathUtils.GiftWrap(points);
        }

        public override XElement Save(XElement parentElement)
        {
            XElement saveElement = null;

            if (sub == null)
            {
                if (this.saveElement == null)
                {
                    var doc = Submarine.OpenFile(filePath);
                    saveElement = doc.Root;

                    saveElement.Name = "LinkedSubmarine";

                    saveElement.Add(new XAttribute("filepath", filePath));
                }
                else
                {
                    saveElement = this.saveElement;
                }


                
                var linkedPort = linkedTo.FirstOrDefault(lt => (lt is Item) && ((Item)lt).GetComponent<DockingPort>() != null);
                if (linkedPort != null)
                {
                    if (saveElement.Attribute("linkedto") != null) saveElement.Attribute("linkedto").Remove();
                    
                    saveElement.Add(new XAttribute("linkedto", linkedPort.ID));
                }
            }
            else
            {

                saveElement = new XElement("LinkedSubmarine");


                sub.SaveToXElement(saveElement);
            }

            if (sub != null)
            {
                if (!sub.DockedTo.Contains(Submarine.MainSub))
                {
                    saveElement.Add(new XAttribute("location", Level.Loaded.Seed));
                    saveElement.Add(new XAttribute("worldpos", ToolBox.Vector2ToString(sub.SubBody.Position)));

                }
                else 
                {
                    if (saveElement.Attribute("location") != null) saveElement.Attribute("location").Remove();
                    if (saveElement.Attribute("worldpos") != null) saveElement.Attribute("worldpos").Remove();
                }
                            
                if (saveElement.Attribute("pos") != null) saveElement.Attribute("pos").Remove();
                saveElement.Add(new XAttribute("pos", ToolBox.Vector2ToString(Position - Submarine.HiddenSubPosition)));
            }



            parentElement.Add(saveElement);

            return saveElement;
        }

        public static void Load(XElement element, Submarine submarine)
        {
            Vector2 pos = ToolBox.GetAttributeVector2(element, "pos", Vector2.Zero);

            LinkedSubmarine linkedSub = null;

            if (Screen.Selected == GameMain.EditMapScreen)
            {
                //string filePath = ToolBox.GetAttributeString(element, "filepath", "");
                
                linkedSub = CreateDummy(submarine, element, pos);
                linkedSub.saveElement = element;
            }
            else
            {
                linkedSub = new LinkedSubmarine(submarine);
                linkedSub.saveElement = element;

                string levelSeed = ToolBox.GetAttributeString(element, "location", "");
                if (!string.IsNullOrWhiteSpace(levelSeed) && GameMain.GameSession.Level != null && GameMain.GameSession.Level.Seed != levelSeed)
                {
                    linkedSub.loadSub = false;
                    return;
                }

                linkedSub.loadSub = true;

                linkedSub.rect.Location = pos.ToPoint();
            }

            linkedSub.filePath = ToolBox.GetAttributeString(element, "filepath", "");

            string linkedToString = ToolBox.GetAttributeString(element, "linkedto", "");
            if (linkedToString != "")
            {
                string[] linkedToIds = linkedToString.Split(',');
                for (int i = 0; i < linkedToIds.Length; i++)
                {
                    linkedSub.linkedToID.Add((ushort)int.Parse(linkedToIds[i]));
                }
            }

        }

        public override void OnMapLoaded()
        {
            if (!loadSub) return;

            sub = Submarine.Load(saveElement, false);            

            Vector2 worldPos = ToolBox.GetAttributeVector2(saveElement, "worldpos", Vector2.Zero);
            if (worldPos != Vector2.Zero)
            {
                sub.SetPosition(worldPos);
            }
            else
            {
                sub.SetPosition(WorldPosition - Submarine.WorldPosition);
                sub.Submarine = Submarine;
            }
            
            
            
            var linkedItem = linkedTo.FirstOrDefault(lt => (lt is Item) && ((Item)lt).GetComponent<DockingPort>() != null);

            if (linkedItem == null) return;
            
            var linkedPort = ((Item)linkedItem).GetComponent<DockingPort>();

            DockingPort myPort = null;
            float closestDistance = 0.0f;

            foreach (DockingPort port in DockingPort.list)
            {
                if (port.Item.Submarine != sub || port.IsHorizontal != linkedPort.IsHorizontal) continue;

                float dist = Vector2.Distance(port.Item.WorldPosition, linkedPort.Item.WorldPosition);
                if (myPort == null || dist < closestDistance)
                {
                    myPort = port;
                    closestDistance = dist;
                }
            }

            if (myPort != null)
            {
                myPort.DockingTarget = linkedPort;
            }            
        }
    }
}
