using Barotrauma.Networking;
using Barotrauma.RuinGeneration;
using Barotrauma.Sounds;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class RoundSound
    {
        public Sound Sound;
        public readonly float Volume;
        public readonly float Range;
        public readonly bool Stream;

        public string Filename
        {
            get { return Sound.Filename; }
        }

        public RoundSound(XElement element, Sound sound)
        {
            Sound = sound;
            Stream = sound.Stream;
            Range = element.GetAttributeFloat("range", 1000.0f);
            Volume = element.GetAttributeFloat("volume", 1.0f);
        }
    }

    partial class Submarine : Entity, IServerSerializable
    {
        public Sprite PreviewImage;

        private static List<RoundSound> roundSounds = null;
        public static RoundSound LoadRoundSound(XElement element, bool stream = false)
        {
            string filename = element.GetAttributeString("file", "");
            if (string.IsNullOrEmpty(filename)) filename = element.GetAttributeString("sound", "");

            if (string.IsNullOrEmpty(filename))
            {
                string errorMsg = "Error when loading round sound (" + element + ") - file path not set";
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("Submarine.LoadRoundSound:FilePathEmpty" + element.ToString(), GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg + "\n" + Environment.StackTrace);
                return null;
            }

            filename = Path.GetFullPath(filename);            
            Sound existingSound = null;
            if (roundSounds == null)
            {
                roundSounds = new List<RoundSound>();
            }
            else
            {
                existingSound = roundSounds.Find(s => s.Filename == filename && s.Stream == stream)?.Sound;
            }

            if (existingSound == null)
            {
                try
                {
                    existingSound = GameMain.SoundManager.LoadSound(filename, stream);
                    if (existingSound == null) { return null; }
                }
                catch (FileNotFoundException e)
                {
                    string errorMsg = "Failed to load sound file \"" + filename + "\".";
                    DebugConsole.ThrowError(errorMsg, e);
                    GameAnalyticsManager.AddErrorEventOnce("Submarine.LoadRoundSound:FileNotFound" + filename, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg + "\n" + Environment.StackTrace);
                    return null;
                }
            }

            RoundSound newSound = new RoundSound(element, existingSound);

            roundSounds.Add(newSound);
            return newSound;
        }

        private static void RemoveRoundSound(RoundSound roundSound)
        {
            roundSound.Sound?.Dispose();
            if (roundSounds == null) return;

            if (roundSounds.Contains(roundSound)) roundSounds.Remove(roundSound);
            foreach (RoundSound otherSound in roundSounds)
            {
                if (otherSound.Sound == roundSound.Sound) otherSound.Sound = null;
            }
        }

        public static void RemoveAllRoundSounds()
        {
            if (roundSounds == null) return;
            for (int i = roundSounds.Count - 1; i >= 0; i--)
            {
                RemoveRoundSound(roundSounds[i]);
            }
        }

        //drawing ----------------------------------------------------
        private static readonly HashSet<Submarine> visibleSubs = new HashSet<Submarine>();
        private static readonly HashSet<Ruin> visibleRuins = new HashSet<Ruin>();
        public static void CullEntities(Camera cam)
        {
            visibleSubs.Clear();
            foreach (Submarine sub in Loaded)
            {
                if (sub.WorldPosition.Y < Level.MaxEntityDepth) continue;

                Rectangle worldBorders = new Rectangle(
                    sub.Borders.X + (int)sub.WorldPosition.X - 500,
                    sub.Borders.Y + (int)sub.WorldPosition.Y + 500,
                    sub.Borders.Width + 1000,
                    sub.Borders.Height + 1000);

                if (RectsOverlap(worldBorders, cam.WorldView))
                {
                    visibleSubs.Add(sub);
                }
            }

            visibleRuins.Clear();
            if (Level.Loaded != null)
            {
                foreach (Ruin ruin in Level.Loaded.Ruins)
                {
                    Rectangle worldBorders = new Rectangle(
                        ruin.Area.X - 500,
                        ruin.Area.Y + ruin.Area.Height + 500,
                        ruin.Area.Width + 1000,
                        ruin.Area.Height + 1000);

                    if (RectsOverlap(worldBorders, cam.WorldView))
                    {
                        visibleRuins.Add(ruin);
                    }
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
                else if (entity.ParentRuin != null)
                {
                    if (!visibleRuins.Contains(entity.ParentRuin)) { continue; }
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

                    if (sub.subBody.PositionBuffer.Count < 2) continue;

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
        public static void DrawDamageable(SpriteBatch spriteBatch, Effect damageEffect, bool editing = false)
        {
            var entitiesToRender = !editing && visibleEntities != null ? visibleEntities : MapEntity.mapEntityList;

            depthSortedDamageable.Clear();

            //insertion sort according to draw depth
            foreach (MapEntity e in entitiesToRender)
            {
                if (e is Structure structure && structure.DrawDamageEffect)
                {
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
            var horizontalLine = GUI.Style.GetComponentStyle("HorizontalLine").Sprites[GUIComponent.ComponentState.None].First();
            var verticalLine = GUI.Style.GetComponentStyle("VerticalLine").Sprites[GUIComponent.ComponentState.None].First();
            
            Vector2 topLeft = roundedGridCenter - Vector2.One * GridSize * gridCells / 2;
            Vector2 bottomRight = roundedGridCenter + Vector2.One * GridSize * gridCells / 2;

            for (int i = 0; i < gridCells; i++)
            {
                float distFromGridX = (MathUtils.RoundTowardsClosest(gridCenter.X, GridSize.X) - gridCenter.X) / GridSize.X;
                float distFromGridY = (MathUtils.RoundTowardsClosest(gridCenter.X, GridSize.Y) - gridCenter.X) / GridSize.Y;

                float normalizedDistX = Math.Abs(i + distFromGridX - gridCells / 2) / (gridCells / 2);
                float normalizedDistY = Math.Abs(i - distFromGridY - gridCells / 2) / (gridCells / 2);

                float expandX = MathHelper.Lerp(30.0f, 0.0f, normalizedDistX);
                float expandY = MathHelper.Lerp(30.0f, 0.0f, normalizedDistY);

                GUI.DrawLine(spriteBatch,
                    horizontalLine.Sprite,
                    new Vector2(topLeft.X - expandX, -bottomRight.Y + i * GridSize.Y),
                    new Vector2(bottomRight.X + expandX, -bottomRight.Y + i * GridSize.Y),
                    Color.White * (1.0f - normalizedDistY) * alpha, depth: 0.6f, width: 3);
                GUI.DrawLine(spriteBatch,
                    verticalLine.Sprite,
                    new Vector2(topLeft.X + i * GridSize.X, -topLeft.Y + expandY),
                    new Vector2(topLeft.X + i * GridSize.X, -bottomRight.Y - expandY),
                    Color.White * (1.0f - normalizedDistX) * alpha, depth: 0.6f, width: 3);
            }
        }

        public static bool SaveCurrent(string filePath, MemoryStream previewImage = null)
        {
            if (MainSub == null)
            {
                MainSub = new Submarine(filePath);
            }

            MainSub.filePath = filePath;
            return MainSub.SaveAs(filePath, previewImage);
        }

        public void CreatePreviewWindow(GUIComponent parent)
        {
            var upperPart = new GUILayoutGroup(new RectTransform(new Vector2(1, 0.5f), parent.RectTransform, Anchor.Center, Pivot.BottomCenter));
            var descriptionBox = new GUIListBox(new RectTransform(new Vector2(1, 0.5f), parent.RectTransform, Anchor.Center, Pivot.TopCenter))
            {
                ScrollBarVisible = true,
                Spacing = 5
            };

            if (PreviewImage == null)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 1), upperPart.RectTransform), TextManager.Get(SavedSubmarines.Contains(this) ? "SubPreviewImageNotFound" : "SubNotDownloaded"));
            }
            else
            {
                var submarinePreviewBackground = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), upperPart.RectTransform)) { Color = Color.Black };
                new GUIImage(new RectTransform(new Vector2(1.0f, 1.0f), submarinePreviewBackground.RectTransform), PreviewImage, scaleToFit: true);
            }

            //space
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.03f), descriptionBox.Content.RectTransform), style: null);

            new GUITextBlock(new RectTransform(new Vector2(1, 0), descriptionBox.Content.RectTransform), TextManager.Get("submarine.name." + Name, true) ?? Name, font: GUI.LargeFont, wrap: true) { ForceUpperCase = true, CanBeFocused = false };

            float leftPanelWidth = 0.6f;
            float rightPanelWidth = 0.4f / leftPanelWidth;

            ScalableFont font = descriptionBox.Rect.Width < 350 ? GUI.SmallFont : GUI.Font;

            Vector2 realWorldDimensions = Dimensions * Physics.DisplayToRealWorldRatio;
            if (realWorldDimensions != Vector2.Zero)
            {
                string dimensionsStr = TextManager.GetWithVariables("DimensionsFormat", new string[2] { "[width]", "[height]" }, new string[2] { ((int)realWorldDimensions.X).ToString(), ((int)realWorldDimensions.Y).ToString() });

                var dimensionsText = new GUITextBlock(new RectTransform(new Vector2(leftPanelWidth, 0), descriptionBox.Content.RectTransform),
                    TextManager.Get("Dimensions"), textAlignment: Alignment.TopLeft, font: font, wrap: true)
                { CanBeFocused = false };
                new GUITextBlock(new RectTransform(new Vector2(rightPanelWidth, 0.0f), dimensionsText.RectTransform, Anchor.TopRight, Pivot.TopLeft),
                    dimensionsStr, textAlignment: Alignment.TopLeft, font: font, wrap: true)
                { CanBeFocused = false };
                dimensionsText.RectTransform.MinSize = new Point(0, dimensionsText.Children.First().Rect.Height);
            }

            if (RecommendedCrewSizeMax > 0)
            {
                var crewSizeText = new GUITextBlock(new RectTransform(new Vector2(leftPanelWidth, 0), descriptionBox.Content.RectTransform),
                    TextManager.Get("RecommendedCrewSize"), textAlignment: Alignment.TopLeft, font: font, wrap: true)
                { CanBeFocused = false };
                new GUITextBlock(new RectTransform(new Vector2(rightPanelWidth, 0.0f), crewSizeText.RectTransform, Anchor.TopRight, Pivot.TopLeft),
                    RecommendedCrewSizeMin + " - " + RecommendedCrewSizeMax, textAlignment: Alignment.TopLeft, font: font, wrap: true)
                { CanBeFocused = false };
                crewSizeText.RectTransform.MinSize = new Point(0, crewSizeText.Children.First().Rect.Height);
            }

            if (!string.IsNullOrEmpty(RecommendedCrewExperience))
            {
                var crewExperienceText = new GUITextBlock(new RectTransform(new Vector2(leftPanelWidth, 0), descriptionBox.Content.RectTransform),
                    TextManager.Get("RecommendedCrewExperience"), textAlignment: Alignment.TopLeft, font: font, wrap: true)
                { CanBeFocused = false };
                new GUITextBlock(new RectTransform(new Vector2(rightPanelWidth, 0.0f), crewExperienceText.RectTransform, Anchor.TopRight, Pivot.TopLeft),
                    TextManager.Get(RecommendedCrewExperience), textAlignment: Alignment.TopLeft, font: font, wrap: true)
                { CanBeFocused = false };
                crewExperienceText.RectTransform.MinSize = new Point(0, crewExperienceText.Children.First().Rect.Height);
            }

            if (RequiredContentPackages.Any())
            {
                var contentPackagesText = new GUITextBlock(new RectTransform(new Vector2(leftPanelWidth, 0), descriptionBox.Content.RectTransform),
                    TextManager.Get("RequiredContentPackages"), textAlignment: Alignment.TopLeft, font: font)
                { CanBeFocused = false };
                new GUITextBlock(new RectTransform(new Vector2(rightPanelWidth, 0.0f), contentPackagesText.RectTransform, Anchor.TopRight, Pivot.TopLeft),
                    string.Join(", ", RequiredContentPackages), textAlignment: Alignment.TopLeft, font: font, wrap: true)
                { CanBeFocused = false };
                contentPackagesText.RectTransform.MinSize = new Point(0, contentPackagesText.Children.First().Rect.Height);
            }
            
            // show what game version the submarine was created on
            if (!IsVanillaSubmarine())
            {
                var versionText = new GUITextBlock(new RectTransform(new Vector2(leftPanelWidth, 0), descriptionBox.Content.RectTransform),
                        TextManager.Get("serverlistversion"), textAlignment: Alignment.TopLeft, font: font, wrap: true)
                    { CanBeFocused = false };
                new GUITextBlock(new RectTransform(new Vector2(rightPanelWidth, 0.0f), versionText.RectTransform, Anchor.TopRight, Pivot.TopLeft),
                        GameVersion.ToString(), textAlignment: Alignment.TopLeft, font: font, wrap: true)
                    { CanBeFocused = false };
                
                versionText.RectTransform.MinSize = new Point(0, versionText.Children.First().Rect.Height);
            }

            GUITextBlock.AutoScaleAndNormalize(descriptionBox.Content.Children.Where(c => c is GUITextBlock).Cast<GUITextBlock>());

            //space
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), descriptionBox.Content.RectTransform), style: null);

            if (!string.IsNullOrEmpty(Description))
            {
                new GUITextBlock(new RectTransform(new Vector2(1, 0), descriptionBox.Content.RectTransform), 
                    TextManager.Get("SaveSubDialogDescription", fallBackTag: "WorkshopItemDescription"), font: GUI.Font, wrap: true) { CanBeFocused = false, ForceUpperCase = true };
            }

            new GUITextBlock(new RectTransform(new Vector2(1, 0), descriptionBox.Content.RectTransform), Description, font: font, wrap: true)
            {
                CanBeFocused = false
            };
        }
        
        public void CreateMiniMap(GUIComponent parent, IEnumerable<Entity> pointsOfInterest = null)
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
                style: null);

            foreach (Hull hull in Hull.hullList)
            {
                if (hull.Submarine != this && !DockedTo.Contains(hull.Submarine)) continue;

                Vector2 relativeHullPos = new Vector2(
                    (hull.WorldRect.X - worldBorders.X) / (float)worldBorders.Width, 
                    (worldBorders.Y - hull.WorldRect.Y) / (float)worldBorders.Height);
                Vector2 relativeHullSize = new Vector2(hull.Rect.Width / (float)worldBorders.Width, hull.Rect.Height / (float)worldBorders.Height);

                var hullFrame = new GUIFrame(new RectTransform(relativeHullSize, hullContainer.RectTransform) { RelativeOffset = relativeHullPos }, style: "MiniMapRoom", color: Color.DarkCyan * 0.8f)
                {
                    UserData = hull
                };
                new GUIFrame(new RectTransform(Vector2.One, hullFrame.RectTransform), style: "ScanLines", color: Color.DarkCyan * 0.8f);
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

        public bool IsVanillaSubmarine()
        {
            var vanilla = GameMain.VanillaContent;
            if (vanilla != null)
            {
                var vanillaSubs = vanilla.GetFilesOfType(ContentType.Submarine);
                string pathToCompare = filePath.Replace(@"\", @"/").ToLowerInvariant();
                if (vanillaSubs.Any(sub => sub.Replace(@"\", @"/").ToLowerInvariant() == pathToCompare))
                {
                    return true;
                }
            }
            return false;
        }

        public void CheckForErrors()
        {
            List<string> errorMsgs = new List<string>();

            if (!Hull.hullList.Any())
            {
                errorMsgs.Add(TextManager.Get("NoHullsWarning"));
            }

            foreach (Item item in Item.ItemList)
            {
                if (item.GetComponent<Items.Components.Vent>() == null) continue;

                if (!item.linkedTo.Any())
                {
                    errorMsgs.Add(TextManager.Get("DisconnectedVentsWarning"));
                    break;
                }
            }

            if (!WayPoint.WayPointList.Any(wp => wp.ShouldBeSaved && wp.SpawnType == SpawnType.Path))
            {
                errorMsgs.Add(TextManager.Get("NoWaypointsWarning"));
            }

            if (WayPoint.WayPointList.Find(wp => wp.SpawnType == SpawnType.Cargo) == null)
            {
                errorMsgs.Add(TextManager.Get("NoCargoSpawnpointWarning"));
            }

            if (!Item.ItemList.Any(it => it.GetComponent<Items.Components.Pump>() != null && it.HasTag("ballast")))
            {
                errorMsgs.Add(TextManager.Get("NoBallastTagsWarning"));
            }

            if (Gap.GapList.Any(g => g.linkedTo.Count == 0))
            {
                errorMsgs.Add(TextManager.Get("NonLinkedGapsWarning"));
            }

            if (errorMsgs.Any())
            {
                new GUIMessageBox(TextManager.Get("Warning"), string.Join("\n\n", errorMsgs), new Vector2(0.25f, 0.0f), new Point(400, 200));
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
                        new string[] { TextManager.Get("Yes"), TextManager.Get("No") });

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
        }
        
        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            var posInfo = PhysicsBody.ClientRead(type, msg, sendingTime, parentDebugName: Name);
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
