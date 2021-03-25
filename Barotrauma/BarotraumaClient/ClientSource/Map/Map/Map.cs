using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    partial class Map
    {
        class MapAnim
        {
            public Location StartLocation;
            public Location EndLocation;
            public string StartMessage;
            public string EndMessage;

            /// <summary>
            /// Initial zoom (0 - 1, from min zoom to max)
            /// </summary>
            public float? StartZoom;
            /// <summary>
            /// Initial zoom (0 - 1, from min zoom to max)
            /// </summary>
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

        private readonly Queue<MapAnim> mapAnimQueue = new Queue<MapAnim>();

        public Location HighlightedLocation { get; private set; }

        private static Sprite noiseOverlay;

        public Vector2 DrawOffset;
        private Vector2 drawOffsetNoise;

        private Vector2 currLocationIndicatorPos;

        private float zoom = 3.0f;
        private float targetZoom;

        private Rectangle borders;
        
        private Sprite[,] mapTiles;
        private bool[,] tileDiscovered;

        private float connectionHighlightState;

        private (Rectangle targetArea, string tip)? tooltip;
        private string sanitizedTooltip;
        private List<RichTextData> tooltipRichTextData;
        private string prevTooltip;

        /*private (Rectangle targetArea, string tip)? connectionTooltip;
        private string sanitizedConnectionTooltip;
        private List<RichTextData> connectionTooltipRichTextData;
        private string prevConnectionTooltip;*/

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
                OnClicked = (btn, userData) =>
                {
                    Rand.SetSyncedSeed(ToolBox.StringToInt(this.Seed));
                    Generate();
                    InitProjectSpecific();
                    return true;
                }
            };
        }
#endif
        public Location CurrentDisplayLocation
        {
            get 
            {
                return GameMain.GameSession.Campaign.CurrentDisplayLocation;
            }
        }

        partial void InitProjectSpecific()
        {
            noiseOverlay ??= new Sprite("Content/UI/noise.png", Vector2.Zero);

            OnLocationChanged = LocationChanged;

            borders = new Rectangle(
                (int)Locations.Min(l => l.MapPosition.X),
                (int)Locations.Min(l => l.MapPosition.Y),
                (int)Locations.Max(l => l.MapPosition.X),
                (int)Locations.Max(l => l.MapPosition.Y));
            borders.Width -= borders.X;
            borders.Height -= borders.Y;

            if (CurrentLocation != null)
            {
                DrawOffset = -CurrentLocation.MapPosition;
            }

            Vector2 tileSize = generationParams.MapTiles.Values.First().First().size * generationParams.MapTileScale;
            int tilesX = (int)Math.Ceiling(Width / tileSize.X);
            int tilesY = (int)Math.Ceiling(Height / tileSize.Y);
            mapTiles = new Sprite[tilesX, tilesY];
            tileDiscovered = new bool[tilesX, tilesY];
            for (int x = 0; x < tilesX; x++)
            {
                for (int y = 0; y < tilesY; y++)
                {
                    var biome = GetBiome(x * tileSize.X);
                    var tileList = generationParams.MapTiles.ContainsKey(biome.Identifier) ?
                        generationParams.MapTiles[biome.Identifier] :
                        generationParams.MapTiles.Values.First();
                    mapTiles[x, y] = tileList[x % tileList.Count];                    
                }
            }

            RemoveFogOfWar(StartLocation);

            GenerateLocationConnectionVisuals();
        }

        partial void GenerateLocationConnectionVisuals()
        {
            foreach (LocationConnection connection in Connections)
            {
                Vector2 connectionStart = connection.Locations[0].MapPosition;
                Vector2 connectionEnd = connection.Locations[1].MapPosition;
                float connectionLength = Vector2.Distance(connectionStart, connectionEnd);
                int iterations = Math.Min((int)Math.Sqrt(connectionLength * generationParams.ConnectionIndicatorIterationMultiplier), 5);
                connection.CrackSegments.Clear();
                connection.CrackSegments.AddRange(MathUtils.GenerateJaggedLine(
                    connectionStart, connectionEnd,
                    iterations, connectionLength * generationParams.ConnectionIndicatorDisplacementMultiplier));
            }
        }

        private void LocationChanged(Location prevLocation, Location newLocation)
        {
            if (prevLocation == newLocation) return;
            //focus on starting location
            if (prevLocation != null)
            {
                mapAnimQueue.Enqueue(new MapAnim()
                {
                    EndZoom = 1.0f,
                    EndLocation = prevLocation,
                    Duration = MathHelper.Clamp(Vector2.Distance(-DrawOffset, prevLocation.MapPosition) / 1000.0f, 0.1f, 0.5f)
                });
                mapAnimQueue.Enqueue(new MapAnim()
                {
                    EndZoom = 0.5f,
                    StartLocation = prevLocation,
                    EndLocation = newLocation,
                    Duration = 2.0f,
                    StartDelay = 0.5f
                });
            }
            else
            {
                currLocationIndicatorPos = CurrentLocation.MapPosition;
            }

            RemoveFogOfWar(newLocation);
        }

        private void RemoveFogOfWar(Location location, bool removeFromAdjacentLocations = true)
        {
            if (location == null) { return; }
            Vector2 mapTileSize = mapTiles[0, 0].size * generationParams.MapTileScale;
            int startX = (int)Math.Max(Math.Floor(location.MapPosition.X / mapTileSize.X - 0.25f), 0);
            int startY = (int)Math.Max(Math.Floor(location.MapPosition.Y / mapTileSize.Y - 0.25f), 0);
            int endX = (int)Math.Min(Math.Floor(location.MapPosition.X / mapTileSize.X + 0.25f), mapTiles.GetLength(0));
            int endY = (int)Math.Min(Math.Floor(location.MapPosition.Y / mapTileSize.Y + 0.25f), mapTiles.GetLength(1));
            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    tileDiscovered[x, y] = true;
                }
            }
            if (removeFromAdjacentLocations)
            {
                foreach (LocationConnection c in location.Connections)
                {
                    var otherLocation = c.OtherLocation(location);
                    RemoveFogOfWar(otherLocation, removeFromAdjacentLocations: false);
                }
            }
        }

        private bool IsInFogOfWar(Location location)
        {
            if (GameMain.DebugDraw) { return false; }
            Vector2 mapTileSize = mapTiles[0, 0].size * generationParams.MapTileScale;
            int x = (int)Math.Floor(location.MapPosition.X / mapTileSize.X);
            int y = (int)Math.Floor(location.MapPosition.Y / mapTileSize.Y);

            return !tileDiscovered[MathHelper.Clamp(x, 0, tileDiscovered.Length), MathHelper.Clamp(y, 0, tileDiscovered.Length)];
        }

        partial void ChangeLocationTypeProjSpecific(Location location, string prevName, LocationTypeChange change)
        {
            if (change.Messages.Any())
            {
                string msg = change.Messages[Rand.Range(0, change.Messages.Count)]
                    .Replace("[previousname]", $"‖color:gui.orange‖{prevName}‖end‖")
                    .Replace("[name]", $"‖color:gui.orange‖{location.Name}‖end‖");
                location.LastTypeChangeMessage = msg;
                if (GameMain.Client != null)
                {
                    GameMain.Client.AddChatMessage(msg, Networking.ChatMessageType.Default, TextManager.Get("RadioAnnouncerName"));
                }
                else
                {
                    GameMain.GameSession?.GameMode.CrewManager.AddSinglePlayerChatMessage(
                        TextManager.Get("RadioAnnouncerName"),
                        msg,
                        Networking.ChatMessageType.Default,
                        sender: null);
                }
            }         
        }

        partial void ClearAnimQueue()
        {
            mapAnimQueue.Clear();
        }

        public void Update(float deltaTime, GUICustomComponent mapContainer)
        {
            Rectangle rect = mapContainer.Rect;

            if (CurrentDisplayLocation != null)
            {
                if (!CurrentDisplayLocation.Discovered)
                {
                    RemoveFogOfWar(CurrentDisplayLocation);
                    CurrentDisplayLocation.Discovered = true;
                    if (CurrentDisplayLocation.MapPosition.X > furthestDiscoveredLocation.MapPosition.X)
                    {
                        furthestDiscoveredLocation = CurrentDisplayLocation;
                    }
                }
            }

            Vector2 currentPosition = CurrentDisplayLocation.MapPosition;
            if (Level.Loaded?.Type == LevelData.LevelType.LocationConnection && Level.Loaded.StartLocation != null && Level.Loaded.EndLocation != null)
            {
                Vector2 startPos = CurrentDisplayLocation == Level.Loaded.StartLocation ? Level.Loaded.StartLocation.MapPosition : Level.Loaded.EndLocation.MapPosition;
                int moveDir = CurrentDisplayLocation == Level.Loaded.StartLocation ? 1 : -1;

                Vector2 diff = Level.Loaded.EndLocation.MapPosition - Level.Loaded.StartLocation.MapPosition;
                currentPosition = startPos + 
                    Vector2.Normalize(diff) * Math.Min(100, diff.Length() * 0.2f) * moveDir;
            }
            else
            {
                currentPosition += Vector2.UnitY * 35;
            }

            currLocationIndicatorPos = Vector2.Lerp(currLocationIndicatorPos, currentPosition, deltaTime);
#if DEBUG
            if (GameMain.DebugDraw)
            {
                if (editor == null) CreateEditor();
                editor.AddToGUIUpdateList(order: 1);
            }

            if (PlayerInput.KeyHit(Keys.Space))
            {
                Radiation?.OnStep();
            }
#endif

            Radiation?.MapUpdate(deltaTime);

            if (mapAnimQueue.Count > 0)
            {
                hudVisibility = Math.Max(hudVisibility - deltaTime, 0.0f);
                UpdateMapAnim(mapAnimQueue.Peek(), deltaTime);
                if (mapAnimQueue.Peek().Finished)
                {
                    mapAnimQueue.Dequeue();
                }
                return;
            }

            hudVisibility = Math.Min(hudVisibility + deltaTime, 0.75f + (float)Math.Sin(Timing.TotalTime * 3.0f) * 0.25f);
            
            Vector2 rectCenter = new Vector2(rect.Center.X, rect.Center.Y);
            Vector2 viewOffset = DrawOffset + drawOffsetNoise;

            float closestDist = 0.0f;
            HighlightedLocation = null;
            if (GUI.MouseOn == null || GUI.MouseOn == mapContainer)
            {
                for (int i = 0; i < Locations.Count; i++)
                {
                    Location location = Locations[i];
                    if (IsInFogOfWar(location) && !(CurrentDisplayLocation?.Connections.Any(c => c.Locations.Contains(location)) ?? false) && !GameMain.DebugDraw) { continue; }

                    Vector2 pos = rectCenter + (location.MapPosition + viewOffset) * zoom;
                    if (!rect.Contains(pos)) { continue; }

                    Sprite locationSprite = location.IsCriticallyRadiated() ? location.Type.RadiationSprite ?? location.Type.Sprite : location.Type.Sprite;
                    float iconScale = generationParams.LocationIconSize / locationSprite.size.X;
                    if (location == CurrentDisplayLocation) { iconScale *= 1.2f; }

                    Rectangle drawRect = locationSprite.SourceRect;
                    drawRect.Width = (int)(drawRect.Width * iconScale * zoom * 1.4f);
                    drawRect.Height = (int)(drawRect.Height * iconScale * zoom * 1.4f);
                    drawRect.X = (int)pos.X - drawRect.Width / 2;
                    drawRect.Y = (int)pos.Y - drawRect.Width / 2;

                    if (!drawRect.Contains(PlayerInput.MousePosition)) { continue; }

                    float dist = Vector2.Distance(PlayerInput.MousePosition, pos);
                    if (HighlightedLocation == null || dist < closestDist)
                    {
                        closestDist = dist;
                        HighlightedLocation = location;
                    }
                }
            }

            if (SelectedConnection != null)
            {
                connectionHighlightState = Math.Min(connectionHighlightState + deltaTime, 1.0f);
            }
            else
            {
                connectionHighlightState = 0.0f;
            }

            if (GUI.KeyboardDispatcher.Subscriber == null)
            {
                float moveSpeed = 1000.0f;
                Vector2 moveAmount = Vector2.Zero;
                if (PlayerInput.KeyDown(InputType.Left)) { moveAmount += Vector2.UnitX; }
                if (PlayerInput.KeyDown(InputType.Right)) { moveAmount -= Vector2.UnitX; }
                if (PlayerInput.KeyDown(InputType.Up)) { moveAmount += Vector2.UnitY; }
                if (PlayerInput.KeyDown(InputType.Down)) { moveAmount -= Vector2.UnitY; }
                DrawOffset += moveAmount * moveSpeed / zoom * deltaTime;
            }

            targetZoom = MathHelper.Clamp(targetZoom, generationParams.MinZoom, generationParams.MaxZoom);
            zoom = MathHelper.Lerp(zoom, targetZoom * GUI.Scale, 0.1f);

            if (GUI.MouseOn == mapContainer)
            {
                foreach (LocationConnection connection in Connections)
                {
                    if (HighlightedLocation != CurrentDisplayLocation &&
                        connection.Locations.Contains(HighlightedLocation) && 
                        connection.Locations.Contains(CurrentDisplayLocation))
                    {
                        if (PlayerInput.PrimaryMouseButtonClicked() &&
                            SelectedLocation != HighlightedLocation && HighlightedLocation != null)
                        {
                            if (connection.Locked)
                            {
                                new GUIMessageBox(string.Empty, TextManager.Get("LockedPathTooltip"));
                            }
                            //clients aren't allowed to select the location without a permission
                            else if ((GameMain.GameSession?.GameMode as CampaignMode)?.AllowedToManageCampaign() ?? false)
                            {
                                connectionHighlightState = 0.0f;
                                SelectedConnection = connection;
                                SelectedLocation = HighlightedLocation;

                                OnLocationSelected?.Invoke(SelectedLocation, SelectedConnection);
                                GameMain.Client?.SendCampaignState();
                            }
                        }
                    }
                }            

                targetZoom += PlayerInput.ScrollWheelSpeed / 500.0f;

                if (PlayerInput.MidButtonHeld() || (HighlightedLocation == null && PlayerInput.PrimaryMouseButtonHeld()))
                {
                    DrawOffset += PlayerInput.MouseSpeed / zoom;
                }
                if (AllowDebugTeleport)
                {
                    if (PlayerInput.DoubleClicked() && HighlightedLocation != null)
                    {
                        var passedConnection = CurrentDisplayLocation.Connections.Find(c => c.OtherLocation(CurrentDisplayLocation) == HighlightedLocation);
                        if (passedConnection != null)
                        {
                            passedConnection.Passed = true;
                        }

                        Location prevLocation = CurrentDisplayLocation;
                        CurrentLocation = HighlightedLocation;
                        Level.Loaded.DebugSetStartLocation(CurrentLocation);
                        Level.Loaded.DebugSetEndLocation(null);

                        CurrentLocation.Discovered = true;
                        OnLocationChanged?.Invoke(prevLocation, CurrentLocation);
                        SelectLocation(-1);
                        if (GameMain.Client == null)
                        {
                            CurrentLocation.CreateStore();
                            ProgressWorld();
                            Radiation.OnStep(1);
                        }
                        else
                        {
                            GameMain.Client.SendCampaignState();
                        }
                    }

                    if (PlayerInput.KeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift) && PlayerInput.PrimaryMouseButtonClicked() && HighlightedLocation != null)
                    {
                        int distance = DistanceToClosestLocationWithOutpost(HighlightedLocation, out Location foundLocation);
                        DebugConsole.NewMessage($"Distance to closest outpost from {HighlightedLocation.Name} to {foundLocation?.Name} is {distance}");
                    }

                    if (PlayerInput.PrimaryMouseButtonClicked() && HighlightedLocation == null)
                    {
                        SelectLocation(-1);
                    }
                }
            }
        }
        
        public void Draw(SpriteBatch spriteBatch, GUICustomComponent mapContainer)
        {
            tooltip = null;

            Rectangle rect = mapContainer.Rect;

            Vector2 viewSize = new Vector2(rect.Width / zoom, rect.Height / zoom);
            Vector2 edgeBuffer = new Vector2(rect.Width * 0.05f);
            DrawOffset.X = MathHelper.Clamp(DrawOffset.X, -Width - edgeBuffer.X + viewSize.X / 2.0f, edgeBuffer.X - viewSize.X / 2.0f);
            DrawOffset.Y = MathHelper.Clamp(DrawOffset.Y, -Height - edgeBuffer.Y + viewSize.Y / 2.0f, edgeBuffer.Y - viewSize.Y / 2.0f);

            drawOffsetNoise = new Vector2(
                (float)PerlinNoise.CalculatePerlin(Timing.TotalTime * 0.1f % 255, Timing.TotalTime * 0.1f % 255, 0) - 0.5f, 
                (float)PerlinNoise.CalculatePerlin(Timing.TotalTime * 0.2f % 255, Timing.TotalTime * 0.2f % 255, 0.5f) - 0.5f) * 10.0f;

            Vector2 viewOffset = DrawOffset + drawOffsetNoise;

            Vector2 rectCenter = new Vector2(rect.Center.X, rect.Center.Y);

            Rectangle prevScissorRect = GameMain.Instance.GraphicsDevice.ScissorRectangle;
            spriteBatch.End();
            spriteBatch.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(prevScissorRect, rect);
            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);

            Vector2 topLeft = rectCenter + viewOffset;
            Vector2 bottomRight = rectCenter + (viewOffset + new Vector2(Width, Height));
            Vector2 mapTileSize = mapTiles[0, 0].size * generationParams.MapTileScale;

            int startX = (int)Math.Floor(-topLeft.X / mapTileSize.X) - 1;
            int startY = (int)Math.Floor(-topLeft.Y / mapTileSize.Y) - 1;
            int endX = (int)Math.Ceiling((-topLeft.X + rect.Width) / mapTileSize.X);
            int endY = (int)Math.Ceiling((-topLeft.Y + rect.Height) / mapTileSize.Y);

            float noiseT = (float)(Timing.TotalTime * 0.01f);
            cameraNoiseStrength = (float)PerlinNoise.CalculatePerlin(noiseT, noiseT * 0.5f, noiseT * 0.2f);
            float noiseScale = (float)PerlinNoise.CalculatePerlin(noiseT * 5.0f, noiseT * 2.0f, 0) * 5.0f;

            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    int tileX = Math.Abs(x) % mapTiles.GetLength(0);
                    int tileY = Math.Abs(y) % mapTiles.GetLength(1);
                    Vector2 tilePos = rectCenter + (viewOffset + new Vector2(x, y) * mapTileSize) * zoom;
                    mapTiles[tileX, tileY].Draw(spriteBatch, tilePos, Color.White, origin: Vector2.Zero, scale: generationParams.MapTileScale * zoom);

                    if (GameMain.DebugDraw) { continue; }
                    if (!tileDiscovered[tileX, tileY] || x < 0 || y < 0 || x >= tileDiscovered.GetLength(0) || y >= tileDiscovered.GetLength(1))
                    {
                        generationParams.FogOfWarSprite?.Draw(spriteBatch, tilePos, Color.White * cameraNoiseStrength, origin: Vector2.Zero, scale: generationParams.MapTileScale * zoom);
                        noiseOverlay.DrawTiled(spriteBatch, tilePos, mapTileSize * zoom,
                            startOffset: new Vector2(Rand.Range(0.0f, noiseOverlay.SourceRect.Width), Rand.Range(0.0f, noiseOverlay.SourceRect.Height)),
                            color: Color.White * cameraNoiseStrength * 0.2f,
                            textureScale: Vector2.One * noiseScale);
                    }
                }
            }

            if (GameMain.DebugDraw)
            {
                if (topLeft.X > rect.X)
                    GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X, rect.Y, (int)(topLeft.X - rect.X), rect.Height), Color.Black * 0.5f, true);
                if (topLeft.Y > rect.Y)
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)topLeft.X, rect.Y, (int)(bottomRight.X - topLeft.X), (int)(topLeft.Y - rect.Y)), Color.Black * 0.5f, true);
                if (bottomRight.X < rect.Right)
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)bottomRight.X, rect.Y, (int)(rect.Right - bottomRight.X), rect.Height), Color.Black * 0.5f, true);
                if (bottomRight.Y < rect.Bottom)
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)topLeft.X, (int)bottomRight.Y, (int)(bottomRight.X - topLeft.X), (int)(rect.Bottom - bottomRight.Y)), Color.Black * 0.5f, true);
            }

            float rawNoiseScale = 1.0f + PerlinNoise.GetPerlin((int)(Timing.TotalTime * 1 - 1), (int)(Timing.TotalTime * 1 - 1));
            DrawNoise(spriteBatch, rect, rawNoiseScale);

            Radiation.Draw(spriteBatch, rect, zoom);

            if (generationParams.ShowLocations)
            {
                foreach (LocationConnection connection in Connections)
                {
                    if (IsInFogOfWar(connection.Locations[0]) && IsInFogOfWar(connection.Locations[1])) { continue; }
                    DrawConnection(spriteBatch, connection, rect, viewOffset);
                }
                
                for (int i = 0; i < Locations.Count; i++)
                {
                    Location location = Locations[i];
                    if (IsInFogOfWar(location)) { continue; }
                    Vector2 pos = rectCenter + (location.MapPosition + viewOffset) * zoom;

                    Sprite locationSprite = location.IsCriticallyRadiated() ? location.Type.RadiationSprite ?? location.Type.Sprite : location.Type.Sprite;

                    Rectangle drawRect = locationSprite.SourceRect;
                    drawRect.X = (int)pos.X - drawRect.Width / 2;
                    drawRect.Y = (int)pos.Y - drawRect.Width / 2;

                    if (!rect.Intersects(drawRect)) { continue; }

                    Color color = location.Type.SpriteColor;
                    if (!location.Discovered) { color = Color.White; }
                    if (location.Connections.Find(c => c.Locations.Contains(CurrentDisplayLocation)) == null)
                    {
                        color *= 0.5f;
                    }

                    float iconScale = location == CurrentDisplayLocation ? 1.2f : 1.0f;
                    if (location == HighlightedLocation)
                    {
                        iconScale *= 1.2f;
                    }

                    locationSprite.Draw(spriteBatch, pos, color, 
                        scale: generationParams.LocationIconSize / locationSprite.size.X * iconScale * zoom);

                    if (location == CurrentDisplayLocation)
                    {
                        if (SelectedLocation != null)
                        {
                            Vector2 dir = Vector2.Normalize(SelectedLocation.MapPosition - currLocationIndicatorPos);                                
                            GUI.Arrow.Draw(spriteBatch, 
                                rectCenter + (currLocationIndicatorPos + viewOffset) * zoom + dir * generationParams.LocationIconSize * 0.6f * zoom,
                                generationParams.IndicatorColor,
                                GUI.Arrow.Origin,
                                rotate: MathUtils.VectorToAngle(dir) + MathHelper.PiOver2,
                                new Vector2(0.5f, 1.0f) * zoom);
                        }  
                        generationParams.CurrentLocationIndicator.Draw(spriteBatch,
                            rectCenter + (currLocationIndicatorPos + viewOffset) * zoom,
                            generationParams.IndicatorColor,
                            generationParams.CurrentLocationIndicator.Origin, 0, Vector2.One * (generationParams.LocationIconSize / generationParams.CurrentLocationIndicator.size.X) * 0.8f * zoom);
                                                  
                    }

                    if (location == SelectedLocation)
                    {
                        generationParams.SelectedLocationIndicator.Draw(spriteBatch,
                            rectCenter + (location.MapPosition + viewOffset) * zoom,
                            generationParams.IndicatorColor,
                            generationParams.SelectedLocationIndicator.Origin, 0, Vector2.One * (generationParams.LocationIconSize / generationParams.SelectedLocationIndicator.size.X) * 1.7f * zoom);
                    }

                    if (location.TimeSinceLastTypeChange < 1 && !string.IsNullOrEmpty(location.LastTypeChangeMessage) && generationParams.TypeChangeIcon != null)
                    {
                        Vector2 typeChangeIconPos = pos + new Vector2(1.35f, -0.35f) * generationParams.LocationIconSize * 0.5f * zoom;
                        float typeChangeIconScale = 18.0f / generationParams.TypeChangeIcon.SourceRect.Width;
                        generationParams.TypeChangeIcon.Draw(spriteBatch, typeChangeIconPos, GUI.Style.Red, scale: typeChangeIconScale * zoom);
                        if (Vector2.Distance(PlayerInput.MousePosition, typeChangeIconPos) < generationParams.TypeChangeIcon.SourceRect.Width * zoom &&
                            (tooltip == null || IsPreferredTooltip(typeChangeIconPos)))
                        {
                            tooltip = (new Rectangle(typeChangeIconPos.ToPoint(), new Point(30)), location.LastTypeChangeMessage);
                        }
                    }
                    if (location != CurrentLocation && CurrentLocation.AvailableMissions.Any(m => m.Locations.Contains(location)) && generationParams.MissionIcon != null)
                    {
                        Vector2 missionIconPos = pos + new Vector2(1.35f, 0.35f) * generationParams.LocationIconSize * 0.5f * zoom;
                        float missionIconScale = 18.0f / generationParams.MissionIcon.SourceRect.Width;
                        generationParams.MissionIcon.Draw(spriteBatch, missionIconPos, generationParams.IndicatorColor, scale: missionIconScale * zoom);
                        if (Vector2.Distance(PlayerInput.MousePosition, missionIconPos) < generationParams.MissionIcon.SourceRect.Width * zoom && IsPreferredTooltip(missionIconPos))
                        {
                            var availableMissions = CurrentLocation.AvailableMissions.Where(m => m.Locations.Contains(location));
                            tooltip = (new Rectangle(missionIconPos.ToPoint(), new Point(30)), TextManager.Get("mission") + '\n'+ string.Join('\n', availableMissions.Select(m => "- " + m.Name)));
                        }
                    }

                    if (GameMain.DebugDraw && location == HighlightedLocation && (!location.Discovered || !location.HasOutpost()))
                    {
                        if (location.Reputation != null)
                        {
                            Vector2 dPos = pos;
                            dPos.Y += 48;
                            string name = $"Reputation: {location.Name}";
                            Vector2 nameSize = GUI.SmallFont.MeasureString(name);
                            GUI.DrawString(spriteBatch, dPos, name, Color.White, Color.Black * 0.8f, 4, font: GUI.SmallFont);
                            dPos.Y += nameSize.Y + 16;

                            Rectangle bgRect = new Rectangle((int)dPos.X, (int)dPos.Y, 256, 32);
                            bgRect.Inflate(8,8);
                            Color barColor = ToolBox.GradientLerp(location.Reputation.NormalizedValue, Color.Red, Color.Yellow, Color.LightGreen);
                            GUI.DrawRectangle(spriteBatch, bgRect, Color.Black * 0.8f, isFilled: true);
                            GUI.DrawRectangle(spriteBatch, new Rectangle((int)dPos.X, (int)dPos.Y, (int)(location.Reputation.NormalizedValue * 255), 32), barColor, isFilled: true);
                            string reputationValue = ((int)location.Reputation.Value).ToString();
                            Vector2 repValueSize = GUI.SubHeadingFont.MeasureString(reputationValue);
                            GUI.DrawString(spriteBatch, dPos + (new Vector2(256, 32) / 2) - (repValueSize / 2), reputationValue, Color.White, Color.Black, font: GUI.SubHeadingFont);
                            GUI.DrawRectangle(spriteBatch, new Rectangle((int)dPos.X, (int)dPos.Y, 256, 32), Color.White);
                        }
                    }
                }
            }

            DrawDecorativeHUD(spriteBatch, rect);

            bool drawRadiationTooltip = true;
            
            if (tooltip != null)
            {
                if (tooltip.Value.tip != prevTooltip)
                {
                    prevTooltip = tooltip.Value.tip;
                    tooltipRichTextData = RichTextData.GetRichTextData(tooltip.Value.tip, out sanitizedTooltip);
                }
                GUIComponent.DrawToolTip(spriteBatch, sanitizedTooltip, tooltip.Value.targetArea, tooltipRichTextData);
                drawRadiationTooltip = false;
            }
            else if (HighlightedLocation != null)
            {
                drawRadiationTooltip = false;
                Vector2 pos = rectCenter + (HighlightedLocation.MapPosition + viewOffset) * zoom;
                pos.X += 50 * zoom;
                pos.X = (int)pos.X;
                pos.Y = (int)pos.Y;
                Vector2 nameSize = GUI.LargeFont.MeasureString(HighlightedLocation.Name);
                Vector2 typeSize = string.IsNullOrEmpty(HighlightedLocation.Type.Name) ? Vector2.Zero : GUI.Font.MeasureString(HighlightedLocation.Type.Name);
                Vector2 size = new Vector2(Math.Max(nameSize.X, typeSize.X), nameSize.Y + typeSize.Y);
                bool showReputation = hudVisibility > 0.0f && HighlightedLocation.Discovered && HighlightedLocation.Type.HasOutpost && HighlightedLocation.Reputation != null;
                string repLabelText = null, repValueText = null;
                Vector2 repLabelSize = Vector2.Zero, repBarSize = Vector2.Zero;
                if (showReputation)
                {
                    repLabelText = TextManager.Get("reputation");
                    repLabelSize = GUI.Font.MeasureString(repLabelText);
                    repBarSize = new Vector2(GUI.IntScale(200), repLabelSize.Y);
                    size.Y += 2 * repLabelSize.Y + GUI.IntScale(5) + repBarSize.Y;
                    repValueText = HighlightedLocation.Reputation.GetFormattedReputationText(addColorTags: false);
                    size.X = Math.Max(size.X, repBarSize.X + GUI.Font.MeasureString(repValueText).X + GUI.IntScale(10));
                }
                GUI.Style.GetComponentStyle("OuterGlow").Sprites[GUIComponent.ComponentState.None][0].Draw(
                    spriteBatch, new Rectangle((int)(pos.X - 60 * GUI.Scale), (int)(pos.Y - size.Y), (int)(size.X + 120 * GUI.Scale), (int)(size.Y * 2.2f)), Color.Black * hudVisibility);
                var topLeftPos = pos - new Vector2(0.0f, size.Y / 2);
                GUI.DrawString(spriteBatch, topLeftPos, HighlightedLocation.Name, GUI.Style.TextColor * hudVisibility * 1.5f, font: GUI.LargeFont);
                topLeftPos += new Vector2(0.0f, nameSize.Y);
                GUI.DrawString(spriteBatch, topLeftPos, HighlightedLocation.Type.Name, GUI.Style.TextColor * hudVisibility * 1.5f);
                if (showReputation)
                {
                    topLeftPos += new Vector2(0.0f, typeSize.Y + repLabelSize.Y);
                    GUI.DrawString(spriteBatch, topLeftPos, repLabelText, GUI.Style.TextColor * hudVisibility * 1.5f);
                    topLeftPos += new Vector2(0.0f, repLabelSize.Y + GUI.IntScale(10));
                    Rectangle repBarRect = new Rectangle(new Point((int)topLeftPos.X, (int)topLeftPos.Y), new Point((int)repBarSize.X, (int)repBarSize.Y));
                    RoundSummary.DrawReputationBar(spriteBatch, repBarRect, HighlightedLocation.Reputation.NormalizedValue);
                    GUI.DrawString(spriteBatch, new Vector2(repBarRect.Right + GUI.IntScale(5), repBarRect.Top), repValueText, Reputation.GetReputationColor(HighlightedLocation.Reputation.NormalizedValue));
                }
            }

            if (drawRadiationTooltip)
            {
                Radiation.DrawFront(spriteBatch);
            }

            spriteBatch.End();
            GameMain.Instance.GraphicsDevice.ScissorRectangle = prevScissorRect;
            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
        }

        public static void DrawNoise(SpriteBatch spriteBatch, Rectangle rect, float strength)
        {
            noiseOverlay ??= new Sprite("Content/UI/noise.png", Vector2.Zero);

            float noiseT = (float)(Timing.TotalTime * 0.01f);
            float noiseScale = (float)PerlinNoise.CalculatePerlin(noiseT * 5.0f, noiseT * 2.0f, 0) * 5.0f;

            float rawNoiseScale = 1.0f + GetPerlinNoise();

            noiseOverlay.DrawTiled(spriteBatch, rect.Location.ToVector2(), rect.Size.ToVector2(), 
                startOffset: new Vector2(Rand.Range(0.0f, noiseOverlay.SourceRect.Width), Rand.Range(0.0f, noiseOverlay.SourceRect.Height)),
                color : Color.White * strength * 0.1f,
                textureScale: Vector2.One * rawNoiseScale);

            noiseOverlay.DrawTiled(spriteBatch, rect.Location.ToVector2(), rect.Size.ToVector2(),
                startOffset: new Vector2(Rand.Range(0.0f, noiseOverlay.SourceRect.Width), Rand.Range(0.0f, noiseOverlay.SourceRect.Height)),
                color: new Color(20,20,20,50),
                textureScale: Vector2.One * rawNoiseScale * 2);

            noiseOverlay.DrawTiled(spriteBatch, Vector2.Zero, new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight),
                startOffset: new Vector2(Rand.Range(0.0f, noiseOverlay.SourceRect.Width), Rand.Range(0.0f, noiseOverlay.SourceRect.Height)),
                color: Color.White * strength * 0.1f,
                textureScale: Vector2.One * noiseScale);
        }

        private static float GetPerlinNoise() => PerlinNoise.GetPerlin((int)(Timing.TotalTime * 1 - 1), (int)(Timing.TotalTime * 1 - 1));

        private void DrawConnection(SpriteBatch spriteBatch, LocationConnection connection, Rectangle viewArea, Vector2 viewOffset, Color? overrideColor = null)
        {
            Color connectionColor;
            if (GameMain.DebugDraw)
            {
                float sizeFactor = MathUtils.InverseLerp(
                   generationParams.SmallLevelConnectionLength,
                   generationParams.LargeLevelConnectionLength,
                   connection.Length);
                connectionColor = ToolBox.GradientLerp(sizeFactor, Color.LightGreen, GUI.Style.Orange, GUI.Style.Red);
            }
            else if (overrideColor.HasValue)
            {
                connectionColor = overrideColor.Value;
            }
            else
            {
                connectionColor = connection.Passed ? generationParams.ConnectionColor : generationParams.UnvisitedConnectionColor;
            }

            int width = (int)(generationParams.LocationConnectionWidth * zoom);

            //current level
            if (Level.Loaded?.LevelData == connection.LevelData)
            {
                connectionColor = generationParams.HighlightedConnectionColor;
                width = (int)(width * 1.5f);
            }
            //selected connection
            if (SelectedLocation != CurrentDisplayLocation &&
                connection.Locations.Contains(SelectedLocation) && connection.Locations.Contains(CurrentDisplayLocation))
            {
                connectionColor = generationParams.HighlightedConnectionColor;
                width *= 2;
            }
            //highlighted connection
            else if (HighlightedLocation != CurrentDisplayLocation &&
                    connection.Locations.Contains(HighlightedLocation) && connection.Locations.Contains(CurrentDisplayLocation))
            {
                connectionColor = generationParams.HighlightedConnectionColor;
                width *= 2;
            }

            Vector2 rectCenter = viewArea.Center.ToVector2();

            int startIndex = connection.CrackSegments.Count > 2 ? 1 : 0;
            int endIndex = connection.CrackSegments.Count > 2 ? connection.CrackSegments.Count - 1 : connection.CrackSegments.Count;

            Vector2? connectionStart = null;
            Vector2? connectionEnd = null;
            for (int i = startIndex; i < endIndex; i++)
            {
                var segment = connection.CrackSegments[i];

                Vector2 start = rectCenter + (segment[0] + viewOffset) * zoom;
                if (!connectionStart.HasValue) { connectionStart = start; }
                Vector2 end =  rectCenter + (segment[1] + viewOffset) * zoom;
                connectionEnd = end;

                if (!viewArea.Contains(start) && !viewArea.Contains(end))
                {
                    continue;
                }
                else
                {
                    if (MathUtils.GetLineRectangleIntersection(start, end, new Rectangle(viewArea.X, viewArea.Y + viewArea.Height, viewArea.Width, viewArea.Height), out Vector2 intersection))
                    {
                        if (!viewArea.Contains(start))
                        {
                            start = intersection;
                        }
                        else
                        {
                            end = intersection;
                        }
                    }
                }

                float a = 1.0f;
                if (!connection.Locations[0].Discovered && !connection.Locations[1].Discovered)
                {
                    if (IsInFogOfWar(connection.Locations[0]))
                    {
                        a = (float)i / connection.CrackSegments.Count;
                    }
                    else if (IsInFogOfWar(connection.Locations[1]))
                    {
                        a = 1.0f - (float)i / connection.CrackSegments.Count;
                    }
                }
                float dist = Vector2.Distance(start, end);
                var connectionSprite = connection.Passed ? generationParams.PassedConnectionSprite : generationParams.ConnectionSprite;

                Color segmentColor = connectionColor;
                int segmentWidth = width;
                if (connection == SelectedConnection)
                {
                    float t = (i - startIndex) / (float)(endIndex - startIndex - 1);
                    if (CurrentDisplayLocation == connection.Locations[1]) { t = 1.0f - t; }
                    if (t > connectionHighlightState) 
                    { 
                        segmentWidth /= 2; 
                        segmentColor = connection.Passed ? generationParams.ConnectionColor : generationParams.UnvisitedConnectionColor; 
                    }
                    else 
                    { 
                    }
                }

                spriteBatch.Draw(connectionSprite.Texture,
                    new Rectangle((int)start.X, (int)start.Y, (int)(dist - 1 * zoom), segmentWidth),
                    connectionSprite.SourceRect, segmentColor * a, 
                    MathUtils.VectorToAngle(end - start),
                    new Vector2(0, connectionSprite.size.Y / 2), SpriteEffects.None, 0.01f);
            }

            int iconCount = 0, iconIndex = 0;
            if (connectionStart.HasValue && connectionEnd.HasValue)
            {
                if (connection.LevelData.HasBeaconStation) { iconCount++; }
                if (connection.LevelData.HasHuntingGrounds) { iconCount++; }
                if (connection.Locked) { iconCount++; }
                string tooltip = null;
                var subCrushDepth = Submarine.MainSub?.RealWorldCrushDepth ?? Level.DefaultRealWorldCrushDepth;
                if (GameMain.GameSession?.Campaign?.UpgradeManager != null)
                {
                    var hullUpgradePrefab =  UpgradePrefab.Find("increasewallhealth");
                    if (hullUpgradePrefab != null)
                    {
                        int pendingLevel = GameMain.GameSession.Campaign.UpgradeManager.GetUpgradeLevel(hullUpgradePrefab, hullUpgradePrefab.UpgradeCategories.First());
                        int currentLevel = GameMain.GameSession.Campaign.UpgradeManager.GetRealUpgradeLevel(hullUpgradePrefab, hullUpgradePrefab.UpgradeCategories.First());
                        if (pendingLevel > currentLevel)
                        {
                            string updateValueStr = hullUpgradePrefab.SourceElement?.Element("Structure")?.GetAttributeString("crushdepth", null);
                            if (!string.IsNullOrEmpty(updateValueStr))
                            {
                                subCrushDepth = PropertyReference.CalculateUpgrade(subCrushDepth, pendingLevel - currentLevel, updateValueStr);
                            }
                        }
                    }
                }

                string crushDepthWarningIconStyle = null;
                if (connection.LevelData.InitialDepth * Physics.DisplayToRealWorldRatio > subCrushDepth)
                {
                    iconCount++;
                    crushDepthWarningIconStyle = "CrushDepthWarningHighIcon";
                    tooltip = "crushdepthwarninghigh";
                }
                else if ((connection.LevelData.InitialDepth + connection.LevelData.Size.Y) * Physics.DisplayToRealWorldRatio > subCrushDepth)
                {
                    iconCount++;
                    crushDepthWarningIconStyle = "CrushDepthWarningLowIcon";
                    tooltip = "crushdepthwarninglow";
                }

                if (connection.LevelData.HasBeaconStation)
                {
                    var beaconStationIconStyle = connection.LevelData.IsBeaconActive ? "BeaconStationActive" : "BeaconStationInactive";
                    DrawIcon(beaconStationIconStyle, (int)(28 * zoom), TextManager.Get(connection.LevelData.IsBeaconActive ? "BeaconStationActiveTooltip" : "BeaconStationInactiveTooltip"));
                }

                if (connection.Locked)
                {
                    var gateLocation = connection.Locations[0].IsGateBetweenBiomes ? connection.Locations[0] : connection.Locations[1];
                    var unlockEvent =
                        EventSet.PrefabList.Find(ep => ep.UnlockPathEvent && ep.BiomeIdentifier == gateLocation.LevelData.Biome.Identifier) ??
                        EventSet.PrefabList.Find(ep => ep.UnlockPathEvent && string.IsNullOrEmpty(ep.BiomeIdentifier));

                    if (unlockEvent != null)
                    {
                        Reputation unlockReputation = CurrentLocation.Reputation;
                        Faction unlockFaction = null;
                        if (!string.IsNullOrEmpty(unlockEvent.UnlockPathFaction))
                        {
                            unlockFaction = GameMain.GameSession.Campaign.Factions.Find(f => f.Prefab.Identifier.Equals(unlockEvent.UnlockPathFaction, StringComparison.OrdinalIgnoreCase));
                            unlockReputation = unlockFaction?.Reputation;
                        }

                        DrawIcon(
                            "LockedLocationConnection", (int)(28 * zoom),
                            TextManager.GetWithVariables(unlockEvent.UnlockPathTooltip ?? "LockedPathTooltip",
                              new string[] { "[requiredreputation]", "[currentreputation]" },
                              new string[] { Reputation.GetFormattedReputationText(MathUtils.InverseLerp(unlockReputation.MinReputation, unlockReputation.MaxReputation, unlockEvent.UnlockPathReputation), unlockEvent.UnlockPathReputation, addColorTags: true), unlockReputation.GetFormattedReputationText(addColorTags: true) }));
                    }
                    else
                    {
                        DrawIcon("LockedLocationConnection", (int)(28 * zoom), TextManager.Get("LockedPathTooltip"));
                    }

                }

                if (connection.LevelData.HasHuntingGrounds)
                {
                    DrawIcon("HuntingGrounds", (int)(28 * zoom), TextManager.Get("HuntingGroundsTooltip"));
                }

                if (crushDepthWarningIconStyle != null)
                {
                    DrawIcon(crushDepthWarningIconStyle, (int)(32 * zoom), 
                        TextManager.Get(tooltip)
                                .Replace("[initialdepth]", $"‖color:gui.orange‖{(int)(connection.LevelData.InitialDepth * Physics.DisplayToRealWorldRatio)}‖end‖")
                                .Replace("[submarinecrushdepth]", $"‖color:gui.orange‖{(int)subCrushDepth}‖end‖"));
                }
            }

            if (GameMain.DebugDraw && zoom > (1.0f * GUI.Scale) && generationParams.ShowLevelTypeNames)
            {
                Vector2 center = rectCenter + (connection.CenterPos + viewOffset) * zoom;
                if (viewArea.Contains(center) && connection.Biome != null)
                {
                    GUI.DrawString(spriteBatch, center, connection.Biome.Identifier + " (" + connection.Difficulty + ")", Color.White);
                }
            }

            void DrawIcon(string iconStyle, int iconSize, string tooltipText)
            {
                Vector2 iconPos = (connectionStart.Value + connectionEnd.Value) / 2;
                Vector2 iconDiff = Vector2.Normalize(connectionEnd.Value - connectionStart.Value) * iconSize;

                iconPos += (iconDiff * -(iconCount - 1) / 2.0f) + iconDiff * iconIndex;

                var style = GUI.Style.GetComponentStyle(iconStyle);
                bool mouseOn = Vector2.DistanceSquared(iconPos, PlayerInput.MousePosition) < iconSize * iconSize && IsPreferredTooltip(iconPos);
                Sprite iconSprite = style.GetDefaultSprite();
                iconSprite.Draw(spriteBatch, iconPos, (mouseOn ? style.HoverColor : style.Color) * 0.7f,
                    scale: iconSize / iconSprite.size.X);
                if (mouseOn)
                {
                    tooltip = (new Rectangle((iconPos - Vector2.One * iconSize / 2).ToPoint(), new Point(iconSize)), tooltipText);
                }
                iconIndex++;
            }
        }

        private bool IsPreferredTooltip(Vector2 tooltipPos)
        {
            return tooltip == null || Vector2.DistanceSquared(tooltipPos, PlayerInput.MousePosition) < Vector2.DistanceSquared(tooltip.Value.targetArea.Center.ToVector2(), PlayerInput.MousePosition);
        }

        private float hudVisibility;
        private float cameraNoiseStrength;

        private void DrawDecorativeHUD(SpriteBatch spriteBatch, Rectangle rect)
        {
            generationParams.DecorativeGraphSprite.Draw(spriteBatch, (int)((Timing.TotalTime * 5.0f) % generationParams.DecorativeGraphSprite.FrameCount),
                new Vector2(rect.Left, rect.Top), Color.White, Vector2.Zero, 0, Vector2.One * GUI.Scale);

            GUI.DrawString(spriteBatch,
                new Vector2(rect.Right - GUI.IntScale(170), rect.Y + GUI.IntScale(5)),
                "JOVIAN FLUX " + ((cameraNoiseStrength + Rand.Range(-0.02f, 0.02f)) * 500), generationParams.IndicatorColor * hudVisibility, font: GUI.SmallFont);
            GUI.DrawString(spriteBatch,
                new Vector2(rect.X + GUI.IntScale(15), rect.Bottom - GUI.IntScale(25)),
                "LAT " + (-DrawOffset.Y / 100.0f) + "   LON " + (-DrawOffset.X / 100.0f), generationParams.IndicatorColor * hudVisibility, font: GUI.SmallFont);
        }

        private void UpdateMapAnim(MapAnim anim, float deltaTime)
        {
            //pause animation while there are messageboxes (other than hints) on screen
            if (GUIMessageBox.MessageBoxes.Count(c => !(c is GUIMessageBox mb) || mb.MessageBoxType != GUIMessageBox.Type.Hint) > 0) { return; }

            if (!string.IsNullOrEmpty(anim.StartMessage))
            {
                new GUIMessageBox("", anim.StartMessage);
                anim.StartMessage = null;
                return;
            }

            float unscaledZoom = zoom / GUI.Scale;
            if (anim.StartZoom == null) { anim.StartZoom = MathUtils.InverseLerp(generationParams.MinZoom, generationParams.MaxZoom, unscaledZoom); }
            if (anim.EndZoom == null) { anim.EndZoom = MathUtils.InverseLerp(generationParams.MinZoom, generationParams.MaxZoom, unscaledZoom); }

            anim.StartPos = (anim.StartLocation == null) ? -DrawOffset : anim.StartLocation.MapPosition;

            anim.Timer = Math.Min(anim.Timer + deltaTime, anim.Duration);
            float t = anim.Duration <= 0.0f ? 1.0f : Math.Max(anim.Timer / anim.Duration, 0.0f);
            DrawOffset = -Vector2.SmoothStep(anim.StartPos.Value, anim.EndLocation.MapPosition, t);
            DrawOffset += new Vector2(
                (float)PerlinNoise.CalculatePerlin(Timing.TotalTime * 0.3f % 255, Timing.TotalTime * 0.4f % 255, 0) - 0.5f,
                (float)PerlinNoise.CalculatePerlin(Timing.TotalTime * 0.4f % 255, Timing.TotalTime * 0.3f % 255, 0.5f) - 0.5f) * 50.0f * (float)Math.Sin(t * MathHelper.Pi);

            zoom =
                MathHelper.Lerp(generationParams.MinZoom, generationParams.MaxZoom,
                    MathHelper.SmoothStep(anim.StartZoom.Value, anim.EndZoom.Value, t))
                        * GUI.Scale;

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
            noiseOverlay?.Remove();
            noiseOverlay = null;
        }
    }
}
