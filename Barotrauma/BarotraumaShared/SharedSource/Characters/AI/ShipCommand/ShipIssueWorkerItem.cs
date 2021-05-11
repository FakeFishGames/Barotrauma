using Barotrauma.Items.Components;

namespace Barotrauma
{
    abstract class ShipIssueWorkerItem : ShipIssueWorker
    {
        public ShipIssueWorkerItem(ShipCommandManager shipCommandManager, Order order, Item targetItem, ItemComponent targetItemComponent, string option = null) : base(shipCommandManager, order, option)
        {
            TargetItemComponent = targetItemComponent;
            TargetItem = targetItem;
        }

        protected override bool IsIssueViable()
        {
            if (TargetItemComponent == null)
            {
                DebugConsole.ThrowError("TargetItemComponent was null in " + this);
                return false;
            }

            if (TargetItem == null)
            {
                DebugConsole.ThrowError("TargetItem was null in " + this);
                return false;
            }

            if (TargetItem.IgnoreByAI) { return false; }

            return true;
        }
    }
}
