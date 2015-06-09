namespace Subsurface
{
    class RepairTask : Task
    {
        Item item;

        public RepairTask(Item item, float priority, string name)
            : base(priority, name)
        {
            if (taskManager == null) return;

            this.item = item;

            taskManager.TaskStarted(this);
        }

        public override void Update(float deltaTime)
        {
            if (item.Condition > 50.0f) Finished();
        }
    }
}
