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
        
        private AITarget selectedAiTarget;

        private float updateObjectiveTimer;

        public Order CurrentOrder
        {
            get;
            private set;
        }

        public string CurrentOrderOption
        {
            get;
            private set;
        }

        public HumanAIController(Character c) : base(c)
        {
            steeringManager = new IndoorsSteeringManager(this, true);

            objectiveManager = new AIObjectiveManager(c);
            objectiveManager.AddObjective(new AIObjectiveFindSafety(c));
            objectiveManager.AddObjective(new AIObjectiveIdle(c));

            updateObjectiveTimer = Rand.Range(0.0f, UpdateObjectiveInterval);

            if (GameMain.GameSession!=null && GameMain.GameSession.CrewManager!=null)
            {
                CurrentOrder = Order.PrefabList.Find(o => o.Name.ToLower() == "dismissed");
                objectiveManager.SetOrder(CurrentOrder, "");
                GameMain.GameSession.CrewManager.SetCharacterOrder(Character, CurrentOrder);
            }

        }

        public override void Update(float deltaTime)
        {
            Character.ClearInputs();

            //steeringManager = Character.AnimController.CurrentHull == null ? outdoorsSteeringManager : indoorsSteeringManager;

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
         
            float currObjectivePriority = objectiveManager.GetCurrentPriority(Character);
            float moveSpeed = MathHelper.Clamp(currObjectivePriority/10.0f, 1.0f, 3.0f);
            
            steeringManager.Update(moveSpeed);

            Character.AnimController.IgnorePlatforms = (-Character.AnimController.TargetMovement.Y > Math.Abs(Character.AnimController.TargetMovement.X*0.5f));

            if (!Character.AnimController.InWater)
            {
                Character.AnimController.TargetMovement = new Vector2(
                    Character.AnimController.TargetMovement.X,
                    MathHelper.Clamp(Character.AnimController.TargetMovement.Y, -1.0f, 1.0f)) * Character.SpeedMultiplier;

                Character.SpeedMultiplier = 1.0f;
            }

            if (Character.SelectedConstruction != null && Character.SelectedConstruction.GetComponent<Items.Components.Ladder>()!=null)
            {
                var currPath = (steeringManager as IndoorsSteeringManager).CurrentPath;
                if (currPath != null && currPath.CurrentNode != null && currPath.CurrentNode.Ladders != null)
                {
                    Character.AnimController.TargetMovement = new Vector2( 0.0f, Math.Sign(Character.AnimController.TargetMovement.Y));
                }

            }

            if (Character.IsKeyDown(InputType.Aim))
            {
                Character.AnimController.TargetDir = Character.CursorPosition.X > Character.Position.X ? Direction.Right : Direction.Left;
                if (Character.SelectedConstruction != null) Character.SelectedConstruction.SecondaryUse(deltaTime, Character);

            }
            else if (Math.Abs(Character.AnimController.TargetMovement.X) > 0.1f && !Character.AnimController.InWater)
            {
                Character.AnimController.TargetDir = Character.AnimController.TargetMovement.X > 0.0f ? Direction.Right : Direction.Left;
            }



        }

        public override void OnAttacked(IDamageable attacker, float amount)
        {
            var enemy = attacker as Character;
            if (enemy == null) return;

            objectiveManager.AddObjective(new AIObjectiveCombat(Character, enemy));
        }

        public void SetOrder(Order order, string option)
        {
            CurrentOrderOption = option;
            CurrentOrder = order;
            objectiveManager.SetOrder(order, option);

            GameMain.GameSession.CrewManager.SetCharacterOrder(Character, order);
        }

        public override void SelectTarget(AITarget target)
        {
            selectedAiTarget = target;
        }

        public override void DebugDraw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            if (selectedAiTarget != null)
            {
                GUI.DrawLine(spriteBatch, 
                    new Vector2(Character.WorldPosition.X, -Character.WorldPosition.Y), 
                    new Vector2(selectedAiTarget.WorldPosition.X, -selectedAiTarget.WorldPosition.Y), Color.Red);
            }

            IndoorsSteeringManager pathSteering = steeringManager as IndoorsSteeringManager;
            if (pathSteering == null || pathSteering.CurrentPath == null || pathSteering.CurrentPath.CurrentNode==null) return;

            GUI.DrawLine(spriteBatch,
                new Vector2(Character.WorldPosition.X, -Character.WorldPosition.Y),
                new Vector2(pathSteering.CurrentPath.CurrentNode.Position.X+Submarine.Loaded.Position.X, -(pathSteering.CurrentPath.CurrentNode.Position.Y+Submarine.Loaded.Position.Y)),
                Color.LightGreen);


            for (int i = 1; i < pathSteering.CurrentPath.Nodes.Count; i++)
            {
                GUI.DrawLine(spriteBatch,
                    new Vector2(pathSteering.CurrentPath.Nodes[i].Position.X + Submarine.Loaded.Position.X, -(pathSteering.CurrentPath.Nodes[i].Position.Y + Submarine.Loaded.Position.Y)),
                    new Vector2(pathSteering.CurrentPath.Nodes[i - 1].Position.X + Submarine.Loaded.Position.X, -(pathSteering.CurrentPath.Nodes[i-1].Position.Y + Submarine.Loaded.Position.Y)),
                    Color.LightGreen);
            }
        }
    }
}
