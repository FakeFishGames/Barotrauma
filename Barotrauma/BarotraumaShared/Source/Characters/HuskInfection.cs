using Microsoft.Xna.Framework;
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
            UpdateProjSpecific(prevTimer,character);
        }
        partial void UpdateProjSpecific(float prevTimer, Character character);

        private void UpdateDormantState(float deltaTime, Character character)
        {
            float prevTimer = IncubationTimer;

            state = InfectionState.Dormant;

            IncubationTimer += deltaTime / IncubationDuration;

            if (Character.Controlled != character) return;
        }

        private void UpdateTransitionState(float deltaTime, Character character)
        {
            IncubationTimer += deltaTime / IncubationDuration;
            
            state = InfectionState.Transition;
        }

        private void UpdateActiveState(float deltaTime, Character character)
        {
            if (state != InfectionState.Active)
            {
                ActivateHusk(character);
                state = InfectionState.Active;
            }

            character.AddDamage(CauseOfDeath.Husk, 0.5f*deltaTime, null);
        }


        private void ActivateHusk(Character character)
        {
            character.NeedsAir = false;
            AttachHuskAppendage(character);
        }

        private void AttachHuskAppendage(Character character)
        {
            XDocument doc = ToolBox.TryLoadXml(Path.Combine("Content", "Characters", "Human", "huskappendage.xml"));
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
            if (character != null)
                character.OnDeath -= CharacterDead;
        }

        private void CharacterDead(Character character, CauseOfDeath causeOfDeath)
        {
            if (GameMain.Client != null) return;

            var husk = Character.Create(
                Path.Combine("Content", "Characters", "Human", "humanhusk.xml"),
                character.WorldPosition,
                character.Info, 
                false, true);
            
            foreach (Limb limb in husk.AnimController.Limbs)
            {
                if (limb.type == LimbType.None)
                {
                    limb.body.SetTransform(character.SimPosition, 0.0f);
                    continue;
                }

                var matchingLimb = character.AnimController.GetLimb(limb.type);                
                limb.body.SetTransform(matchingLimb.SimPosition, matchingLimb.Rotation);
            }

            for (int i = 0; i < character.Inventory.Items.Length; i++)
            {
                if (character.Inventory.Items[i] == null) continue;
                husk.Inventory.TryPutItem(character.Inventory.Items[i], i, true);
            }
            
            character.Enabled = false;
            Entity.Spawner.AddToRemoveQueue(character);
        }
    }
}
