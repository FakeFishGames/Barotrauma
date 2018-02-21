#if CLIENT
using Microsoft.Xna.Framework;
#endif
using System.Collections.Generic;
using System.IO;
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
        public InfectionState State
        {
            get { return state; }
        }

        public bool CanSpeak
        {
            get { return Strength < Prefab.MaxStrength * 0.5f; }
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

            UpdateMessages(prevStrength, characterHealth.Character);
            if (Strength < Prefab.MaxStrength * 0.5f)
            {
                UpdateDormantState(deltaTime, characterHealth.Character);
            }
            else if (Strength < Prefab.MaxStrength)
            {
                UpdateTransitionState(deltaTime, characterHealth.Character);
            }
            else
            {
                UpdateActiveState(deltaTime, characterHealth.Character);
            }
        }

        private void UpdateMessages(float prevStrength, Character character)
        {
#if CLIENT
            if (Strength < Prefab.MaxStrength * 0.5f)
            {
                if (prevStrength % 10.0f > 0.05f && Strength % 10.0f < 0.05f)
                {
                    GUI.AddMessage(TextManager.Get("HuskDormant"), Color.Red, 4.0f);
                }
            }
            else if (Strength < Prefab.MaxStrength)
            {
                if (state == InfectionState.Dormant && Character.Controlled == character)
                {
                    new GUIMessageBox("", TextManager.Get("HuskCantSpeak"));
                }
            }
            else if (state != InfectionState.Active && Character.Controlled == character)
            {
                new GUIMessageBox("", TextManager.Get("HuskActivate"));
            }
#endif
        }

        private void UpdateDormantState(float deltaTime, Character character)
        {
            //TODO: remove husk appendage if reverting from active state
            state = InfectionState.Dormant;
        }

        private void UpdateTransitionState(float deltaTime, Character character)
        {
            //TODO: remove husk appendage if reverting from active state
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
                character.DamageLimb(
                    limb.WorldPosition, limb,
                    new List<Affliction>() { AfflictionPrefab.InternalDamage.Instantiate(0.5f * deltaTime / character.AnimController.Limbs.Length) },
                    0.0f, false, 0.0f);
            }
        }


        private void ActivateHusk(Character character)
        {
            character.NeedsAir = false;
            AttachHuskAppendage(character);
        }

        private void AttachHuskAppendage(Character character)
        {
            XDocument doc = XMLExtensions.TryLoadXml(Path.Combine("Content", "Characters", "Human", "huskappendage.xml"));
            if (doc == null || doc.Root == null) return;

            var limbElement = doc.Root.Element("limb");
            if (limbElement == null)
            {
                DebugConsole.ThrowError("Error in huskappendage.xml - limb element not found");
                return;
            }

            var jointElement = doc.Root.Element("joint");
            if (jointElement == null)
            {
                DebugConsole.ThrowError("Error in huskappendage.xml - joint element not found");
                return;
            }

            character.SetStun(0.5f);
            if (character.AnimController.Dir < 1.0f)
            {
                character.AnimController.Flip();
            }

            var torso = character.AnimController.GetLimb(LimbType.Torso);

            var newLimb = new Limb(character, limbElement);
            newLimb.body.Submarine = character.Submarine;
            newLimb.body.SetTransform(torso.SimPosition, torso.Rotation);

            character.AnimController.AddLimb(newLimb);
            character.AnimController.AddJoint(jointElement);
        }

        public void Remove(Character character)
        {
            if (character != null) character.OnDeath -= CharacterDead;
            subscribedToDeathEvent = false;
        }

        private void CharacterDead(Character character, CauseOfDeathType causeOfDeath)
        {
            if (GameMain.Client != null) return;

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

            var characterFiles = GameMain.SelectedPackage.GetFilesOfType(ContentType.Character);
            var configFile = characterFiles.Find(f => Path.GetFileNameWithoutExtension(f) == "humanhusk");

            if (string.IsNullOrEmpty(configFile))
            {
                DebugConsole.ThrowError("Failed to turn character \"" + character.Name + "\" into a husk - humanhusk config file not found.");
                yield return CoroutineStatus.Success;
            }

            var husk = Character.Create(configFile, character.WorldPosition, character.Info, false, true);

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
                husk.Inventory.TryPutItem(character.Inventory.Items[i], i, true, null);
            }

            yield return CoroutineStatus.Success;
        }
    }
}
