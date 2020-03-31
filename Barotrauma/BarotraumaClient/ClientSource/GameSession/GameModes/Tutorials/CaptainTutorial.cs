using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma.Tutorials
{
    class CaptainTutorial : ScenarioTutorial
    {
        // Room 1
        private float shakeTimer = 1f;
        private float shakeAmount = 20f;

        // Room 2
        private MotionSensor captain_equipmentObjectiveSensor;
        private ItemContainer captain_equipmentCabinet;
        private Door captain_firstDoor;
        private LightComponent captain_firstDoorLight;

        // Room 3
        private Character captain_medic;
        private MotionSensor captain_medicObjectiveSensor;
        private Vector2 captain_medicSpawnPos;
        private Door tutorial_submarineDoor;
        private LightComponent tutorial_submarineDoorLight;

        // Submarine
        private MotionSensor captain_enteredSubmarineSensor;
        private Steering captain_navConsole;
        private CustomInterface captain_navConsoleCustomInterface;
        private Sonar captain_sonar;
        private Item captain_statusMonitor;
        private Character captain_security;
        private Character captain_mechanic;
        private Character captain_engineer;
        private Reactor tutorial_submarineReactor;
        private Door tutorial_lockedDoor_1;
        private Door tutorial_lockedDoor_2;

        // Variables
        private Character captain;
        private string radioSpeakerName;
        private Sprite captain_steerIcon;
        private Color captain_steerIconColor;

        public CaptainTutorial(XElement element) : base(element)
        {
        }

        public override void Start()
        {
            base.Start();

            captain = Character.Controlled;
            radioSpeakerName = TextManager.Get("Tutorial.Radio.Watchman");
            GameMain.GameSession.CrewManager.AllowCharacterSwitch = false;

            var revolver = FindOrGiveItem(captain, "revolver");
            revolver.Unequip(captain);
            captain.Inventory.RemoveItem(revolver);

            var captainscap = 
                captain.Inventory.FindItemByIdentifier("captainscap1") ??
                captain.Inventory.FindItemByIdentifier("captainscap2") ??
                captain.Inventory.FindItemByIdentifier("captainscap3");

            if (captainscap != null)
            {
                captainscap.Unequip(captain);
                captain.Inventory.RemoveItem(captainscap);
            }

            var captainsuniform = 
                captain.Inventory.FindItemByIdentifier("captainsuniform1") ??
                captain.Inventory.FindItemByIdentifier("captainsuniform2") ??
                captain.Inventory.FindItemByIdentifier("captainsuniform3");
            if (captainsuniform != null)
            {
                captainsuniform.Unequip(captain);
                captain.Inventory.RemoveItem(captainsuniform);
            }

            var steerOrder = Order.GetPrefab("steer");
            captain_steerIcon = steerOrder.SymbolSprite;
            captain_steerIconColor = steerOrder.Color;

            // Room 2
            captain_equipmentObjectiveSensor = Item.ItemList.Find(i => i.HasTag("captain_equipmentobjectivesensor")).GetComponent<MotionSensor>();
            captain_equipmentCabinet = Item.ItemList.Find(i => i.HasTag("captain_equipmentcabinet")).GetComponent<ItemContainer>();
            captain_firstDoor = Item.ItemList.Find(i => i.HasTag("captain_firstdoor")).GetComponent<Door>();
            captain_firstDoorLight = Item.ItemList.Find(i => i.HasTag("captain_firstdoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(captain_firstDoor, captain_firstDoorLight, true);

            // Room 3
            captain_medicObjectiveSensor = Item.ItemList.Find(i => i.HasTag("captain_medicobjectivesensor")).GetComponent<MotionSensor>();
            captain_medicSpawnPos = Item.ItemList.Find(i => i.HasTag("captain_medicspawnpos")).WorldPosition;
            tutorial_submarineDoor = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoor")).GetComponent<Door>();
            tutorial_submarineDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoorlight")).GetComponent<LightComponent>();
            var medicInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, "", JobPrefab.Get("medicaldoctor"));
            captain_medic = Character.Create(medicInfo, captain_medicSpawnPos, "medicaldoctor");
            captain_medic.GiveJobItems(null);
            captain_medic.CanSpeak = captain_medic.AIController.Enabled = false;
            SetDoorAccess(tutorial_submarineDoor, tutorial_submarineDoorLight, false);

            // Submarine
            captain_enteredSubmarineSensor = Item.ItemList.Find(i => i.HasTag("captain_enteredsubmarinesensor")).GetComponent<MotionSensor>();
            tutorial_submarineReactor = Item.ItemList.Find(i => i.HasTag("engineer_submarinereactor")).GetComponent<Reactor>();
            captain_navConsole = Item.ItemList.Find(i => i.HasTag("command")).GetComponent<Steering>();
            captain_navConsoleCustomInterface = Item.ItemList.Find(i => i.HasTag("command")).GetComponent<CustomInterface>();
            captain_sonar = captain_navConsole.Item.GetComponent<Sonar>();
            captain_statusMonitor = Item.ItemList.Find(i => i.HasTag("captain_statusmonitor"));

            tutorial_submarineReactor.CanBeSelected = false;
            tutorial_submarineReactor.IsActive = tutorial_submarineReactor.AutoTemp = false;

            tutorial_lockedDoor_1 = Item.ItemList.Find(i => i.HasTag("tutorial_lockeddoor_1")).GetComponent<Door>();
            tutorial_lockedDoor_2 = Item.ItemList.Find(i => i.HasTag("tutorial_lockeddoor_2")).GetComponent<Door>();
            SetDoorAccess(tutorial_lockedDoor_1, null, false);
            SetDoorAccess(tutorial_lockedDoor_2, null, false);

            var mechanicInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, "", JobPrefab.Get("mechanic"));
            captain_mechanic = Character.Create(mechanicInfo, WayPoint.GetRandom(SpawnType.Human, mechanicInfo.Job, Submarine.MainSub).WorldPosition, "mechanic");
            captain_mechanic.GiveJobItems();

            var securityInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, "", JobPrefab.Get("securityofficer"));
            captain_security = Character.Create(securityInfo, WayPoint.GetRandom(SpawnType.Human, securityInfo.Job, Submarine.MainSub).WorldPosition, "securityofficer");
            captain_security.GiveJobItems();

            var engineerInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, "", JobPrefab.Get("engineer"));
            captain_engineer = Character.Create(engineerInfo, WayPoint.GetRandom(SpawnType.Human, engineerInfo.Job, Submarine.MainSub).WorldPosition, "engineer");
            captain_engineer.GiveJobItems();

            captain_mechanic.CanSpeak = captain_security.CanSpeak = captain_engineer.CanSpeak = false;
            captain_mechanic.AIController.Enabled = captain_security.AIController.Enabled = captain_engineer.AIController.Enabled = false;
        }

        public override IEnumerable<object> UpdateState()
        {
            while (GameMain.Instance.LoadingScreenOpen) yield return null;

            // Room 1
            while (shakeTimer > 0.0f) // Wake up, shake
            {
                shakeTimer -= 0.1f;
                GameMain.GameScreen.Cam.Shake = shakeAmount;
                yield return new WaitForSeconds(0.1f, false);
            }
            
            // Room 2
            do { yield return null; } while (!captain_firstDoor.IsOpen);
            captain_medic.AIController.Enabled = true;

            // Room 3
            do { yield return null; } while (!captain_medicObjectiveSensor.MotionDetected);
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(captain_medic.Info.DisplayName, TextManager.Get("Captain.Radio.Medic"), ChatMessageType.Radio, null);
            yield return new WaitForSeconds(2f, false);
            GameMain.GameSession.CrewManager.ToggleCrewListOpen = true;
            GameMain.GameSession.CrewManager.AddCharacter(captain_medic);
            TriggerTutorialSegment(0, GameMain.Config.KeyBindText(InputType.Command));
            do
            {
                yield return null;
                // TODO: Rework order highlighting for new command UI
                // GameMain.GameSession.CrewManager.HighlightOrderButton(captain_medic, "follow", highlightColor, new Vector2(5, 5));
            }
            while (!HasOrder(captain_medic, "follow"));
            SetDoorAccess(tutorial_submarineDoor, tutorial_submarineDoorLight, true);
            RemoveCompletedObjective(segments[0]);

            // Submarine
            do { yield return null; } while (!captain_enteredSubmarineSensor.MotionDetected);
            yield return new WaitForSeconds(3f, false);
            captain_mechanic.AIController.Enabled = captain_security.AIController.Enabled = captain_engineer.AIController.Enabled = true;
            TriggerTutorialSegment(1, GameMain.Config.KeyBindText(InputType.Command));
            GameMain.GameSession.CrewManager.AddCharacter(captain_mechanic);
            do
            {
                yield return null;
                // TODO: Rework order highlighting for new command UI
                // GameMain.GameSession.CrewManager.HighlightOrderButton(captain_mechanic, "repairsystems", highlightColor, new Vector2(5, 5));
                //HighlightOrderOption("jobspecific");
            }
            while (!HasOrder(captain_mechanic, "repairsystems"));
            RemoveCompletedObjective(segments[1]);
            yield return new WaitForSeconds(2f, false);
            TriggerTutorialSegment(2, GameMain.Config.KeyBindText(InputType.Command));
            GameMain.GameSession.CrewManager.AddCharacter(captain_security);
            do
            {
                yield return null;
                // TODO: Rework order highlighting for new command UI
                // GameMain.GameSession.CrewManager.HighlightOrderButton(captain_security, "operateweapons", highlightColor, new Vector2(5, 5));
                HighlightOrderOption("fireatwill");
            }
            while (!HasOrder(captain_security, "operateweapons", "fireatwill"));
            RemoveCompletedObjective(segments[2]);
            yield return new WaitForSeconds(4f, false);
            TriggerTutorialSegment(3, GameMain.Config.KeyBindText(InputType.Command));
            GameMain.GameSession.CrewManager.AddCharacter(captain_engineer);
            do
            {
                yield return null;
                // TODO: Rework order highlighting for new command UI
                // GameMain.GameSession.CrewManager.HighlightOrderButton(captain_engineer, "operatereactor", highlightColor, new Vector2(5, 5));
                HighlightOrderOption("powerup");
            }
            while (!HasOrder(captain_engineer, "operatereactor", "powerup"));
            RemoveCompletedObjective(segments[3]);
            tutorial_submarineReactor.CanBeSelected = true;
            do { yield return null; } while (!tutorial_submarineReactor.IsActive); // Wait until reactor on      
            TriggerTutorialSegment(4);
            while (ContentRunning) yield return null;            
            captain.AddActiveObjectiveEntity(captain_navConsole.Item, captain_steerIcon, captain_steerIconColor);
            SetHighlight(captain_navConsole.Item, true);
            SetHighlight(captain_sonar.Item, true);
            SetHighlight(captain_statusMonitor, true);
            captain_navConsole.UseAutoDocking = false;
            do
            {
                //captain_navConsoleCustomInterface.HighlightElement(0, uiHighlightColor, duration: 1.0f, pulsateAmount: 0.0f);
                yield return new WaitForSeconds(1.0f, false);
            } while (Submarine.MainSub.DockedTo.Any());
            RemoveCompletedObjective(segments[4]);
            yield return new WaitForSeconds(2f, false);
            TriggerTutorialSegment(5); // Navigate to destination
            do
            {
                if (IsSelectedItem(captain_navConsole.Item))
                {
                    if (captain_sonar.SonarModeSwitch.Frame.FlashTimer <= 0)
                    {
                        captain_sonar.SonarModeSwitch.Frame.Flash(highlightColor, 1.5f, false, false, new Vector2(2.5f, 2.5f));
                    }
                }
                yield return null;
            } while (captain_sonar.CurrentMode != Sonar.Mode.Active);
            do { yield return null; } while (Vector2.Distance(Submarine.MainSub.WorldPosition, Level.Loaded.EndPosition) > 4000f);
            RemoveCompletedObjective(segments[5]);
            captain_navConsole.UseAutoDocking = true;
            yield return new WaitForSeconds(4f, false);
            TriggerTutorialSegment(6); // Docking
            do
            {
                //captain_navConsoleCustomInterface.HighlightElement(0, uiHighlightColor, duration: 1.0f, pulsateAmount: 0.0f);
                yield return new WaitForSeconds(1.0f, false);
            } while (!Submarine.MainSub.AtEndPosition || Submarine.MainSub.DockedTo.Any());
            RemoveCompletedObjective(segments[6]);
            yield return new WaitForSeconds(3f, false);
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.GetWithVariable("Captain.Radio.Complete", "[OUTPOSTNAME]", GameMain.GameSession.EndLocation.Name), ChatMessageType.Radio, null);
            SetHighlight(captain_navConsole.Item, false);
            SetHighlight(captain_sonar.Item, false);
            SetHighlight(captain_statusMonitor, false);
            captain.RemoveActiveObjectiveEntity(captain_navConsole.Item);

            CoroutineManager.StartCoroutine(TutorialCompleted());
        }

        private void HighlightOrderOption(string option)
        {
            if (GameMain.GameSession.CrewManager.OrderOptionButtons.Count == 0) return;
            var order = GameMain.GameSession.CrewManager.OrderOptionButtons[0].UserData as Order;

            int orderIndex = 0;
            for (int i = 0; i < GameMain.GameSession.CrewManager.OrderOptionButtons.Count; i++)
            {
                if (orderIndex >= order.Options.Length)
                {
                    orderIndex = 0;
                }
                if (order.Options[orderIndex] == option)
                {
                    if (GameMain.GameSession.CrewManager.OrderOptionButtons[i].FlashTimer <= 0)
                    {
                        GameMain.GameSession.CrewManager.OrderOptionButtons[i].Flash(highlightColor);
                    }
                }

                orderIndex++;
            }            
        }

        private bool IsSelectedItem(Item item)
        {
            return captain?.SelectedConstruction == item;
        }
    }
}
