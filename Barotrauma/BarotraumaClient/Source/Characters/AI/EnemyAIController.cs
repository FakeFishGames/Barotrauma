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

            if (SelectedAiTarget?.Entity != null)
            {
                GUI.DrawLine(spriteBatch, pos, new Vector2(SelectedAiTarget.WorldPosition.X, -SelectedAiTarget.WorldPosition.Y), Color.Red * 0.5f, 0, 4);

                if (wallTarget != null)
                {
                    Vector2 wallTargetPos = wallTarget.Position;
                    if (wallTarget.Structure.Submarine != null) { wallTargetPos += wallTarget.Structure.Submarine.Position; }
                    wallTargetPos.Y = -wallTargetPos.Y;
                    GUI.DrawRectangle(spriteBatch, wallTargetPos - new Vector2(10.0f, 10.0f), new Vector2(20.0f, 20.0f), Color.Orange, false);
                    GUI.DrawLine(spriteBatch, pos, wallTargetPos, Color.Orange * 0.5f, 0, 5);
                }
                GUI.DrawString(spriteBatch, pos - Vector2.UnitY * 60.0f, $"{SelectedAiTarget.Entity.ToString()} ({targetValue.FormatZeroDecimal()})", Color.Red, Color.Black);
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
                    GUI.DrawLine(spriteBatch, pos,
                        ConvertUnits.ToDisplayUnits(new Vector2(LatchOntoAI.WallAttachPos.Value.X, -LatchOntoAI.WallAttachPos.Value.Y)), Color.Green, 0, 3);
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
            GUI.DrawLine(spriteBatch, pos, pos + ConvertUnits.ToDisplayUnits(new Vector2(Character.AnimController.TargetMovement.X, -Character.AnimController.TargetMovement.Y)), Color.SteelBlue, width: 2);
            GUI.DrawLine(spriteBatch, pos, pos + ConvertUnits.ToDisplayUnits(new Vector2(Steering.X, -Steering.Y)), Color.Blue, width: 3);
        }
    }
}
