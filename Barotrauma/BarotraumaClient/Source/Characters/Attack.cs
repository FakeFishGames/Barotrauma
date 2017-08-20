using Barotrauma.Particles;
using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Attack
    {
        private Sound sound;

        private ParticleEmitter particleEmitterPrefab;

        partial void InitProjSpecific(XElement element)
        {
            string soundPath = ToolBox.GetAttributeString(element, "sound", "");
            if (!string.IsNullOrWhiteSpace(soundPath))
            {
                sound = Sound.Load(soundPath);
            }

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "particleemitter":
                        particleEmitterPrefab = new ParticleEmitter(subElement);
                        break;
                }

            }
        }

        partial void DamageParticles(float deltaTime, Vector2 worldPosition)
        {
            if (particleEmitterPrefab != null)
            {
                particleEmitterPrefab.Emit(deltaTime, worldPosition);
            }

            if (sound != null)
            {
                sound.Play(1.0f, 500.0f, worldPosition);
            }
        }
    }
}
