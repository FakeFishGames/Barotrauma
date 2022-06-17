using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Tutorials
{
    class DoctorTutorial : ScenarioTutorial
    {
        // Room 1
        private float shakeTimer = 1f;
        private float shakeAmount = 20f;

        private LocalizedString radioSpeakerName;
        private Character doctor;

        private ItemContainer doctor_suppliesCabinet;
        private ItemContainer doctor_medBayCabinet;
        private Character patient1, patient2;
        private List<Character> subPatients;
        private Hull medBay;

        private Door doctor_firstDoor;
        private Door doctor_secondDoor;
        private Door doctor_thirdDoor;
        private Door tutorial_upperFinalDoor;
        private Door tutorial_lockedDoor_2;

        private LightComponent doctor_firstDoorLight;
        private LightComponent doctor_secondDoorLight;
        private LightComponent doctor_thirdDoorLight;
        private Door tutorial_submarineDoor;
        private LightComponent tutorial_submarineDoorLight;

        // Variables
        private Sprite doctor_firstAidIcon;
        private Color doctor_firstAidIconColor;


        public DoctorTutorial() : base("tutorial.medicaldoctortraining".ToIdentifier(),
            new Segment(
                "Doctor.Supplies".ToIdentifier(),
                "Doctor.SuppliesObjective".ToIdentifier(),
                TutorialContentType.TextOnly,
                textContent: new Segment.Text { Tag = "Doctor.SuppliesText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center }),
            new Segment(
                "Doctor.OpenMedicalInterface".ToIdentifier(),
                "Doctor.OpenMedicalInterfaceObjective".ToIdentifier(),
                TutorialContentType.ManualVideo,
                textContent: new Segment.Text { Tag = "Doctor.OpenMedicalInterfaceText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center },
                videoContent: new Segment.Video { File = "tutorial_medinterface1.webm", TextTag = "Doctor.OpenMedicalInterfaceText".ToIdentifier(), Width = 450, Height = 80 }),
            new Segment(
                "Doctor.FirstAidSelf".ToIdentifier(),
                "Doctor.FirstAidSelfObjective".ToIdentifier(),
                TutorialContentType.ManualVideo,
                textContent: new Segment.Text { Tag = "Doctor.FirstAidSelfText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center },
                videoContent: new Segment.Video { File = "tutorial_medinterface1.webm", TextTag = "Doctor.FirstAidSelfText".ToIdentifier(), Width = 450, Height = 80 }),
            new Segment(
                "Doctor.Medbay".ToIdentifier(),
                "Doctor.MedbayObjective".ToIdentifier(),
                TutorialContentType.ManualVideo,
                textContent: new Segment.Text { Tag = "Doctor.MedbayText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center },
                videoContent: new Segment.Video { File = "tutorial_command.webm", TextTag = "Doctor.MedbayText".ToIdentifier(), Width = 450, Height = 80 }),
            new Segment(
                "Doctor.TreatBurns".ToIdentifier(),
                "Doctor.TreatBurnsObjective".ToIdentifier(),
                TutorialContentType.ManualVideo,
                textContent: new Segment.Text { Tag = "Doctor.TreatBurnsText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center },
                videoContent: new Segment.Video { File = "tutorial_medinterface2.webm", TextTag = "Doctor.TreatBurnsText".ToIdentifier(), Width = 450, Height = 80 }),
            new Segment(
                "Doctor.CPR".ToIdentifier(),
                "Doctor.CPRObjective".ToIdentifier(),
                TutorialContentType.ManualVideo,
                textContent: new Segment.Text { Tag = "Doctor.CPRText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center },
                videoContent: new Segment.Video { File = "tutorial_cpr.webm", TextTag = "Doctor.CPRText".ToIdentifier(), Width = 450, Height = 80 }),
            new Segment(
                "Doctor.Submarine".ToIdentifier(),
                "Doctor.SubmarineObjective".ToIdentifier(),
                TutorialContentType.TextOnly,
                textContent: new Segment.Text { Tag = "Doctor.SubmarineText".ToIdentifier(), Width = 450, Height = 80, Anchor = Anchor.Center }))
        { }

        protected override CharacterInfo GetCharacterInfo()
        {
            return new CharacterInfo(
                CharacterPrefab.HumanSpeciesName,
                jobOrJobPrefab: new Job(
                    JobPrefab.Prefabs["medicaldoctor"], Rand.RandSync.Unsynced, 0,
                    new Skill("medical".ToIdentifier(), 70),
                    new Skill("weapons".ToIdentifier(), 20),
                    new Skill("mechanical".ToIdentifier(), 20),
                    new Skill("electrical".ToIdentifier(), 20),
                    new Skill("helm".ToIdentifier(), 20)));
        }

        protected override void Initialize()
        {
            var firstAidOrder = OrderPrefab.Prefabs["requestfirstaid"];
            doctor_firstAidIcon = firstAidOrder.SymbolSprite;
            doctor_firstAidIconColor = firstAidOrder.Color;

            subPatients = new List<Character>();
            radioSpeakerName = TextManager.Get("Tutorial.Radio.Speaker");
            doctor = Character.Controlled;

            foreach (Item item in doctor.Inventory.AllItemsMod)
            {
                if (item.HasTag("clothing") || item.HasTag("identitycard") || item.HasTag("mobileradio")) { continue; }
                item.Unequip(doctor);
                doctor.Inventory.RemoveItem(item);
            }

            doctor_suppliesCabinet = Item.ItemList.Find(i => i.HasTag("doctor_suppliescabinet"))?.GetComponent<ItemContainer>();
            doctor_medBayCabinet = Item.ItemList.Find(i => i.HasTag("doctor_medbaycabinet"))?.GetComponent<ItemContainer>();

            var patientHull1 = WayPoint.WayPointList.Find(wp => wp.IdCardDesc == "waitingroom").CurrentHull;
            var patientHull2 = WayPoint.WayPointList.Find(wp => wp.IdCardDesc == "airlock").CurrentHull;
            medBay = WayPoint.WayPointList.Find(wp => wp.IdCardDesc == "medbay").CurrentHull;

            var assistantInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobOrJobPrefab: JobPrefab.Get("assistant"))
            {
                TeamID = CharacterTeamType.Team1
            };
            patient1 = Character.Create(assistantInfo, patientHull1.WorldPosition, "1");
            patient1.GiveJobItems(null);
            patient1.CanSpeak = false;
            patient1.Params.Health.BurnReduction = 0;
            patient1.AddDamage(patient1.WorldPosition, new List<Affliction>() { new Affliction(AfflictionPrefab.Burn, 15.0f) }, stun: 0, playSound: false);
            patient1.AIController.Enabled = false;

            assistantInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobOrJobPrefab: JobPrefab.Get("assistant"))
            {
                TeamID = CharacterTeamType.Team1
            };
            patient2 = Character.Create(assistantInfo, patientHull2.WorldPosition, "2");
            patient2.GiveJobItems(null);
            patient2.CanSpeak = false;
            patient2.AIController.Enabled = false;

            var mechanicInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobOrJobPrefab: JobPrefab.Get("engineer"))
            {
                TeamID = CharacterTeamType.Team1
            };
            var subPatient1 = Character.Create(mechanicInfo, WayPoint.GetRandom(SpawnType.Human, mechanicInfo.Job?.Prefab, Submarine.MainSub).WorldPosition, "3");
            subPatient1.Params.Health.BurnReduction = 0;
            subPatient1.AddDamage(patient1.WorldPosition, new List<Affliction>() { new Affliction(AfflictionPrefab.Burn, 40.0f) }, stun: 0, playSound: false);
            subPatients.Add(subPatient1);

            var securityInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobOrJobPrefab: JobPrefab.Get("securityofficer"));
            var subPatient2 = Character.Create(securityInfo, WayPoint.GetRandom(SpawnType.Human, securityInfo.Job?.Prefab, Submarine.MainSub).WorldPosition, "3");
            subPatient2.TeamID = CharacterTeamType.Team1;
            subPatient2.AddDamage(patient1.WorldPosition, new List<Affliction>() { new Affliction(AfflictionPrefab.InternalDamage, 40.0f) }, stun: 0, playSound: false);
            subPatients.Add(subPatient2);

            var engineerInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobOrJobPrefab: JobPrefab.Get("engineer"))
            {
                TeamID = CharacterTeamType.Team1
            };
            var subPatient3 = Character.Create(securityInfo, WayPoint.GetRandom(SpawnType.Human, engineerInfo.Job?.Prefab, Submarine.MainSub).WorldPosition, "3");
            subPatient3.Params.Health.BurnReduction = 0;
            subPatient3.AddDamage(patient1.WorldPosition, new List<Affliction>() { new Affliction(AfflictionPrefab.Burn, 20.0f) }, stun: 0, playSound: false);
            subPatients.Add(subPatient3);

            doctor_firstDoor = Item.ItemList.Find(i => i.HasTag("doctor_firstdoor")).GetComponent<Door>();
            doctor_secondDoor = Item.ItemList.Find(i => i.HasTag("doctor_seconddoor")).GetComponent<Door>();
            doctor_thirdDoor = Item.ItemList.Find(i => i.HasTag("doctor_thirddoor")).GetComponent<Door>();
            tutorial_upperFinalDoor = Item.ItemList.Find(i => i.HasTag("tutorial_upperfinaldoor")).GetComponent<Door>();
            doctor_firstDoorLight = Item.ItemList.Find(i => i.HasTag("doctor_firstdoorlight")).GetComponent<LightComponent>();
            doctor_secondDoorLight = Item.ItemList.Find(i => i.HasTag("doctor_seconddoorlight")).GetComponent<LightComponent>();
            doctor_thirdDoorLight = Item.ItemList.Find(i => i.HasTag("doctor_thirddoorlight")).GetComponent<LightComponent>();
            SetDoorAccess(doctor_firstDoor, doctor_firstDoorLight, false);
            SetDoorAccess(doctor_secondDoor, doctor_secondDoorLight, false);
            SetDoorAccess(doctor_thirdDoor, doctor_thirdDoorLight, false);
            tutorial_submarineDoor = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoor")).GetComponent<Door>();
            tutorial_submarineDoorLight = Item.ItemList.Find(i => i.HasTag("tutorial_submarinedoorlight")).GetComponent<LightComponent>();
            SetDoorAccess(tutorial_submarineDoor, tutorial_submarineDoorLight, false);
            tutorial_lockedDoor_2 = Item.ItemList.Find(i => i.HasTag("tutorial_lockeddoor_2")).GetComponent<Door>();
            SetDoorAccess(tutorial_lockedDoor_2, null, true);


            foreach (var patient in subPatients)
            {
                patient.CanSpeak = false;
                patient.AIController.Enabled = false;
                patient.GiveJobItems();
            }

            Item reactorItem = Item.ItemList.Find(i => i.Submarine == Submarine.MainSub && i.GetComponent<Reactor>() != null);
            reactorItem.GetComponent<Reactor>().AutoTemp = true;

            GameAnalyticsManager.AddDesignEvent("Tutorial:DoctorTutorial:Started");
            GameAnalyticsManager.AddDesignEvent("Tutorial:Started");
        }

        public override IEnumerable<CoroutineStatus> UpdateState()
        {
            while (GameMain.Instance.LoadingScreenOpen) yield return null;

            // explosions and radio messages ------------------------------------------------------

            yield return new WaitForSeconds(3.0f, false);

            //SoundPlayer.PlayDamageSound("StructureBlunt", 10, Character.Controlled.WorldPosition);
            //// Room 1
            //while (shakeTimer > 0.0f) // Wake up, shake
            //{
            //    shakeTimer -= 0.1f;
            //    GameMain.GameScreen.Cam.Shake = shakeAmount;
            //    yield return new WaitForSeconds(0.1f);
            //}
            //yield return new WaitForSeconds(2.5f);
            //GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Mechanic.Radio.WakeUp"), ChatMessageType.Radio, null);

            //yield return new WaitForSeconds(2.5f);

            doctor.SetStun(1.5f);
            var explosion = new Explosion(range: 100, force: 10, damage: 0, structureDamage: 0, itemDamage: 0);
            explosion.DisableParticles();
            GameMain.GameScreen.Cam.Shake = shakeAmount;
            explosion.Explode(Character.Controlled.WorldPosition - Vector2.UnitX * 25, null);
            SoundPlayer.PlayDamageSound("StructureBlunt", 10, Character.Controlled.WorldPosition - Vector2.UnitX * 25);

            yield return new WaitForSeconds(0.5f, false);

            doctor.DamageLimb(
                Character.Controlled.WorldPosition,
                doctor.AnimController.GetLimb(LimbType.Torso),
                new List<Affliction> { new Affliction(AfflictionPrefab.InternalDamage, 10.0f) },
                stun: 3.0f, playSound: true, attackImpulse: 0.0f);

            shakeTimer = 0.5f;
            while (shakeTimer > 0.0f) // Wake up, shake
            {
                shakeTimer -= 0.1f;
                GameMain.GameScreen.Cam.Shake = shakeAmount;
                yield return new WaitForSeconds(0.1f, false);
            }

            yield return new WaitForSeconds(3.0f, false);
            GameMain.GameSession?.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Doctor.Radio.KnockedDown"), ChatMessageType.Radio, null);

            // first tutorial segment, get medical supplies ------------------------------------------------------

            yield return new WaitForSeconds(1.5f, false);
            SetHighlight(doctor_suppliesCabinet.Item, true);

            /*while (doctor.CurrentHull != doctor_suppliesCabinet.Item.CurrentHull)
            {
                yield return new WaitForSeconds(2.0f);
            }*/

            TriggerTutorialSegment(0, GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Select), GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Deselect), "None"); // Medical supplies objective

            do
            {
                for (int i = 0; i < doctor_suppliesCabinet.Inventory.Capacity; i++)
                {
                    if (doctor_suppliesCabinet.Inventory.GetItemAt(i) != null)
                    {
                        HighlightInventorySlot(doctor_suppliesCabinet.Inventory, i, highlightColor, .5f, .5f, 0f);
                    }
                }
                if (doctor.SelectedConstruction == doctor_suppliesCabinet.Item)
                {
                    for (int i = 0; i < doctor.Inventory.Capacity; i++)
                    {
                        if (doctor.Inventory.GetItemAt(i) == null) { HighlightInventorySlot(doctor.Inventory, i, highlightColor, .5f, .5f, 0f); }
                    }
                }
                yield return null;
            } while (doctor.Inventory.FindItemByIdentifier("antidama1".ToIdentifier()) == null); // Wait until looted
            yield return new WaitForSeconds(1.0f, false);

            SetHighlight(doctor_suppliesCabinet.Item, false);
            RemoveCompletedObjective(0);
            GameAnalyticsManager.AddDesignEvent("Tutorial:DoctorTutorial:Objective0");

            yield return new WaitForSeconds(1.0f, false);

            // 2nd tutorial segment, treat self -------------------------------------------------------------------------

            TriggerTutorialSegment(1, GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Health)); // Open health interface
            while (CharacterHealth.OpenHealthWindow == null)
            {
                doctor.CharacterHealth.HealthBarPulsateTimer = 1.0f;
                yield return null;
            }
            yield return null;
            RemoveCompletedObjective(1);
            GameAnalyticsManager.AddDesignEvent("Tutorial:DoctorTutorial:Objective1");
            yield return new WaitForSeconds(1.0f, false);
            TriggerTutorialSegment(2); //Treat self
            while (doctor.CharacterHealth.GetAfflictionStrength("damage") > 0.01f)
            {
                if (CharacterHealth.OpenHealthWindow == null)
                {
                    doctor.CharacterHealth.HealthBarPulsateTimer = 1.0f;
                }
                else
                {
                    HighlightInventorySlot(doctor.Inventory, "antidama1".ToIdentifier(), highlightColor, .5f, .5f, 0f);
                }

                yield return null;
            }

            RemoveCompletedObjective(2);
            GameAnalyticsManager.AddDesignEvent("Tutorial:DoctorTutorial:Objective2");
            SetDoorAccess(doctor_firstDoor, doctor_firstDoorLight, true);

            while (CharacterHealth.OpenHealthWindow != null)
            {
                yield return new WaitForSeconds(1.0f, false);
            }

            // treat patient --------------------------------------------------------------------------------------------

            //patient 1 requests first aid
            var newOrder = new Order(OrderPrefab.Prefabs["requestfirstaid"], patient1.CurrentHull, null, orderGiver: patient1);
            doctor.AddActiveObjectiveEntity(patient1, doctor_firstAidIcon, doctor_firstAidIconColor);
            //GameMain.GameSession.CrewManager.AddOrder(newOrder, newOrder.FadeOutTime);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(patient1.Name, newOrder.GetChatMessage("", patient1.CurrentHull?.DisplayName?.Value, givingOrderToSelf: false), ChatMessageType.Order, null);

            while (doctor.CurrentHull != patient1.CurrentHull)
            {
                yield return new WaitForSeconds(1.0f, false);
            }
            yield return new WaitForSeconds(0.0f, false);

            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Doctor.Radio.AssistantBurns"), ChatMessageType.Radio, null);
            GameMain.GameSession.CrewManager.AllowCharacterSwitch = false;
            GameMain.GameSession.CrewManager.AddCharacter(doctor);
            GameMain.GameSession.CrewManager.AddCharacter(patient1);
            GameMain.GameSession.CrewManager.AutoShowCrewList();
            patient1.CharacterHealth.UseHealthWindow = false;

            yield return new WaitForSeconds(3.0f, false);
            patient1.AIController.Enabled = true;
            doctor.RemoveActiveObjectiveEntity(patient1);
            TriggerTutorialSegment(3, GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Command)); // Get the patient to medbay

            while (patient1.GetCurrentOrderWithTopPriority()?.Identifier != "follow")
            {
                // TODO: Rework order highlighting for new command UI
                // GameMain.GameSession.CrewManager.HighlightOrderButton(patient1, "follow", highlightColor, new Vector2(5, 5));
                yield return null;
            }

            SetDoorAccess(doctor_secondDoor, doctor_secondDoorLight, true);

            while (patient1.CurrentHull != medBay)
            {
                yield return new WaitForSeconds(1.0f, false);
            }
            RemoveCompletedObjective(3);
            GameAnalyticsManager.AddDesignEvent("Tutorial:DoctorTutorial:Objective3");
            SetHighlight(doctor_medBayCabinet.Item, true);
            SetDoorAccess(doctor_thirdDoor, doctor_thirdDoorLight, true);
            patient1.CharacterHealth.UseHealthWindow = true;

            yield return new WaitForSeconds(2.0f, false);

            TriggerTutorialSegment(4, GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Health)); // treat burns

            do
            {
                for (int i = 0; i < 3; i++)
                {
                    if (doctor_medBayCabinet.Inventory.GetItemAt(i) != null)
                    {
                        HighlightInventorySlot(doctor_medBayCabinet.Inventory, i, highlightColor, .5f, .5f, 0f);
                    }
                }
                if (doctor.SelectedConstruction == doctor_medBayCabinet.Item)
                {
                    for (int i = 0; i < doctor.Inventory.Capacity; i++)
                    {
                        if (doctor.Inventory.GetItemAt(i) == null) { HighlightInventorySlot(doctor.Inventory, i, highlightColor, .5f, .5f, 0f); }
                    }
                }
                yield return null;
            } while (doctor.Inventory.FindItemByIdentifier("antibleeding1".ToIdentifier()) == null); // Wait until looted
            SetHighlight(doctor_medBayCabinet.Item, false);
            SetHighlight(patient1, true);

            while (patient1.CharacterHealth.GetAfflictionStrength("burn") > 0.01f)
            {
                if (CharacterHealth.OpenHealthWindow == null)
                {
                    doctor.CharacterHealth.HealthBarPulsateTimer = 1.0f;
                }
                else
                {
                    HighlightInventorySlot(doctor.Inventory, "antibleeding1".ToIdentifier(), highlightColor, .5f, .5f, 0f);
                }
                yield return null;

            }
            RemoveCompletedObjective(4);
            GameAnalyticsManager.AddDesignEvent("Tutorial:DoctorTutorial:Objective4");
            SetHighlight(patient1, false);
            yield return new WaitForSeconds(1.0f, false);

            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Doctor.Radio.AssistantBurnsHealed"), ChatMessageType.Radio, null);

            // treat unconscious patient  ------------------------------------------------------

            //patient calls for help
            //patient2.CanSpeak = true;
            yield return new WaitForSeconds(2.0f, false);
            newOrder = new Order(OrderPrefab.Prefabs["requestfirstaid"], patient2.CurrentHull, null, orderGiver: patient2);
            doctor.AddActiveObjectiveEntity(patient2, doctor_firstAidIcon, doctor_firstAidIconColor);
            //GameMain.GameSession.CrewManager.AddOrder(newOrder, newOrder.FadeOutTime);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(patient2.Name, newOrder.GetChatMessage("", patient1.CurrentHull?.DisplayName?.Value, givingOrderToSelf: false), ChatMessageType.Order, null);
            patient2.AIController.Enabled = true;
            patient2.Oxygen = -50;
            CoroutineManager.StartCoroutine(KeepPatientAlive(patient2), "KeepPatient2Alive");

            /*while (doctor.CurrentHull != patient2.CurrentHull)
            {
                yield return new WaitForSeconds(1.0f);
            }*/
            do { yield return null; } while (!tutorial_upperFinalDoor.IsOpen);
            yield return new WaitForSeconds(2.0f, false);

            TriggerTutorialSegment(5, GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Health)); // perform CPR
            SetHighlight(patient2, true);
            while (patient2.IsUnconscious)
            {
                if (CharacterHealth.OpenHealthWindow != null && doctor.AnimController.Anim != AnimController.Animation.CPR)
                {
                    //Disabled pulse until it's replaced by a better effect
                    //CharacterHealth.OpenHealthWindow.CPRButton.Pulsate(Vector2.One, Vector2.One * 1.5f, 1.0f);
                    if (CharacterHealth.OpenHealthWindow.CPRButton.FlashTimer <= 0.0f)
                    {
                        CharacterHealth.OpenHealthWindow.CPRButton.Flash(highlightColor);
                    }
                }
                yield return null;
            }
            RemoveCompletedObjective(5);
            GameAnalyticsManager.AddDesignEvent("Tutorial:DoctorTutorial:Objective5");
            SetHighlight(patient2, false);
            doctor.RemoveActiveObjectiveEntity(patient2);
            CoroutineManager.StopCoroutines("KeepPatient2Alive");

            SetDoorAccess(tutorial_submarineDoor, tutorial_submarineDoorLight, true);

            while (doctor.Submarine != Submarine.MainSub)
            {
                yield return new WaitForSeconds(1.0f, false);
            }

            subPatients[2].Oxygen = -50;
            CoroutineManager.StartCoroutine(KeepPatientAlive(subPatients[2]), "KeepPatient3Alive");

            yield return new WaitForSeconds(5.0f, false);
            GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(radioSpeakerName, TextManager.Get("Doctor.Radio.EnteredSub"), ChatMessageType.Radio, null);

            yield return new WaitForSeconds(3.0f, false);
            TriggerTutorialSegment(6, GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Health)); // give treatment to anyone in need

            foreach (var patient in subPatients)
            {
                //patient.CanSpeak = true;
                patient.AIController.Enabled = true;
                SetHighlight(patient, true);
            }

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
                        doctor.AddActiveObjectiveEntity(subPatients[i], doctor_firstAidIcon, doctor_firstAidIconColor);
                        newOrder = new Order(OrderPrefab.Prefabs["requestfirstaid"], subPatients[i].CurrentHull, null, orderGiver: subPatients[i]);
                        string message = newOrder.GetChatMessage("", subPatients[i].CurrentHull?.DisplayName?.Value, givingOrderToSelf: false);
                        GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(subPatients[i].Name, message, ChatMessageType.Order, null);
                        patientCalledHelp[i] = true;
                    }

                    if (subPatients[i].ExternalHighlight && subPatients[i].Vitality >= subPatients[i].MaxVitality * 0.9f)
                    {
                        doctor.RemoveActiveObjectiveEntity(subPatients[i]);
                        SetHighlight(subPatients[i], false);
                    }
                }
                yield return new WaitForSeconds(1.0f, false);
            }
            RemoveCompletedObjective(6);
            GameAnalyticsManager.AddDesignEvent("Tutorial:DoctorTutorial:Objective6");
            foreach (var patient in subPatients)
            {
                SetHighlight(patient, false);
                doctor.RemoveActiveObjectiveEntity(patient);
            }

            // END TUTORIAL
            GameAnalyticsManager.AddDesignEvent("Tutorial:DoctorTutorial:Completed");
            CoroutineManager.StartCoroutine(TutorialCompleted());
        }

        public IEnumerable<CoroutineStatus> KeepPatientAlive(Character patient)
        {
            while (patient != null && !patient.Removed)
            {
                patient.Oxygen = Math.Max(patient.Oxygen, -50);
                yield return null;
            }
        }
    }
}
