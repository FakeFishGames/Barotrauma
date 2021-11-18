#nullable enable
using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class EventManager
    {
        private Graph intensityGraph;
        private Graph targetIntensityGraph;
        private Graph monsterStrengthGraph;
        private const float intensityGraphUpdateInterval = 10;
        private float lastIntensityUpdate;

        private Vector2 pinnedPosition = new Vector2(256, 128);
        private bool isDragging;

        public Event? PinnedEvent { get; set; }

        private bool isGraphSelected;

        public void DebugDraw(SpriteBatch spriteBatch)
        {
            foreach (Event ev in activeEvents)
            {
                Vector2 drawPos = ev.DebugDrawPos;
                drawPos.Y = -drawPos.Y;

                var textOffset = new Vector2(-150, 0);
                spriteBatch.DrawCircle(drawPos, 600, 6, Color.White, thickness: 20);
                GUI.DrawString(spriteBatch, drawPos + textOffset, ev.ToString(), Color.White, Color.Black, 0, GUI.LargeFont);
            }
        }

        public void DebugDrawHUD(SpriteBatch spriteBatch, int y)
        {
            foreach (ScriptedEvent scriptedEvent in activeEvents.Where(ev => !ev.IsFinished && ev is ScriptedEvent).Cast<ScriptedEvent>())
            {
                DrawEventTargetTags(spriteBatch, scriptedEvent);
            }

            float theoreticalMaxMonsterStrength = 10000;
            float relativeMaxMonsterStrength = theoreticalMaxMonsterStrength * GameMain.GameSession.LevelData.Difficulty / 100;
            float absoluteMonsterStrength = monsterStrength / theoreticalMaxMonsterStrength;
            float relativeMonsterStrength = monsterStrength / relativeMaxMonsterStrength;
            GUI.DrawString(spriteBatch, new Vector2(10, y), "EventManager", Color.White, Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 20), "Event cooldown: " + (int)Math.Max(eventCoolDown, 0), Color.White, Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 35), "Current intensity: " + (int)Math.Round(currentIntensity * 100), Color.Lerp(Color.White, GUI.Style.Red, currentIntensity), Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 50), "Target intensity: " + (int)Math.Round(targetIntensity * 100), Color.Lerp(Color.White, GUI.Style.Red, targetIntensity), Color.Black * 0.6f, 0, GUI.SmallFont);

            GUI.DrawString(spriteBatch, new Vector2(15, y + 65), "Crew health: " + (int)Math.Round(avgCrewHealth * 100), Color.Lerp(GUI.Style.Red, GUI.Style.Green, avgCrewHealth), Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 80), "Hull integrity: " + (int)Math.Round(avgHullIntegrity * 100), Color.Lerp(GUI.Style.Red, GUI.Style.Green, avgHullIntegrity), Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 95), "Flooding amount: " + (int)Math.Round(floodingAmount * 100), Color.Lerp(GUI.Style.Green, GUI.Style.Red, floodingAmount), Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 110), "Fire amount: " + (int)Math.Round(fireAmount * 100), Color.Lerp(GUI.Style.Green, GUI.Style.Red, fireAmount), Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 125), "Enemy danger: " + (int)Math.Round(enemyDanger * 100), Color.Lerp(GUI.Style.Green, GUI.Style.Red, enemyDanger), Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 140), "Current monster strength (total): " + (int)Math.Round(monsterStrength), Color.Lerp(GUI.Style.Green, GUI.Style.Red, relativeMonsterStrength), Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 155), "Main events: " + (int)Math.Round(CumulativeMonsterStrengthMain), Color.White, Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 170), "Ruin events: " + (int)Math.Round(CumulativeMonsterStrengthRuins), Color.White, Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 185), "Wreck events: " + (int)Math.Round(CumulativeMonsterStrengthWrecks), Color.White, Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 200), "Cave events: " + (int)Math.Round(CumulativeMonsterStrengthCaves), Color.White, Color.Black * 0.6f, 0, GUI.SmallFont);

#if DEBUG
            if (PlayerInput.KeyDown(Microsoft.Xna.Framework.Input.Keys.LeftAlt) &&
                PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.T))
            {
                eventCoolDown = 1.0f;
            }
#endif

            if (intensityGraph == null)
            {
                int graphDensity = 360; // 60 min
                intensityGraph = new Graph(graphDensity);
                targetIntensityGraph = new Graph(graphDensity);
                monsterStrengthGraph = new Graph(graphDensity);
            }

            if (Timing.TotalTime > lastIntensityUpdate + intensityGraphUpdateInterval)
            {
                intensityGraph.Update(currentIntensity);
                targetIntensityGraph.Update(targetIntensity);
                monsterStrengthGraph.Update(relativeMonsterStrength);
                lastIntensityUpdate = (float)Timing.TotalTime;
            }

            Rectangle graphRect = new Rectangle(15, y + 240, (int)(200 * GUI.xScale), (int)(100 * GUI.yScale));
            bool isGraphHovered = graphRect.Contains(PlayerInput.MousePosition);
            bool leftMousePressed = PlayerInput.PrimaryMouseButtonDown() || PlayerInput.PrimaryMouseButtonHeld();
            bool rightMousePressed = PlayerInput.SecondaryMouseButtonHeld() || PlayerInput.SecondaryMouseButtonDown();
            if (!isGraphSelected && isGraphHovered && leftMousePressed)
            {
                isGraphSelected = true;
            }
            if (isGraphSelected && rightMousePressed)
            {
                isGraphSelected = false;
            }
            Color intensityColor = Color.Lerp(Color.White, GUI.Style.Red, currentIntensity);
            if (isGraphHovered || isGraphSelected)
            {
                graphRect.Size = new Point(GameMain.GraphicsWidth - 30, (int)(GameMain.GraphicsHeight * 0.35f));
                intensityColor = Color.Red;
                GUI.DrawRectangle(spriteBatch, graphRect, Color.Black * 0.95f, isFilled: true);
            }
            else
            {
                GUI.DrawRectangle(spriteBatch, graphRect, Color.Black * 0.6f, isFilled: true);
            }
            intensityGraph.Draw(spriteBatch, graphRect, maxValue: 1.0f, xOffset: 0, intensityColor, (sBatch, value, order, pos) =>
            {
                if (isGraphHovered || isGraphSelected)
                {
                    Vector2 bottomPoint = new Vector2(pos.X, graphRect.Bottom);
                    float height = 3 * GUI.yScale;
                    if (order % 6 == 0)
                    {
                        height *= 3;
                        string text = (order / 6).ToString();
                        var font = GUI.SmallFont;
                        Vector2 textSize = font.MeasureString(text);
                        Vector2 textPos = new Vector2(bottomPoint.X - textSize.X / 2, bottomPoint.Y + height * 1.5f);
                        GUI.DrawString(sBatch, textPos, text, Color.White, font: font);
                    }
                    GUI.DrawLine(sBatch, bottomPoint, bottomPoint + Vector2.UnitY * height, Color.White, width: Math.Max(GUI.Scale, 1));
                    DrawTimeStamps(sBatch, Color.Red, pos, order);
                }
            });
            targetIntensityGraph.Draw(spriteBatch, graphRect, maxValue: 1.0f, xOffset: 0, intensityColor * 0.5f);
            if (isGraphHovered || isGraphSelected)
            {
                float? maxValue = 1;
                Color color = Color.White;
                if (relativeMonsterStrength > 1)
                {
                    maxValue = null;
                    color = Color.Yellow;
                }
                monsterStrengthGraph.Draw(spriteBatch, graphRect, maxValue, color: color, doForEachValue: (sBatch, value, order, pos) => DrawTimeStamps(sBatch, color, pos, order));
            }

            void DrawTimeStamps(SpriteBatch sBatch, Color color, Vector2 pos, int order)
            {
                if (isGraphHovered || isGraphSelected)
                {
                    foreach (var timeStamp in timeStamps)
                    {
                        int t = (int)Math.Abs(Math.Round((timeStamp.Time - lastIntensityUpdate) / intensityGraphUpdateInterval));
                        if (t == order)
                        {
                            float size = 6;
                            Vector2 p = new Vector2(pos.X - size / 2, pos.Y - size / 2);
                            ShapeExtensions.DrawPoint(sBatch, p, color, size);
                            break;
                        }
                    }
                }
            }

            GUI.DrawLine(spriteBatch,
                new Vector2(graphRect.Right, graphRect.Y + graphRect.Height * (1.0f - eventThreshold)),
                new Vector2(graphRect.Right + 5, graphRect.Y + graphRect.Height * (1.0f - eventThreshold)), Color.Orange, width: 3);

            int yStep = (int)(20 * GUI.yScale);
            y = graphRect.Bottom + yStep;
            if (isGraphHovered || isGraphSelected)
            {
                y += yStep;
            }
            int x = graphRect.X;
            if (isCrewAway && crewAwayDuration < settings.FreezeDurationWhenCrewAway)
            {
                GUI.DrawString(spriteBatch, new Vector2(x, y), "Events frozen (crew away from sub): " + ToolBox.SecondsToReadableTime(settings.FreezeDurationWhenCrewAway - crewAwayDuration), Color.LightGreen * 0.8f, null, 0, GUI.SmallFont);
                y += 15;
            }
            else if (crewAwayResetTimer > 0.0f)
            {
                GUI.DrawString(spriteBatch, new Vector2(x, y), "Events frozen (crew just returned to the sub): " + ToolBox.SecondsToReadableTime(crewAwayResetTimer), Color.LightGreen * 0.8f, null, 0, GUI.SmallFont);
                y += 15;
            }
            else if (eventCoolDown > 0.0f)
            {
                GUI.DrawString(spriteBatch, new Vector2(x, y), "Event cooldown active: " + ToolBox.SecondsToReadableTime(eventCoolDown), Color.LightGreen * 0.8f, null, 0, GUI.SmallFont);
                y += 15;
            }
            else if (currentIntensity > eventThreshold)
            {
                GUI.DrawString(spriteBatch, new Vector2(x, y),
                    "Intensity too high for new events: " + (int)(currentIntensity * 100) + "%/" + (int)(eventThreshold * 100) + "%", Color.LightGreen * 0.8f, null, 0, GUI.SmallFont);
                y += 15;
            }

            foreach (EventSet eventSet in pendingEventSets)
            {
                if (Submarine.MainSub == null) { break; }

                GUI.DrawString(spriteBatch, new Vector2(x, y), "New event (ID " + eventSet.DebugIdentifier + ") after: ", Color.Orange * 0.8f, null, 0, GUI.SmallFont);
                y += 12;

                if (eventSet.PerCave)
                {
                    GUI.DrawString(spriteBatch, new Vector2(x, y), "    submarine near cave", Color.Orange * 0.8f, null, 0, GUI.SmallFont);
                    y += 12;
                }
                if (eventSet.PerWreck)
                {
                    GUI.DrawString(spriteBatch, new Vector2(x, y), "    submarine near the wreck", Color.Orange * 0.8f, null, 0, GUI.SmallFont);
                    y += 12;
                }
                if (eventSet.PerRuin)
                {
                    GUI.DrawString(spriteBatch, new Vector2(x, y), "    submarine near the ruins", Color.Orange * 0.8f, null, 0, GUI.SmallFont);
                    y += 12;
                }
                if (roundDuration < eventSet.MinMissionTime)
                {
                    GUI.DrawString(spriteBatch, new Vector2(x, y),
                        "    " + (int) (eventSet.MinDistanceTraveled * 100.0f) + "% travelled (current: " + (int) (distanceTraveled * 100.0f) + " %)",
                        ((Submarine.MainSub == null || distanceTraveled < eventSet.MinDistanceTraveled) ? Color.Lerp(GUI.Style.Yellow, GUI.Style.Red, eventSet.MinDistanceTraveled - distanceTraveled) : GUI.Style.Green) * 0.8f, null, 0, GUI.SmallFont);
                    y += 12;
                }

                if (CurrentIntensity < eventSet.MinIntensity || CurrentIntensity > eventSet.MaxIntensity)
                {
                    GUI.DrawString(spriteBatch, new Vector2(x, y),
                        "    intensity between " + eventSet.MinIntensity.FormatDoubleDecimal() + " and " + eventSet.MaxIntensity.FormatDoubleDecimal(),
                        Color.Orange * 0.8f, null, 0, GUI.SmallFont);
                    y += 12;
                }

                if (roundDuration < eventSet.MinMissionTime)
                {
                    GUI.DrawString(spriteBatch, new Vector2(x, y),
                        "    " + (int) (eventSet.MinMissionTime - roundDuration) + " s",
                        Color.Lerp(GUI.Style.Yellow, GUI.Style.Red, (eventSet.MinMissionTime - roundDuration)), null, 0, GUI.SmallFont);
                }

                y += 15;

                if (y > GameMain.GraphicsHeight * 0.9f)
                {
                    y = graphRect.Bottom + yStep * 2;
                    x += 300;
                }
            }

            GUI.DrawString(spriteBatch, new Vector2(x, y), "Current events: ", Color.White * 0.9f, null, 0, GUI.SmallFont);
            y += yStep;

            foreach (Event ev in activeEvents.Where(ev => !ev.IsFinished || PlayerInput.IsShiftDown()))
            {
                GUI.DrawString(spriteBatch, new Vector2(x + 5, y), ev.ToString(), (!ev.IsFinished ? Color.White : Color.Red) * 0.8f, null, 0, GUI.SmallFont);

                Rectangle rect = new Rectangle(new Point(x + 5, y), GUI.SmallFont.MeasureString(ev.ToString()).ToPoint());

                Rectangle outlineRect = new Rectangle(rect.Location, rect.Size);
                outlineRect.Inflate(4, 4);

                if (PinnedEvent == ev) { GUI.DrawRectangle(spriteBatch, outlineRect, Color.White); }

                if (rect.Contains(PlayerInput.MousePosition))
                {
                    GUI.MouseCursor = CursorState.Hand;
                    GUI.DrawRectangle(spriteBatch, outlineRect, Color.White);
                    if (ev != PinnedEvent)
                    {
                        DrawEvent(spriteBatch, ev, rect);
                    }
                    else if (rightMousePressed)
                    {
                        PinnedEvent = null;
                    }
                    if (leftMousePressed)
                    {
                        PinnedEvent = ev;
                    }
                }

                y += 18;
                if (y > GameMain.GraphicsHeight * 0.9f)
                {
                    y = graphRect.Bottom + yStep * 2;
                    x += 300;
                }
            }
        }

        public void DrawPinnedEvent(SpriteBatch spriteBatch) 
        {
            if (PinnedEvent != null)
            {
                Rectangle rect = DrawEvent(spriteBatch, PinnedEvent, null);

                if (rect != Rectangle.Empty)
                {
                    if (rect.Contains(PlayerInput.MousePosition) && !isDragging)
                    {
                        GUI.MouseCursor = CursorState.Move;
                        if (PlayerInput.PrimaryMouseButtonDown() || PlayerInput.PrimaryMouseButtonHeld())
                        {
                            isDragging = true;
                        }

                        if (PlayerInput.SecondaryMouseButtonClicked() || PlayerInput.SecondaryMouseButtonHeld())
                        {
                            PinnedEvent = null;
                            isDragging = false;
                        }
                    }
                }

                if (isDragging)
                {
                    GUI.MouseCursor = CursorState.Dragging;
                    pinnedPosition = PlayerInput.MousePosition - (new Vector2(rect.Width / 2.0f, -24));
                    if (!PlayerInput.PrimaryMouseButtonHeld())
                    {
                        isDragging = false;
                    }
                }
            }
        }

        private static void DrawEventTargetTags(SpriteBatch spriteBatch, ScriptedEvent scriptedEvent)
        {
            if (Screen.Selected is GameScreen screen)
            {
                Camera cam = screen.Cam;
                Dictionary<Entity, List<string>> tagsDictionary = new Dictionary<Entity, List<string>>();
                foreach ((string key, List<Entity> value) in scriptedEvent.Targets)
                {
                    foreach (Entity entity in value)
                    {
                        if (tagsDictionary.ContainsKey(entity))
                        {
                            tagsDictionary[entity].Add(key);
                        }
                        else
                        {
                            tagsDictionary.Add(entity, new List<string> { key });
                        }
                    }
                }

                string identifier = scriptedEvent.Prefab.Identifier;

                foreach ((Entity entity, List<string> tags) in tagsDictionary)
                {
                    if (entity.Removed) { continue; }

                    string text = tags.Aggregate("Tags:\n", (current, tag) => current + $"    {tag.ColorizeObject()}\n").TrimEnd('\r', '\n');
                    if (!string.IsNullOrWhiteSpace(identifier)) { text = $"Event: {identifier.ColorizeObject()}\n{text}"; }

                    List<RichTextData> richTextData = RichTextData.GetRichTextData(text, out text);

                    Vector2 entityPos = cam.WorldToScreen(entity.WorldPosition);
                    Vector2 infoSize = GUI.SmallFont.MeasureString(text);

                    Vector2 infoPos = entityPos + new Vector2(128 * cam.Zoom, -(128 * cam.Zoom));
                    infoPos.Y -= infoSize.Y / 2;

                    Rectangle infoRect = new Rectangle(infoPos.ToPoint(), infoSize.ToPoint());
                    infoRect.Inflate(4, 4);

                    GUI.DrawRectangle(spriteBatch, infoRect, Color.Black * 0.8f, isFilled: true);
                    GUI.DrawRectangle(spriteBatch, infoRect, Color.White, isFilled: false);

                    GUI.DrawStringWithColors(spriteBatch, infoPos, text, Color.White, richTextData, font: GUI.SmallFont);

                    GUI.DrawLine(spriteBatch, entityPos, new Vector2(infoRect.Location.X, infoRect.Location.Y + infoRect.Height / 2), Color.White);
                }
            }
        }

        private readonly struct DebugLine
        {
            public readonly Vector2 Position;
            public readonly Color Color;

            public DebugLine(Vector2 position, Color color)
            {
                Position = position;
                Color = color;
            }
        }

        private Rectangle DrawEvent(SpriteBatch spriteBatch, Event ev, Rectangle? parentRect = null)
        {
            return ev switch
            {
                ScriptedEvent scriptedEvent => DrawScriptedEvent(spriteBatch, scriptedEvent, parentRect),
                ArtifactEvent artifactEvent => DrawArtifactEvent(spriteBatch, artifactEvent, parentRect),
                MonsterEvent monsterEvent => DrawMonsterEvent(spriteBatch, monsterEvent, parentRect),
                _ => Rectangle.Empty
            };
        }

        private Rectangle DrawScriptedEvent(SpriteBatch spriteBatch, ScriptedEvent scriptedEvent, Rectangle? parentRect = null)
        {
            EventAction? currentEvent = !scriptedEvent.IsFinished ? scriptedEvent.Actions[scriptedEvent.CurrentActionIndex] : null;

            List<DebugLine> positions = new List<DebugLine>();

            string text = $"Finished: {scriptedEvent.IsFinished.ColorizeObject()}\n" +
                          $"Action index: {scriptedEvent.CurrentActionIndex.ColorizeObject()}\n" +
                          $"Current action: {currentEvent?.ToDebugString() ?? ToolBox.ColorizeObject(null)}\n";

            text += "All actions:\n";
            text += FindActions(scriptedEvent).Aggregate(string.Empty, (current, action) => current + $"{new string(' ', action.Item1 * 6)}{action.Item2.ToDebugString()}\n");

            text += "Targets:\n";
            foreach (var (key, value) in scriptedEvent.Targets)
            {
                text += $"    {key.ColorizeObject()}: {value.Aggregate(string.Empty, (current, entity) => current + $"{entity.ColorizeObject()} ")}\n";
            }

            if (scriptedEvent.Targets != null)
            {
                foreach ((_, List<Entity> entities) in scriptedEvent.Targets)
                {
                    if (entities == null || !entities.Any()) { continue; }

                    foreach (var entity in entities)
                    {
                        positions.Add(new DebugLine(entity.WorldPosition, Color.White));
                    }
                }
            }

            return DrawInfoRectangle(spriteBatch, scriptedEvent, text, parentRect, positions);
        }

        private readonly List<DebugLine> debugPositions = new List<DebugLine>();

        private Rectangle DrawArtifactEvent(SpriteBatch spriteBatch, ArtifactEvent artifactEvent, Rectangle? parentRect = null)
        {
            debugPositions.Clear();

            string text = $"Finished: {artifactEvent.IsFinished.ColorizeObject()}\n" +
                          $"Item: {artifactEvent.Item.ColorizeObject()}\n" +
                          $"Spawn pending: {artifactEvent.SpawnPending.ColorizeObject()}\n" +
                          $"Spawn position: {artifactEvent.SpawnPos.ColorizeObject()}\n";

            if (artifactEvent.Item != null && !artifactEvent.Item.Removed)
            {
                Vector2 pos = artifactEvent.Item.WorldPosition;
                debugPositions.Add(new DebugLine(pos, Color.White));
            }

            return DrawInfoRectangle(spriteBatch, artifactEvent, text, parentRect, debugPositions);
        }

        private Rectangle DrawMonsterEvent(SpriteBatch spriteBatch, MonsterEvent monsterEvent, Rectangle? parentRect = null)
        {
            debugPositions.Clear();

            string text = $"Finished: {monsterEvent.IsFinished.ColorizeObject()}\n" +
                          $"Amount: {monsterEvent.MinAmount.ColorizeObject()} - {monsterEvent.MaxAmount.ColorizeObject()}\n" +
                          $"Spawn pending: {monsterEvent.SpawnPending.ColorizeObject()}\n" +
                          $"Spawn position: {monsterEvent.SpawnPos.ColorizeObject()}\n";

            if (monsterEvent.SpawnPos != null && Submarine.MainSub != null)
            {
                Vector2 pos = monsterEvent.SpawnPos.Value;
                text += $"Distance from submarine: {Vector2.Distance(pos, Submarine.MainSub.WorldPosition).ColorizeObject()}\n";
                debugPositions.Add(new DebugLine(pos, Color.White));
            }

            if (monsterEvent.Monsters != null)
            {
                text += !monsterEvent.Monsters.Any() ? $"Monsters: {"None".ColorizeObject()}" : "Monsters:\n";

                foreach (Character monster in monsterEvent.Monsters)
                {
                    text += $"    {monster.ColorizeObject()} -> (Dead: {monster.IsDead.ColorizeObject()}, Health: {monster.HealthPercentage.ColorizeObject()}%, AIState: {(monster.AIController is EnemyAIController enemyAI ? enemyAI.State : AIState.Idle ).ColorizeObject()})\n";
                    if (monster.Removed) { continue; }
                    debugPositions.Add(new DebugLine(monster.WorldPosition, Color.Red));
                }
            }
            return DrawInfoRectangle(spriteBatch, monsterEvent, text, parentRect, debugPositions);
        }

        private Rectangle DrawInfoRectangle(SpriteBatch spriteBatch, Event @event, string text, Rectangle? parentRect = null, List<DebugLine>? drawPoints = null)
        {
            text = text.TrimEnd('\r', '\n');

            string identifier = @event.Prefab.Identifier;
            if (!string.IsNullOrWhiteSpace(identifier))
            {
                text = $"Identifier: {identifier.ColorizeObject()}\n{text}";
            }

            List<RichTextData> richTextData = RichTextData.GetRichTextData(text, out text);

            Vector2 size = GUI.SmallFont.MeasureString(text);
            Vector2 pos = pinnedPosition;
            Rectangle infoRect;
            Rectangle? infoBarRect = null;

            if (parentRect != null)
            {
                Rectangle rect = parentRect.Value;
                pos = new Vector2(350, GameMain.GraphicsHeight / 2.0f - size.Y / 2);
                infoRect = new Rectangle(pos.ToPoint(), size.ToPoint());
                infoRect.Inflate(8, 8);

                GUI.DrawLine(spriteBatch, new Vector2(rect.Right, rect.Y + rect.Height / 2), new Vector2(infoRect.X, infoRect.Y + infoRect.Height / 2), Color.White);
            }
            else
            {
                infoRect = new Rectangle(pos.ToPoint(), size.ToPoint());
                infoRect.Inflate(8, 8);

                Rectangle barRect = new Rectangle(infoRect.Left, infoRect.Top - 32, infoRect.Width, 32);
                const string titleHeader = "Pinned event";

                GUI.DrawRectangle(spriteBatch, barRect, Color.DarkGray * 0.8f, isFilled: true);
                GUI.DrawString(spriteBatch, barRect.Location.ToVector2() + barRect.Size.ToVector2() / 2 - GUI.SubHeadingFont.MeasureString(titleHeader) / 2, titleHeader, Color.White);
                GUI.DrawRectangle(spriteBatch, barRect, Color.White);
                infoBarRect = barRect;
            }

            if (drawPoints != null && drawPoints.Any() && Screen.Selected?.Cam != null)
            {
                foreach (DebugLine line in drawPoints)
                {
                    if (line.Position != Vector2.Zero)
                    {
                        float xPos = infoRect.Right;

                        if (parentRect == null && pinnedPosition.X + infoRect.Width / 2.0f > GameMain.GraphicsWidth / 2.0f)
                        {
                            xPos = infoRect.Left;
                        }

                        GUI.DrawLine(spriteBatch, new Vector2(xPos, infoRect.Top + infoRect.Height / 2), Screen.Selected.Cam.WorldToScreen(line.Position), line.Color);
                    }
                }
            }

            GUI.DrawRectangle(spriteBatch, infoRect, Color.Black * 0.8f, isFilled: true);
            GUI.DrawRectangle(spriteBatch, infoRect, Color.White);

            GUI.DrawStringWithColors(spriteBatch, pos, text, Color.White, richTextData, null, 0, GUI.SmallFont);
            richTextData.Clear();
            return infoBarRect ?? infoRect;
        }

        public void ClientRead(IReadMessage msg)
        {
            NetworkEventType eventType = (NetworkEventType)msg.ReadByte();
            switch (eventType)
            {
                case NetworkEventType.STATUSEFFECT:
                    string eventIdentifier = msg.ReadString();
                    UInt16 actionIndex = msg.ReadUInt16();
                    UInt16 targetCount = msg.ReadUInt16();
                    List<Entity> targets = new List<Entity>();
                    for (int i = 0; i < targetCount; i++)
                    {
                        UInt16 targetID = msg.ReadUInt16();
                        Entity target = Entity.FindEntityByID(targetID);
                        if (target != null) { targets.Add(target); }
                    }

                    var eventPrefab = EventSet.GetEventPrefab(eventIdentifier);
                    if (eventPrefab == null) { return; }
                    int j = 0;
                    foreach (XElement element in eventPrefab.ConfigElement.Descendants())
                    {
                        if (j != actionIndex)
                        {
                            j++;
                            continue;
                        }
                        foreach (XElement subElement in element.Elements())
                        {
                            if (!subElement.Name.ToString().Equals("statuseffect", StringComparison.OrdinalIgnoreCase)) { continue; }
                            StatusEffect effect = StatusEffect.Load(subElement, $"EventManager.ClientRead ({eventIdentifier})");
                            foreach (Entity target in targets)
                            {
                                effect.Apply(effect.type, 1.0f, target, target as ISerializableEntity);
                            }
                        }
                        break;
                    }
                    break;
                case NetworkEventType.CONVERSATION:
                    UInt16 identifier = msg.ReadUInt16();
                    string eventSprite = msg.ReadString();
                    byte dialogType = msg.ReadByte();
                    bool continueConversation = msg.ReadBoolean();
                    UInt16 speakerId = msg.ReadUInt16();
                    string text = msg.ReadString();
                    bool fadeToBlack = msg.ReadBoolean();
                    byte optionCount = msg.ReadByte();
                    List<string> options = new List<string>();
                    for (int i = 0; i < optionCount; i++)
                    {
                        options.Add(msg.ReadString());
                    }

                    byte endCount = msg.ReadByte();
                    int[] endings = new int[endCount];
                    for (int i = 0; i < endCount; i++)
                    {
                        endings[i] = msg.ReadByte();
                    }

                    if (string.IsNullOrEmpty(text) && optionCount == 0)
                    {
                        GUIMessageBox.MessageBoxes.ForEachMod(mb =>
                        {
                            if (mb.UserData is Pair<string, UInt16> pair && pair.First == "ConversationAction" && pair.Second == identifier)
                            {
                                (mb as GUIMessageBox)?.Close();
                            }
                        });
                    }
                    else
                    {
                        ConversationAction.CreateDialog(text, Entity.FindEntityByID(speakerId) as Character, options, endings, eventSprite, identifier, fadeToBlack, (ConversationAction.DialogTypes)dialogType, continueConversation);
                    }
                    if (Entity.FindEntityByID(speakerId) is Character speaker)
                    {
                        speaker.CampaignInteractionType = CampaignMode.InteractionType.None;
                        speaker.SetCustomInteract(null, null);
                    }
                    break;
                case NetworkEventType.MISSION:
                    string missionIdentifier = msg.ReadString();

                    MissionPrefab? prefab = MissionPrefab.List.Find(mp => mp.Identifier.Equals(missionIdentifier, StringComparison.OrdinalIgnoreCase));
                    if (prefab != null)
                    {
                        new GUIMessageBox(string.Empty, TextManager.GetWithVariable("missionunlocked", "[missionname]", prefab.Name), 
                            new string[0], type: GUIMessageBox.Type.InGame, icon: prefab.Icon, relativeSize: new Vector2(0.3f, 0.15f), minSize: new Point(512, 128))
                        {
                            IconColor = prefab.IconColor
                        };
                    }
                    break;
                case NetworkEventType.UNLOCKPATH:
                    UInt16 connectionIndex = msg.ReadUInt16();
                    if (GameMain.GameSession?.Map?.Connections != null)
                    {
                        if (connectionIndex >= GameMain.GameSession.Map.Connections.Count)
                        {
                            DebugConsole.ThrowError($"Failed to unlock a path on the campaign map. Connection index out of bounds (index: {connectionIndex}, number of connections: {GameMain.GameSession.Map.Connections.Count})");
                        }
                        else
                        {
                            GameMain.GameSession.Map.Connections[connectionIndex].Locked = false;
                            new GUIMessageBox(string.Empty, TextManager.Get("pathunlockedgeneric"),
                                new string[0], type: GUIMessageBox.Type.InGame, iconStyle: "UnlockPathIcon", relativeSize: new Vector2(0.3f, 0.15f), minSize: new Point(512, 128));
                        }
                    }
                    break;
            }
        }
    }
}