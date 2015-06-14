
namespace Subsurface
{
    class Task
    {
      
        protected string name;

        private float priority;

        protected string musicType;

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

        public string MusicType
        {
            get { return musicType; }
        }

        public bool IsFinished
        {
            get { return isFinished; }
        }

        public Task(float priority, string name)
        {
            if (Game1.GameSession==null || Game1.GameSession.taskManager == null) return;

            taskManager = Game1.GameSession.taskManager;
            musicType = "repair";
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
