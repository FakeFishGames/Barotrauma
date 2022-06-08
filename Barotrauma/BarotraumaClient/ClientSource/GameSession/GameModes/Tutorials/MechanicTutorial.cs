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
        private Door tutorial_upperFinalDoor;
        private Steering tutorial_submarineSteering;

        // Room 1
        private float shakeTimer = 1f;
        private float shakeAmount = 20f;
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
        private ItemContainer mechanic_craftingCabinet;
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
        private Door tutorial_mechanicFinalDoor;
        private LightComponent tutorial_mechanicFinalDoorLight;

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
        private LocalizedString radioSpeakerName;
        private Character mechanic;
        private Sprite mechanic_repairIcon;
        private Color mechanic_repairIconColor;
        private Sprite mechanic_weldIcon;

        public MechanicTutorial() : base("tutorial.mechanictraining".ToIdentifier(),
            new Segment(
                "Mechanic.OpenDoor".ToIdentifier(),
                "Mechanic.OpenDoorObjective".ToIdentifier(),
                TutorialContentType.TextOnly,
                textContent: new Segment.Text { Tag = "Mechanic.OpenDoorText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center }),
            new Segment(
                "Mechanic.Equipment".ToIdentifier(),
                "Mechanic.EquipmentObjective".ToIdentifier(),
                TutorialContentType.ManualVideo,
                textContent: new Segment.Text { Tag = "Mechanic.EquipmentText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center },
                videoContent: new Segment.Video { File = "tutorial_inventory.webm", TextTag = "Mechanic.EquipmentText".ToIdentifier(), Width = 450, Height = 80 }),
            new Segment(
                "Mechanic.Welding".ToIdentifier(),
                "Mechanic.WeldingObjective".ToIdentifier(),
                TutorialContentType.ManualVideo,
                textContent: new Segment.Text { Tag = "Mechanic.WeldingText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center },
                videoContent: new Segment.Video { File = "tutorial_equip.webm", TextTag = "Mechanic.WeldingText".ToIdentifier(), Width = 450, Height = 80 }),
            new Segment(
                "Mechanic.Drain".ToIdentifier(),
                "Mechanic.DrainObjective".ToIdentifier(),
                TutorialContentType.TextOnly,
                textContent: new Segment.Text { Tag = "Mechanic.DrainText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center }),
            new Segment(
                "Mechanic.Deconstruct".ToIdentifier(),
                "Mechanic.DeconstructObjective".ToIdentifier(),
                TutorialContentType.ManualVideo,
                textContent: new Segment.Text { Tag = "Mechanic.DeconstructText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center },
                videoContent: new Segment.Video { File = "tutorial_deconstruct.webm", TextTag = "Mechanic.DeconstructText".ToIdentifier(), Width = 450, Height = 80 }),
            new Segment(
                "Mechanic.Fabricate".ToIdentifier(),
                "Mechanic.FabricateObjective".ToIdentifier(),
                TutorialContentType.ManualVideo,
                textContent: new Segment.Text { Tag = "Mechanic.FabricateText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center },
                videoContent: new Segment.Video { File = "tutorial_fabricate.webm", TextTag = "Mechanic.FabricateText".ToIdentifier(), Width = 450, Height = 80 }),
            new Segment(
                "Mechanic.Extinguisher".ToIdentifier(),
                "Mechanic.ExtinguisherObjective".ToIdentifier(),
                TutorialContentType.TextOnly,
                textContent: new Segment.Text { Tag = "Mechanic.ExtinguisherText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center }),
            new Segment(
                "Mechanic.DropExtinguisher".ToIdentifier(),
                "Mechanic.DropExtinguisherObjective".ToIdentifier(),
                TutorialContentType.TextOnly,
                textContent: new Segment.Text { Tag = "Mechanic.DropExtinguisherText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center }),
            new Segment(
                "Mechanic.Diving".ToIdentifier(),
                "Mechanic.DivingObjective".ToIdentifier(),
                TutorialContentType.TextOnly,
                textContent: new Segment.Text { Tag = "Mechanic.DivingText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center }),
            new Segment(
                "Mechanic.RepairPump".ToIdentifier(),
                "Mechanic.RepairPumpObjective".ToIdentifier(),
                TutorialContentType.TextOnly,
                textContent: new Segment.Text { Tag = "Mechanic.RepairPumpText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center }),
            new Segment(
                "Mechanic.RepairSubmarine".ToIdentifier(),
                "Mechanic.RepairSubmarineObjective".ToIdentifier(),
                TutorialContentType.TextOnly,
                textContent: new Segment.Text { Tag = "Mechanic.RepairSubmarineText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center }),
            new Segment(
                "tutorial.laddertitle".ToIdentifier(),
                "tutorial.laddertitle".ToIdentifier(),
                TutorialContentType.TextOnly,
                textContent: new Segment.Text { Tag = "tutorial.ladderdescription".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center }))
        { }

        protected override CharacterInfo GetCharacterInfo()
        {
            return new CharacterInfo(
                CharacterPrefab.HumanSpeciesName,
                jobOrJobPrefab: new Job(
                    JobPrefab.Prefabs["mechanic"], Rand.RandSync.Unsynced, 0,
                    new Skill("medical".ToIdentifier(), 0),
                    new Skill("weapons".ToIdentifier(), 0),
                    new Skill("mechanical".ToIdentifier(), 50),
                    new Skill("electrical".ToIdentifier(), 20),
                    new Skill("helm".ToIdentifier(), 0)));
        }

        protected override void Initialize()
        {
            radioSpeakerName = TextManager.Get("Tutorial.Radio.Speaker");
            mechanic = Character.Controlled;

            foreach (Item item in mechanic.Inventory.AllItemsMod)
            {
                if (item.HasTag("clothing") || item.HasTag("identitycard") || item.HasTag("headset")) { continue; }
                item.Unequip(mechanic);
                mechanic.Inventory.RemoveItem(item);
            }

            var repairOrder = OrderPrefab.Prefabs["repairsystems"];
            mechanic_repairIcon = repairOrder.SymbolSprite;
            mechanic_repairIconColor = repairOrder.Color;
            mechanic_weldIcon = new Sprite("Content/UI/MainIconsAtlas.png", new Rectangle(1, 256, 127, 127), new Vector2(0.5f, 0.5f));

            // Other tutorial items
            tutorial_securityFinalDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_securityfinaldoorlight")).GetComponent<LightComponent>();
            tutorial_upperFinalDoor = Item.ItemList.Find(i => i.HasTag("tutorial_upperfinaldoor")).GetComponent<Door>();
            tutorial_submarineSteering = Item.ItemList.Find(i => i.HasTag("command")).GetComponent<Steering>();

            tutorial_submarineSteering.CanBeSelected = false;
            foreach (ItemComponent ic in tutorial_submarineSteering.Item.Components)
            {
                ic.CanBeSelected = false;
            }

            SetDoorAccess(null, tutorial_securityFinalDoorLight, false);
            SetDoorAccess(tutorial_upperFinalDoor, null, false);

            // Room 1
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
            //mechanic_ladderSensor = Item.ItemList.Find(i => i.HasTag("mechanic_laddersensor")).GetComponent<MotionSensor>();

            SetDoorAccess(mechanic_thirdDoor, mechanic_thirdDoorLight, false);
            mechanic_brokenWall_1.Indestructible = false;
            mechanic_brokenWall_1.SpriteColor = Color.White;
            for (int i = 0; i < mechanic_brokenWall_1.SectionCount; i++)
            {
                mechanic_brokenWall_1.AddDamage(i, 85);
            }
            mechanic_brokenhull_1 = mechanic_brokenWall_1.Sections[0].gap.FlowTargetHull;

            // Room 4
            mechanic_craftingObjectiveSensor = Item.ItemList.Find(i => i.HasTag("mechanic_craftingobjectivesensor")).GetComponent<MotionSensor>();
            mechanic_deconstructor = Item.ItemList.Find(i => i.HasTag("mechanic_deconstructor")).GetComponent<Deconstructor>();
            mechanic_fabricator = Item.ItemList.Find(i => i.HasTag("mechanic_fabricator")).GetComponent<Fabricator>();
            mechanic_craftingCabinet = Item.ItemList.Find(i => i.HasTag("mechanic_craftingcabinet")).GetComponent<ItemContainer>();
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
            foreach (Item item in mechanic_divingSuitContainer.Inventory.AllItems)
            {
                foreach (ItemComponent ic in item.Components)
                {
                    ic.CanBePicked = true;
                }
            }

            mechanic_oxygenContainer = Item.ItemList.Find(i => i.HasTag("mechanic_oxygencontainer")).GetComponent<ItemContainer>();
            foreach (Item item in mechanic_oxygenContainer.Inventory.AllItems)
            {
                foreach (ItemComponent ic in item.Components)
                {
                    ic.CanBePicked = true;
                }
            }

            tutorial_mechanicFinalDoor = Item.ItemList.Find(i => i.HasTag("tutorial_mechanicfinaldoor")).GetComponent<Door>();
            tutorial_mechanicFinalDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_mechanicfinaldoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(tutorial_mechanicFinalDoor, tutorial_mechanicFinalDoorLight, false);

            // Room 7
            mechanic_brokenPump = Item.ItemList.Find(i => i.HasTag("mechanic_brokenpump")).GetComponent<Pump>();
            mechanic_brokenPump.Item.Indestructible = false;
            mechanic_brokenPump.Item.Condition = 0;
            mechanic_brokenPump.CanBeSelected = false;
            mechanic_brokenPump.Item.GetComponent<Repairable>().CanBeSelected = false;
            mechanic_brokenWall_2 = Structure.WallList.Find(i => i.SpecialTag == "mechanic_brokenwall_2");
            tutorial_submarineDoor = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoor")).GetComponent<Door>();
            tutorial_submarineDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoorlight")).GetComponent<LightComponent>();

            mechanic_brokenWall_2.Indestructible = false;
            mechanic_brokenWall_2.SpriteColor = Color.White;
            for (int i = 0; i < mechanic_brokenWall_2.SectionCount; i++)
            {
                mechanic_brokenWall_2.AddDamage(i, 85);
            }
            mechanic_brokenhull_2 = mechanic_brokenWall_2.Sections[0].gap.FlowTargetHull;
            SetDoorAccess(tutorial_submarineDoor, tutorial_submarineDoorLight, false);

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

            GameAnalyticsManager.AddDesignEvent("Tutorial:MechanicTutorial:Started");
            GameAnalyticsManager.AddDesignEvent("Tutorial:Started");
        }

        public override void Update(float deltaTime)
        {
            if (mechanic_brokenhull_1 != null)
            {
                mechanic_brokenhull_1.WaterVolume = MathHelper.Clamp(mechanic_brokenhull_1.WaterVolume, 0, mechanic_brokenhull_1.Volume * 0.85f);
            }
            base.Update(deltaTime);
        }

        public override IEnumerable<CoroutineStatus> UpdateState()
        {
            while (GameMain.Instance.LoadingScreenOpen) yield return null;

            // Room 1
            SoundPlayer.PlayDamageSound("StructureBlunt", 10, Character.Controlled.WorldPosition);
            while (shakeTimer > 0.0f) // Wake up, shake
            {
                shakeTimer -= 0.1f;
                GameMain.GameScreen.Cam.Shake = shakeAmount;
                yield return new WaitForSeconds(0.1f, false);
            }
            yield return new WaitForSeconds(2.5f, false);

            mechanic_fabricator.RemoveFabricationRecipes(allowedIdentifiers:
                new[] { "extinguisher", "wrench", "weldingtool", "weldingfuel", "divingmask", "railgunshell", "nuclearshell", "uex", "harpoongun" }.ToIdentifiers());
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.WakeUp"), ChatMessageType.Radio, null);

            yield return new WaitForSeconds(2.5f, false);
            TriggerTutorialSegment(0, GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Up), GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Left), GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Down), GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Right), GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Select), GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Select)); // Open door objective
            yield return new WaitForSeconds(0.0f, false);
            SetDoorAccess(mechanic_firstDoor, mechanic_firstDoorLight, true);
            SetHighlight(mechanic_firstDoor.Item, true);
            do { yield return null; } while (!mechanic_firstDoor.IsOpen);
            SetHighlight(mechanic_firstDoor.Item, false);
            yield return new WaitForSeconds(1.5f, false);
            RemoveCompletedObjective(0);
            GameAnalyticsManager.AddDesignEvent("Tutorial:MechanicTutorial:Objective0");

            // Room 2
            yield return new WaitForSeconds(0.0f, false);
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.Equipment"), ChatMessageType.Radio, null);
            do { yield return null; } while (!mechanic_equipmentObjectiveSensor.MotionDetected);
            TriggerTutorialSegment(1, GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Select), GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Deselect)); // Equipment & inventory objective
            SetHighlight(mechanic_equipmentCabinet.Item, true);
            bool firstSlotRemoved = false;
            bool secondSlotRemoved = false;
            bool thirdSlotRemoved = false;
            do
            {
                if (IsSelectedItem(mechanic_equipmentCabinet.Item))
                {
                    if (!firstSlotRemoved)
                    {
                        HighlightInventorySlot(mechanic_equipmentCabinet.Inventory, 0, highlightColor, .5f, .5f, 0f);
                        if (mechanic_equipmentCabinet.Inventory.GetItemAt(0) == null) { firstSlotRemoved = true; }
                    }

                    if (!secondSlotRemoved)
                    {
                        HighlightInventorySlot(mechanic_equipmentCabinet.Inventory, 1, highlightColor, .5f, .5f, 0f);
                        if (mechanic_equipmentCabinet.Inventory.GetItemAt(1) == null) { secondSlotRemoved = true; }
                    }

                    if (!thirdSlotRemoved)
                    {
                        HighlightInventorySlot(mechanic_equipmentCabinet.Inventory, 2, highlightColor, .5f, .5f, 0f);
                        if (mechanic_equipmentCabinet.Inventory.GetItemAt(2) == null) { thirdSlotRemoved = true; }
                    }

                    for (int i = 0; i < mechanic.Inventory.Capacity; i++)
                    {
                        if (mechanic.Inventory.GetItemAt(i) == null) { HighlightInventorySlot(mechanic.Inventory, i, highlightColor, .5f, .5f, 0f); }
                    }
                }

                yield return null;
            } while (mechanic.Inventory.FindItemByIdentifier("divingmask".ToIdentifier()) == null || mechanic.Inventory.FindItemByIdentifier("weldingtool".ToIdentifier()) == null || mechanic.Inventory.FindItemByIdentifier("wrench".ToIdentifier()) == null); // Wait until looted
            SetHighlight(mechanic_equipmentCabinet.Item, false);
            yield return new WaitForSeconds(1.5f, false);
            RemoveCompletedObjective(1);
            GameAnalyticsManager.AddDesignEvent("Tutorial:MechanicTutorial:Objective1");
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.Breach"), ChatMessageType.Radio, null);

            // Room 3
            do { yield return null; } while (!mechanic_weldingObjectiveSensor.MotionDetected);
            TriggerTutorialSegment(2, GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Aim), GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Shoot)); // Welding objective
            do
            {
                if (!mechanic.HasEquippedItem("divingmask".ToIdentifier()))
                {
                    HighlightInventorySlot(mechanic.Inventory, "divingmask".ToIdentifier(), highlightColor, .5f, .5f, 0f);
                }

                if (!mechanic.HasEquippedItem("weldingtool".ToIdentifier()))
                {
                    HighlightInventorySlot(mechanic.Inventory, "weldingtool".ToIdentifier(), highlightColor, .5f, .5f, 0f);
                }
                yield return null;
            } while (!mechanic.HasEquippedItem("divingmask".ToIdentifier()) || !mechanic.HasEquippedItem("weldingtool".ToIdentifier())); // Wait until equipped
            SetDoorAccess(mechanic_secondDoor, mechanic_secondDoorLight, true);
            mechanic.AddActiveObjectiveEntity(mechanic_brokenWall_1, mechanic_weldIcon, mechanic_repairIconColor);
            do { yield return null; } while (WallHasDamagedSections(mechanic_brokenWall_1)); // Highlight until repaired
            mechanic.RemoveActiveObjectiveEntity(mechanic_brokenWall_1);
            RemoveCompletedObjective(2);
            GameAnalyticsManager.AddDesignEvent("Tutorial:MechanicTutorial:Objective2");

            yield return new WaitForSeconds(1f, false);
            TriggerTutorialSegment(3, GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Select)); // Pump objective
            SetHighlight(mechanic_workingPump.Item, true);
            do
            {
                yield return null;
                if (IsSelectedItem(mechanic_workingPump.Item))
                {
                    if (mechanic_workingPump.PowerButton.FlashTimer <= 0)
                    {
                        mechanic_workingPump.PowerButton.Flash(uiHighlightColor, 1.5f, true);
                    }
                }
            } while (mechanic_workingPump.FlowPercentage >= 0 || !mechanic_workingPump.IsActive); // Highlight until draining
            SetHighlight(mechanic_workingPump.Item, false);
            do { yield return null; } while (mechanic_brokenhull_1 != null && mechanic_brokenhull_1.WaterPercentage > waterVolumeBeforeOpening); // Unlock door once drained
            RemoveCompletedObjective(3);
            GameAnalyticsManager.AddDesignEvent("Tutorial:MechanicTutorial:Objective3");

            SetDoorAccess(mechanic_thirdDoor, mechanic_thirdDoorLight, true);
            //TriggerTutorialSegment(11, GameSettings.CurrentConfig.KeyMap.Bindings[InputType.Select], GameSettings.CurrentConfig.KeyMap.Bindings[InputType.Up], GameSettings.CurrentConfig.KeyMap.Bindings[InputType.Down], GameSettings.CurrentConfig.KeyMap.Bindings[InputType.Select]); // Ladder objective
            //do { yield return null; } while (!mechanic_ladderSensor.MotionDetected);
            //RemoveCompletedObjective(segments[11]);
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.News"), ChatMessageType.Radio, null);
            yield return new WaitForSeconds(1f, false);
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.Fire"), ChatMessageType.Radio, null);
            
            // Room 4
            do { yield return null; } while (!mechanic_thirdDoor.IsOpen);
            yield return new WaitForSeconds(1f, false);
            mechanic_fire = new DummyFireSource(new Vector2(20f, 2f), Item.ItemList.Find(i => i.HasTag("mechanic_fire")).WorldPosition);
            //do { yield return null; } while (!mechanic_craftingObjectiveSensor.MotionDetected);
            TriggerTutorialSegment(4); // Deconstruct

            SetHighlight(mechanic_craftingCabinet.Item, true);

            bool gotOxygenTank = false;
            bool gotSodium = false;
            do
            {
                if (mechanic.SelectedConstruction == mechanic_craftingCabinet.Item)
                {
                    for (int i = 0; i < mechanic.Inventory.Capacity; i++)
                    {
                        if (mechanic.Inventory.GetItemAt(i) == null) { HighlightInventorySlot(mechanic.Inventory, i, highlightColor, .5f, .5f, 0f); }
                    }

                    if (mechanic.Inventory.FindItemByIdentifier("oxygentank".ToIdentifier()) == null && mechanic.Inventory.FindItemByIdentifier("aluminium".ToIdentifier()) == null)
                    {
                        for (int i = 0; i < mechanic_craftingCabinet.Capacity; i++)
                        {
                            Item item = mechanic_craftingCabinet.Inventory.GetItemAt(i);
                            if (item != null && item.Prefab.Identifier == "oxygentank")
                            {
                                HighlightInventorySlot(mechanic_craftingCabinet.Inventory, i, highlightColor, .5f, .5f, 0f);
                            }
                        }
                    }

                    if (mechanic.Inventory.FindItemByIdentifier("sodium".ToIdentifier()) == null)
                    {
                        for (int i = 0; i < mechanic_craftingCabinet.Inventory.Capacity; i++)
                        {
                            Item item = mechanic_craftingCabinet.Inventory.GetItemAt(i);
                            if (item != null && item.Prefab.Identifier == "sodium")
                            {
                                HighlightInventorySlot(mechanic_craftingCabinet.Inventory, i, highlightColor, .5f, .5f, 0f);
                            }
                        }
                    }
                }

                if (!gotOxygenTank && (mechanic.Inventory.FindItemByIdentifier("oxygentank".ToIdentifier()) != null ||
                                       mechanic_deconstructor.InputContainer.Inventory.FindItemByIdentifier("oxygentank".ToIdentifier()) != null))
                {
                    gotOxygenTank = true;
                }
                if (!gotSodium && mechanic.Inventory.FindItemByIdentifier("sodium".ToIdentifier()) != null)
                {
                    gotSodium = true;
                }
                yield return null;
            } while (!gotOxygenTank || !gotSodium); // Wait until looted

            yield return new WaitForSeconds(1.0f, false);
            SetHighlight(mechanic_craftingCabinet.Item, false);
            SetHighlight(mechanic_deconstructor.Item, true);
            do
            {
                if (IsSelectedItem(mechanic_deconstructor.Item))
                {
                    if (mechanic_deconstructor.OutputContainer.Inventory.FindItemByIdentifier("aluminium".ToIdentifier()) != null)
                    {
                        HighlightInventorySlot(mechanic_deconstructor.OutputContainer.Inventory, "aluminium".ToIdentifier(), highlightColor, .5f, .5f, 0f);

                        for (int i = 0; i < mechanic.Inventory.Capacity; i++)
                        {
                            if (mechanic.Inventory.GetItemAt(i) == null) { HighlightInventorySlot(mechanic.Inventory, i, highlightColor, .5f, .5f, 0f); }
                        }
                    }
                    else
                    {
                        if (mechanic.Inventory.FindItemByIdentifier("oxygentank".ToIdentifier()) != null && mechanic_deconstructor.InputContainer.Inventory.FindItemByIdentifier("oxygentank".ToIdentifier()) == null)
                        {
                            HighlightInventorySlot(mechanic.Inventory, "oxygentank".ToIdentifier(), highlightColor, .5f, .5f, 0f);
                            for (int i = 0; i < mechanic_deconstructor.InputContainer.Inventory.Capacity; i++)
                            {
                                HighlightInventorySlot(mechanic_deconstructor.InputContainer.Inventory, i, highlightColor, .5f, .5f, 0f);
                            }                            
                        }

                        if (mechanic_deconstructor.InputContainer.Inventory.FindItemByIdentifier("oxygentank".ToIdentifier()) != null && !mechanic_deconstructor.IsActive)
                        {
                            if (mechanic_deconstructor.ActivateButton.FlashTimer <= 0)
                            {
                                mechanic_deconstructor.ActivateButton.Flash(highlightColor, 1.5f, false);
                            }
                        }
                    }
                }
                yield return null;
            } while (
                mechanic.Inventory.FindItemByIdentifier("aluminium".ToIdentifier()) == null && 
                mechanic_fabricator.InputContainer.Inventory.FindItemByIdentifier("aluminium".ToIdentifier()) == null); // Wait until aluminium obtained

                SetHighlight(mechanic_deconstructor.Item, false);
            RemoveCompletedObjective(4);
            GameAnalyticsManager.AddDesignEvent("Tutorial:MechanicTutorial:Objective4");

            yield return new WaitForSeconds(1f, false);
            TriggerTutorialSegment(5); // Fabricate
            SetHighlight(mechanic_fabricator.Item, true);
            do
            {
                if (IsSelectedItem(mechanic_fabricator.Item))
                {
                    if (mechanic_fabricator.SelectedItem?.TargetItem.Identifier != "extinguisher")
                    {
                        mechanic_fabricator.HighlightRecipe("extinguisher", highlightColor);
                    }
                    else
                    {
                        if (mechanic_fabricator.OutputContainer.Inventory.FindItemByIdentifier("extinguisher".ToIdentifier()) != null)
                        {
                            HighlightInventorySlot(mechanic_fabricator.OutputContainer.Inventory, "extinguisher".ToIdentifier(), highlightColor, .5f, .5f, 0f);

                            /*for (int i = 0; i < mechanic.Inventory.Capacity; i++)
                            {
                                if (mechanic.Inventory.Items[i] == null) HighlightInventorySlot(mechanic.Inventory, i, highlightColor, .5f, .5f, 0f);
                            }*/
                        }
                        else if (mechanic_fabricator.InputContainer.Inventory.FindItemByIdentifier("aluminium".ToIdentifier()) != null && mechanic_fabricator.InputContainer.Inventory.FindItemByIdentifier("sodium".ToIdentifier()) != null && !mechanic_fabricator.IsActive)
                        {
                            if (mechanic_fabricator.ActivateButton.FlashTimer <= 0)
                            {
                                mechanic_fabricator.ActivateButton.Flash(highlightColor, 1.5f, false);
                            }
                        }
                        else if (mechanic.Inventory.FindItemByIdentifier("aluminium".ToIdentifier()) != null || mechanic.Inventory.FindItemByIdentifier("sodium".ToIdentifier()) != null)
                        {
                            HighlightInventorySlot(mechanic.Inventory, "aluminium".ToIdentifier(), highlightColor, .5f, .5f, 0f);
                            HighlightInventorySlot(mechanic.Inventory, "sodium".ToIdentifier(), highlightColor, .5f, .5f, 0f);

                            if (mechanic_fabricator.InputContainer.Inventory.GetItemAt(0) == null)
                            {
                                HighlightInventorySlot(mechanic_fabricator.InputContainer.Inventory, 0, highlightColor, .5f, .5f, 0f);
                            }

                            if (mechanic_fabricator.InputContainer.Inventory.GetItemAt(1) == null)
                            {
                                HighlightInventorySlot(mechanic_fabricator.InputContainer.Inventory, 1, highlightColor, .5f, .5f, 0f);
                            }
                        }
                    }                   
                }
                yield return null;
            } while (mechanic.Inventory.FindItemByIdentifier("extinguisher".ToIdentifier()) == null); // Wait until extinguisher is created
            RemoveCompletedObjective(5);
            GameAnalyticsManager.AddDesignEvent("Tutorial:MechanicTutorial:Objective5");
            SetHighlight(mechanic_fabricator.Item, false);
            SetDoorAccess(mechanic_fourthDoor, mechanic_fourthDoorLight, true);

            // Room 5
            do { yield return null; } while (!mechanic_fireSensor.MotionDetected);
            TriggerTutorialSegment(6, GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Aim), GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Shoot)); // Using the extinguisher
            do { yield return null; } while (!mechanic_fire.Removed); // Wait until extinguished
            yield return new WaitForSeconds(3f, false);
            RemoveCompletedObjective(6);
            GameAnalyticsManager.AddDesignEvent("Tutorial:MechanicTutorial:Objective6");

            if (mechanic.HasEquippedItem("extinguisher".ToIdentifier())) // do not trigger if dropped already
            {
                TriggerTutorialSegment(7);
                do
                {
                    HighlightInventorySlot(mechanic.Inventory, "extinguisher".ToIdentifier(), highlightColor, 0.5f, 0.5f, 0f);
                    yield return null;
                } while (mechanic.HasEquippedItem("extinguisher".ToIdentifier()));
                RemoveCompletedObjective(7);
                GameAnalyticsManager.AddDesignEvent("Tutorial:MechanicTutorial:Objective7");
            }
            SetDoorAccess(mechanic_fifthDoor, mechanic_fifthDoorLight, true);

            // Room 6
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.Diving"), ChatMessageType.Radio, null);
            do { yield return null; } while (!mechanic_divingSuitObjectiveSensor.MotionDetected);
            TriggerTutorialSegment(8); // Dangers of pressure, equip diving suit objective
            SetHighlight(mechanic_divingSuitContainer.Item, true);
            do
            {
                if (IsSelectedItem(mechanic_divingSuitContainer.Item))
                {
                    if (mechanic_divingSuitContainer.Inventory.visualSlots != null)
                    {
                        for (int i = 0; i < mechanic_divingSuitContainer.Inventory.Capacity; i++)
                        {
                            HighlightInventorySlot(mechanic_divingSuitContainer.Inventory, i, highlightColor, 0.5f, 0.5f, 0f);
                        }
                    }
                }
                yield return null;
            } while (!mechanic.HasEquippedItem("divingsuit".ToIdentifier(), slotType: InvSlotType.OuterClothes));
            SetHighlight(mechanic_divingSuitContainer.Item, false);
            RemoveCompletedObjective(8);
            GameAnalyticsManager.AddDesignEvent("Tutorial:MechanicTutorial:Objective8");
            SetDoorAccess(tutorial_mechanicFinalDoor, tutorial_mechanicFinalDoorLight, true);

            // Room 7
            mechanic.AddActiveObjectiveEntity(mechanic_brokenWall_2, mechanic_weldIcon, mechanic_repairIconColor);
            do { yield return null; } while (WallHasDamagedSections(mechanic_brokenWall_2));
            mechanic.RemoveActiveObjectiveEntity(mechanic_brokenWall_2);
            yield return new WaitForSeconds(2f, false);

            TriggerTutorialSegment(9, GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Use)); // Repairing machinery (pump)
            SetHighlight(mechanic_brokenPump.Item, true);
            mechanic_brokenPump.CanBeSelected = true;
            Repairable repairablePumpComponent = mechanic_brokenPump.Item.GetComponent<Repairable>();
            repairablePumpComponent.CanBeSelected = true;
            do
            {
                yield return null;
                if (repairablePumpComponent.IsBelowRepairThreshold)
                {
                    if (!mechanic.HasEquippedItem("wrench".ToIdentifier()))
                    {
                        HighlightInventorySlot(mechanic.Inventory, "wrench".ToIdentifier(), highlightColor, 0.5f, 0.5f, 0f);
                    }
                    else if (IsSelectedItem(mechanic_brokenPump.Item) && repairablePumpComponent.CurrentFixer == null)
                    {
                        if (repairablePumpComponent.RepairButton.FlashTimer <= 0)
                        {
                            repairablePumpComponent.RepairButton.Flash();
                        }
                    }
                }
                else
                {
                    if (IsSelectedItem(mechanic_brokenPump.Item))
                    {
                        if (mechanic_brokenPump.PowerButton.FlashTimer <= 0)
                        {
                            mechanic_brokenPump.PowerButton.Flash(uiHighlightColor, 1.5f, true);
                        }
                    }
                }
            } while (repairablePumpComponent.IsBelowRepairThreshold || mechanic_brokenPump.FlowPercentage >= 0 || !mechanic_brokenPump.IsActive);
            RemoveCompletedObjective(9);
            GameAnalyticsManager.AddDesignEvent("Tutorial:MechanicTutorial:Objective9");
            SetHighlight(mechanic_brokenPump.Item, false);
            do { yield return null; } while (mechanic_brokenhull_2.WaterPercentage > waterVolumeBeforeOpening);
            SetDoorAccess(tutorial_submarineDoor, tutorial_submarineDoorLight, true);

            // Submarine
            do { yield return null; } while (!tutorial_enteredSubmarineSensor.MotionDetected);
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.Submarine"), ChatMessageType.Radio, null);
            TriggerTutorialSegment(10); // Repairing ballast pumps, engine
            while (ContentRunning) yield return null;
            mechanic.AddActiveObjectiveEntity(mechanic_ballastPump_1.Item, mechanic_repairIcon, mechanic_repairIconColor);
            mechanic.AddActiveObjectiveEntity(mechanic_ballastPump_2.Item, mechanic_repairIcon, mechanic_repairIconColor);
            mechanic.AddActiveObjectiveEntity(mechanic_submarineEngine.Item, mechanic_repairIcon, mechanic_repairIconColor);
            SetHighlight(mechanic_ballastPump_1.Item, true);
            SetHighlight(mechanic_ballastPump_2.Item, true);
            SetHighlight(mechanic_submarineEngine.Item, true);

            Repairable repairablePumpComponent1 = mechanic_ballastPump_1.Item.GetComponent<Repairable>();
            Repairable repairablePumpComponent2 = mechanic_ballastPump_2.Item.GetComponent<Repairable>();
            Repairable repairableEngineComponent = mechanic_submarineEngine.Item.GetComponent<Repairable>();

            // Remove highlights when each individual machine is repaired
            do { CheckHighlights(repairablePumpComponent1, repairablePumpComponent2, repairableEngineComponent); yield return null; } while (repairablePumpComponent1.IsBelowRepairThreshold || repairablePumpComponent2.IsBelowRepairThreshold || repairableEngineComponent.IsBelowRepairThreshold);
            CheckHighlights(repairablePumpComponent1, repairablePumpComponent2, repairableEngineComponent);
            RemoveCompletedObjective(10);
            GameAnalyticsManager.AddDesignEvent("Tutorial:MechanicTutorial:Objective10");
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.Complete"), ChatMessageType.Radio, null);

            // END TUTORIAL
            GameAnalyticsManager.AddDesignEvent("Tutorial:MechanicTutorial:Completed");
            CoroutineManager.StartCoroutine(TutorialCompleted());
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

        private void CheckHighlights(Repairable comp1, Repairable comp2, Repairable comp3)
        {
            if (!comp1.IsBelowRepairThreshold && mechanic_ballastPump_1.Item.ExternalHighlight)
            {
                SetHighlight(mechanic_ballastPump_1.Item, false);
                mechanic.RemoveActiveObjectiveEntity(mechanic_ballastPump_1.Item);
            }
            if (!comp2.IsBelowRepairThreshold && mechanic_ballastPump_2.Item.ExternalHighlight)
            {
                SetHighlight(mechanic_ballastPump_2.Item, false);
                mechanic.RemoveActiveObjectiveEntity(mechanic_ballastPump_2.Item);
            }
            if (!comp3.IsBelowRepairThreshold && mechanic_submarineEngine.Item.ExternalHighlight)
            {
                SetHighlight(mechanic_submarineEngine.Item, false);
                mechanic.RemoveActiveObjectiveEntity(mechanic_submarineEngine.Item);
            }
        }
    }
}
