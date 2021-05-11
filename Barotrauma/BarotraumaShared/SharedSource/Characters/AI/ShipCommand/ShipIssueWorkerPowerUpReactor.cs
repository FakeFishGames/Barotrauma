using Barotrauma.Items.Components;

namespace Barotrauma
{
    class ShipIssueWorkerPowerUpReactor : ShipIssueWorkerItem
    {
        public ShipIssueWorkerPowerUpReactor(ShipCommandManager shipCommandManager, Order order, Item targetItem, ItemComponent targetItemComponent, string option) : base(shipCommandManager, order, targetItem, targetItemComponent, option)
        {
        }

        public override void CalculateImportanceSpecific()
        {
            if (TargetItem.Condition <= 0f) { return; }

            if (TargetItemComponent is Reactor reactor && -reactor.CurrPowerConsumption < float.Epsilon)
            {
                Importance = 40f;
            }
        }
    }
}
