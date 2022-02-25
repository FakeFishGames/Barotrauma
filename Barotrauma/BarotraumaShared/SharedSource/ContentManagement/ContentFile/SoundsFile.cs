using System;
using System.Collections.Immutable;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage]
#if CLIENT
    sealed class SoundsFile : GenericPrefabFile<SoundPrefab>
    {
        public SoundsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected override PrefabCollection<SoundPrefab> prefabs => SoundPrefab.Prefabs;

        protected override SoundPrefab CreatePrefab(ContentXElement element)
        {
            var elemName = element.NameAsIdentifier();
            if (SoundPrefab.TagToDerivedPrefab.ContainsKey(elemName))
            {
                return Activator.CreateInstance(SoundPrefab.TagToDerivedPrefab[elemName], new object[] { element, this }) as SoundPrefab;
            }
            return new SoundPrefab(element, this);
        }

        protected override bool MatchesPlural(Identifier identifier) => identifier == "sounds";

        protected override bool MatchesSingular(Identifier identifier) => !MatchesPlural(identifier);

        public override Md5Hash CalculateHash() => Md5Hash.Blank;
    }
#else
    sealed class SoundsFile : OtherFile
    {
        public SoundsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }
    }
#endif
}