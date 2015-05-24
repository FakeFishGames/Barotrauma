namespace Subsurface
{
    class RepairTask : Task
    {
        Item item;

        public RepairTask(TaskManager taskManager, Item item, float priority, string name)
            : base(taskManager, priority, name)
        {
            this.item = item;

            taskManager.TaskStarted(this);
        }

        public override void Update(float deltaTime)
        {
            if (item.Condition > 50.0f) Finished();
        }
    }
}
