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
            foreach (ScriptedEvent ev in events)
            {
                Vector2 drawPos = ev.DebugDrawPos;
                drawPos.Y = -drawPos.Y;

                GUI.SubmarineIcon.Draw(spriteBatch, drawPos, Color.White * 0.5f, GUI.SubmarineIcon.size / 2, 0.0f, 40.0f);
                GUI.DrawString(spriteBatch, drawPos, ev.DebugDrawText, Color.White, Color.Black, 0, GUI.LargeFont);
            }
        }

        public void DebugDrawHUD(SpriteBatch spriteBatch, int y)
        {
            GUI.DrawString(spriteBatch, new Vector2(10, y), "EventManager", Color.White, Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 20), "Event cooldown: " + eventCoolDown, Color.White, Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 35), "Current intensity: " + (int)(currentIntensity * 100), Color.Lerp(Color.White, Color.Red, currentIntensity), Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 50), "Target intensity: " + (int)(targetIntensity * 100), Color.Lerp(Color.White, Color.Red, targetIntensity), Color.Black * 0.6f, 0, GUI.SmallFont);

            GUI.DrawString(spriteBatch, new Vector2(15, y + 65), "AvgHealth: " + (int)(avgCrewHealth * 100), Color.Lerp(Color.Red, Color.Green, avgCrewHealth), Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 80), "AvgHullIntegrity: " + (int)(avgHullIntegrity * 100), Color.Lerp(Color.Red, Color.Green, avgHullIntegrity), Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 95), "FloodingAmount: " + (int)(floodingAmount * 100), Color.Lerp(Color.Green, Color.Red, floodingAmount), Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 110), "FireAmount: " + (int)(fireAmount * 100), Color.Lerp(Color.Green, Color.Red, fireAmount), Color.Black * 0.6f, 0, GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(15, y + 125), "EnemyDanger: " + (int)(enemyDanger * 100), Color.Lerp(Color.Green, Color.Red, enemyDanger), Color.Black * 0.6f, 0, GUI.SmallFont);

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
            intensityGraph.Draw(spriteBatch, graphRect, 1.0f, 0.0f, Color.Lerp(Color.White, Color.Red, currentIntensity));
            targetIntensityGraph.Draw(spriteBatch, graphRect, 1.0f, 0.0f, Color.Lerp(Color.White, Color.Red, targetIntensity) * 0.5f);

            GUI.DrawLine(spriteBatch,
                new Vector2(graphRect.Right, graphRect.Y + graphRect.Height * (1.0f - eventThreshold)),
                new Vector2(graphRect.Right + 5, graphRect.Y + graphRect.Height * (1.0f - eventThreshold)), Color.Orange, 0, 1);

            y = graphRect.Bottom + 20;
            foreach (ScriptedEventSet eventSet in selectedEventSets)
            {
                GUI.DrawString(spriteBatch, new Vector2(graphRect.X, y),
                    "- new event after " + (int)(eventSet.MinDistanceTraveled * 100.0f) + "% or " + (int)(eventSet.MinMissionTime / 60.0f) + "min passed",
                    Color.White * 0.8f, null, 0, GUI.SmallFont);
                y += 15;
            }
        }
    }
}
