using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Submarine : Entity, IServerSerializable
    {
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

        public static bool SaveCurrent(string filePath)
        {
            if (Submarine.MainSub == null)
            {
                Submarine.MainSub = new Submarine(filePath);
                // return;
            }

            Submarine.MainSub.filePath = filePath;

            return Submarine.MainSub.SaveAs(filePath);
        }

        public void CheckForErrors()
        {
            List<string> errorMsgs = new List<string>();

            if (!Hull.hullList.Any())
            {
                errorMsgs.Add("No hulls found in the submarine. Hulls determine the \"borders\" of an individual room and are required for water and air distribution to work correctly.");
            }

            foreach (Item item in Item.ItemList)
            {
                if (item.GetComponent<Items.Components.Vent>() == null) continue;

                if (!item.linkedTo.Any())
                {
                    errorMsgs.Add("The submarine contains vents which haven't been linked to an oxygen generator. Select a vent and click an oxygen generator while holding space to link them.");
                    break;
                }
            }

            if (WayPoint.WayPointList.Find(wp => !wp.MoveWithLevel && wp.SpawnType == SpawnType.Path) == null)
            {
                errorMsgs.Add("No waypoints found in the submarine. AI controlled crew members won't be able to navigate without waypoints.");
            }

            if (WayPoint.WayPointList.Find(wp => wp.SpawnType == SpawnType.Cargo) == null)
            {
                errorMsgs.Add("The submarine doesn't have spawnpoints for cargo (which are used for determining where to place bought items). "
                     + "To fix this, create a new spawnpoint and change its \"spawn type\" parameter to \"cargo\".");
            }

            if (errorMsgs.Any())
            {
                new GUIMessageBox("Warning", string.Join("\n\n", errorMsgs), 400, 0);
            }

            foreach (MapEntity e in MapEntity.mapEntityList)
            {
                if (Vector2.Distance(e.Position, HiddenSubPosition) > 20000)
                {
                    var msgBox = new GUIMessageBox(
                        "Warning",
                        "One or more structures have been placed very far from the submarine. Show the structures?",
                        new string[] { "Yes", "No" });

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
