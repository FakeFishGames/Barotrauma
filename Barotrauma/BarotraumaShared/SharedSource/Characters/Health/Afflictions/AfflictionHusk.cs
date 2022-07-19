using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class AfflictionHusk : Affliction
    {
        public enum InfectionState
        {
            Initial, Dormant, Transition, Active, Final
        }

        private bool subscribedToDeathEvent;

        private InfectionState state;

        private List<Limb> huskAppendage;

        private Character character;

        private bool stun = true;

        private readonly List<Affliction> huskInfection = new List<Affliction>();

        [Serialize(0f, IsPropertySaveable.Yes), Editable]
        public override float Strength
        {
            get { return _strength; }
            set
            {
                // Don't allow to set the strength too high (from outside) to avoid rapid transformation into husk when taking lots of damage from husks.
                float previousValue = _strength;
                float threshold = _strength > ActiveThreshold ? ActiveThreshold + 1 : DormantThreshold - 1;
                float max = Math.Max(threshold, previousValue);
                _strength = Math.Clamp(value, 0, max);
                stun = GameMain.GameSession?.IsRunning ?? true;
                if (previousValue > 0.0f && value <= 0.0f)
                {
                    DeactivateHusk();
                    highestStrength = 0;
                }
            }
        }
        private float highestStrength;

        public InfectionState State
        {
            get { return state; }
            private set
            {
                if (state == value) { return; }
                state = value;
            }
        }

        private readonly AfflictionPrefabHusk HuskPrefab;

        private float DormantThreshold => HuskPrefab.DormantThreshold;
        private float ActiveThreshold => HuskPrefab.ActiveThreshold;
        private float TransitionThreshold => HuskPrefab.TransitionThreshold;
        private float TransformThresholdOnDeath => HuskPrefab.TransformThresholdOnDeath;

        public AfflictionHusk(AfflictionPrefab prefab, float strength) : base(prefab, strength)
        {
            HuskPrefab = prefab as AfflictionPrefabHusk;
            if (HuskPrefab == null)
            {
                DebugConsole.ThrowError("Error in husk affliction definition: the prefab is of wrong type!");
            }
        }

        public override void Update(CharacterHealth characterHealth, Limb targetLimb, float deltaTime)
        {
            if (HuskPrefab == null) { return; }
            base.Update(characterHealth, targetLimb, deltaTime);
            highestStrength = Math.Max(_strength, highestStrength);
            character = characterHealth.Character;
            if (character == null) { return; }

            UpdateMessages();

            if (!subscribedToDeathEvent)
            {
                character.OnDeath += CharacterDead;
                subscribedToDeathEvent = true;
            }
            if (Strength < DormantThreshold)
            {
                DeactivateHusk();
                if (Strength > Math.Min(1.0f, DormantThreshold))
                {
                    State = InfectionState.Dormant;
                }
            }
            else if (Strength < ActiveThreshold)
            {
                DeactivateHusk();
                if (Prefab is AfflictionPrefabHusk { CauseSpeechImpediment: true })
                {
                    character.SpeechImpediment = 30;
                }
                State = InfectionState.Transition;
            }
            else if (Strength < TransitionThreshold)
            {
                if (State != InfectionState.Active && stun)
                {
                    character.SetStun(Rand.Range(2f, 3f));
                }
                if (Prefab is AfflictionPrefabHusk { CauseSpeechImpediment: true })
                {
                    character.SpeechImpediment = 100;
                }
                State = InfectionState.Active;
                ActivateHusk();
            }
            else
            {
                State = InfectionState.Final;
                ActivateHusk();
                ApplyDamage(deltaTime, applyForce: true);
                character.SetStun(5);
            }
        }

        private InfectionState? prevDisplayedMessage;
        private void UpdateMessages()
        {
            if (Prefab is AfflictionPrefabHusk { SendMessages: false }) { return; }
            if (prevDisplayedMessage.HasValue && prevDisplayedMessage.Value == State) { return; }
            if (highestStrength > Strength) { return; }

            switch (State)
            {
                case InfectionState.Dormant:
                    if (Strength < DormantThreshold * 0.5f)
                    {
                        return;
                    }
                    if (character == Character.Controlled)
                    {
#if CLIENT
                        GUI.AddMessage(TextManager.Get("HuskDormant"), GUIStyle.Red);
#endif
                    }
                    else if (character.IsBot)
                    {
                        character.Speak(TextManager.Get("dialoghuskdormant").Value, delay: Rand.Range(0.5f, 5.0f), identifier: "huskdormant".ToIdentifier());
                    }
                    break;
                case InfectionState.Transition:
                    if (character == Character.Controlled)
                    {
#if CLIENT
                        GUI.AddMessage(TextManager.Get("HuskCantSpeak"), GUIStyle.Red);
#endif
                    }
                    else if (character.IsBot)
                    {
                        character.Speak(TextManager.Get("dialoghuskcantspeak").Value, delay: Rand.Range(0.5f, 5.0f), identifier: "huskcantspeak".ToIdentifier());
                    }
                    break;
                case InfectionState.Active:
#if CLIENT
                    if (character == Character.Controlled && character.Params.UseHuskAppendage)
                    {
                        GUI.AddMessage(TextManager.GetWithVariable("HuskActivate", "[Attack]", GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Attack)), GUIStyle.Red);
                    }
#endif
                    break;
                case InfectionState.Final:
                default:
                    break;
            }
            prevDisplayedMessage = State;
        }

        private void ApplyDamage(float deltaTime, bool applyForce)
        {
            int limbCount = character.AnimController.Limbs.Count(l => !l.IgnoreCollisions && !l.IsSevered && !l.Hidden);
            foreach (Limb limb in character.AnimController.Limbs)
            {
                if (limb.IsSevered) { continue; }
                if (limb.Hidden) { continue; }
                float random = Rand.Value();
                huskInfection.Clear();
                huskInfection.Add(AfflictionPrefab.InternalDamage.Instantiate(random * 10 * deltaTime / limbCount));
                character.LastDamageSource = null;
                float force = applyForce ? random * 0.5f * limb.Mass : 0;
                character.DamageLimb(limb.WorldPosition, limb, huskInfection, 0, false, force);
            }
        }

        public void ActivateHusk()
        {
            if (huskAppendage == null && character.Params.UseHuskAppendage)
            {
                huskAppendage = AttachHuskAppendage(character, Prefab.Identifier);
            }

            if (Prefab is AfflictionPrefabHusk { NeedsAir: false })
            {
                character.NeedsAir = false;
            }

            if (Prefab is AfflictionPrefabHusk { CauseSpeechImpediment: true })
            {
                character.SpeechImpediment = 100;
            }
        }

        private void DeactivateHusk()
        {
            if (character?.AnimController == null || character.Removed) { return; }
            if (Prefab is AfflictionPrefabHusk { NeedsAir: false })
            {
                character.NeedsAir = character.Params.MainElement.GetAttributeBool("needsair", false);
            }

            if (huskAppendage != null)
            {
                huskAppendage.ForEach(l => character.AnimController.RemoveLimb(l));
                huskAppendage = null;
            }
        }

        public void UnsubscribeFromDeathEvent()
        {
            if (character == null || !subscribedToDeathEvent) { return; }
            character.OnDeath -= CharacterDead;
            subscribedToDeathEvent = false;
        }

        private void CharacterDead(Character character, CauseOfDeath causeOfDeath)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (Strength < TransformThresholdOnDeath || character.Removed || 
                character.CharacterHealth.GetAllAfflictions().Any(a => a.GetActiveEffect()?.BlockTransformation.Contains(Prefab.Identifier) ?? false)) 
            {
                UnsubscribeFromDeathEvent();
                return; 
            }

            //don't turn the character into a husk if any of its limbs are severed
            if (character.AnimController?.LimbJoints != null)
            {
                foreach (var limbJoint in character.AnimController.LimbJoints)
                {
                    if (limbJoint.IsSevered) { return; }
                }
            }

            //create the AI husk in a coroutine to ensure that we don't modify the character list while enumerating it
            CoroutineManager.StartCoroutine(CreateAIHusk());
        }

        private IEnumerable<CoroutineStatus> CreateAIHusk()
        {
            //character already in remove queue (being removed by something else, for example a modded affliction that uses AfflictionHusk as the base)
            // -> don't spawn the AI husk
            if (Entity.Spawner.IsInRemoveQueue(character))
            {
                yield return CoroutineStatus.Success;
            }
#if SERVER
            var client = GameMain.Server?.ConnectedClients.FirstOrDefault(c => c.Character == character);
#endif
            character.Enabled = false;
            Entity.Spawner.AddEntityToRemoveQueue(character);
            UnsubscribeFromDeathEvent();

            Identifier huskedSpeciesName = GetHuskedSpeciesName(character.SpeciesName, Prefab as AfflictionPrefabHusk);
            CharacterPrefab prefab = CharacterPrefab.FindBySpeciesName(huskedSpeciesName);

            if (prefab == null)
            {
                DebugConsole.ThrowError("Failed to turn character \"" + character.Name + "\" into a husk - husk config file not found.");
                yield return CoroutineStatus.Success;
            }

            XElement parentElement = new XElement("CharacterInfo");
            XElement infoElement = character.Info?.Save(parentElement);
            CharacterInfo huskCharacterInfo = infoElement == null ? null : new CharacterInfo(infoElement);

            if (huskCharacterInfo != null)
            {
                var bodyTint = GetBodyTint();
                huskCharacterInfo.Head.SkinColor =
                        Color.Lerp(huskCharacterInfo.Head.SkinColor, bodyTint.Opaque(), bodyTint.A / 255.0f);
            }

            var husk = Character.Create(huskedSpeciesName, character.WorldPosition, ToolBox.RandomSeed(8), huskCharacterInfo, isRemotePlayer: false, hasAi: true);
            if (husk.Info != null)
            {
                husk.Info.Character = husk;
                husk.Info.TeamID = CharacterTeamType.None;
            }

            if (Prefab is AfflictionPrefabHusk huskPrefab)
            {
                if (huskPrefab.ControlHusk)
                {
#if SERVER
                    if (client != null)
                    {
                        GameMain.Server.SetClientCharacter(client, husk);
                    }
#else
                    if (!character.IsRemotelyControlled && character == Character.Controlled)
                    {
                        Character.Controlled = husk; 
                    }
#endif
                }
            }

            foreach (Limb limb in husk.AnimController.Limbs)
            {
                if (limb.type == LimbType.None)
                {
                    limb.body.SetTransform(character.SimPosition, 0.0f);
                    continue;
                }

                var matchingLimb = character.AnimController.GetLimb(limb.type);
                if (matchingLimb?.body != null)
                {
                    limb.body.SetTransform(matchingLimb.SimPosition, matchingLimb.Rotation);
                    limb.body.LinearVelocity = matchingLimb.LinearVelocity;
                    limb.body.AngularVelocity = matchingLimb.body.AngularVelocity;
                }
            }

            if ((Prefab as AfflictionPrefabHusk)?.TransferBuffs ?? false)
            {
                foreach (Affliction affliction in character.CharacterHealth.GetAllAfflictions())
                {
                    if (affliction.Prefab.IsBuff)
                    {
                        husk.CharacterHealth.ApplyAffliction(
                            character.CharacterHealth.GetAfflictionLimb(affliction), 
                            affliction.Prefab.Instantiate(affliction.Strength));
                    }
                }
            }

            if (character.Inventory != null && husk.Inventory != null)
            {
                for (int i = 0; i < character.Inventory.Capacity && i < husk.Inventory.Capacity; i++)
                {
                    character.Inventory.GetItemsAt(i).ForEachMod(item => husk.Inventory.TryPutItem(item, i, true, false, null));
                }
            }

            husk.SetStun(5);
            yield return new WaitForSeconds(5, false);
#if CLIENT
            husk?.PlaySound(CharacterSound.SoundType.Idle);
#endif
            yield return CoroutineStatus.Success;
        }

        public static List<Limb> AttachHuskAppendage(Character character, Identifier afflictionIdentifier, ContentXElement appendageDefinition = null, Ragdoll ragdoll = null)
        {
            var appendage = new List<Limb>();
            if (!(AfflictionPrefab.List.FirstOrDefault(ap => ap.Identifier == afflictionIdentifier) is AfflictionPrefabHusk matchingAffliction))
            {
                DebugConsole.ThrowError($"Could not find an affliction of type 'huskinfection' that matches the affliction '{afflictionIdentifier}'!");
                return appendage;
            }
            Identifier nonhuskedSpeciesName = GetNonHuskedSpeciesName(character.SpeciesName, matchingAffliction);
            Identifier huskedSpeciesName = GetHuskedSpeciesName(nonhuskedSpeciesName, matchingAffliction);
            CharacterPrefab huskPrefab = CharacterPrefab.FindBySpeciesName(huskedSpeciesName);
            if (huskPrefab?.ConfigElement == null)
            {
                DebugConsole.ThrowError($"Failed to find the config file for the husk infected species with the species name '{huskedSpeciesName}'!");
                return appendage;
            }
            var mainElement = huskPrefab.ConfigElement;
            var element = appendageDefinition;
            if (element == null)
            {
                element = mainElement.GetChildElements("huskappendage").FirstOrDefault(e => e.GetAttributeIdentifier("affliction", Identifier.Empty) == afflictionIdentifier);
            }
            if (element == null)
            {
                DebugConsole.ThrowError($"Error in '{huskPrefab.FilePath}': Failed to find a huskappendage that matches the affliction with an identifier '{afflictionIdentifier}'!");
                return appendage;
            }
            ContentPath pathToAppendage = element.GetAttributeContentPath("path") ?? ContentPath.Empty;
            XDocument doc = XMLExtensions.TryLoadXml(pathToAppendage);
            if (doc == null) { return appendage; }
            if (ragdoll == null)
            {
                ragdoll = character.AnimController;
            }
            if (ragdoll.Dir < 1.0f)
            {
                ragdoll.Flip();
            }

            var root = doc.Root.FromPackage(pathToAppendage.ContentPackage);
            var limbElements = root.GetChildElements("limb").ToDictionary(e => e.GetAttributeString("id", null), e => e);
            foreach (var jointElement in root.GetChildElements("joint"))
            {
                if (limbElements.TryGetValue(jointElement.GetAttributeString("limb2", null), out ContentXElement limbElement))
                {
                    var jointParams = new RagdollParams.JointParams(jointElement, ragdoll.RagdollParams);
                    Limb attachLimb = null;
                    if (matchingAffliction.AttachLimbId > -1)
                    {
                        attachLimb = ragdoll.Limbs.FirstOrDefault(l => !l.IsSevered && l.Params.ID == matchingAffliction.AttachLimbId);
                    }
                    else if (matchingAffliction.AttachLimbName != null)
                    {
                        attachLimb = ragdoll.Limbs.FirstOrDefault(l => !l.IsSevered && l.Name == matchingAffliction.AttachLimbName);
                    }
                    else if (matchingAffliction.AttachLimbType != LimbType.None)
                    {
                        attachLimb = ragdoll.Limbs.FirstOrDefault(l => !l.IsSevered && l.type == matchingAffliction.AttachLimbType);
                    }
                    if (attachLimb == null)
                    {
                        attachLimb = ragdoll.Limbs.FirstOrDefault(l => !l.IsSevered && l.Params.ID == jointParams.Limb1);
                    }
                    if (attachLimb != null)
                    {
                        jointParams.Limb1 = attachLimb.Params.ID;
                        var appendageLimbParams = new RagdollParams.LimbParams(limbElement, ragdoll.RagdollParams)
                        {
                            // Ensure that we have a valid id for the new limb
                            ID = ragdoll.Limbs.Length
                        };
                        jointParams.Limb2 = appendageLimbParams.ID;
                        Limb huskAppendage = new Limb(ragdoll, character, appendageLimbParams);
                        huskAppendage.body.Submarine = character.Submarine;
                        huskAppendage.body.SetTransform(attachLimb.SimPosition, attachLimb.Rotation);
                        ragdoll.AddLimb(huskAppendage);
                        ragdoll.AddJoint(jointParams);
                        appendage.Add(huskAppendage);
                    }
                }
            }
            return appendage;
        }

        public static Identifier GetHuskedSpeciesName(Identifier speciesName, AfflictionPrefabHusk prefab)
        {
            return prefab.HuskedSpeciesName.Replace(AfflictionPrefabHusk.Tag, speciesName);
        }

        public static Identifier GetNonHuskedSpeciesName(Identifier huskedSpeciesName, AfflictionPrefabHusk prefab)
        {
            Identifier nonTag = prefab.HuskedSpeciesName.Remove(AfflictionPrefabHusk.Tag);
            return huskedSpeciesName.Remove(nonTag);
        }
    }
}
