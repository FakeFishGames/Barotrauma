using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma.Tutorials
{
    class DoctorTutorial : ScenarioTutorial
    {
        // Room 1
        private float shakeTimer = 3.0f;
        private float shakeAmount = 20f;

        private string radioSpeakerName;
        private Character doctor;

        private ItemContainer doctor_suppliesCabinet;
        private Character patient1, patient2;
        private List<Character> subPatients = new List<Character>();
        private Hull startRoom;
        private Hull medBay;

        public DoctorTutorial(XElement element) : base(element)
        {
        }
        public override void Start()
        {
            base.Start();

            radioSpeakerName = TextManager.Get("Tutorial.Radio.Speaker");
            doctor = Character.Controlled;

            doctor_suppliesCabinet = Item.ItemList.Find(i => i.HasTag("doctor_suppliescabinet"))?.GetComponent<ItemContainer>();

            var patientHull1 = Hull.hullList.Find(h => h.RoomName == "Waiting room" && h.Submarine == doctor.Submarine);
            var patientHull2 = Hull.hullList.Find(h => h.RoomName == "Airlock" && h.Submarine == doctor.Submarine);
            medBay = Hull.hullList.Find(h => h.RoomName == "Med bay" && h.Submarine == doctor.Submarine);

            var assistantInfo = new CharacterInfo(Character.HumanConfigFile, "", JobPrefab.List.Find(jp => jp.Identifier == "assistant"));
            patient1 = Character.Create(assistantInfo, patientHull1.WorldPosition, "1");
            patient1.GiveJobItems(null);
            patient1.CanSpeak = false;
            patient1.AddDamage(patient1.WorldPosition, new List<Affliction>() { new Affliction(AfflictionPrefab.Burn, 45.0f) }, stun: 0, playSound: false);
            patient1.AIController.Enabled = false;
            
            assistantInfo = new CharacterInfo(Character.HumanConfigFile, "", JobPrefab.List.Find(jp => jp.Identifier == "assistant"));
            patient2 = Character.Create(assistantInfo, patientHull2.WorldPosition, "2");
            patient2.GiveJobItems(null);
            patient2.CanSpeak = false;
            patient2.AIController.Enabled = false;

            var mechanicInfo = new CharacterInfo(Character.HumanConfigFile, "", JobPrefab.List.Find(jp => jp.Identifier == "engineer"));
            var subPatient1 = Character.Create(mechanicInfo, WayPoint.GetRandom(SpawnType.Human, mechanicInfo.Job, Submarine.MainSub).WorldPosition, "3");
            subPatient1.AddDamage(patient1.WorldPosition, new List<Affliction>() { new Affliction(AfflictionPrefab.Burn, 40.0f) }, stun: 0, playSound: false);
            subPatients.Add(subPatient1);

            var securityInfo = new CharacterInfo(Character.HumanConfigFile, "", JobPrefab.List.Find(jp => jp.Identifier == "securityofficer"));
            var subPatient2 = Character.Create(securityInfo, WayPoint.GetRandom(SpawnType.Human, securityInfo.Job, Submarine.MainSub).WorldPosition, "3");
            subPatient2.AddDamage(patient1.WorldPosition, new List<Affliction>() { new Affliction(AfflictionPrefab.InternalDamage, 40.0f) }, stun: 0, playSound: false);
            subPatients.Add(subPatient2);

            var engineerInfo = new CharacterInfo(Character.HumanConfigFile, "", JobPrefab.List.Find(jp => jp.Identifier == "engineer"));
            var subPatient3 = Character.Create(securityInfo, WayPoint.GetRandom(SpawnType.Human, engineerInfo.Job, Submarine.MainSub).WorldPosition, "3");
            subPatient3.AddDamage(patient1.WorldPosition, new List<Affliction>() { new Affliction(AfflictionPrefab.Burn, 20.0f) }, stun: 0, playSound: false);
            subPatients.Add(subPatient3);

            foreach (var patient in subPatients)
            {
                patient.CanSpeak = false;
                patient.AIController.Enabled = false;
                patient.GiveJobItems();
            }

            Item reactorItem = Item.ItemList.Find(i => i.Submarine == Submarine.MainSub && i.GetComponent<Reactor>() != null);
            reactorItem.GetComponent<Reactor>().AutoTemp = true;
        }

        public override IEnumerable<object> UpdateState()
        {
            // explosions and radio messages ------------------------------------------------------

            yield return new WaitForSeconds(3.0f);

            SoundPlayer.PlayDamageSound("StructureBlunt", 10, Character.Controlled.WorldPosition);
            // Room 1
            while (shakeTimer > 0.0f) // Wake up, shake
            {
                shakeTimer -= 0.1f;
                GameMain.GameScreen.Cam.Shake = shakeAmount;
                yield return new WaitForSeconds(0.1f);
            }
            yield return new WaitForSeconds(2.5f);
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.WakeUp"), ChatMessageType.Radio, null);

            yield return new WaitForSeconds(2.5f);

            doctor.SetStun(2.0f);
            var explosion = new Explosion(range: 100, force: 20, damage: 0, structureDamage: 0);
            explosion.DisableParticles();
            GameMain.GameScreen.Cam.Shake = shakeAmount;
            explosion.Explode(Character.Controlled.WorldPosition - Vector2.UnitX * 50, null);
            SoundPlayer.PlayDamageSound("StructureBlunt", 10, Character.Controlled.WorldPosition - Vector2.UnitX * 50);

            yield return new WaitForSeconds(0.5f);

            doctor.DamageLimb(
                Character.Controlled.WorldPosition,
                doctor.AnimController.GetLimb(LimbType.Head),
                new List<Affliction> { new Affliction(AfflictionPrefab.InternalDamage, 10.0f) },
                stun: 3.0f, playSound: true, attackImpulse: 0.0f);

            shakeTimer = 2.0f;
            while (shakeTimer > 0.0f) // Wake up, shake
            {
                shakeTimer -= 0.1f;
                GameMain.GameScreen.Cam.Shake = shakeAmount;
                yield return new WaitForSeconds(0.1f);
            }

            yield return new WaitForSeconds(3.0f);
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Doctor.Radio.KnockedDown"), ChatMessageType.Radio, null);
            
            // first tutorial segment, get medical supplies ------------------------------------------------------

            SetHighlight(doctor_suppliesCabinet.Item, true);

            while (doctor.CurrentHull != doctor_suppliesCabinet.Item.CurrentHull)
            {
                yield return new WaitForSeconds(2.0f);
            }

            TriggerTutorialSegment(0); // Medical supplies objective

            do
            {
                for (int i = 0; i < doctor_suppliesCabinet.Inventory.Items.Length; i++)
                {
                    if (doctor_suppliesCabinet.Inventory.Items[i] != null)
                    {
                        HighlightInventorySlot(doctor_suppliesCabinet.Inventory, i, highlightColor, .5f, .5f, 0f);
                    }
                }
                if (doctor.SelectedConstruction == doctor_suppliesCabinet.Item)
                {
                    for (int i = 0; i < doctor.Inventory.slots.Length; i++)
                    {
                        if (doctor.Inventory.Items[i] == null) HighlightInventorySlot(doctor.Inventory, i, highlightColor, .5f, .5f, 0f);
                    }
                }
                yield return null;
            } while (doctor.Inventory.FindItemByIdentifier("antidama1") == null); // Wait until looted
            yield return new WaitForSeconds(1.0f);

            SetHighlight(doctor_suppliesCabinet.Item, false);
            RemoveCompletedObjective(segments[0]);
            yield return new WaitForSeconds(1.0f);

            // 2nd tutorial segment, treat self -------------------------------------------------------------------------

            TriggerTutorialSegment(1); // Open health interface
            while (CharacterHealth.OpenHealthWindow == null)
            {
                yield return new WaitForSeconds(1.0f);
            }
            RemoveCompletedObjective(segments[1]);

            TriggerTutorialSegment(2); //Treat self
            while (doctor.CharacterHealth.GetAfflictionStrength("damage") > 0.01f)
            {
                if (CharacterHealth.OpenHealthWindow == null)
                {
                    doctor.CharacterHealth.HealthBarPulsateTimer = 1.0f;
                }
                else
                {
                    HighlightInventorySlot(doctor.Inventory, "antidama1", highlightColor, .5f, .5f, 0f);
                }

                yield return null;
            }

            RemoveCompletedObjective(segments[2]);

            while (CharacterHealth.OpenHealthWindow != null)
            {
                yield return new WaitForSeconds(1.0f);
            }

            // treat patient --------------------------------------------------------------------------------------------

            //patient 1 requests first aid
            patient1.CanSpeak = true;
            var newOrder = new Order(Order.PrefabList.Find(o => o.AITag == "requestfirstaid"), patient1.CurrentHull, null);
            GameMain.GameSession.CrewManager.AddOrder(newOrder, newOrder.FadeOutTime);
            patient1.Speak(newOrder.GetChatMessage("", patient1.CurrentHull?.RoomName, givingOrderToSelf: false), ChatMessageType.Order);
            patient1.AIController.Enabled = true;

            while (doctor.CurrentHull != patient1.CurrentHull)
            {
                yield return new WaitForSeconds(1.0f);
            }
            yield return new WaitForSeconds(2.0f);

            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Doctor.Radio.AssistantBurns"), ChatMessageType.Radio, null);
            GameMain.GameSession.CrewManager.AllowCharacterSwitch = false;
            GameMain.GameSession.CrewManager.AddCharacter(doctor);
            GameMain.GameSession.CrewManager.AddCharacter(patient1);
            GameMain.GameSession.CrewManager.ToggleCrewAreaOpen = true;

            yield return new WaitForSeconds(3.0f);
            TriggerTutorialSegment(3); // Get the patient to medbay

            while (patient1.CurrentOrder == null || patient1.CurrentOrder.AITag != "follow")
            {
                GameMain.GameSession.CrewManager.HighlightOrderButton(patient1, "follow", highlightColor);
                yield return null;
            }

            while (patient1.CurrentHull != medBay)
            {
                yield return new WaitForSeconds(1.0f);
            }
            RemoveCompletedObjective(segments[3]);

            yield return new WaitForSeconds(2.0f);

            TriggerTutorialSegment(4); // treat burns

            while (patient1.CharacterHealth.GetAfflictionStrength("burn") > 0.01f)
            {
                //TODO: highlight patient
                yield return null;
            }
            RemoveCompletedObjective(segments[4]);
            yield return new WaitForSeconds(1.0f);

            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Doctor.Radio.AssistantBurnsHealed"), ChatMessageType.Radio, null);

            // treat unconscious patient  ------------------------------------------------------

            //patient calls for help
            patient2.CanSpeak = true;
            newOrder = new Order(Order.PrefabList.Find(o => o.AITag == "requestfirstaid"), patient2.CurrentHull, null);
            GameMain.GameSession.CrewManager.AddOrder(newOrder, newOrder.FadeOutTime);
            patient2.Speak(newOrder.GetChatMessage("", patient1.CurrentHull?.RoomName, givingOrderToSelf: false), ChatMessageType.Order);
            patient2.AIController.Enabled = true;
            patient2.Oxygen = -50;
            CoroutineManager.StartCoroutine(KeepPatientAlive(patient2), "KeepPatient2Alive");

            while (doctor.CurrentHull != patient2.CurrentHull)
            {
                yield return new WaitForSeconds(1.0f);
            }
            yield return new WaitForSeconds(3.0f);

            TriggerTutorialSegment(5); // perform CPR

            while (patient2.IsUnconscious)
            {
                if (CharacterHealth.OpenHealthWindow != null && doctor.AnimController.Anim != AnimController.Animation.CPR)
                {
                    CharacterHealth.OpenHealthWindow.CPRButton.Pulsate(Vector2.One, Vector2.One * 1.5f, 1.0f);
                    CharacterHealth.OpenHealthWindow.CPRButton.Flash();
                }
                yield return null;
            }
            RemoveCompletedObjective(segments[5]);
            CoroutineManager.StopCoroutines("KeepPatient2Alive");

            while (doctor.Submarine != Submarine.MainSub)
            {
                yield return new WaitForSeconds(1.0f);
            }
            yield return new WaitForSeconds(5.0f);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Doctor.Radio.EnteredSub"), ChatMessageType.Radio, null);

            yield return new WaitForSeconds(3.0f);
            TriggerTutorialSegment(6); // give treatment to anyone in need

            foreach (var patient in subPatients)
            {
                patient.CanSpeak = true;
                patient.AIController.Enabled = true;
            }
            subPatients[2].Oxygen = -50;
            CoroutineManager.StartCoroutine(KeepPatientAlive(subPatients[2]), "KeepPatient3Alive");

            double subEnterTime = Timing.TotalTime;

            bool[] patientCalledHelp = new bool[] { false, false, false };
            while (subPatients.Any(p => p.Vitality < p.MaxVitality * 0.9f && !p.IsDead))
            {
                for (int i = 0; i < subPatients.Count; i++)
                {
                    //make patients call for help to make sure the player finds them
                    //(within 1 minute intervals of entering the sub)
                    if (!patientCalledHelp[i] && Timing.TotalTime > subEnterTime + 60 * (i + 1))
                    {
                        newOrder = new Order(Order.PrefabList.Find(o => o.AITag == "requestfirstaid"), subPatients[i].CurrentHull, null);
                        GameMain.GameSession.CrewManager.AddOrder(newOrder, newOrder.FadeOutTime);

                        string message = newOrder.GetChatMessage("", subPatients[i].CurrentHull?.RoomName, givingOrderToSelf: false);
                        if (subPatients[i].CanSpeak)
                        {
                            subPatients[i].Speak(message, ChatMessageType.Order);                   
                        }
                        else
                        {
                            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, message, ChatMessageType.Radio, null);
                        }
                        patientCalledHelp[i] = true;
                    }
                }
                yield return new WaitForSeconds(1.0f);
            }
            RemoveCompletedObjective(segments[6]);

            // END TUTORIAL
            Completed = true;
        }

        public IEnumerable<object> KeepPatientAlive(Character patient)
        {
            while (patient != null && !patient.Removed)
            {
                patient.Oxygen = Math.Max(patient.Oxygen, -50);
                yield return null;
            }
        }
    }
}
