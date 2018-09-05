using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class MiniMap : Powered
    {
        private GUIFrame submarineContainer;

        partial void InitProjSpecific(XElement element)
        {
            new GUICustomComponent(new RectTransform(new Vector2(0.9f, 0.85f), GuiFrame.RectTransform, Anchor.Center),
                DrawHUD, null);
            submarineContainer = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.85f), GuiFrame.RectTransform, Anchor.Center), style: null);
        }

        public override void AddToGUIUpdateList()
        {
            GuiFrame.AddToGUIUpdateList();
        }

        public override void OnMapLoaded()
        {
            base.OnMapLoaded();
            CreateHUD();
        }

        private void CreateHUD()
        {
            submarineContainer.ClearChildren();
            item.Submarine.CreateMiniMap(submarineContainer);
        }

        private void DrawHUD(SpriteBatch spriteBatch, GUICustomComponent container)
        {
            if (item.Submarine == null) return;
            
            if (!hasPower) return;

            foreach (Hull hull in Hull.hullList)
            {
                var hullFrame = submarineContainer.Children.First().FindChild(hull);
                if (hullFrame == null) continue;

                hullDatas.TryGetValue(hull, out HullData hullData);

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

                if (hullFrame.Rect.Contains(PlayerInput.MousePosition))
                {
                    borderColor = Color.Lerp(borderColor, Color.White, 0.5f);
                }
                hullFrame.Color = borderColor;
            }

            /*Rectangle miniMap = new Rectangle(x + 20, y + 40, width - 40, height - 60);
            float size = Math.Min(miniMap.Width / (float)item.Submarine.Borders.Width, miniMap.Height / (float)item.Submarine.Borders.Height);
            foreach (Hull hull in Hull.hullList)
            {
                Point topLeft = new Point(
                    miniMap.X + (int)((hull.Rect.X - item.Submarine.HiddenSubPosition.X - item.Submarine.Borders.X) * size),
                    miniMap.Y - (int)((hull.Rect.Y - item.Submarine.HiddenSubPosition.Y - item.Submarine.Borders.Y) * size));

                Point bottomRight = new Point(
                    topLeft.X + (int)(hull.Rect.Width * size),
                    topLeft.Y + (int)(hull.Rect.Height * size));

                topLeft.X = (int)MathUtils.RoundTowardsClosest(topLeft.X, 4);
                topLeft.Y = (int)MathUtils.RoundTowardsClosest(topLeft.Y, 4);
                bottomRight.X = (int)MathUtils.RoundTowardsClosest(bottomRight.X, 4);
                bottomRight.Y = (int)MathUtils.RoundTowardsClosest(bottomRight.Y, 4);

                Rectangle hullRect = new Rectangle(
                    topLeft, bottomRight - topLeft);

                hullDatas.TryGetValue(hull, out HullData hullData);

                Color borderColor = Color.Green;

                //hull integrity -----------------------------------

                float? gapOpenSum = 0.0f;
                if (ShowHullIntegrity)
                {
                    gapOpenSum = hull.ConnectedGaps.Where(g => !g.IsRoomToRoom).Sum(g => g.Open);
                    borderColor = Color.Lerp(borderColor, Color.Red, Math.Min((float)gapOpenSum, 1.0f));
                }

                //oxygen -----------------------------------

                float? oxygenAmount = null;
                if (RequireOxygenDetectors && (hullData == null || hullData.Oxygen == null))
                {
                    borderColor *= 0.5f;
                }
                else
                {
                    oxygenAmount = hullData != null && hullData.Oxygen != null ? (float)hullData.Oxygen : hull.OxygenPercentage;
                    GUI.DrawRectangle(spriteBatch, hullRect, Color.Lerp(Color.Red * 0.5f, Color.Green * 0.3f, (float)oxygenAmount / 100.0f), true);
                }

                //water -----------------------------------

                float? waterAmount = null;
                if (RequireWaterDetectors && (hullData == null || hullData.Water == null))
                {
                    borderColor *= 0.5f;
                }
                else
                {
                    waterAmount = hullData != null && hullData.Water != null ?
                        (float)hullData.Water :
                        Math.Min(hull.WaterVolume / hull.Volume, 1.0f);

                    if (hullRect.Height * waterAmount > 3.0f)
                    {
                        Rectangle waterRect = new Rectangle(
                            hullRect.X,
                            (int)(hullRect.Y + hullRect.Height * (1.0f - waterAmount)),
                            hullRect.Width,
                            (int)(hullRect.Height * waterAmount));

                        waterRect.Inflate(-3, -3);

                        GUI.DrawRectangle(spriteBatch, waterRect, Color.DarkBlue, true);
                        GUI.DrawLine(spriteBatch, new Vector2(waterRect.X, waterRect.Y), new Vector2(waterRect.Right, waterRect.Y), Color.LightBlue);
                    }
                }

                if (hullRect.Contains(PlayerInput.MousePosition))
                {
                    borderColor = Color.White;

                    if (gapOpenSum > 0.1f)
                    {
                        GUI.DrawString(spriteBatch,
                            new Vector2(x + 10, y + height - 60),
                            TextManager.Get("MiniMapHullBreach"), Color.Red, Color.Black * 0.5f, 2, GUI.SmallFont);
                    }

                    GUI.DrawString(spriteBatch,
                        new Vector2(x + 10, y + height - 60),
                        oxygenAmount == null ? TextManager.Get("MiniMapAirQualityUnavailable") : TextManager.Get("MiniMapAirQuality") + ": " + (int)oxygenAmount + " %",
                        oxygenAmount == null ? Color.Red : Color.Lerp(Color.Red, Color.LightGreen, (float)oxygenAmount / 100.0f),
                        Color.Black * 0.5f, 2, GUI.SmallFont);

                    GUI.DrawString(spriteBatch,
                        new Vector2(x + 10, y + height - 40),
                        waterAmount == null ? TextManager.Get("MiniMapWaterLevelUnavailable") : TextManager.Get("MiniMapWaterLevel") + ": " + (int)(waterAmount * 100.0f) + " %",
                        waterAmount == null ? Color.Red : Color.Lerp(Color.LightGreen, Color.Red, (float)waterAmount),
                        Color.Black * 0.5f, 2, GUI.SmallFont);
                }

                GUI.DrawRectangle(spriteBatch, hullRect, borderColor, false, 0.0f, 2);
            }*/
        }
    }
}
