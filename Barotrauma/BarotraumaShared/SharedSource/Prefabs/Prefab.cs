#nullable enable

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public abstract class Prefab
    {
        public readonly static ImmutableHashSet<Type> Types;
        static Prefab()
        {
            Types = ReflectionUtils.GetDerivedNonAbstract<Prefab>().ToImmutableHashSet();
        }

        protected static bool potentialCallFromConstructor = false;

        public static bool IsActivator<T>()
        {
            return typeof(T).GetInterfaces().Any(i => i.Name.Contains(nameof(IImplementsActivator)));
        }

        public static void DisallowCallFromConstructor<T>()
        {
            if (IsActivator<T>()) { return; }
            if (!potentialCallFromConstructor) { return; }
            StackTrace st = new StackTrace(skipFrames: 2, fNeedFileInfo: false);
            for (int i = st.FrameCount - 1; i >= 0; i--)
            {
                if (st.GetFrame(i)?.GetMethod() is { IsConstructor: true, DeclaringType: { } declaringType }
                    && Types.Contains(declaringType))
                {
                    throw new Exception("Called disallowed method from within a prefab's constructor!");
                }
            }
            potentialCallFromConstructor = false;
        }
        
        public readonly Identifier Identifier;
        public readonly ContentFile ContentFile;

        public ContentPackage? ContentPackage => ContentFile?.ContentPackage;
        public ContentPath FilePath => ContentFile.Path;

        public Prefab(ContentFile file, Identifier identifier)
        {
            potentialCallFromConstructor = true;
            ContentFile = file;
            Identifier = identifier;
            if (Identifier.IsEmpty) { throw new ArgumentException($"Error creating {GetType().Name}: Identifier cannot be empty"); }
        }

        public Prefab(ContentFile file, ContentXElement element)
        {
            potentialCallFromConstructor = true;
            ContentFile = file;
            Identifier = DetermineIdentifier(element!);
            if (Identifier.IsEmpty) { throw new ArgumentException($"Error creating {GetType().Name}: Identifier cannot be empty"); }
        }

        protected virtual Identifier DetermineIdentifier(XElement element)
        {
            return element.GetAttributeIdentifier("identifier", Identifier.Empty);
        }

        public abstract void Dispose();
    }
}
