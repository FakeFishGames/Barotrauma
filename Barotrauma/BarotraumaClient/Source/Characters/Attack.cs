using Barotrauma.Particles;
using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Attack
    {
        private Sound sound;

        private ParticleEmitterPrefab particleEmitterPrefab;

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
                        particleEmitterPrefab = new ParticleEmitterPrefab(subElement);
                        break;
                }

            }
        }

        partial void DamageParticles(Vector2 worldPosition)
        {
            if (particleEmitterPrefab != null)
            {
                particleEmitterPrefab.Emit(worldPosition);
            }

            if (sound != null)
            {
                sound.Play(1.0f, 500.0f, worldPosition);
            }
        }
    }
}
