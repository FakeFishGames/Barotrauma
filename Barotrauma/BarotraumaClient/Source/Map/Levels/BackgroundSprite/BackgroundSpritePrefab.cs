using Barotrauma.Particles;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class BackgroundSpritePrefab
    {        
        public readonly List<ParticleEmitterPrefab> ParticleEmitterPrefabs;
        public readonly List<Vector2> EmitterPositions;

        public readonly XElement SoundElement;
        public readonly Vector2 SoundPosition;
    }
}
