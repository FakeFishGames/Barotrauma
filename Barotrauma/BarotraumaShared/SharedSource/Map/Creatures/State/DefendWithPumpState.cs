using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.MapCreatures.Behavior
{
    class DefendWithPumpState : IBallastFloraState
    {
        private readonly BallastFloraBranch targetBranch;
        private readonly List<Pump> allAvailablePumps = new List<Pump>();
        private readonly List<Door> allAvailableDoors = new List<Door>();
        private readonly List<Pump> targetPumps = new List<Pump>();
        private readonly List<Door> jammedDoors = new List<Door>();

        private bool isFinished;
        private float timer = 10f;
        private bool filled;
        private bool tryDrown;
        private readonly Character attacker;

        public DefendWithPumpState(BallastFloraBranch branch, List<Item> items, Character attacker)
        {
            targetBranch = branch;
            this.attacker = attacker;

            foreach (Item item in items)
            {
                if (item.GetComponent<Pump>() is { } pump)
                {
                    allAvailablePumps.Add(pump);
                }

                if (item.GetComponent<Door>() is { } door)
                {
                    allAvailableDoors.Add(door);
                }
            }
        }

        public ExitState GetState()
        {
            if (isFinished) { return ExitState.ReturnLast; }
            if (targetBranch.CurrentHull == null) { return ExitState.ReturnLast; }
            return timer < 0 ? ExitState.ReturnLast : ExitState.Running;
        }

        public void Enter()
        {
            foreach (Pump pump in allAvailablePumps)
            {
                if (pump.Item.CurrentHull == targetBranch.CurrentHull)
                {
                    targetPumps.Add(pump);
                    pump.Hijacked = true;
                }
            }

            if (!targetPumps.Any() || targetPumps.All(p => !p.HasPower))
            {
                isFinished = true;
            }

            // lock the doors if the attacker is in the same hull as the ballast flora to try to drown them
            if (targetBranch.CurrentHull != null && attacker != null && attacker.CurrentHull == targetBranch.CurrentHull)
            {
                foreach (Door door in allAvailableDoors)
                {
                    if (door.LinkedGap != null && door.LinkedGap.linkedTo.Contains(targetBranch.CurrentHull))
                    {
                        door.TrySetState(false, false, true);
                        door.IsJammed = true;
                        jammedDoors.Add(door);
                    }
                }

                tryDrown = true;
            }
        }

        public void Exit()
        {
            foreach (Pump pump in targetPumps)
            {
                pump.Hijacked = false;
            }

            foreach (Door door in jammedDoors)
            {
                door.IsJammed = false;
            }
        }

        public void Update(float deltaTime)
        {
            foreach (Pump pump in targetPumps)
            {
                if (pump.TargetLevel != null)
                {
                    pump.TargetLevel = 100f;
                }
                else
                {
                    pump.FlowPercentage = 100f;
                }
            }

            if (tryDrown && !filled)
            {
                // keep the ballast filled for extra 10 seconds
                if (targetBranch.CurrentHull == null || targetBranch.CurrentHull.WaterPercentage >= 95f)
                {
                    filled = true;
                    timer += 10f;
                }
            }

            timer -= deltaTime;
        }
    }
}