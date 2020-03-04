using Barotrauma.Sounds;
using Barotrauma.Particles;
using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Attack
    {
        [Serialize("StructureBlunt", true), Editable()]
        public string StructureSoundType { get; private set; }

        private RoundSound sound;

        private ParticleEmitter particleEmitter;

        partial void InitProjSpecific(XElement element)
        {
            if (element.Attribute("sound") != null)
            {
                DebugConsole.ThrowError("Error in attack ("+element+") - sounds should be defined as child elements, not as attributes.");
                return;
            }

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "particleemitter":
                        particleEmitter = new ParticleEmitter(subElement);
                        break;
                    case "sound":
                        sound = Submarine.LoadRoundSound(subElement);
                        break;
                }

            }
        }

        partial void DamageParticles(float deltaTime, Vector2 worldPosition)
        {
            if (particleEmitter != null)
            {
                particleEmitter.Emit(deltaTime, worldPosition);
            }

            if (sound != null)
            {
                SoundPlayer.PlaySound(sound.Sound, worldPosition, sound.Volume, sound.Range);
            }
        }
    }
}
