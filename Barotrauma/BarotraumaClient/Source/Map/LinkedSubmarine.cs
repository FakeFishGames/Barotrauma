using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.IO;
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

            new GUITextBlock(new Rectangle(0, 0, 100, 20), TextManager.Get("LinkedSub"), "",
                Alignment.TopLeft, Alignment.TopLeft, editingHUD, false, GUI.LargeFont);

            var pathBox = new GUITextBox(new Rectangle(10, 30, 300, 20), "", editingHUD);
            pathBox.Font = GUI.SmallFont;
            pathBox.Text = filePath;

            var reloadButton = new GUIButton(new Rectangle(320, 30, 80, 20), TextManager.Get("ReloadLinkedSub"), "", editingHUD);
            reloadButton.OnClicked = Reload;
            reloadButton.UserData = pathBox;
            reloadButton.ToolTip = TextManager.Get("ReloadLinkedSubTooltip");

            y += 20;

            if (!inGame)
            {
                new GUITextBlock(new Rectangle(0, 0, 0, 20), TextManager.Get("LinkLinkedSub"),
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
                new GUIMessageBox(TextManager.Get("Error"), TextManager.Get("ReloadLinkedSubError").Replace("[file]", pathBox.Text));
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
    }
}
