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

        public TutorialMode(GameModePreset preset)
            : base(preset)
        {
        }

        public override void Start()
        {
            base.Start();
            GameMain.GameSession.CrewManager = new CrewManager(true);
            Tutorial.Start();
            foreach (Item item in Item.ItemList)
            {
                //don't consider the items to belong in the outpost to prevent the stealing icon from showing
                item.SpawnedInOutpost = false;
            }
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
