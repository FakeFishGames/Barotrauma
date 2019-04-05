using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Tutorials
{
    class MechanicTutorial : ScenarioTutorial
    {
        private MotionSensor mechanicDoorUse;

        public MechanicTutorial(XElement element) : base(element)
        {

        }

        public override void Start()
        {
            base.Start();
            mechanicDoorUse = Item.ItemList.Find(i => i.HasTag("mechanic_buttonuse"))?.GetComponent<MotionSensor>();
        }

        public override IEnumerable<object> UpdateState()
        {
            while (!mechanicDoorUse.MotionDetected) yield return null;
            infoBox = CreateInfoFrame("How open door", true);
        }
    }
}
