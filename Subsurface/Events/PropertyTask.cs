namespace Subsurface
{
    class PropertyTask : Task
    {
        Item item;

        
        public delegate bool IsFinishedHandler();
        private IsFinishedHandler IsFinishedChecker;

        public PropertyTask(TaskManager taskManager, Item item, IsFinishedHandler isFinished, float priority, string name)
            : base(taskManager, priority, name)
        {
            this.item = item;
            IsFinishedChecker = isFinished;

            taskManager.TaskStarted(this);
        }

        public override void Update(float deltaTime)
        {
            if (IsFinishedChecker())
            {
                Finished();
            }
        }
    }
}
