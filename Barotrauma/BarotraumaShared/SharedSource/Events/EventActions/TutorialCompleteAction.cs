namespace Barotrauma
{
    class TutorialCompleteAction : EventAction
    {
        private bool isFinished;

        public TutorialCompleteAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

#if CLIENT
            if (GameMain.GameSession?.GameMode is TutorialMode tutorialMode)
            {
                tutorialMode.Tutorial?.Complete();
            }
#endif
            isFinished = true;
        }

        public override bool IsFinished(ref string goToLabel)
        {
            return isFinished;
        }

        public override void Reset()
        {
            isFinished = false;
        }
    }
}