using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System;

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

        private readonly List<Affliction> huskInfection = new List<Affliction>();

        [Serialize(0f, true), Editable]
        public override float Strength
        {
            get { return _strength; }
            set
            {
                // Don't allow to set the strength too high (from outside) to avoid rapid transformation into husk when taking lots of damage from husks.
                // If the strength is more than the value, this will effectively reset the current strength to the max. That's why we use two steps.
                float max = _strength > ActiveThreshold ? ActiveThreshold + 1 : DormantThreshold - 1;
                _strength = Math.Clamp(value, 0, max);
            }
        }

        public InfectionState State
        {
            get { return state; }
            private set
            {
                if (state == value) { return; }
                state = value;
                if (character != null && character == Character.Controlled)
                {
                    UpdateMessages();
                }
            }
        }

        private float DormantThreshold => Prefab.MaxStrength * 0.5f;
        private float ActiveThreshold => Prefab.MaxStrength * 0.75f;

        public AfflictionHusk(AfflictionPrefab prefab, float strength) : base(prefab, strength) { }

        public override void Update(CharacterHealth characterHealth, Limb targetLimb, float deltaTime)
        {
            base.Update(characterHealth, targetLimb, deltaTime);
            character = characterHealth.Character;
            if (character == null) { return; }
            if (!subscribedToDeathEvent)
            {
                character.OnDeath += CharacterDead;
                subscribedToDeathEvent = true;
            }
            if (Strength < DormantThreshold)
            {
                DeactivateHusk();
                State = InfectionState.Dormant;
            }
            else if (Strength < ActiveThreshold)
            {
                DeactivateHusk();
                character.SpeechImpediment = 100;
                State = InfectionState.Transition;
            }
            else if (Strength < Prefab.MaxStrength)
            {
                if (State != InfectionState.Active)
                {
                    character.SetStun(Rand.Range(2, 4, Rand.RandSync.Server));
                }
                State = InfectionState.Active;
                ActivateHusk();
            }
            else
            {
                State = InfectionState.Final;
                ActivateHusk();
                ApplyDamage(deltaTime, applyForce: true);
                character.SetStun(1);
            }
        }

        partial void UpdateMessages();

        private void ApplyDamage(float deltaTime, bool applyForce)
        {
            foreach (Limb limb in character.AnimController.Limbs)
            {
                float random = Rand.Value(Rand.RandSync.Server);
                huskInfection.Clear();
                huskInfection.Add(AfflictionPrefab.InternalDamage.Instantiate(random * 10 * deltaTime / character.AnimController.Limbs.Length));
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
            character.NeedsAir = false;
            character.SpeechImpediment = 100;
        }

        private void DeactivateHusk()
        {
            character.NeedsAir = character.Params.MainElement.GetAttributeBool("needsair", false);
            if (huskAppendage != null)
            {
                huskAppendage.ForEach(l => character.AnimController.RemoveLimb(l));
                huskAppendage = null;
            }
        }

        public void Remove()
        {
            if (character == null) { return; }
            DeactivateHusk();
            character.OnDeath -= CharacterDead;
            subscribedToDeathEvent = false;
        }

        private void CharacterDead(Character character, CauseOfDeath causeOfDeath)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (Strength < ActiveThreshold || character.Removed) { return; }

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

        private IEnumerable<object> CreateAIHusk()
        {
            character.Enabled = false;
            Entity.Spawner.AddToRemoveQueue(character);

            string huskedSpeciesName = GetHuskedSpeciesName(character.SpeciesName, Prefab as AfflictionPrefabHusk);
            CharacterPrefab prefab = CharacterPrefab.FindBySpeciesName(huskedSpeciesName);

            if (prefab == null)
            {
                DebugConsole.ThrowError("Failed to turn character \"" + character.Name + "\" into a husk - husk config file not found.");
                yield return CoroutineStatus.Success;
            }
            var husk = Character.Create(huskedSpeciesName, character.WorldPosition, ToolBox.RandomSeed(8), character.Info, isRemotePlayer: false, hasAi: true);

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

            if (character.Inventory.Items.Length != husk.Inventory.Items.Length)
            {
                string errorMsg = "Failed to move items from the source character's inventory into a husk's inventory (inventory sizes don't match)";
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("AfflictionHusk.CreateAIHusk:InventoryMismatch", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                yield return CoroutineStatus.Success;
            }

            for (int i = 0; i < character.Inventory.Items.Length && i < husk.Inventory.Items.Length; i++)
            {
                if (character.Inventory.Items[i] == null) continue;
                husk.Inventory.TryPutItem(character.Inventory.Items[i], i, true, false, null);
            }

            husk.SetStun(5);
            yield return new WaitForSeconds(5, false);
#if CLIENT
            husk.PlaySound(CharacterSound.SoundType.Idle);
#endif
            yield return CoroutineStatus.Success;
        }

        public static List<Limb> AttachHuskAppendage(Character character, string afflictionIdentifier, XElement appendageDefinition = null, Ragdoll ragdoll = null)
        {
            var appendage = new List<Limb>();
            if (!(AfflictionPrefab.List.FirstOrDefault(ap => ap.Identifier == afflictionIdentifier) is AfflictionPrefabHusk matchingAffliction))
            {
                DebugConsole.ThrowError($"Could not find an affliction of type 'huskinfection' that matches the affliction '{afflictionIdentifier}'!");
                return appendage;
            }
            string nonhuskedSpeciesName = GetNonHuskedSpeciesName(character.SpeciesName, matchingAffliction);
            string huskedSpeciesName = GetHuskedSpeciesName(nonhuskedSpeciesName, matchingAffliction);
            CharacterPrefab huskPrefab = CharacterPrefab.FindBySpeciesName(huskedSpeciesName);
            if (huskPrefab?.XDocument == null)
            {
                DebugConsole.ThrowError($"Failed to find the config file for the husk infected species with the species name '{huskedSpeciesName}'!");
                return appendage;
            }
            var mainElement = huskPrefab.XDocument.Root.IsOverride() ? huskPrefab.XDocument.Root.FirstElement() : huskPrefab.XDocument.Root;
            var element = appendageDefinition;
            if (element == null)
            {
                element = mainElement.GetChildElements("huskappendage").FirstOrDefault(e => e.GetAttributeString("affliction", string.Empty).Equals(afflictionIdentifier));
            }
            if (element == null)
            {
                DebugConsole.ThrowError($"Error in '{huskPrefab.FilePath}': Failed to find a huskappendage that matches the affliction with an identifier '{afflictionIdentifier}'!");
                return appendage;
            }
            string pathToAppendage = element.GetAttributeString("path", string.Empty);
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
            var limbElements = doc.Root.Elements("limb").ToDictionary(e => e.GetAttributeString("id", null), e => e);
            foreach (var jointElement in doc.Root.Elements("joint"))
            {
                if (limbElements.TryGetValue(jointElement.GetAttributeString("limb2", null), out XElement limbElement))
                {
                    var jointParams = new RagdollParams.JointParams(jointElement, ragdoll.RagdollParams);
                    Limb attachLimb = null;
                    if (matchingAffliction.AttachLimbId > -1)
                    {
                        attachLimb = ragdoll.Limbs.FirstOrDefault(l => l.Params.ID == matchingAffliction.AttachLimbId);
                    }
                    else if (matchingAffliction.AttachLimbName != null)
                    {
                        attachLimb = ragdoll.Limbs.FirstOrDefault(l => l.Name == matchingAffliction.AttachLimbName);
                    }
                    else if (matchingAffliction.AttachLimbType != LimbType.None)
                    {
                        attachLimb = ragdoll.Limbs.FirstOrDefault(l => l.type == matchingAffliction.AttachLimbType);
                    }
                    if (attachLimb == null)
                    {
                        DebugConsole.Log("Attachment limb not defined in the affliction prefab or no matching limb could be found. Using the appendage definition as it is.");
                        attachLimb = ragdoll.Limbs.FirstOrDefault(l => l.Params.ID == jointParams.Limb1);
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
                    else
                    {
                        DebugConsole.ThrowError("Attachment limb not found!");
                    }
                }
            }
            return appendage;
        }

        public static string GetHuskedSpeciesName(string speciesName, AfflictionPrefabHusk prefab)
        {
            return prefab.HuskedSpeciesName.Replace(AfflictionPrefabHusk.Tag, speciesName);
        }

        public static string GetNonHuskedSpeciesName(string huskedSpeciesName, AfflictionPrefabHusk prefab)
        {
            string nonTag = prefab.HuskedSpeciesName.Remove(AfflictionPrefabHusk.Tag);
            return huskedSpeciesName.ToLowerInvariant().Remove(nonTag);
        }
    }
}
