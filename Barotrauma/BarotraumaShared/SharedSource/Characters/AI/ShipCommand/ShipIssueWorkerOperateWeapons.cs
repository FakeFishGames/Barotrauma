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

        public ShipIssueWorkerOperateWeapons(ShipCommandManager shipCommandManager, Order order, Item targetItem, ItemComponent targetItemComponent) : base(shipCommandManager, order, targetItem, targetItemComponent) { }

        float GetTargetingImportance(Entity entity)
        {
            float currentDistanceToEnemy = Vector2.Distance(entity.WorldPosition, TargetItem.WorldPosition);
            return MathHelper.Clamp(100 - (currentDistanceToEnemy / 100f), MinImportance, MaxImportance);
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
