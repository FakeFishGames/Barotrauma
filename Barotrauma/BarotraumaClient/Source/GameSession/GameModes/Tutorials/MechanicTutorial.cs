using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma.Tutorials
{
    class MechanicTutorial : ScenarioTutorial
    {
        // Other tutorial items
        private Door tutorial_securityFinalDoor;
        private LightComponent tutorial_securityFinalDoorLight;
        private Door tutorial_upperFinalDoor;

        // Room 1
        private float shakeTimer = 3.0f;
        private float shakeAmount = 20f;
        private Item mechanic_firstButton;
        private Door mechanic_firstDoor;
        private LightComponent mechanic_firstDoorLight;

        // Room 2
        private MotionSensor mechanic_equipmentObjectiveSensor;
        private ItemContainer mechanic_equipmentCabinet;
        private Door mechanic_secondDoor;
        private LightComponent mechanic_secondDoorLight;

        // Room 3
        private MotionSensor mechanic_weldingObjectiveSensor;
        private Pump mechanic_workingPump;
        private Door mechanic_thirdDoor;
        private LightComponent mechanic_thirdDoorLight;
        private Structure mechanic_brokenWall_1;
        private Hull mechanic_brokenhull_1;

        // Room 4
        private MotionSensor mechanic_craftingObjectiveSensor;
        private Deconstructor mechanic_deconstructor;
        private Fabricator mechanic_fabricator;
        private Door mechanic_fourthDoor;
        private LightComponent mechanic_fourthDoorLight;

        private bool fabricatorInteractedWith = false;
        private bool deconstructorInteractedWith = false;

        // Room 5
        private DummyFireSource mechanic_fire;
        private Door mechanic_fifthDoor;
        private LightComponent mechanic_fifthDoorLight;

        // Room 6
        private MotionSensor mechanic_divingSuitObjectiveSensor;
        private ItemContainer mechanic_divingSuitContainer;
        private Door mechanic_sixthDoor;
        private LightComponent mechanic_sixthDoorLight;

        // Room 7
        private Pump mechanic_brokenPump;
        private Structure mechanic_brokenWall_2;
        private Hull mechanic_brokenhull_2;
        private Door tutorial_submarineDoor;
        private LightComponent tutorial_submarineDoorLight;

        // Submarine
        private MotionSensor mechanic_enteredSubmarineSensor;
        private Engine mechanic_submarineEngine;
        private Pump mechanic_ballastPump_1;
        private Pump mechanic_ballastPump_2;

        // Colors
        private Color highlightColor = Color.OrangeRed;
        private Color inaccessibleColor = Color.Red;
        private Color accessibleColor = Color.Green;

        public MechanicTutorial(XElement element) : base(element)
        {

        }

        public override void Start()
        {
            base.Start();

            // Other tutorial items
            tutorial_securityFinalDoor = Item.ItemList.Find(i => i.HasTag("tutorial_securityfinaldoor")).GetComponent<Door>();
            tutorial_securityFinalDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_securityfinaldoorlight")).GetComponent<LightComponent>();
            tutorial_upperFinalDoor = Item.ItemList.Find(i => i.HasTag("tutorial_upperfinaldoor")).GetComponent<Door>();          

            SetDoorAccess(tutorial_securityFinalDoor, tutorial_securityFinalDoorLight, false);
            SetDoorAccess(tutorial_upperFinalDoor, null, false);

            // Room 1
            mechanic_firstButton = Item.ItemList.Find(i => i.HasTag("mechanic_firstbutton"));
            mechanic_firstButton.SpriteColor = accessibleColor;
            mechanic_firstButton.ExternalHighlight = true;
            mechanic_firstDoor = Item.ItemList.Find(i => i.HasTag("mechanic_firstdoor")).GetComponent<Door>();
            mechanic_firstDoorLight = Item.ItemList.Find(i => i.HasTag("mechanic_firstdoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(mechanic_firstDoor, mechanic_firstDoorLight, false);

            // Room 2
            mechanic_equipmentObjectiveSensor = Item.ItemList.Find(i => i.HasTag("mechanic_equipmentobjectivesensor")).GetComponent<MotionSensor>();
            mechanic_equipmentCabinet = Item.ItemList.Find(i => i.HasTag("mechanic_equipmentcabinet")).GetComponent<ItemContainer>();
            mechanic_secondDoor = Item.ItemList.Find(i => i.HasTag("mechanic_seconddoor")).GetComponent<Door>();
            mechanic_secondDoorLight = Item.ItemList.Find(i => i.HasTag("mechanic_seconddoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(mechanic_secondDoor, mechanic_secondDoorLight, false);

            // Room 3
            mechanic_weldingObjectiveSensor = Item.ItemList.Find(i => i.HasTag("mechanic_weldingobjectivesensor")).GetComponent<MotionSensor>();
            mechanic_workingPump = Item.ItemList.Find(i => i.HasTag("mechanic_workingpump")).GetComponent<Pump>();
            mechanic_thirdDoor = Item.ItemList.Find(i => i.HasTag("mechanic_thirddoor")).GetComponent<Door>();
            mechanic_thirdDoorLight = Item.ItemList.Find(i => i.HasTag("mechanic_thirddoorlight")).GetComponent<LightComponent>();
            mechanic_brokenWall_1 = Structure.WallList.Find(i => i.SpecialTag == "mechanic_brokenwall_1");

            SetDoorAccess(mechanic_thirdDoor, mechanic_thirdDoorLight, false);
            mechanic_brokenWall_1.Indestructible = false;
            mechanic_brokenWall_1.SpriteColor = Color.White;
            for (int i = 0; i < mechanic_brokenWall_1.SectionCount; i++)
            {
                mechanic_brokenWall_1.AddDamage(i, 50);
            }
            mechanic_brokenhull_1 = mechanic_brokenWall_1.Sections[0].gap.FlowTargetHull;

            // Room 4
            mechanic_craftingObjectiveSensor = Item.ItemList.Find(i => i.HasTag("mechanic_craftingobjectivesensor")).GetComponent<MotionSensor>();
            mechanic_deconstructor = Item.ItemList.Find(i => i.HasTag("deconstructor")).GetComponent<Deconstructor>();
            mechanic_fabricator = Item.ItemList.Find(i => i.HasTag("fabricator")).GetComponent<Fabricator>();
            mechanic_fourthDoor = Item.ItemList.Find(i => i.HasTag("mechanic_fourthdoor")).GetComponent<Door>();
            mechanic_fourthDoorLight = Item.ItemList.Find(i => i.HasTag("mechanic_fourthdoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(mechanic_fourthDoor, mechanic_fourthDoorLight, false);

            // Room 5
            mechanic_fire = new DummyFireSource(new Vector2(20f, 2f), Item.ItemList.Find(i => i.HasTag("mechanic_fire")).WorldPosition);
            mechanic_fifthDoor = Item.ItemList.Find(i => i.HasTag("mechanic_fifthdoor")).GetComponent<Door>();
            mechanic_fifthDoorLight = Item.ItemList.Find(i => i.HasTag("mechanic_fifthdoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(mechanic_fifthDoor, mechanic_fifthDoorLight, false);

            // Room 6
            mechanic_divingSuitObjectiveSensor = Item.ItemList.Find(i => i.HasTag("mechanic_divingsuitobjectivesensor")).GetComponent<MotionSensor>();
            mechanic_divingSuitContainer = Item.ItemList.Find(i => i.HasTag("mechanic_divingsuitcontainer")).GetComponent<ItemContainer>();
            mechanic_sixthDoor = Item.ItemList.Find(i => i.HasTag("mechanic_sixthdoor")).GetComponent<Door>();
            mechanic_sixthDoorLight = Item.ItemList.Find(i => i.HasTag("mechanic_sixthdoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(mechanic_sixthDoor, mechanic_sixthDoorLight, false);

            // Room 7
            mechanic_brokenPump = Item.ItemList.Find(i => i.HasTag("mechanic_brokenpump")).GetComponent<Pump>();
            mechanic_brokenPump.Item.Indestructible = false;
            mechanic_brokenPump.Item.Condition = 0;
            mechanic_brokenWall_2 = Structure.WallList.Find(i => i.SpecialTag == "mechanic_brokenwall_2");
            tutorial_submarineDoor = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoor")).GetComponent<Door>();
            tutorial_submarineDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoorlight")).GetComponent<LightComponent>();

            mechanic_brokenWall_2.Indestructible = false;
            mechanic_brokenWall_2.SpriteColor = Color.White;
            for (int i = 0; i < mechanic_brokenWall_2.SectionCount; i++)
            {
                mechanic_brokenWall_2.AddDamage(i, 500);
            }
            mechanic_brokenhull_2 = mechanic_brokenWall_2.Sections[0].gap.FlowTargetHull;
            SetDoorAccess(tutorial_submarineDoor, tutorial_submarineDoorLight, false);

            return;
            // Submarine
            mechanic_enteredSubmarineSensor = Item.ItemList.Find(i => i.HasTag("mechanic_enteredsubmarinesensor")).GetComponent<MotionSensor>();
            mechanic_submarineEngine = Item.ItemList.Find(i => i.HasTag("mechanic_submarineengine")).GetComponent<Engine>();
            mechanic_submarineEngine.Item.Indestructible = false;
            mechanic_submarineEngine.Item.Condition = 0f;
            mechanic_ballastPump_1 = Item.ItemList.Find(i => i.HasTag("mechanic_ballastpump_1")).GetComponent<Pump>();
            mechanic_ballastPump_1.Item.Indestructible = false;
            mechanic_ballastPump_1.Item.Condition = 0f;
            mechanic_ballastPump_2 = Item.ItemList.Find(i => i.HasTag("mechanic_ballastpump_2")).GetComponent<Pump>();
            mechanic_ballastPump_2.Item.Indestructible = false;
            mechanic_ballastPump_2.Item.Condition = 0f;
        }

        public override IEnumerable<object> UpdateState()
        {
            // Room 1
            while (shakeTimer > 0.0f) // Wake up, shake
            {
                shakeTimer -= 0.1f;
                GameMain.GameScreen.Cam.Shake = shakeAmount;
                yield return new WaitForSeconds(0.1f);
            }

            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage("", TextManager.Get("Mechanic.Radio.WakeUp"), ChatMessageType.Default, null);

            yield return new WaitForSeconds(2.5f);

            TriggerTutorialSegment(0); // Open door objective
            SetDoorAccess(mechanic_firstDoor, mechanic_firstDoorLight, true);
            SetHighlight(mechanic_firstButton, true);
            while (!mechanic_firstDoor.IsOpen) yield return null;
            RemoveCompletedObjective(segments[0]);
            SetHighlight(mechanic_firstButton, false);

            // Room 2
            while (!mechanic_equipmentObjectiveSensor.MotionDetected) yield return null;
            TriggerTutorialSegment(1); // Equipment & inventory objective
            SetHighlight(mechanic_equipmentCabinet.Item, true);
            while (!IsSelectedItem(mechanic_equipmentCabinet.Item)) yield return null;
            SetHighlight(mechanic_equipmentCabinet.Item, false);
            while (!Character.Controlled.HasEquippedItem("divingmask") || !Character.Controlled.HasEquippedItem("weldingtool")) yield return null; // Wait until equipped
            RemoveCompletedObjective(segments[1]);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage("", TextManager.Get("Mechanic.Radio.Equipment"), ChatMessageType.Default, null);
            SetDoorAccess(mechanic_secondDoor, mechanic_secondDoorLight, true);

            // Room 3
            while (!mechanic_weldingObjectiveSensor.MotionDetected) yield return null;
            TriggerTutorialSegment(2); // Welding objective
            SetHighlight(mechanic_brokenWall_1, true);
            while (WallHasDamagedSections(mechanic_brokenWall_1)) yield return null; // Highlight until repaired
            RemoveCompletedObjective(segments[2]);
            SetHighlight(mechanic_brokenWall_1, false);
            yield return new WaitForSeconds(1f);
            TriggerTutorialSegment(3); // Pump objective
            SetHighlight(mechanic_workingPump.Item, true);
            while (mechanic_workingPump.FlowPercentage >= 0) yield return null; // Highlight until draining
            SetHighlight(mechanic_workingPump.Item, false);
            while (mechanic_brokenhull_1.WaterPercentage > 0) yield return null; // Unlock door once drained
            RemoveCompletedObjective(segments[3]);
            SetDoorAccess(mechanic_thirdDoor, mechanic_thirdDoorLight, true);

            // Room 4
            while (!mechanic_thirdDoor.IsOpen) yield return null;
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage("", TextManager.Get("Mechanic.Radio.News"), ChatMessageType.Default, null);
            yield return new WaitForSeconds(1f);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage("", TextManager.Get("Mechanic.Radio.Fire"), ChatMessageType.Default, null);
            while (!mechanic_craftingObjectiveSensor.MotionDetected) yield return null;
            TriggerTutorialSegment(4); // Deconstruct
            SetHighlight(mechanic_deconstructor.Item, true);
            while (Character.Controlled.Inventory.FindItemByIdentifier("aluminium") == null) yield return null; // Wait until deconstructed
            SetHighlight(mechanic_deconstructor.Item, false);
            RemoveCompletedObjective(segments[4]);
            yield return new WaitForSeconds(1f);
            TriggerTutorialSegment(5); // Fabricate
            SetHighlight(mechanic_fabricator.Item, true);
            while (Character.Controlled.Inventory.FindItemByIdentifier("extinguisher") == null) yield return null; // Wait until extinguisher is created
            RemoveCompletedObjective(segments[5]);
            SetHighlight(mechanic_deconstructor.Item, false);
            SetDoorAccess(mechanic_fourthDoor, mechanic_fourthDoorLight, true);

            // Room 5
            while (!mechanic_fourthDoor.IsOpen) yield return null;
            TriggerTutorialSegment(6); // Using the extinguisher
            while (!mechanic_fire.Removed) yield return null; // Wait until extinguished
            RemoveCompletedObjective(segments[6]);
            SetDoorAccess(mechanic_fifthDoor, mechanic_fifthDoorLight, true);

            // Room 6
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage("", TextManager.Get("Mechanic.Radio.Diving"), ChatMessageType.Default, null);
            while (!mechanic_divingSuitObjectiveSensor.MotionDetected) yield return null;
            TriggerTutorialSegment(7); // Dangers of pressure, equip diving suit objective
            SetHighlight(mechanic_divingSuitContainer.Item, true);
            while (!IsSelectedItem(mechanic_divingSuitContainer.Item)) yield return null;
            SetHighlight(mechanic_divingSuitContainer.Item, false);
            while (!Character.Controlled.HasEquippedItem("divingsuit")) yield return null;
            RemoveCompletedObjective(segments[7]);
            SetDoorAccess(mechanic_sixthDoor, mechanic_sixthDoorLight, true);

            // Room 7
            SetHighlight(mechanic_brokenWall_2, true);
            while (WallHasDamagedSections(mechanic_brokenWall_2)) yield return null;
            SetHighlight(mechanic_brokenWall_2, false);
            TriggerTutorialSegment(8); // Repairing machinery (pump)
            SetHighlight(mechanic_brokenPump.Item, true);
            while (!mechanic_brokenPump.Item.IsFullCondition && mechanic_brokenPump.FlowPercentage >= 0) yield return null;
            RemoveCompletedObjective(segments[8]);
            SetHighlight(mechanic_brokenPump.Item, false);
            while (mechanic_brokenhull_2.WaterPercentage > 0) yield return null;
            SetDoorAccess(tutorial_submarineDoor, tutorial_submarineDoorLight, true);


            while (true) yield return null;
            // Submarine
            while (!mechanic_enteredSubmarineSensor.MotionDetected) yield return null;
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage("", TextManager.Get("Mechanic.Radio.Submarine"), ChatMessageType.Default, null);
            TriggerTutorialSegment(9); // Repairing ballast pumps, engine
            SetHighlight(mechanic_ballastPump_1.Item, true);
            SetHighlight(mechanic_ballastPump_2.Item, true);
            SetHighlight(mechanic_submarineEngine.Item, true);

            while (!mechanic_ballastPump_1.Item.IsFullCondition || !mechanic_ballastPump_2.Item.IsFullCondition || !mechanic_submarineEngine.Item.IsFullCondition)
            {
                // Remove highlights when each individual machine is repaired
                if (mechanic_ballastPump_1.Item.IsFullCondition && mechanic_ballastPump_1.Item.ExternalHighlight) SetHighlight(mechanic_ballastPump_1.Item, false);
                if (mechanic_ballastPump_2.Item.IsFullCondition && mechanic_ballastPump_2.Item.ExternalHighlight) SetHighlight(mechanic_ballastPump_2.Item, false);
                if (mechanic_submarineEngine.Item.IsFullCondition && mechanic_submarineEngine.Item.ExternalHighlight) SetHighlight(mechanic_submarineEngine.Item, false);
                yield return null;
            }

            RemoveCompletedObjective(segments[9]);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage("", TextManager.Get("Mechanic.Radio.Complete"), ChatMessageType.Default, null);

            // END TUTORIAL
            Completed = true;
        }

        private void SetHighlight(Item item, bool state)
        {
            item.SpriteColor = (state) ? highlightColor : Color.White;
            item.ExternalHighlight = state;
        }

        private void SetHighlight(Structure structure, bool state)
        {
            structure.SpriteColor = (state) ? highlightColor : Color.White;
            structure.ExternalHighlight = state;
        }

        private void SetDoorAccess(Door door, LightComponent light, bool state)
        {
            if (door != null) door.Stuck = (state) ? 0f : 100f;
            if (light != null) light.LightColor = (state) ? accessibleColor : inaccessibleColor;
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
