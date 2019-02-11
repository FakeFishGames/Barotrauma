using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma
{
    partial class HumanAIController : AIController
    {
        public static bool DisableCrewAI;

        const float UpdateObjectiveInterval = 0.5f;

        private AIObjectiveManager objectiveManager;
        
        private float updateObjectiveTimer;

        private bool shouldCrouch;
        private float crouchRaycastTimer;
        const float CrouchRaycastInterval = 1.0f;

        private SteeringManager outsideSteering, insideSteering;

        public override AIObjectiveManager ObjectiveManager
        {
            get { return objectiveManager; }
        }

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
            insideSteering = new IndoorsSteeringManager(this, true, false);
            outsideSteering = new SteeringManager(this);

            objectiveManager = new AIObjectiveManager(c);
            objectiveManager.AddObjective(new AIObjectiveFindSafety(c));
            objectiveManager.AddObjective(new AIObjectiveIdle(c));

            updateObjectiveTimer = Rand.Range(0.0f, UpdateObjectiveInterval);

            InitProjSpecific();
        }
        partial void InitProjSpecific();

        public override void Update(float deltaTime)
        {
            if (DisableCrewAI || Character.IsUnconscious) return;
            
            if (Character.Submarine != null || SelectedAiTarget?.Entity?.Submarine != null)
            {
                if (steeringManager != insideSteering) insideSteering.Reset();
                steeringManager = insideSteering;
            }
            else
            {
                if (steeringManager != outsideSteering) outsideSteering.Reset();
                steeringManager = outsideSteering;
            }

            (Character.AnimController as HumanoidAnimController).Crouching = shouldCrouch;
            CheckCrouching(deltaTime);
            Character.ClearInputs();
            
            if (updateObjectiveTimer > 0.0f)
            {
                updateObjectiveTimer -= deltaTime;
            }
            else
            {
                objectiveManager.UpdateObjectives();
                updateObjectiveTimer = UpdateObjectiveInterval;
            }

            if (Character.SpeechImpediment < 100.0f)
            {
                ReportProblems();
                UpdateSpeaking();
            }

            objectiveManager.DoCurrentObjective(deltaTime);
         
            float currObjectivePriority = objectiveManager.GetCurrentPriority();

            bool run = currObjectivePriority > 30.0f;         
            steeringManager.Update(Character.AnimController.GetCurrentSpeed(run));

            bool ignorePlatforms = Character.AnimController.TargetMovement.Y < -0.5f &&
                (-Character.AnimController.TargetMovement.Y > Math.Abs(Character.AnimController.TargetMovement.X));

            var currPath = (steeringManager as IndoorsSteeringManager)?.CurrentPath;
            if (currPath != null && currPath.CurrentNode != null)
            {
                if (currPath.CurrentNode.SimPosition.Y < Character.AnimController.GetColliderBottom().Y)
                {
                    ignorePlatforms = true;
                }
            }

            Character.AnimController.IgnorePlatforms = ignorePlatforms;

            if (!Character.AnimController.InWater)
            {
                Vector2 targetMovement = new Vector2(
                    Character.AnimController.TargetMovement.X,
                    MathHelper.Clamp(Character.AnimController.TargetMovement.Y, -1.0f, 1.0f));

                float maxSpeed = Character.GetCurrentMaxSpeed(run);
                targetMovement.X = MathHelper.Clamp(targetMovement.X, -maxSpeed, maxSpeed);
                targetMovement.Y = MathHelper.Clamp(targetMovement.Y, -maxSpeed, maxSpeed);

                //apply speed multiplier if 
                //  a. it's boosting the movement speed and the character is trying to move fast (= running)
                //  b. it's a debuff that decreases movement speed
                if (run || Character.SpeedMultiplier <= 0.0f) targetMovement *= Character.SpeedMultiplier;
                
                Character.SpeedMultiplier = 1.0f;   // Reset, items will set the value before the next update

                Character.AnimController.TargetMovement = targetMovement;
            }

            if (Character.AnimController.Anim == AnimController.Animation.Climbing &&
                Character.SelectedConstruction != null &&
                Character.SelectedConstruction.GetComponent<Items.Components.Ladder>() != null)
            {
                if (currPath != null && currPath.CurrentNode != null && currPath.CurrentNode.Ladders != null)
                {
                    Character.AnimController.TargetMovement = new Vector2(0.0f, Math.Sign(Character.AnimController.TargetMovement.Y));
                }
            }

            //suit can be taken off if there character is inside a hull and there's air in the room
            bool canTakeOffSuit = Character.AnimController.CurrentHull != null &&
                Character.AnimController.CurrentHull.OxygenPercentage > 30.0f &&
                Character.AnimController.CurrentHull.WaterVolume < Character.AnimController.CurrentHull.Volume * 0.3f;

            //the suit can be taken off and the character is running out of oxygen (couldn't find a tank for the suit?) or idling
            //-> take the suit off
            if (canTakeOffSuit && (Character.Oxygen < 50.0f || objectiveManager.CurrentObjective is AIObjectiveIdle))
            {
                var divingSuit = Character.Inventory.FindItemByIdentifier("divingsuit") ?? Character.Inventory.FindItemByTag("divingsuit");
                if (divingSuit != null) divingSuit.Drop(Character);
            }

            if (Character.IsKeyDown(InputType.Aim))
            {
                var cursorDiffX = Character.CursorPosition.X - Character.Position.X;
                if (cursorDiffX > 10.0f)
                {
                    Character.AnimController.TargetDir = Direction.Right;
                }
                else if (cursorDiffX < -10.0f)
                {
                    Character.AnimController.TargetDir = Direction.Left;
                }

                if (Character.SelectedConstruction != null) Character.SelectedConstruction.SecondaryUse(deltaTime, Character);

            }
            else if (Math.Abs(Character.AnimController.TargetMovement.X) > 0.1f && !Character.AnimController.InWater)
            {
                Character.AnimController.TargetDir = Character.AnimController.TargetMovement.X > 0.0f ? Direction.Right : Direction.Left;
            }
        }
        
        private void ReportProblems()
        {
            if (GameMain.Client != null) return;

            Order newOrder = null;
            if (Character.CurrentHull != null)
            {
                if (Character.CurrentHull.FireSources.Count > 0)
                {
                    var orderPrefab = Order.PrefabList.Find(o => o.AITag == "reportfire");
                    newOrder = new Order(orderPrefab, Character.CurrentHull, null);
                }

                if (Character.CurrentHull.ConnectedGaps.Any(g => !g.IsRoomToRoom && g.ConnectedDoor == null && g.Open > 0.0f))
                {
                    var orderPrefab = Order.PrefabList.Find(o => o.AITag == "reportbreach");
                    newOrder = new Order(orderPrefab, Character.CurrentHull, null);
                }

                foreach (Character c in Character.CharacterList)
                {
                    if (c.CurrentHull == Character.CurrentHull && !c.IsDead &&
                        (c.AIController is EnemyAIController || c.TeamID != Character.TeamID))
                    {
                        var orderPrefab = Order.PrefabList.Find(o => o.AITag == "reportintruders");
                        newOrder = new Order(orderPrefab, Character.CurrentHull, null);
                    }
                }
            }

            if (Character.CurrentHull != null && (Character.Bleeding > 1.0f || Character.Vitality < Character.MaxVitality * 0.1f))
            {
                var orderPrefab = Order.PrefabList.Find(o => o.AITag == "requestfirstaid");
                newOrder = new Order(orderPrefab, Character.CurrentHull, null);
            }

            if (newOrder != null)
            {
                if (GameMain.GameSession?.CrewManager != null && GameMain.GameSession.CrewManager.AddOrder(newOrder, newOrder.FadeOutTime))
                {
                    Character.Speak(
                        newOrder.GetChatMessage("", Character.CurrentHull?.RoomName, givingOrderToSelf: false), ChatMessageType.Order);

                    if (GameMain.Server != null)
                    {
                        OrderChatMessage msg = new OrderChatMessage(newOrder, "", Character.CurrentHull, null, Character);
                        GameMain.Server.SendOrderChatMessage(msg);
                    }
                }
            }
        }

        private void UpdateSpeaking()
        {
            if (Character.Oxygen < 20.0f)
            {
                Character.Speak(TextManager.Get("DialogLowOxygen"), null, 0, "lowoxygen", 30.0f);
            }

            if (Character.Bleeding > 2.0f)
            {
                Character.Speak(TextManager.Get("DialogBleeding"), null, 0, "bleeding", 30.0f);
            }

            if (Character.PressureTimer > 50.0f && Character.CurrentHull != null)
            {
                Character.Speak(TextManager.Get("DialogPressure").Replace("[roomname]", Character.CurrentHull.RoomName), null, 0, "pressure", 30.0f);
            }
        }
        
        public override void OnAttacked(Character attacker, AttackResult attackResult)
        {
            float totalDamage = attackResult.Damage;
            if (totalDamage <= 0.0f || attacker == null) return;

            if (attacker.AnimController.Anim == AnimController.Animation.CPR && attacker.SelectedCharacter == Character)
            {
                // Don't attack characters that damage you while doing cpr, because let's assume that they are helping you.
                // Should not cancel any existing ai objectives (so that if the character attacked you and then helped, we still would want to retaliate).
                return;
            }

            objectiveManager.AddObjective(new AIObjectiveCombat(Character, attacker), Rand.Range(0.5f, 1, Rand.RandSync.Unsynced), () =>
            {
                //the objective in the manager is not necessarily the same as the one we just instantiated,
                //because the objective isn't added if there's already an identical objective in the manager
                var combatObjective = objectiveManager.GetObjective<AIObjectiveCombat>();
                combatObjective.MaxEnemyDamage = Math.Max(totalDamage, combatObjective.MaxEnemyDamage);
            });
        }

        public void SetOrder(Order order, string option, Character orderGiver, bool speak = true)
        {
            CurrentOrderOption = option;
            CurrentOrder = order;
            objectiveManager.SetOrder(order, option, orderGiver);
            if (speak && Character.SpeechImpediment < 100.0f) Character.Speak(TextManager.Get("DialogAffirmative"), null, 1.0f);

            SetOrderProjSpecific(order);
        }
        partial void SetOrderProjSpecific(Order order);

        public override void SelectTarget(AITarget target)
        {
            SelectedAiTarget = target;
        }

        private void CheckCrouching(float deltaTime)
        {
            crouchRaycastTimer -= deltaTime;
            if (crouchRaycastTimer > 0.0f) return;

            crouchRaycastTimer = CrouchRaycastInterval;

            //start the raycast in front of the character in the direction it's heading to
            Vector2 startPos = Character.SimPosition;
            startPos.X += MathHelper.Clamp(Character.AnimController.TargetMovement.X, -1.0f, 1.0f);

            //do a raycast upwards to find any walls
            float minCeilingDist = Character.AnimController.Collider.height / 2 + Character.AnimController.Collider.radius + 0.1f;
            shouldCrouch = Submarine.PickBody(startPos, startPos + Vector2.UnitY * minCeilingDist, null, Physics.CollisionWall) != null;
        }
    }
}
