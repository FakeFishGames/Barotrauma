using Barotrauma.Particles;
using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Attack
    {
        [Serialize("StructureBlunt", IsPropertySaveable.Yes), Editable()]
        public string StructureSoundType { get; private set; }

        private RoundSound sound;

        private ParticleEmitter particleEmitter;

        partial void InitProjSpecific(ContentXElement element)
        {
            if (element.Attribute("sound") != null)
            {
                DebugConsole.ThrowError("Error in attack ("+element+") - sounds should be defined as child elements, not as attributes.");
                return;
            }

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "particleemitter":
                        particleEmitter = new ParticleEmitter(subElement);
                        break;
                    case "sound":
                        sound = RoundSound.Load(subElement);
                        break;
                }

            }
        }

        partial void DamageParticles(float deltaTime, Vector2 worldPosition)
        {
            particleEmitter?.Emit(deltaTime, worldPosition);

            if (sound != null)
            {
                SoundPlayer.PlaySound(sound.Sound, worldPosition, sound.Volume, sound.Range, ignoreMuffling: sound.IgnoreMuffling);
            }
        }
    }
}
