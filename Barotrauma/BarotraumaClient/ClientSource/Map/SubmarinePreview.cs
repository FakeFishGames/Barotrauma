using Barotrauma.Extensions;
using Barotrauma.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    sealed class SubmarinePreview : IDisposable
    {
        private readonly SubmarineInfo submarineInfo;

        private SpriteRecorder spriteRecorder;
        private Camera camera;
        private Task loadTask;
        private (Vector2 Min, Vector2 Max) bounds;

        private volatile bool isDisposed;

        private GUIFrame previewFrame;

        private sealed class HullCollection
        {
            public readonly List<Rectangle> Rects;
            public readonly LocalizedString Name;

            public HullCollection(Identifier identifier)
            {
                Rects = new List<Rectangle>();
                Name = TextManager.Get(identifier).Fallback(identifier.Value);
            }

            public void AddRect(XElement element)
            {
                Rectangle rect = element.GetAttributeRect("rect", Rectangle.Empty);
                rect.Y = -rect.Y;
                Rects.Add(rect);
            }
        }

        private struct Door
        {
            public readonly Rectangle Rect;

            public Door(Rectangle rect)
            {
                rect.Y = -rect.Y;
                Rect = rect;
            }
        }

        private readonly Dictionary<Identifier,HullCollection> hullCollections;
        private readonly List<Door> doors;

        private static SubmarinePreview instance = null;

        public static void Create(SubmarineInfo submarineInfo)
        {
            Close();
            instance = new SubmarinePreview(submarineInfo);
        }

        public static void Close()
        {
            instance?.Dispose(); instance = null;
        }

        private SubmarinePreview(SubmarineInfo subInfo)
        {
            camera = new Camera();
            submarineInfo = subInfo;
            spriteRecorder = new SpriteRecorder();
            isDisposed = false;
            loadTask = null;

            hullCollections = new Dictionary<Identifier, HullCollection>();
            doors = new List<Door>();

            previewFrame = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: null);
            new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, previewFrame.RectTransform, Anchor.Center), style: "GUIBackgroundBlocker");

            new GUIButton(new RectTransform(Vector2.One, previewFrame.RectTransform), "", style: null)
            {
                OnClicked = (btn, obj) => { Dispose(); return false; }
            };

            var innerFrame = new GUIFrame(new RectTransform(Vector2.One * 0.9f, previewFrame.RectTransform, Anchor.Center));
            int innerPadding = GUI.IntScale(100f);
            var innerPadded = new GUIFrame(new RectTransform(new Point(innerFrame.Rect.Width - innerPadding, innerFrame.Rect.Height - innerPadding), previewFrame.RectTransform, Anchor.Center), style: null)
            {
                OutlineColor = Color.Black,
                OutlineThickness = 2
            };

            GUITextBlock titleText = null;
            GUIListBox specsContainer = null;

            new GUICustomComponent(new RectTransform(Vector2.One, innerPadded.RectTransform, Anchor.Center),
                (spriteBatch, component) => 
                {
                    if (isDisposed) { return; }
                    camera.UpdateTransform(interpolate: true, updateListener: false);
                    Rectangle drawRect = new Rectangle(component.Rect.X + 1, component.Rect.Y + 1, component.Rect.Width - 2, component.Rect.Height - 2);
                    RenderSubmarine(spriteBatch, drawRect, component);
                },
                (deltaTime, component) => 
                {
                    if (isDisposed) { return; }
                    bool isMouseOnComponent = GUI.MouseOn == component;
                    camera.MoveCamera(deltaTime, allowZoom: isMouseOnComponent, followSub: false);
                    if (isMouseOnComponent &&
                        (PlayerInput.MidButtonHeld() || PlayerInput.LeftButtonHeld()))
                    {
                        Vector2 moveSpeed = PlayerInput.MouseSpeed * (float)deltaTime * 60.0f / camera.Zoom;
                        moveSpeed.X = -moveSpeed.X;
                        camera.Position += moveSpeed;
                    }
                    
                    if (titleText != null && specsContainer != null)
                    {
                        specsContainer.Visible = GUI.IsMouseOn(titleText);
                    }
                    if (PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.Escape))
                    {
                        Dispose();
                    }
                });

            var topContainer = new GUIFrame(new RectTransform(new Vector2(1f, 0.07f), innerPadded.RectTransform, Anchor.TopLeft), style: null)
            {
                Color = Color.Black * 0.65f
            };
            var topLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.97f, 5f / 7f), topContainer.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft);

            titleText = new GUITextBlock(new RectTransform(new Vector2(0.95f, 1f), topLayout.RectTransform), subInfo.DisplayName, font: GUIStyle.LargeFont);
            new GUIButton(new RectTransform(new Vector2(0.05f, 1f), topLayout.RectTransform), TextManager.Get("Close"))
            {
                OnClicked = (btn, obj) => { Dispose(); return false; }
            };

            specsContainer = new GUIListBox(new RectTransform(new Vector2(0.4f, 1f), innerPadded.RectTransform, Anchor.TopLeft) { RelativeOffset = new Vector2(0.015f, 0.07f) })
            {
                CurrentSelectMode = GUIListBox.SelectMode.None,
                Color = Color.Black * 0.65f,
                ScrollBarEnabled = false,
                ScrollBarVisible = false,
                Spacing = GUI.IntScale(5)
            };
            subInfo.CreateSpecsWindow(specsContainer, GUIStyle.Font, includeTitle: false, includeDescription: true);
            int width = specsContainer.Rect.Width;
            void recalculateSpecsContainerHeight()
            {
                int totalSize = 0;
                var children = specsContainer.Content.Children.Where(c => c.Visible);
                foreach (GUIComponent child in children)
                {
                    totalSize += child.Rect.Height;
                }
                totalSize += specsContainer.Content.CountChildren * specsContainer.Spacing;
                if (specsContainer.PadBottom)
                {
                    GUIComponent last = specsContainer.Content.Children.LastOrDefault();
                    if (last != null)
                    {
                        totalSize += specsContainer.Rect.Height - last.Rect.Height;
                    }
                }
                specsContainer.RectTransform.Resize(new Point(width, totalSize), true);
                specsContainer.RecalculateChildren();
            }
            //hell
            recalculateSpecsContainerHeight();
            specsContainer.Content.GetAllChildren<GUITextBlock>().ForEach(c =>
            {
                var firstChild = c.Children.FirstOrDefault() as GUITextBlock;
                if (firstChild != null)
                {
                    firstChild.CalculateHeightFromText(); firstChild.SetTextPos();
                    c.RectTransform.MinSize = new Point(0, firstChild.Rect.Height);
                }
                c.CalculateHeightFromText(); c.SetTextPos();
            });
            recalculateSpecsContainerHeight();

            TaskPool.Add(nameof(GeneratePreviewMeshes), GeneratePreviewMeshes(), _ =>
            {
                // Reset the camera's position on the main thread,
                // because the Camera class is not thread-safe and
                // it's possible for its state to not get updated
                // properly if done within a task
                camera.Position = (bounds.Min + bounds.Max) * (0.5f, -0.5f);
                Vector2 span2d = bounds.Max - bounds.Min;
                Vector2 scaledSpan2d = span2d / camera.Resolution.ToVector2();
                float scaledSpan = Math.Max(scaledSpan2d.X, scaledSpan2d.Y);
                camera.MinZoom = Math.Min(0.1f, 0.4f / scaledSpan);
                camera.Zoom = 0.7f / scaledSpan;
                camera.StopMovement();
                camera.UpdateTransform(interpolate: false, updateListener: false);
            });
        }

        public static void AddToGUIUpdateList()
        {
            instance?.previewFrame?.AddToGUIUpdateList();
        }

        public Task GeneratePreviewMeshes()
        {
            if (loadTask != null) { throw new InvalidOperationException("Tried to start SubmarinePreview loadTask more than once!"); }
            loadTask = Task.Run(GeneratePreviewMeshesInternal);
            return loadTask;
        }

        private async Task GeneratePreviewMeshesInternal()
        {
            await Task.Yield();
            spriteRecorder.Begin(SpriteSortMode.BackToFront);

            HashSet<int> toIgnore = new HashSet<int>();
            HashSet<int> wires = new HashSet<int>();

            foreach (var subElement in submarineInfo.SubmarineElement.Elements())
            {
                switch (subElement.Name.LocalName.ToLowerInvariant())
                {
                    case "item":
                        foreach (var component in subElement.Elements())
                        {
                            switch (component.Name.LocalName.ToLowerInvariant())
                            {
                                case "itemcontainer":
                                    ExtractItemContainerIds(component, toIgnore);
                                    break;
                                case "connectionpanel":
                                    ExtractConnectionPanelLinks(component, wires);
                                    break;
                            }
                        }
                        break;
                }
                if (isDisposed) { return; }
                await Task.Yield();
            }

            var wireNodes = new List<XElement>();

            foreach (var subElement in submarineInfo.SubmarineElement.Elements())
            {
                if (subElement.GetAttributeBool("hiddeningame", false)) { continue; }
                switch (subElement.Name.LocalName.ToLowerInvariant())
                {
                    case "structure":
                    case "item":
                        var id = subElement.GetAttributeInt("ID", 0);
                        if (wires.Contains(id))
                        {
                            wireNodes.Add(subElement);
                        }
                        else if (!toIgnore.Contains(id))
                        {
                            BakeMapEntity(subElement);
                        }
                        break;
                    case "hull":
                        Identifier identifier = subElement.GetAttributeIdentifier("roomname", "");
                        if (!identifier.IsEmpty)
                        {
                            if (!hullCollections.TryGetValue(identifier, out HullCollection hullCollection))
                            {
                                hullCollection = new HullCollection(identifier);
                                hullCollections.Add(identifier, hullCollection);
                            }
                            hullCollection.AddRect(subElement);
                        }
                        break;
                }
                if (isDisposed) { return; }
                await Task.Yield();
            }

            bounds = (spriteRecorder.Min, spriteRecorder.Max);
            wireNodes.ForEach(BakeWireNodes);

            spriteRecorder.End();
        }

        private static void ExtractItemContainerIds(XElement component, HashSet<int> ids)
        {
            string containedString = component.GetAttributeString("contained", "");
            string[] itemIdStrings = containedString.Split(',');
            for (int i = 0; i < itemIdStrings.Length; i++)
            {
                foreach (string idStr in itemIdStrings[i].Split(';'))
                {
                    if (!int.TryParse(idStr, NumberStyles.Any, CultureInfo.InvariantCulture, out int id)) { continue; }
                    if (id != 0 && !ids.Contains(id)) { ids.Add(id); }
                }
            }
        }

        private static void ExtractConnectionPanelLinks(XElement component, HashSet<int> ids)
        {
            var pins = component.Elements("input").Concat(component.Elements("output"));
            foreach (var pin in pins)
            {
                var links = pin.Elements("link");
                foreach (var link in links)
                {
                    int id = link.GetAttributeInt("w", 0);
                    if (id != 0 && !ids.Contains(id)) { ids.Add(id); }
                }
            }
        }

        private void BakeWireNodes(XElement element)
        {
            var prefabIdentifier = element.GetAttributeIdentifier("identifier", "");
            if (prefabIdentifier.IsEmpty) { return; }
            if (!ItemPrefab.Prefabs.TryGet(prefabIdentifier, out var prefab)) { return; }
            
            var prefabWireComponentElement = prefab.ConfigElement.GetChildElement("wire");
            if (prefabWireComponentElement is null) { return; }
            
            var wireComponent = element.GetChildElement("wire");
            if (wireComponent is null) { return; }
            
            var color = element.GetAttributeColor("spritecolor") ?? Color.White;
            
            var nodes = Wire.ExtractNodes(wireComponent).ToImmutableArray();
            var wireSprite = Wire.ExtractWireSprite(prefab.ConfigElement);

            var useSpriteDepth = element.GetAttributeBool("usespritedepth", false);
            var depth = 
                useSpriteDepth
                    ? element.GetAttributeFloat("spritedepth", 1.0f)
                    : wireSprite.Depth;

            var width = prefabWireComponentElement.GetAttributeFloat("width", 0.3f);
            
            for (int i = 0; i < nodes.Length - 1; i++)
            {
                var line = (Start: nodes[i], End: nodes[i + 1]);
                var wireSegment = new Wire.WireSection(line.Start, line.End);
                wireSegment.Draw(spriteRecorder, wireSprite, color, Vector2.Zero, depth, width);
            }
        }
        
        private void BakeMapEntity(XElement element)
        {
            Identifier identifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);
            if (identifier.IsEmpty) { return; }
            Rectangle rect = element.GetAttributeRect("rect", Rectangle.Empty);
            if (rect.Equals(Rectangle.Empty)) { return; }

            float depth = element.GetAttributeFloat("spritedepth", 1f);
            bool flippedX = element.GetAttributeBool("flippedx", false);
            bool flippedY = element.GetAttributeBool("flippedy", false);

            float scale = element.GetAttributeFloat("scale", 1f);
            Color color = element.GetAttributeColor("spritecolor", Color.White);

            float rotation = element.GetAttributeFloat("rotation", 0f);

            MapEntityPrefab prefab;
            if (element.NameAsIdentifier() == "item"
                && ItemPrefab.Prefabs.TryGet(identifier, out ItemPrefab ip))
            {
                prefab = ip;
            }
            else
            {
                prefab = MapEntityPrefab.FindByIdentifier(identifier);
            }
            if (prefab == null) { return; }

            flippedX &= prefab.CanSpriteFlipX;
            flippedY &= prefab.CanSpriteFlipY;

            SpriteEffects spriteEffects = SpriteEffects.None;
            if (flippedX)
            {
                spriteEffects |= SpriteEffects.FlipHorizontally;
            }
            if (flippedY)
            {
                spriteEffects |= SpriteEffects.FlipVertically;
            }

            var prevEffects = prefab.Sprite.effects;
            prefab.Sprite.effects ^= spriteEffects;

            bool overrideSprite = false;
            ItemPrefab itemPrefab = prefab as ItemPrefab;
            if (itemPrefab != null)
            {
                BakeItemComponents(itemPrefab, rect, color, scale, rotation, depth, out overrideSprite);
            }

            if (!overrideSprite)
            {
                if (prefab is StructurePrefab structurePrefab)
                {
                    ParseUpgrades(structurePrefab.ConfigElement, ref scale);

                    if (!prefab.ResizeVertical)
                    {
                        rect.Height = (int)(rect.Height * scale / prefab.Scale);
                    }
                    if (!prefab.ResizeHorizontal)
                    {
                        rect.Width = (int)(rect.Width * scale / prefab.Scale);
                    }
                    var textureScale = element.GetAttributeVector2("texturescale", Vector2.One);

                    Vector2 backGroundOffset = Vector2.Zero;

                    Vector2 textureOffset = element.GetAttributeVector2("textureoffset", Vector2.Zero);
                    if (flippedX) { textureOffset.X = -textureOffset.X; }
                    if (flippedY) { textureOffset.Y = -textureOffset.Y; }

                    backGroundOffset = new Vector2(
                                MathUtils.PositiveModulo((int)-textureOffset.X, prefab.Sprite.SourceRect.Width),
                                MathUtils.PositiveModulo((int)-textureOffset.Y, prefab.Sprite.SourceRect.Height));

                    prefab.Sprite.DrawTiled(
                        spriteRecorder,
                        rect.Location.ToVector2() * new Vector2(1f, -1f),
                        rect.Size.ToVector2(),
                        color: color,
                        startOffset: backGroundOffset,
                        textureScale: textureScale * scale,
                        depth: depth);
                }
                else if (itemPrefab != null)
                {
                    bool usePrefabValues = element.GetAttributeBool("isoverride", false) != itemPrefab.IsOverride;
                    if (usePrefabValues)
                    {
                        scale = itemPrefab.ConfigElement.GetAttributeFloat(scale, "scale", "Scale");
                    }

                    ParseUpgrades(itemPrefab.ConfigElement, ref scale);

                    if (prefab.ResizeVertical || prefab.ResizeHorizontal)
                    {
                        if (!prefab.ResizeHorizontal)
                        {
                            rect.Width = (int)(prefab.Sprite.size.X * scale);
                        }
                        if (!prefab.ResizeVertical)
                        {
                            rect.Height = (int)(prefab.Sprite.size.Y * scale);
                        }

                        var spritePos = rect.Center.ToVector2();
                        //spritePos.Y = rect.Height - spritePos.Y;

                        prefab.Sprite.DrawTiled(
                            spriteRecorder,
                            rect.Location.ToVector2() * new Vector2(1f, -1f),
                            rect.Size.ToVector2(),
                            color: color,
                            textureScale: Vector2.One * scale,
                            depth: depth);

                        foreach (var decorativeSprite in itemPrefab.DecorativeSprites)
                        {
                            float offsetState = 0f;
                            Vector2 offset = decorativeSprite.GetOffset(ref offsetState, Vector2.Zero) * scale;
                            if (flippedX) { offset.X = -offset.X; }
                            if (flippedY) { offset.Y = -offset.Y; }
                            decorativeSprite.Sprite.DrawTiled(spriteRecorder,
                                new Vector2(spritePos.X + offset.X - rect.Width / 2, -(spritePos.Y + offset.Y + rect.Height / 2)),
                                rect.Size.ToVector2(), color: color,
                                textureScale: Vector2.One * scale,
                                depth: Math.Min(depth + (decorativeSprite.Sprite.Depth - prefab.Sprite.Depth), 0.999f));
                        }
                    }
                    else
                    {
                        rect.Width = (int)(rect.Width * scale / prefab.Scale);
                        rect.Height = (int)(rect.Height * scale / prefab.Scale);

                        var spritePos = rect.Center.ToVector2();
                        spritePos.Y -= rect.Height;
                        //spritePos.Y = rect.Height - spritePos.Y;

                        prefab.Sprite.Draw(
                            spriteRecorder,
                            spritePos * new Vector2(1f, -1f),
                            color,
                            prefab.Sprite.Origin,
                            rotation,
                            scale,
                            prefab.Sprite.effects, depth);

                        foreach (var decorativeSprite in itemPrefab.DecorativeSprites)
                        {
                            float rotationState = 0f; float offsetState = 0f;
                            float rot = decorativeSprite.GetRotation(ref rotationState, 0f);
                            Vector2 offset = decorativeSprite.GetOffset(ref offsetState, Vector2.Zero) * scale;
                            if (flippedX) { offset.X = -offset.X; }
                            if (flippedY) { offset.Y = -offset.Y; }
                            decorativeSprite.Sprite.Draw(spriteRecorder, new Vector2(spritePos.X + offset.X, -(spritePos.Y + offset.Y)), color,
                                MathHelper.ToRadians(rotation) + rot, decorativeSprite.GetScale(0f) * scale, prefab.Sprite.effects,
                                depth: Math.Min(depth + (decorativeSprite.Sprite.Depth - prefab.Sprite.Depth), 0.999f));
                        }
                    }
                }
            }

            prefab.Sprite.effects = prevEffects;
        }

        private void BakeItemComponents(
            ItemPrefab prefab,
            Rectangle rect, Color color,
            float scale, float rotation, float depth,
            out bool overrideSprite)
        {
            overrideSprite = false;

            float relativeScale = scale / prefab.Scale;
            foreach (var subElement in prefab.ConfigElement.Elements())
            {
                switch (subElement.Name.LocalName.ToLowerInvariant())
                {
                    case "turret":
                        Sprite barrelSprite = null;
                        Sprite railSprite = null;
                        foreach (var turretSubElem in subElement.Elements())
                        {
                            switch (turretSubElem.Name.ToString().ToLowerInvariant())
                            {
                                case "barrelsprite":
                                    barrelSprite = new Sprite(turretSubElem);
                                    break;
                                case "railsprite":
                                    railSprite = new Sprite(turretSubElem);
                                    break;
                            }
                        }

                        Vector2 barrelPos = subElement.GetAttributeVector2("barrelpos", Vector2.Zero);
                        Vector2 relativeBarrelPos = barrelPos * prefab.Scale - new Vector2(rect.Width / 2, rect.Height / 2);
                        var transformedBarrelPos = MathUtils.RotatePoint(
                            relativeBarrelPos,                            
                            MathHelper.ToRadians(rotation));

                        Vector2 drawPos = new Vector2(rect.X + rect.Width * relativeScale / 2 + transformedBarrelPos.X * relativeScale, rect.Y - rect.Height * relativeScale / 2 - transformedBarrelPos.Y * relativeScale);
                        drawPos.Y = -drawPos.Y;

                        railSprite?.Draw(spriteRecorder,
                            drawPos,
                            color,
                            rotation + MathHelper.PiOver2, scale,
                            SpriteEffects.None, depth + (railSprite.Depth - prefab.Sprite.Depth));

                        barrelSprite?.Draw(spriteRecorder,
                            drawPos,
                            color,
                            rotation + MathHelper.PiOver2, scale,
                            SpriteEffects.None, depth + (barrelSprite.Depth - prefab.Sprite.Depth));

                        break;
                    case "door":
                        var scaledRect = rect with { Size = (rect.Size.ToVector2() * relativeScale).ToPoint() };
                        
                        doors.Add(new Door(scaledRect));

                        var doorSpriteElem = subElement.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("sprite", StringComparison.OrdinalIgnoreCase));
                        if (doorSpriteElem != null)
                        {
                            string texturePath = doorSpriteElem.GetAttributeStringUnrestricted("texture", "");
                            Vector2 pos = scaledRect.Location.ToVector2() * new Vector2(1f, -1f);
                            if (subElement.GetAttributeBool("horizontal", false))
                            {
                                pos.Y += (float)scaledRect.Height * 0.5f;
                            }
                            else
                            {
                                pos.X += (float)scaledRect.Width * 0.5f;
                            }
                            Sprite doorSprite = new Sprite(doorSpriteElem, texturePath.Contains("/") ? "" : Path.GetDirectoryName(prefab.FilePath));
                            spriteRecorder.Draw(doorSprite.Texture, pos,
                                new Rectangle((int)doorSprite.SourceRect.X,
                                    (int)doorSprite.SourceRect.Y,
                                    (int)doorSprite.size.X, (int)doorSprite.size.Y),
                                color, 0.0f, doorSprite.Origin, new Vector2(scale), SpriteEffects.None, doorSprite.Depth);
                        }
                        break;
                    case "ladder":
                        var backgroundSprElem = subElement.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("backgroundsprite", StringComparison.OrdinalIgnoreCase));
                        if (backgroundSprElem != null)
                        {
                            Sprite backgroundSprite = new Sprite(backgroundSprElem);
                            backgroundSprite.DrawTiled(spriteRecorder,
                                new Vector2(rect.Left, -rect.Top) - backgroundSprite.Origin * scale,
                                new Vector2(backgroundSprite.size.X * scale, rect.Height), color: color,
                                textureScale: Vector2.One * scale,
                                depth: depth + 0.1f);
                        }
                        break;
                }
            }
        }

        private void ParseUpgrades(XElement prefabConfigElement, ref float scale)
        {
            foreach (var upgrade in prefabConfigElement.Elements("Upgrade"))
            {
                var upgradeVersion = new Version(upgrade.GetAttributeString("gameversion", "0.0.0.0"));
                if (upgradeVersion >= submarineInfo.GameVersion)
                {
                    string scaleModifier = upgrade.GetAttributeString("scale", "*1");

                    if (scaleModifier.StartsWith("*"))
                    {
                        if (float.TryParse(scaleModifier.Substring(1), NumberStyles.Any, CultureInfo.InvariantCulture, out float parsedScale))
                        {
                            scale *= parsedScale;
                        }
                    }
                    else
                    {
                        if (float.TryParse(scaleModifier, NumberStyles.Any, CultureInfo.InvariantCulture, out float parsedScale))
                        {
                            scale = parsedScale;
                        }
                    }
                }
            }
        }

        private void RenderSubmarine(SpriteBatch spriteBatch, Rectangle scissorRectangle, GUIComponent component)
        {
            if (spriteRecorder == null) { return; }

            GUI.DrawRectangle(spriteBatch, scissorRectangle, new Color(0.051f, 0.149f, 0.271f, 1.0f), isFilled: true);
            
            if (!spriteRecorder.ReadyToRender)
            {
                LocalizedString waitText = !loadTask.IsCompleted ?
                    TextManager.Get("generatingsubmarinepreview", "loading") :
                    (loadTask.Exception?.ToString() ?? "Task completed without marking as ready to render");
                Vector2 origin = (GUIStyle.Font.MeasureString(waitText) * 0.5f);
                origin.X = MathF.Round(origin.X);
                origin.Y = MathF.Round(origin.Y);
                GUIStyle.Font.DrawString(
                    spriteBatch,
                    waitText,
                    scissorRectangle.Center.ToVector2(),
                    Color.White,
                    0f,
                    origin,
                    1f,
                    SpriteEffects.None,
                    0f);
                return;
            }
            spriteBatch.End();

            var prevScissorRect = GameMain.Instance.GraphicsDevice.ScissorRectangle;
            GameMain.Instance.GraphicsDevice.ScissorRectangle = scissorRectangle;
            var prevRasterizerState = GameMain.Instance.GraphicsDevice.RasterizerState;
            GameMain.Instance.GraphicsDevice.RasterizerState = GameMain.ScissorTestEnable;

            spriteRecorder.Render(camera);

            var mousePos = camera.ScreenToWorld(PlayerInput.MousePosition);
            mousePos.Y = -mousePos.Y;

            spriteBatch.Begin(SpriteSortMode.BackToFront, rasterizerState: GameMain.ScissorTestEnable, transformMatrix: camera.Transform);
            GameMain.Instance.GraphicsDevice.ScissorRectangle = scissorRectangle;
            foreach (var hullCollection in hullCollections.Values)
            {
                bool mouseOver = false;
                if (GUI.MouseOn == null || GUI.MouseOn == component)
                {
                    foreach (var rect in hullCollection.Rects)
                    {
                        mouseOver = rect.Contains(mousePos);
                        if (mouseOver) { break; }
                    }
                }

                foreach (var rect in hullCollection.Rects)
                {
                    GUI.DrawRectangle(spriteBatch, rect, mouseOver ? Color.Red : Color.Blue, depth: mouseOver ? 0.45f : 0.5f, thickness: (mouseOver ? 4f : 2f) / camera.Zoom);
                }

                if (mouseOver)
                {
                    LocalizedString str = hullCollection.Name;
                    Vector2 strSize = GUIStyle.Font.MeasureString(str) / camera.Zoom;
                    Vector2 padding = new Vector2(30, 30) / camera.Zoom;
                    Vector2 shift = new Vector2(10, 0) / camera.Zoom;

                    GUI.DrawRectangle(spriteBatch, mousePos + shift, strSize + padding, Color.Black, isFilled: true, depth: 0.25f);
                    GUIStyle.Font.DrawString(spriteBatch, str, mousePos + shift + (strSize + padding) * 0.5f, Color.White, 0f, strSize * camera.Zoom * 0.5f, 1f / camera.Zoom, SpriteEffects.None, 0f);
                }
            }
            foreach (var door in doors)
            {
                GUI.DrawRectangle(spriteBatch, door.Rect, GUIStyle.Green * 0.5f, isFilled: true, depth: 0.4f);
            }
            spriteBatch.End();

            GameMain.Instance.GraphicsDevice.ScissorRectangle = prevScissorRect;
            GameMain.Instance.GraphicsDevice.RasterizerState = prevRasterizerState;
            spriteBatch.Begin(SpriteSortMode.Deferred);
        }

        public void Dispose()
        {
            if (previewFrame != null)
            {
                previewFrame.RectTransform.Parent = null;
                previewFrame = null;
            }
            spriteRecorder?.Dispose(); spriteRecorder = null;
            camera?.Dispose(); camera = null;
            isDisposed = true;
        }
    }
}
