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
        // Other tutorial items
        private LightComponent tutorial_securityFinalDoorLight;
        private LightComponent tutorial_mechanicFinalDoorLight;

        // Room 1
        private float shakeTimer = 3f;
        private float shakeAmount = 20f;

        // Room 2
        private MotionSensor engineer_equipmentObjectiveSensor;
        private ItemContainer engineer_equipmentCabinet;
        private Item engineer_firstDoorButton;
        private LightComponent engineer_firstDoorLight;

        // Room 3
        private MotionSensor engineer_reactorObjectiveSensor;
        private Reactor engineer_reactor;
        private Item engineer_secondDoorButton;
        private LightComponent engineer_secondDoorLight;

        // Room 4
        private MotionSensor engineer_repairJunctionBoxObjectiveSensor;
        private Item engineer_brokenJunctionBox;
        private Item engineer_thirdDoorButton;
        private LightComponent engineer_thirdDoorLight;

        // Room 5
        private MotionSensor engineer_disconnectedJunctionBoxObjectiveSensor;
        private PowerTransfer engineer_disconnectedJunctionBox_1;
        private PowerTransfer engineer_disconnectedJunctionBox_2;
        private PowerTransfer engineer_disconnectedJunctionBox_3;
        private PowerTransfer engineer_disconnectedJunctionBox_4;
        private Item engineer_wire_1;
        private Powered engineer_lamp_1;
        private Item engineer_wire_2;
        private Powered engineer_lamp_2;
        private Item engineer_fourthDoorButton;
        private LightComponent engineer_fourthDoorLight;

        // Room 6
        private Pump engineer_workingPump;

        // Submarine
        private Item tutorial_submarineDoorButton;
        private LightComponent tutorial_submarineDoorLight;
        private MotionSensor tutorial_enteredSubmarineSensor;
        private Item engineer_submarineJunctionBox_1;
        private Item engineer_submarineJunctionBox_2;
        private Item engineer_submarineJunctionBox_3;
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

            var toolbox = engineer.Inventory.FindItemByIdentifier("toolbox");
            engineer.Inventory.RemoveItem(toolbox);

            // Other tutorial items
            tutorial_securityFinalDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_securityfinaldoorlight")).GetComponent<LightComponent>();
            tutorial_mechanicFinalDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_mechanicfinaldoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(null, tutorial_securityFinalDoorLight, false);
            SetDoorAccess(null, tutorial_mechanicFinalDoorLight, false);

            // Room 2
            engineer_equipmentObjectiveSensor = Item.ItemList.Find(i => i.HasTag("engineer_equipmentobjectivesensor")).GetComponent<MotionSensor>();
            engineer_equipmentCabinet = Item.ItemList.Find(i => i.HasTag("engineer_equipmentcabinet")).GetComponent<ItemContainer>();
            engineer_firstDoorButton = Item.ItemList.Find(i => i.HasTag("engineer_firstdoorbutton"));
            engineer_firstDoorLight = Item.ItemList.Find(i => i.HasTag("engineer_firstdoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(engineer_firstDoorButton, engineer_firstDoorLight, false);

            // Room 3
            engineer_reactorObjectiveSensor = Item.ItemList.Find(i => i.HasTag("engineer_reactorobjectivesensor")).GetComponent<MotionSensor>();
            engineer_reactor = Item.ItemList.Find(i => i.HasTag("engineer_reactor")).GetComponent<Reactor>();
            engineer_reactor.FireDelay = float.PositiveInfinity;

            engineer_secondDoorButton = Item.ItemList.Find(i => i.HasTag("engineer_seconddoorbutton"));
            engineer_secondDoorLight = Item.ItemList.Find(i => i.HasTag("engineer_seconddoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(engineer_secondDoorButton, engineer_secondDoorLight, false);

            // Room 4
            engineer_repairJunctionBoxObjectiveSensor = Item.ItemList.Find(i => i.HasTag("engineer_repairjunctionboxobjectivesensor")).GetComponent<MotionSensor>();
            engineer_brokenJunctionBox = Item.ItemList.Find(i => i.HasTag("engineer_brokenjunctionbox"));
            engineer_thirdDoorButton = Item.ItemList.Find(i => i.HasTag("engineer_thirddoorbutton"));
            engineer_thirdDoorLight = Item.ItemList.Find(i => i.HasTag("engineer_thirddoorlight")).GetComponent<LightComponent>();

            engineer_brokenJunctionBox.Indestructible = false;
            engineer_brokenJunctionBox.Condition = 0f;

            SetDoorAccess(engineer_thirdDoorButton, engineer_thirdDoorLight, false);

            // Room 5
            engineer_disconnectedJunctionBoxObjectiveSensor = Item.ItemList.Find(i => i.HasTag("engineer_disconnectedjunctionboxobjectivesensor")).GetComponent<MotionSensor>();
            engineer_disconnectedJunctionBox_1 = Item.ItemList.Find(i => i.HasTag("engineer_disconnectedjunctionbox_1")).GetComponent<PowerTransfer>();
            engineer_disconnectedJunctionBox_1.Item.GetComponent<ConnectionPanel>().Locked = false;
            engineer_disconnectedJunctionBox_2 = Item.ItemList.Find(i => i.HasTag("engineer_disconnectedjunctionbox_2")).GetComponent<PowerTransfer>();
            engineer_disconnectedJunctionBox_2.Item.GetComponent<ConnectionPanel>().Locked = false;
            engineer_disconnectedJunctionBox_3 = Item.ItemList.Find(i => i.HasTag("engineer_disconnectedjunctionbox_3")).GetComponent<PowerTransfer>();
            engineer_disconnectedJunctionBox_3.Item.GetComponent<ConnectionPanel>().Locked = false;
            engineer_disconnectedJunctionBox_4 = Item.ItemList.Find(i => i.HasTag("engineer_disconnectedjunctionbox_4")).GetComponent<PowerTransfer>();
            engineer_disconnectedJunctionBox_4.Item.GetComponent<ConnectionPanel>().Locked = false;
            engineer_wire_1 = Item.ItemList.Find(i => i.HasTag("engineer_wire_1"));
            engineer_wire_2 = Item.ItemList.Find(i => i.HasTag("engineer_wire_2"));
            engineer_lamp_1 = Item.ItemList.Find(i => i.HasTag("engineer_lamp_1")).GetComponent<Powered>();
            engineer_lamp_2 = Item.ItemList.Find(i => i.HasTag("engineer_lamp_2")).GetComponent<Powered>();
            engineer_fourthDoorButton = Item.ItemList.Find(i => i.HasTag("engineer_fourthdoorbutton"));
            engineer_fourthDoorLight = Item.ItemList.Find(i => i.HasTag("engineer_fourthdoorlight")).GetComponent<LightComponent>();
            SetDoorAccess(engineer_fourthDoorButton, engineer_fourthDoorLight, false);

            // Room 6
            engineer_workingPump = Item.ItemList.Find(i => i.HasTag("engineer_workingpump")).GetComponent<Pump>();
            engineer_workingPump.Item.CurrentHull.WaterVolume += engineer_workingPump.Item.CurrentHull.Volume;
            engineer_workingPump.IsActive = true;

            // Submarine
            tutorial_submarineDoorButton = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoorbutton"));
            tutorial_submarineDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoorlight")).GetComponent<LightComponent>();
            SetDoorAccess(tutorial_submarineDoorButton, tutorial_submarineDoorLight, true);

            tutorial_enteredSubmarineSensor = Item.ItemList.Find(i => i.HasTag("tutorial_enteredsubmarinesensor")).GetComponent<MotionSensor>();
            engineer_submarineJunctionBox_1 = Item.ItemList.Find(i => i.HasTag("engineer_submarinejunctionbox_1"));
            engineer_submarineJunctionBox_2 = Item.ItemList.Find(i => i.HasTag("engineer_submarinejunctionbox_2"));
            engineer_submarineJunctionBox_3 = Item.ItemList.Find(i => i.HasTag("engineer_submarinejunctionbox_3"));
            engineer_submarineReactor = Item.ItemList.Find(i => i.HasTag("engineer_submarinereactor")).GetComponent<Reactor>();

            engineer_submarineJunctionBox_1.Indestructible = false;
            engineer_submarineJunctionBox_1.Condition = 0f;
            engineer_submarineJunctionBox_2.Indestructible = false;
            engineer_submarineJunctionBox_2.Condition = 0f;
            engineer_submarineJunctionBox_3.Indestructible = false;
            engineer_submarineJunctionBox_3.Condition = 0f;
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
            do { yield return null; } while (!engineer_equipmentObjectiveSensor.MotionDetected);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.Equipment"), ChatMessageType.Radio, null);
            yield return new WaitForSeconds(2f);
            TriggerTutorialSegment(0); // Retrieve equipment
            SetHighlight(engineer_equipmentCabinet.Item, true);
            do { yield return null; } while (engineer.Inventory.FindItemByIdentifier("screwdriver") == null || engineer.Inventory.FindItemByIdentifier("redwire") == null || engineer.Inventory.FindItemByIdentifier("bluewire") == null); // Wait until looted
            RemoveCompletedObjective(segments[0]);
            SetHighlight(engineer_equipmentCabinet.Item, false);
            SetDoorAccess(engineer_firstDoorButton, engineer_firstDoorLight, true);

            // Room 3
            do { yield return null; } while (!engineer_reactorObjectiveSensor.MotionDetected);
            TriggerTutorialSegment(1);
            do { yield return null; } while (!ReactorOperatedProperly()); // TODO
            yield return new WaitForSeconds(2f);
            RemoveCompletedObjective(segments[1]);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.ReactorStable"), ChatMessageType.Radio, null);
            do { yield return null; } while (!engineer_reactor.AutoTemp);
            SetDoorAccess(engineer_secondDoorButton, engineer_secondDoorLight, true);

            // Room 4
            TriggerTutorialSegment(2); // Repair the junction box
            SetHighlight(engineer_brokenJunctionBox, true);
            do { yield return null; } while (!engineer_brokenJunctionBox.IsFullCondition); // Wait until repaired
            SetHighlight(engineer_brokenJunctionBox, false);
            RemoveCompletedObjective(segments[2]);
            SetDoorAccess(engineer_thirdDoorButton, engineer_thirdDoorLight, true);

            // Room 5
            do { yield return null; } while (!engineer_disconnectedJunctionBoxObjectiveSensor.MotionDetected);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.FaultyWiring"), ChatMessageType.Radio, null);
            yield return new WaitForSeconds(2f);
            TriggerTutorialSegment(3); // Connect the junction boxes
            SetHighlight(engineer_disconnectedJunctionBox_1.Item, true);
            SetHighlight(engineer_disconnectedJunctionBox_2.Item, true);
            SetHighlight(engineer_disconnectedJunctionBox_3.Item, true);
            SetHighlight(engineer_disconnectedJunctionBox_4.Item, true);
            do { CheckGhostWires(); yield return null; } while (engineer_workingPump.Voltage < engineer_workingPump.MinVoltage); // Wait until connected all the way to the pump
            SetHighlight(engineer_disconnectedJunctionBox_1.Item, false);
            SetHighlight(engineer_disconnectedJunctionBox_2.Item, false);
            SetHighlight(engineer_disconnectedJunctionBox_3.Item, false);
            SetHighlight(engineer_disconnectedJunctionBox_4.Item, false);
            RemoveCompletedObjective(segments[3]);
            do { yield return null; } while (engineer_workingPump.Item.CurrentHull.WaterPercentage > waterVolumeBeforeOpening); // Wait until drained
            SetDoorAccess(engineer_fourthDoorButton, engineer_fourthDoorLight, true);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.ChangeOfPlans"), ChatMessageType.Radio, null);

            // Submarine
            do { yield return null; } while (!tutorial_enteredSubmarineSensor.MotionDetected);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.Submarine"), ChatMessageType.Radio, null);
            yield return new WaitForSeconds(2f);
            TriggerTutorialSegment(4); // Repair junction box
            SetHighlight(engineer_submarineJunctionBox_1, true);
            SetHighlight(engineer_submarineJunctionBox_2, true);
            SetHighlight(engineer_submarineJunctionBox_3, true);
            // Remove highlights when each individual machine is repaired
            do { CheckJunctionBoxHighlights(); yield return null; } while (!engineer_submarineJunctionBox_1.IsFullCondition || !engineer_submarineJunctionBox_2.IsFullCondition || !engineer_submarineJunctionBox_3.IsFullCondition);
            CheckJunctionBoxHighlights();
            RemoveCompletedObjective(segments[4]);
            TriggerTutorialSegment(5); // Powerup reactor
            SetHighlight(engineer_reactor.Item, true);
            do { yield return null; } while (!IsReactorPoweredUp()); // Wait until ~matches load
            SetHighlight(engineer_reactor.Item, false);
            RemoveCompletedObjective(segments[5]);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.Complete"), ChatMessageType.Radio, null);

            Completed = true;
        }

        private bool ReactorOperatedProperly()
        {
            return true;
        }

        private void CheckGhostWires()
        {
            if (engineer_wire_1 != null && engineer_lamp_1.Voltage > engineer_lamp_1.MinVoltage)
            {
                engineer_wire_1.Remove();
                engineer_wire_1 = null;
            }

            if (engineer_wire_2 != null && engineer_lamp_2.Voltage > engineer_lamp_2.MinVoltage)
            {
                engineer_wire_2.Remove();
                engineer_wire_2 = null;
            }
        }

        private void CheckJunctionBoxHighlights()
        {
            if (engineer_submarineJunctionBox_1.IsFullCondition && engineer_submarineJunctionBox_1.ExternalHighlight) SetHighlight(engineer_submarineJunctionBox_1, false);
            if (engineer_submarineJunctionBox_2.IsFullCondition && engineer_submarineJunctionBox_2.ExternalHighlight) SetHighlight(engineer_submarineJunctionBox_2, false);
            if (engineer_submarineJunctionBox_3.IsFullCondition && engineer_submarineJunctionBox_3.ExternalHighlight) SetHighlight(engineer_submarineJunctionBox_3, false);
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
