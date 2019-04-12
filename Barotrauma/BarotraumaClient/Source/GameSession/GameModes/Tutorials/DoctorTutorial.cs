using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
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
        private Character patient1;
        private Hull startRoom;
        private Hull patientHull1;
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
            
            patientHull1 = Hull.hullList.Find(h => h.RoomName == "Waiting room" && h.Submarine == doctor.Submarine);
            medBay = Hull.hullList.Find(h => h.RoomName == "Med bay" && h.Submarine == doctor.Submarine);

            var assistantInfo = new CharacterInfo(Character.HumanConfigFile, "", JobPrefab.List.Find(jp => jp.Identifier == "assistant"));
            patient1 = Character.Create(assistantInfo, patientHull1.WorldPosition, "asdfsdfg");
            patient1.GiveJobItems(null);
            patient1.CanSpeak = false;
            patient1.AddDamage(patient1.WorldPosition, new List<Affliction>() { new Affliction(AfflictionPrefab.Burn, 45.0f) }, stun: 0, playSound: false);
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

            TriggerTutorialSegment(1); // Treat self objective

            while (doctor.CharacterHealth.GetAfflictionStrength("damage") > 0.01f)
            {
                if (CharacterHealth.OpenHealthWindow == null)
                {
                    doctor.CharacterHealth.HealthBarPulsateTimer = 1.0f;
                }
                else
                {
                    HighlightInventorySlot(doctor.Inventory, "morphine", highlightColor, .5f, .5f, 0f);
                }

                yield return null;
            }

            RemoveCompletedObjective(segments[1]);

            while (CharacterHealth.OpenHealthWindow != null)
            {
                yield return new WaitForSeconds(1.0f);
            }

            // treat patient --------------------------------------------------------------------------------------------

            //patient 1 requests first aid
            patient1.CanSpeak = true;
            var orderPrefab = Order.PrefabList.Find(o => o.AITag == "requestfirstaid");
            var newOrder = new Order(orderPrefab, patient1.CurrentHull, null);
            GameMain.GameSession.CrewManager.AddOrder(newOrder, newOrder.FadeOutTime);
            patient1.Speak(newOrder.GetChatMessage("", patient1.CurrentHull?.RoomName, givingOrderToSelf: false), ChatMessageType.Order);
            
            while (doctor.CurrentHull != patientHull1)
            {
                yield return new WaitForSeconds(1.0f);
            }

            TriggerTutorialSegment(2); // Get the patient to medbay

            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Doctor.Radio.AssistantBurns"), ChatMessageType.Radio, null);
            GameMain.GameSession.CrewManager.AllowCharacterSwitch = false;
            GameMain.GameSession.CrewManager.AddCharacter(patient1);
            GameMain.GameSession.CrewManager.ToggleCrewAreaOpen = true;

            while (patient1.CurrentOrder == null || patient1.CurrentOrder.AITag != "follow")
            {
                GameMain.GameSession.CrewManager.HighlightOrderButton(patient1, "follow", highlightColor);
                yield return new WaitForSeconds(1.0f);
            }

            while (patient1.CurrentHull != medBay)
            {
                yield return new WaitForSeconds(1.0f);
            }
            RemoveCompletedObjective(segments[2]);

            TriggerTutorialSegment(3); // treat burns
            
            while (patient1.CharacterHealth.GetAfflictionStrength("burn") > 0.01f)
            {
                //TODO: highlight patient
                yield return null;
            }
            RemoveCompletedObjective(segments[3]);
            yield return new WaitForSeconds(1.0f);

            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Doctor.Radio.AssistantBurnsHealed"), ChatMessageType.Radio, null);
            
            // treat unconscious patient  ------------------------------------------------------

            // END TUTORIAL
            Completed = true;
        }
    }
}
