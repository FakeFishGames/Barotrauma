using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma.Tutorials
{
    class MechanicTutorial : ScenarioTutorial
    {
        // Room 1
        private float shakeTimer = 3.0f;
        private float shakeAmount = 20f;
        private Item mechanic_firstButton;
        private Door mechanic_firstDoor;

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
        private Deconstructor mechanic_deconstructor;
        private Fabricator mechanic_fabricator;
        private Door mechanic_fourthDoor;
        private LightComponent mechanic_fourthDoorLight;

        private bool fabricatorInteractedWith = false;
        private bool deconstructorInteractedWith = false;

        // Room 5
        private DummyFireSource mechanic_fire;
        private MotionSensor mechanic_divingSuitObjectiveSensor;
        private ItemContainer mechanic_divingSuitContainer;
        private Door mechanic_fifthDoor;
        private LightComponent mechanic_fifthDoorLight;

        // Room 6
        private Pump mechanic_brokenPump;
        private Structure mechanic_brokenWall_2;
        private Hull mechanic_brokenhull_2;
        private Door mechanic_sixthDoor;
        private LightComponent mechanic_sixthDoorLight;

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

            // Room 1
            mechanic_firstButton = Item.ItemList.Find(i => i.HasTag("mechanic_firstbutton"));
            mechanic_firstButton.SpriteColor = accessibleColor;
            mechanic_firstButton.ExternalHighlight = true;
            mechanic_firstDoor = Item.ItemList.Find(i => i.HasTag("mechanic_firstdoor")).GetComponent<Door>();

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
            mechanic_brokenhull_1 = mechanic_brokenWall_1.Sections[0].gap.FlowTargetHull;

            SetDoorAccess(mechanic_thirdDoor, mechanic_thirdDoorLight, false);
            mechanic_brokenWall_1.Indestructible = false;
            for (int i = 0; i < mechanic_brokenWall_1.SectionCount; i++)
            {
                mechanic_brokenWall_1.AddDamage(i, 100);
            }

            // Room 4
            mechanic_deconstructor = Item.ItemList.Find(i => i.HasTag("deconstructor")).GetComponent<Deconstructor>();
            mechanic_fabricator = Item.ItemList.Find(i => i.HasTag("fabricator")).GetComponent<Fabricator>();
            mechanic_fourthDoor = Item.ItemList.Find(i => i.HasTag("mechanic_fourthdoor")).GetComponent<Door>();
            mechanic_fourthDoorLight = Item.ItemList.Find(i => i.HasTag("mechanic_fourthdoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(mechanic_fourthDoor, mechanic_fourthDoorLight, false);

            // Room 5
            mechanic_fire = new DummyFireSource(new Vector2(10f, 5f), Item.ItemList.Find(i => i.HasTag("mechanic_fire")).WorldPosition);
            mechanic_divingSuitObjectiveSensor = Item.ItemList.Find(i => i.HasTag("mechanic_divingsuitobjectivesensor")).GetComponent<MotionSensor>();
            mechanic_divingSuitContainer = Item.ItemList.Find(i => i.HasTag("mechanic_divingsuitcontainer")).GetComponent<ItemContainer>();
            mechanic_fifthDoor = Item.ItemList.Find(i => i.HasTag("mechanic_fifthdoor")).GetComponent<Door>();
            mechanic_fifthDoorLight = Item.ItemList.Find(i => i.HasTag("mechanic_fifthdoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(mechanic_fifthDoor, mechanic_fifthDoorLight, false);

            // Room 6
            mechanic_brokenPump = Item.ItemList.Find(i => i.HasTag("mechanic_brokenpump")).GetComponent<Pump>();
            mechanic_brokenWall_2 = Structure.WallList.Find(i => i.SpecialTag == "mechanic_brokenwall_2");
            mechanic_brokenhull_2 = mechanic_brokenWall_2.Sections[0].gap.FlowTargetHull;
            mechanic_sixthDoor = Item.ItemList.Find(i => i.HasTag("mechanic_sixthdoor")).GetComponent<Door>();
            mechanic_sixthDoorLight = Item.ItemList.Find(i => i.HasTag("mechanic_sixthdoorlight")).GetComponent<LightComponent>();

            mechanic_brokenWall_2.Indestructible = false;
            for (int i = 0; i < mechanic_brokenWall_2.SectionCount; i++)
            {
                mechanic_brokenWall_2.AddDamage(i, 100);
            }
            SetDoorAccess(mechanic_sixthDoor, mechanic_sixthDoorLight, false);

            // Submarine
            mechanic_enteredSubmarineSensor = Item.ItemList.Find(i => i.HasTag("mechanic_enteredsubmarinesensor")).GetComponent<MotionSensor>();
            mechanic_submarineEngine = Item.ItemList.Find(i => i.HasTag("mechanic_submarineengine")).GetComponent<Engine>();
            mechanic_ballastPump_1 = Item.ItemList.Find(i => i.HasTag("mechanic_ballastpump_1")).GetComponent<Pump>();
            mechanic_ballastPump_2 = Item.ItemList.Find(i => i.HasTag("mechanic_ballastpump_2")).GetComponent<Pump>();
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

            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage("Concerned crewmember", "telling player to get moving and get to the sub, a Moloch is attacking", ChatMessageType.Default, null);

            TriggerTutorialSegment(0); // Open door objective
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
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage("crewmember explaining", "that the player needs to repair the walls along the way", ChatMessageType.Default, null);
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
            while (mechanic_workingPump.CurrFlow >= 0) yield return null; // Highlight until draining
            SetHighlight(mechanic_workingPump.Item, false);
            while (mechanic_brokenhull_1.WaterPercentage > 0) yield return null; // Unlock door once drained
            RemoveCompletedObjective(segments[3]);
            SetDoorAccess(mechanic_thirdDoor, mechanic_thirdDoorLight, true);

            // Room 4
            SetHighlight(mechanic_deconstructor.Item, true);
            SetHighlight(mechanic_fabricator.Item, true);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage("crewmember explaining", "you must fabricate a fire extinguisher using sodium and aluminum from a deconstructed oxygen tank", ChatMessageType.Default, null);
            while (!IsExtinguisherCreationComplete()) yield return null; // Wait until extinguisher is created
            TriggerTutorialSegment(6); // Equip extinguisher
            while (!Character.Controlled.HasEquippedItem("extinguisher")) yield return null;
            SetDoorAccess(mechanic_fourthDoor, mechanic_fourthDoorLight, false);

            // Room 5
            while (!mechanic_fourthDoor.IsOpen) yield return null;
            TriggerTutorialSegment(7); // Using the extinguisher
            while (mechanic_fire != null) yield return null; // Wait until extinguished
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage("crewmember warning", "of high pressure in next room", ChatMessageType.Default, null);
            while (!mechanic_divingSuitObjectiveSensor.MotionDetected) yield return null;
            TriggerTutorialSegment(8); // Dangers of pressure, equip diving suit objective
            SetHighlight(mechanic_divingSuitContainer.Item, true);
            while (!IsSelectedItem(mechanic_divingSuitContainer.Item)) yield return null;
            SetHighlight(mechanic_divingSuitContainer.Item, false);
            while (!Character.Controlled.HasEquippedItem("divingsuit")) yield return null;
            SetDoorAccess(mechanic_fifthDoor, mechanic_fifthDoorLight, true);

            // Room 6
            SetHighlight(mechanic_brokenWall_2, true);
            while (WallHasDamagedSections(mechanic_brokenWall_2)) yield return null;
            SetHighlight(mechanic_brokenWall_2, false);
            TriggerTutorialSegment(9); // Repairing machinery (pump)
            SetHighlight(mechanic_brokenPump.Item, true);
            while (!mechanic_brokenPump.Item.IsFullCondition && !mechanic_brokenPump.IsActive) yield return null;
            SetHighlight(mechanic_brokenPump.Item, false);
            while (mechanic_brokenhull_2.WaterPercentage > 0) yield return null;
            SetDoorAccess(mechanic_sixthDoor, mechanic_sixthDoorLight, true);

            // Submarine
            while (!mechanic_enteredSubmarineSensor.MotionDetected) yield return null;
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage("crewmember prompts", "player to repair ballast pumps and engine", ChatMessageType.Default, null);
            TriggerTutorialSegment(10); // Repairing ballast pumps, engine
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

            // END TUTORIAL
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
            door.Stuck = (state) ? 100f : 0f;
            light.LightColor = (state) ? accessibleColor : inaccessibleColor;
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

        private bool IsExtinguisherCreationComplete()
        {
            if (deconstructorInteractedWith && fabricatorInteractedWith && !HasObjective(segments[4]) && !HasObjective(segments[5])) return true;

            if (!deconstructorInteractedWith)
            {
                if (IsSelectedItem(mechanic_deconstructor.Item))
                {
                    deconstructorInteractedWith = true;
                    SetHighlight(mechanic_deconstructor.Item, false);
                    TriggerTutorialSegment(4); // Deconstructor objective
                }
            }
            else
            {
                if (HasObjective(segments[4]))
                {
                    if (Character.Controlled.Inventory.FindItemByIdentifier("sodium") != null && Character.Controlled.Inventory.FindItemByIdentifier("aluminium") != null)
                    {
                        RemoveCompletedObjective(segments[4]);
                    }
                }
            }

            if (!fabricatorInteractedWith)
            {
                if (IsSelectedItem(mechanic_fabricator.Item))
                {
                    fabricatorInteractedWith = true;
                    SetHighlight(mechanic_fabricator.Item, false);
                    TriggerTutorialSegment(5); // Fabricator objective
                }
            }
            else
            {
                if (HasObjective(segments[5]))
                {
                    if (Character.Controlled.Inventory.FindItemByIdentifier("extinguisher") != null)
                    {
                        RemoveCompletedObjective(segments[5]);
                    }
                }
            }

            return false;
        }
    }
}
