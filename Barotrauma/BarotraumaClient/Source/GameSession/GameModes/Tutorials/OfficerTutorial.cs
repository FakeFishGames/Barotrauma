using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma.Tutorials
{
    class OfficerTutorial : ScenarioTutorial
    {
        // Other tutorial items
        private LightComponent tutorial_mechanicFinalDoorLight;
        private Steering tutorial_submarineSteering;

        // Room 1
        private float shakeTimer = 3f;
        private float shakeAmount = 20f;
        private MotionSensor officer_equipmentObjectiveSensor;

        // Room 2
        private ItemContainer officer_equipmentCabinet;
        private Door officer_firstDoor;
        private LightComponent officer_firstDoorLight;

        // Room 3
        private MotionSensor officer_crawlerSensor;
        private Character officer_crawler;
        private Vector2 officer_crawlerSpawnPos;
        private Door officer_secondDoor;
        private LightComponent officer_secondDoorLight;

        // Room 4
        private MotionSensor officer_somethingBigSensor;
        private ItemContainer officer_coilgunLoader;
        private PowerContainer officer_superCapacitor;
        private Item officer_coilgunPeriscope;
        private Character officer_hammerhead;
        private Vector2 officer_hammerheadSpawnPos;
        private Door officer_thirdDoor;
        private LightComponent officer_thirdDoorLight;

        // Room 5
        private MotionSensor officer_rangedWeaponSensor;
        private ItemContainer officer_rangedWeaponCabinet;
        private Door officer_fourthDoor;
        private LightComponent officer_fourthDoorLight;

        // Room 6

        // Submarine
        private Door tutorial_submarineDoor;
        private LightComponent tutorial_submarineDoorLight;
        private MotionSensor tutorial_enteredSubmarineSensor;

        // Variables
        private string radioSpeakerName;
        private Character officer;
        private string crawlerCharacterFile;
        private string hammerheadCharacterFile;

        public OfficerTutorial(XElement element) : base(element)
        {
            IEnumerable<string> characterFiles = GameMain.Instance.GetFilesOfType(ContentType.Character);
            foreach (string characterFile in characterFiles)
            {
                if (Path.GetFileNameWithoutExtension(characterFile).ToLowerInvariant() == "crawler")
                {
                    crawlerCharacterFile = characterFile;
                    break;
                }
            }

            foreach (string characterFile in characterFiles)
            {
                if (Path.GetFileNameWithoutExtension(characterFile).ToLowerInvariant() == "hammerhead")
                {
                    hammerheadCharacterFile = characterFile;
                    break;
                }
            }
        }

        public override void Start()
        {
            base.Start();

            return;

            radioSpeakerName = TextManager.Get("Tutorial.Radio.Speaker");
            officer = Character.Controlled;

            // Other tutorial items
            tutorial_mechanicFinalDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_mechanicfinaldoorlight")).GetComponent<LightComponent>();
            tutorial_submarineSteering = Item.ItemList.Find(i => i.HasTag("command")).GetComponent<Steering>();

            tutorial_submarineSteering.CanBeSelected = false;
            foreach (ItemComponent ic in tutorial_submarineSteering.Item.Components)
            {
                ic.CanBeSelected = false;
            }

            SetDoorAccess(null, tutorial_mechanicFinalDoorLight, false);

            // Room 2
            officer_equipmentObjectiveSensor = Item.ItemList.Find(i => i.HasTag("officer_equipmentobjectivesensor")).GetComponent<MotionSensor>();
            officer_equipmentCabinet = Item.ItemList.Find(i => i.HasTag("officer_equipmentcabinet")).GetComponent<ItemContainer>();
            officer_firstDoor = Item.ItemList.Find(i => i.HasTag("officer_firstdoor")).GetComponent<Door>();
            officer_firstDoorLight = Item.ItemList.Find(i => i.HasTag("officer_firstdoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(officer_firstDoor, officer_firstDoorLight, false);

            // Room 3
            officer_crawlerSpawnPos = Item.ItemList.Find(i => i.HasTag("officer_crawlerspawn")).WorldPosition;
            officer_secondDoor = Item.ItemList.Find(i => i.HasTag("officer_seconddoor")).GetComponent<Door>();
            officer_secondDoorLight = Item.ItemList.Find(i => i.HasTag("officer_seconddoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(officer_secondDoor, officer_secondDoorLight, false);

            // Room 4
            officer_somethingBigSensor = Item.ItemList.Find(i => i.HasTag("officer_somethingbigsensor")).GetComponent<MotionSensor>();
            officer_coilgunLoader = Item.ItemList.Find(i => i.HasTag("officer_coilgunloader")).GetComponent<ItemContainer>();
            officer_superCapacitor = Item.ItemList.Find(i => i.HasTag("officer_supercapacitor")).GetComponent<PowerContainer>();
            officer_coilgunPeriscope = Item.ItemList.Find(i => i.HasTag("officer_coilgunperiscope"));
            officer_hammerheadSpawnPos = Item.ItemList.Find(i => i.HasTag("officer_hammerheadspawn")).WorldPosition;
            officer_thirdDoor = Item.ItemList.Find(i => i.HasTag("officer_thirddoor")).GetComponent<Door>();
            officer_thirdDoorLight = Item.ItemList.Find(i => i.HasTag("officer_thirddoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(officer_thirdDoor, officer_thirdDoorLight, false);

            // Room 5
            officer_rangedWeaponSensor = Item.ItemList.Find(i => i.HasTag("officer_rangedweaponsensor")).GetComponent<MotionSensor>();
            officer_rangedWeaponCabinet = Item.ItemList.Find(i => i.HasTag("officer_rangedweaponcabinet")).GetComponent<ItemContainer>();
            officer_fourthDoor = Item.ItemList.Find(i => i.HasTag("officer_fourthdoor")).GetComponent<Door>();
            officer_fourthDoorLight = Item.ItemList.Find(i => i.HasTag("officer_fourthdoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(officer_fourthDoor, officer_fourthDoorLight, false);

            // Submarine
            tutorial_submarineDoor = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoor")).GetComponent<Door>();
            tutorial_submarineDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoorlight")).GetComponent<LightComponent>();
            tutorial_enteredSubmarineSensor = Item.ItemList.Find(i => i.HasTag("tutorial_enteredsubmarinesensor")).GetComponent<MotionSensor>();

            SetDoorAccess(tutorial_submarineDoor, tutorial_submarineDoorLight, true);
        }

        public override IEnumerable<object> UpdateState()
        {
            while (true) yield return null;
            // Room 1
            while (shakeTimer > 0.0f) // Wake up, shake
            {
                shakeTimer -= 0.1f;
                GameMain.GameScreen.Cam.Shake = shakeAmount;
                yield return new WaitForSeconds(0.1f);
            }

            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Officer.Radio.WakeUp"), ChatMessageType.Radio, null);

            // Room 2
            do { yield return null; } while (!officer_equipmentObjectiveSensor.MotionDetected);
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Officer.Radio.Equipment"), ChatMessageType.Radio, null);
            yield return new WaitForSeconds(1f);
            TriggerTutorialSegment(0);
            SetHighlight(officer_equipmentCabinet.Item, true);
            bool firstSlotRemoved = false;
            bool secondSlotRemoved = false;
            bool thirdSlotRemoved = false;
            do
            {
                if (IsSelectedItem(officer_equipmentCabinet.Item))
                {
                    if (!firstSlotRemoved)
                    {
                        HighlightInventorySlot(officer_equipmentCabinet.Inventory, 0, highlightColor, .5f, .5f, 0f);
                        if (officer_equipmentCabinet.Inventory.Items[0] == null) firstSlotRemoved = true;
                    }

                    if (!secondSlotRemoved)
                    {
                        HighlightInventorySlot(officer_equipmentCabinet.Inventory, 1, highlightColor, .5f, .5f, 0f);
                        if (officer_equipmentCabinet.Inventory.Items[1] == null) secondSlotRemoved = true;
                    }

                    if (!thirdSlotRemoved)
                    {
                        HighlightInventorySlot(officer_equipmentCabinet.Inventory, 2, highlightColor, .5f, .5f, 0f);
                        if (officer_equipmentCabinet.Inventory.Items[2] == null) thirdSlotRemoved = true;
                    }

                    for (int i = 0; i < officer.Inventory.slots.Length; i++)
                    {
                        if (officer.Inventory.Items[i] == null) HighlightInventorySlot(officer.Inventory, i, highlightColor, .5f, .5f, 0f);
                    }
                }

                yield return null;
            } while (officer.Inventory.FindItemByIdentifier("") == null || officer.Inventory.FindItemByIdentifier("") == null || officer.Inventory.FindItemByIdentifier("") == null); // Wait until looted
            RemoveCompletedObjective(segments[0]);
            do { yield return null; } while (IsSelectedItem(officer_equipmentCabinet.Item));
            yield return new WaitForSeconds(1f);
            TriggerTutorialSegment(1);
            do
            {
                if (!officer.HasEquippedItem("stunbaton"))
                {
                    HighlightInventorySlot(officer.Inventory, "stunbaton", highlightColor, .5f, .5f, 0f);
                }
                yield return null;
            } while (!officer.HasEquippedItem("stunbaton"));
            RemoveCompletedObjective(segments[1]);
            SetDoorAccess(officer_firstDoor, officer_firstDoorLight, true);

            // Room 3
            do { yield return null; } while (!officer_crawlerSensor.MotionDetected);
            TriggerTutorialSegment(2);
            officer_crawler = Character.Create(crawlerCharacterFile, officer_crawlerSpawnPos, ToolBox.RandomSeed(8));
            do { yield return null; } while (!officer_crawler.IsDead);
            RemoveCompletedObjective(segments[2]);
            yield return new WaitForSeconds(1f);
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Officer.Radio.CrawlerDead"), ChatMessageType.Radio, null);
            SetDoorAccess(officer_secondDoor, officer_secondDoorLight, true);

            // Room 4
            do { yield return null; } while (!officer_somethingBigSensor.MotionDetected);
            TriggerTutorialSegment(3);
            do
            {
                SetHighlight(officer_coilgunLoader.Item, officer_coilgunLoader.Inventory.Items[0] == null);
                SetHighlight(officer_superCapacitor.Item, officer_superCapacitor.ChargePercentage < 50);
                yield return null;
            } while (officer_coilgunLoader.Inventory.Items[0] == null || officer_superCapacitor.ChargePercentage < 50);
            RemoveCompletedObjective(segments[4]);
            yield return new WaitForSeconds(1f);
            TriggerTutorialSegment(4);
            officer_hammerhead = Character.Create(hammerheadCharacterFile, officer_hammerheadSpawnPos, ToolBox.RandomSeed(8));
            SetHighlight(officer_coilgunPeriscope, true);
            do { yield return null; } while (!officer_hammerhead.IsDead);
            SetHighlight(officer_coilgunPeriscope, false);
            RemoveCompletedObjective(segments[4]);
            yield return new WaitForSeconds(1f);
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Officer.Radio.HammerheadDead"), ChatMessageType.Radio, null);
            SetDoorAccess(officer_thirdDoor, officer_thirdDoorLight, true);

            // Room 5
        }

        private bool IsSelectedItem(Item item)
        {
            return officer?.SelectedConstruction == item;
        }
    }
}
