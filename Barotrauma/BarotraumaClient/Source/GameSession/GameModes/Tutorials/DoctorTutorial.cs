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

        public DoctorTutorial(XElement element) : base(element)
        {
        }
        public override void Start()
        {
            base.Start();

            radioSpeakerName = TextManager.Get("Tutorial.Radio.Speaker");
            doctor = Character.Controlled;

            doctor_suppliesCabinet = Item.ItemList.Find(i => i.HasTag("doctor_suppliescabinet"))?.GetComponent<ItemContainer>();
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
                for (int i = 0; i<doctor_suppliesCabinet.Inventory.Items.Length; i++)
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
            } while (doctor.Inventory.FindItemByIdentifier("something????") == null || doctor.Inventory.FindItemByIdentifier("some_other_thing") == null); // Wait until looted
            SetHighlight(doctor_suppliesCabinet.Item, false);

            // END TUTORIAL
            Completed = true;
        }
    }
}
