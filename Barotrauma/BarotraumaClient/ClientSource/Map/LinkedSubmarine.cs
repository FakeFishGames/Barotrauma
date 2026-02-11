using System;
using System.Collections.Generic;
using Barotrauma.IO;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    partial class LinkedSubmarinePrefab : MapEntityPrefab
    {
        partial void OnLinkedSubCreated(LinkedSubmarine linkedSub)
        {
            SubEditorScreen.StoreCommand(
                new AddOrDeleteCommand(new List<MapEntity> { linkedSub }, false));
        }
    }

    partial class LinkedSubmarine : MapEntity
    {
        private Sprite previewSprite;

        /// <summary>
        /// Loads the preview image from the saveElement's "previewimage" Base64 attribute,
        /// or from the linked submarine file if the attribute is not present.
        /// </summary>
        private void LoadPreviewImage()
        {
            // Try to load from saveElement first (available when created from file path)
            string previewData = saveElement?.GetAttributeString("previewimage", "") ?? "";

            // If saveElement doesn't have a preview (e.g. received via network sync where it's stripped),
            // try to load from the local submarine file
            if (string.IsNullOrEmpty(previewData) && !string.IsNullOrEmpty(filePath))
            {
                try
                {
                    var doc = SubmarineInfo.OpenFile(filePath);
                    previewData = doc?.Root?.GetAttributeString("previewimage", "") ?? "";
                }
                catch (Exception) { /* file may not exist locally — preview will just not show */ }
            }

            if (string.IsNullOrEmpty(previewData)) { return; }
            try
            {
                using var mem = new System.IO.MemoryStream(Convert.FromBase64String(previewData));
                var texture = TextureLoader.FromStream(mem, compress: false);
                if (texture != null)
                {
                    previewSprite = new Sprite(texture, sourceRectangle: null, newOffset: null);
                }
            }
            catch (Exception e)
            {
                DebugConsole.AddWarning($"[LinkedSub] Failed to load preview image: {e.Message}");
            }
        }

        partial void OnDummyCreated()
        {
            LoadPreviewImage();
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing, bool back = true)
        {
            if (!editing || wallVertices == null) { return; }
            
            Draw(spriteBatch, Position);

            if (!Item.ShowLinks) { return; }

            foreach (MapEntity e in linkedTo)
            {
                bool isLinkAllowed = e is Item item && item.HasTag(Tags.DockingPort);

                GUI.DrawLine(spriteBatch,
                             new Vector2(WorldPosition.X, -WorldPosition.Y),
                             new Vector2(e.WorldPosition.X, -e.WorldPosition.Y),
                             isLinkAllowed ? GUIStyle.Green * 0.5f : GUIStyle.Red * 0.5f, width: 3);
            }
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 drawPos, float alpha = 1.0f)
        {
            Color color = (IsHighlighted) ? GUIStyle.Orange : GUIStyle.Green;
            if (IsSelected) { color = GUIStyle.Red; }

            Vector2 pos = drawPos;

            // Draw preview image scaled to match the real submarine proportions.
            // The preview image covers allEntityBounds (all entities including waypoints).
            // The wallVertices bounding box covers only structures.
            // We compute the scale from image pixels to world units, then position the
            // structure portion of the image to align with the wallVertices box.
            if (previewSprite?.Texture != null && wallVertices != null && wallVertices.Count > 0
                && allEntityBounds.Width > 0 && allEntityBounds.Height > 0)
            {
                float wallMinX = wallVertices.Min(v => v.X);
                float wallMaxX = wallVertices.Max(v => v.X);
                float wallMinY = wallVertices.Min(v => v.Y);
                float wallMaxY = wallVertices.Max(v => v.Y);
                float wallW = wallMaxX - wallMinX;
                float wallH = wallMaxY - wallMinY;
                
                int imgW = previewSprite.Texture.Width;
                int imgH = previewSprite.Texture.Height;
                
                // The image maps allEntityBounds to the full image.
                // Scale: world units per pixel
                float worldPerPixelX = (float)allEntityBounds.Width / imgW;
                float worldPerPixelY = (float)allEntityBounds.Height / imgH;
                // Use uniform scale (should be nearly equal)
                float worldPerPixel = Math.Max(worldPerPixelX, worldPerPixelY);
                
                // Full image size in world units
                float imgWorldW = imgW * worldPerPixel;
                float imgWorldH = imgH * worldPerPixel;
                
                // Center of allEntityBounds in local coordinates
                float allCenterX = allEntityBounds.X + allEntityBounds.Width / 2f;
                float allCenterY = allEntityBounds.Y + allEntityBounds.Height / 2f;
                
                // Center of wall box in local coordinates  
                float wallCenterX = (wallMinX + wallMaxX) / 2f;
                float wallCenterY = (wallMinY + wallMaxY) / 2f;
                
                // Offset from image center to wall center (in world units)
                float offsetX = wallCenterX - allCenterX;
                float offsetY = wallCenterY - allCenterY;
                
                // Draw the full image centered at the wall center minus the offset
                float drawCenterX = pos.X + wallCenterX - offsetX;
                float drawCenterY = -(pos.Y + wallCenterY - offsetY);
                
                var destRect = new Rectangle(
                    (int)(drawCenterX - imgWorldW / 2f),
                    (int)(drawCenterY - imgWorldH / 2f),
                    (int)imgWorldW, (int)imgWorldH);
                spriteBatch.Draw(previewSprite.Texture, destRect, null, Color.White * 0.35f * alpha);
            }

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

            foreach (MapEntity entity in HighlightedEntities)
            {
                if (entity == this|| entity is not Item || !entity.IsMouseOn(position)) { continue; }
                if (((Item)entity).GetComponent<DockingPort>() == null) { continue; }
                if (linkedTo.Contains(entity))
                {
                    linkedTo.Remove(entity);
                    entity.linkedTo.Remove(this);
                }
                else
                {
                    linkedTo.Add(entity);
                    if (!entity.linkedTo.Contains(this)) { entity.linkedTo.Add(this); }
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
                TextManager.Get("LinkedSub"), font: GUIStyle.LargeFont);

            if (!inGame)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform), 
                    TextManager.Get("LinkLinkedSub"), textColor: GUIStyle.Orange, font: GUIStyle.SmallFont);
            }

            var pathContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform), isHorizontal: true);

            string filePath = this.filePath;
            if (filePath.StartsWith("Submarines"))
            {
                //this is the old submarines path, try to find a local mod that has a submarine with this name
                string subName = Path.GetFileNameWithoutExtension(filePath);
                string foundPath = ContentPackageManager.LocalPackages.Concat(ContentPackageManager.VanillaCorePackage.ToEnumerable())
                    .SelectMany(p => p.GetFiles<SubmarineFile>())
                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f.Path.Value).Equals(subName, StringComparison.OrdinalIgnoreCase))
                    ?.Path.Value;
                if (foundPath.IsNullOrEmpty())
                {
                    //no such sub found among the local mods, just guess the correct path
                    foundPath = Path.Combine(ContentPackage.LocalModsDir, subName, $"{subName}.sub");
                }

                filePath = foundPath;
            }
            
            var pathBox = new GUITextBox(new RectTransform(new Vector2(0.75f, 1.0f), pathContainer.RectTransform), filePath, font: GUIStyle.SmallFont);
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
                pathBox.Flash(GUIStyle.Red);
                pathBox.Text = filePath;
                return false;
            }

            XDocument doc = SubmarineInfo.OpenFile(pathBox.Text);
            if (doc == null || doc.Root == null) { return false; }
            doc.Root.SetAttributeValue("filepath", pathBox.Text);

            pathBox.Flash(GUIStyle.Green);

            GenerateWallVertices(doc.Root);
            saveElement = doc.Root;
            saveElement.Name = "LinkedSubmarine";
            CargoCapacity = doc.Root.GetAttributeInt("cargocapacity", 0);
            LoadPreviewImage();

            filePath = pathBox.Text;

            return true;
        }
    }
}
