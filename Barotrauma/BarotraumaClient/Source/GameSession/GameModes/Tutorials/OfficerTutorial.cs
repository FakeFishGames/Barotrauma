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

        // Room 2
        private MotionSensor officer_equipmentObjectiveSensor;
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
        private ItemContainer officer_rangedWeaponHolder_1;
        private ItemContainer officer_rangedWeaponHolder_2;
        private Door officer_fourthDoor;
        private LightComponent officer_fourthDoorLight;

        // Room 6
        private MotionSensor officer_mudraptorObjectiveSensor;
        private Vector2 officer_mudraptorSpawnPos;
        private Character officer_mudraptor;
        private Door tutorial_securityFinalDoor;
        private LightComponent tutorial_securityFinalDoorLight;

        // Submarine
        private Door tutorial_submarineDoor;
        private LightComponent tutorial_submarineDoorLight;
        private MotionSensor tutorial_enteredSubmarineSensor;
        private Item officer_subAmmoBox_1;
        private Item officer_subAmmoBox_2;
        private ItemContainer officer_subLoader_1;
        private ItemContainer officer_subLoader_2;
        private PowerContainer officer_subSuperCapacitor_1;
        private PowerContainer officer_subSuperCapacitor_2;

        // Variables
        private string radioSpeakerName;
        private Character officer;
        private string crawlerCharacterFile;
        private string hammerheadCharacterFile;
        private string mudraptorCharacterFile;
        private float superCapacitorPercentage = 50;

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

            foreach (string characterFile in characterFiles)
            {
                if (Path.GetFileNameWithoutExtension(characterFile).ToLowerInvariant() == "mudraptor")
                {
                    mudraptorCharacterFile = characterFile;
                    break;
                }
            }
        }

        public override void Start()
        {
            base.Start();

            radioSpeakerName = TextManager.Get("Tutorial.Radio.Speaker");
            officer = Character.Controlled;

            var handcuffs = officer.Inventory.FindItemByIdentifier("handcuffs");
            officer.Inventory.RemoveItem(handcuffs);

            var stunbaton = officer.Inventory.FindItemByIdentifier("stunbaton");
            officer.Inventory.RemoveItem(stunbaton);

            var ballistichelmet = officer.Inventory.FindItemByIdentifier("ballistichelmet");
            ballistichelmet.Unequip(officer);
            officer.Inventory.RemoveItem(ballistichelmet);

            var bodyarmor = officer.Inventory.FindItemByIdentifier("bodyarmor");
            bodyarmor.Unequip(officer);
            officer.Inventory.RemoveItem(bodyarmor);

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
            officer_crawlerSensor = Item.ItemList.Find(i => i.HasTag("officer_crawlerobjectivesensor")).GetComponent<MotionSensor>();
            officer_crawlerSpawnPos = Item.ItemList.Find(i => i.HasTag("officer_crawlerspawn")).WorldPosition;
            officer_secondDoor = Item.ItemList.Find(i => i.HasTag("officer_seconddoor")).GetComponent<Door>();
            officer_secondDoorLight = Item.ItemList.Find(i => i.HasTag("officer_seconddoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(officer_secondDoor, officer_secondDoorLight, false);

            // Room 4
            officer_somethingBigSensor = Item.ItemList.Find(i => i.HasTag("officer_somethingbigobjectivesensor")).GetComponent<MotionSensor>();
            officer_coilgunLoader = Item.ItemList.Find(i => i.HasTag("officer_coilgunloader")).GetComponent<ItemContainer>();
            officer_superCapacitor = Item.ItemList.Find(i => i.HasTag("officer_supercapacitor")).GetComponent<PowerContainer>();
            officer_coilgunPeriscope = Item.ItemList.Find(i => i.HasTag("officer_coilgunperiscope"));
            officer_hammerheadSpawnPos = Item.ItemList.Find(i => i.HasTag("officer_hammerheadspawn")).WorldPosition;
            officer_thirdDoor = Item.ItemList.Find(i => i.HasTag("officer_thirddoor")).GetComponent<Door>();
            officer_thirdDoorLight = Item.ItemList.Find(i => i.HasTag("officer_thirddoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(officer_thirdDoor, officer_thirdDoorLight, false);

            // Room 5
            officer_rangedWeaponSensor = Item.ItemList.Find(i => i.HasTag("officer_rangedweaponobjectivesensor")).GetComponent<MotionSensor>();
            officer_rangedWeaponCabinet = Item.ItemList.Find(i => i.HasTag("officer_rangedweaponcabinet")).GetComponent<ItemContainer>();
            officer_rangedWeaponHolder_1 = Item.ItemList.Find(i => i.HasTag("officer_rangedweaponholder_1")).GetComponent<ItemContainer>();
            officer_rangedWeaponHolder_2 = Item.ItemList.Find(i => i.HasTag("officer_rangedweaponholder_2")).GetComponent<ItemContainer>();
            officer_fourthDoor = Item.ItemList.Find(i => i.HasTag("officer_fourthdoor")).GetComponent<Door>();
            officer_fourthDoorLight = Item.ItemList.Find(i => i.HasTag("officer_fourthdoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(officer_fourthDoor, officer_fourthDoorLight, false);

            // Room 6
            officer_mudraptorObjectiveSensor = Item.ItemList.Find(i => i.HasTag("officer_mudraptorobjectivesensor")).GetComponent<MotionSensor>();
            officer_mudraptorSpawnPos = Item.ItemList.Find(i => i.HasTag("officer_mudraptorspawn")).WorldPosition;
            tutorial_securityFinalDoor = Item.ItemList.Find(i => i.HasTag("tutorial_securityfinaldoor")).GetComponent<Door>();
            tutorial_securityFinalDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_securityfinaldoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(tutorial_securityFinalDoor, tutorial_securityFinalDoorLight, false);

            // Submarine
            tutorial_submarineDoor = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoor")).GetComponent<Door>();
            tutorial_submarineDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoorlight")).GetComponent<LightComponent>();
            tutorial_enteredSubmarineSensor = Item.ItemList.Find(i => i.HasTag("tutorial_enteredsubmarinesensor")).GetComponent<MotionSensor>();
            officer_subAmmoBox_1 = Item.ItemList.Find(i => i.HasTag("officer_subammobox_1"));
            officer_subAmmoBox_2 = Item.ItemList.Find(i => i.HasTag("officer_subammobox_2"));
            officer_subLoader_1 = Item.ItemList.Find(i => i.HasTag("officer_subloader_1")).GetComponent<ItemContainer>();
            officer_subLoader_2 = Item.ItemList.Find(i => i.HasTag("officer_subloader_2")).GetComponent<ItemContainer>();
            officer_subSuperCapacitor_1 = Item.ItemList.Find(i => i.HasTag("officer_subsupercapacitor_1")).GetComponent<PowerContainer>();
            officer_subSuperCapacitor_2 = Item.ItemList.Find(i => i.HasTag("officer_subsupercapacitor_2")).GetComponent<PowerContainer>();

            SetDoorAccess(tutorial_submarineDoor, tutorial_submarineDoorLight, true);
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
            } while (!officer_equipmentCabinet.Inventory.IsEmpty()); // Wait until looted
            RemoveCompletedObjective(segments[0]);
            SetHighlight(officer_equipmentCabinet.Item, false);
            do { yield return null; } while (IsSelectedItem(officer_equipmentCabinet.Item));
            yield return new WaitForSeconds(1f);
            TriggerTutorialSegment(1);
            do
            {
                if (!officer.HasEquippedItem("stunbaton"))
                {
                    HighlightInventorySlot(officer.Inventory, "stunbaton", highlightColor, .5f, .5f, 0f);
                }
                if (!officer.HasEquippedItem("bodyarmor"))
                {
                    HighlightInventorySlot(officer.Inventory, "bodyarmor", highlightColor, .5f, .5f, 0f);
                }
                if (!officer.HasEquippedItem("ballistichelmet"))
                {
                    HighlightInventorySlot(officer.Inventory, "ballistichelmet", highlightColor, .5f, .5f, 0f);
                }
                yield return null;
            } while (!officer.HasEquippedItem("stunbaton") || !officer.HasEquippedItem("bodyarmor") || !officer.HasEquippedItem("ballistichelmet"));
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
                SetHighlight(officer_superCapacitor.Item, officer_superCapacitor.ChargePercentage < superCapacitorPercentage);
                yield return null;
            } while (officer_coilgunLoader.Inventory.Items[0] == null || officer_superCapacitor.ChargePercentage < superCapacitorPercentage);
            SetHighlight(officer_coilgunLoader.Item, false);
            SetHighlight(officer_superCapacitor.Item, false);
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
            do { yield return null; } while (!officer_rangedWeaponSensor.MotionDetected);
            TriggerTutorialSegment(5);
            SetHighlight(officer_rangedWeaponHolder_1.Item, true);
            SetHighlight(officer_rangedWeaponHolder_2.Item, true);

            do
            {
                //if (IsSelectedItem(officer_rangedWeaponHolder_1.Item) || IsSelectedItem(officer_rangedWeaponHolder_2.Item))
                //{
                //    for (int i = 0; i < officer.Inventory.slots.Length; i++)
                //    {
                //        if (officer.Inventory.Items[i] == null) HighlightInventorySlot(officer.Inventory, i, highlightColor, .5f, .5f, 0f);
                //    }
                //}

                //if (IsSelectedItem(officer_rangedWeaponHolder_1.Item))
                //{
                //    if (officer_rangedWeaponHolder_1.Inventory.Items[0] != null)
                //    {
                //        HighlightInventorySlot(officer_rangedWeaponHolder_1.Inventory, 0, highlightColor, .5f, .5f, 0f);
                //    }
                //}

                //if (IsSelectedItem(officer_rangedWeaponHolder_2.Item))
                //{
                //    if (officer_rangedWeaponHolder_2.Inventory.Items[0] != null)
                //    {
                //        HighlightInventorySlot(officer_rangedWeaponHolder_2.Inventory, 0, highlightColor, .5f, .5f, 0f);
                //    }
                //}

                //SetHighlight(officer_rangedWeaponHolder_1.Item, !officer_rangedWeaponHolder_1.Inventory.IsEmpty());
                //SetHighlight(officer_rangedWeaponHolder_2.Item, !officer_rangedWeaponHolder_2.Inventory.IsEmpty());
                yield return null;
            } while (!officer_rangedWeaponHolder_1.Inventory.IsEmpty() || !officer_rangedWeaponHolder_2.Inventory.IsEmpty()); // Wait until looted
            SetHighlight(officer_rangedWeaponHolder_1.Item, false);
            SetHighlight(officer_rangedWeaponHolder_2.Item, false);
            do
            {
                HighlightInventorySlot(officer.Inventory, "revolver", highlightColor, 0.5f, 0.5f, 0f);
                HighlightInventorySlot(officer.Inventory, "harpoongun", highlightColor, 0.5f, 0.5f, 0f);
                yield return null;
            } while (!officer.HasEquippedItem("revolver") || !officer.HasEquippedItem("harpoongun")); // Wait until equipped

            RemoveCompletedObjective(segments[5]);
            SetHighlight(officer_rangedWeaponCabinet.Item, false);
            SetHighlight(officer_rangedWeaponHolder_1.Item, false);
            SetHighlight(officer_rangedWeaponHolder_2.Item, false);
            SetDoorAccess(officer_fourthDoor, officer_fourthDoorLight, true);

            // Room 6
            do { yield return null; } while (!officer_mudraptorObjectiveSensor.MotionDetected);
            TriggerTutorialSegment(6);
            officer_mudraptor = Character.Create(mudraptorCharacterFile, officer_mudraptorSpawnPos, ToolBox.RandomSeed(8));
            do { yield return null; } while (!officer_mudraptor.IsDead);
            RemoveCompletedObjective(segments[6]);
            SetDoorAccess(tutorial_securityFinalDoor, tutorial_securityFinalDoorLight, true);

            // Submarine
            do { yield return null; } while (!tutorial_enteredSubmarineSensor.MotionDetected);
            TriggerTutorialSegment(7);
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Officer.Radio.Submarine"), ChatMessageType.Radio, null);
            do
            {
                SetHighlight(officer_subLoader_1.Item, officer_subLoader_1.Inventory.Items[0] == null || officer_subLoader_1.Inventory.Items[0].Condition == 0);
                SetHighlight(officer_subLoader_2.Item, officer_subLoader_2.Inventory.Items[0] == null || officer_subLoader_2.Inventory.Items[0].Condition == 0);
                SetHighlight(officer_subSuperCapacitor_1.Item, officer_subSuperCapacitor_1.ChargePercentage < superCapacitorPercentage);
                SetHighlight(officer_subSuperCapacitor_2.Item, officer_subSuperCapacitor_2.ChargePercentage < superCapacitorPercentage);
                SetHighlight(officer_subAmmoBox_1, officer_subLoader_1.Item.ExternalHighlight || officer_subLoader_2.Item.ExternalHighlight);
                SetHighlight(officer_subAmmoBox_2, officer_subLoader_1.Item.ExternalHighlight || officer_subLoader_2.Item.ExternalHighlight);
                yield return null;
            } while (officer_subLoader_1.Item.ExternalHighlight || officer_subLoader_2.Item.ExternalHighlight || officer_subSuperCapacitor_1.Item.ExternalHighlight || officer_subSuperCapacitor_2.Item.ExternalHighlight);
            SetHighlight(officer_subLoader_1.Item, false);
            SetHighlight(officer_subLoader_2.Item, false);
            SetHighlight(officer_subSuperCapacitor_1.Item, false);
            SetHighlight(officer_subSuperCapacitor_2.Item, false);
            SetHighlight(officer_subAmmoBox_1, false);
            SetHighlight(officer_subAmmoBox_2, false);
            RemoveCompletedObjective(segments[7]);
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Officer.Radio.Complete"), ChatMessageType.Radio, null);
            // END TUTORIAL
        }

        private bool IsSelectedItem(Item item)
        {
            return officer?.SelectedConstruction == item;
        }
    }
}
