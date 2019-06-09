using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Map
    {
        //how much larger the ice background is compared to the size of the map
        private const float BackgroundScale = 1.5f;
        
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
        private Vector2 drawOffsetNoise;

        private float subReticleAnimState;
        private float targetReticleAnimState;
        private Vector2 subReticlePosition;

        private float zoom = 3.0f;

        private Rectangle borders;
        
        private MapTile[,] mapTiles;
        private bool messageBoxOpen;
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
                    Rand.SetSyncedSeed(ToolBox.StringToInt(this.Seed));
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
                (int)Locations.Min(l => l.MapPosition.X),
                (int)Locations.Min(l => l.MapPosition.Y),
                (int)Locations.Max(l => l.MapPosition.X),
                (int)Locations.Max(l => l.MapPosition.Y));
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
                        generationParams.BackgroundTileSprites[Rand.Int(generationParams.BackgroundTileSprites.Count)], Rand.Range(0.0f, 1.0f) < 0.5f ? 
                        SpriteEffects.FlipHorizontally : SpriteEffects.None);
                }
            }

            drawOffset = -CurrentLocation.MapPosition;
        }
        
        private static Texture2D rawNoiseTexture;
        private static Sprite rawNoiseSprite;
        private static Texture2D noiseTexture;

        partial void GenerateNoiseMapProjSpecific()
        {
            if (noiseTexture == null)
            {
                CrossThread.RequestExecutionOnMainThread(() =>
                {
                    noiseTexture = new Texture2D(GameMain.Instance.GraphicsDevice, generationParams.NoiseResolution, generationParams.NoiseResolution);
                    rawNoiseTexture = new Texture2D(GameMain.Instance.GraphicsDevice, generationParams.NoiseResolution, generationParams.NoiseResolution);
                });
                rawNoiseSprite = new Sprite(rawNoiseTexture, null, null);
            }

            Color[] crackTextureData = new Color[generationParams.NoiseResolution * generationParams.NoiseResolution];
            Color[] noiseTextureData = new Color[generationParams.NoiseResolution * generationParams.NoiseResolution];
            Color[] rawNoiseTextureData = new Color[generationParams.NoiseResolution * generationParams.NoiseResolution];
            for (int x = 0; x < generationParams.NoiseResolution; x++)
            {
                for (int y = 0; y < generationParams.NoiseResolution; y++)
                {
                    noiseTextureData[x + y * generationParams.NoiseResolution] = Color.Lerp(Color.Black, Color.Transparent, Noise[x, y]);
                    rawNoiseTextureData[x + y * generationParams.NoiseResolution] = Color.Lerp(Color.Black, Color.White, Rand.Range(0.0f,1.0f));
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

                        float perlin = (float)PerlinNoise.CalculatePerlin(
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

            CrossThread.RequestExecutionOnMainThread(() =>
            {
                noiseTexture.SetData(noiseTextureData);
                rawNoiseTexture.SetData(rawNoiseTextureData);
            });
        }

        private void LocationChanged(Location prevLocation, Location newLocation)
        {
            if (prevLocation == newLocation) return;
            //focus on starting location
            mapAnimQueue.Enqueue(new MapAnim()
            {
                EndZoom = 2.0f,
                EndLocation = prevLocation,
                Duration = MathHelper.Clamp(Vector2.Distance(-drawOffset, prevLocation.MapPosition) / 1000.0f, 0.1f, 0.5f),
            });
            mapAnimQueue.Enqueue(new MapAnim()
            {
                EndZoom = 3.0f,
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
                Duration = CurrentLocation == location ? 1.0f : 2.0f,
                StartDelay = 1.0f
            };
            if (change.Messages != null && change.Messages.Count > 0)
            {
                mapAnim.EndMessage = change.Messages[Rand.Range(0, change.Messages.Count)]
                    .Replace("[previousname]", prevName)
                    .Replace("[name]", location.Name);
            }
            mapAnimQueue.Enqueue(mapAnim);
            
            mapAnimQueue.Enqueue(new MapAnim()
            {
                EndZoom = zoom,
                StartLocation = location,
                EndLocation = CurrentLocation,
                Duration = 1.0f,
                StartDelay = 0.5f
            });            
        }

        partial void ClearAnimQueue()
        {
            mapAnimQueue.Clear();
        }

        public void Update(float deltaTime, GUICustomComponent mapContainer)
        {
            Rectangle rect = mapContainer.Rect;

            subReticlePosition = Vector2.Lerp(subReticlePosition, CurrentLocation.MapPosition, deltaTime);
            subReticleAnimState = 0.8f - Vector2.Distance(subReticlePosition, CurrentLocation.MapPosition) / 50.0f;
            subReticleAnimState = MathHelper.Clamp(subReticleAnimState + (float)Math.Sin(Timing.TotalTime * 3.5f) * 0.2f, 0.0f, 1.0f);

            targetReticleAnimState = SelectedLocation == null ? 
                Math.Max(targetReticleAnimState - deltaTime, 0.0f) : 
                Math.Min(targetReticleAnimState + deltaTime, 0.6f + (float)Math.Sin(Timing.TotalTime * 2.5f) * 0.4f);
#if DEBUG
            if (GameMain.DebugDraw)
            {
                if (editor == null) CreateEditor();
                editor.AddToGUIUpdateList(order: 1);
            }
#endif

            if (mapAnimQueue.Count > 0)
            {
                hudOpenState = Math.Max(hudOpenState - deltaTime, 0.0f);
                UpdateMapAnim(mapAnimQueue.Peek(), deltaTime);
                if (mapAnimQueue.Peek().Finished)
                {
                    mapAnimQueue.Dequeue();
                }
                return;
            }

            hudOpenState = Math.Min(hudOpenState + deltaTime, 0.75f + (float)Math.Sin(Timing.TotalTime * 3.0f) * 0.25f);
            
            Vector2 rectCenter = new Vector2(rect.Center.X, rect.Center.Y);
            
            float closestDist = 0.0f;
            highlightedLocation = null;
            if (GUI.MouseOn == null || GUI.MouseOn == mapContainer)
            {
                for (int i = 0; i < Locations.Count; i++)
                {
                    Location location = Locations[i];
                    Vector2 pos = rectCenter + (location.MapPosition + drawOffset) * zoom;

                    if (!rect.Contains(pos)) continue;

                    float iconScale = MapGenerationParams.Instance.LocationIconSize / location.Type.Sprite.size.X;

                    Rectangle drawRect = location.Type.Sprite.SourceRect;
                    drawRect.Width = (int)(drawRect.Width * iconScale * zoom * 1.4f);
                    drawRect.Height = (int)(drawRect.Height * iconScale * zoom * 1.4f);
                    drawRect.X = (int)pos.X - drawRect.Width / 2;
                    drawRect.Y = (int)pos.Y - drawRect.Width / 2;

                    if (!drawRect.Contains(PlayerInput.MousePosition)) continue;

                    float dist = Vector2.Distance(PlayerInput.MousePosition, pos);
                    if (highlightedLocation == null || dist < closestDist)
                    {
                        closestDist = dist;
                        highlightedLocation = location;
                    }
                }
            }

            foreach (LocationConnection connection in connections)
            {
                if (highlightedLocation != CurrentLocation &&
                    connection.Locations.Contains(highlightedLocation) && connection.Locations.Contains(CurrentLocation))
                {
                    if (PlayerInput.LeftButtonClicked() &&
                        SelectedLocation != highlightedLocation && highlightedLocation != null)
                    {
                        //clients aren't allowed to select the location without a permission
                        if (GameMain.Client == null || GameMain.Client.HasPermission(Networking.ClientPermissions.ManageCampaign))
                        {
                            SelectedConnection = connection;
                            SelectedLocation = highlightedLocation;
                            targetReticleAnimState = 0.0f;

                            OnLocationSelected?.Invoke(SelectedLocation, SelectedConnection);
                            GameMain.Client?.SendCampaignState();
                        }
                    }
                }
            }
            
            if (GUI.KeyboardDispatcher.Subscriber == null)
            {
                float moveSpeed = 1000.0f;
                Vector2 moveAmount = Vector2.Zero;
                if (PlayerInput.KeyDown(InputType.Left)) { moveAmount += Vector2.UnitX; }
                if (PlayerInput.KeyDown(InputType.Right)) { moveAmount -= Vector2.UnitX; }
                if (PlayerInput.KeyDown(InputType.Up)) { moveAmount += Vector2.UnitY; }
                if (PlayerInput.KeyDown(InputType.Down)) { moveAmount -= Vector2.UnitY; }
                drawOffset += moveAmount * moveSpeed / zoom * deltaTime;
            }

            if (GUI.MouseOn == mapContainer)
            {
                zoom += PlayerInput.ScrollWheelSpeed / 1000.0f;
                zoom = MathHelper.Clamp(zoom, 1.0f, 4.0f);

                if (PlayerInput.MidButtonHeld() || (highlightedLocation == null && PlayerInput.LeftButtonHeld()))
                {
                    drawOffset += PlayerInput.MouseSpeed / zoom;
                }
#if DEBUG
                if (PlayerInput.DoubleClicked() && highlightedLocation != null)
                {
                    var passedConnection = CurrentLocation.Connections.Find(c => c.OtherLocation(CurrentLocation) == highlightedLocation);
                    if (passedConnection != null)
                    {
                        passedConnection.Passed = true;
                    }

                    Location prevLocation = CurrentLocation;
                    CurrentLocation = highlightedLocation;
                    CurrentLocation.Discovered = true;
                    OnLocationChanged?.Invoke(prevLocation, CurrentLocation);
                    SelectLocation(-1);
                    ProgressWorld();
                }
#endif
            }
        }
        
        public void Draw(SpriteBatch spriteBatch, GUICustomComponent mapContainer)
        {
            Rectangle rect = mapContainer.Rect;

            Vector2 viewSize = new Vector2(rect.Width / zoom, rect.Height / zoom);
            float edgeBuffer = size * (BackgroundScale - 1.0f) / 2;
            drawOffset.X = MathHelper.Clamp(drawOffset.X, -size - edgeBuffer + viewSize.X / 2.0f, edgeBuffer -viewSize.X / 2.0f);
            drawOffset.Y = MathHelper.Clamp(drawOffset.Y, -size - edgeBuffer + viewSize.Y / 2.0f, edgeBuffer -viewSize.Y / 2.0f);

            drawOffsetNoise = new Vector2(
                (float)PerlinNoise.CalculatePerlin(Timing.TotalTime * 0.1f % 255, Timing.TotalTime * 0.1f % 255, 0) - 0.5f, 
                (float)PerlinNoise.CalculatePerlin(Timing.TotalTime * 0.2f % 255, Timing.TotalTime * 0.2f % 255, 0.5f) - 0.5f) * 10.0f;

            Vector2 viewOffset = drawOffset + drawOffsetNoise;

            Vector2 rectCenter = new Vector2(rect.Center.X, rect.Center.Y);

            Rectangle prevScissorRect = GameMain.Instance.GraphicsDevice.ScissorRectangle;
            spriteBatch.End();
            spriteBatch.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(prevScissorRect, rect);
            spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: GameMain.ScissorTestEnable);

            for (int x = 0; x < mapTiles.GetLength(0); x++)
            {
                for (int y = 0; y < mapTiles.GetLength(1); y++)
                {
                    Vector2 mapPos = new Vector2(
                        x * generationParams.TileSpriteSpacing.X + ((y % 2 == 0) ? 0.0f : generationParams.TileSpriteSpacing.X * 0.5f), 
                        y * generationParams.TileSpriteSpacing.Y);
                    
                    mapPos.X -= size / 2 * (BackgroundScale - 1.0f);
                    mapPos.Y -= size / 2 * (BackgroundScale - 1.0f);
                    
                    Vector2 scale = new Vector2(
                        generationParams.TileSpriteSize.X / mapTiles[x, y].Sprite.size.X, 
                        generationParams.TileSpriteSize.Y / mapTiles[x, y].Sprite.size.Y);
                    mapTiles[x, y].Sprite.Draw(spriteBatch, rectCenter + (mapPos + viewOffset) * zoom, Color.White,
                        origin: new Vector2(256.0f, 256.0f), rotate: 0, scale: scale * zoom, spriteEffect: mapTiles[x, y].SpriteEffect);
                }
            }
#if DEBUG
            if (generationParams.ShowNoiseMap)
            {
                GUI.DrawRectangle(spriteBatch, rectCenter + (borders.Location.ToVector2() + viewOffset) * zoom, borders.Size.ToVector2() * zoom, Color.White, true);
            }
#endif
            Vector2 topLeft = rectCenter + viewOffset * zoom;
            topLeft.X = (int)topLeft.X;
            topLeft.Y = (int)topLeft.Y;

            Vector2 bottomRight = rectCenter + (viewOffset + new Vector2(size,size)) * zoom;
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
            
            var sourceRect = rect;
            float rawNoiseScale = 1.0f + Noise[(int)(Timing.TotalTime * 100 % Noise.GetLength(0) - 1), (int)(Timing.TotalTime * 100 % Noise.GetLength(1) - 1)];
            cameraNoiseStrength = Noise[(int)(Timing.TotalTime * 10 % Noise.GetLength(0) - 1), (int)(Timing.TotalTime * 10 % Noise.GetLength(1) - 1)];

            rawNoiseSprite.DrawTiled(spriteBatch, rect.Location.ToVector2(), rect.Size.ToVector2(), 
                startOffset: new Point(Rand.Range(0,rawNoiseSprite.SourceRect.Width), Rand.Range(0, rawNoiseSprite.SourceRect.Height)),
                color : Color.White * cameraNoiseStrength * 0.5f,
                textureScale: Vector2.One * rawNoiseScale);

            rawNoiseSprite.DrawTiled(spriteBatch, rect.Location.ToVector2(), rect.Size.ToVector2(),
                startOffset: new Point(Rand.Range(0, rawNoiseSprite.SourceRect.Width), Rand.Range(0, rawNoiseSprite.SourceRect.Height)),
                color: new Color(20,20,20,100),
                textureScale: Vector2.One * rawNoiseScale * 2);

            if (generationParams.ShowLocations)
            {
                foreach (LocationConnection connection in connections)
                {
                    Color connectionColor;
                    if (GameMain.DebugDraw)
                    {
                        float sizeFactor = MathUtils.InverseLerp(
                           MapGenerationParams.Instance.SmallLevelConnectionLength,
                           MapGenerationParams.Instance.LargeLevelConnectionLength,
                           connection.Length);

                        connectionColor = ToolBox.GradientLerp(sizeFactor, Color.LightGreen, Color.Orange, Color.Red);
                    }
                    else
                    {
                        connectionColor = ToolBox.GradientLerp(connection.Difficulty / 100.0f, 
                            MapGenerationParams.Instance.LowDifficultyColor,
                            MapGenerationParams.Instance.MediumDifficultyColor,
                            MapGenerationParams.Instance.HighDifficultyColor);
                    }

                    int width = (int)(3 * zoom);

                    if (SelectedLocation != CurrentLocation &&
                        (connection.Locations.Contains(SelectedLocation) && connection.Locations.Contains(CurrentLocation)))
                    {
                        connectionColor = Color.Gold;
                        width *= 2;
                    }
                    else if (highlightedLocation != CurrentLocation &&
                    (connection.Locations.Contains(highlightedLocation) && connection.Locations.Contains(CurrentLocation)))
                    {
                        connectionColor = Color.Lerp(connectionColor, Color.White, 0.5f);
                        width *= 2;
                    }
                    else if (!connection.Passed)
                    {
                        //crackColor *= 0.5f;
                    }

                    for (int i = 0; i < connection.CrackSegments.Count; i++)
                    {
                        var segment = connection.CrackSegments[i];

                        Vector2 start = rectCenter + (segment[0] + viewOffset) * zoom;
                        Vector2 end = rectCenter + (segment[1] + viewOffset) * zoom;

                        if (!rect.Contains(start) && !rect.Contains(end))
                        {
                            continue;
                        }
                        else
                        {
                            if (MathUtils.GetLineRectangleIntersection(start, end, new Rectangle(rect.X, rect.Y + rect.Height, rect.Width, rect.Height), out Vector2 intersection))
                            {
                                if (!rect.Contains(start))
                                {
                                    start = intersection;
                                }
                                else
                                {
                                    end = intersection;
                                }
                            }
                        }

                        float distFromPlayer = Vector2.Distance(CurrentLocation.MapPosition, (segment[0] + segment[1]) / 2.0f);
                        float dist = Vector2.Distance(start, end);
                        
                        float a = GameMain.DebugDraw ? 1.0f : (200.0f - distFromPlayer) / 200.0f;
                        spriteBatch.Draw(generationParams.ConnectionSprite.Texture,
                            new Rectangle((int)start.X, (int)start.Y, (int)(dist - 1 * zoom), width),
                            null, connectionColor * MathHelper.Clamp(a, 0.1f, 0.5f), MathUtils.VectorToAngle(end - start),
                            new Vector2(0, 16), SpriteEffects.None, 0.01f);
                    }

                    if (GameMain.DebugDraw && zoom > 1.0f && generationParams.ShowLevelTypeNames)
                    {
                        Vector2 center = rectCenter + (connection.CenterPos + viewOffset) * zoom;
                        if (rect.Contains(center))
                        {
                            GUI.DrawString(spriteBatch, center, connection.Biome.Name + " (" + connection.Difficulty + ")", Color.White);
                        }
                    }
                }
                
                rect.Inflate(8, 8);
                GUI.DrawRectangle(spriteBatch, rect, Color.Black, false, 0.0f, 8);
                GUI.DrawRectangle(spriteBatch, rect, Color.LightGray);

                for (int i = 0; i < Locations.Count; i++)
                {
                    Location location = Locations[i];
                    Vector2 pos = rectCenter + (location.MapPosition + viewOffset) * zoom;
                    
                    Rectangle drawRect = location.Type.Sprite.SourceRect;
                    drawRect.X = (int)pos.X - drawRect.Width / 2;
                    drawRect.Y = (int)pos.Y - drawRect.Width / 2;

                    if (!rect.Intersects(drawRect)) { continue; }

                    Color color = location.Type.SpriteColor;                    
                    if (location.Connections.Find(c => c.Locations.Contains(CurrentLocation)) == null)
                    {
                        color *= 0.5f;
                    }

                    float iconScale = location == CurrentLocation ? 1.2f : 1.0f;
                    if (location == highlightedLocation)
                    {
                        iconScale *= 1.1f;
                        color = Color.Lerp(color, Color.White, 0.5f);
                    }
                    
                    float distFromPlayer = Vector2.Distance(CurrentLocation.MapPosition, location.MapPosition);
                    color *= MathHelper.Clamp((1000.0f - distFromPlayer) / 500.0f, 0.1f, 1.0f);

                    location.Type.Sprite.Draw(spriteBatch, pos, color, 
                        scale: MapGenerationParams.Instance.LocationIconSize / location.Type.Sprite.size.X * iconScale * zoom);
                    MapGenerationParams.Instance.LocationIndicator.Draw(spriteBatch, pos, color, 
                        scale: MapGenerationParams.Instance.LocationIconSize / MapGenerationParams.Instance.LocationIndicator.size.X * iconScale * zoom * 1.4f);            
                }

                //PLACEHOLDER until the stuff at the center of the map is implemented
                float centerIconSize = 50.0f;
                Vector2 centerPos = rectCenter + (new Vector2(size / 2) + viewOffset) * zoom;
                bool mouseOn = Vector2.Distance(PlayerInput.MousePosition, centerPos) < centerIconSize * zoom;

                var centerLocationType = LocationType.List.Last();
                Color centerColor = centerLocationType.SpriteColor * (mouseOn ? 1.0f : 0.6f);
                centerLocationType.Sprite.Draw(spriteBatch, centerPos, centerColor,
                    scale: centerIconSize / centerLocationType.Sprite.size.X * zoom);
                MapGenerationParams.Instance.LocationIndicator.Draw(spriteBatch, centerPos, centerColor,
                    scale: centerIconSize / MapGenerationParams.Instance.LocationIndicator.size.X * zoom * 1.2f);

                if (mouseOn && PlayerInput.LeftButtonClicked() && !messageBoxOpen)
                {
                    if (TextManager.ContainsTag("centerarealockedheader") && TextManager.ContainsTag("centerarealockedtext") )
                    {
                        var messageBox = new GUIMessageBox(
                            TextManager.Get("centerarealockedheader"),
                            TextManager.Get("centerarealockedtext"));
                        messageBoxOpen = true;
                        CoroutineManager.StartCoroutine(WaitForMessageBoxClosed(messageBox));
                    }
                    else
                    {
                        //if the message cannot be shown in the selected language, 
                        //show the campaign roadmap (which mentions the center location not being reachable)
                        var messageBox = new GUIMessageBox(TextManager.Get("CampaignRoadMapTitle"), TextManager.Get("CampaignRoadMapText"));
                        messageBoxOpen = true;
                        CoroutineManager.StartCoroutine(WaitForMessageBoxClosed(messageBox));
                    }
                }
            }
            
            DrawDecorativeHUD(spriteBatch, rect);

            for (int i = 0; i < 2; i++)
            {
                Location location = (i == 0) ? highlightedLocation : CurrentLocation;
                if (location == null) continue;

                Vector2 pos = rectCenter + (location.MapPosition + viewOffset) * zoom;
                pos.X += 25 * zoom;
                pos.Y -= 5 * zoom;
                Vector2 size = GUI.LargeFont.MeasureString(location.Name);
                GUI.Style.GetComponentStyle("OuterGlow").Sprites[GUIComponent.ComponentState.None][0].Draw(
                    spriteBatch, new Rectangle((int)pos.X - 30, (int)pos.Y, (int)size.X + 60, (int)(size.Y + 25 * GUI.Scale)), Color.Black * hudOpenState * 0.7f);
                GUI.DrawString(spriteBatch, pos, 
                    location.Name, Color.White * hudOpenState * 1.5f, font: GUI.LargeFont);
                GUI.DrawString(spriteBatch, pos + Vector2.UnitY * 25 * GUI.Scale, 
                    location.Type.Name, Color.White * hudOpenState * 1.5f);
            }
                        
            GameMain.Instance.GraphicsDevice.ScissorRectangle = prevScissorRect;
            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred);
        }

        private IEnumerable<object> WaitForMessageBoxClosed(GUIMessageBox box)
        {
            messageBoxOpen = true;
            while (GUIMessageBox.MessageBoxes.Contains(box)) yield return null;
            yield return new WaitForSeconds(.1f);
            messageBoxOpen = false;
        }

        private float hudOpenState;
        private float cameraNoiseStrength;

        private void DrawDecorativeHUD(SpriteBatch spriteBatch, Rectangle rect)
        {
            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, null, null, GameMain.ScissorTestEnable);
            
            if (generationParams.ShowOverlay)
            {
                Vector2 mapCenter = rect.Center.ToVector2() + (new Vector2(size, size) / 2 + drawOffset + drawOffsetNoise) * zoom;
                Vector2 centerDiff = CurrentLocation.MapPosition - new Vector2(size) / 2;
                int currentZone = (int)Math.Floor((centerDiff.Length() / (size * 0.5f) * generationParams.DifficultyZones));
                for (int i = 0; i < generationParams.DifficultyZones; i++)
                {
                    float radius = size / 2 * ((i + 1.0f) / generationParams.DifficultyZones);
                    float textureSize = (radius / (generationParams.MapCircle.size.X / 2) * zoom);
                    
                    generationParams.MapCircle.Draw(spriteBatch,
                        mapCenter, 
                        i == currentZone || i == currentZone - 1  ? Color.White * 0.5f : Color.White * 0.2f, 
                        i * 0.4f + (float)Timing.TotalTime * 0.01f, textureSize);
                }
            }

            float animPulsate = (float)Math.Sin(Timing.TotalTime * 2.0f) * 0.1f;

            Vector2 frameSize = generationParams.DecorativeGraphSprite.FrameSize.ToVector2();
            generationParams.DecorativeGraphSprite.Draw(spriteBatch, (int)((cameraNoiseStrength + animPulsate) * hudOpenState * generationParams.DecorativeGraphSprite.FrameCount),
                new Vector2(rect.Right, rect.Bottom), Color.White, frameSize, 0,
                Vector2.Divide(new Vector2(rect.Width / 4, rect.Height / 10), frameSize));

            /*frameSize = generationParams.DecorativeMapSprite.FrameSize.ToVector2();
            generationParams.DecorativeMapSprite.Draw(spriteBatch, (int)((cameraNoiseStrength + animPulsate) * hudOpenState * generationParams.DecorativeMapSprite.FrameCount),
                new Vector2(rect.X, rect.Y + rect.Height * 0.17f), Color.White, new Vector2(0, frameSize.Y * 0.2f), 0,
                Vector2.Divide(new Vector2(rect.Width / 3, rect.Height / 5), frameSize), spriteEffect: SpriteEffects.FlipVertically);

            GUI.DrawString(spriteBatch,
                new Vector2(rect.X + rect.Width / 15, rect.Y + rect.Height / 11),
                "JOVIAN FLUX " + ((cameraNoiseStrength + Rand.Range(-0.02f, 0.02f)) * 500), Color.White * hudOpenState, font: GUI.SmallFont);*/
            GUI.DrawString(spriteBatch,
                new Vector2(rect.X + rect.Width * 0.27f, rect.Y + rect.Height * 0.93f),
                "LAT " + (-drawOffset.Y / 100.0f) + "   LON " + (-drawOffset.X / 100.0f), Color.White * hudOpenState, font: GUI.SmallFont);

            System.Text.StringBuilder sb = new System.Text.StringBuilder("GEST F ");
            for (int i = 0; i < 20; i++)
            {
                sb.Append(Rand.Range(0.0f, 1.0f) < cameraNoiseStrength ? ToolBox.RandomSeed(1) : "0");
            }
            GUI.DrawString(spriteBatch,
                new Vector2(rect.X + rect.Width * 0.8f, rect.Y + rect.Height * 0.96f),
                sb.ToString(), Color.White * hudOpenState, font: GUI.SmallFont);

            frameSize = generationParams.DecorativeLineTop.FrameSize.ToVector2();
            generationParams.DecorativeLineTop.Draw(spriteBatch, (int)(hudOpenState * generationParams.DecorativeLineTop.FrameCount),
                new Vector2(rect.Right, rect.Y), Color.White, new Vector2(frameSize.X, frameSize.Y * 0.2f), 0,
                Vector2.Divide(new Vector2(rect.Width * 0.72f, rect.Height / 9), frameSize));
            frameSize = generationParams.DecorativeLineBottom.FrameSize.ToVector2();
            generationParams.DecorativeLineBottom.Draw(spriteBatch, (int)(hudOpenState * generationParams.DecorativeLineBottom.FrameCount),
                new Vector2(rect.X, rect.Bottom), Color.White, new Vector2(0, frameSize.Y * 0.6f), 0,
                Vector2.Divide(new Vector2(rect.Width * 0.72f, rect.Height / 9), frameSize));

            frameSize = generationParams.DecorativeLineCorner.FrameSize.ToVector2();
            generationParams.DecorativeLineCorner.Draw(spriteBatch, (int)((hudOpenState + animPulsate) * generationParams.DecorativeLineCorner.FrameCount),
                new Vector2(rect.Right - rect.Width / 8, rect.Bottom), Color.White, frameSize * 0.8f, 0,
                Vector2.Divide(new Vector2(rect.Width / 4, rect.Height / 4), frameSize), spriteEffect: SpriteEffects.FlipVertically);

            generationParams.DecorativeLineCorner.Draw(spriteBatch, (int)((hudOpenState + animPulsate) * generationParams.DecorativeLineCorner.FrameCount),
                new Vector2(rect.X + rect.Width / 8, rect.Y), Color.White, frameSize * 0.1f, 0,
                Vector2.Divide(new Vector2(rect.Width / 4, rect.Height / 4), frameSize), spriteEffect: SpriteEffects.FlipHorizontally);

            //reticles
            generationParams.ReticleLarge.Draw(spriteBatch, (int)(subReticleAnimState * generationParams.ReticleLarge.FrameCount),
                rect.Center.ToVector2() + (subReticlePosition + drawOffset - drawOffsetNoise * 2) * zoom, Color.White,
                generationParams.ReticleLarge.Origin, 0, Vector2.One * (float)Math.Sqrt(zoom) * 0.4f);
            generationParams.ReticleMedium.Draw(spriteBatch, (int)(subReticleAnimState * generationParams.ReticleMedium.FrameCount),
                rect.Center.ToVector2() + (subReticlePosition + drawOffset - drawOffsetNoise) * zoom, Color.White,
                generationParams.ReticleMedium.Origin, 0, new Vector2(1.0f, 0.7f) * (float)Math.Sqrt(zoom) * 0.4f);

            if (SelectedLocation != null)
            {
                generationParams.ReticleSmall.Draw(spriteBatch, (int)(targetReticleAnimState * generationParams.ReticleSmall.FrameCount),
                    rect.Center.ToVector2() + (SelectedLocation.MapPosition + drawOffset + drawOffsetNoise * 2) * zoom, Color.White,
                    generationParams.ReticleSmall.Origin, 0, new Vector2(1.0f, 0.7f) * (float)Math.Sqrt(zoom) * 0.4f);
            }

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, null, null, null, GameMain.ScissorTestEnable);
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
            drawOffset += new Vector2(
                (float)PerlinNoise.CalculatePerlin(Timing.TotalTime * 0.3f % 255, Timing.TotalTime * 0.4f % 255, 0) - 0.5f,
                (float)PerlinNoise.CalculatePerlin(Timing.TotalTime * 0.4f % 255, Timing.TotalTime * 0.3f % 255, 0.5f) - 0.5f) * 50.0f * (float)Math.Sin(t * MathHelper.Pi);

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

        partial void RemoveProjSpecific()
        {
            rawNoiseSprite?.Remove();
            rawNoiseSprite = null;

            rawNoiseTexture?.Dispose();
            rawNoiseTexture = null;

            noiseTexture?.Dispose();
            noiseTexture = null;
        }
    }
}
