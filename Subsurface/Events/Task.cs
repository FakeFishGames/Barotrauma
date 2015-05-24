
namespace Subsurface
{
    class Task
    {
      
        protected string name;

        private float priority;

        protected TaskManager taskManager;

        protected bool isFinished;

        public string Name
        {
            get { return name; }
        }

        public float Priority
        {
            get { return priority; }
        }

        public bool IsFinished
        {
            get { return isFinished; }
        }

        public Task(TaskManager taskManager, float priority, string name)
        {
            this.taskManager = taskManager;
            this.priority = priority;
            this.name = name;

            taskManager.AddTask(this);
        }

        public virtual void Update(float deltaTime)
        {

        }

        protected virtual void Finished()
        {
            taskManager.TaskFinished(this);
            isFinished = true;
        }
    }
}
