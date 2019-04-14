using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma.Tutorials
{
    class CaptainTutorial : ScenarioTutorial
    {
        // Room 1
        private float shakeTimer = 3.0f;
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
        private Sonar captain_sonar;
        private Character captain_security;
        private Character captain_mechanic;
        private Character captain_engineer;
        private Reactor tutorial_submarineReactor;

        // Variables
        private Character captain;
        private string radioSpeakerName;

        public CaptainTutorial(XElement element) : base(element)
        {
        }

        public override void Start()
        {
            base.Start();

            captain = Character.Controlled;
            radioSpeakerName = TextManager.Get("Tutorial.Radio.Watchman");
            GameMain.GameSession.CrewManager.AllowCharacterSwitch = false;

            var revolver = captain.Inventory.FindItemByIdentifier("revolver");
            captain.Inventory.RemoveItem(revolver);

            var captainscap = captain.Inventory.FindItemByIdentifier("captainscap");
            captainscap.Unequip(captain);
            captain.Inventory.RemoveItem(captainscap);

            var captainsuniform = captain.Inventory.FindItemByIdentifier("captainsuniform");
            captainsuniform.Unequip(captain);
            captain.Inventory.RemoveItem(captainsuniform);

            // Room 2
            captain_equipmentObjectiveSensor = Item.ItemList.Find(i => i.HasTag("captain_equipmentobjectivesensor")).GetComponent<MotionSensor>();
            captain_equipmentCabinet = Item.ItemList.Find(i => i.HasTag("captain_equipmentcabinet")).GetComponent<ItemContainer>();
            captain_firstDoor = Item.ItemList.Find(i => i.HasTag("captain_firstdoor")).GetComponent<Door>();
            captain_firstDoorLight = Item.ItemList.Find(i => i.HasTag("captain_firstdoorlight")).GetComponent<LightComponent>();

            SetDoorAccess(captain_firstDoor, captain_firstDoorLight, false);

            // Room 3
            captain_medicObjectiveSensor = Item.ItemList.Find(i => i.HasTag("captain_medicobjectivesensor")).GetComponent<MotionSensor>();
            captain_medicSpawnPos = Item.ItemList.Find(i => i.HasTag("captain_medicspawnpos")).WorldPosition;
            tutorial_submarineDoor = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoor")).GetComponent<Door>();
            tutorial_submarineDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoorlight")).GetComponent<LightComponent>();

            var medicInfo = new CharacterInfo(Character.HumanConfigFile, "", JobPrefab.List.Find(jp => jp.Identifier == "medic"));
            captain_medic = Character.Create(medicInfo, captain_medicSpawnPos, "medicaldoctor");
            captain_medic.GiveJobItems(null);
            captain_medic.CanSpeak = captain_medic.AIController.Enabled = false;
            SetDoorAccess(tutorial_submarineDoor, tutorial_submarineDoorLight, false);

            // Submarine
            captain_enteredSubmarineSensor = Item.ItemList.Find(i => i.HasTag("captain_enteredsubmarinesensor")).GetComponent<MotionSensor>();
            tutorial_submarineReactor = Item.ItemList.Find(i => i.HasTag("engineer_submarinereactor")).GetComponent<Reactor>();
            captain_navConsole = Item.ItemList.Find(i => i.HasTag("command")).GetComponent<Steering>();
            captain_sonar = captain_navConsole.Item.GetComponent<Sonar>();

            tutorial_submarineReactor.CanBeSelected = false;

            var mechanicInfo = new CharacterInfo(Character.HumanConfigFile, "", JobPrefab.List.Find(jp => jp.Identifier == "mechanic"));
            captain_mechanic = Character.Create(mechanicInfo, WayPoint.GetRandom(SpawnType.Human, mechanicInfo.Job, Submarine.MainSub).WorldPosition, "mechanic");
            captain_mechanic.GiveJobItems();

            var securityInfo = new CharacterInfo(Character.HumanConfigFile, "", JobPrefab.List.Find(jp => jp.Identifier == "securityofficer"));
            captain_security = Character.Create(securityInfo, WayPoint.GetRandom(SpawnType.Human, securityInfo.Job, Submarine.MainSub).WorldPosition, "securityofficer");
            captain_security.GiveJobItems();

            var engineerInfo = new CharacterInfo(Character.HumanConfigFile, "", JobPrefab.List.Find(jp => jp.Identifier == "engineer"));
            captain_engineer = Character.Create(engineerInfo, WayPoint.GetRandom(SpawnType.Human, engineerInfo.Job, Submarine.MainSub).WorldPosition, "engineer");
            captain_engineer.GiveJobItems();

            captain_mechanic.CanSpeak = captain_security.CanSpeak = captain_engineer.CanSpeak = false;
            captain_mechanic.AIController.Enabled = captain_security.AIController.Enabled = captain_engineer.AIController.Enabled = false;
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

            // Room 2
            do { yield return null; } while (!captain_equipmentObjectiveSensor.MotionDetected);
            SetHighlight(captain_equipmentCabinet.Item, true);
            do { yield return null; } while (!captain_equipmentCabinet.Inventory.IsEmpty());
            SetHighlight(captain_equipmentCabinet.Item, false);
            captain_medic.AIController.Enabled = true;
            SetDoorAccess(captain_firstDoor, captain_firstDoorLight, true);

            // Room 3
            do { yield return null; } while (!captain_medicObjectiveSensor.MotionDetected);
            yield return new WaitForSeconds(2f);
            GameMain.GameSession.CrewManager.ToggleCrewAreaOpen = true;
            GameMain.GameSession.CrewManager.AddCharacter(captain_medic);
            TriggerTutorialSegment(0);
            do
            {
                GameMain.GameSession.CrewManager.HighlightOrderButton(captain_medic, "follow", highlightColor);
                yield return null;
            }
            while (!HasOrder(captain_medic, "follow"));
            SetDoorAccess(tutorial_submarineDoor, tutorial_submarineDoorLight, true);
            RemoveCompletedObjective(segments[0]);

            // Submarine
            do { yield return null; } while (!captain_enteredSubmarineSensor.MotionDetected);
            captain_mechanic.AIController.Enabled = captain_security.AIController.Enabled = captain_engineer.AIController.Enabled = true;
            TriggerTutorialSegment(1);
            GameMain.GameSession.CrewManager.AddCharacter(captain_mechanic);
            do
            {
                GameMain.GameSession.CrewManager.HighlightOrderButton(captain_mechanic, "repairsystems", highlightColor);
                yield return null;
            }
            while (!HasOrder(captain_mechanic, "repairsystems"));
            RemoveCompletedObjective(segments[1]);
            yield return new WaitForSeconds(2f);
            TriggerTutorialSegment(2);
            GameMain.GameSession.CrewManager.AddCharacter(captain_security);
            do
            {
                GameMain.GameSession.CrewManager.HighlightOrderButton(captain_security, "operateweapons", highlightColor);
                yield return null;
            }
            while (!HasOrder(captain_security, "operateweapons"));
            RemoveCompletedObjective(segments[2]);
            yield return new WaitForSeconds(2f);
            TriggerTutorialSegment(3);
            GameMain.GameSession.CrewManager.AddCharacter(captain_engineer);
            do
            {
                GameMain.GameSession.CrewManager.HighlightOrderButton(captain_engineer, "operatereactor", highlightColor);
                yield return null;
            }
            while (!HasOrder(captain_engineer, "operatereactor", "powerup"));
            RemoveCompletedObjective(segments[3]);
            do { yield return null; } while (!tutorial_submarineReactor.IsActive); // Wait until reactor on      
            TriggerTutorialSegment(4);
            SetHighlight(captain_navConsole.Item, true);
            SetHighlight(captain_sonar.Item, true);
            do { yield return null; } while (Submarine.MainSub.DockedTo.Count > 0);
            RemoveCompletedObjective(segments[4]);
            yield return new WaitForSeconds(2f);
            TriggerTutorialSegment(5);
            do { yield return null; } while (Vector2.Distance(Submarine.MainSub.WorldPosition, Level.Loaded.EndPosition) > 4000f);
            RemoveCompletedObjective(segments[5]);
            yield return new WaitForSeconds(2f);
            TriggerTutorialSegment(6);
            do { yield return null; } while (!Submarine.MainSub.AtEndPosition || Submarine.MainSub.DockedTo.Count == 0);
            RemoveCompletedObjective(segments[6]);
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Captain.Radio.Complete").Replace("[OUTPOSTNAME]", GameMain.GameSession.EndLocation.Name), ChatMessageType.Radio, null);
            SetHighlight(captain_navConsole.Item, false);
            SetHighlight(captain_sonar.Item, false);
            // Tutorial complete
        }
    }
}
