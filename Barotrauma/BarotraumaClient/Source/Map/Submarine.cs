using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Barotrauma
{
    partial class Submarine : Entity, IServerSerializable
    {
        public Sprite PreviewImage;

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

                    if (sub.subBody.MemPos.Count < 2) continue;

                    Vector2 prevPos = ConvertUnits.ToDisplayUnits(sub.subBody.MemPos[0].Position);
                    prevPos.Y = -prevPos.Y;

                    for (int i = 1; i < sub.subBody.MemPos.Count; i++)
                    {
                        Vector2 currPos = ConvertUnits.ToDisplayUnits(sub.subBody.MemPos[i].Position);
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

        public static void DrawDamageable(SpriteBatch spriteBatch, Effect damageEffect, bool editing = false)
        {
            var entitiesToRender = !editing && visibleEntities != null ? visibleEntities : MapEntity.mapEntityList;

            foreach (MapEntity e in entitiesToRender)
            {
                if (e.DrawDamageEffect)
                    e.DrawDamage(spriteBatch, damageEffect);
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

        public static bool SaveCurrent(string filePath, MemoryStream previewImage = null)
        {
            if (MainSub == null)
            {
                MainSub = new Submarine(filePath);
            }

            MainSub.filePath = filePath;
            return MainSub.SaveAs(filePath, previewImage);
        }

        public void CreatePreviewWindow(GUIComponent frame)
        {
            new GUITextBlock(new Rectangle(0, 0, 0, 20), Name, "", Alignment.TopCenter, Alignment.TopCenter, frame, true, GUI.LargeFont);

            if (PreviewImage == null)
            {
                var txtBlock = new GUITextBlock(new Rectangle(-20, 60, 256, 128), TextManager.Get("SubPreviewImageNotFound"), Color.Black * 0.5f, null, Alignment.Center, "", frame, true);
                txtBlock.OutlineColor = txtBlock.TextColor;
            }
            else
            {
                new GUIImage(new Rectangle(-10, 60, 256, 128), PreviewImage, Alignment.TopLeft, frame);
            }

            Vector2 realWorldDimensions = Dimensions * Physics.DisplayToRealWorldRatio;
            string dimensionsStr = realWorldDimensions == Vector2.Zero ?
                TextManager.Get("Unknown") :
                TextManager.Get("DimensionsFormat").Replace("[width]", ((int)(realWorldDimensions.X)).ToString()).Replace("[height]", ((int)(realWorldDimensions.Y)).ToString());
            
            new GUITextBlock(new Rectangle(246, 60, 100, 20),
                TextManager.Get("ContentPackage") + ": " + (ContentPackage ?? "Unknown"),
                "", frame, GUI.SmallFont);

            new GUITextBlock(new Rectangle(246, 80, 100, 20),
                TextManager.Get("Dimensions") + ": " + dimensionsStr,
                "", frame, GUI.SmallFont);

            new GUITextBlock(new Rectangle(246, 100, 100, 20),
                TextManager.Get("RecommendedCrewSize") + ": " + (RecommendedCrewSizeMax == 0 ? TextManager.Get("Unknown") : RecommendedCrewSizeMin + " - " + RecommendedCrewSizeMax),
                "", frame, GUI.SmallFont);

            new GUITextBlock(new Rectangle(246, 120, 100, 20),
                TextManager.Get("RecommendedCrewExperience") + ": " + (string.IsNullOrEmpty(RecommendedCrewExperience) ? TextManager.Get("unknown") : RecommendedCrewExperience),
                "", frame, GUI.SmallFont);

            var descr = new GUITextBlock(new Rectangle(0, 200, 0, 100), Description, "", Alignment.TopLeft, Alignment.TopLeft, frame, true, GUI.SmallFont);
            descr.CanBeFocused = false;
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

            if (WayPoint.WayPointList.Find(wp => !wp.MoveWithLevel && wp.SpawnType == SpawnType.Path) == null)
            {
                errorMsgs.Add(TextManager.Get("NoWaypointsWarning"));
            }

            if (WayPoint.WayPointList.Find(wp => wp.SpawnType == SpawnType.Cargo) == null)
            {
                errorMsgs.Add(TextManager.Get("NoCargoSpawnpointWarning"));
            }

            if (errorMsgs.Any())
            {
                new GUIMessageBox(TextManager.Get("Warning"), string.Join("\n\n", errorMsgs), 400, 0);
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

    }
}
