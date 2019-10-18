using FarseerPhysics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class EnemyAIController : AIController
    {
        public override void DebugDraw(SpriteBatch spriteBatch)
        {
            if (Character.IsDead) return;

            Vector2 pos = Character.WorldPosition;
            pos.Y = -pos.Y;

            if (State == AIState.Idle && PreviousState == AIState.Attack)
            {
                var target = _selectedAiTarget ?? _lastAiTarget;
                if (target != null)
                {
                    var memory = GetTargetMemory(target);
                    Vector2 targetPos = memory.Location;
                    targetPos.Y = -targetPos.Y;
                    GUI.DrawLine(spriteBatch, pos, targetPos, Color.White * 0.5f, 0, 4);
                    GUI.DrawString(spriteBatch, pos - Vector2.UnitY * 60.0f, $"{target.Entity.ToString()} ({memory.Priority.FormatZeroDecimal()})", Color.White, Color.Black);
                }
            }
            else if (SelectedAiTarget?.Entity != null)
            {
                Vector2 targetPos = SelectedAiTarget.WorldPosition;
                if (State == AIState.Attack)
                {
                    targetPos = attackWorldPos;
                }
                targetPos.Y = -targetPos.Y;
                GUI.DrawLine(spriteBatch, pos, targetPos, Color.Red * 0.5f, 0, 4);
                if (wallTarget != null)
                {
                    Vector2 wallTargetPos = wallTarget.Position;
                    if (wallTarget.Structure.Submarine != null) { wallTargetPos += wallTarget.Structure.Submarine.Position; }
                    wallTargetPos.Y = -wallTargetPos.Y;
                    GUI.DrawRectangle(spriteBatch, wallTargetPos - new Vector2(10.0f, 10.0f), new Vector2(20.0f, 20.0f), Color.Orange, false);
                    GUI.DrawLine(spriteBatch, pos, wallTargetPos, Color.Orange * 0.5f, 0, 5);
                }
                GUI.DrawString(spriteBatch, pos - Vector2.UnitY * 60.0f, $"{SelectedAiTarget.Entity.ToString()} ({GetTargetMemory(SelectedAiTarget).Priority.FormatZeroDecimal()})", Color.Red, Color.Black);
                GUI.DrawString(spriteBatch, pos - Vector2.UnitY * 40.0f, $"({targetValue.FormatZeroDecimal()})", Color.Red, Color.Black);
            }

            /*GUI.Font.DrawString(spriteBatch, targetValue.ToString(), pos - Vector2.UnitY * 80.0f, Color.Red);
            GUI.Font.DrawString(spriteBatch, "updatetargets: " + MathUtils.Round(updateTargetsTimer, 0.1f), pos - Vector2.UnitY * 100.0f, Color.Red);
            GUI.Font.DrawString(spriteBatch, "cooldown: " + MathUtils.Round(coolDownTimer, 0.1f), pos - Vector2.UnitY * 120.0f, Color.Red);*/

            Color stateColor = Color.White;
            switch (State)
            {
                case AIState.Attack:
                    stateColor = IsCoolDownRunning ? Color.Orange : Color.Red;
                    break;
                case AIState.Escape:
                    stateColor = Color.LightBlue;
                    break;
                case AIState.Flee:
                    stateColor = Color.White;
                    break;
                case AIState.Eat:
                    stateColor = Color.Brown;
                    break;
            }
            GUI.DrawString(spriteBatch, pos - Vector2.UnitY * 80.0f, State.ToString(), stateColor, Color.Black);

            if (LatchOntoAI != null)
            {
                foreach (Joint attachJoint in LatchOntoAI.AttachJoints)
                {
                    GUI.DrawLine(spriteBatch,
                        ConvertUnits.ToDisplayUnits(new Vector2(attachJoint.WorldAnchorA.X, -attachJoint.WorldAnchorA.Y)),
                        ConvertUnits.ToDisplayUnits(new Vector2(attachJoint.WorldAnchorB.X, -attachJoint.WorldAnchorB.Y)), Color.Green, 0, 4);
                }

                if (LatchOntoAI.WallAttachPos.HasValue)
                {
                    //GUI.DrawLine(spriteBatch, pos,
                    //    ConvertUnits.ToDisplayUnits(new Vector2(LatchOntoAI.WallAttachPos.Value.X, -LatchOntoAI.WallAttachPos.Value.Y)), Color.Green, 0, 3);
                }
            }

            if (steeringManager is IndoorsSteeringManager pathSteering)
            {
                var path = pathSteering.CurrentPath;
                if (path != null)
                {
                    if (path.CurrentNode != null)
                    {
                        GUI.DrawLine(spriteBatch, pos,
                            new Vector2(path.CurrentNode.DrawPosition.X, -path.CurrentNode.DrawPosition.Y),
                            Color.DarkViolet, 0, 3);

                        GUI.DrawString(spriteBatch, pos - new Vector2(0, 100), "Path cost: " + path.Cost.FormatZeroDecimal(), Color.White, Color.Black * 0.5f);
                    }
                    for (int i = 1; i < path.Nodes.Count; i++)
                    {
                        var previousNode = path.Nodes[i - 1];
                        var currentNode = path.Nodes[i];
                        GUI.DrawLine(spriteBatch,
                            new Vector2(currentNode.DrawPosition.X, -currentNode.DrawPosition.Y),
                            new Vector2(previousNode.DrawPosition.X, -previousNode.DrawPosition.Y),
                            Color.Red * 0.5f, 0, 3);

                        GUI.SmallFont.DrawString(spriteBatch,
                            currentNode.ID.ToString(),
                            new Vector2(currentNode.DrawPosition.X - 10, -currentNode.DrawPosition.Y - 30),
                            Color.Red);
                    }
                }
            }
            else
            {
                if (steeringManager.AvoidDir.LengthSquared() > 0.0001f)
                {
                    Vector2 hitPos = ConvertUnits.ToDisplayUnits(steeringManager.AvoidRayCastHit);
                    hitPos.Y = -hitPos.Y;

                    GUI.DrawLine(spriteBatch, hitPos, hitPos + new Vector2(steeringManager.AvoidDir.X, -steeringManager.AvoidDir.Y) * 100, Color.Red, width: 5);
                    //GUI.DrawLine(spriteBatch, pos, ConvertUnits.ToDisplayUnits(steeringManager.AvoidLookAheadPos.X, -steeringManager.AvoidLookAheadPos.Y), Color.Orange, width: 4);
                }
            }
            GUI.DrawLine(spriteBatch, pos, pos + ConvertUnits.ToDisplayUnits(new Vector2(Character.AnimController.TargetMovement.X, -Character.AnimController.TargetMovement.Y)), Color.SteelBlue, width: 2);
            GUI.DrawLine(spriteBatch, pos, pos + ConvertUnits.ToDisplayUnits(new Vector2(Steering.X, -Steering.Y)), Color.Blue, width: 3);
        }
    }
}
