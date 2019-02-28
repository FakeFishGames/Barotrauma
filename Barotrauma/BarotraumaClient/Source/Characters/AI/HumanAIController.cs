using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Linq;

namespace Barotrauma
{
    partial class HumanAIController : AIController
    {
        partial void InitProjSpecific()
        {
            /*if (GameMain.GameSession != null && GameMain.GameSession.CrewManager != null)
            {
                CurrentOrder = Order.PrefabList.Find(o => o.AITag == "dismissed");
                objectiveManager.SetOrder(CurrentOrder, "", null);
                GameMain.GameSession.CrewManager.SetCharacterOrder(Character, CurrentOrder, null, null);
            }*/
        }

        partial void SetOrderProjSpecific(Order order)
        {
            GameMain.GameSession.CrewManager.DisplayCharacterOrder(Character, order);
        }

        public override void DebugDraw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            Vector2 pos = Character.WorldPosition;
            pos.Y = -pos.Y;
            Vector2 textOffset = new Vector2(-40, -120);

            if (SelectedAiTarget?.Entity != null)
            {
                //GUI.DrawLine(spriteBatch, pos, new Vector2(SelectedAiTarget.WorldPosition.X, -SelectedAiTarget.WorldPosition.Y), Color.Red);
                //GUI.DrawString(spriteBatch, pos + textOffset, $"AI TARGET: {SelectedAiTarget.Entity.ToString()}", Color.White, Color.Black);
            }

            if (ObjectiveManager != null)
            {
                var currentOrder = ObjectiveManager.CurrentOrder;
                if (currentOrder != null)
                {
                    GUI.DrawString(spriteBatch, pos + textOffset, $"ORDER: {currentOrder.DebugTag} ({currentOrder.GetPriority(ObjectiveManager).FormatZeroDecimal()})", Color.White, Color.Black);
                }
                var currentObjective = ObjectiveManager.CurrentObjective;
                if (currentObjective != null)
                {
                    GUI.DrawString(spriteBatch, pos + textOffset + new Vector2(0, 20), $"OBJECTIVE: {currentObjective.DebugTag} ({currentObjective.GetPriority(ObjectiveManager).FormatZeroDecimal()})", Color.White, Color.Black);
                    var subObjective = currentObjective.CurrentSubObjective;
                    if (subObjective != null)
                    {
                        GUI.DrawString(spriteBatch, pos + textOffset + new Vector2(0, 40), $"SUBOBJECTIVE: {subObjective.DebugTag} ({subObjective.GetPriority(ObjectiveManager).FormatZeroDecimal()})", Color.White, Color.Black);
                    }
                }
            }

            if (steeringManager is IndoorsSteeringManager pathSteering)
            {
                if (pathSteering.CurrentPath != null)
                {
                    if (pathSteering.CurrentPath.CurrentNode != null)
                    {
                        GUI.DrawLine(spriteBatch, pos, new Vector2(pathSteering.CurrentPath.CurrentNode.DrawPosition.X, -pathSteering.CurrentPath.CurrentNode.DrawPosition.Y), Color.BlueViolet, 0, 3);
                    }
                    for (int i = 1; i < pathSteering.CurrentPath.Nodes.Count; i++)
                    {
                        GUI.DrawLine(spriteBatch,
                            new Vector2(pathSteering.CurrentPath.Nodes[i].DrawPosition.X, -pathSteering.CurrentPath.Nodes[i].DrawPosition.Y),
                            new Vector2(pathSteering.CurrentPath.Nodes[i - 1].DrawPosition.X, -pathSteering.CurrentPath.Nodes[i - 1].DrawPosition.Y),
                            Color.Blue * 0.5f, 0, 3);

                        GUI.SmallFont.DrawString(spriteBatch,
                            pathSteering.CurrentPath.Nodes[i].ID.ToString(),
                            new Vector2(pathSteering.CurrentPath.Nodes[i].DrawPosition.X, -pathSteering.CurrentPath.Nodes[i].DrawPosition.Y - 10),
                            Color.LightGreen);
                    }
                }
            }
            else
            {
                GUI.DrawLine(spriteBatch, pos, pos + new Vector2(Steering.X, -Steering.Y), Color.Blue, width: 5);
            }

            if (Character.IsKeyDown(InputType.Aim))
            {
                GUI.DrawLine(spriteBatch, pos, new Vector2(Character.CursorWorldPosition.X, -Character.CursorWorldPosition.Y), Color.Red, width: 4);
            }
        }
    }
}
