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

        // Room 5
        private MotionSensor mechanic_fireSensor;
        private DummyFireSource mechanic_fire;
        private Door mechanic_fifthDoor;
        private LightComponent mechanic_fifthDoorLight;

        // Room 6
        private MotionSensor mechanic_divingSuitObjectiveSensor;
        private ItemContainer mechanic_divingSuitContainer;
        private ItemContainer mechanic_oxygenContainer;
        private Door mechanic_sixthDoor;
        private LightComponent mechanic_sixthDoorLight;

        // Room 7
        private Pump mechanic_brokenPump;
        private Structure mechanic_brokenWall_2;
        private Hull mechanic_brokenhull_2;
        private Door tutorial_submarineDoor;
        private LightComponent tutorial_submarineDoorLight;

        // Submarine
        private MotionSensor tutorial_enteredSubmarineSensor;
        private Engine mechanic_submarineEngine;
        private Pump mechanic_ballastPump_1;
        private Pump mechanic_ballastPump_2;

        // Variables
        private const float waterVolumeBeforeOpening = 15f;
        private string radioSpeakerName;
        private Character mechanic;

        public MechanicTutorial(XElement element) : base(element)
        {

        }

        public override void Start()
        {
            base.Start();

            radioSpeakerName = TextManager.Get("Tutorial.Radio.Speaker");
            mechanic = Character.Controlled;

            var toolbox = mechanic.Inventory.FindItemByIdentifier("toolbox");
            mechanic.Inventory.RemoveItem(toolbox);

            var crowbar = mechanic.Inventory.FindItemByIdentifier("crowbar");
            mechanic.Inventory.RemoveItem(crowbar);

            // Other tutorial items
            tutorial_securityFinalDoor = Item.ItemList.Find(i => i.HasTag("tutorial_securityfinaldoor")).GetComponent<Door>();
            tutorial_securityFinalDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_securityfinaldoorlight")).GetComponent<LightComponent>();
            tutorial_upperFinalDoor = Item.ItemList.Find(i => i.HasTag("tutorial_upperfinaldoor")).GetComponent<Door>();          

            SetDoorAccess(tutorial_securityFinalDoor, tutorial_securityFinalDoorLight, false);
            SetDoorAccess(tutorial_upperFinalDoor, null, false);

            // Room 1
            mechanic_firstButton = Item.ItemList.Find(i => i.HasTag("mechanic_firstbutton"));
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
                mechanic_brokenWall_1.AddDamage(i, 99);
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
            mechanic_fifthDoor = Item.ItemList.Find(i => i.HasTag("mechanic_fifthdoor")).GetComponent<Door>();
            mechanic_fifthDoorLight = Item.ItemList.Find(i => i.HasTag("mechanic_fifthdoorlight")).GetComponent<LightComponent>();
            mechanic_fireSensor = Item.ItemList.Find(i => i.HasTag("mechanic_firesensor")).GetComponent<MotionSensor>();

            SetDoorAccess(mechanic_fifthDoor, mechanic_fifthDoorLight, false);

            // Room 6
            mechanic_divingSuitObjectiveSensor = Item.ItemList.Find(i => i.HasTag("mechanic_divingsuitobjectivesensor")).GetComponent<MotionSensor>();
            mechanic_divingSuitContainer = Item.ItemList.Find(i => i.HasTag("mechanic_divingsuitcontainer")).GetComponent<ItemContainer>();
            for (int i = 0; i < mechanic_divingSuitContainer.Inventory.Items.Length; i++)
            {
                foreach (ItemComponent ic in mechanic_divingSuitContainer.Inventory.Items[i].Components)
                {
                    ic.CanBePicked = true;
                }                    
            }
            mechanic_oxygenContainer = Item.ItemList.Find(i => i.HasTag("mechanic_oxygencontainer")).GetComponent<ItemContainer>();
            for (int i = 0; i < mechanic_oxygenContainer.Inventory.Items.Length; i++)
            {
                foreach (ItemComponent ic in mechanic_oxygenContainer.Inventory.Items[i].Components)
                {
                    ic.CanBePicked = true;
                }
            }
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
                mechanic_brokenWall_2.AddDamage(i, 250);
            }
            mechanic_brokenhull_2 = mechanic_brokenWall_2.Sections[0].gap.FlowTargetHull;
            SetDoorAccess(tutorial_submarineDoor, tutorial_submarineDoorLight, false);

            return;
            // Submarine
            tutorial_enteredSubmarineSensor = Item.ItemList.Find(i => i.HasTag("tutorial_enteredsubmarinesensor")).GetComponent<MotionSensor>();
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

        public override void Update(float deltaTime)
        {
            mechanic_brokenhull_1.WaterVolume = MathHelper.Clamp(mechanic_brokenhull_1.WaterVolume, 0, mechanic_brokenhull_1.Volume * 0.9f);
            base.Update(deltaTime);
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
            yield return new WaitForSeconds(2.5f);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.WakeUp"), ChatMessageType.Radio, null);

            yield return new WaitForSeconds(2.5f);
            TriggerTutorialSegment(0); // Open door objective
            yield return new WaitForSeconds(1.5f);
            SetDoorAccess(mechanic_firstDoor, mechanic_firstDoorLight, true);
            SetHighlight(mechanic_firstButton, true);
            while (!mechanic_firstDoor.IsOpen) yield return null;
            SetHighlight(mechanic_firstButton, false);
            yield return new WaitForSeconds(1.5f);
            RemoveCompletedObjective(segments[0]);
            
            // Room 2
            yield return new WaitForSeconds(2.5f);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.Equipment"), ChatMessageType.Radio, null);
            while (!mechanic_equipmentObjectiveSensor.MotionDetected) yield return null;
            TriggerTutorialSegment(1); // Equipment & inventory objective
            SetHighlight(mechanic_equipmentCabinet.Item, true);
            while (!IsSelectedItem(mechanic_equipmentCabinet.Item)) yield return null;
            SetHighlight(mechanic_equipmentCabinet.Item, false);
            while (mechanic.Inventory.FindItemByIdentifier("divingmask") == null || mechanic.Inventory.FindItemByIdentifier("weldingtool") == null || mechanic.Inventory.FindItemByIdentifier("wrench") == null) yield return null; // Wait until looted
            yield return new WaitForSeconds(1.5f);
            RemoveCompletedObjective(segments[1]);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.Breach"), ChatMessageType.Radio, null);

            // Room 3
            while (!mechanic_weldingObjectiveSensor.MotionDetected) yield return null;
            TriggerTutorialSegment(2); // Welding objective
            while (!mechanic.HasEquippedItem("divingmask") || !mechanic.HasEquippedItem("weldingtool")) yield return null; // Wait until equipped
            SetDoorAccess(mechanic_secondDoor, mechanic_secondDoorLight, true);
            SetHighlight(mechanic_brokenWall_1, true);
            while (WallHasDamagedSections(mechanic_brokenWall_1)) yield return null; // Highlight until repaired
            RemoveCompletedObjective(segments[2]);
            SetHighlight(mechanic_brokenWall_1, false);
            yield return new WaitForSeconds(1f);
            TriggerTutorialSegment(3); // Pump objective
            SetHighlight(mechanic_workingPump.Item, true);
            while (mechanic_workingPump.FlowPercentage >= 0 || !mechanic_workingPump.IsActive) yield return null; // Highlight until draining
            SetHighlight(mechanic_workingPump.Item, false);
            while (mechanic_brokenhull_1.WaterPercentage > waterVolumeBeforeOpening) yield return null; // Unlock door once drained
            RemoveCompletedObjective(segments[3]);
            SetDoorAccess(mechanic_thirdDoor, mechanic_thirdDoorLight, true);
            yield return new WaitForSeconds(2f);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.News"), ChatMessageType.Radio, null);
            yield return new WaitForSeconds(1f);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.Fire"), ChatMessageType.Radio, null);

            // Room 4
            while (!mechanic_thirdDoor.IsOpen) yield return null;
            mechanic_fire = new DummyFireSource(new Vector2(20f, 2f), Item.ItemList.Find(i => i.HasTag("mechanic_fire")).WorldPosition);
            while (!mechanic_craftingObjectiveSensor.MotionDetected) yield return null;
            TriggerTutorialSegment(4); // Deconstruct
            SetHighlight(mechanic_deconstructor.Item, true);
            while (mechanic.Inventory.FindItemByIdentifier("aluminium") == null) yield return null; // Wait until deconstructed
            SetHighlight(mechanic_deconstructor.Item, false);
            RemoveCompletedObjective(segments[4]);
            yield return new WaitForSeconds(1f);
            TriggerTutorialSegment(5); // Fabricate
            SetHighlight(mechanic_fabricator.Item, true);
            while (mechanic.Inventory.FindItemByIdentifier("extinguisher") == null) yield return null; // Wait until extinguisher is created
            RemoveCompletedObjective(segments[5]);
            SetHighlight(mechanic_fabricator.Item, false);
            SetDoorAccess(mechanic_fourthDoor, mechanic_fourthDoorLight, true);

            // Room 5
            while (!mechanic_fireSensor.MotionDetected) yield return null;
            TriggerTutorialSegment(6); // Using the extinguisher
            while (!mechanic_fire.Removed) yield return null; // Wait until extinguished
            yield return new WaitForSeconds(3f);
            RemoveCompletedObjective(segments[6]);
            SetDoorAccess(mechanic_fifthDoor, mechanic_fifthDoorLight, true);

            // Room 6
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.Diving"), ChatMessageType.Radio, null);
            while (!mechanic_divingSuitObjectiveSensor.MotionDetected) yield return null;
            TriggerTutorialSegment(7); // Dangers of pressure, equip diving suit objective
            SetHighlight(mechanic_divingSuitContainer.Item, true);
            while (!IsSelectedItem(mechanic_divingSuitContainer.Item)) yield return null;
            SetHighlight(mechanic_divingSuitContainer.Item, false);
            while (!mechanic.HasEquippedItem("divingsuit")) yield return null;
            RemoveCompletedObjective(segments[7]);
            SetDoorAccess(mechanic_sixthDoor, mechanic_sixthDoorLight, true);

            // Room 7
            SetHighlight(mechanic_brokenWall_2, true);
            while (WallHasDamagedSections(mechanic_brokenWall_2)) yield return null;
            SetHighlight(mechanic_brokenWall_2, false);
            TriggerTutorialSegment(8); // Repairing machinery (pump)
            SetHighlight(mechanic_brokenPump.Item, true);
            while (!mechanic_brokenPump.Item.IsFullCondition || mechanic_brokenPump.FlowPercentage >= 0 || !mechanic_brokenPump.IsActive) yield return null;
            RemoveCompletedObjective(segments[8]);
            SetHighlight(mechanic_brokenPump.Item, false);
            while (mechanic_brokenhull_2.WaterPercentage > waterVolumeBeforeOpening) yield return null;
            SetDoorAccess(tutorial_submarineDoor, tutorial_submarineDoorLight, true);


            while (true) yield return null;
            // Submarine
            while (!tutorial_enteredSubmarineSensor.MotionDetected) yield return null;
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.Submarine"), ChatMessageType.Radio, null);
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
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.Complete"), ChatMessageType.Radio, null);

            // END TUTORIAL
            Completed = true;
        }

        private bool IsSelectedItem(Item item)
        {
            return mechanic?.SelectedConstruction == item;
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
