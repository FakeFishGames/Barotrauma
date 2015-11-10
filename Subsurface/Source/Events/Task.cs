
namespace Barotrauma
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

        public virtual bool IsStarted
        {
            get { return true; }
        }

        public Task(float priority, string name)
        {
            if (GameMain.GameSession==null || GameMain.GameSession.TaskManager == null) return;

            taskManager = GameMain.GameSession.TaskManager;
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
