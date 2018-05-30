using Barotrauma.Tutorials;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    class TutorialMode : GameMode
    {
        public Tutorial tutorial;
        
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
            tutorial.Start();
        }

        public override void AddToGUIUpdateList()
        {
            tutorial.AddToGUIUpdateList();
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            tutorial.Update(deltaTime);
        }
    }
}
