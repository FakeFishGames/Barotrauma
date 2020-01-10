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

            Color color = (IsHighlighted) ? Color.Orange : Color.Green;
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

            if (!Item.ShowLinks) return;

            foreach (MapEntity e in linkedTo)
            {
                bool isLinkAllowed = e is Item item && item.HasTag("dock");

                GUI.DrawLine(spriteBatch,
                    new Vector2(WorldPosition.X, -WorldPosition.Y),
                     new Vector2(e.WorldPosition.X, -e.WorldPosition.Y),
                    isLinkAllowed ? Color.LightGreen * 0.5f : Color.Red * 0.5f, width: 3);
            }
        }

        public override void UpdateEditing(Camera cam)
        {
            if (editingHUD == null || editingHUD.UserData as LinkedSubmarine != this)
            {
                editingHUD = CreateEditingHUD();
            }

            editingHUD.UpdateManually((float)Timing.Step);

            if (!PlayerInput.PrimaryMouseButtonClicked() || !PlayerInput.KeyDown(Keys.Space)) return;

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
            int width = 450, height = 120;
            int x = GameMain.GraphicsWidth / 2 - width / 2, y = 30;

            editingHUD = new GUIFrame(new RectTransform(new Point(width, height), GUI.Canvas) { ScreenSpaceOffset = new Point(x, y) })
            {
                UserData = this
            };

            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.8f), editingHUD.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform),
                TextManager.Get("LinkedSub"), font: GUI.LargeFont);

            if (!inGame)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform), 
                    TextManager.Get("LinkLinkedSub"), textColor: Color.Yellow, font: GUI.SmallFont);
            }

            var pathContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform), isHorizontal: true);

            var pathBox = new GUITextBox(new RectTransform(new Vector2(0.8f, 1.0f), pathContainer.RectTransform), filePath, font: GUI.SmallFont);
            var reloadButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), pathContainer.RectTransform), TextManager.Get("ReloadLinkedSub"))
            {
                OnClicked = Reload,
                UserData = pathBox,
                ToolTip = TextManager.Get("ReloadLinkedSubTooltip")
            };

            PositionEditingHUD();

            return editingHUD;
        }

        private bool Reload(GUIButton button, object obj)
        {
            var pathBox = obj as GUITextBox;

            if (!File.Exists(pathBox.Text))
            {
                new GUIMessageBox(TextManager.Get("Error"), TextManager.GetWithVariable("ReloadLinkedSubError", "[file]", pathBox.Text));
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
