using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class LinkedSubmarine : MapEntity
    {
        public override void Draw(SpriteBatch spriteBatch, bool editing, bool back = true)
        {
            if (!editing || wallVertices == null) { return; }
            
            Draw(spriteBatch, Position);

            if (!Item.ShowLinks) { return; }

            foreach (MapEntity e in linkedTo)
            {
                bool isLinkAllowed = e is Item item && item.HasTag("dock");

                GUI.DrawLine(spriteBatch,
                             new Vector2(WorldPosition.X, -WorldPosition.Y),
                             new Vector2(e.WorldPosition.X, -e.WorldPosition.Y),
                             isLinkAllowed ? GUI.Style.Green * 0.5f : GUI.Style.Red * 0.5f, width: 3);
            }
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 drawPos, float alpha = 1.0f)
        {
            Color color = (IsHighlighted) ? GUI.Style.Orange : GUI.Style.Green;
            if (IsSelected) { color = GUI.Style.Red; }

            Vector2 pos = drawPos;

            for (int i = 0; i < wallVertices.Count; i++)
            {
                Vector2 startPos = wallVertices[i] + pos;
                startPos.Y = -startPos.Y;

                Vector2 endPos = wallVertices[(i + 1) % wallVertices.Count] + pos;
                endPos.Y = -endPos.Y;

                GUI.DrawLine(spriteBatch,
                             startPos,
                             endPos,
                             color * alpha, 0.0f, 5);
            }

            pos.Y = -pos.Y;
            GUI.DrawLine(spriteBatch, pos + Vector2.UnitY * 50.0f, pos - Vector2.UnitY * 50.0f, color * alpha, 0.0f, 5);
            GUI.DrawLine(spriteBatch, pos + Vector2.UnitX * 50.0f, pos - Vector2.UnitX * 50.0f, color * alpha, 0.0f, 5);
        }

        public override void UpdateEditing(Camera cam, float deltaTime)
        {
            if (editingHUD == null || editingHUD.UserData as LinkedSubmarine != this)
            {
                editingHUD = CreateEditingHUD();
            }

            editingHUD.UpdateManually(deltaTime);

            if (!PlayerInput.PrimaryMouseButtonClicked() || !PlayerInput.KeyDown(Keys.Space)) { return; }

            Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);

            foreach (MapEntity entity in mapEntityList)
            {
                if (entity == this || !entity.IsHighlighted || !(entity is Item) || !entity.IsMouseOn(position)) { continue; }
                if (((Item)entity).GetComponent<DockingPort>() == null) { continue; }
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
            editingHUD = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.25f), GUI.Canvas, Anchor.CenterRight) { MinSize = new Point(400, 0) }) 
            {
                UserData = this 
            };
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.8f), editingHUD.RectTransform, Anchor.Center))
            {
                Stretch = true,
                AbsoluteSpacing = (int)(GUI.Scale * 5)
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform),
                TextManager.Get("LinkedSub"), font: GUI.LargeFont);

            if (!inGame)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform), 
                    TextManager.Get("LinkLinkedSub"), textColor: GUI.Style.Orange, font: GUI.SmallFont);
            }

            var pathContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform), isHorizontal: true);
            var pathBox = new GUITextBox(new RectTransform(new Vector2(0.75f, 1.0f), pathContainer.RectTransform), filePath, font: GUI.SmallFont);
            var reloadButton = new GUIButton(new RectTransform(new Vector2(0.25f / pathBox.RectTransform.RelativeSize.X, 1.0f), pathBox.RectTransform, Anchor.CenterRight, Pivot.CenterLeft), 
                                             TextManager.Get("ReloadLinkedSub"), style: "GUIButtonSmall")
            {
                OnClicked = Reload,
                UserData = pathBox,
                ToolTip = TextManager.Get("ReloadLinkedSubTooltip")
            };

            editingHUD.RectTransform.Resize(new Point(
                editingHUD.Rect.Width, 
                (int)(paddedFrame.Children.Sum(c => c.Rect.Height + paddedFrame.AbsoluteSpacing) / paddedFrame.RectTransform.RelativeSize.Y)));

            PositionEditingHUD();

            return editingHUD;
        }

        private bool Reload(GUIButton button, object obj)
        {
            var pathBox = obj as GUITextBox;

            if (!File.Exists(pathBox.Text))
            {
                new GUIMessageBox(TextManager.Get("Error"), TextManager.GetWithVariable("ReloadLinkedSubError", "[file]", pathBox.Text));
                pathBox.Flash(GUI.Style.Red);
                pathBox.Text = filePath;
                return false;
            }

            XDocument doc = SubmarineInfo.OpenFile(pathBox.Text);
            if (doc == null || doc.Root == null) return false;
            doc.Root.SetAttributeValue("filepath", pathBox.Text);

            pathBox.Flash(GUI.Style.Green);

            GenerateWallVertices(doc.Root);
            saveElement = doc.Root;
            saveElement.Name = "LinkedSubmarine";

            filePath = pathBox.Text;

            return true;
        }
    }
}
