#nullable enable
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    class EditorImageManager
    {
        private struct EditorImageContainer
        {
            public float Rotation;
            public float Scale;
            public Vector2 Position;
            public string Path;
            public float Opacity;
            public EditorImage.DrawTargetType DrawTarget;

            public EditorImage CreateImage()
            {
                return new EditorImage(Path, Position)
                {
                    Position = Position,
                    Scale = Scale,
                    Opacity = Opacity,
                    Rotation = Rotation,
                    DrawTarget = DrawTarget
                };
            }

            public static EditorImageContainer? Load(XElement element)
            {
                string path = element.GetAttributeString("path", "");
                if (string.IsNullOrWhiteSpace(path)) { return null; }

                Vector2 pos = element.GetAttributeVector2("pos", Vector2.Zero);
                float scale = element.GetAttributeFloat("scale", 1f);
                float rotation = element.GetAttributeFloat("rotation", 0f);
                float opacity = element.GetAttributeFloat("opacity", 1f);
                string drawTargetString = element.GetAttributeString("drawtarget", "");
                if (!Enum.TryParse<EditorImage.DrawTargetType>(drawTargetString, out var drawTarget))
                {
                    drawTarget = EditorImage.DrawTargetType.World;
                }

                return new EditorImageContainer
                {
                    Path = path,
                    Rotation = rotation,
                    Opacity = opacity,
                    Position = pos,
                    Scale = scale,
                    DrawTarget = drawTarget
                };
            }

            public static EditorImageContainer ImageToContainer(EditorImage img)
            {
                return new EditorImageContainer
                {
                    Path = img.ImagePath,
                    Rotation = img.Rotation,
                    Position = img.Position,
                    Opacity = img.Opacity,
                    Scale = img.Scale,
                    DrawTarget = img.DrawTarget
                };
            }

            public static XElement SerializeImage(EditorImageContainer image)
            {
                return new XElement("image",
                    new XAttribute("pos", XMLExtensions.Vector2ToString(image.Position)),
                    new XAttribute("rotation", image.Rotation),
                    new XAttribute("opacity", image.Opacity),
                    new XAttribute("path", image.Path),
                    new XAttribute("scale", image.Scale),
                    new XAttribute("drawtarget", image.DrawTarget.ToString()));
            }
        }

        private readonly List<EditorImageContainer> PendingImages = new List<EditorImageContainer>();

        public readonly List<EditorImage> Images = new List<EditorImage>();

        private readonly List<EditorImage> screenImages = new List<EditorImage>(),
                                           worldImages = new List<EditorImage>();

        public bool EditorMode;

        private string editModeText = "";
        private Vector2 textSize = Vector2.Zero;

        public void Save(XElement element)
        {
            XElement saveElement = new XElement("editorimages");
            foreach (EditorImage image in Images)
            {
                EditorImageContainer container = EditorImageContainer.ImageToContainer(image);
                saveElement.Add(EditorImageContainer.SerializeImage(container));
            }

            foreach (EditorImageContainer container in PendingImages)
            {
                saveElement.Add(EditorImageContainer.SerializeImage(container));
            }

            element.Add(saveElement);
        }

        public void Load(XElement element)
        {
            Clear(alsoPending: true);

            foreach (XElement subElement in element.Elements())
            {
                EditorImageContainer? tempImage = EditorImageContainer.Load(subElement);
                if (tempImage != null)
                {
                    PendingImages.Add(tempImage.Value);
                }
            }
        }

        public void OnEditorSelected()
        {
            editModeText = TextManager.Get("SubEditor.ImageEditingMode");
            textSize = GUI.LargeFont.MeasureString(editModeText);

            TryLoadPendingImages();
        }

        private void TryLoadPendingImages()
        {
            if (PendingImages.Count == 0) { return; }

            Clear(alsoPending: false);

            foreach (EditorImageContainer pendingImage in PendingImages)
            {
                EditorImage img = pendingImage.CreateImage();
                if (img.Image == null) { continue; }
                Images.Add(img);
                img.UpdateRectangle();
            }

            UpdateImageCategories();
            PendingImages.Clear();
        }

        public void Clear(bool alsoPending = false)
        {
            foreach (EditorImage img in Images)
            {
                img.Image?.Dispose();
            }

            Images.Clear();
            screenImages.Clear();
            worldImages.Clear();
            if (alsoPending)
            {
                PendingImages.Clear();
            }
        }

        public void Update(float deltaTime)
        {
            if (!EditorMode) { return; }

            foreach (EditorImage image in Images)
            {
                image.Update(deltaTime);
            }

            if (PlayerInput.PrimaryMouseButtonDown())
            {
                EditorImage? hover = Images.FirstOrDefault(img => img.IsMouseOn());
                if (hover != null)
                {
                    foreach (EditorImage image in Images)
                    {
                        image.Selected = false;
                    }

                    hover.Selected = true;
                }
            }

            if (PlayerInput.KeyHit(Keys.Delete) || (PlayerInput.IsCtrlDown() && PlayerInput.KeyHit(Keys.D)))
            {
                Images.RemoveAll(img => img.Selected);
                UpdateImageCategories();
            }

            if (PlayerInput.KeyHit(Keys.Space))
            {
                foreach (EditorImage image in Images)
                {
                    if (image.Selected)
                    {
                        if (image.DrawTarget == EditorImage.DrawTargetType.World)
                        {
                            Vector2 pos = image.Position;
                            pos.Y = -pos.Y;
                            pos = Screen.Selected.Cam.WorldToScreen(pos);
                            if (PlayerInput.IsShiftDown())
                            {
                                pos = new Vector2(GameMain.GraphicsWidth / 2f, GameMain.GraphicsHeight / 2f);
                            }

                            image.Position = pos;
                            image.DrawTarget = EditorImage.DrawTargetType.Camera;
                            image.Scale *= Screen.Selected.Cam.Zoom;
                            image.UpdateRectangle();
                        }
                        else
                        {
                            Vector2 pos = Screen.Selected.Cam.ScreenToWorld(image.Position);
                            pos.Y = -pos.Y;
                            image.Position = pos;
                            image.DrawTarget = EditorImage.DrawTargetType.World;
                            image.Scale /= Screen.Selected.Cam.Zoom;
                            image.UpdateRectangle();
                        }
                    }
                }

                UpdateImageCategories();
            }

            MapEntity.DisableSelect = true;
        }

        private void UpdateImageCategories()
        {
            screenImages.Clear();
            worldImages.Clear();

            foreach (EditorImage image in Images)
            {
                switch (image.DrawTarget)
                {
                    case EditorImage.DrawTargetType.World:
                        worldImages.Add(image);
                        break;
                    default:
                        screenImages.Add(image);
                        break;
                }
            }
        }

        public void CreateImageWizard()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!Directory.Exists(home)) { return; }

            FileSelection.OnFileSelected = file =>
            {
                Vector2 pos = Screen.Selected.Cam.ScreenToWorld(PlayerInput.MousePosition);
                pos.Y = -pos.Y;
                Images.Add(new EditorImage(file, pos) { DrawTarget = EditorImage.DrawTargetType.World });
                UpdateImageCategories();
                GameMain.Config.SaveNewPlayerConfig();
            };

            FileSelection.ClearFileTypeFilters();
            FileSelection.AddFileTypeFilter("PNG", "*.png");
            FileSelection.AddFileTypeFilter("JPEG", "*.jpg, *.jpeg");
            FileSelection.AddFileTypeFilter("All files", "*.*");
            FileSelection.SelectFileTypeFilter("*.png");
            FileSelection.CurrentDirectory = home;
            FileSelection.Open = true;
        }

        public void DrawEditing(SpriteBatch spriteBatch, Camera cam)
        {
            if (!EditorMode) { return; }

            DrawImages(spriteBatch, cam);

            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState);
            Vector2 textPos = new Vector2(GameMain.GraphicsWidth / 2f - (textSize.X / 2f), GameMain.GraphicsHeight / 10f - (textSize.Y / 2f));
            GUI.DrawString(spriteBatch, textPos, editModeText, GUI.Style.Yellow, Color.Black * 0.4f, 8, GUI.LargeFont);
            spriteBatch.End();
        }

        public void Draw(SpriteBatch spriteBatch, Camera cam)
        {
            if (EditorMode) { return; }

            DrawImages(spriteBatch, cam);
        }

        private void DrawImages(SpriteBatch spriteBatch, Camera cam)
        {
            if (screenImages.Count > 0)
            {
                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState);
                foreach (EditorImage image in screenImages)
                {
                    image.Draw(spriteBatch);
                    if (EditorMode) { image.DrawEditing(spriteBatch, cam); }
                }

                spriteBatch.End();
            }

            if (worldImages.Count > 0)
            {
                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, transformMatrix: cam.Transform);
                foreach (EditorImage image in worldImages)
                {
                    image.Draw(spriteBatch);
                    if (EditorMode) { image.DrawEditing(spriteBatch, cam); }
                }

                spriteBatch.End();
            }
        }
    }

    class EditorImage
    {
        public enum DrawTargetType
        {
            Camera,
            World
        }

        public Texture2D? Image;
        public string ImagePath;
        public Vector2 Position;
        public float Rotation;
        public float Opacity = 1f;
        public float Scale = 1f;
        public DrawTargetType DrawTarget;
        public bool Selected;

        public Rectangle Bounds;
        private float prevAngle;
        private bool disableMove;
        private bool isDragging;

        private readonly Dictionary<string, Widget> widgets = new Dictionary<string, Widget>();

        public EditorImage(string path, Vector2 pos)
        {
            Image = Sprite.LoadTexture(path, out Sprite _, compress: false);
            ImagePath = path;
            Position = pos;
            UpdateRectangle();
        }

        public bool IsMouseOn() => Bounds.Contains(GetMousePos());

        public Vector2 GetMousePos()
        {
            switch (DrawTarget)
            {
                case DrawTargetType.Camera:
                    return PlayerInput.MousePosition;
                case DrawTargetType.World:
                    Vector2 pos = Screen.Selected.Cam.ScreenToWorld(PlayerInput.MousePosition);
                    pos.Y = -pos.Y;
                    return pos;
                default:
                    return PlayerInput.MousePosition;
            }
        }

        public void Update(float deltaTime)
        {
            if (!Selected) { return; }

            if (widgets.Values.Any(w => w.IsSelected)) { return; }

            if (PlayerInput.PrimaryMouseButtonDown() && !disableMove && IsMouseOn())
            {
                isDragging = true;
            }

            if (isDragging)
            {
                Camera cam = Screen.Selected.Cam;
                if (PlayerInput.MouseSpeed != Vector2.Zero)
                {
                    Vector2 mouseSpeed = PlayerInput.MouseSpeed;
                    if (DrawTarget == DrawTargetType.World)
                    {
                        mouseSpeed /= cam.Zoom;
                    }

                    Position += mouseSpeed;
                    UpdateRectangle();
                }
            }

            if (PlayerInput.KeyDown(Keys.OemPlus) || PlayerInput.KeyDown(Keys.Up))
            {
                Opacity += 0.01f;
            }

            if (PlayerInput.KeyDown(Keys.OemMinus) || PlayerInput.KeyDown(Keys.Down))
            {
                Opacity -= 0.01f;
            }
            
            if (PlayerInput.KeyHit(Keys.D0))
            {
                Opacity = 1f;
            }

            Opacity = Math.Clamp(Opacity, 0, 1f);

            if (!PlayerInput.PrimaryMouseButtonHeld())
            {
                isDragging = false;
            }
        }

        private void DrawWidgets(SpriteBatch spriteBatch)
        {
            float widgetSize = Image == null ? 100f : Math.Max(Image.Width, Image.Height) / 2f;

            int width = 3;
            int size = 32;
            if (DrawTarget == DrawTargetType.World)
            {
                width = Math.Max(width, (int) (width / Screen.Selected.Cam.Zoom));
            }

            Widget currentWidget = GetWidget("transform", size, width, widget =>
            {
                widget.MouseDown += () =>
                {
                    widget.color = GUI.Style.Green;
                    prevAngle = Rotation;
                    disableMove = true;
                };
                widget.Deselected += () =>
                {
                    widget.color = Color.Yellow;
                    disableMove = false;
                };
                widget.MouseHeld += (deltaTime) =>
                {
                    Rotation = GetRotationAngle(Position) + (float) Math.PI / 2f;
                    float distance = Vector2.Distance(Position, GetMousePos());
                    Scale = Math.Abs(distance) / widgetSize;
                    if (PlayerInput.IsShiftDown())
                    {
                        const float rotationStep = (float) (Math.PI / 4f);
                        Rotation = (float) Math.Round(Rotation / rotationStep) * rotationStep;
                    }

                    if (PlayerInput.IsCtrlDown())
                    {
                        const float scaleStep = 0.1f;
                        Scale = (float) Math.Round(Scale / scaleStep) * scaleStep;
                    }

                    UpdateRectangle();
                };
                widget.PreUpdate += (deltaTime) =>
                {
                    if (DrawTarget != DrawTargetType.World) { return; }

                    widget.DrawPos = new Vector2(widget.DrawPos.X, -widget.DrawPos.Y);
                    widget.DrawPos = Screen.Selected.Cam.WorldToScreen(widget.DrawPos);
                };
                widget.PostUpdate += (deltaTime) =>
                {
                    if (DrawTarget != DrawTargetType.World) { return; }

                    widget.DrawPos = Screen.Selected.Cam.ScreenToWorld(widget.DrawPos);
                    widget.DrawPos = new Vector2(widget.DrawPos.X, -widget.DrawPos.Y);
                };
                widget.PreDraw += (sprtBtch, deltaTime) =>
                {
                    widget.tooltip = $"Scale: {Math.Round(Scale, 2)}\n" +
                                     $"Rotation: {(int) MathHelper.ToDegrees(Rotation)}";
                    float rotation = Rotation - (float) Math.PI / 2f;
                    widget.DrawPos = Position + new Vector2((float) Math.Cos(rotation), (float) Math.Sin(rotation)) * (Scale * widgetSize);
                    widget.Update(deltaTime);
                };
            });

            currentWidget.Draw(spriteBatch, (float) Timing.Step);
            GUI.DrawLine(spriteBatch, Position, currentWidget.DrawPos, GUI.Style.Green, width: width);
        }

        private float GetRotationAngle(Vector2 drawPosition)
        {
            Vector2 rotationVector = GetMousePos() - drawPosition;
            rotationVector.Normalize();
            double angle = Math.Atan2(MathHelper.ToRadians(rotationVector.Y), MathHelper.ToRadians(rotationVector.X));
            if (angle < 0)
            {
                angle = Math.Abs(angle - prevAngle) < Math.Abs((angle + Math.PI * 2) - prevAngle) ? angle : angle + Math.PI * 2;
            }
            else if (angle > 0)
            {
                angle = Math.Abs(angle - prevAngle) < Math.Abs((angle - Math.PI * 2) - prevAngle) ? angle : angle - Math.PI * 2;
            }

            angle = MathHelper.Clamp((float) angle, -((float) Math.PI * 2), (float) Math.PI * 2);
            prevAngle = (float) angle;
            return (float) angle;
        }

        private Widget GetWidget(string id, int size, float thickness = 1f, Action<Widget>? initMethod = null)
        {
            if (!widgets.TryGetValue(id, out Widget? widget))
            {
                widget = new Widget(id, size, Widget.Shape.Rectangle)
                {
                    color = Color.Yellow,
                    RequireMouseOn = false
                };
                widgets.Add(id, widget);
                initMethod?.Invoke(widget);
            }

            widget.size = size;
            widget.thickness = thickness;
            return widget;
        }

        public void UpdateRectangle()
        {
            if (Image == null)
            {
                Bounds = new Rectangle((int) Position.X, (int) Position.Y, 512, 512);
                return;
            }

            Vector2 size = new Vector2(Image.Width * Scale, Image.Height * Scale);
            Bounds = new Rectangle((Position - size / 2f).ToPoint(), size.ToPoint());
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (Image == null) { return; }

            spriteBatch.Draw(Image, Position, null, Color.White * Opacity, Rotation, new Vector2(Image.Width / 2f, Image.Height / 2f), scale: Scale, SpriteEffects.None, 0f);
        }

        public void DrawEditing(SpriteBatch spriteBatch, Camera cam)
        {
            Rectangle bounds = Bounds;
            int width = 4;
            if (DrawTarget == DrawTargetType.World)
            {
                width = (int) (width / cam.Zoom);
            }

            GUI.DrawRectangle(spriteBatch, bounds, Selected ? GUI.Style.Red : GUI.Style.Green, thickness: width);
            if (Selected)
            {
                DrawWidgets(spriteBatch);
            }
        }
    }
}