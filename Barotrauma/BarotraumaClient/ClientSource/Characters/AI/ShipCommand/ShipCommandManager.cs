using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Linq;

namespace Barotrauma
{
    public partial class ShipCommandManager
    {
        public void DebugDraw(SpriteBatch spriteBatch)
        {
            if (CommandedSubmarine == null || CommandedSubmarine.Info.IsOutpost) { return; }
            Vector2 submarinePos = CommandedSubmarine.WorldPosition;
            submarinePos.Y = -submarinePos.Y;
            GUI.DrawString(spriteBatch, submarinePos, NavigationState.ToString(), Color.Yellow, Color.Black, font: GUIStyle.LargeFont);
            if (boardingCooldownTimer > 0)
            {
                GUI.DrawString(spriteBatch, submarinePos + Vector2.UnitY * 35, "Boarding cooldown: " + (int)boardingCooldownTimer, Color.Yellow, Color.Black);
            }
            switch (NavigationState)
            {
                case NavigationStates.Patrol:
                    if (PatrolPositions.Any())
                    {
                        Vector2 patrolPos = PatrolPositions.First();
                        patrolPos.Y = -patrolPos.Y;
                        GUI.DrawLine(spriteBatch, submarinePos, patrolPos, Color.Blue, width: 10);
                    }
                    break;
                case NavigationStates.Ram:
                    if (rammingTarget != null)
                    {
                        Vector2 targetSubPos = rammingTarget.WorldPosition;
                        targetSubPos.Y = -targetSubPos.Y;
                        GUI.DrawLine(spriteBatch, submarinePos, targetSubPos, Color.Red, width: 10);
                    }
                    break;
                case NavigationStates.NavigateToFiringPosition:
                case NavigationStates.NavigateToBoardingPodFiringPosition:
                    if (potentialFiringPositions.Any())
                    {
                        float minPrio = potentialFiringPositions.Min(p => p.Priority);
                        float maxPrio = potentialFiringPositions.Max(p => p.Priority);
                        foreach (FiringPosition firingPos in potentialFiringPositions)
                        {
                            float normalizedPriority = MathUtils.InverseLerp(minPrio, maxPrio, firingPos.Priority);
                            Color iconColor = ToolBox.GradientLerp(normalizedPriority, Color.Red, Color.Yellow, Color.LightGreen);
                            GUIStyle.SubmarineLocationIcon.Value?.Sprite.Draw(spriteBatch, firingPos.Position.FlipY(), iconColor);

                            GUI.DrawString(spriteBatch, firingPos.Position.FlipY(),
                                $"Firing pos {firingPos.Identifier}:\nEnemy turrets: {firingPos.EnemyTurretDanger}\nOwn turrets: {firingPos.MyTurretCoverage}",
                                Color.White);
                        }
                        if (currentFiringPosition != null)
                        {
                            Vector2 targetSubPos = currentFiringPosition.Position;
                            targetSubPos.Y = -targetSubPos.Y;
                            GUI.DrawLine(spriteBatch, submarinePos, targetSubPos, Color.Red, width: 10);
                        }
                    }
                    break;
            }
        }
    }
}