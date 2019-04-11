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
        private LightComponent tutorial_securityFinalDoorLight;
        private Item tutorial_upperFinalDoorButton;

        // Room 1
        private float shakeTimer = 3.0f;
        private float shakeAmount = 20f;
        private Item mechanic_firstDoorButton;
        private Door mechanic_firstDoor;
        private LightComponent mechanic_firstDoorLight;

        // Room 2
        private MotionSensor mechanic_equipmentObjectiveSensor;
        private ItemContainer mechanic_equipmentCabinet;
        private Item mechanic_secondDoorButton;
        private LightComponent mechanic_secondDoorLight;

        // Room 3
        private MotionSensor mechanic_weldingObjectiveSensor;
        private Pump mechanic_workingPump;
        private Door mechanic_thirdDoor;
        private Item mechanic_thirdDoorButton;
        private LightComponent mechanic_thirdDoorLight;
        private Structure mechanic_brokenWall_1;
        private Hull mechanic_brokenhull_1;

        // Room 4
        private MotionSensor mechanic_craftingObjectiveSensor;
        private Deconstructor mechanic_deconstructor;
        private Fabricator mechanic_fabricator;
        private Item mechanic_fourthDoorButton;
        private LightComponent mechanic_fourthDoorLight;

        // Room 5
        private MotionSensor mechanic_fireSensor;
        private DummyFireSource mechanic_fire;
        private Item mechanic_fifthDoorButton;
        private LightComponent mechanic_fifthDoorLight;

        // Room 6
        private MotionSensor mechanic_divingSuitObjectiveSensor;
        private ItemContainer mechanic_divingSuitContainer;
        private ItemContainer mechanic_oxygenContainer;
        private Item tutorial_mechanicFinalDoorButton;
        private LightComponent tutorial_mechanicFinalDoorLight;

        // Room 7
        private Pump mechanic_brokenPump;
        private Structure mechanic_brokenWall_2;
        private Hull mechanic_brokenhull_2;
        private Item tutorial_submarineDoorButton;
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
            tutorial_securityFinalDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_securityfinaldoorlight")).GetComponent<LightComponent>();
            tutorial_upperFinalDoorButton = Item.ItemList.Find(i => i.HasTag("tutorial_upperfinaldoorbutton"));     

            SetDoorAccess(null, tutorial_securityFinalDoorLight, false);
            SetDoorAccess(tutorial_upperFinalDoorButton, null, false);

            // Room 1
            mechanic_firstDoorButton = Item.ItemList.Find(i => i.HasTag("mechanic_firstdoorbutton"));
            mechanic_firstDoor = Item.ItemList.Find(i => i.HasTag("mechanic_firstdoor")).GetComponent<Door>();
            mechanic_firstDoorLight = Item.ItemList.Find(i => i.HasTag("mechanic_firstdoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(mechanic_firstDoorButton, mechanic_firstDoorLight, false);

            // Room 2
            mechanic_equipmentObjectiveSensor = Item.ItemList.Find(i => i.HasTag("mechanic_equipmentobjectivesensor")).GetComponent<MotionSensor>();
            mechanic_equipmentCabinet = Item.ItemList.Find(i => i.HasTag("mechanic_equipmentcabinet")).GetComponent<ItemContainer>();
            mechanic_secondDoorButton = Item.ItemList.Find(i => i.HasTag("mechanic_seconddoorbutton"));
            mechanic_secondDoorLight = Item.ItemList.Find(i => i.HasTag("mechanic_seconddoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(mechanic_secondDoorButton, mechanic_secondDoorLight, false);

            // Room 3
            mechanic_weldingObjectiveSensor = Item.ItemList.Find(i => i.HasTag("mechanic_weldingobjectivesensor")).GetComponent<MotionSensor>();
            mechanic_workingPump = Item.ItemList.Find(i => i.HasTag("mechanic_workingpump")).GetComponent<Pump>();
            mechanic_thirdDoor = Item.ItemList.Find(i => i.HasTag("mechanic_thirddoor")).GetComponent<Door>();
            mechanic_thirdDoorButton = Item.ItemList.Find(i => i.HasTag("mechanic_thirddoorbutton"));
            mechanic_thirdDoorLight = Item.ItemList.Find(i => i.HasTag("mechanic_thirddoorlight")).GetComponent<LightComponent>();
            mechanic_brokenWall_1 = Structure.WallList.Find(i => i.SpecialTag == "mechanic_brokenwall_1");

            SetDoorAccess(mechanic_thirdDoorButton, mechanic_thirdDoorLight, false);
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
            mechanic_fourthDoorButton = Item.ItemList.Find(i => i.HasTag("mechanic_fourthdoorbutton"));
            mechanic_fourthDoorLight = Item.ItemList.Find(i => i.HasTag("mechanic_fourthdoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(mechanic_fourthDoorButton, mechanic_fourthDoorLight, false);

            // Room 5
            mechanic_fifthDoorButton = Item.ItemList.Find(i => i.HasTag("mechanic_fifthdoorbutton"));
            mechanic_fifthDoorLight = Item.ItemList.Find(i => i.HasTag("mechanic_fifthdoorlight")).GetComponent<LightComponent>();
            mechanic_fireSensor = Item.ItemList.Find(i => i.HasTag("mechanic_firesensor")).GetComponent<MotionSensor>();

            SetDoorAccess(mechanic_fifthDoorButton, mechanic_fifthDoorLight, false);

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
            tutorial_mechanicFinalDoorButton = Item.ItemList.Find(i => i.HasTag("tutorial_mechanicfinaldoorbutton"));
            tutorial_mechanicFinalDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_mechanicfinaldoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(tutorial_mechanicFinalDoorButton, tutorial_mechanicFinalDoorLight, false);

            // Room 7
            mechanic_brokenPump = Item.ItemList.Find(i => i.HasTag("mechanic_brokenpump")).GetComponent<Pump>();
            mechanic_brokenPump.Item.Indestructible = false;
            mechanic_brokenPump.Item.Condition = 0;
            mechanic_brokenWall_2 = Structure.WallList.Find(i => i.SpecialTag == "mechanic_brokenwall_2");
            tutorial_submarineDoorButton = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoorbutton"));
            tutorial_submarineDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoorlight")).GetComponent<LightComponent>();

            mechanic_brokenWall_2.Indestructible = false;
            mechanic_brokenWall_2.SpriteColor = Color.White;
            for (int i = 0; i < mechanic_brokenWall_2.SectionCount; i++)
            {
                mechanic_brokenWall_2.AddDamage(i, 250);
            }
            mechanic_brokenhull_2 = mechanic_brokenWall_2.Sections[0].gap.FlowTargetHull;
            SetDoorAccess(tutorial_submarineDoorButton, tutorial_submarineDoorLight, false);

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
            SetDoorAccess(mechanic_firstDoorButton, mechanic_firstDoorLight, true);
            SetHighlight(mechanic_firstDoorButton, true);
            do { yield return null; } while (!mechanic_firstDoor.IsOpen);
            SetHighlight(mechanic_firstDoorButton, false);
            yield return new WaitForSeconds(1.5f);
            RemoveCompletedObjective(segments[0]);
            
            // Room 2
            yield return new WaitForSeconds(0.0f);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.Equipment"), ChatMessageType.Radio, null);
            do { yield return null; } while (!mechanic_equipmentObjectiveSensor.MotionDetected);
            TriggerTutorialSegment(1); // Equipment & inventory objective
            SetHighlight(mechanic_equipmentCabinet.Item, true);
            do { yield return null; } while (mechanic.Inventory.FindItemByIdentifier("divingmask") == null || mechanic.Inventory.FindItemByIdentifier("weldingtool") == null || mechanic.Inventory.FindItemByIdentifier("wrench") == null); // Wait until looted
            SetHighlight(mechanic_equipmentCabinet.Item, false);
            yield return new WaitForSeconds(1.5f);
            RemoveCompletedObjective(segments[1]);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.Breach"), ChatMessageType.Radio, null);

            // Room 3
            do { yield return null; } while (!mechanic_weldingObjectiveSensor.MotionDetected);
            TriggerTutorialSegment(2); // Welding objective
            do { yield return null; } while (!mechanic.HasEquippedItem("divingmask") || !mechanic.HasEquippedItem("weldingtool")); // Wait until equipped
            SetDoorAccess(mechanic_secondDoorButton, mechanic_secondDoorLight, true);
            SetHighlight(mechanic_brokenWall_1, true);
            do { yield return null; } while (WallHasDamagedSections(mechanic_brokenWall_1)); // Highlight until repaired
            RemoveCompletedObjective(segments[2]);
            SetHighlight(mechanic_brokenWall_1, false);
            yield return new WaitForSeconds(1f);
            TriggerTutorialSegment(3); // Pump objective
            SetHighlight(mechanic_workingPump.Item, true);
            do { yield return null; } while (mechanic_workingPump.FlowPercentage >= 0 || !mechanic_workingPump.IsActive); // Highlight until draining
            SetHighlight(mechanic_workingPump.Item, false);
            do { yield return null; } while (mechanic_brokenhull_1.WaterPercentage > waterVolumeBeforeOpening); // Unlock door once drained
            RemoveCompletedObjective(segments[3]);
            SetDoorAccess(mechanic_thirdDoorButton, mechanic_thirdDoorLight, true);
            yield return new WaitForSeconds(2f);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.News"), ChatMessageType.Radio, null);
            yield return new WaitForSeconds(1f);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.Fire"), ChatMessageType.Radio, null);

            // Room 4
            do { yield return null; } while (!mechanic_thirdDoor.IsOpen);
            mechanic_fire = new DummyFireSource(new Vector2(20f, 2f), Item.ItemList.Find(i => i.HasTag("mechanic_fire")).WorldPosition);
            do { yield return null; } while (!mechanic_craftingObjectiveSensor.MotionDetected);
            TriggerTutorialSegment(4); // Deconstruct
            SetHighlight(mechanic_deconstructor.Item, true);
            do { yield return null; } while (mechanic.Inventory.FindItemByIdentifier("aluminium") == null); // Wait until deconstructed
            SetHighlight(mechanic_deconstructor.Item, false);
            RemoveCompletedObjective(segments[4]);
            yield return new WaitForSeconds(1f);
            TriggerTutorialSegment(5); // Fabricate
            SetHighlight(mechanic_fabricator.Item, true);
            do { yield return null; } while (mechanic.Inventory.FindItemByIdentifier("extinguisher") == null); // Wait until extinguisher is created
            RemoveCompletedObjective(segments[5]);
            SetHighlight(mechanic_fabricator.Item, false);
            SetDoorAccess(mechanic_fourthDoorButton, mechanic_fourthDoorLight, true);

            // Room 5
            do { yield return null; } while (!mechanic_fireSensor.MotionDetected);
            TriggerTutorialSegment(6); // Using the extinguisher
            do { yield return null; } while (!mechanic_fire.Removed); // Wait until extinguished
            yield return new WaitForSeconds(3f);
            RemoveCompletedObjective(segments[6]);
            SetDoorAccess(mechanic_fifthDoorButton, mechanic_fifthDoorLight, true);

            // Room 6
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.Diving"), ChatMessageType.Radio, null);
            do { yield return null; } while (!mechanic_divingSuitObjectiveSensor.MotionDetected);
            TriggerTutorialSegment(7); // Dangers of pressure, equip diving suit objective
            SetHighlight(mechanic_divingSuitContainer.Item, true);
            do { yield return null; } while (!IsSelectedItem(mechanic_divingSuitContainer.Item));
            SetHighlight(mechanic_divingSuitContainer.Item, false);
            do { yield return null; } while (!mechanic.HasEquippedItem("divingsuit"));
            RemoveCompletedObjective(segments[7]);
            SetDoorAccess(tutorial_mechanicFinalDoorButton, tutorial_mechanicFinalDoorLight, true);

            // Room 7
            SetHighlight(mechanic_brokenWall_2, true);
            do { yield return null; } while (WallHasDamagedSections(mechanic_brokenWall_2));
            SetHighlight(mechanic_brokenWall_2, false);
            TriggerTutorialSegment(8); // Repairing machinery (pump)
            SetHighlight(mechanic_brokenPump.Item, true);
            do { yield return null; } while (!mechanic_brokenPump.Item.IsFullCondition || mechanic_brokenPump.FlowPercentage >= 0 || !mechanic_brokenPump.IsActive);
            RemoveCompletedObjective(segments[8]);
            SetHighlight(mechanic_brokenPump.Item, false);
            do { yield return null; } while (mechanic_brokenhull_2.WaterPercentage > waterVolumeBeforeOpening);
            SetDoorAccess(tutorial_submarineDoorButton, tutorial_submarineDoorLight, true);

            // Submarine
            do { yield return null; } while (!tutorial_enteredSubmarineSensor.MotionDetected);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.Submarine"), ChatMessageType.Radio, null);
            TriggerTutorialSegment(9); // Repairing ballast pumps, engine
            SetHighlight(mechanic_ballastPump_1.Item, true);
            SetHighlight(mechanic_ballastPump_2.Item, true);
            SetHighlight(mechanic_submarineEngine.Item, true);
            // Remove highlights when each individual machine is repaired
            do { CheckHighlights(); yield return null; } while (!mechanic_ballastPump_1.Item.IsFullCondition || !mechanic_ballastPump_2.Item.IsFullCondition || !mechanic_submarineEngine.Item.IsFullCondition);
            CheckHighlights();
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

        private void CheckHighlights()
        {
            if (mechanic_ballastPump_1.Item.IsFullCondition && mechanic_ballastPump_1.Item.ExternalHighlight) SetHighlight(mechanic_ballastPump_1.Item, false);
            if (mechanic_ballastPump_2.Item.IsFullCondition && mechanic_ballastPump_2.Item.ExternalHighlight) SetHighlight(mechanic_ballastPump_2.Item, false);
            if (mechanic_submarineEngine.Item.IsFullCondition && mechanic_submarineEngine.Item.ExternalHighlight) SetHighlight(mechanic_submarineEngine.Item, false);
        }
    }
}
