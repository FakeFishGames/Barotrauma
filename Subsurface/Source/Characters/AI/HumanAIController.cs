using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class HumanAIController : AIController
    {
        const float UpdateObjectiveInterval = 0.5f;

        private AIObjectiveManager objectiveManager;

        private IndoorsSteeringManager indoorsSteeringManager;
        private SteeringManager outdoorsSteeringManager;

        private AITarget selectedAiTarget;

        private float updateObjectiveTimer;

        public HumanAIController(Character c) : base(c)
        {
            indoorsSteeringManager = new IndoorsSteeringManager(this, true);
            outdoorsSteeringManager = new SteeringManager(this);

            objectiveManager = new AIObjectiveManager(c);
            objectiveManager.AddObjective(new AIObjectiveFindSafety(c));
            objectiveManager.AddObjective(new AIObjectiveIdle(c));
        }

        public override void Update(float deltaTime)
        {
            Character.ClearInputs();

            steeringManager = Character.AnimController.CurrentHull == null ? outdoorsSteeringManager : indoorsSteeringManager;

            if (updateObjectiveTimer>0.0f)
            {
                updateObjectiveTimer -= deltaTime;
            }
            else
            {
                objectiveManager.UpdateObjectives();
                updateObjectiveTimer = UpdateObjectiveInterval;
            }

            objectiveManager.DoCurrentObjective(deltaTime);
         
            Character.AnimController.IgnorePlatforms = (-Character.AnimController.TargetMovement.Y > Math.Abs(Character.AnimController.TargetMovement.X));

            if (Character.IsKeyDown(InputType.Aim))
            {
                Character.AnimController.TargetDir = Character.CursorPosition.X > Character.Position.X ? Direction.Right : Direction.Left;
            }
            else if (Math.Abs(Character.AnimController.TargetMovement.X) > 0.1f && !Character.AnimController.InWater)
            {
                Character.AnimController.TargetDir = Character.AnimController.TargetMovement.X > 0.0f ? Direction.Right : Direction.Left;
            }

            float currObjectivePriority = objectiveManager.GetCurrentPriority(Character);
            float moveSpeed = MathHelper.Clamp(currObjectivePriority/10.0f, 1.0f, 3.0f);
            
            steeringManager.Update(moveSpeed);
        }

        public override void OnAttacked(IDamageable attacker, float amount)
        {
            var enemy = attacker as Character;
            if (enemy == null) return;

            objectiveManager.AddObjective(new AIObjectiveCombat(Character, enemy));
        }

        public void SetOrder(Order order, string option)
        {
            objectiveManager.SetOrder(order, option);
        }

        public override void SelectTarget(AITarget target)
        {
            selectedAiTarget = target;
        }

        public override void DebugDraw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            if (selectedAiTarget != null)
            {
                GUI.DrawLine(spriteBatch, new Vector2(Character.Position.X, -Character.Position.Y), ConvertUnits.ToDisplayUnits(new Vector2(selectedAiTarget.SimPosition.X, -selectedAiTarget.SimPosition.Y)), Color.Red);
            }

            IndoorsSteeringManager pathSteering = steeringManager as IndoorsSteeringManager;
            if (pathSteering == null || pathSteering.CurrentPath == null || pathSteering.CurrentPath.CurrentNode==null) return;

            GUI.DrawLine(spriteBatch,
                new Vector2(Character.Position.X, -Character.Position.Y),
                new Vector2(pathSteering.CurrentPath.CurrentNode.Position.X, -pathSteering.CurrentPath.CurrentNode.Position.Y),
                Color.LightGreen);


            for (int i = 1; i < pathSteering.CurrentPath.Nodes.Count; i++)
            {
                GUI.DrawLine(spriteBatch,
                    new Vector2(pathSteering.CurrentPath.Nodes[i].Position.X, -pathSteering.CurrentPath.Nodes[i].Position.Y),
                    new Vector2(pathSteering.CurrentPath.Nodes[i - 1].Position.X, -pathSteering.CurrentPath.Nodes[i-1].Position.Y),
                    Color.LightGreen);
            }
        }
    }
}
