using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class BackgroundSpritePrefab
    {        
        public readonly Particles.ParticleEmitterPrefab ParticleEmitterPrefab;
        public readonly Vector2 EmitterPosition;

        public readonly XElement SoundElement;
        public readonly Vector2 SoundPosition;
    }
}
