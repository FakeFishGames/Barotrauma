using System.Xml.Linq;
#if CLIENT
using Barotrauma.Particles;
#endif

namespace Barotrauma
{
    [RequiredByCorePackage]
    [NotSyncedInMultiplayer]
#if CLIENT
    sealed class ParticlesFile : GenericPrefabFile<ParticlePrefab>
    {
        public ParticlesFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override bool MatchesSingular(Identifier identifier) => !MatchesPlural(identifier);
        protected override bool MatchesPlural(Identifier identifier) => identifier == "prefabs" || identifier == "particles";
        protected override PrefabCollection<ParticlePrefab> prefabs => ParticlePrefab.Prefabs;
        protected override ParticlePrefab CreatePrefab(ContentXElement element)
        {
            return new ParticlePrefab(element, this);
        }

        public override Md5Hash CalculateHash() => Md5Hash.Blank;
    }
#else
    sealed class ParticlesFile : OtherFile
    {
        public ParticlesFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { } //this content type doesn't do anything on a server
    }
#endif
}
