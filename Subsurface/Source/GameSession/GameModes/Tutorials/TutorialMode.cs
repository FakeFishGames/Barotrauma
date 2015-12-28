using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Barotrauma.Tutorials;

namespace Barotrauma
{
    class TutorialMode : GameMode
    {
        public TutorialType tutorialType;
        
        public static void StartTutorial(TutorialType tutorialType)
        {
            Submarine.Load("Content/Map/TutorialSub.sub", "");


            tutorialType.Initialize();
        }

        public TutorialMode(GameModePreset preset)
            : base(preset)
        {
        }

        public override void Start()
        {
            base.Start();

            tutorialType.Start();

        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            tutorialType.Update(deltaTime);

        }


        public override void Draw(SpriteBatch spriteBatch)
        {
            base.Draw(spriteBatch);

            //CrewManager.Draw(spriteBatch);
            tutorialType.Draw(spriteBatch);
        }

    }
}
