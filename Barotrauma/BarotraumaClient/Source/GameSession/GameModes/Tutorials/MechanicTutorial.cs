using Barotrauma.Items.Components;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.Tutorials
{
    class MechanicTutorial : ScenarioTutorial
    {
        private MotionSensor mechanicDoorUse;
        private Item mechanicDoorButton;
        private Structure testWall;
        private DummyFireSource testFire;

        public MechanicTutorial(XElement element) : base(element)
        {

        }

        public override void Start()
        {
            base.Start();
            mechanicDoorUse = Item.ItemList.Find(i => i.HasTag("mechanic_buttonuse"))?.GetComponent<MotionSensor>();
            mechanicDoorButton = Item.ItemList.Find(i => i.HasTag("mechanic_button"));
            mechanicDoorButton.SpriteColor = Color.Orange;

            testWall = Structure.WallList.Find(i => i.SpecialTag == "testwall");
        }

        public override IEnumerable<object> UpdateState()
        {
            yield return new WaitForSeconds(1f);

            GameMain.GameScreen.Cam.Shake = 10000;
            testFire = new DummyFireSource(new Vector2(10f, 10f), Item.ItemList.Find(i => i.HasTag("mechanic_fire")).WorldPosition);

            while (!mechanicDoorUse.MotionDetected) yield return null;

            infoBox = CreateInfoFrame("mechanic_button", true);

            testWall.Indestructible = false;
            for (int i = 0; i < testWall.SectionCount; i++)
            {
                testWall.AddDamage(i, 100000);
            }
        }
    }
}
