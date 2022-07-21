using Barotrauma.Items.Components;

namespace Barotrauma
{
    abstract class ShipIssueWorkerItem : ShipIssueWorker
    {
        public ShipIssueWorkerItem(ShipCommandManager shipCommandManager, Order order) : base(shipCommandManager, order) { }

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

            return true;
        }
    }
}
