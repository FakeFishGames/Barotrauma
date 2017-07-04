namespace Barotrauma
{
    class RepairTask : Task
    {
        Item item;

        public RepairTask(Item item, float priority, string name)
            : base(priority, name)
        {
            if (taskManager == null) return;

            this.item = item;
        }

        public override void Update(float deltaTime)
        {
            if (item.Condition > item.Prefab.Health * 0.5f) Finished();
        }
    }
}
