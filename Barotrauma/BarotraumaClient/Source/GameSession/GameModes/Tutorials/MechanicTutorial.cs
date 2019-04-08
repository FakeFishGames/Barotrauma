using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;

namespace Barotrauma.Tutorials
{
    class MechanicTutorial : ScenarioTutorial
    {
        // Room 1
        private Item mechanic_firstButton;
        
        // Room 2
        private MotionSensor mechanic_equipmentObjectiveSensor;
        private Item mechanic_doorButtonBeforeEquipmentOn;

        // Room 3
        private MotionSensor mechanic_weldingObjectiveSensor;
        private Item mechanic_doorButtonBeforeWaterDrained;
        private Structure mechanic_brokenWall_1;
        private Hull mechanic_brokenhull_1;

        // Room 4
        private Deconstructor mechanic_deconstructor;
        private Fabricator mechanic_fabricator;
        private Item mechanic_doorButtonBeforeFire;

        private bool fabricatorInteractedWith = false;
        private bool deconstructorInteractedWith = false;

        // Room 5
        private Door mechanic_doorToFire;
        private DummyFireSource mechanic_fire;
        private MotionSensor mechanic_divingSuitObjectiveSensor;
        private Item mechanic_doorButtonBeforePressure;

        // Room 6
        private Pump mechanic_brokenPump;
        private Structure mechanic_brokenWall_2;
        private Hull mechanic_brokenhull_2;

        // Submarine
        private MotionSensor mechanic_enteredSubmarineSensor;
        private Engine mechanic_submarineEngine;
        private Pump mechanic_ballastPump_1;
        private Pump mechanic_ballastPump_2;

        // Colors
        private Color inaccessibleButtonColor = Color.Red;
        private Color accessibleButtonColor = Color.Green;

        public MechanicTutorial(XElement element) : base(element)
        {

        }

        public override void Start()
        {
            base.Start();

            // Room 1
            mechanic_firstButton = Item.ItemList.Find(i => i.HasTag("mechanic_firstButton"));
            mechanic_firstButton.SpriteColor = accessibleButtonColor;

            // Room 2
            mechanic_equipmentObjectiveSensor = Item.ItemList.Find(i => i.HasTag("mechanic_equipmentObjectiveSensor"))?.GetComponent<MotionSensor>();
            mechanic_doorButtonBeforeEquipmentOn = Item.ItemList.Find(i => i.HasTag("mechanic_doorButtonBeforeEquipmentOn"));
            mechanic_doorButtonBeforeEquipmentOn.SpriteColor = inaccessibleButtonColor;

            // Room 3
            mechanic_brokenWall_1 = Structure.WallList.Find(i => i.SpecialTag == "mechanic_brokenWall_1");
            mechanic_brokenWall_2 = Structure.WallList.Find(i => i.SpecialTag == "mechanic_brokenWall_2");
            mechanic_doorButtonBeforeWaterDrained.SpriteColor = inaccessibleButtonColor;

            mechanic_brokenWall_1.Indestructible = false;
            for (int i = 0; i < mechanic_brokenWall_1.SectionCount; i++)
            {
                mechanic_brokenWall_1.AddDamage(i, 100);
            }

            mechanic_brokenhull_1 = mechanic_brokenWall_1.Sections[0].gap.FlowTargetHull;

            // Room 4
            mechanic_deconstructor = Item.ItemList.Find(i => i.HasTag("deconstructor"))?.GetComponent<Deconstructor>();
            mechanic_fabricator = Item.ItemList.Find(i => i.HasTag("fabricator"))?.GetComponent<Fabricator>();
            mechanic_doorButtonBeforeFire.SpriteColor = inaccessibleButtonColor;

            // Room 5
            mechanic_fire = new DummyFireSource(new Vector2(10f, 5f), Item.ItemList.Find(i => i.HasTag("mechanic_fire")).WorldPosition);
            mechanic_doorButtonBeforePressure.SpriteColor = inaccessibleButtonColor;

            // Room 6
            mechanic_brokenWall_2.Indestructible = false;
            for (int i = 0; i < mechanic_brokenWall_2.SectionCount; i++)
            {
                mechanic_brokenWall_2.AddDamage(i, 100);
            }

            mechanic_brokenhull_2 = mechanic_brokenWall_2.Sections[0].gap.FlowTargetHull;
            mechanic_brokenPump = Item.ItemList.Find(i => i.HasTag("mechanic_brokenPump"))?.GetComponent<Pump>();
        }

        public override IEnumerable<object> UpdateState()
        {
            // Room 1
            // Wake up, shake
            // RADIO: Concerned crewmember telling player to get moving and get to the sub, a Moloch 
            // Open door objective
            // Remove first button highlight after interaction

            // Room 2
            while (!mechanic_equipmentObjectiveSensor.MotionDetected) yield return null;
            TriggerTutorialSegment(1); // Equipment & inventory objective
            // Highlight cabinet
            // Remove cabinet highlight after interact
            while (!Character.Controlled.HasEquippedItem("divingmask") || !Character.Controlled.HasEquippedItem("weldingtool")) yield return null; // Wait until equipped
            // RADIO: crewmember explaining that the player needs to repair the walls along the way
            mechanic_doorButtonBeforeEquipmentOn.SpriteColor = accessibleButtonColor; // Unlock door
            // Remove button highlight after interact

            // Room 3
            while (!mechanic_weldingObjectiveSensor.MotionDetected) yield return null;
            TriggerTutorialSegment(2); // Welding objective
            // Highlight damaged wall
            while (WallHasDamagedSections(mechanic_brokenWall_1)) yield return null;
            // Remove damaged wall highlight
            TriggerTutorialSegment(3); // Pump objective
            // Highlight pump until draining
            while (mechanic_brokenhull_1.WaterPercentage > 0) yield return null;
            mechanic_doorButtonBeforeWaterDrained.SpriteColor = accessibleButtonColor;

            // Room 4

            while (!deconstructorInteractedWith && !fabricatorInteractedWith) // Wait until interacted with both
            {
                if (!deconstructorInteractedWith)
                {
                    if (IsSelectedItem(mechanic_deconstructor.Item))
                    {
                        deconstructorInteractedWith = true;
                        TriggerTutorialSegment(4); // Deconstructor objective
                    }
                }

                if (!fabricatorInteractedWith)
                {
                    if (IsSelectedItem(mechanic_fabricator.Item))
                    {
                        fabricatorInteractedWith = true;
                        TriggerTutorialSegment(5); // Fabricator objective
                    }
                }

                yield return null;
            }

            // RADIO: crewmember explains you must fabricate a fire extinguisher using sodium and aluminum from a deconstructed oxygen tank
            while (!Character.Controlled.HasEquippedItem("extinguisher")) yield return null; // Wait until equipped
            mechanic_doorButtonBeforeFire.SpriteColor = accessibleButtonColor;

            // Room 5
            while (!mechanic_doorToFire.IsOpen) yield return null;
            TriggerTutorialSegment(6); // Using the extinguisher
            while (mechanic_fire != null) yield return null; // Wait until extinguished
            // RADIO: crewmember warns of high pressure in next room
            while (!mechanic_divingSuitObjectiveSensor.MotionDetected) yield return null;
            TriggerTutorialSegment(7); // Danges of pressure, equip diving suit objective
            while (!Character.Controlled.HasEquippedItem("divingsuit")) yield return null;
            mechanic_doorButtonBeforePressure.SpriteColor = accessibleButtonColor;

            // Room 6
            // Higlight damaged hull
            while (WallHasDamagedSections(mechanic_brokenWall_2)) yield return null;
            TriggerTutorialSegment(8); // Repairing machinery (pump)
            // Highlight pump
            while (!mechanic_brokenPump.Item.IsFullCondition && !mechanic_brokenPump.IsActive) yield return null;
            // Disable highlight
            while (mechanic_brokenhull_2.WaterPercentage > 0) yield return null;
            // Open hatch

            // Submarine
            while (!mechanic_enteredSubmarineSensor.MotionDetected) yield return null;
            // RADIO: crewmember prompts player to repair ballast pumps and engine
            while (!mechanic_ballastPump_1.Item.IsFullCondition || !mechanic_ballastPump_2.Item.IsFullCondition || !mechanic_submarineEngine.Item.IsFullCondition)
            {
                // Remove highlights when each individual machine is repaired
                yield return null;
            }            

            // END TUTORIAL
        }

        private bool IsSelectedItem(Item item)
        {
            return Character.Controlled?.SelectedConstruction == item;
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
