using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class EventManager
    {
        private Graph intensityGraph;
        private Graph targetIntensityGraph;
        private float intensityGraphUpdateInterval;
        private float lastIntensityUpdate;

        public void DebugDraw(SpriteBatch spriteBatch)
        {
            foreach (ScriptedEvent ev in activeEvents)
            {
                Vector2 drawPos = ev.DebugDrawPos;
                drawPos.Y = -drawPos.Y;

                var textOffset = new Vector2(-150, 0);
                ShapeExtensions.DrawCircle(spriteBatch, drawPos, 600, 6, Color.White, thickness: 20);
                GUI.DrawString(spriteBatch, drawPos + textOffset, ev.ToString(), Color.White, Color.Black, 0, GUI.LargeFont);
            }
        }

        public void DebugDrawHUD(SpriteBatch spriteBatch, int y)
        {
            GUI.DrawString(spriteBatch, new Vector2(10, y), "EventManager", Color.White, Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 20), "Event cooldown: " + eventCoolDown, Color.White, Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 35), "Current intensity: " + (int)(currentIntensity * 100), Color.Lerp(Color.White, GUI.Style.Red, currentIntensity), Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 50), "Target intensity: " + (int)(targetIntensity * 100), Color.Lerp(Color.White, GUI.Style.Red, targetIntensity), Color.Black * 0.6f, 0, GUI.SmallFont);

            GUI.DrawString(spriteBatch, new Vector2(15, y + 65), "AvgHealth: " + (int)(avgCrewHealth * 100), Color.Lerp(GUI.Style.Red, GUI.Style.Green, avgCrewHealth), Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 80), "AvgHullIntegrity: " + (int)(avgHullIntegrity * 100), Color.Lerp(GUI.Style.Red, GUI.Style.Green, avgHullIntegrity), Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 95), "FloodingAmount: " + (int)(floodingAmount * 100), Color.Lerp(GUI.Style.Green, GUI.Style.Red, floodingAmount), Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 110), "FireAmount: " + (int)(fireAmount * 100), Color.Lerp(GUI.Style.Green, GUI.Style.Red, fireAmount), Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 125), "EnemyDanger: " + (int)(enemyDanger * 100), Color.Lerp(GUI.Style.Green, GUI.Style.Red, enemyDanger), Color.Black * 0.6f, 0, GUI.SmallFont);

#if DEBUG
            if (PlayerInput.KeyDown(Microsoft.Xna.Framework.Input.Keys.LeftAlt) && 
                PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.T))
            {
                eventCoolDown = 1.0f;
            }
#endif

            if (intensityGraph == null)
            {
                intensityGraph = new Graph();
                targetIntensityGraph = new Graph();
            }

            intensityGraphUpdateInterval = 5.0f;
            if (Timing.TotalTime > lastIntensityUpdate + intensityGraphUpdateInterval)
            {
                intensityGraph.Update(currentIntensity);
                targetIntensityGraph.Update(targetIntensity);
                lastIntensityUpdate = (float)Timing.TotalTime;
            }

            Rectangle graphRect = new Rectangle(15, y + 150, 150, 50);

            GUI.DrawRectangle(spriteBatch, graphRect, Color.Black * 0.5f, true);
            intensityGraph.Draw(spriteBatch, graphRect, 1.0f, 0.0f, Color.Lerp(Color.White, GUI.Style.Red, currentIntensity));
            targetIntensityGraph.Draw(spriteBatch, graphRect, 1.0f, 0.0f, Color.Lerp(Color.White, GUI.Style.Red, targetIntensity) * 0.5f);

            GUI.DrawLine(spriteBatch,
                new Vector2(graphRect.Right, graphRect.Y + graphRect.Height * (1.0f - eventThreshold)),
                new Vector2(graphRect.Right + 5, graphRect.Y + graphRect.Height * (1.0f - eventThreshold)), Color.Orange, 0, 1);

            y = graphRect.Bottom + 20;
            if (eventCoolDown > 0.0f)
            {
                GUI.DrawString(spriteBatch, new Vector2(graphRect.X, y), "Event cooldown active: " + (int)eventCoolDown, Color.LightGreen * 0.8f, null, 0, GUI.SmallFont);
                y += 15;
            }
            else if (currentIntensity > eventThreshold)
            {
                GUI.DrawString(spriteBatch, new Vector2(graphRect.X, y),
                    "Intensity too high for new events: " + (int)(currentIntensity * 100) + "%/" + (int)(eventThreshold * 100) + "%", Color.LightGreen * 0.8f, null, 0, GUI.SmallFont);
                y += 15;
            }
            foreach (ScriptedEventSet eventSet in pendingEventSets)
            {
                float distanceTraveled = MathHelper.Clamp(
                    (Submarine.MainSub.WorldPosition.X - level.StartPosition.X) / (level.EndPosition.X - level.StartPosition.X),
                    0.0f, 1.0f);

                GUI.DrawString(spriteBatch, new Vector2(graphRect.X, y), "New event (ID " + eventSet.DebugIdentifier + ") after: ", Color.Orange * 0.8f, null, 0, GUI.SmallFont);
                y += 12;

                if ((Submarine.MainSub == null || distanceTraveled < eventSet.MinDistanceTraveled) &&
                    roundDuration < eventSet.MinMissionTime)
                {
                    GUI.DrawString(spriteBatch, new Vector2(graphRect.X, y),
                        "    " + (int)(eventSet.MinDistanceTraveled * 100.0f) + "% travelled (current: " + (int)(distanceTraveled * 100.0f) + " %)",
                        Color.Orange * 0.8f, null, 0, GUI.SmallFont);
                    y += 12;
                }
                if (CurrentIntensity < eventSet.MinIntensity || CurrentIntensity > eventSet.MaxIntensity)
                {
                    GUI.DrawString(spriteBatch, new Vector2(graphRect.X, y),
                        "    intensity between " + ((int)eventSet.MinIntensity) + " and " + ((int)eventSet.MaxIntensity),
                        Color.Orange * 0.8f, null, 0, GUI.SmallFont);
                    y += 12;
                }
                if (roundDuration < eventSet.MinMissionTime)
                {
                    GUI.DrawString(spriteBatch, new Vector2(graphRect.X, y),
                        "    " + (int)(eventSet.MinMissionTime - roundDuration) + " s",
                        Color.Orange * 0.8f, null, 0, GUI.SmallFont);
                }

                y += 15;
            }


            GUI.DrawString(spriteBatch, new Vector2(graphRect.X, y), "Current events: ", Color.White * 0.9f, null, 0, GUI.SmallFont);
            y += 12;
            foreach (ScriptedEvent scriptedEvent in activeEvents)
            {
                if (scriptedEvent.IsFinished) { continue; }
                GUI.DrawString(spriteBatch, new Vector2(graphRect.X + 5, y), scriptedEvent.ToString(), Color.White * 0.8f, null, 0, GUI.SmallFont);
                y += 12;
            }
        }
    }
}
