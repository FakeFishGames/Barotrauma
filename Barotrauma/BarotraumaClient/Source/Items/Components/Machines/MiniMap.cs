using Barotrauma.Extensions;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class MiniMap : Powered
    {
        private GUIFrame submarineContainer;

        private GUIFrame hullInfoFrame;

        private GUITextBlock hullNameText, hullBreachText, hullAirQualityText, hullWaterText;

        private List<Submarine> displayedSubs = new List<Submarine>();

        partial void InitProjSpecific(XElement element)
        {
            new GUICustomComponent(new RectTransform(new Vector2(0.9f, 0.85f), GuiFrame.RectTransform, Anchor.Center),
                DrawHUDBack, null);
            submarineContainer = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.85f), GuiFrame.RectTransform, Anchor.Center), style: null);

            hullInfoFrame = new GUIFrame(new RectTransform(new Vector2(0.13f, 0.13f), GUI.Canvas, minSize: new Point(250, 150)),
                style: "InnerFrame")
            {
                CanBeFocused = false
            };
            var hullInfoContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), hullInfoFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            hullNameText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), hullInfoContainer.RectTransform), "");
            hullBreachText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), hullInfoContainer.RectTransform), "");
            hullAirQualityText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), hullInfoContainer.RectTransform), "");
            hullWaterText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), hullInfoContainer.RectTransform), "");

            hullInfoFrame.Children.ForEach(c => { c.CanBeFocused = false; c.Children.ForEach(c2 => c2.CanBeFocused = false); });
        }

        public override void AddToGUIUpdateList()
        {
            base.AddToGUIUpdateList();
            if (hasPower) hullInfoFrame.AddToGUIUpdateList();
        }

        public override void OnMapLoaded()
        {
            base.OnMapLoaded();
            CreateHUD();
        }

        private void CreateHUD()
        {
            submarineContainer.ClearChildren();

            if (item.Submarine == null) return;

            item.Submarine.CreateMiniMap(submarineContainer);
            displayedSubs.Clear();
            displayedSubs.Add(item.Submarine);
            displayedSubs.AddRange(item.Submarine.DockedTo);
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            //recreate HUD if the subs we should display have changed
            if ((item.Submarine == null && displayedSubs.Count > 0) ||              //item not inside a sub anymore, but display is still showing subs
                !displayedSubs.Contains(item.Submarine) ||                          //current sub not displayer
                item.Submarine.DockedTo.Any(s => !displayedSubs.Contains(s)) ||     //some of the docked subs not diplayed
                displayedSubs.Any(s => s != item.Submarine && !item.Submarine.DockedTo.Contains(s))) //displaying a sub that shouldn't be displayed
            {
                CreateHUD();
            }

            float distort = 1.0f - item.Condition / 100.0f;
            foreach (HullData hullData in hullDatas.Values)
            {
                hullData.DistortionTimer -= deltaTime;
                if (hullData.DistortionTimer <= 0.0f)
                {
                    hullData.Distort = Rand.Range(0.0f, 1.0f) < distort * distort;
                    if (hullData.Distort)
                    {
                        hullData.Oxygen = Rand.Range(0.0f, 100.0f);
                        hullData.Water = Rand.Range(0.0f, 1.0f);
                    }
                    hullData.DistortionTimer = Rand.Range(1.0f, 10.0f);
                }
            }
        }

        private void DrawHUDBack(SpriteBatch spriteBatch, GUICustomComponent container)
        {
            hullInfoFrame.Visible = false;
            int? groupId = null;
            if (item.Submarine == null || !hasPower)
            {
                InitializeHullDatas();
                foreach (Hull hull in Hull.hullList)
                {
                    var hullFrame = submarineContainer.Children.First().FindChild(hull);
                    if (hullFrame == null) continue;

                    if (GUI.MouseOn == hullFrame || hullFrame.IsParentOf(GUI.MouseOn))
                    {
                        hullDatas.TryGetValue(hull, out HullData hullData);
                        groupId = hullData == null ? null : hullData.GroupId;
                    }

                    hullFrame.Color = Color.DarkCyan * 0.3f;
                    hullFrame.Children.First().Color = Color.DarkCyan * 0.3f;
                }
            }

            float scale = 1.0f;
            float? totalGap = null,
                totalOxygen = null,
                totalWater = null;
            HashSet<Submarine> subs = new HashSet<Submarine>();
            foreach (Hull hull in Hull.hullList)
            {
                if (hull.Submarine == null) continue;
                var hullFrame = submarineContainer.Children.First().FindChild(hull);
                if (hullFrame == null) continue;

                hullDatas.TryGetValue(hull, out HullData hullData);
                if (hullData == null)
                {
                    hullData = new HullData();
                    hullDatas.Add(hull, hullData);
                }

                if (hullData.Distort)
                {
                    hullFrame.Children.First().Color = Color.Lerp(Color.Black, Color.DarkGray * 0.5f, Rand.Range(0.0f, 1.0f));
                    hullFrame.Color = Color.DarkGray * 0.5f;
                    continue;
                }
                
                subs.Add(hull.Submarine);
                scale = Math.Min(
                    hullFrame.Parent.Rect.Width / (float)hull.Submarine.Borders.Width, 
                    hullFrame.Parent.Rect.Height / (float)hull.Submarine.Borders.Height);
                
                Color borderColor = Color.DarkCyan;
                
                float? gapOpenSum = 0.0f;
                if (ShowHullIntegrity)
                {
                    gapOpenSum = hull.ConnectedGaps.Where(g => !g.IsRoomToRoom).Sum(g => g.Open);
                    borderColor = Color.Lerp(Color.DarkCyan, Color.Red, Math.Min((float)gapOpenSum, 1.0f));
                }

                float? oxygenAmount = null;
                if (!RequireOxygenDetectors || hullData?.Oxygen != null)
                {
                    oxygenAmount = RequireOxygenDetectors ? hullData.Oxygen : hull.OxygenPercentage;
                    GUI.DrawRectangle(spriteBatch, hullFrame.Rect, Color.Lerp(Color.Red * 0.5f, Color.Green * 0.3f, (float)oxygenAmount / 100.0f), true);
                }

                float? waterAmount = null;
                if (!RequireWaterDetectors || hullData.Water != null)
                {
                    waterAmount = RequireWaterDetectors ? hullData.Water : Math.Min(hull.WaterVolume / hull.Volume, 1.0f);
                    if (hullFrame.Rect.Height * waterAmount > 3.0f)
                    {
                        Rectangle waterRect = new Rectangle(
                            hullFrame.Rect.X, (int)(hullFrame.Rect.Y + hullFrame.Rect.Height * (1.0f - waterAmount)),
                            hullFrame.Rect.Width, (int)(hullFrame.Rect.Height * waterAmount));

                        waterRect.Inflate(-3, -3);

                        GUI.DrawRectangle(spriteBatch, waterRect, new Color(85, 136, 147), true);
                        GUI.DrawLine(spriteBatch, new Vector2(waterRect.X, waterRect.Y), new Vector2(waterRect.Right, waterRect.Y), Color.LightBlue);
                    }
                }

                if (GUI.MouseOn == hullFrame || hullFrame.IsParentOf(GUI.MouseOn))
                {
                    hullInfoFrame.RectTransform.ScreenSpaceOffset = hullFrame.Rect.Center;
                    hullInfoFrame.Visible = true;
                    hullNameText.Text = hull.RoomName;
                }

                if (GUI.MouseOn == hullFrame || hullFrame.IsParentOf(GUI.MouseOn) ||
                    (groupId != null && hullData.GroupId == groupId))
                {                    
                    borderColor = Color.Lerp(borderColor, Color.White, 0.5f);
                    hullFrame.Children.First().Color = Color.White;
                    hullFrame.Color = borderColor;

                    if (groupId != null && hullData.GroupId == groupId)
                    {
                        totalGap += hull.ConnectedGaps.Where(g => !g.IsRoomToRoom).Sum(g => g.Open);
                        totalOxygen += RequireOxygenDetectors ? hullData.Oxygen : hull.OxygenPercentage;
                        totalWater += RequireWaterDetectors ? hullData.Water : Math.Min(hull.WaterVolume / hull.Volume, 1.0f);
                    }
                    else
                    {
                        hullBreachText.Text = gapOpenSum > 0.1f ? TextManager.Get("MiniMapHullBreach") : "";
                        hullBreachText.TextColor = Color.Red;

                        hullAirQualityText.Text = oxygenAmount == null ? TextManager.Get("MiniMapAirQualityUnavailable") : TextManager.Get("MiniMapAirQuality") + ": " + (int)oxygenAmount + " %";
                        hullAirQualityText.TextColor = oxygenAmount == null ? Color.Red : Color.Lerp(Color.Red, Color.LightGreen, (float)oxygenAmount / 100.0f);

                        hullWaterText.Text = waterAmount == null ? TextManager.Get("MiniMapWaterLevelUnavailable") : TextManager.Get("MiniMapWaterLevel") + ": " + (int)(waterAmount * 100.0f) + " %";
                        hullWaterText.TextColor = waterAmount == null ? Color.Red : Color.Lerp(Color.LightGreen, Color.Red, (float)waterAmount);
                    }
                }
                else
                {
                    hullFrame.Children.First().Color = Color.DarkCyan * 0.8f;
                }
                
                hullFrame.Color = borderColor;
            }
            //connected hulls
            if (groupId != null)
            {
                hullBreachText.Text = totalGap > 0.1f ? TextManager.Get("MiniMapHullBreach") : "";
                hullBreachText.TextColor = Color.Red;

                hullAirQualityText.Text = totalOxygen == null ? TextManager.Get("MiniMapAirQualityUnavailable") : TextManager.Get("MiniMapAirQuality") + ": " + (int)totalOxygen + " %";
                hullAirQualityText.TextColor = totalOxygen == null ? Color.Red : Color.Lerp(Color.Red, Color.LightGreen, (float)totalOxygen / 100.0f);

                hullWaterText.Text = totalWater == null ? TextManager.Get("MiniMapWaterLevelUnavailable") : TextManager.Get("MiniMapWaterLevel") + ": " + (int)(totalWater * 100.0f) + " %";
                hullWaterText.TextColor = totalWater == null ? Color.Red : Color.Lerp(Color.LightGreen, Color.Red, (float)totalWater);
            }

            foreach (Submarine sub in subs)
            {
                if (sub.HullVertices == null) { continue; }

                Rectangle worldBorders = sub.GetDockedBorders();
                worldBorders.Location += sub.WorldPosition.ToPoint();
                
                scale = Math.Min(
                    submarineContainer.Rect.Width / (float)worldBorders.Width,
                    submarineContainer.Rect.Height / (float)worldBorders.Height) * 0.9f;

                float displayScale = ConvertUnits.ToDisplayUnits(scale);
                Vector2 offset = ConvertUnits.ToSimUnits(sub.WorldPosition - new Vector2(worldBorders.Center.X, worldBorders.Y - worldBorders.Height / 2));
                Vector2 center = container.Rect.Center.ToVector2();
                
                for (int i = 0; i < sub.HullVertices.Count; i++)
                {
                    Vector2 start = (sub.HullVertices[i] + offset) * displayScale;
                    start.Y = -start.Y;
                    Vector2 end = (sub.HullVertices[(i + 1) % sub.HullVertices.Count] + offset) * displayScale;
                    end.Y = -end.Y;
                    GUI.DrawLine(spriteBatch, center + start, center + end, Color.DarkCyan * Rand.Range(0.3f, 0.35f), width: 10);
                }
            }
        }

        /// <summary>
        /// Assigns same group id to connected hulls
        /// </summary>
        private void InitializeHullDatas()
        {
            foreach (Hull hull in Hull.hullList)
            {
                if (hull.linkedTo.Count == 0)
                    continue;

                hullDatas.TryGetValue(hull, out HullData hullData1);
                if (hullData1 != null)
                    hullData1.GroupId = hullData1.GroupId ?? GetNewGroupId();

                foreach (Hull subHull in hull.linkedTo)
                {
                    if (subHull == hull)
                        continue;

                    hullDatas.TryGetValue(subHull, out HullData hullData2);
                    if (hullData2.GroupId == null)
                        hullData2.GroupId = hullData1.GroupId;
                    else if (hullData2.GroupId != hullData1.GroupId)
                        ChangeGroupId((int)hullData2.GroupId, (int)hullData1.GroupId);
                }
            }
        }

        /// <summary>
        /// Returns new unique id and records it to HullDataGroupIds
        /// </summary>
        /// <returns></returns>
        private int? GetNewGroupId()
        {
            int rand;
            while (true)
            {
                int max = HullDataGroupIds.Count != 0 ? HullDataGroupIds.Count : 1;
                rand = Rand.Int(max * 2);
                if (!HullDataGroupIds.Contains(rand))
                    break;
            }
            HullDataGroupIds.Add(rand);
            return rand;
        }

        private void ChangeGroupId(int oldId, int newId)
        {
            foreach (HullData data in hullDatas.Values)
            {
                data.GroupId = data.GroupId == oldId ? newId : data.GroupId;
            }
            HullDataGroupIds.Remove(oldId);
        }
    }
}
