using Barotrauma.Tutorials;

namespace Barotrauma
{
    class TutorialMode : GameMode
    {
        public Tutorial Tutorial;
        
        public static void StartTutorial(Tutorial tutorial)
        {     
            tutorial.Initialize();
        }

        public TutorialMode(GameModePreset preset, object param)
            : base(preset, param)
        {
        }

        public override void Start()
        {
            base.Start();
            GameMain.GameSession.CrewManager = new CrewManager(true);
            Tutorial.Start();
        }

        public override void AddToGUIUpdateList()
        {
            base.AddToGUIUpdateList();
            Tutorial.AddToGUIUpdateList();
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            Tutorial.Update(deltaTime);
        }
    }
}
