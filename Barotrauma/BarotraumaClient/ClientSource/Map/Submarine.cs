using Barotrauma.Networking;
using Barotrauma.RuinGeneration;
using Barotrauma.Sounds;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    partial class Submarine : Entity, IServerSerializable
    {
        public static Vector2 MouseToWorldGrid(Camera cam, Submarine sub)
        {
            Vector2 position = PlayerInput.MousePosition;
            position = cam.ScreenToWorld(position);

            Vector2 worldGridPos = VectorToWorldGrid(position);

            if (sub != null)
            {
                worldGridPos.X += sub.Position.X % GridSize.X;
                worldGridPos.Y += sub.Position.Y % GridSize.Y;
            }

            return worldGridPos;
        }

        //drawing ----------------------------------------------------
        private static readonly HashSet<Submarine> visibleSubs = new HashSet<Submarine>();
        public static void CullEntities(Camera cam)
        {
            visibleSubs.Clear();
            foreach (Submarine sub in Loaded)
            {
                if (sub.WorldPosition.Y < Level.MaxEntityDepth) { continue; }

                int margin = 500;
                Rectangle worldBorders = new Rectangle(
                    sub.VisibleBorders.X + (int)sub.WorldPosition.X - margin,
                    sub.VisibleBorders.Y + (int)sub.WorldPosition.Y + margin,
                    sub.VisibleBorders.Width + margin * 2,
                    sub.VisibleBorders.Height + margin * 2);

                if (RectsOverlap(worldBorders, cam.WorldView))
                {
                    visibleSubs.Add(sub);
                }
            }

            if (visibleEntities == null)
            {
                visibleEntities = new List<MapEntity>(MapEntity.mapEntityList.Count);
            }
            else
            {
                visibleEntities.Clear();
            }

            Rectangle worldView = cam.WorldView;
            foreach (MapEntity entity in MapEntity.mapEntityList)
            {
                if (entity.Submarine != null)
                {
                    if (!visibleSubs.Contains(entity.Submarine)) { continue; }
                }

                if (entity.IsVisible(worldView)) { visibleEntities.Add(entity); }
            }
        }

        public static void Draw(SpriteBatch spriteBatch, bool editing = false)
        {
            var entitiesToRender = !editing && visibleEntities != null ? visibleEntities : MapEntity.mapEntityList;

            foreach (MapEntity e in entitiesToRender)
            {
                e.Draw(spriteBatch, editing);
            }
        }

        public static void DrawFront(SpriteBatch spriteBatch, bool editing = false, Predicate<MapEntity> predicate = null)
        {
            var entitiesToRender = !editing && visibleEntities != null ? visibleEntities : MapEntity.mapEntityList;

            foreach (MapEntity e in entitiesToRender)
            {
                if (!e.DrawOverWater) continue;

                if (predicate != null)
                {
                    if (!predicate(e)) continue;
                }

                e.Draw(spriteBatch, editing, false);
            }

            if (GameMain.DebugDraw)
            {
                foreach (Submarine sub in Loaded)
                {
                    Rectangle worldBorders = sub.Borders;
                    worldBorders.Location += sub.WorldPosition.ToPoint();
                    worldBorders.Y = -worldBorders.Y;

                    GUI.DrawRectangle(spriteBatch, worldBorders, Color.White, false, 0, 5);

                    if (sub.SubBody == null || sub.subBody.PositionBuffer.Count < 2) continue;

                    Vector2 prevPos = ConvertUnits.ToDisplayUnits(sub.subBody.PositionBuffer[0].Position);
                    prevPos.Y = -prevPos.Y;

                    for (int i = 1; i < sub.subBody.PositionBuffer.Count; i++)
                    {
                        Vector2 currPos = ConvertUnits.ToDisplayUnits(sub.subBody.PositionBuffer[i].Position);
                        currPos.Y = -currPos.Y;

                        GUI.DrawRectangle(spriteBatch, new Rectangle((int)currPos.X - 10, (int)currPos.Y - 10, 20, 20), Color.Blue * 0.6f, true, 0.01f);
                        GUI.DrawLine(spriteBatch, prevPos, currPos, Color.Cyan * 0.5f, 0, 5);

                        prevPos = currPos;
                    }
                }
            }
        }

        public static float DamageEffectCutoff;
        public static Color DamageEffectColor;

        private static readonly List<Structure> depthSortedDamageable = new List<Structure>();
        public static void DrawDamageable(SpriteBatch spriteBatch, Effect damageEffect, bool editing = false, Predicate<MapEntity> predicate = null)
        {
            var entitiesToRender = !editing && visibleEntities != null ? visibleEntities : MapEntity.mapEntityList;

            depthSortedDamageable.Clear();

            //insertion sort according to draw depth
            foreach (MapEntity e in entitiesToRender)
            {
                if (e is Structure structure && structure.DrawDamageEffect)
                {
                    if (predicate != null)
                    {
                        if (!predicate(e)) continue;
                    }
                    float drawDepth = structure.GetDrawDepth();
                    int i = 0;
                    while (i < depthSortedDamageable.Count)
                    {
                        float otherDrawDepth = depthSortedDamageable[i].GetDrawDepth();
                        if (otherDrawDepth < drawDepth) { break; }
                        i++;
                    }
                    depthSortedDamageable.Insert(i, structure);
                }
            }

            foreach (Structure s in depthSortedDamageable)
            {
                s.DrawDamage(spriteBatch, damageEffect, editing);
            }
            if (damageEffect != null)
            {
                damageEffect.Parameters["aCutoff"].SetValue(0.0f);
                damageEffect.Parameters["cCutoff"].SetValue(0.0f);
                DamageEffectCutoff = 0.0f;
            }
        }

        public static void DrawPaintedColors(SpriteBatch spriteBatch, bool editing = false, Predicate<MapEntity> predicate = null)
        {
            var entitiesToRender = !editing && visibleEntities != null ? visibleEntities : MapEntity.mapEntityList;

            foreach (MapEntity e in entitiesToRender)
            {
                if (e is Hull hull)
                {
                    if (hull.SupportsPaintedColors)
                    {
                        if (predicate != null)
                        {
                            if (!predicate(e)) continue;
                        }

                        hull.DrawSectionColors(spriteBatch);
                    }
                }
            }
        }

        public static void DrawBack(SpriteBatch spriteBatch, bool editing = false, Predicate<MapEntity> predicate = null)
        {
            var entitiesToRender = !editing && visibleEntities != null ? visibleEntities : MapEntity.mapEntityList;

            foreach (MapEntity e in entitiesToRender)
            {
                if (!e.DrawBelowWater) continue;

                if (predicate != null)
                {
                    if (!predicate(e)) continue;
                }

                e.Draw(spriteBatch, editing, true);
            }
        }

        public static void DrawGrid(SpriteBatch spriteBatch, int gridCells, Vector2 gridCenter, Vector2 roundedGridCenter, float alpha = 1.0f)
        {
            Vector2 topLeft = roundedGridCenter - Vector2.One * GridSize * gridCells / 2;
            Vector2 bottomRight = roundedGridCenter + Vector2.One * GridSize * gridCells / 2;

            for (int i = 0; i < gridCells; i++)
            {
                float distFromGridX = (MathUtils.RoundTowardsClosest(gridCenter.X, GridSize.X) - gridCenter.X) / GridSize.X;
                float distFromGridY = (MathUtils.RoundTowardsClosest(gridCenter.Y, GridSize.Y) - gridCenter.Y) / GridSize.Y;

                float normalizedDistX = Math.Abs(i + distFromGridX - gridCells / 2) / (gridCells / 2);
                float normalizedDistY = Math.Abs(i - distFromGridY - gridCells / 2) / (gridCells / 2);

                float expandX = MathHelper.Lerp(30.0f, 0.0f, normalizedDistY);
                float expandY = MathHelper.Lerp(30.0f, 0.0f, normalizedDistX);

                GUI.DrawLine(spriteBatch,
                    new Vector2(topLeft.X - expandX, -bottomRight.Y + i * GridSize.Y),
                    new Vector2(bottomRight.X + expandX, -bottomRight.Y + i * GridSize.Y),
                    Color.White * (1.0f - normalizedDistY) * alpha, depth: 0.6f, width: 3);
                GUI.DrawLine(spriteBatch,
                    new Vector2(topLeft.X + i * GridSize.X, -topLeft.Y + expandY),
                    new Vector2(topLeft.X + i * GridSize.X, -bottomRight.Y - expandY),
                    Color.White * (1.0f - normalizedDistX) * alpha, depth: 0.6f, width: 3);
            }
        }

        // TODO remove
        [Obsolete("Use MiniMap.CreateMiniMap()")]
        public void CreateMiniMap(GUIComponent parent, IEnumerable<Entity> pointsOfInterest = null, bool ignoreOutpost = false)
        {
            Rectangle worldBorders = GetDockedBorders();
            worldBorders.Location += WorldPosition.ToPoint();

            //create a container that has the same "aspect ratio" as the sub
            float aspectRatio = worldBorders.Width / (float)worldBorders.Height;
            float parentAspectRatio = parent.Rect.Width / (float)parent.Rect.Height;

            float scale = 0.9f;

            GUIFrame hullContainer = new GUIFrame(new RectTransform(
                (parentAspectRatio > aspectRatio ? new Vector2(aspectRatio / parentAspectRatio, 1.0f) : new Vector2(1.0f, parentAspectRatio / aspectRatio)) * scale,
                parent.RectTransform, Anchor.Center),
                style: null)
            {
                UserData = "hullcontainer"
            };

            var connectedSubs = GetConnectedSubs();

            HashSet<Hull> hullList = Hull.HullList.Where(hull => hull.Submarine == this || connectedSubs.Contains(hull.Submarine)).Where(hull => !ignoreOutpost || IsEntityFoundOnThisSub(hull, true)).ToHashSet();

            Dictionary<Hull, HashSet<Hull>> combinedHulls = new Dictionary<Hull, HashSet<Hull>>();

            foreach (Hull hull in hullList)
            {
                if (combinedHulls.ContainsKey(hull) || combinedHulls.Values.Any(hh => hh.Contains(hull))) { continue; }

                List<Hull> linkedHulls = new List<Hull>();
                MiniMap.GetLinkedHulls(hull, linkedHulls);

                linkedHulls.Remove(hull);

                foreach (Hull linkedHull in linkedHulls)
                {
                    if (!combinedHulls.ContainsKey(hull))
                    {
                        combinedHulls.Add(hull, new HashSet<Hull>());
                    }

                    combinedHulls[hull].Add(linkedHull);
                }
            }

            foreach (Hull hull in hullList)
            {
                Vector2 relativeHullPos = new Vector2(
                    (hull.WorldRect.X - worldBorders.X) / (float)worldBorders.Width,
                    (worldBorders.Y - hull.WorldRect.Y) / (float)worldBorders.Height);
                Vector2 relativeHullSize = new Vector2(hull.Rect.Width / (float)worldBorders.Width, hull.Rect.Height / (float)worldBorders.Height);

                bool hideHull = combinedHulls.ContainsKey(hull) || combinedHulls.Values.Any(hh => hh.Contains(hull));

                if (hideHull) { continue; }

                Color color = Color.DarkCyan * 0.8f;

                var hullFrame = new GUIFrame(new RectTransform(relativeHullSize, hullContainer.RectTransform) { RelativeOffset = relativeHullPos }, style: "MiniMapRoom", color: color)
                {
                    UserData = hull
                };

                new GUIFrame(new RectTransform(Vector2.One, hullFrame.RectTransform), style: "ScanLines", color: color);
            }

            foreach (var (mainHull, linkedHulls) in combinedHulls)
            {
                MiniMapHullData data = ConstructLinkedHulls(mainHull, linkedHulls, hullContainer, worldBorders);

                Vector2 relativeHullPos = new Vector2(
                    (data.Bounds.X - worldBorders.X) / worldBorders.Width,
                    (worldBorders.Y - data.Bounds.Y) / worldBorders.Height);

                Vector2 relativeHullSize = new Vector2(data.Bounds.Width / worldBorders.Width, data.Bounds.Height / worldBorders.Height);

                Color color = Color.DarkCyan * 0.8f;

                float highestY = 0f,
                      highestX = 0f;

                foreach (var (r, _) in data.RectDatas)
                {
                    float y = r.Y - -r.Height,
                          x = r.X;

                    if (y > highestY) { highestY = y; }
                    if (x > highestX) { highestX = x; }
                }

                HashSet<GUIFrame> frames = new HashSet<GUIFrame>();

                foreach (var (snappredRect, hull) in data.RectDatas)
                {
                    RectangleF rect = snappredRect;
                    rect.Height = -rect.Height;
                    rect.Y -= rect.Height;

                    var (parentW, parentH) = hullContainer.Rect.Size.ToVector2();
                    Vector2 size = new Vector2(rect.Width / parentW, rect.Height / parentH);
                    // TODO this won't be required if we some day switch RectTransform to use RectangleF
                    Vector2 pos = new Vector2(rect.X / parentW, rect.Y / parentH);

                    GUIFrame hullFrame = new GUIFrame(new RectTransform(size, hullContainer.RectTransform) { RelativeOffset = pos }, style: "ScanLinesSeamless", color: color)
                    {
                        UserData = hull,
                        UVOffset = new Vector2(highestX - rect.X, highestY - rect.Y)
                    };

                    frames.Add(hullFrame);
                }

                new GUICustomComponent(new RectTransform(relativeHullSize, hullContainer.RectTransform) { RelativeOffset = relativeHullPos }, (spriteBatch, component) =>
                {
                    foreach (List<Vector2> list in data.Polygon)
                    {
                        spriteBatch.DrawPolygonInner(hullContainer.Rect.Location.ToVector2(), list, component.Color, 2f);
                    }
                }, (deltaTime, component) =>
                {
                    if (component.Parent.Rect.Size != data.ParentSize)
                    {
                        data = ConstructLinkedHulls(mainHull, linkedHulls, hullContainer, worldBorders);
                    }
                })
                {
                    UserData = frames,
                    Color = color,
                    CanBeFocused = false
                };
            }

            if (pointsOfInterest != null)
            {
                foreach (Entity entity in pointsOfInterest)
                {
                    Vector2 relativePos = new Vector2(
                        (entity.WorldPosition.X - worldBorders.X) / worldBorders.Width,
                        (worldBorders.Y - entity.WorldPosition.Y) / worldBorders.Height);
                    new GUIFrame(new RectTransform(new Point(1, 1), hullContainer.RectTransform) { RelativeOffset = relativePos }, style: null)
                    {
                        CanBeFocused = false,
                        UserData = entity
                    };
                }
            }
        }

        public static MiniMapHullData ConstructLinkedHulls(Hull mainHull, HashSet<Hull> linkedHulls, GUIComponent parent, Rectangle worldBorders)
        {
            Rectangle parentRect = parent.Rect;

            Dictionary<Hull, Rectangle> rects = new Dictionary<Hull, Rectangle>();
            Rectangle worldRect = mainHull.WorldRect;
            worldRect.Y = -worldRect.Y;

            rects.Add(mainHull, worldRect);

            foreach (Hull hull in linkedHulls)
            {
                Rectangle rect = hull.WorldRect;
                rect.Y = -rect.Y;

                worldRect = Rectangle.Union(worldRect, rect);
                rects.Add(hull, rect);
            }

            worldRect.Y = -worldRect.Y;

            List<RectangleF> normalizedRects = new List<RectangleF>();
            List<Hull> hullRefs = new List<Hull>();
            foreach (var (hull, rect) in rects)
            {
                Rectangle wRect = rect;
                wRect.Y = -wRect.Y;

                var (posX, posY) = new Vector2(
                    (wRect.X - worldBorders.X) / (float)worldBorders.Width,
                    (worldBorders.Y - wRect.Y) / (float)worldBorders.Height);

                var (scaleX, scaleY) = new Vector2(wRect.Width / (float)worldBorders.Width, wRect.Height / (float)worldBorders.Height);

                RectangleF newRect = new RectangleF(posX * parentRect.Width, posY * parentRect.Height, scaleX * parentRect.Width, scaleY * parentRect.Height);

                normalizedRects.Add(newRect);
                hullRefs.Add(hull);
            }

            ImmutableArray<RectangleF> snappedRectangles = ToolBox.SnapRectangles(normalizedRects, treshold: 1);

            List<List<Vector2>> polygon = ToolBox.CombineRectanglesIntoShape(snappedRectangles);

            List<List<Vector2>> scaledPolygon = new List<List<Vector2>>();

            foreach (List<Vector2> list in polygon)
            {
                var (polySizeX, polySizeY) = ToolBox.GetPolygonBoundingBoxSize(list);
                float sizeX = polySizeX - 1f,
                      sizeY = polySizeY - 1f;

                scaledPolygon.Add(ToolBox.ScalePolygon(list, new Vector2(sizeX / polySizeX, sizeY / polySizeY)));
            }

            return new MiniMapHullData(scaledPolygon, worldRect, parentRect.Size, snappedRectangles, hullRefs.ToImmutableArray());
        }

        public void CheckForErrors()
        {
            List<string> errorMsgs = new List<string>();
            List<SubEditorScreen.WarningType> warnings = new List<SubEditorScreen.WarningType>();

            if (!Hull.HullList.Any())
            {
                if (!IsWarningSuppressed(SubEditorScreen.WarningType.NoWaypoints))
                {
                    errorMsgs.Add(TextManager.Get("NoHullsWarning").Value);
                    warnings.Add(SubEditorScreen.WarningType.NoHulls);
                }
            }

            if (Info.Type != SubmarineType.OutpostModule || 
                (Info.OutpostModuleInfo?.ModuleFlags.Any(f => f != "hallwayvertical" && f != "hallwayhorizontal") ?? true))
            {
                if (!WayPoint.WayPointList.Any(wp => wp.ShouldBeSaved && wp.SpawnType == SpawnType.Path))
                {
                    if (!IsWarningSuppressed(SubEditorScreen.WarningType.NoWaypoints))
                    {
                        errorMsgs.Add(TextManager.Get("NoWaypointsWarning").Value);
                        warnings.Add(SubEditorScreen.WarningType.NoWaypoints);
                    }
                }
            }

            if (Info.Type == SubmarineType.Player)
            {
                foreach (Item item in Item.ItemList)
                {
                    if (item.GetComponent<Items.Components.Vent>() == null) { continue; }
                    if (!item.linkedTo.Any())
                    {
                        if (!IsWarningSuppressed(SubEditorScreen.WarningType.DisconnectedVents))
                        {
                            errorMsgs.Add(TextManager.Get("DisconnectedVentsWarning").Value);
                            warnings.Add(SubEditorScreen.WarningType.DisconnectedVents);
                        }
                        break;
                    }
                }

                if (!WayPoint.WayPointList.Any(wp => wp.ShouldBeSaved && wp.SpawnType == SpawnType.Human))
                {
                    if (!IsWarningSuppressed(SubEditorScreen.WarningType.NoHumanSpawnpoints))
                    {
                        errorMsgs.Add(TextManager.Get("NoHumanSpawnpointWarning").Value);
                        warnings.Add(SubEditorScreen.WarningType.NoHumanSpawnpoints);
                    }
                }
                if (WayPoint.WayPointList.Find(wp => wp.SpawnType == SpawnType.Cargo) == null)
                {
                    if (!IsWarningSuppressed(SubEditorScreen.WarningType.NoCargoSpawnpoints))
                    {
                        errorMsgs.Add(TextManager.Get("NoCargoSpawnpointWarning").Value);
                        warnings.Add(SubEditorScreen.WarningType.NoCargoSpawnpoints);
                    }
                }
                if (!Item.ItemList.Any(it => it.GetComponent<Items.Components.Pump>() != null && it.HasTag("ballast")))
                {
                    if (!IsWarningSuppressed(SubEditorScreen.WarningType.NoBallastTag))
                    {
                        errorMsgs.Add(TextManager.Get("NoBallastTagsWarning").Value);
                        warnings.Add(SubEditorScreen.WarningType.NoBallastTag);
                    }
                }
            }
            else if (Info.Type == SubmarineType.OutpostModule)
            {
                foreach (Item item in Item.ItemList)
                {
                    var junctionBox = item.GetComponent<PowerTransfer>();
                    if (junctionBox == null) { continue; }
                    int doorLinks =
                        item.linkedTo.Count(lt => lt is Gap || (lt is Item it2 && it2.GetComponent<Door>() != null)) +
                        Item.ItemList.Count(it2 => it2.linkedTo.Contains(item) && !item.linkedTo.Contains(it2));
                    for (int i = 0; i < item.Connections.Count; i++)
                    {
                        int wireCount = item.Connections[i].Wires.Count(w => w != null);
                        if (doorLinks + wireCount > item.Connections[i].MaxWires)
                        {
                            errorMsgs.Add(TextManager.GetWithVariables("InsufficientFreeConnectionsWarning",
                                ("[doorcount]", doorLinks.ToString()),
                                ("[freeconnectioncount]", (item.Connections[i].MaxWires - wireCount).ToString())).Value);
                            break;
                        }
                    }
                }
            }

            if (Gap.GapList.Any(g => g.linkedTo.Count == 0))
            {
                if (!IsWarningSuppressed(SubEditorScreen.WarningType.NonLinkedGaps))
                {
                    errorMsgs.Add(TextManager.Get("NonLinkedGapsWarning").Value);
                    warnings.Add(SubEditorScreen.WarningType.NonLinkedGaps);
                }
            }

            int disabledItemLightCount = 0;
            foreach (Item item in Item.ItemList)
            {
                if (item.ParentInventory == null) { continue; }
                disabledItemLightCount += item.GetComponents<Items.Components.LightComponent>().Count();
            }
            int count = GameMain.LightManager.Lights.Count(l => l.CastShadows) - disabledItemLightCount;
            if (count > 45)
            {
                if (!IsWarningSuppressed(SubEditorScreen.WarningType.TooManyLights))
                {
                    errorMsgs.Add(TextManager.Get("subeditor.shadowcastinglightswarning").Value);
                    warnings.Add(SubEditorScreen.WarningType.TooManyLights);
                }
            }

            if (errorMsgs.Any())
            {
                GUIMessageBox msgBox = new GUIMessageBox(TextManager.Get("Warning"), string.Join("\n\n", errorMsgs), new Vector2(0.25f, 0.0f), new Point(400, 200));
                if (warnings.Any())
                {
                    Point size = msgBox.RectTransform.NonScaledSize;
                    GUITickBox suppress = new GUITickBox(new RectTransform(new Vector2(1f, 0.33f), msgBox.Content.RectTransform), TextManager.Get("editor.suppresswarnings"));
                    msgBox.RectTransform.NonScaledSize = new Point(size.X, size.Y + suppress.RectTransform.NonScaledSize.Y);

                    msgBox.Buttons[0].OnClicked += (button, obj) =>
                    {
                        if (suppress.Selected)
                        {
                            foreach (SubEditorScreen.WarningType warning in warnings.Where(warning => !SubEditorScreen.SuppressedWarnings.Contains(warning)))
                            {
                                SubEditorScreen.SuppressedWarnings.Add(warning);
                            }
                        }

                        return true;
                    };
                }
            }

            foreach (MapEntity e in MapEntity.mapEntityList)
            {
                if (Vector2.Distance(e.Position, HiddenSubPosition) > 20000)
                {
                    //move disabled items (wires, items inside containers) inside the sub
                    if (e is Item item && item.body != null && !item.body.Enabled)
                    {
                        item.SetTransform(ConvertUnits.ToSimUnits(HiddenSubPosition), 0.0f);
                    }
                }
            }

            foreach (MapEntity e in MapEntity.mapEntityList)
            {
                if (Vector2.Distance(e.Position, HiddenSubPosition) > 20000)
                {
                    var msgBox = new GUIMessageBox(
                        TextManager.Get("Warning"),
                        TextManager.Get("FarAwayEntitiesWarning"),
                        new LocalizedString[] { TextManager.Get("Yes"), TextManager.Get("No") });

                    msgBox.Buttons[0].OnClicked += (btn, obj) =>
                    {
                        GameMain.SubEditorScreen.Cam.Position = e.WorldPosition;
                        return true;
                    };
                    msgBox.Buttons[0].OnClicked += msgBox.Close;
                    msgBox.Buttons[1].OnClicked += msgBox.Close;

                    break;

                }
            }

            bool IsWarningSuppressed(SubEditorScreen.WarningType type)
            {
                return SubEditorScreen.SuppressedWarnings.Contains(type);
            }
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            if (type != ServerNetObject.ENTITY_POSITION)
            {
                DebugConsole.NewMessage($"Error while reading a network event for the submarine \"{Info.Name} ({ID})\". Invalid event type ({type}).", Color.Red);
            }

            var posInfo = PhysicsBody.ClientRead(type, msg, sendingTime, parentDebugName: Info.Name);
            msg.ReadPadBits();

            if (posInfo != null)
            {
                int index = 0;
                while (index < subBody.PositionBuffer.Count && sendingTime > subBody.PositionBuffer[index].Timestamp)
                {
                    index++;
                }

                subBody.PositionBuffer.Insert(index, posInfo);
            }
        }
    }
}
