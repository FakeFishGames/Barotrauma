using Barotrauma.Tutorials;

namespace Barotrauma
{
    class TutorialMode : GameMode
    {
        public Tutorial Tutorial;

        public TutorialMode(GameModePreset preset)
            : base(preset)
        {
        }

        public override void Start()
        {
            base.Start();
            GameMain.GameSession.CrewManager = new CrewManager(true);
            foreach (Item item in Item.ItemList)
            {
                //don't consider the items to belong in the outpost to prevent the stealing icon from showing
                item.SpawnedInCurrentOutpost = false;
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
