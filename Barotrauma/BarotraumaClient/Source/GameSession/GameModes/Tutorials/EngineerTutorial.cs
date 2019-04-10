using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma.Tutorials
{
    class EngineerTutorial : ScenarioTutorial
    {
        // Room 1
        private float shakeTimer = 3f;
        private float shakeAmount = 20f;

        // Room 2
        private MotionSensor engineer_equipmentObjectiveSensor;
        private Door engineer_firstDoor;
        private LightComponent engineer_firstDoorLight;

        // Room 3
        private MotionSensor engineer_reactorObjectiveSensor;
        private Reactor engineer_reactor;
        private Door engineer_secondDoor;
        private LightComponent engineer_secondDoorLight;

        // Room 4
        private MotionSensor engineer_repairJunctionBoxObjectiveSensor;
        private Item engineer_brokenJunctionBox;
        private Door engineer_thirdDoor;
        private LightComponent engineer_thirdDoorLight;

        // Room 5
        private MotionSensor engineer_disconnectedJunctionBoxObjectiveSensor;
        private PowerTransfer engineer_disconnectedJunctionBox_1;
        private PowerTransfer engineer_disconnectedJunctionBox_2;
        private PowerTransfer engineer_disconnectedJunctionBox_3;
        private PowerTransfer engineer_disconnectedJunctionBox_4;
        private Door engineer_fourthDoor;
        private LightComponent engineer_fourthDoorLight;

        // Room 6
        private Pump engineer_workingPump;

        // Submarine
        private MotionSensor tutorial_enteredSubmarineSensor;
        private Item engineer_submarineJunctionBox;
        private Reactor engineer_submarineReactor;

        // Variables
        private string radioSpeakerName;
        private Character engineer;
        private const float waterVolumeBeforeOpening = 15f;

        public EngineerTutorial(XElement element) : base(element)
        {

        }

        public override void Start()
        {
            base.Start();

            radioSpeakerName = TextManager.Get("Tutorial.Radio.Speaker");
            engineer = Character.Controlled;

            // Room 2
            engineer_equipmentObjectiveSensor = Item.ItemList.Find(i => i.HasTag("engineer_equipmentobjectivesensor")).GetComponent <MotionSensor>();
            engineer_firstDoor = Item.ItemList.Find(i => i.HasTag("engineer_firstdoor")).GetComponent<Door>();
            engineer_firstDoorLight = Item.ItemList.Find(i => i.HasTag("engineer_firstdoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(engineer_firstDoor, engineer_firstDoorLight, false);

            // Room 3
            engineer_reactorObjectiveSensor = Item.ItemList.Find(i => i.HasTag("engineer_reactorobjectivesensor")).GetComponent<MotionSensor>();
            engineer_reactor = Item.ItemList.Find(i => i.HasTag("engineer_reactor")).GetComponent<Reactor>();
            engineer_secondDoor = Item.ItemList.Find(i => i.HasTag("engineer_seconddoor")).GetComponent<Door>();
            engineer_secondDoorLight = Item.ItemList.Find(i => i.HasTag("engineer_seconddoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(engineer_secondDoor, engineer_secondDoorLight, false);

            // Room 4
            engineer_repairJunctionBoxObjectiveSensor = Item.ItemList.Find(i => i.HasTag("engineer_repairjunctionboxobjectivesensor")).GetComponent<MotionSensor>();
            engineer_brokenJunctionBox = Item.ItemList.Find(i => i.HasTag("engineer_brokenjunctionbox"));
            engineer_thirdDoor = Item.ItemList.Find(i => i.HasTag("engineer_thirddoor")).GetComponent<Door>();
            engineer_thirdDoorLight = Item.ItemList.Find(i => i.HasTag("engineer_thirddoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(engineer_thirdDoor, engineer_thirdDoorLight, false);

            // Room 5
            engineer_disconnectedJunctionBoxObjectiveSensor = Item.ItemList.Find(i => i.HasTag("engineer_disconnectedjunctionboxobjectivesensor")).GetComponent<MotionSensor>();
            engineer_disconnectedJunctionBox_1 = Item.ItemList.Find(i => i.HasTag("engineer_disconnectedjunctionbox_1")).GetComponent<PowerTransfer>();
            engineer_disconnectedJunctionBox_2 = Item.ItemList.Find(i => i.HasTag("engineer_disconnectedjunctionbox_2")).GetComponent<PowerTransfer>();
            engineer_disconnectedJunctionBox_3 = Item.ItemList.Find(i => i.HasTag("engineer_disconnectedjunctionbox_3")).GetComponent<PowerTransfer>();
            engineer_disconnectedJunctionBox_4 = Item.ItemList.Find(i => i.HasTag("engineer_disconnectedjunctionbox_4")).GetComponent<PowerTransfer>();
            engineer_fourthDoor = Item.ItemList.Find(i => i.HasTag("engineer_fourthdoor")).GetComponent<Door>();
            engineer_fourthDoorLight = Item.ItemList.Find(i => i.HasTag("engineer_fourthdoorlight")).GetComponent<LightComponent>();
            SetDoorAccess(engineer_fourthDoor, engineer_fourthDoorLight, false);

            // Room 6
            engineer_workingPump = Item.ItemList.Find(i => i.HasTag("engineer_workingpump")).GetComponent<Pump>();

            // Submarine
            tutorial_enteredSubmarineSensor = Item.ItemList.Find(i => i.HasTag("tutorial_enteredsubmarinesensor")).GetComponent<MotionSensor>();
            engineer_submarineJunctionBox = Item.ItemList.Find(i => i.HasTag("engineer_submarinejunctionbox"));
            engineer_submarineReactor = Item.ItemList.Find(i => i.HasTag("engineer_submarinereactor")).GetComponent<Reactor>();
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
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.WakeUp"), ChatMessageType.Radio, null);

            // Room 2
            while (!engineer_equipmentObjectiveSensor.MotionDetected) yield return null;
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.Equipment"), ChatMessageType.Radio, null);
            yield return new WaitForSeconds(2f);
            TriggerTutorialSegment(0); // Retrieve equipment
            while (engineer.Inventory.FindItemByIdentifier("divingmask") == null || engineer.Inventory.FindItemByIdentifier("weldingtool") == null || engineer.Inventory.FindItemByIdentifier("wrench") == null) yield return null; // Wait until looted
            RemoveCompletedObjective(segments[0]);
            SetDoorAccess(engineer_firstDoor, engineer_firstDoorLight, true);

            // Room 3
            while (!engineer_reactorObjectiveSensor.MotionDetected) yield return null;
            TriggerTutorialSegment(1);
            while (!ReactorOperatedProperly()) yield return null; // TODO
            yield return new WaitForSeconds(2f);
            RemoveCompletedObjective(segments[1]);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.ReactorStable"), ChatMessageType.Radio, null);
            while (!engineer_reactor.AutoTemp) yield return null;
            SetDoorAccess(engineer_secondDoor, engineer_secondDoorLight, true);

            // Room 4
            TriggerTutorialSegment(2); // Repair the junction box
            SetHighlight(engineer_brokenJunctionBox, true);
            while (!engineer_brokenJunctionBox.IsFullCondition) yield return null; // Wait until repaired
            SetHighlight(engineer_brokenJunctionBox, false);
            RemoveCompletedObjective(segments[2]);
            SetDoorAccess(engineer_thirdDoor, engineer_thirdDoorLight, true);

            // Room 5
            while (!engineer_disconnectedJunctionBoxObjectiveSensor.MotionDetected) yield return null;
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.FaultyWiring"), ChatMessageType.Radio, null);
            yield return new WaitForSeconds(2f);
            TriggerTutorialSegment(3); // Connect the junction boxes
            SetHighlight(engineer_disconnectedJunctionBox_1.Item, true);
            SetHighlight(engineer_disconnectedJunctionBox_2.Item, true);
            SetHighlight(engineer_disconnectedJunctionBox_3.Item, true);
            SetHighlight(engineer_disconnectedJunctionBox_4.Item, true);
            while (engineer_workingPump.CurrPowerConsumption == 0f) yield return null; // Wait until connected all the way to the pump
            SetHighlight(engineer_disconnectedJunctionBox_1.Item, false);
            SetHighlight(engineer_disconnectedJunctionBox_2.Item, false);
            SetHighlight(engineer_disconnectedJunctionBox_3.Item, false);
            SetHighlight(engineer_disconnectedJunctionBox_4.Item, false);
            RemoveCompletedObjective(segments[3]);
            while (engineer_workingPump.Item.CurrentHull.WaterPercentage > waterVolumeBeforeOpening) yield return null; // Wait until drained
            SetDoorAccess(engineer_fourthDoor, engineer_fourthDoorLight, true);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.ChangeOfPlans"), ChatMessageType.Radio, null);

            // Submarine
            while (!tutorial_enteredSubmarineSensor.MotionDetected) yield return null;
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.Submarine"), ChatMessageType.Radio, null);
            yield return new WaitForSeconds(2f);
            TriggerTutorialSegment(4); // Repair junction box
            SetHighlight(engineer_submarineJunctionBox, true);
            while (!engineer_submarineJunctionBox.IsFullCondition) yield return null; // Wait until repaired
            SetHighlight(engineer_submarineJunctionBox, false);
            TriggerTutorialSegment(5); // Powerup reactor
            SetHighlight(engineer_reactor.Item, true);
            while (!IsReactorPoweredUp()) yield return null; // Wait until ~matches load
            SetHighlight(engineer_reactor.Item, false);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.Complete"), ChatMessageType.Radio, null);

            Completed = true;
        }

        private bool ReactorOperatedProperly()
        {
            return true;
        }

        private bool IsReactorPoweredUp()
        {
            float load = 0.0f;
            List<Connection> connections = engineer_reactor.Item.Connections;
            if (connections != null && connections.Count > 0)
            {
                foreach (Connection connection in connections)
                {
                    if (!connection.IsPower) continue;
                    foreach (Connection recipient in connection.Recipients)
                    {
                        if (!(recipient.Item is Item it)) continue;

                        PowerTransfer pt = it.GetComponent<PowerTransfer>();
                        if (pt == null) continue;

                        load = Math.Max(load, pt.PowerLoad);
                    }
                }
            }

            return Math.Abs(load + engineer_reactor.CurrPowerConsumption) < 10;
        }
    }
}
