using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class HuskInfection
    {
        public enum InfectionState
        {
            Dormant, Transition, Active
        }

        const float IncubationDuration = 300.0f;

        private InfectionState state;

        private Limb huskAppendage;

        private float incubationTimer;
        public float IncubationTimer
        {
            get { return incubationTimer; }
            set
            {
                incubationTimer = MathHelper.Clamp(value, 0.0f, 1.0f);
            }
        }

        public InfectionState State
        {
            get { return state; }
        }
        
        public bool CanSpeak
        {
            get { return IncubationTimer < 0.5f; }
        }

        public HuskInfection(Character character)
        {
            character.OnDeath += CharacterDead;
        }

        public void Update(float deltaTime, Character character)
        {
            float prevTimer = IncubationTimer;

            UpdateProjSpecific(prevTimer,character);
            if (IncubationTimer < 0.5f)
            {
                UpdateDormantState(deltaTime, character);
            }
            else if (IncubationTimer < 1.0f)
            {
                UpdateTransitionState(deltaTime, character);
            }
            else
            {
                UpdateActiveState(deltaTime, character);
            }
        }
        partial void UpdateProjSpecific(float prevTimer, Character character);

        private void UpdateDormantState(float deltaTime, Character character)
        {
            if (state != InfectionState.Dormant)
            {
                DeactivateHusk(character);
            }

            float prevTimer = IncubationTimer;
            state = InfectionState.Dormant;

            IncubationTimer += deltaTime / IncubationDuration;

            character.AddDamage(CauseOfDeath.Husk, GameMain.NilMod.PlayerHuskInfectedDrain * deltaTime, null);
        }

        private void UpdateTransitionState(float deltaTime, Character character)
        {
            if (state != InfectionState.Transition)
            {
                DeactivateHusk(character);
            }

            IncubationTimer += deltaTime / IncubationDuration;

            character.AddDamage(CauseOfDeath.Husk, GameMain.NilMod.PlayerHuskInfectedDrain * deltaTime, null);

            state = InfectionState.Transition;
        }

        private void UpdateActiveState(float deltaTime, Character character)
        {
            if (state != InfectionState.Active)
            {
                ActivateHusk(character);
                state = InfectionState.Active;
            }

            character.AddDamage(CauseOfDeath.Husk, GameMain.NilMod.PlayerHuskIncurableDrain * deltaTime, null);
            //character.AddDamage(CauseOfDeath.Husk, 0.5f * deltaTime, null);
        }


        private void ActivateHusk(Character character)
        {
            if (GameMain.Server != null) GameMain.Server.ServerLog.WriteLine(character.Name + " Maxed Husk infection!", Networking.ServerLog.MessageType.Husk);
            character.NeedsAir = false;
            AttachHuskAppendage(character);
        }

        private void AttachHuskAppendage(Character character)
        {
            //husk appendage already created, don't do anything 
            if (huskAppendage != null) return;

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

            huskAppendage = new Limb(character, limbElement);
            huskAppendage.body.Submarine = character.Submarine;
            huskAppendage.body.SetTransform(torso.SimPosition, torso.Rotation);

            character.AnimController.AddLimb(huskAppendage);
            character.AnimController.AddJoint(jointElement);
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
        }

        private void CharacterDead(Character character, CauseOfDeath causeOfDeath)
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

            //Nilmod Deactivate players turning into husks on death optionally
            if (!GameMain.NilMod.PlayerHuskAiOnDeath)
            {
                if (GameMain.Server != null) GameMain.Server.ServerLog.WriteLine(character.Name + " Died husk infected but did not convert.", Networking.ServerLog.MessageType.Husk);
                return;
            }

            //create the AI husk in a coroutine to ensure that we don't modify the character list while enumerating it 
            //CoroutineManager.StartCoroutine(CreateAIHusk(character));
            CoroutineManager.StartCoroutine(CreateAIHuskDelayed(character));
        }

        private IEnumerable<object> CreateAIHusk(Character character)
        {
            character.Enabled = false;
            Entity.Spawner.AddToRemoveQueue(character);

            if (GameMain.Server != null) GameMain.Server.ServerLog.WriteLine(character.Name + " Converted into an AI Husk!", Networking.ServerLog.MessageType.Husk);
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

        private IEnumerable<object> CreateAIHuskDelayed(Character character)
        {
            //character.Enabled = false;
            //Entity.Spawner.AddToRemoveQueue(character);

            if (GameMain.Server != null) GameMain.Server.ServerLog.WriteLine(character.Name + " Converted into an AI Husk!", Networking.ServerLog.MessageType.Husk);
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

#if CLIENT
            if(GameMain.Server != null) GameSession.inGameInfo.AddNoneClientCharacter(husk);
#endif

            GameMain.NilMod.HideCharacter(character);

            yield return CoroutineStatus.Success;
        }
    }
}
