using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.Tutorials
{
    class MechanicTutorial : ScenarioTutorial
    {
        //private MotionSensor mechanicDoorUse;
        //private Item mechanicDoorButton;
        private Structure mechanic_brokenhull_1;
        private Structure mechanic_brokenhull_2;
        private DummyFireSource mechanic_fire;

        public MechanicTutorial(XElement element) : base(element)
        {

        }

        public override void Start()
        {
            base.Start();
            //mechanicDoorUse = Item.ItemList.Find(i => i.HasTag("mechanic_buttonuse"))?.GetComponent<MotionSensor>();
            //mechanicDoorButton = Item.ItemList.Find(i => i.HasTag("mechanic_button"));
            //mechanicDoorButton.SpriteColor = Color.Orange;

            mechanic_brokenhull_1 = Structure.WallList.Find(i => i.SpecialTag == "mechanic_brokenhull_1");
            mechanic_brokenhull_2 = Structure.WallList.Find(i => i.SpecialTag == "mechanic_brokenhull_2");
            mechanic_fire = new DummyFireSource(new Vector2(20f, 10f), Item.ItemList.Find(i => i.HasTag("mechanic_fire")).WorldPosition);

            mechanic_brokenhull_1.Indestructible = false;
            for (int i = 0; i < mechanic_brokenhull_1.SectionCount; i++)
            {
                mechanic_brokenhull_1.AddDamage(i, 100000);
            }

            mechanic_brokenhull_2.Indestructible = false;
            for (int i = 0; i < mechanic_brokenhull_2.SectionCount; i++)
            {
                mechanic_brokenhull_2.AddDamage(i, 100000);
            }
        }

        public override IEnumerable<object> UpdateState()
        {
            yield return new WaitForSeconds(1f);
            infoBox = CreateInfoFrame("", "mechanic_button", hasButton: true);
        }
    }
}
