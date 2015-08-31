namespace Subsurface
{
    class ScriptedTask : Task
    {
        private ScriptedEvent scriptedEvent;

        private bool prevStarted;

        public override bool IsStarted
        {
            get { return scriptedEvent.IsStarted; }
        }

        public ScriptedTask(ScriptedEvent scriptedEvent)
            : base(scriptedEvent.Difficulty, scriptedEvent.Name)
        {
            if (taskManager == null) return;

            this.musicType = scriptedEvent.MusicType;

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
