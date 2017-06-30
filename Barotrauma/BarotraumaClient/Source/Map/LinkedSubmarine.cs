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
    partial class LinkedSubmarine : MapEntity
    {
        public override void Draw(SpriteBatch spriteBatch, bool editing, bool back = true)
        {
            if (!editing || wallVertices == null) return;

            Color color = (isHighlighted) ? Color.Orange : Color.Green;
            if (IsSelected) color = Color.Red;

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

        public override void UpdateEditing(Camera cam)
        {
            if (editingHUD == null || editingHUD.UserData as LinkedSubmarine != this)
            {
                editingHUD = CreateEditingHUD();
            }

            editingHUD.Update((float)Timing.Step);

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

        public override void DrawEditing(SpriteBatch spriteBatch, Camera cam)
        {
            if (editingHUD == null) return;

            editingHUD.Draw(spriteBatch);
        }


        private GUIComponent CreateEditingHUD(bool inGame = false)
        {
            int width = 450;
            int x = GameMain.GraphicsWidth / 2 - width / 2, y = 10;

            editingHUD = new GUIFrame(new Rectangle(x, y, width, 100), "");
            editingHUD.Padding = new Vector4(10, 10, 0, 0);
            editingHUD.UserData = this;

            new GUITextBlock(new Rectangle(0, 0, 100, 20), "Linked submarine", "",
                Alignment.TopLeft, Alignment.TopLeft, editingHUD, false, GUI.LargeFont);

            var pathBox = new GUITextBox(new Rectangle(10, 30, 300, 20), "", editingHUD);
            pathBox.Font = GUI.SmallFont;
            pathBox.Text = filePath;

            var reloadButton = new GUIButton(new Rectangle(320, 30, 80, 20), "Refresh", "", editingHUD);
            reloadButton.OnClicked = Reload;
            reloadButton.UserData = pathBox;

            reloadButton.ToolTip = "Reload the linked submarine from the specified file";

            y += 20;

            if (!inGame)
            {
                new GUITextBlock(new Rectangle(0, 0, 0, 20), "Hold space to link to a docking port",
                    "", Alignment.TopRight, Alignment.TopRight, editingHUD, false, GUI.SmallFont);
                y += 25;

            }
            return editingHUD;
        }

        private bool Reload(GUIButton button, object obj)
        {
            var pathBox = obj as GUITextBox;

            if (!File.Exists(pathBox.Text))
            {
                new GUIMessageBox("Error", "Submarine file \"" + pathBox.Text + "\" not found!");
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

                if (saveElement.Attribute("pos") != null) saveElement.Attribute("pos").Remove();
                saveElement.Add(new XAttribute("pos", ToolBox.Vector2ToString(Position - Submarine.HiddenSubPosition)));



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
                bool leaveBehind = false;
                if (!sub.DockedTo.Contains(Submarine.MainSub))
                {
                    System.Diagnostics.Debug.Assert(Submarine.MainSub.AtEndPosition || Submarine.MainSub.AtStartPosition);
                    if (Submarine.MainSub.AtEndPosition)
                    {
                        leaveBehind = sub.AtEndPosition != Submarine.MainSub.AtEndPosition;
                    }
                    else
                    {
                        leaveBehind = sub.AtStartPosition != Submarine.MainSub.AtStartPosition;
                    }
                }


                if (leaveBehind)
                {
                    saveElement.SetAttributeValue("location", Level.Loaded.Seed);
                    saveElement.SetAttributeValue("worldpos", ToolBox.Vector2ToString(sub.SubBody.Position));

                }
                else
                {
                    if (saveElement.Attribute("location") != null) saveElement.Attribute("location").Remove();
                    if (saveElement.Attribute("worldpos") != null) saveElement.Attribute("worldpos").Remove();
                }

                saveElement.SetAttributeValue("pos", ToolBox.Vector2ToString(Position - Submarine.HiddenSubPosition));
            }



            parentElement.Add(saveElement);

            return saveElement;
        }
    }
}
