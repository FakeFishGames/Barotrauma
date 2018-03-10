using Barotrauma.Sounds;
using Barotrauma.Particles;
using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Attack
    {
        private Sound sound;

        private ParticleEmitter particleEmitter;

        partial void InitProjSpecific(XElement element)
        {
            string soundPath = element.GetAttributeString("sound", "");
            if (!string.IsNullOrWhiteSpace(soundPath))
            {
                sound = Submarine.LoadRoundSound(soundPath);
            }

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "particleemitter":
                        particleEmitter = new ParticleEmitter(subElement);
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
                sound.Play(1.0f, 500.0f, worldPosition);
            }
        }
    }
}
