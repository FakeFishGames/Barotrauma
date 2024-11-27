﻿using Barotrauma.Items.Components;

namespace Barotrauma
{
    class ShipIssueWorkerSteer : ShipIssueWorkerItem
    {
        // The AI could be set to steer automatically through a specialized job or autonomous objectives
        // but the logic involved doesn't really allow that without some annoyingly specific changes
        // hence the AI will command itself to steer if steering is not being taken care of or the target location is wrong
        public ShipIssueWorkerSteer(ShipCommandManager shipCommandManager, Order order) : base(shipCommandManager, order) { }
        public override void CalculateImportanceSpecific()
        {
            if (shipCommandManager.NavigationState == ShipCommandManager.NavigationStates.Inactive) { return; }
            if (TargetItemComponent is Powered { HasPower: false }) { return; }
            if (TargetItem.Condition <= 0f) { return; }

            Importance = 70f;
        }
    }
}
