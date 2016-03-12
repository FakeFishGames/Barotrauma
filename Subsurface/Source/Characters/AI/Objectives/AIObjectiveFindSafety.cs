using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveFindSafety : AIObjective
    {
        const float SearchHullInterval = 3.0f;
        const float MinSafety = 50.0f;

        private AIObjectiveGoTo goToObjective;

        private List<Hull> unreachable;

        private float currenthullSafety;

        private float searchHullTimer;

        private AIObjective divingGearObjective;

        public float? OverrideCurrentHullSafety;

        public AIObjectiveFindSafety(Character character)
            : base(character, "")
        {
            unreachable = new List<Hull>();
        }

        protected override void Act(float deltaTime)
        {
            if (character.AnimController.CurrentHull == null) return;

            currenthullSafety = OverrideCurrentHullSafety == null ? 
                GetHullSafety(character.AnimController.CurrentHull) : (float)OverrideCurrentHullSafety;

            if (character.AnimController.CurrentHull == null || currenthullSafety > MinSafety)
            {
                character.AIController.SteeringManager.SteeringSeek(character.AnimController.CurrentHull.SimPosition);
                character.AIController.SelectTarget(null);

                goToObjective = null;
                return;
            }

            var currentHull = character.AnimController.CurrentHull;
            if (currentHull.Volume / currentHull.FullVolume > 0.5f)
            {
                if (!FindDivingGear(deltaTime)) return;
            }

            if (searchHullTimer>0.0f)
            {
                searchHullTimer -= deltaTime;
                //return;
            }
            else
            {
                Hull bestHull = null;
                float bestValue = currenthullSafety;

                foreach (Hull hull in Hull.hullList)
                {
                    if (hull == character.AnimController.CurrentHull) continue;
                    if (unreachable.Contains(hull)) continue;

                    float hullValue =  GetHullSafety(hull);
                    hullValue -= (float)Math.Sqrt(Math.Abs(character.Position.X- hull.Position.X));
                    hullValue -= (float)Math.Sqrt(Math.Abs(character.Position.Y - hull.Position.Y)*2.0f);

                    if (bestHull == null || hullValue > bestValue)
                    {
                        bestHull = hull;
                        bestValue = hullValue;
                    }
                }

                if (bestHull != null)
                {
                    //var path = pathSteering.PathFinder.FindPath(character.SimPosition, bestHull.SimPosition);
                    //if (pathSteering.CurrentPath == null || (pathSteering.CurrentPath.NextNode==null && pathSteering.CurrentPath.Cost > path.Cost) || 
                    //    pathSteering.CurrentPath.Unreachable || goToObjective==null)
                    //{
               
                        //pathSteering.SetPath(path);
                        goToObjective = new AIObjectiveGoTo(bestHull, character);
                    //}

                    
                    //haracter.AIController.SelectTarget(bestHull.AiTarget);
                }


                searchHullTimer = SearchHullInterval;
            }

            if (goToObjective != null)
            {
                var pathSteering = character.AIController.SteeringManager as IndoorsSteeringManager;
                if (pathSteering!=null && pathSteering.CurrentPath!= null && 
                    pathSteering.CurrentPath.Unreachable && !unreachable.Contains(goToObjective.Target))
                {
                    unreachable.Add(goToObjective.Target as Hull);
                }


                goToObjective.TryComplete(deltaTime);
            }
        }

        private bool FindDivingGear(float deltaTime)
        {
            if (divingGearObjective==null)
            {
                divingGearObjective = new AIObjectiveFindDivingGear(character, false);
            }

            divingGearObjective.TryComplete(deltaTime);
            return divingGearObjective.IsCompleted();

            //var item = character.Inventory.FindItem("diving");
            //if (item == null)
            //{
            //    //get a diving mask/suit first
            //    if (!(divingGearObjective is AIObjectiveGetItem))
            //    {
            //        divingGearObjective = new AIObjectiveGetItem(character, "diving", true);
            //    }
            //}
            //else
            //{
            //    var containedItems = item.ContainedItems;
            //    if (containedItems == null) return true;

            //    //check if there's an oxygen tank in the mask
            //    var oxygenTank = Array.Find(containedItems, i => i.Name == "Oxygen Tank" && i.Condition > 0.0f);

            //    if (oxygenTank != null) return true;


            //    if (!(divingGearObjective is AIObjectiveContainItem))
            //    {
            //        divingGearObjective = new AIObjectiveContainItem(character, "Oxygen Tank", item.GetComponent<ItemContainer>());
            //    }
            //}

            //if (divingGearObjective != null)
            //{
            //    divingGearObjective.TryComplete(deltaTime);

            //    bool isCompleted = divingGearObjective.IsCompleted();
            //    if (isCompleted) divingGearObjective = null;
            //    return isCompleted;
            //}

            //return false;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return (otherObjective is AIObjectiveFindSafety);
        }

        public override float GetPriority(Character character)
        {
            if (character.AnimController.CurrentHull == null) return 0.0f;
            currenthullSafety = GetHullSafety(character.AnimController.CurrentHull);
            priority = 100.0f - currenthullSafety;


            if (divingGearObjective != null && !divingGearObjective.IsCompleted()) priority += 20.0f;

            return priority;
        }

        private float GetHullSafety(Hull hull)
        {
            float waterPercentage = (hull.Volume / hull.FullVolume)*100.0f;
            float fireAmount = 0.0f;

            foreach (FireSource fireSource in hull.FireSources)
            {
                fireAmount += Math.Max(fireSource.Size.X,50.0f);
            }
            
            float safety = 100.0f - fireAmount;
            
            if (waterPercentage > 30.0f && character.Oxygen<80.0f) safety -= waterPercentage; 
            if (hull.OxygenPercentage < 30.0f) safety -= (30.0f-hull.OxygenPercentage)*5.0f;

            return safety;
        }
    }
}
