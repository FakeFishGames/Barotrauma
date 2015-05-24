namespace Subsurface
{
    class ScriptedTask : Task
    {
        private ScriptedEvent scriptedEvent;

        private bool prevStarted;

        public ScriptedTask(TaskManager taskManager, ScriptedEvent scriptedEvent)
            : base(taskManager, scriptedEvent.Difficulty, scriptedEvent.Name)
        {
            this.scriptedEvent = scriptedEvent;
            scriptedEvent.Init();
        }

        public override void Update(float deltaTime)
        {
            if (prevStarted == false && scriptedEvent.IsStarted)
            {
                taskManager.TaskStarted(this);
                prevStarted = true;
            }

            scriptedEvent.Update(deltaTime);
            if (scriptedEvent.IsFinished) Finished();
        }
    }
}
