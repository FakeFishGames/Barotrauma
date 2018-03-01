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
            steeringManager = new IndoorsSteeringManager(this, true, false);

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

            (Character.AnimController as HumanoidAnimController).Crouching = false;
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

            if (Character.CanSpeak)
            {
                ReportProblems();
                UpdateSpeaking();
            }

            objectiveManager.DoCurrentObjective(deltaTime);
         
            float currObjectivePriority = objectiveManager.GetCurrentPriority(Character);
            float moveSpeed = 1.0f;

            if (currObjectivePriority > 30.0f)
            {
                moveSpeed *= Character.AnimController.InWater ? Character.AnimController.SwimSpeedMultiplier : Character.AnimController.RunSpeedMultiplier;                
            }
            
            steeringManager.Update(moveSpeed);

            bool ignorePlatforms = Character.AnimController.TargetMovement.Y < -0.5f &&
                (-Character.AnimController.TargetMovement.Y > Math.Abs(Character.AnimController.TargetMovement.X));

            var currPath = (steeringManager as IndoorsSteeringManager).CurrentPath;
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
                Character.AnimController.TargetMovement = new Vector2(
                    Character.AnimController.TargetMovement.X,
                    MathHelper.Clamp(Character.AnimController.TargetMovement.Y, -1.0f, 1.0f)) * Character.SpeedMultiplier;

                Character.SpeedMultiplier = 1.0f;
            }

            if (Character.SelectedConstruction != null && Character.SelectedConstruction.GetComponent<Items.Components.Ladder>()!=null)
            {
                if (currPath != null && currPath.CurrentNode != null && currPath.CurrentNode.Ladders != null)
                {
                    Character.AnimController.TargetMovement = new Vector2( 0.0f, Math.Sign(Character.AnimController.TargetMovement.Y));
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
                var divingSuit = Character.Inventory.FindItem("Diving Suit");
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

            if (Character.Bleeding > 1.0f || Character.Vitality < Character.MaxVitality * 0.1f)
            {
                var orderPrefab = Order.PrefabList.Find(o => o.AITag == "requestfirstaid");
                newOrder = new Order(orderPrefab, Character.CurrentHull, null);
            }

            if (newOrder != null)
            {
#if CLIENT
                //TODO: move crewmanager to shared project to allow ai crew to report things in dedicated server?
                if (GameMain.GameSession?.CrewManager != null && GameMain.GameSession.CrewManager.AddOrder(newOrder, newOrder.FadeOutTime))
                {
                    Character.Speak(
                        newOrder.GetChatMessage("", Character.CurrentHull?.RoomName), ChatMessageType.Order);

                    if (GameMain.Server != null)
                    {
                        OrderChatMessage msg = new OrderChatMessage(newOrder, "", Character.CurrentHull, null, Character);
                        GameMain.Server.SendOrderChatMessage(msg, null);
                    }
                }             
#endif
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

            objectiveManager.AddObjective(new AIObjectiveCombat(Character, attacker));

            //the objective in the manager is not necessarily the same as the one we just instantiated,
            //because the objective isn't added if there's already an identical objective in the manager
            var combatObjective = objectiveManager.GetObjective<AIObjectiveCombat>();
            combatObjective.MaxEnemyDamage = Math.Max(totalDamage, combatObjective.MaxEnemyDamage);
        }

        public void SetOrder(Order order, string option)
        {
            CurrentOrderOption = option;
            CurrentOrder = order;
            objectiveManager.SetOrder(order, option);
            Character.Speak(TextManager.Get("DialogAffirmative"), null, 1.0f);

            SetOrderProjSpecific(order);
        }
        partial void SetOrderProjSpecific(Order order);

        public override void SelectTarget(AITarget target)
        {
            selectedAiTarget = target;
        }
    }
}
