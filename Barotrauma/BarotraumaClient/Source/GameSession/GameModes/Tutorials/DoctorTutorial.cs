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

            patientHull1 = Hull.hullList.Find(h => h.RoomName == "Med bay" && h.Submarine == doctor.Submarine);
            medBay = Hull.hullList.Find(h => h.RoomName == "Med bay" && h.Submarine == doctor.Submarine);

            var assistantInfo = new CharacterInfo(Character.HumanConfigFile, "", JobPrefab.List.Find(jp => jp.Identifier == "assistant"));
            patient1 = Character.Create(assistantInfo, patientHull1.WorldPosition, "asdfsdfg");
            patient1.AddDamage(patient1.WorldPosition, new List<Affliction>() { new Affliction(AfflictionPrefab.Burn, 45.0f) }, stun: 0, playSound: false);
        }

        public override IEnumerable<object> UpdateState()
        {
            yield return new WaitForSeconds(1.0f);
            SoundPlayer.PlayDamageSound("StructureBlunt", 10, Character.Controlled.WorldPosition + Vector2.UnitY * 50);
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

            var explosion = new Explosion(range: 100, force: 10, damage: 0, structureDamage: 0);
            explosion.DisableParticles();
            GameMain.GameScreen.Cam.Shake = shakeAmount;
            explosion.Explode(Character.Controlled.WorldPosition - Vector2.UnitX * 50, null);
            SoundPlayer.PlayDamageSound("StructureBlunt", 10, Character.Controlled.WorldPosition - Vector2.UnitX * 50);
            doctor.DamageLimb(
                Character.Controlled.WorldPosition, 
                doctor.AnimController.GetLimb(LimbType.Head),
                new List<Affliction> { new Affliction(AfflictionPrefab.InternalDamage, 10.0f) }, 
                stun: 3.0f, playSound: true, attackImpulse: 0.0f);


            yield return new WaitForSeconds(5.0f);
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Doctor.Radio.KnockedDown"), ChatMessageType.Radio, null);

            TriggerTutorialSegment(0); // Medical supplies objective

            SetHighlight(doctor_suppliesCabinet.Item, true);
            do
            {
                for (int i = 0; i < doctor_suppliesCabinet.Inventory.Items.Length; i++)
                {
                    if (doctor_suppliesCabinet.Inventory.Items[i] != null)
                    {
                        HighlightInventorySlot(doctor_suppliesCabinet.Inventory, i, highlightColor, .5f, .5f, 0f);
                    }
                }
                for (int i = 0; i < doctor.Inventory.slots.Length; i++)
                {
                    if (doctor.Inventory.Items[i] == null) HighlightInventorySlot(doctor.Inventory, i, highlightColor, .5f, .5f, 0f);
                }

                yield return null;
            } while (doctor.Inventory.FindItemByIdentifier("morphine") == null); // Wait until looted
            yield return new WaitForSeconds(1.0f);

            SetHighlight(doctor_suppliesCabinet.Item, false);
            RemoveCompletedObjective(segments[0]);
            yield return new WaitForSeconds(1.0f);

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

            //patient 1 requests first aid
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

            // END TUTORIAL
            Completed = true;
        }
    }
}
