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

            if (selectedAiTarget?.Entity != null)
            {
                GUI.DrawLine(spriteBatch, pos, new Vector2(selectedAiTarget.WorldPosition.X, -selectedAiTarget.WorldPosition.Y), Color.Red * 0.3f, 0, 5);

                if (wallTarget != null)
                {
                    Vector2 wallTargetPos = wallTarget.Position;
                    if (wallTarget.Structure.Submarine != null) wallTargetPos += wallTarget.Structure.Submarine.Position;
                    wallTargetPos.Y = -wallTargetPos.Y;
                    GUI.DrawRectangle(spriteBatch, wallTargetPos - new Vector2(10.0f, 10.0f), new Vector2(20.0f, 20.0f), Color.Red, false);
                GUI.DrawLine(spriteBatch, pos, wallTargetPos, Color.Orange * 0.5f, 0, 5);
                }

                GUI.Font.DrawString(spriteBatch, $"{selectedAiTarget.Entity.ToString()} ({targetValue.ToString()})", pos - Vector2.UnitY * 20.0f, Color.Red);
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
                case AIState.GoTo:
                    stateColor = Color.Magenta;
                    break;
            }
            GUI.DrawString(spriteBatch, pos - Vector2.UnitY * 80.0f, State.ToString(), stateColor, Color.Black);

            if (latchOntoAI != null)
            {
                foreach (Joint attachJoint in latchOntoAI.AttachJoints)
                {
                    GUI.DrawLine(spriteBatch,
                        ConvertUnits.ToDisplayUnits(new Vector2(attachJoint.WorldAnchorA.X, -attachJoint.WorldAnchorA.Y)),
                        ConvertUnits.ToDisplayUnits(new Vector2(attachJoint.WorldAnchorB.X, -attachJoint.WorldAnchorB.Y)), Color.Orange * 0.6f, 0, 5);
                }

                if (latchOntoAI.WallAttachPos.HasValue)
                {
                    GUI.DrawLine(spriteBatch, pos,
                        ConvertUnits.ToDisplayUnits(new Vector2(latchOntoAI.WallAttachPos.Value.X, -latchOntoAI.WallAttachPos.Value.Y)), Color.Orange * 0.6f, 0, 3);

                }
            }

            IndoorsSteeringManager pathSteering = steeringManager as IndoorsSteeringManager;
            if (pathSteering == null || pathSteering.CurrentPath == null || pathSteering.CurrentPath.CurrentNode == null) return;

            GUI.DrawLine(spriteBatch,
                new Vector2(Character.DrawPosition.X, -Character.DrawPosition.Y),
                new Vector2(pathSteering.CurrentPath.CurrentNode.DrawPosition.X, -pathSteering.CurrentPath.CurrentNode.DrawPosition.Y),
                Color.Orange * 0.6f, 0, 3);
            
            for (int i = 1; i < pathSteering.CurrentPath.Nodes.Count; i++)
            {
                GUI.DrawLine(spriteBatch,
                    new Vector2(pathSteering.CurrentPath.Nodes[i].DrawPosition.X, -pathSteering.CurrentPath.Nodes[i].DrawPosition.Y),
                    new Vector2(pathSteering.CurrentPath.Nodes[i - 1].DrawPosition.X, -pathSteering.CurrentPath.Nodes[i - 1].DrawPosition.Y),
                    Color.Orange * 0.6f, 0, 3);

                GUI.SmallFont.DrawString(spriteBatch,
                    pathSteering.CurrentPath.Nodes[i].ID.ToString(),
                    new Vector2(pathSteering.CurrentPath.Nodes[i].DrawPosition.X, -pathSteering.CurrentPath.Nodes[i].DrawPosition.Y - 10),
                    Color.LightGreen);
            }
        }
    }
}
