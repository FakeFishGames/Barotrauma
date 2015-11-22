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

        public HumanAIController(Character c) : base(c)
        {
            steeringManager = new PathSteeringManager(this);

            objectiveManager = new AIObjectiveManager(c);
            objectiveManager.AddObjective(new AIObjectiveFindSafety());
        }

        public override void Update(float deltaTime)
        {
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

            //if (Character.Controlled != null)
            //{
            //    steeringManager.SteeringSeek(Character.Controlled.Position);
            //}

            Character.AnimController.IgnorePlatforms = (-Character.AnimController.TargetMovement.Y > Math.Abs(Character.AnimController.TargetMovement.X));

            if (Math.Abs(Character.AnimController.TargetMovement.X)>0.1f)
            {
                Character.AnimController.TargetDir = Character.AnimController.TargetMovement.X > 0.0f ? Direction.Right : Direction.Left;
            }

            steeringManager.Update();
        }

        public override void SelectTarget(AITarget target)
        {
            selectedAiTarget = target;
        }

        public override void DebugDraw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {

            if (selectedAiTarget != null)
            {
                GUI.DrawLine(spriteBatch, new Vector2(Character.Position.X, -Character.Position.Y), ConvertUnits.ToDisplayUnits(new Vector2(selectedAiTarget.Position.X, -selectedAiTarget.Position.Y)), Color.Red);
            }

            PathSteeringManager pathSteering = steeringManager as PathSteeringManager;
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
