using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Map
    {
        //how much larger the ice background is compared to the size of the map
        private const float BackgroundScale = 1.5f;

        private static Sprite iceTexture;
        private static Texture2D iceCraters;
        private static Texture2D iceCrack;

        private static Texture2D circleTexture;

        private static List<Sprite> mapPieces = new List<Sprite>();

        class MapAnim
        {
            public Location StartLocation;
            public Location EndLocation;
            public string StartMessage;
            public string EndMessage;

            public float? StartZoom;
            public float? EndZoom;

            private float startDelay;
            public float StartDelay
            {
                get { return startDelay; }
                set
                {
                    startDelay = value;
                    Timer = -startDelay;
                }
            }

            public Vector2? StartPos;

            public float Duration;
            public float Timer;

            public bool Finished;
        }

        private Queue<MapAnim> mapAnimQueue = new Queue<MapAnim>();
        
        private Location highlightedLocation;

        private Vector2 drawOffset;

        private float zoom = 3.0f;

        private Rectangle borders;
        
        private MapTile[,] mapTiles;
        
#if DEBUG
        private GUIComponent editor;

        private void CreateEditor()
        {
            editor = new GUIFrame(new RectTransform(new Vector2(0.25f, 1.0f), GUI.Canvas, Anchor.TopRight, minSize: new Point(400, 0)));
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), editor.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.02f,
                CanBeFocused = false
            };

            var listBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.95f), paddedFrame.RectTransform, Anchor.Center));
            new SerializableEntityEditor(listBox.Content.RectTransform, generationParams, false, true);

            new GUIButton(new RectTransform(new Vector2(1.0f, 0.05f), paddedFrame.RectTransform), "Generate")
            {
                OnClicked =(btn, userData) =>
                {
                    Rand.SetSyncedSeed(ToolBox.StringToInt(this.seed));
                    Generate();
                    return true;
                }
            };
        }
#endif

        struct MapTile
        {
            public readonly Sprite Sprite;
            public SpriteEffects SpriteEffect;
            public Vector2 Offset;

            public MapTile(Sprite sprite, SpriteEffects spriteEffect)
            {
                Sprite = sprite;
                SpriteEffect = spriteEffect;

                Offset = Rand.Vector(Rand.Range(0.0f, 1.0f));
            }
        }
        
        partial void InitProjectSpecific()
        {
            OnLocationChanged += LocationChanged;

            borders = new Rectangle(
                (int)locations.Min(l => l.MapPosition.X),
                (int)locations.Min(l => l.MapPosition.Y),
                (int)locations.Max(l => l.MapPosition.X),
                (int)locations.Max(l => l.MapPosition.Y));
            borders.Width = borders.Width - borders.X;
            borders.Height = borders.Height - borders.Y;

            mapTiles = new MapTile[
                (int)Math.Ceiling(size * BackgroundScale / generationParams.TileSpriteSpacing.X), 
                (int)Math.Ceiling(size * BackgroundScale / generationParams.TileSpriteSpacing.Y)];

            for (int x = 0; x < mapTiles.GetLength(0); x++)
            {
                for (int y = 0; y < mapTiles.GetLength(1); y++)
                {
                    mapTiles[x, y] = new MapTile(
                        mapPieces[Rand.Int(mapPieces.Count)], Rand.Range(0.0f, 1.0f) < 0.5f ? 
                        SpriteEffects.FlipHorizontally : SpriteEffects.None);
                }
            }

            drawOffset = -currentLocation.MapPosition;
        }

        private Texture2D noiseTexture;

        partial void GenerateNoiseMapProjSpecific()
        {
            if (noiseTexture == null)
            {
                noiseTexture = new Texture2D(GameMain.Instance.GraphicsDevice, generationParams.NoiseResolution, generationParams.NoiseResolution);
            }

            Color[] crackTextureData = new Color[generationParams.NoiseResolution * generationParams.NoiseResolution];
            Color[] noiseTextureData = new Color[generationParams.NoiseResolution * generationParams.NoiseResolution];
            for (int x = 0; x < generationParams.NoiseResolution; x++)
            {
                for (int y = 0; y < generationParams.NoiseResolution; y++)
                {
                    noiseTextureData[x + y * generationParams.NoiseResolution] = Color.Lerp(Color.Black, Color.Transparent, Noise[x, y]);
                }
            }

            float mapRadius = size / 2;
            Vector2 mapCenter = Vector2.One * mapRadius;
            foreach (LocationConnection connection in connections)
            {
                float centerDist = Vector2.Distance(connection.CenterPos, mapCenter);

                Vector2 connectionStart = connection.Locations[0].MapPosition;
                Vector2 connectionEnd = connection.Locations[1].MapPosition;
                float connectionLength = Vector2.Distance(connectionStart, connectionEnd);
                int iterations = (int)(Math.Sqrt(connectionLength * generationParams.ConnectionIndicatorIterationMultiplier));
                connection.CrackSegments = MathUtils.GenerateJaggedLine(
                    connectionStart, connectionEnd, 
                    iterations, connectionLength * generationParams.ConnectionIndicatorDisplacementMultiplier);                

                iterations = (int)(Math.Sqrt(connectionLength * generationParams.ConnectionIterationMultiplier));
                var visualCrackSegments = MathUtils.GenerateJaggedLine(
                    connectionStart, connectionEnd, 
                    iterations, connectionLength * generationParams.ConnectionDisplacementMultiplier);

                float totalLength = Vector2.Distance(visualCrackSegments[0][0], visualCrackSegments.Last()[1]);
                for (int i = 0; i < visualCrackSegments.Count; i++)
                {
                    Vector2 start = visualCrackSegments[i][0] * (generationParams.NoiseResolution / (float)size);
                    Vector2 end = visualCrackSegments[i][1] * (generationParams.NoiseResolution / (float)size);

                    float length = Vector2.Distance(start, end);
                    for (float x = 0; x < 1; x += 1.0f / length)
                    {
                        Vector2 pos = Vector2.Lerp(start, end, x);
                        SetNoiseColorOnArea(pos, MathHelper.Clamp((int)(totalLength / 30), 2, 5) + Rand.Range(-1,1), Color.Transparent);
                    }
                }
            }

            void SetNoiseColorOnArea(Vector2 pos, int dist, Color color)
            {
                for (int x = -dist; x < dist; x++)
                {
                    for (int y = -dist; y < dist; y++)
                    {
                        float d = 1.0f - new Vector2(x, y).Length() / dist;
                        if (d <= 0) continue;

                        int xIndex = (int)pos.X + x;
                        if (xIndex < 0 || xIndex >= generationParams.NoiseResolution) continue;
                        int yIndex = (int)pos.Y + y;
                        if (yIndex < 0 || yIndex >= generationParams.NoiseResolution) continue;

                        float perlin = (float)PerlinNoise.Perlin(
                            xIndex / (float)generationParams.NoiseResolution * 100.0f, 
                            yIndex / (float)generationParams.NoiseResolution * 100.0f, 0);
                        
                        byte a = Math.Max(crackTextureData[xIndex + yIndex * generationParams.NoiseResolution].A, (byte)((d * perlin) * 255));

                        crackTextureData[xIndex + yIndex * generationParams.NoiseResolution].A = a;
                    }
                }
            }

            for (int i = 0; i < noiseTextureData.Length; i++)
            {
                float darken = noiseTextureData[i].A / 255.0f;
                Color pathColor = Color.Lerp(Color.White, Color.Transparent, noiseTextureData[i].A / 255.0f);
                noiseTextureData[i] =
                    Color.Lerp(noiseTextureData[i], pathColor, crackTextureData[i].A / 255.0f * 0.5f);
            }

            noiseTexture.SetData(noiseTextureData);
        }

        private void LocationChanged(Location prevLocation, Location newLocation)
        {
            if (prevLocation == newLocation) return;
            //focus on starting location
            mapAnimQueue.Enqueue(new MapAnim()
            {
                EndZoom = 1.5f,
                EndLocation = prevLocation,
                Duration = MathHelper.Clamp(Vector2.Distance(-drawOffset, prevLocation.MapPosition) / 1000.0f, 0.1f, 0.5f),
            });
            mapAnimQueue.Enqueue(new MapAnim()
            {
                EndZoom = 2.0f,
                StartLocation = prevLocation,
                EndLocation = newLocation,
                Duration = 2.0f,
                StartDelay = 0.5f
            });
        }

        partial void ChangeLocationType(Location location, string prevName, LocationTypeChange change)
        {            
            //focus on the location
            var mapAnim = new MapAnim()
            {
                EndZoom = zoom * 1.5f,
                EndLocation = location,
                Duration = currentLocation == location ? 1.0f : 2.0f,
                StartDelay = 1.0f
            };
            if (change.Messages.Count > 0)
            {
                mapAnim.EndMessage = change.Messages[Rand.Range(0,change.Messages.Count)]
                    .Replace("[prevname]", prevName)
                    .Replace("[name]", location.Name);
            }
            mapAnimQueue.Enqueue(mapAnim);
            
            mapAnimQueue.Enqueue(new MapAnim()
            {
                EndZoom = zoom,
                StartLocation = location,
                EndLocation = currentLocation,
                Duration = 1.0f,
                StartDelay = 0.5f
            });            
        }

        public void Update(float deltaTime, Rectangle rect)
        {
#if DEBUG
            if (GameMain.DebugDraw)
            {
                if (editor == null) CreateEditor();
                editor.AddToGUIUpdateList(order: 1);
            }
#endif

            if (mapAnimQueue.Count > 0)
            {
                UpdateMapAnim(mapAnimQueue.Peek(), deltaTime);
                if (mapAnimQueue.Peek().Finished)
                {
                    mapAnimQueue.Dequeue();
                }
                return;
            }

            //GenerateNoiseMap(4, 0.5f);

            if (PlayerInput.KeyHit(Keys.D1))
            {
                drawOverlay = !drawOverlay;
            }
            if (PlayerInput.KeyDown(Keys.D2)) xScale -= 0.05f * deltaTime;
            if (PlayerInput.KeyDown(Keys.D3)) xScale += 0.05f * deltaTime;
            if (PlayerInput.KeyDown(Keys.D4)) yScale -= 0.05f * deltaTime;
            if (PlayerInput.KeyDown(Keys.D5)) yScale += 0.05f * deltaTime;
            if (PlayerInput.KeyDown(Keys.D6)) randomOffsetScale -= 0.05f * deltaTime;
            if (PlayerInput.KeyDown(Keys.D7)) randomOffsetScale += 0.05f * deltaTime;

            Vector2 rectCenter = new Vector2(rect.Center.X, rect.Center.Y);

            float maxDist = 20.0f;
            float closestDist = 0.0f;
            highlightedLocation = null;
            for (int i = 0; i < locations.Count; i++)
            {
                Location location = locations[i];
                Vector2 pos = rectCenter + (location.MapPosition + drawOffset) * zoom;

                if (!rect.Contains(pos)) continue;

                float dist = Vector2.Distance(PlayerInput.MousePosition, pos);
                if (dist < maxDist && (highlightedLocation == null || dist < closestDist))
                {
                    closestDist = dist;
                    highlightedLocation = location;
                }
            }

            foreach (LocationConnection connection in connections)
            {
                if (highlightedLocation != currentLocation &&
                    connection.Locations.Contains(highlightedLocation) && connection.Locations.Contains(currentLocation))
                {
                    if (PlayerInput.LeftButtonClicked() &&
                        selectedLocation != highlightedLocation && highlightedLocation != null)
                    {
                        selectedConnection = connection;
                        selectedLocation = highlightedLocation;
                        
                        //clients aren't allowed to select the location without a permission
                        if (GameMain.Client == null || GameMain.Client.HasPermission(Networking.ClientPermissions.ManageCampaign))
                        {
                            OnLocationSelected?.Invoke(selectedLocation, selectedConnection);
                            GameMain.Client?.SendCampaignState();
                        }
                    }
                }
            }

            zoom += PlayerInput.ScrollWheelSpeed / 1000.0f;
            zoom = MathHelper.Clamp(zoom, 0.5f, 4.0f);

            if (rect.Contains(PlayerInput.MousePosition) && PlayerInput.MidButtonHeld())
            {
                drawOffset += PlayerInput.MouseSpeed / zoom;
            }

#if DEBUG
            if (PlayerInput.DoubleClicked() && highlightedLocation != null)
            {
                var passedConnection = currentLocation.Connections.Find(c => c.OtherLocation(currentLocation) == highlightedLocation);
                if (passedConnection != null)
                {
                    passedConnection.Passed = true;
                }

                Location prevLocation = currentLocation;
                currentLocation = highlightedLocation;
                CurrentLocation.Discovered = true;
                OnLocationChanged?.Invoke(prevLocation, currentLocation);
                ProgressWorld();
            }
#endif
        }

        private float xScale = 1.0f, yScale = 1.0f, randomOffsetScale = 0.0f;
        private bool drawOverlay = true;
        
        public void Draw(SpriteBatch spriteBatch, Rectangle rect)
        {

            Vector2 viewSize = new Vector2(rect.Width / zoom, rect.Height / zoom);
            float edgeBuffer = size * (BackgroundScale - 1.0f) / 2;
            drawOffset.X = MathHelper.Clamp(drawOffset.X, -size - edgeBuffer + viewSize.X / 2.0f, edgeBuffer -viewSize.X / 2.0f);
            drawOffset.Y = MathHelper.Clamp(drawOffset.Y, -size - edgeBuffer + viewSize.Y / 2.0f, edgeBuffer -viewSize.Y / 2.0f);



            GUI.DrawString(spriteBatch, new Vector2(10, 10), "Num key 1 to toggle location visibility", Color.White);
            GUI.DrawString(spriteBatch, new Vector2(10, 30), "Num keys 2-5 to edit map tile spacing", Color.White);
            GUI.DrawString(spriteBatch, new Vector2(10, 50), "Tile spacing: "+xScale+"x"+yScale, Color.White);
            GUI.DrawString(spriteBatch, new Vector2(10, 70), "Random offset scale: "+ randomOffsetScale, Color.White);
            
            Vector2 rectCenter = new Vector2(rect.Center.X, rect.Center.Y);

            Rectangle prevScissorRect = GameMain.Instance.GraphicsDevice.ScissorRectangle;
            GameMain.Instance.GraphicsDevice.ScissorRectangle = rect;
            
            //GUI.DrawRectangle(spriteBatch, rectCenter + (borders.Location.ToVector2() + drawOffset) * zoom, borders.Size.ToVector2() * zoom, Color.CadetBlue, true);

            for (int x = 0; x < mapTiles.GetLength(0); x++)
            {
                for (int y = 0; y < mapTiles.GetLength(1); y++)
                {
                    Vector2 mapPos = new Vector2(
                        x * generationParams.TileSpriteSpacing.X + ((y % 2 == 0) ? 0.0f : generationParams.TileSpriteSpacing.X * 0.5f), 
                        y * generationParams.TileSpriteSpacing.Y);

                    mapPos.X *= xScale;
                    mapPos.Y *= yScale;

                    mapPos.X -= size / 2 * (BackgroundScale - 1.0f);
                    mapPos.Y -= size / 2 * (BackgroundScale - 1.0f);

                    mapPos += mapTiles[x, y].Offset * randomOffsetScale * 100.0f;

                    Vector2 scale = new Vector2(
                        generationParams.TileSpriteSize.X / mapTiles[x, y].Sprite.size.X, 
                        generationParams.TileSpriteSize.Y / mapTiles[x, y].Sprite.size.Y);
                    mapTiles[x, y].Sprite.Draw(spriteBatch, rectCenter + (mapPos + drawOffset) * zoom, Color.White,
                        origin: new Vector2(256.0f, 256.0f), rotate: 0, scale: scale * zoom, spriteEffect: mapTiles[x, y].SpriteEffect);
                }
            }

#if DEBUG
            if (generationParams.ShowNoiseMap)
            {
                GUI.DrawRectangle(spriteBatch, rectCenter + (borders.Location.ToVector2() + drawOffset) * zoom, borders.Size.ToVector2() * zoom, Color.White, true);
            }
#endif
            Vector2 topLeft = rectCenter + drawOffset * zoom;
            topLeft.X = (int)topLeft.X;
            topLeft.Y = (int)topLeft.Y;

            Vector2 bottomRight = rectCenter + (drawOffset + new Vector2(size,size)) * zoom;
            bottomRight.X = (int)bottomRight.X;
            bottomRight.Y = (int)bottomRight.Y;

            spriteBatch.Draw(noiseTexture,
                destinationRectangle: new Rectangle((int)topLeft.X, (int)topLeft.Y, (int)(bottomRight.X- topLeft.X), (int)(bottomRight.Y - topLeft.Y)),
                sourceRectangle: null,
                color: Color.White);

            if (topLeft.X > rect.X)
                GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X, rect.Y, (int)(topLeft.X- rect.X), rect.Height), Color.Black* 0.8f, true);
            if (topLeft.Y > rect.Y)
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)topLeft.X, rect.Y, (int)(bottomRight.X - topLeft.X), (int)(topLeft.Y - rect.Y)), Color.Black * 0.8f, true);
            if (bottomRight.X < rect.Right)
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)bottomRight.X, rect.Y, (int)(rect.Right - bottomRight.X), rect.Height), Color.Black * 0.8f, true);
            if (bottomRight.Y < rect.Bottom)
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)topLeft.X, (int)bottomRight.Y, (int)(bottomRight.X - topLeft.X), (int)(rect.Bottom - bottomRight.Y)), Color.Black * 0.8f, true);

            if (drawOverlay)
            {
                for (int i = 0; i < locations.Count; i++)
                {
                    Location location = locations[i];

                    if (location.Type.HaloColor.A > 0 && false)
                    {
                        Vector2 pos = rectCenter + (location.MapPosition + drawOffset) * zoom;

                        spriteBatch.Draw(circleTexture, pos, null, location.Type.HaloColor * 0.1f, 0.0f,
                            new Vector2(512, 512), zoom * 0.1f, SpriteEffects.None, 0);
                    }
                }

                foreach (LocationConnection connection in connections)
                {
                    Color crackColor = Color.White;

                    if (selectedLocation != currentLocation &&
                        (connection.Locations.Contains(selectedLocation) && connection.Locations.Contains(currentLocation)))
                    {
                        crackColor = Color.Red;
                    }
                    else if (highlightedLocation != currentLocation &&
                    (connection.Locations.Contains(highlightedLocation) && connection.Locations.Contains(currentLocation)))
                    {
                        crackColor = Color.Red * 0.5f;
                    }
                    else if (!connection.Passed)
                    {
                        //crackColor *= 0.5f;
                    }

                    for (int i = 0; i < connection.CrackSegments.Count; i++)
                    {
                        var segment = connection.CrackSegments[i];

                        Vector2 start = rectCenter + (segment[0] + drawOffset) * zoom;
                        Vector2 end = rectCenter + (segment[1] + drawOffset) * zoom;

                        if (!rect.Contains(start) && !rect.Contains(end))
                        {
                            continue;
                        }
                        else
                        {
                            Vector2? intersection = MathUtils.GetLineRectangleIntersection(start, end, new Rectangle(rect.X, rect.Y + rect.Height, rect.Width, rect.Height));
                            if (intersection != null)
                            {
                                if (!rect.Contains(start))
                                {
                                    start = (Vector2)intersection;
                                }
                                else
                                {
                                    end = (Vector2)intersection;
                                }
                            }
                        }

                        float distFromPlayer = Vector2.Distance(currentLocation.MapPosition, (segment[0] + segment[1]) / 2.0f);
                        float dist = Vector2.Distance(start, end);

                        int width = (int)(3 * zoom);// (int)(MathHelper.Lerp(1.0f, 4.0f, connection.Difficulty / 100.0f) * zoom);

                        float a = (200.0f - distFromPlayer) / 200.0f;
                        spriteBatch.Draw(iceCrack,
                            new Rectangle((int)start.X, (int)start.Y, (int)(dist - 1 * zoom), width),
                            null, crackColor * MathHelper.Clamp(a, 0.1f, 0.5f), MathUtils.VectorToAngle(end - start),
                            new Vector2(0, 16), SpriteEffects.None, 0.01f);

                        /*GUI.DrawLine(spriteBatch, start, end, Color.Red * MathHelper.Clamp(a * 0.2f, 0.05f, 0.3f), 0, (int)(6 * zoom));*/
                    }

                    /*if (GameMain.DebugDraw)
                    {
                        Vector2 center = rectCenter + (connection.CenterPos + drawOffset) * zoom;
                        GUI.DrawString(spriteBatch, center, connection.Biome.Name + " (" + connection.Difficulty + ")", Color.White);
                    }*/
                }

                for (int i = 0; i < generationParams.DifficultyZones; i++)
                {
                    float radius = size / 2 * ((i + 1.0f) / generationParams.DifficultyZones);
                    float textureSize = (radius / (circleTexture.Width / 2) * zoom);

                    spriteBatch.Draw(circleTexture, rectCenter + (drawOffset + new Vector2(size / 2, size / 2)) * zoom, null, Color.White, 0.0f,
                        new Vector2(512, 512), textureSize, SpriteEffects.None, 0);
                }

                rect.Inflate(8, 8);
                GUI.DrawRectangle(spriteBatch, rect, Color.Black, false, 0.0f, 8);
                GUI.DrawRectangle(spriteBatch, rect, Color.LightGray);

                for (int i = 0; i < locations.Count; i++)
                {
                    Location location = locations[i];
                    Vector2 pos = rectCenter + (location.MapPosition + drawOffset) * zoom;

                    Rectangle drawRect = location.Type.Sprite.SourceRect;
                    drawRect.X = (int)pos.X - drawRect.Width / 2;
                    drawRect.Y = (int)pos.Y - drawRect.Width / 2;

                    if (!rect.Intersects(drawRect)) continue;

                    Color color = location.Connections.Find(c => c.Locations.Contains(currentLocation)) == null ? Color.Orange : Color.Green;
                    //color *= (location.Discovered) ? 0.8f : 0.5f;
                    if (location == currentLocation) color = Color.Red;

                    spriteBatch.Draw(location.Type.Sprite.Texture, pos, null, color, 0.0f, location.Type.Sprite.size / 2, 0.2f * zoom, SpriteEffects.None, 0.0f);
                }

                for (int i = 0; i < 3; i++)
                {
                    Location location = (i == 0) ? highlightedLocation : selectedLocation;
                    if (i == 2) location = currentLocation;

                    if (location == null) continue;

                    Vector2 pos = rectCenter + (location.MapPosition + drawOffset) * zoom;
                    pos.X = (int)(pos.X + location.Type.Sprite.SourceRect.Width * 0.6f);
                    pos.Y = (int)(pos.Y - 10);
                    GUI.DrawString(spriteBatch, pos, location.Name, Color.White, Color.Black * 0.8f, 3);
                    GUI.DrawString(spriteBatch, pos + Vector2.UnitY * 25, location.Type.DisplayName, Color.White, Color.Black * 0.8f, 3);
                }
            }
            
            GameMain.Instance.GraphicsDevice.ScissorRectangle = prevScissorRect;
        }

        private void UpdateMapAnim(MapAnim anim, float deltaTime)
        {
            //pause animation while there are messageboxes on screen
            if (GUIMessageBox.MessageBoxes.Count > 0) return;

            if (!string.IsNullOrEmpty(anim.StartMessage))
            {
                new GUIMessageBox("", anim.StartMessage);
                anim.StartMessage = null;
                return;
            }

            if (anim.StartZoom == null) anim.StartZoom = zoom;
            if (anim.EndZoom == null) anim.EndZoom = zoom;

            anim.StartPos = (anim.StartLocation == null) ? -drawOffset : anim.StartLocation.MapPosition;

            anim.Timer = Math.Min(anim.Timer + deltaTime, anim.Duration);
            float t = anim.Duration <= 0.0f ? 1.0f : Math.Max(anim.Timer / anim.Duration, 0.0f);
            drawOffset = -Vector2.SmoothStep(anim.StartPos.Value, anim.EndLocation.MapPosition, t);
            zoom = MathHelper.SmoothStep(anim.StartZoom.Value, anim.EndZoom.Value, t);

            if (anim.Timer >= anim.Duration)
            {
                if (!string.IsNullOrEmpty(anim.EndMessage))
                {
                    new GUIMessageBox("", anim.EndMessage);
                    anim.EndMessage = null;
                    return;
                }
                anim.Finished = true;
            }
        }
    }
}
