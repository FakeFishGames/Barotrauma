using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;

namespace Barotrauma.Tutorials
{
    class MechanicTutorial : ScenarioTutorial
    {
        // Room 1
        private Item firstButton;
        
        // Room 2
        private MotionSensor equipmentVideoSensor;
        private Item doorButtonBeforeEquipmentOn;

        // Room 3
        private MotionSensor weldingVideoSensor;
        private Item doorButtonBeforeWaterDrained;
        private Structure mechanic_brokenwall_1;
        private Hull mechanic_brokenhull_1;
        private Structure mechanic_brokenwall_2;
        private Hull mechanic_brokenhull_2;

        // Room 4
        private MotionSensor deconstructorVideoSensor;
        private MotionSensor fabricatorVideoSensor;

        // Colors
        private Color inaccessibleButtonColor = Color.Red;
        private Color accessibleButtonColor = Color.Green;

        public MechanicTutorial(XElement element) : base(element)
        {

        }

        public override void Start()
        {
            base.Start();

            firstButton.SpriteColor = accessibleButtonColor;

            equipmentVideoSensor = Item.ItemList.Find(i => i.HasTag("mechanic_equipment_video"))?.GetComponent<MotionSensor>();
            doorButtonBeforeEquipmentOn = Item.ItemList.Find(i => i.HasTag("mechanic_equipment_button"));
            doorButtonBeforeEquipmentOn.SpriteColor = inaccessibleButtonColor;
            doorButtonBeforeWaterDrained.SpriteColor = inaccessibleButtonColor;

            mechanic_brokenwall_1 = Structure.WallList.Find(i => i.SpecialTag == "mechanic_brokenwall_1");
            mechanic_brokenwall_2 = Structure.WallList.Find(i => i.SpecialTag == "mechanic_brokenwall_2");
            //mechanic_fire = new DummyFireSource(new Vector2(20f, 10f), Item.ItemList.Find(i => i.HasTag("mechanic_fire")).WorldPosition);

            mechanic_brokenwall_1.Indestructible = false;
            for (int i = 0; i < mechanic_brokenwall_1.SectionCount; i++)
            {
                mechanic_brokenwall_1.AddDamage(i, 100);
            }

            mechanic_brokenhull_1 = mechanic_brokenwall_1.Sections[0].gap.FlowTargetHull;

            mechanic_brokenwall_2.Indestructible = false;
            for (int i = 0; i < mechanic_brokenwall_2.SectionCount; i++)
            {
                mechanic_brokenwall_2.AddDamage(i, 100);
            }

            mechanic_brokenhull_2 = mechanic_brokenwall_2.Sections[0].gap.FlowTargetHull;
        }

        public override IEnumerable<object> UpdateState()
        {
            // Room 1
            // Wake up, shake
            // RADIO: Concerned crewmember telling player to get moving and get to the sub, a Moloch 
            // Remove first button highlight after interaction

            // Room 2
            while (!equipmentVideoSensor.MotionDetected) yield return null;
            TriggerTutorialSegment(1); // Equipment & inventory video
            // Highlight cabinet
            // Remove cabinet highlight after interact
            while (!Character.Controlled.HasEquippedItem("divingmask") || !Character.Controlled.HasEquippedItem("weldingtool")) yield return null; // Wait until equipped
            // RADIO: crewmember explaining that the player needs to repair the walls along the way
            doorButtonBeforeEquipmentOn.SpriteColor = accessibleButtonColor; // Unlock door
            // Remove button highlight after interact

            // Room 3
            while (!weldingVideoSensor.MotionDetected) yield return null;
            TriggerTutorialSegment(2); // Welding video
            // Highlight damaged wall
            while (WallHasDamagedSections(mechanic_brokenwall_1)) yield return null;
            // Remove damaged wall highlight
            TriggerTutorialSegment(3); // Pump video
            // Highlight pump until draining
            while (mechanic_brokenhull_1.WaterPercentage > 0) yield return null;
            doorButtonBeforeWaterDrained.SpriteColor = accessibleButtonColor;

            // Room 4

        }

        private bool WallHasDamagedSections(Structure wall)
        {
            for (int i = 0; i < wall.SectionCount; i++)
            {
                if (wall.Sections[i].damage > 0) return true;
            }

            return false;
        }
    }
}
