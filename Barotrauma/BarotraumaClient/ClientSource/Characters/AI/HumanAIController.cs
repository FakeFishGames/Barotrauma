using Microsoft.Xna.Framework;
using FarseerPhysics;
using System;
using System.Linq;

namespace Barotrauma
{
    partial class HumanAIController : AIController
    {
        public override void DebugDraw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            if (Character == Character.Controlled) { return; }
            if (!DebugAI) { return; }
            Vector2 pos = Character.WorldPosition;
            pos.Y = -pos.Y;
            Vector2 textOffset = new Vector2(-40, -160);
            textOffset.Y -= Math.Max(ObjectiveManager.CurrentOrders.Count - 1, 0) * 20;

            if (SelectedAiTarget?.Entity != null)
            {
                //GUI.DrawLine(spriteBatch, pos, new Vector2(SelectedAiTarget.WorldPosition.X, -SelectedAiTarget.WorldPosition.Y), GUIStyle.Red);
                //GUI.DrawString(spriteBatch, pos + textOffset, $"AI TARGET: {SelectedAiTarget.Entity.ToString()}", Color.White, Color.Black);
            }

            Vector2 stringDrawPos = pos + textOffset;
            GUI.DrawString(spriteBatch, stringDrawPos, Character.Name, Color.White, Color.Black);

            var currentOrder = ObjectiveManager.CurrentOrder;
            if (ObjectiveManager.CurrentOrders.Any())
            {
                var currentOrders = ObjectiveManager.CurrentOrders;
                currentOrders.Sort((x, y) => y.ManualPriority.CompareTo(x.ManualPriority));
                for (int i = 0; i < currentOrders.Count; i++)
                {
                    stringDrawPos += new Vector2(0, 20);
                    var order = currentOrders[i];
                    GUI.DrawString(spriteBatch, stringDrawPos, $"ORDER {i + 1}: {order.Objective.DebugTag} ({order.Objective.Priority.FormatZeroDecimal()})", Color.White, Color.Black);
                }
            }
            else if (ObjectiveManager.WaitTimer > 0)
            {
                stringDrawPos += new Vector2(0, 20);
                GUI.DrawString(spriteBatch, stringDrawPos - textOffset, $"Waiting... {ObjectiveManager.WaitTimer.FormatZeroDecimal()}", Color.White, Color.Black);
            }
            var currentObjective = ObjectiveManager.CurrentObjective;
            if (currentObjective != null)
            {
                int offset = currentOrder != null ? 20 + ((ObjectiveManager.CurrentOrders.Count - 1) * 20) : 0;
                if (currentOrder == null || currentOrder.Priority <= 0)
                {
                    stringDrawPos += new Vector2(0, 20);
                    GUI.DrawString(spriteBatch, stringDrawPos, $"MAIN OBJECTIVE: {currentObjective.DebugTag} ({currentObjective.Priority.FormatZeroDecimal()})", Color.White, Color.Black);
                }
                var subObjective = currentObjective.CurrentSubObjective;
                if (subObjective != null)
                {
                    stringDrawPos += new Vector2(0, 20);
                    GUI.DrawString(spriteBatch, stringDrawPos, $"SUBOBJECTIVE: {subObjective.DebugTag} ({subObjective.Priority.FormatZeroDecimal()})", Color.White, Color.Black);
                }
                var activeObjective = ObjectiveManager.GetActiveObjective();
                if (activeObjective != null)
                {
                    stringDrawPos += new Vector2(0, 20);
                    GUI.DrawString(spriteBatch, stringDrawPos, $"ACTIVE OBJECTIVE: {activeObjective.DebugTag} ({activeObjective.Priority.FormatZeroDecimal()})", Color.White, Color.Black);
                }
            }

            Vector2 objectiveStringDrawPos = stringDrawPos + new Vector2(120, 40);
            for (int i = 0; i < ObjectiveManager.Objectives.Count; i++)
            {
                var objective = ObjectiveManager.Objectives[i];
                GUI.DrawString(spriteBatch, objectiveStringDrawPos, $"{objective.DebugTag} ({objective.Priority.FormatZeroDecimal()})", Color.White, Color.Black * 0.5f);
                objectiveStringDrawPos += new Vector2(0, 18);
            }

            if (steeringManager is IndoorsSteeringManager pathSteering)
            {
                var path = pathSteering.CurrentPath;
                if (path != null)
                {
                    for (int i = 1; i < path.Nodes.Count; i++)
                    {
                        var previousNode = path.Nodes[i - 1];
                        var currentNode = path.Nodes[i];
                        GUI.DrawLine(spriteBatch,
                            new Vector2(currentNode.DrawPosition.X, -currentNode.DrawPosition.Y),
                            new Vector2(previousNode.DrawPosition.X, -previousNode.DrawPosition.Y),
                            Color.Blue * 0.5f, 0, 3);

                        GUIStyle.SmallFont.DrawString(spriteBatch,
                            currentNode.ID.ToString(),
                            new Vector2(currentNode.DrawPosition.X - 10, -currentNode.DrawPosition.Y - 30),
                            Color.Blue);
                    }
                    if (path.CurrentNode != null)
                    {
                        GUI.DrawLine(spriteBatch, pos,
                            new Vector2(path.CurrentNode.DrawPosition.X, -path.CurrentNode.DrawPosition.Y),
                            Color.BlueViolet, 0, 3);

                        GUI.DrawString(spriteBatch, stringDrawPos + new Vector2(0, 40), "Path cost: " + path.Cost.FormatZeroDecimal(), Color.White, Color.Black * 0.5f);
                    }
                }
            }
            GUI.DrawLine(spriteBatch, pos, pos + ConvertUnits.ToDisplayUnits(new Vector2(Character.AnimController.TargetMovement.X, -Character.AnimController.TargetMovement.Y)), Color.SteelBlue, width: 2);
            GUI.DrawLine(spriteBatch, pos, pos + ConvertUnits.ToDisplayUnits(new Vector2(Steering.X, -Steering.Y)), Color.Blue, width: 3);

            if (Character.AnimController.InWater && objectiveManager.GetActiveObjective() is AIObjectiveGoTo gotoObjective && gotoObjective.TargetGap != null)
            {
                Vector2 gapPosition = gotoObjective.TargetGap.WorldPosition;
                gapPosition.Y = -gapPosition.Y;
                GUI.DrawRectangle(spriteBatch, gapPosition - new Vector2(10.0f, 10.0f), new Vector2(20.0f, 20.0f), Color.Orange, false);
                GUI.DrawLine(spriteBatch, pos, gapPosition, Color.Orange * 0.5f, 0, 5);
            }

            //if (Character.IsKeyDown(InputType.Aim))
            //{
            //    GUI.DrawLine(spriteBatch, pos, new Vector2(Character.CursorWorldPosition.X, -Character.CursorWorldPosition.Y), Color.Yellow, width: 4);
            //}
        }
    }
}
