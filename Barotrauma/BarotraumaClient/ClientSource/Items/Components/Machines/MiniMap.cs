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

        private string noPowerTip = "";

        private readonly List<Submarine> displayedSubs = new List<Submarine>();

        private Point prevResolution;

        partial void InitProjSpecific(XElement element)
        {
            noPowerTip = TextManager.Get("SteeringNoPowerTip");
            CreateGUI();
        }

        protected override void CreateGUI()
        {
            GuiFrame.RectTransform.RelativeOffset = new Vector2(0.05f, 0.0f);
            GuiFrame.CanBeFocused = true;
            new GUICustomComponent(new RectTransform(GuiFrame.Rect.Size - GUIStyle.ItemFrameMargin, GuiFrame.RectTransform, Anchor.Center) { AbsoluteOffset = GUIStyle.ItemFrameOffset },
                DrawHUDBack, null);
            submarineContainer = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), GuiFrame.RectTransform, Anchor.Center), style: null);

            new GUICustomComponent(new RectTransform(GuiFrame.Rect.Size - GUIStyle.ItemFrameMargin, GuiFrame.RectTransform, Anchor.Center) { AbsoluteOffset = GUIStyle.ItemFrameOffset },
                DrawHUDFront, null)
            {
                CanBeFocused = false
            };

            hullInfoFrame = new GUIFrame(new RectTransform(new Vector2(0.13f, 0.13f), GUI.Canvas, minSize: new Point(250, 150)),
                style: "GUIToolTip")
            {
                CanBeFocused = false
            };
            var hullInfoContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), hullInfoFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            hullNameText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), hullInfoContainer.RectTransform), "") { Wrap = true };
            hullBreachText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), hullInfoContainer.RectTransform), "") { Wrap = true };
            hullAirQualityText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), hullInfoContainer.RectTransform), "") { Wrap = true };
            hullWaterText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), hullInfoContainer.RectTransform), "") { Wrap = true };

            hullInfoFrame.Children.ForEach(c =>
            {
                c.CanBeFocused = false;
                c.Children.ForEach(c2 => c2.CanBeFocused = false);
            });
        }

        public override void AddToGUIUpdateList()
        {
            base.AddToGUIUpdateList();
            hullInfoFrame.AddToGUIUpdateList(order: 1);
        }

        private void CreateHUD()
        {
            prevResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            submarineContainer?.ClearChildren();

            if (item.Submarine == null) { return; }

            item.Submarine.CreateMiniMap(submarineContainer);
            displayedSubs.Clear();
            displayedSubs.Add(item.Submarine);
            displayedSubs.AddRange(item.Submarine.DockedTo);
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            //recreate HUD if the subs we should display have changed
            if ((item.Submarine == null && displayedSubs.Count > 0) ||                                       //item not inside a sub anymore, but display is still showing subs
                !displayedSubs.Contains(item.Submarine) ||                                                   //current sub not displayer
                prevResolution.X != GameMain.GraphicsWidth || prevResolution.Y != GameMain.GraphicsHeight || //resolution changed
                item.Submarine.DockedTo.Any(s => !displayedSubs.Contains(s)) ||                              //some of the docked subs not diplayed
                !submarineContainer.Children.Any() ||                                                        // We lack a GUI
                displayedSubs.Any(s => s != item.Submarine && !item.Submarine.DockedTo.Contains(s)))         //displaying a sub that shouldn't be displayed
            {
                CreateHUD();
            }
            
            float distort = 1.0f - item.Condition / item.MaxCondition;
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

        private void DrawHUDFront(SpriteBatch spriteBatch, GUICustomComponent container)
        {
            if (Voltage < MinVoltage)
            {
                Vector2 textSize = GUI.Font.MeasureString(noPowerTip);
                Vector2 textPos = GuiFrame.Rect.Center.ToVector2();

                GUI.DrawString(spriteBatch, textPos - textSize / 2, noPowerTip,
                               GUI.Style.Orange * (float)Math.Abs(Math.Sin(Timing.TotalTime)), Color.Black * 0.8f, font: GUI.SubHeadingFont);
                return;
            }
            if (!submarineContainer.Children.Any()) { return; }
            foreach (GUIComponent child in submarineContainer.Children.FirstOrDefault()?.Children)
            {
                if (child.UserData is Hull hull)
                {
                    if (hull.Submarine == null || !hull.Submarine.Info.IsOutpost) { continue; }
                    string text = TextManager.GetWithVariable("MiniMapOutpostDockingInfo", "[outpost]", hull.Submarine.Info.Name);
                    Vector2 textSize = GUI.Font.MeasureString(text);
                    Vector2 textPos = child.Center;
                    if (textPos.X + textSize.X / 2 > submarineContainer.Rect.Right)
                        textPos.X -= ((textPos.X + textSize.X / 2) - submarineContainer.Rect.Right) + 10 * GUI.xScale;
                    if (textPos.X - textSize.X / 2 < submarineContainer.Rect.X)
                        textPos.X += (submarineContainer.Rect.X - (textPos.X - textSize.X / 2)) + 10 * GUI.xScale;
                    GUI.DrawString(spriteBatch, textPos - textSize / 2, text,
                       GUI.Style.Orange * (float)Math.Abs(Math.Sin(Timing.TotalTime)), Color.Black * 0.8f);
                    break;
                }
            }            
        }

        private void DrawHUDBack(SpriteBatch spriteBatch, GUICustomComponent container)
        {
            Hull mouseOnHull = null;
            hullInfoFrame.Visible = false;

            foreach (Hull hull in Hull.hullList)
            {
                var hullFrame = submarineContainer.Children.FirstOrDefault()?.FindChild(hull);
                if (hullFrame == null) { continue; }

                if (GUI.MouseOn == hullFrame || hullFrame.IsParentOf(GUI.MouseOn))
                {
                    mouseOnHull = hull;
                }
                if (item.Submarine == null || !hasPower)
                {
                    hullFrame.Color = Color.DarkCyan * 0.3f;
                    hullFrame.Children.First().Color = Color.DarkCyan * 0.3f;
                }
            }

            if (Voltage < MinVoltage)
            {
                return;
            }

            float scale = 1.0f;
            HashSet<Submarine> subs = new HashSet<Submarine>();
            foreach (Hull hull in Hull.hullList)
            {
                if (hull.Submarine == null) { continue; }
                var hullFrame = submarineContainer.Children.FirstOrDefault()?.FindChild(hull);
                if (hullFrame == null) { continue; }

                hullFrame.Visible = true;
                if (!submarineContainer.Rect.Contains(hullFrame.Rect))
                {
                    if (hull.Submarine.Info.Type != SubmarineType.Player) 
                    {
                        hullFrame.Visible = false;
                        continue; 
                    }
                }

                hullDatas.TryGetValue(hull, out HullData hullData);
                if (hullData == null)
                {
                    hullData = new HullData();
                    GetLinkedHulls(hull, hullData.LinkedHulls);
                    hullDatas.Add(hull, hullData);
                }
                
                Color neutralColor = Color.DarkCyan;
                if (hull.RoomName != null)
                {
                    if (hull.RoomName.Contains("ballast") || hull.RoomName.Contains("Ballast") ||
                        hull.RoomName.Contains("airlock") || hull.RoomName.Contains("Airlock"))
                    {
                        neutralColor = new Color(9, 80, 159);
                    }
                }

                if (hullData.Distort)
                {
                    hullFrame.Children.First().Color = Color.Lerp(Color.Black, Color.DarkGray * 0.5f, Rand.Range(0.0f, 1.0f));
                    hullFrame.Color = neutralColor * 0.5f;
                    continue;
                }
                
                subs.Add(hull.Submarine);
                scale = Math.Min(
                    hullFrame.Parent.Rect.Width / (float)hull.Submarine.Borders.Width, 
                    hullFrame.Parent.Rect.Height / (float)hull.Submarine.Borders.Height);
                
                Color borderColor = neutralColor;
                
                float? gapOpenSum = 0.0f;
                if (ShowHullIntegrity)
                {
                    gapOpenSum = hull.ConnectedGaps.Where(g => !g.IsRoomToRoom).Sum(g => g.Open);
                    borderColor = Color.Lerp(neutralColor, GUI.Style.Red, Math.Min((float)gapOpenSum, 1.0f));
                }

                float? oxygenAmount = null;
                if (!RequireOxygenDetectors || hullData?.Oxygen != null)
                {
                    oxygenAmount = RequireOxygenDetectors ? hullData.Oxygen : hull.OxygenPercentage;
                    GUI.DrawRectangle(
                        spriteBatch, hullFrame.Rect, 
                        Color.Lerp(GUI.Style.Red * 0.5f, GUI.Style.Green * 0.3f, (float)oxygenAmount / 100.0f), 
                        true);
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

                if (mouseOnHull == hull ||
                    hullData.LinkedHulls.Contains(mouseOnHull))
                {
                    borderColor = Color.Lerp(borderColor, Color.White, 0.5f);
                    hullFrame.Children.First().Color = Color.White;
                    hullFrame.Color = borderColor;
                }
                else
                {
                    hullFrame.Children.First().Color = neutralColor * 0.8f;
                }

                if (mouseOnHull == hull)
                {
                    hullInfoFrame.RectTransform.ScreenSpaceOffset = hullFrame.Rect.Center;
                    if (hullInfoFrame.Rect.Right > GameMain.GraphicsWidth) { hullInfoFrame.RectTransform.ScreenSpaceOffset -= new Point(hullInfoFrame.Rect.Width, 0); }
                    if (hullInfoFrame.Rect.Bottom > GameMain.GraphicsHeight) { hullInfoFrame.RectTransform.ScreenSpaceOffset -= new Point(0, hullInfoFrame.Rect.Height); }

                    hullInfoFrame.Visible = true;
                    hullNameText.Text = hull.DisplayName;

                    foreach (Hull linkedHull in hullData.LinkedHulls)
                    {
                        gapOpenSum += linkedHull.ConnectedGaps.Where(g => !g.IsRoomToRoom).Sum(g => g.Open);
                        oxygenAmount += linkedHull.OxygenPercentage;
                        waterAmount += Math.Min(linkedHull.WaterVolume / linkedHull.Volume, 1.0f);
                    }
                    oxygenAmount /= (hullData.LinkedHulls.Count + 1);
                    waterAmount /= (hullData.LinkedHulls.Count + 1);

                    hullBreachText.Text = gapOpenSum > 0.1f ? TextManager.Get("MiniMapHullBreach") : "";
                    hullBreachText.TextColor = GUI.Style.Red;

                    hullAirQualityText.Text = oxygenAmount == null ? TextManager.Get("MiniMapAirQualityUnavailable") :
                        TextManager.AddPunctuation(':', TextManager.Get("MiniMapAirQuality"), + (int)oxygenAmount + " %");
                    hullAirQualityText.TextColor = oxygenAmount == null ? GUI.Style.Red : Color.Lerp(GUI.Style.Red, Color.LightGreen, (float)oxygenAmount / 100.0f);

                    hullWaterText.Text = waterAmount == null ? TextManager.Get("MiniMapWaterLevelUnavailable") : 
                        TextManager.AddPunctuation(':', TextManager.Get("MiniMapWaterLevel"), (int)(waterAmount * 100.0f) + " %");
                    hullWaterText.TextColor = waterAmount == null ? GUI.Style.Red : Color.Lerp(Color.LightGreen, GUI.Style.Red, (float)waterAmount);
                }
                
                hullFrame.Color = borderColor;
            }
            
            foreach (Submarine sub in subs)
            {
                if (sub.HullVertices == null || sub.Info.IsOutpost) { continue; }
                
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
                    GUI.DrawLine(spriteBatch, center + start, center + end, Color.DarkCyan * Rand.Range(0.3f, 0.35f), width: (int)(10 * GUI.Scale));
                }
            }
        }

        private void GetLinkedHulls(Hull hull, List<Hull> linkedHulls)
        {
            foreach (var linkedEntity in hull.linkedTo)
            {
                if (linkedEntity is Hull linkedHull)
                {
                    if (linkedHulls.Contains(linkedHull)) { continue; }
                    linkedHulls.Add(linkedHull);
                    GetLinkedHulls(linkedHull, linkedHulls);
                }
            }
        }
    }
}
