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
        private Steering tutorial_submarineSteering;

        // Room 1
        private float shakeTimer = 1f;
        private float shakeAmount = 20f;

        // Room 2
        private MotionSensor engineer_equipmentObjectiveSensor;
        private ItemContainer engineer_equipmentCabinet;
        private Door engineer_firstDoor;
        private LightComponent engineer_firstDoorLight;

        // Room 3
        private Powered tutorial_oxygenGenerator;
        private Reactor engineer_reactor;
        private Door engineer_secondDoor;
        private LightComponent engineer_secondDoorLight;

        // Room 4
        private Item engineer_brokenJunctionBox;
        private Door engineer_thirdDoor;
        private LightComponent engineer_thirdDoorLight;

        // Room 5
        private PowerTransfer[] engineer_disconnectedJunctionBoxes;
        private ConnectionPanel[] engineer_disconnectedConnectionPanels;
        private Item engineer_wire_1;
        private Powered engineer_lamp_1;
        private Item engineer_wire_2;
        private Powered engineer_lamp_2;
        private Door engineer_fourthDoor;
        private LightComponent engineer_fourthDoorLight;

        // Room 6
        private Pump engineer_workingPump;
        private Door tutorial_lockedDoor_1;

        // Submarine
        private Door tutorial_submarineDoor;
        private LightComponent tutorial_submarineDoorLight;
        private MotionSensor tutorial_enteredSubmarineSensor;
        private Item engineer_submarineJunctionBox_1;
        private Item engineer_submarineJunctionBox_2;
        private Item engineer_submarineJunctionBox_3;
        private Reactor engineer_submarineReactor;

        // Variables
        private LocalizedString radioSpeakerName;
        private Character engineer;
        private int[] reactorLoads = new int[5] { 1500, 3000, 2000, 5000, 3500 };
        private float reactorLoadChangeTime = 2f;
        private float reactorLoadError = 200f;
        private bool reactorOperatedProperly;
        private const float waterVolumeBeforeOpening = 15f;
        private Sprite engineer_repairIcon;
        private Color engineer_repairIconColor;
        private Sprite engineer_reactorIcon;
        private Color engineer_reactorIconColor;
        private bool wiringActive = false;

        public EngineerTutorial() : base("tutorial.engineertraining".ToIdentifier(),
            new Segment(
                "Mechanic.Equipment".ToIdentifier(),
                "Mechanic.EquipmentObjective".ToIdentifier(),
                TutorialContentType.TextOnly,
                textContent: new Segment.Text { Tag = "Mechanic.EquipmentText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center }),
            new Segment(
                "Engineer.Reactor".ToIdentifier(),
                "Engineer.ReactorObjective".ToIdentifier(),
                TutorialContentType.ManualVideo,
                textContent: new Segment.Text { Tag = "Engineer.ReactorText".ToIdentifier(), Width = 700, Height = 80, Anchor = Anchor.Center },
                videoContent: new Segment.Video { File = "tutorial_reactor.webm", TextTag = "Engineer.ReactorText".ToIdentifier(), Width = 700, Height = 80 }),
            new Segment(
                "Engineer.OperateReactor".ToIdentifier(),
                "Engineer.OperateReactorObjective".ToIdentifier(),
                TutorialContentType.ManualVideo,
                textContent: new Segment.Text { Tag = "Engineer.OperateReactorText".ToIdentifier(), Width = 700, Height = 80, Anchor = Anchor.Center },
                videoContent: new Segment.Video { File = "tutorial_reactor.webm", TextTag = "Engineer.ReactorText".ToIdentifier(), Width = 700, Height = 80 }),
            new Segment(
                "Engineer.RepairJunctionBox".ToIdentifier(),
                "Engineer.RepairJunctionBoxObjective".ToIdentifier(),
                TutorialContentType.TextOnly,
                textContent: new Segment.Text { Tag = "Engineer.RepairJunctionBoxText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center }),
            new Segment(
                "Engineer.WireJunctionBoxes".ToIdentifier(),
                "Engineer.WireJunctionBoxesObjective".ToIdentifier(),
                TutorialContentType.ManualVideo,
                textContent: new Segment.Text { Tag = "Engineer.WireJunctionBoxesText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center },
                videoContent: new Segment.Video { File = "tutorial_wiring.webm", TextTag = "Engineer.WireJunctionBoxesText".ToIdentifier(), Width = 450, Height = 80 }),
            new Segment(
                "Engineer.RepairElectricalRoom".ToIdentifier(),
                "Engineer.RepairElectricalRoomObjective".ToIdentifier(),
                TutorialContentType.TextOnly,
                textContent: new Segment.Text { Tag = "Engineer.RepairElectricalRoomText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center }),
            new Segment(
                "Engineer.PowerUpReactor".ToIdentifier(),
                "Engineer.PowerUpReactorObjective".ToIdentifier(),
                TutorialContentType.TextOnly,
                textContent: new Segment.Text { Tag = "Engineer.PowerUpReactorText".ToIdentifier(), Width = 700, Height = 80, Anchor = Anchor.Center }))
        { }

        protected override CharacterInfo GetCharacterInfo()
        {
            return new CharacterInfo(
                CharacterPrefab.HumanSpeciesName,
                jobOrJobPrefab: new Job(
                    JobPrefab.Prefabs["engineer"], Rand.RandSync.Unsynced, 0,
                    new Skill("medical".ToIdentifier(), 0),
                    new Skill("weapons".ToIdentifier(), 0),
                    new Skill("mechanical".ToIdentifier(), 20),
                    new Skill("electrical".ToIdentifier(), 60),
                    new Skill("helm".ToIdentifier(), 0)));
        }

        protected override void Initialize()
        {
            radioSpeakerName = TextManager.Get("Tutorial.Radio.Speaker");
            engineer = Character.Controlled;

            var toolbelt = FindOrGiveItem(engineer, "toolbelt".ToIdentifier());
            toolbelt.Unequip(engineer);
            engineer.Inventory.RemoveItem(toolbelt);

            var repairOrder = OrderPrefab.Prefabs["repairsystems"];
            engineer_repairIcon = repairOrder.SymbolSprite;
            engineer_repairIconColor = repairOrder.Color;

            var reactorOrder = OrderPrefab.Prefabs["operatereactor"];
            engineer_reactorIcon = reactorOrder.SymbolSprite;
            engineer_reactorIconColor = reactorOrder.Color;

            // Other tutorial items
            tutorial_securityFinalDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_securityfinaldoorlight")).GetComponent<LightComponent>();
            tutorial_mechanicFinalDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_mechanicfinaldoorlight")).GetComponent<LightComponent>();
            tutorial_submarineSteering = Item.ItemList.Find(i => i.HasTag("command")).GetComponent<Steering>();

            tutorial_submarineSteering.CanBeSelected = false;
            foreach (ItemComponent ic in tutorial_submarineSteering.Item.Components)
            {
                ic.CanBeSelected = false;
            }

            SetDoorAccess(null, tutorial_securityFinalDoorLight, false);
            SetDoorAccess(null, tutorial_mechanicFinalDoorLight, false);

            // Room 2
            engineer_equipmentObjectiveSensor = Item.ItemList.Find(i => i.HasTag("engineer_equipmentobjectivesensor")).GetComponent<MotionSensor>();
            engineer_equipmentCabinet = Item.ItemList.Find(i => i.HasTag("engineer_equipmentcabinet")).GetComponent<ItemContainer>();
            engineer_firstDoor = Item.ItemList.Find(i => i.HasTag("engineer_firstdoor")).GetComponent<Door>();
            engineer_firstDoorLight = Item.ItemList.Find(i => i.HasTag("engineer_firstdoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(engineer_firstDoor, engineer_firstDoorLight, false);

            // Room 3
            tutorial_oxygenGenerator = Item.ItemList.Find(i => i.HasTag("tutorial_oxygengenerator")).GetComponent<OxygenGenerator>();
            engineer_reactor = Item.ItemList.Find(i => i.HasTag("engineer_reactor")).GetComponent<Reactor>();
            engineer_reactor.FireDelay = engineer_reactor.MeltdownDelay = float.PositiveInfinity;
            engineer_reactor.FuelConsumptionRate = 0.0f;
            engineer_reactor.PowerOn = true;
            reactorOperatedProperly = false;

            engineer_secondDoor = Item.ItemList.Find(i => i.HasTag("engineer_seconddoor")).GetComponent<Door>(); ;
            engineer_secondDoorLight = Item.ItemList.Find(i => i.HasTag("engineer_seconddoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(engineer_secondDoor, engineer_secondDoorLight, false);

            // Room 4
            engineer_brokenJunctionBox = Item.ItemList.Find(i => i.HasTag("engineer_brokenjunctionbox"));
            engineer_thirdDoor = Item.ItemList.Find(i => i.HasTag("engineer_thirddoor")).GetComponent<Door>();
            engineer_thirdDoorLight = Item.ItemList.Find(i => i.HasTag("engineer_thirddoorlight")).GetComponent<LightComponent>();

            engineer_brokenJunctionBox.Indestructible = false;
            engineer_brokenJunctionBox.Condition = 0f;

            SetDoorAccess(engineer_thirdDoor, engineer_thirdDoorLight, false);

            // Room 5
            engineer_disconnectedJunctionBoxes = new PowerTransfer[4];
            engineer_disconnectedConnectionPanels = new ConnectionPanel[4];

            for (int i = 0; i < engineer_disconnectedJunctionBoxes.Length; i++)
            {
                engineer_disconnectedJunctionBoxes[i] = Item.ItemList.Find(item => item.HasTag($"engineer_disconnectedjunctionbox_{i + 1}")).GetComponent<PowerTransfer>();
                engineer_disconnectedConnectionPanels[i] = engineer_disconnectedJunctionBoxes[i].Item.GetComponent<ConnectionPanel>();
                engineer_disconnectedConnectionPanels[i].Locked = false;

                for (int j = 0; j < engineer_disconnectedJunctionBoxes[i].PowerConnections.Count; j++)
                {
                    foreach (Wire wire in engineer_disconnectedJunctionBoxes[i].PowerConnections[j].Wires)
                    {
                        if (wire == null) continue;
                        wire.Locked = true;
                    }
                }
            }

            engineer_wire_1 = Item.ItemList.Find(i => i.HasTag("engineer_wire_1"));
            engineer_wire_1.SpriteColor = Color.Transparent;
            engineer_wire_2 = Item.ItemList.Find(i => i.HasTag("engineer_wire_2"));
            engineer_wire_2.SpriteColor = Color.Transparent;
            engineer_lamp_1 = Item.ItemList.Find(i => i.HasTag("engineer_lamp_1")).GetComponent<Powered>();
            engineer_lamp_2 = Item.ItemList.Find(i => i.HasTag("engineer_lamp_2")).GetComponent<Powered>();
            engineer_fourthDoor = Item.ItemList.Find(i => i.HasTag("engineer_fourthdoor")).GetComponent<Door>();
            engineer_fourthDoorLight = Item.ItemList.Find(i => i.HasTag("engineer_fourthdoorlight")).GetComponent<LightComponent>();
            SetDoorAccess(engineer_fourthDoor, engineer_fourthDoorLight, false);

            // Room 6
            engineer_workingPump = Item.ItemList.Find(i => i.HasTag("engineer_workingpump")).GetComponent<Pump>();
            engineer_workingPump.Item.CurrentHull.WaterVolume += engineer_workingPump.Item.CurrentHull.Volume;
            engineer_workingPump.IsActive = true;
            tutorial_lockedDoor_1 = Item.ItemList.Find(i => i.HasTag("tutorial_lockeddoor_1")).GetComponent<Door>();
            SetDoorAccess(tutorial_lockedDoor_1, null, true);

            // Submarine
            tutorial_submarineDoor = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoor")).GetComponent<Door>();
            tutorial_submarineDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoorlight")).GetComponent<LightComponent>();
            SetDoorAccess(tutorial_submarineDoor, tutorial_submarineDoorLight, true);

            tutorial_enteredSubmarineSensor = Item.ItemList.Find(i => i.HasTag("tutorial_enteredsubmarinesensor")).GetComponent<MotionSensor>();
            engineer_submarineJunctionBox_1 = Item.ItemList.Find(i => i.HasTag("engineer_submarinejunctionbox_1"));
            engineer_submarineJunctionBox_2 = Item.ItemList.Find(i => i.HasTag("engineer_submarinejunctionbox_2"));
            engineer_submarineJunctionBox_3 = Item.ItemList.Find(i => i.HasTag("engineer_submarinejunctionbox_3"));
            engineer_submarineReactor = Item.ItemList.Find(i => i.HasTag("engineer_submarinereactor")).GetComponent<Reactor>();
            engineer_submarineReactor.PowerOn = true;
            engineer_submarineReactor.IsActive = engineer_submarineReactor.AutoTemp = false;

            engineer_submarineJunctionBox_1.Indestructible = false;
            engineer_submarineJunctionBox_1.Condition = 0f;
            engineer_submarineJunctionBox_2.Indestructible = false;
            engineer_submarineJunctionBox_2.Condition = 0f;
            engineer_submarineJunctionBox_3.Indestructible = false;
            engineer_submarineJunctionBox_3.Condition = 0f;
        }

        public override IEnumerable<CoroutineStatus> UpdateState()
        {
            while (GameMain.Instance.LoadingScreenOpen) { yield return null; }

            // Room 1
            SoundPlayer.PlayDamageSound("StructureBlunt", 10, Character.Controlled.WorldPosition);
            while (shakeTimer > 0.0f) // Wake up, shake
            {
                shakeTimer -= 0.1f;
                GameMain.GameScreen.Cam.Shake = shakeAmount;
                yield return new WaitForSeconds(0.1f, false);
            }

            //// Remove
            //for (int i = 0; i < engineer_disconnectedJunctionBoxes.Length; i++)
            //{
            //    SetHighlight(engineer_disconnectedJunctionBoxes[i].Item, true);
            //}
            //do { CheckGhostWires(); HandleJunctionBoxWiringHighlights(); yield return null; } while (engineer_workingPump.Voltage < engineer_workingPump.MinVoltage); // Wait until connected all the way to the pump
            //CheckGhostWires();
            //// Remove

            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.WakeUp"), ChatMessageType.Radio, null);
            SetHighlight(engineer_equipmentCabinet.Item, true);

            // Room 2
            do { yield return null; } while (!engineer_equipmentObjectiveSensor.MotionDetected);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.Equipment"), ChatMessageType.Radio, null);
            yield return new WaitForSeconds(0.5f, false);
            TriggerTutorialSegment(0, GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Select), GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Deselect)); // Retrieve equipment
            bool firstSlotRemoved = false;
            bool secondSlotRemoved = false;
            bool thirdSlotRemoved = false;
            bool fourthSlotRemoved = false;
            do
            {
                if (IsSelectedItem(engineer_equipmentCabinet.Item))
                {
                    if (!firstSlotRemoved)
                    {
                        HighlightInventorySlot(engineer_equipmentCabinet.Inventory, 0, highlightColor, .5f, .5f, 0f);
                        if (engineer_equipmentCabinet.Inventory.GetItemAt(0) == null) { firstSlotRemoved = true; }
                    }

                    if (!secondSlotRemoved)
                    {
                        HighlightInventorySlot(engineer_equipmentCabinet.Inventory, 1, highlightColor, .5f, .5f, 0f);
                        if (engineer_equipmentCabinet.Inventory.GetItemAt(1) == null) { secondSlotRemoved = true; }
                    }

                    if (!thirdSlotRemoved)
                    {
                        HighlightInventorySlot(engineer_equipmentCabinet.Inventory, 2, highlightColor, .5f, .5f, 0f);
                        if (engineer_equipmentCabinet.Inventory.GetItemAt(2) == null) { thirdSlotRemoved = true; }
                    }

                    if (!fourthSlotRemoved)
                    {
                        HighlightInventorySlot(engineer_equipmentCabinet.Inventory, 3, highlightColor, .5f, .5f, 0f);
                        if (engineer_equipmentCabinet.Inventory.GetItemAt(2) == null) { fourthSlotRemoved = true; }
                    }

                    for (int i = 0; i < engineer.Inventory.visualSlots.Length; i++)
                    {
                        if (engineer.Inventory.GetItemAt(i) == null) { HighlightInventorySlot(engineer.Inventory, i, highlightColor, .5f, .5f, 0f); }
                    }
                }

                yield return null;
            } while (!engineer_equipmentCabinet.Inventory.IsEmpty()); // Wait until looted
            RemoveCompletedObjective(0);
            SetHighlight(engineer_equipmentCabinet.Item, false);
            SetHighlight(engineer_reactor.Item, true);
            SetDoorAccess(engineer_firstDoor, engineer_firstDoorLight, true);

            // Room 3
            do { yield return null; } while (!IsSelectedItem(engineer_reactor.Item));
            yield return new WaitForSeconds(0.5f, false);
            TriggerTutorialSegment(1);
            do
            {
                if (IsSelectedItem(engineer_reactor.Item))
                {
                    engineer_reactor.AutoTemp = false;
                    if (engineer_reactor.PowerButton.FlashTimer <= 0)
                    {
                        engineer_reactor.PowerButton.Flash(highlightColor, 1.5f, false);
                    }
                }
                yield return null;
            } while (!engineer_reactor.PowerOn);
            do
            {
                if (IsSelectedItem(engineer_reactor.Item) && engineer_reactor.Item.OwnInventory.visualSlots != null)
                {
                    engineer_reactor.AutoTemp = false;
                    HighlightInventorySlot(engineer.Inventory, "fuelrod".ToIdentifier(), highlightColor, 0.5f, 0.5f, 0f);

                    for (int i = 0; i < engineer_reactor.Item.OwnInventory.visualSlots.Length; i++)
                    {
                        HighlightInventorySlot(engineer_reactor.Item.OwnInventory, i, highlightColor, 0.5f, 0.5f, 0f);
                    }
                }
                yield return null;
            } while (engineer_reactor.AvailableFuel == 0);
            RemoveCompletedObjective(1);
            TriggerTutorialSegment(2);
            CoroutineManager.StartCoroutine(ReactorOperatedProperly());
            do
            {
                if (IsSelectedItem(engineer_reactor.Item))
                {
                    engineer_reactor.AutoTemp = false;
                    if (engineer_reactor.FissionRateScrollBar.FlashTimer <= 0)
                    {
                        engineer_reactor.FissionRateScrollBar.Flash(highlightColor, 1.5f);
                    }

                    if (engineer_reactor.TurbineOutputScrollBar.FlashTimer <= 0)
                    {
                        engineer_reactor.TurbineOutputScrollBar.Flash(highlightColor, 1.5f);
                    }
                }
                yield return null;
            } while (!reactorOperatedProperly);
            yield return new WaitForSeconds(2f, false);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.ReactorStable"), ChatMessageType.Radio, null);
            do
            {
                if (IsSelectedItem(engineer_reactor.Item))
                {
                    if (engineer_reactor.AutoTempSwitch.FlashTimer <= 0)
                    {
                        engineer_reactor.AutoTempSwitch.Flash(highlightColor, 1.5f, false, false, new Vector2(10, 10));
                    }
                }
                yield return null;
            } while (!engineer_reactor.AutoTemp);

            float wait = 1.5f;
            do
            {
                yield return new WaitForSeconds(0.1f, false);
                wait -= 0.1f;
                engineer_reactor.AutoTemp = true;
            } while (wait > 0.0f);
            engineer.SelectedConstruction = null;
            engineer_reactor.CanBeSelected = false;
            RemoveCompletedObjective(2);
            SetHighlight(engineer_reactor.Item, false);
            SetHighlight(engineer_brokenJunctionBox, true);
            SetDoorAccess(engineer_secondDoor, engineer_secondDoorLight, true);

            // Room 4
            do { yield return null; } while (!engineer_secondDoor.IsOpen);
            yield return new WaitForSeconds(1f, false);
            Repairable repairableJunctionBoxComponent = engineer_brokenJunctionBox.GetComponent<Repairable>();
            TriggerTutorialSegment(3, GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Select)); // Repair the junction box
            do
            {
                if (!engineer.HasEquippedItem("screwdriver".ToIdentifier()))
                {
                    HighlightInventorySlot(engineer.Inventory, "screwdriver".ToIdentifier(), highlightColor, .5f, .5f, 0f);
                }
                else if (IsSelectedItem(engineer_brokenJunctionBox) && repairableJunctionBoxComponent.CurrentFixer == null)
                {
                    if (repairableJunctionBoxComponent.RepairButton.FlashTimer <= 0)
                    {
                        repairableJunctionBoxComponent.RepairButton.Flash();
                    }
                }
                yield return null;
            } while (repairableJunctionBoxComponent.IsBelowRepairThreshold); // Wait until repaired
            SetHighlight(engineer_brokenJunctionBox, false);
            RemoveCompletedObjective(3);
            SetDoorAccess(engineer_thirdDoor, engineer_thirdDoorLight, true);
            for (int i = 0; i < engineer_disconnectedJunctionBoxes.Length; i++)
            {
                SetHighlight(engineer_disconnectedJunctionBoxes[i].Item, true);
            }

            // Room 5
            do { yield return null; } while (!engineer_thirdDoor.IsOpen);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.FaultyWiring"), ChatMessageType.Radio, null);
            yield return new WaitForSeconds(2f, false);
            TriggerTutorialSegment(4, GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Use), GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Deselect)); // Connect the junction boxes
            do { CheckGhostWires(); HandleJunctionBoxWiringHighlights(); yield return null; } while (engineer_workingPump.Voltage < engineer_workingPump.MinVoltage); // Wait until connected all the way to the pump
            CheckGhostWires();
            for (int i = 0; i < engineer_disconnectedJunctionBoxes.Length; i++)
            {
                SetHighlight(engineer_disconnectedJunctionBoxes[i].Item, false);
            }
            RemoveCompletedObjective(4);
            do { yield return null; } while (engineer_workingPump.Item.CurrentHull.WaterPercentage > waterVolumeBeforeOpening); // Wait until drained
            wiringActive = false;
            SetDoorAccess(engineer_fourthDoor, engineer_fourthDoorLight, true);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.ChangeOfPlans"), ChatMessageType.Radio, null);

            // Submarine
            do { yield return null; } while (!tutorial_enteredSubmarineSensor.MotionDetected);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.Submarine"), ChatMessageType.Radio, null);
            yield return new WaitForSeconds(2f, false);
            TriggerTutorialSegment(5); // Repair junction box
            while (ContentRunning) yield return null;
            engineer.AddActiveObjectiveEntity(engineer_submarineJunctionBox_1, engineer_repairIcon, engineer_repairIconColor);
            engineer.AddActiveObjectiveEntity(engineer_submarineJunctionBox_2, engineer_repairIcon, engineer_repairIconColor);
            engineer.AddActiveObjectiveEntity(engineer_submarineJunctionBox_3, engineer_repairIcon, engineer_repairIconColor);
            SetHighlight(engineer_submarineJunctionBox_1, true);
            SetHighlight(engineer_submarineJunctionBox_2, true);
            SetHighlight(engineer_submarineJunctionBox_3, true);

            Repairable repairableJunctionBoxComponent1 = engineer_submarineJunctionBox_1.GetComponent<Repairable>();
            Repairable repairableJunctionBoxComponent2 = engineer_submarineJunctionBox_2.GetComponent<Repairable>();
            Repairable repairableJunctionBoxComponent3 = engineer_submarineJunctionBox_3.GetComponent<Repairable>();

            // Remove highlights when each individual machine is repaired
            do { CheckJunctionBoxHighlights(repairableJunctionBoxComponent1, repairableJunctionBoxComponent2, repairableJunctionBoxComponent3); yield return null; } while (repairableJunctionBoxComponent1.IsBelowRepairThreshold || repairableJunctionBoxComponent2.IsBelowRepairThreshold || repairableJunctionBoxComponent3.IsBelowRepairThreshold);
            CheckJunctionBoxHighlights(repairableJunctionBoxComponent1, repairableJunctionBoxComponent2, repairableJunctionBoxComponent3);
            RemoveCompletedObjective(5);
            yield return new WaitForSeconds(2f, false);

            TriggerTutorialSegment(6); // Powerup reactor
            SetHighlight(engineer_submarineReactor.Item, true);
            engineer.AddActiveObjectiveEntity(engineer_submarineReactor.Item, engineer_reactorIcon, engineer_reactorIconColor);
            do { yield return null; } while (!IsReactorPoweredUp(engineer_submarineReactor)); // Wait until ~matches load
            engineer.RemoveActiveObjectiveEntity(engineer_submarineReactor.Item);
            SetHighlight(engineer_submarineReactor.Item, false);
            RemoveCompletedObjective(6);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Engineer.Radio.Complete"), ChatMessageType.Radio, null);

            yield return new WaitForSeconds(4f, false);

            CoroutineManager.StartCoroutine(TutorialCompleted());
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (wiringActive)
            {
                for (int i = 0; i < engineer_disconnectedJunctionBoxes.Length; i++)
                {
                    for (int j = 0; j < engineer_disconnectedJunctionBoxes[i].PowerConnections.Count; j++)
                    {
                        engineer_disconnectedJunctionBoxes[i].PowerConnections[j].UpdateFlashTimer(deltaTime);
                    }
                }
            }
        }

        private bool IsSelectedItem(Item item)
        {
            return engineer?.SelectedConstruction == item;
        }

        private IEnumerable<CoroutineStatus> ReactorOperatedProperly()
        {
            float timer;

            for (int i = 0; i < reactorLoads.Length; i++)
            {
                timer = reactorLoadChangeTime;
                tutorial_oxygenGenerator.PowerConsumption = reactorLoads[i];
                while (timer > 0)
                {
                    yield return CoroutineStatus.Running;
                    if (CoroutineManager.DeltaTime > 0.0f && IsReactorPoweredUp(engineer_reactor))
                    {
                        timer -= CoroutineManager.DeltaTime;
                    }                   
                }
            }

            reactorOperatedProperly = true;
        }

        private void CheckGhostWires()
        {
            Color wireColor = 
                Color.Orange *
                    MathHelper.Lerp(0.25f, 0.75f, (float)(Math.Sin((Timing.TotalTime * 4.0f)) + 1.0f) / 2.0f);

            if (engineer_wire_1 != null)
            {
                engineer_wire_1.SpriteColor = wireColor;
                if (engineer_lamp_1.Voltage > engineer_lamp_1.MinVoltage)
                {
                    engineer_wire_1.Remove();
                    engineer_wire_1 = null;
                }
            }


            if (engineer_wire_2 != null)
            {
                engineer_wire_2.SpriteColor = wireColor;
                if (engineer_lamp_2.Voltage > engineer_lamp_2.MinVoltage)
                {
                    engineer_wire_2.Remove();
                    engineer_wire_2 = null;
                }
            }

        }

        private void HandleJunctionBoxWiringHighlights()
        {
            Item selected = engineer.SelectedConstruction;

            if (!engineer.HasEquippedItem("screwdriver".ToIdentifier()))
            {
                HighlightInventorySlot(engineer.Inventory, "screwdriver".ToIdentifier(), highlightColor, 0.5f, 0.5f, 0f);
            }

            int selectedIndex = -1;

            if (selected != null)
            {
                for (int i = 0; i < engineer_disconnectedJunctionBoxes.Length; i++)
                {
                    if (selected == engineer_disconnectedJunctionBoxes[i].Item)
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            wiringActive = selectedIndex != -1;

            if (!engineer.HasEquippedItem("wire".ToIdentifier()))
            {
                HighlightInventorySlotWithTag(engineer.Inventory, "wire".ToIdentifier(), highlightColor, 0.5f, 0.5f, 0f);
            }
            else
            {
                if (!wiringActive) return;
                for (int i = 0; i < engineer_disconnectedConnectionPanels[selectedIndex].Connections.Count; i++)
                {
                    var connection = engineer_disconnectedConnectionPanels[selectedIndex].Connections[i];
                    if (connection.IsPower && connection.FlashTimer <= 0)
                    {
                        foreach (Wire wire in engineer_disconnectedConnectionPanels[selectedIndex].Connections[i].Wires)
                        {
                            if (wire == null) continue;
                            if (!wire.Locked)
                            {
                                return;
                            }
                        }

                        connection.Flash(highlightColor);
                    }
                }
            }
        }

        private void CheckJunctionBoxHighlights(Repairable comp1, Repairable comp2, Repairable comp3)
        {
            if (!comp1.IsBelowRepairThreshold && engineer_submarineJunctionBox_1.ExternalHighlight)
            {
                SetHighlight(engineer_submarineJunctionBox_1, false);
                engineer.RemoveActiveObjectiveEntity(engineer_submarineJunctionBox_1);
            }
            if (!comp2.IsBelowRepairThreshold && engineer_submarineJunctionBox_2.ExternalHighlight)
            {
                SetHighlight(engineer_submarineJunctionBox_2, false);
                engineer.RemoveActiveObjectiveEntity(engineer_submarineJunctionBox_2);
            }
            if (!comp3.IsBelowRepairThreshold && engineer_submarineJunctionBox_3.ExternalHighlight)
            {
                SetHighlight(engineer_submarineJunctionBox_3, false);
                engineer.RemoveActiveObjectiveEntity(engineer_submarineJunctionBox_3);
            }
        }

        private bool IsReactorPoweredUp(Reactor reactor)
        {
            float load = 0.0f;
            List<Connection> connections = reactor.Item.Connections;
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

            return Math.Abs(load + reactor.CurrPowerConsumption) < reactorLoadError;
        }
    }
}
