using FarseerPhysics;
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
                GUI.DrawLine(spriteBatch, pos, new Vector2(selectedAiTarget.WorldPosition.X, -selectedAiTarget.WorldPosition.Y), Color.Red);

                if (wallAttackPos != Vector2.Zero)
                {
                    GUI.DrawRectangle(spriteBatch, ConvertUnits.ToDisplayUnits(new Vector2(wallAttackPos.X, -wallAttackPos.Y)) - new Vector2(10.0f, 10.0f), new Vector2(20.0f, 20.0f), Color.Red, false);
                }

                GUI.Font.DrawString(spriteBatch, targetValue.ToString(), pos - Vector2.UnitY * 20.0f, Color.Red);
            }

            GUI.Font.DrawString(spriteBatch, targetValue.ToString(), pos - Vector2.UnitY * 80.0f, Color.Red);

            GUI.Font.DrawString(spriteBatch, "updatetargets: " + updateTargetsTimer, pos - Vector2.UnitY * 100.0f, Color.Red);
            GUI.Font.DrawString(spriteBatch, "cooldown: " + coolDownTimer, pos - Vector2.UnitY * 120.0f, Color.Red);


            IndoorsSteeringManager pathSteering = steeringManager as IndoorsSteeringManager;
            if (pathSteering == null || pathSteering.CurrentPath == null || pathSteering.CurrentPath.CurrentNode == null) return;

            GUI.DrawLine(spriteBatch,
                new Vector2(Character.DrawPosition.X, -Character.DrawPosition.Y),
                new Vector2(pathSteering.CurrentPath.CurrentNode.DrawPosition.X, -pathSteering.CurrentPath.CurrentNode.DrawPosition.Y),
                Color.LightGreen);


            for (int i = 1; i < pathSteering.CurrentPath.Nodes.Count; i++)
            {
                GUI.DrawLine(spriteBatch,
                    new Vector2(pathSteering.CurrentPath.Nodes[i].DrawPosition.X, -pathSteering.CurrentPath.Nodes[i].DrawPosition.Y),
                    new Vector2(pathSteering.CurrentPath.Nodes[i - 1].DrawPosition.X, -pathSteering.CurrentPath.Nodes[i - 1].DrawPosition.Y),
                    Color.LightGreen);

                GUI.SmallFont.DrawString(spriteBatch,
                    pathSteering.CurrentPath.Nodes[i].ID.ToString(),
                    new Vector2(pathSteering.CurrentPath.Nodes[i].DrawPosition.X, -pathSteering.CurrentPath.Nodes[i].DrawPosition.Y - 10),
                    Color.LightGreen);
            }
        }
    }
}
