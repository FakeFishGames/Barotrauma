namespace Barotrauma
{
    abstract class ShipGlobalIssue
    {
        public float GlobalImportance { get; set; }

        protected ShipCommandManager shipCommandManager;
        public ShipGlobalIssue(ShipCommandManager shipCommandManager)
        {
            this.shipCommandManager = shipCommandManager;
        }
        public abstract void CalculateGlobalIssue();
    }

    abstract class ShipIssueWorkerGlobal : ShipIssueWorker
    {
        private readonly ShipGlobalIssue shipGlobalIssue;

        public ShipIssueWorkerGlobal(ShipCommandManager shipCommandManager, Order suggestedOrderPrefab, ShipGlobalIssue shipGlobalIssue) : base (shipCommandManager, suggestedOrderPrefab)
        {
            this.shipGlobalIssue = shipGlobalIssue;
        }

        public override void CalculateImportanceSpecific() // importances for global issues are precalculated, so that they don't need to be calculated per each attending character
        {
            Importance = shipGlobalIssue.GlobalImportance;
        }
    }
}
