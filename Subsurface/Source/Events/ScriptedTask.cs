namespace Barotrauma
{
    class ScriptedTask : Task
    {
        private ScriptedEvent scriptedEvent;

        public override bool IsStarted
        {
            get
            {
                return scriptedEvent.IsActive;
            }
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
            scriptedEvent.Update(deltaTime);
            if (scriptedEvent.IsFinished) Finished();
        }
    }
}
