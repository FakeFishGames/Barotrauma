using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Barotrauma
{
    class CoreEntityPrefab : MapEntityPrefab
    {
        public static readonly PrefabCollection<CoreEntityPrefab> Prefabs = new PrefabCollection<CoreEntityPrefab>();

        private readonly ConstructorInfo constructor;

        private CoreEntityPrefab(
            Identifier identifier,
            ConstructorInfo constructor,
            bool resizeHorizontal = false,
            bool resizeVertical = false,
            bool linkable = false,
            IEnumerable<Identifier> allowedLinks = null,
            IEnumerable<string> aliases = null)
            : base(identifier)
        {
            System.Diagnostics.Debug.Assert(constructor != null);
            this.constructor = constructor;
            this.Name = TextManager.Get($"EntityName.{identifier}");
            this.Description = TextManager.Get($"EntityDescription.{identifier}");
            this.ResizeHorizontal = resizeHorizontal;
            this.ResizeVertical = resizeVertical;
            this.Linkable = linkable;
            this.AllowedLinks = (allowedLinks ?? Enumerable.Empty<Identifier>()).ToImmutableHashSet();
            this.Aliases = (aliases ?? Enumerable.Empty<string>()).Concat(identifier.Value.ToEnumerable()).ToImmutableHashSet();
        }

        public static CoreEntityPrefab HullPrefab { get; private set; }
        public static CoreEntityPrefab GapPrefab { get; private set; }
        public static CoreEntityPrefab WayPointPrefab { get; private set; }
        public static CoreEntityPrefab SpawnPointPrefab { get; private set; }
        
        public static void InitCorePrefabs()
        {
            HullPrefab = new CoreEntityPrefab(
                "hull".ToIdentifier(),
                typeof(Hull).GetConstructor(new Type[] { typeof(Rectangle) }),
                resizeHorizontal: true,
                resizeVertical: true,
                linkable: true,
                allowedLinks: new Identifier[] { "hull".ToIdentifier() });
            Prefabs.Add(HullPrefab, false);

            GapPrefab = new CoreEntityPrefab(
                "gap".ToIdentifier(),
                typeof(Gap).GetConstructor(new Type[] { typeof(Rectangle) }),
                resizeHorizontal: true,
                resizeVertical: true);
            Prefabs.Add(GapPrefab, false);

            WayPointPrefab = new CoreEntityPrefab(
                "waypoint".ToIdentifier(),
                typeof(WayPoint).GetConstructor(new Type[] { typeof(MapEntityPrefab), typeof(Rectangle) }));
            Prefabs.Add(WayPointPrefab, false);

            SpawnPointPrefab = new CoreEntityPrefab(
                "spawnpoint".ToIdentifier(),
                typeof(WayPoint).GetConstructor(new Type[] { typeof(MapEntityPrefab), typeof(Rectangle) }));
            Prefabs.Add(SpawnPointPrefab, false);
        }

        protected override void CreateInstance(Rectangle rect)
        {
            if (this == WayPointPrefab || this == SpawnPointPrefab)
            {
                object[] lobject = new object[] { this, rect };
                constructor.Invoke(lobject);
            }
            else
            {
                object[] lobject = new object[] { rect };
                constructor.Invoke(lobject);
            }
        }


        public override Sprite Sprite => null;

        public override string OriginalName => Name.Value;

        public override LocalizedString Name { get; }

        public override ImmutableHashSet<Identifier> Tags { get; } = Enumerable.Empty<Identifier>().ToImmutableHashSet();

        public override ImmutableHashSet<Identifier> AllowedLinks { get; }

        public override MapEntityCategory Category => MapEntityCategory.Structure;

        public override ImmutableHashSet<string> Aliases { get; }

        public override void Dispose()
        {
            throw new InvalidOperationException(
                $"{nameof(CoreEntityPrefab)}.{nameof(Dispose)} should never be called");
        }
    }
}
