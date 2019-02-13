using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class AfflictionHusk : Affliction
    {
        public enum InfectionState
        {
            Dormant, Transition, Active
        }

        private bool subscribedToDeathEvent;

        private InfectionState state;

        private Limb huskAppendage;
        
        public InfectionState State
        {
            get { return state; }
        }
        
        public AfflictionHusk(AfflictionPrefab prefab, float strength) : 
            base(prefab, strength)
        {
        }

        public override void Update(CharacterHealth characterHealth, Limb targetLimb, float deltaTime)
        {
            float prevStrength = Strength;
            base.Update(characterHealth, targetLimb, deltaTime);

            if (!subscribedToDeathEvent)
            {
                characterHealth.Character.OnDeath += CharacterDead;
                subscribedToDeathEvent = true;
            }

            if (characterHealth.Character == Character.Controlled) UpdateMessages(prevStrength, characterHealth.Character);
            if (Strength < Prefab.MaxStrength * 0.5f)
            {
                UpdateDormantState(deltaTime, characterHealth.Character);
            }
            else if (Strength < Prefab.MaxStrength)
            {
                characterHealth.Character.SpeechImpediment = 100.0f;
                UpdateTransitionState(deltaTime, characterHealth.Character);
            }
            else
            {
                characterHealth.Character.SpeechImpediment = 100.0f;
                UpdateActiveState(deltaTime, characterHealth.Character);
            }
        }

        partial void UpdateMessages(float prevStrength, Character character);

        private void UpdateDormantState(float deltaTime, Character character)
        {
            if (state != InfectionState.Dormant)
            {
                DeactivateHusk(character);
            }
            
            state = InfectionState.Dormant;
        }

        private void UpdateTransitionState(float deltaTime, Character character)
        {
            if (state != InfectionState.Transition)
            {
                DeactivateHusk(character);                
            }

            state = InfectionState.Transition;
        }

        private void UpdateActiveState(float deltaTime, Character character)
        {
            if (state != InfectionState.Active)
            {
                ActivateHusk(character);
                state = InfectionState.Active;
            }

            foreach (Limb limb in character.AnimController.Limbs)
            {
                character.LastDamageSource = null;
                character.DamageLimb(
                    limb.WorldPosition, limb,
                    new List<Affliction>() { AfflictionPrefab.InternalDamage.Instantiate(0.5f * deltaTime / character.AnimController.Limbs.Length) },
                    0.0f, false, 0.0f);
            }
        }

        private void ActivateHusk(Character character)
        {
            character.NeedsAir = false;
            if (huskAppendage == null)
            {
                huskAppendage = AttachHuskAppendage(character);
                character.SetStun(0.5f);
            }
        }

        public static Limb AttachHuskAppendage(Character character, Ragdoll ragdoll = null)
        {
            var huskDoc = XMLExtensions.TryLoadXml(Character.GetConfigFile("humanhusk"));
            string pathToAppendage = huskDoc.Root.Element("huskappendage").GetAttributeString("path", string.Empty);
            XDocument doc = XMLExtensions.TryLoadXml(pathToAppendage);
            if (doc == null || doc.Root == null) { return null; }

            var limbElement = doc.Root.Element("limb");
            if (limbElement == null)
            {
                DebugConsole.ThrowError("Error in Huskappendage.xml - limb element not found");
                return null;
            }

            var jointElement = doc.Root.Element("joint");
            if (jointElement == null)
            {
                DebugConsole.ThrowError("Error in Huskappendage.xml - joint element not found");
                return null;
            }

            if (ragdoll == null)
            {
                ragdoll = character.AnimController;
            }

            if (ragdoll.Dir < 1.0f)
            {
                ragdoll.Flip();
            }

            var torso = ragdoll.GetLimb(LimbType.Torso);
            
            var huskAppendage = new Limb(ragdoll, character, new LimbParams(limbElement, ragdoll.RagdollParams));
            huskAppendage.body.Submarine = character.Submarine;
            huskAppendage.body.SetTransform(torso.SimPosition, torso.Rotation);

            ragdoll.AddLimb(huskAppendage);
            ragdoll.AddJoint(jointElement);
            return huskAppendage;
        }

        private void DeactivateHusk(Character character)
        {
            character.NeedsAir = true;
            RemoveHuskAppendage(character);
        }

        private void RemoveHuskAppendage(Character character)
        {
            if (huskAppendage == null) return;

            character.AnimController.RemoveLimb(huskAppendage);
            huskAppendage = null;
        }

        public void Remove(Character character)
        {
            DeactivateHusk(character);
            if (character != null) character.OnDeath -= CharacterDead;
            subscribedToDeathEvent = false;
        }

        private void CharacterDead(Character character, CauseOfDeath causeOfDeath)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (Strength < Prefab.MaxStrength * 0.5f || character.Removed) { return; }

            //don't turn the character into a husk if any of its limbs are severed
            if (character.AnimController?.LimbJoints != null)
            {
                foreach (var limbJoint in character.AnimController.LimbJoints)
                {
                    if (limbJoint.IsSevered) return;
                }
            }

            //create the AI husk in a coroutine to ensure that we don't modify the character list while enumerating it
            CoroutineManager.StartCoroutine(CreateAIHusk(character));
        }

        private IEnumerable<object> CreateAIHusk(Character character)
        {
            character.Enabled = false;
            Entity.Spawner.AddToRemoveQueue(character);

            var configFile = Character.GetConfigFile("husk");

            if (string.IsNullOrEmpty(configFile))
            {
                DebugConsole.ThrowError("Failed to turn character \"" + character.Name + "\" into a husk - husk config file not found.");
                yield return CoroutineStatus.Success;
            }

            //XDocument doc = XMLExtensions.TryLoadXml(configFile);
            //if (doc?.Root == null)
            //{
            //    DebugConsole.ThrowError("Failed to turn character \"" + character.Name + "\" into a husk - husk config file ("+configFile+") could not be read.");
            //    yield return CoroutineStatus.Success;
            //}
            
            //character.Info.Ragdoll = null;
            //character.Info.SourceElement = doc.Root;
            var husk = Character.Create(configFile, character.WorldPosition, character.Info.Name, character.Info, isRemotePlayer: false, hasAi: true);

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
            for (int i = 0; i < character.Inventory.Items.Length; i++)
            {
                if (character.Inventory.Items[i] == null) continue;
                husk.Inventory.TryPutItem(character.Inventory.Items[i], i, true, false, null);
            }

            yield return CoroutineStatus.Success;
        }
    }
}
