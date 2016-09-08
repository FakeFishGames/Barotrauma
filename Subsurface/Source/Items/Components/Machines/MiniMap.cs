using System;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Items.Components
{
    class MiniMap : Powered
    {
        class HullData
        {
            public float? Oxygen;
            public float? Water;
        }
        
        private DateTime resetDataTime;

        bool hasPower;

        [Editable, HasDefaultValue(false, true)]
        public bool RequireWaterDetectors
        {
            get;
            set;
        }

        [Editable, HasDefaultValue(true, true)]
        public bool RequireOxygenDetectors
        {
            get;
            set;
        }

        [Editable, HasDefaultValue(false, true)]
        public bool ShowHullIntegrity
        {
            get;
            set;
        }


        private Dictionary<Hull, HullData> hullDatas;

        public MiniMap(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;

            hullDatas = new Dictionary<Hull, HullData>();
        }
        
        public override void Update(float deltaTime, Camera cam) 
        {
            //periodically reset all hull data
            //(so that outdated hull info won't be shown if detectors stop sending signals)
            if (DateTime.Now > resetDataTime)
            {
                hullDatas.Clear();
                resetDataTime = DateTime.Now + new TimeSpan(0, 0, 1);
            }

            currPowerConsumption = powerConsumption;

            hasPower = voltage > minVoltage;

            voltage = 0.0f;
        }
        
        public override bool Pick(Character picker)
        {
            if (picker == null) return false;

            //picker.SelectedConstruction = item;

            return true;
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (item.Submarine == null) return;

            int width = GuiFrame.Rect.Width, height = GuiFrame.Rect.Height;
            int x = GuiFrame.Rect.X;
            int y = GuiFrame.Rect.Y;

            GuiFrame.Draw(spriteBatch);

            if (!hasPower) return;

            Rectangle miniMap = new Rectangle(x + 20, y + 40, width - 40, height - 60);

            float size = Math.Min((float)miniMap.Width / (float)item.Submarine.Borders.Width, (float)miniMap.Height / (float)item.Submarine.Borders.Height);
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

                HullData hullData;
                hullDatas.TryGetValue(hull, out hullData);
                
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
                        Math.Min(hull.Volume / hull.FullVolume, 1.0f);

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
                            "Hull breach", Color.Red, Color.Black * 0.5f, 2, GUI.SmallFont);  
                    }

                    GUI.DrawString(spriteBatch, 
                        new Vector2(x+10, y+height-40), 
                        oxygenAmount == null ? "Air quality data not available" : "Air quality: "+(int)oxygenAmount+" %",
                        oxygenAmount == null ? Color.Red : Color.Lerp(Color.Red, Color.LightGreen, (float)oxygenAmount / 100.0f),
                        Color.Black * 0.5f, 2, GUI.SmallFont);

                    GUI.DrawString(spriteBatch,
                        new Vector2(x + 10, y + height - 20),
                        waterAmount == null ? "Water level data not available" : "Water level: " + (int)(waterAmount*100.0f) + " %",
                        waterAmount == null ? Color.Red : Color.Lerp(Color.LightGreen, Color.Red, (float)waterAmount),
                        Color.Black * 0.5f, 2, GUI.SmallFont);                    
                }
                                
                GUI.DrawRectangle(spriteBatch, hullRect, borderColor, 2);
            }
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item sender, float power = 0)
        {
            base.ReceiveSignal(stepsTaken, signal, connection, sender, power);

            if (sender == null || sender.CurrentHull == null) return;

            Hull senderHull = sender.CurrentHull;

            HullData hullData;
            if (!hullDatas.TryGetValue(senderHull, out hullData))
            {
                hullData = new HullData();
                hullDatas.Add(senderHull, hullData);
            }

            switch (connection.Name)
            {
                case "water_data_in":
                    //cheating a bit because water detectors don't actually send the water level
                    if (sender.GetComponent<WaterDetector>() == null)
                    {
                        hullData.Water = Rand.Range(0.0f, 1.0f);
                    }
                    else
                    {
                        hullData.Water = Math.Min(senderHull.Volume / senderHull.FullVolume, 1.0f);
                    }
                    break;
                case "oxygen_data_in":
                    float oxy;

                    if (!float.TryParse(signal, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out oxy))
                    {
                        oxy = Rand.Range(0.0f, 100.0f);
                    }

                    hullData.Oxygen = oxy;
                    break;
            }
        }

    }
}
