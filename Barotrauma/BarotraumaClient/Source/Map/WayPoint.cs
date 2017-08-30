using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    partial class WayPoint : MapEntity
    {
        private static Texture2D iconTexture;
        private const int IconSize = 32;
        private static int[] iconIndices = { 3, 0, 1, 2 };

        public override void Draw(SpriteBatch spriteBatch, bool editing, bool back = true)
        {
            if (!editing && !GameMain.DebugDraw) return;

            if (IsHidden()) return;

            //Rectangle drawRect =
            //    Submarine == null ? rect : new Rectangle((int)(Submarine.DrawPosition.X + rect.X), (int)(Submarine.DrawPosition.Y + rect.Y), rect.Width, rect.Height);

            Vector2 drawPos = Position;
            if (Submarine != null) drawPos += Submarine.DrawPosition;
            drawPos.Y = -drawPos.Y;

            Color clr = currentHull == null ? Color.Blue : Color.White;
            if (IsSelected) clr = Color.Red;
            if (isHighlighted) clr = Color.DarkRed;

            int iconX = iconIndices[(int)spawnType] * IconSize % iconTexture.Width;
            int iconY = (int)(Math.Floor(iconIndices[(int)spawnType] * IconSize / (float)iconTexture.Width)) * IconSize;

            int iconSize = ConnectedGap == null && Ladders == null ? IconSize : (int)(IconSize * 1.5f);

            spriteBatch.Draw(iconTexture,
                new Rectangle((int)(drawPos.X - iconSize / 2), (int)(drawPos.Y - iconSize / 2), iconSize, iconSize),
                new Rectangle(iconX, iconY, IconSize, IconSize), clr);

            //GUI.DrawRectangle(spriteBatch, new Rectangle(drawRect.X, -drawRect.Y, rect.Width, rect.Height), clr, true);

            //GUI.SmallFont.DrawString(spriteBatch, Position.ToString(), new Vector2(Position.X, -Position.Y), Color.White);

            foreach (MapEntity e in linkedTo)
            {
                GUI.DrawLine(spriteBatch,
                    drawPos,
                    new Vector2(e.DrawPosition.X, -e.DrawPosition.Y),
                    Color.Green);
            }
        }

        private bool IsHidden()
        {
            if (spawnType == SpawnType.Path)
            {
                return (!GameMain.DebugDraw && !ShowWayPoints);
            }
            else
            {
                return (!GameMain.DebugDraw && !ShowSpawnPoints);
            }
        }

        public override void UpdateEditing(Camera cam)
        {
            if (editingHUD == null || editingHUD.UserData != this)
            {
                editingHUD = CreateEditingHUD();
            }

            editingHUD.Update((float)Timing.Step);

            if (PlayerInput.LeftButtonClicked())
            {
                Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);

                foreach (MapEntity e in mapEntityList)
                {
                    if (e.GetType() != typeof(WayPoint)) continue;
                    if (e == this) continue;

                    if (!Submarine.RectContains(e.Rect, position)) continue;

                    linkedTo.Add(e);
                    e.linkedTo.Add(this);
                }
            }
        }

        public override void DrawEditing(SpriteBatch spriteBatch, Camera cam)
        {
            if (editingHUD != null) editingHUD.Draw(spriteBatch);
        }

        private bool ChangeSpawnType(GUIButton button, object obj)
        {
            GUITextBlock spawnTypeText = button.Parent as GUITextBlock;

            spawnType += (int)button.UserData;

            if (spawnType > SpawnType.Cargo) spawnType = SpawnType.Human;
            if (spawnType < SpawnType.Human) spawnType = SpawnType.Cargo;

            spawnTypeText.Text = spawnType.ToString();

            return true;
        }

        private bool EnterIDCardTags(GUITextBox textBox, string text)
        {
            IdCardTags = text.Split(',');
            textBox.Text = text;
            textBox.Color = Color.Green;

            textBox.Deselect();

            return true;
        }

        private bool EnterAssignedJob(GUITextBox textBox, string text)
        {
            string trimmedName = text.ToLowerInvariant().Trim();
            assignedJob = JobPrefab.List.Find(jp => jp.Name.ToLowerInvariant() == trimmedName);

            if (assignedJob != null && trimmedName != "none")
            {
                textBox.Color = Color.Green;
                textBox.Text = (assignedJob == null) ? "None" : assignedJob.Name;
            }

            textBox.Deselect();

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
            int height = spawnType == SpawnType.Path ? 100 : 140;
            int x = GameMain.GraphicsWidth / 2 - width / 2, y = 10;

            editingHUD = new GUIFrame(new Rectangle(x, y, width, height), Color.Black * 0.5f);
            editingHUD.Padding = new Vector4(10, 10, 0, 0);
            editingHUD.UserData = this;

            if (spawnType == SpawnType.Path)
            {
                new GUITextBlock(new Rectangle(0, 0, 100, 20), "Editing waypoint", "", editingHUD);
                new GUITextBlock(new Rectangle(0, 20, 100, 20), "Hold space to link to another waypoint", "", editingHUD);
            }
            else
            {
                new GUITextBlock(new Rectangle(0, 0, 100, 20), "Editing spawnpoint", "", editingHUD);
                new GUITextBlock(new Rectangle(0, 25, 100, 20), "Spawn type: ", "", editingHUD);

                var spawnTypeText = new GUITextBlock(new Rectangle(0, 25, 200, 20), spawnType.ToString(), "", Alignment.Right, Alignment.TopLeft, editingHUD);

                var button = new GUIButton(new Rectangle(-30, 0, 20, 20), "-", Alignment.Right, "", spawnTypeText);
                button.UserData = -1;
                button.OnClicked = ChangeSpawnType;

                button = new GUIButton(new Rectangle(0, 0, 20, 20), "+", Alignment.Right, "", spawnTypeText);
                button.UserData = 1;
                button.OnClicked = ChangeSpawnType;

                y = 40 + 20;

                new GUITextBlock(new Rectangle(0, y, 100, 20), "ID Card tags:", Color.Transparent, Color.White, Alignment.TopLeft, null, editingHUD);
                GUITextBox propertyBox = new GUITextBox(new Rectangle(100, y, 200, 20), "", editingHUD);
                propertyBox.Text = string.Join(", ", idCardTags);
                propertyBox.OnEnterPressed = EnterIDCardTags;
                propertyBox.OnTextChanged = TextBoxChanged;
                propertyBox.ToolTip = "Characters spawning at this spawnpoint will have the specified tags added to their ID card. You can, for example, use these tags to limit access to some parts of the sub.";

                y = y + 30;

                new GUITextBlock(new Rectangle(0, y, 100, 20), "Assigned job:", Color.Transparent, Color.White, Alignment.TopLeft, null, editingHUD);
                propertyBox = new GUITextBox(new Rectangle(100, y, 200, 20), "", editingHUD);
                propertyBox.Text = (assignedJob == null) ? "None" : assignedJob.Name;
                propertyBox.OnEnterPressed = EnterAssignedJob;
                propertyBox.OnTextChanged = TextBoxChanged;
                propertyBox.ToolTip = "Only characters with the specified job will spawn at this spawnpoint.";

            }
            
            y = y + 30;

            return editingHUD;
        }        
    }
}
