using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class ShipIssueWorkerOperateWeapons : ShipIssueWorkerItem
    {
        public override float RedundantIssueModifier => 0.65f;
        private readonly List<float> targetingImportances = new List<float>();

        public override bool AllowEasySwitching => true;

        public ShipIssueWorkerOperateWeapons(ShipCommandManager shipCommandManager, Order order) : base(shipCommandManager, order) { }

        float GetTargetingImportance(Entity entity)
        {
            float currentDistanceToEnemy = Vector2.Distance(entity.WorldPosition, TargetItem.WorldPosition);

            float importance = MathHelper.Clamp(100 - (currentDistanceToEnemy / 100f), MinImportance, MaxImportance * 0.5f);
            if (TargetItem.Submarine != null)
            {
                Vector2 dir = entity.WorldPosition - TargetItem.WorldPosition;
                Vector2 submarineDir = TargetItem.WorldPosition - TargetItem.Submarine.WorldPosition;
                if (Vector2.Dot(dir, submarineDir) < 0)
                {
                    //direction from the weapon to the target is opposite to the direction from the sub to the weapon
                    // = the turret is most likely on the wrong side of the sub, reduce importance
                    importance *= 0.1f;
                }
            }
            return importance;
        }

        public override void CalculateImportanceSpecific()
        {
            if (TargetItemComponent is Turret turret && !turret.HasPowerToShoot())
            {
                //operate (= recharge the turrets) with low priority if they're out of power
                //if something else (issues with reactor or the electrical grid) is preventing them from being charged, fixing those issues should take priority
                Importance = ShipCommandManager.MinimumIssueThreshold * 1.05f;
                return;
            }

            targetingImportances.Clear();
            foreach (Character character in shipCommandManager.EnemyCharacters)
            {
                targetingImportances.Add(GetTargetingImportance(character));
            } 
            // there should maybe be additional logic for targeting and destroying spires, because they currently cause some issues with pathing

            if (targetingImportances.Any(i => i > 0))
            {
                targetingImportances.Sort();
                Importance = targetingImportances.TakeLast(3).Sum();
            }
        }
    }
}
