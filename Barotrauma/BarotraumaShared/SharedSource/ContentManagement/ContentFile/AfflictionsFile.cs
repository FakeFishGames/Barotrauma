using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage]
    sealed class AfflictionsFile : ContentFile
    {
        private readonly static ImmutableHashSet<Type> afflictionTypes;
        static AfflictionsFile()
        {
            afflictionTypes = ReflectionUtils.GetDerivedNonAbstract<Affliction>()
                .ToImmutableHashSet();
        }

        public AfflictionsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        private void ParseElement(ContentXElement element, bool overriding)
        {
            Identifier elementName = element.NameAsIdentifier();
            if (element.IsOverride())
            {
                element.Elements().ForEach(s => ParseElement(s, overriding: true));
            }
            else if (elementName == "Afflictions")
            {
                element.Elements().ForEach(s => ParseElement(s, overriding: overriding));
            }
            else if (elementName == "cprsettings")
            {
                var cprSettings = new CPRSettings(element, this);
                CPRSettings.Prefabs.Add(cprSettings, overriding);
            }
            else if (elementName == "damageoverlay")
            {
#if CLIENT
                var damageOverlay = new CharacterHealth.DamageOverlayPrefab(element, this);
                CharacterHealth.DamageOverlayPrefab.Prefabs.Add(damageOverlay, overriding);
#endif
            }
            else
            {
                Identifier identifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);
                if (identifier.IsEmpty)
                {
                    DebugConsole.ThrowError(
                        $"No identifier defined for the affliction '{elementName}' in file '{Path}'",
                        contentPackage: element?.ContentPackage);
                    return;
                }

                if (AfflictionPrefab.Prefabs.TryGet(identifier, out var existingAffliction))
                {
                    if (overriding)
                    {
                        DebugConsole.NewMessage(
                            $"Overriding an affliction or a buff with the identifier '{identifier}' using the version in '{element.ContentPackage.Name}'",
                            Color.MediumPurple);
                    }
                    else
                    {
                        DebugConsole.ThrowError(
                            $"Duplicate affliction: '{identifier}' defined in {element.ContentPackage.Name} is already defined in the previously loaded content package {existingAffliction.ContentPackage.Name}."+
                            $" You may need to adjust the mod load order to make sure {element.ContentPackage.Name} is loaded first.", 
                            contentPackage: element?.ContentPackage);
                        return;
                    }
                }

                var type = afflictionTypes.FirstOrDefault(t =>
                               t.Name == elementName
                               || t.Name == $"Affliction{elementName}".ToIdentifier())
                           ?? typeof(Affliction);
                var prefab = CreatePrefab(element, type);
                AfflictionPrefab.Prefabs.Add(prefab, overriding);
            }
        }
        
        public override void LoadFile()
        {
            XDocument doc = XMLExtensions.TryLoadXml(Path);
            if (doc?.Root is null) { return; }
            ParseElement(doc.Root.FromPackage(ContentPackage), overriding: false);
        }

        private AfflictionPrefab CreatePrefab(ContentXElement element, Type type)
        {
            if (type == typeof(AfflictionHusk)) { return new AfflictionPrefabHusk(element, this, type); }
            return new AfflictionPrefab(element, this, type);
        }

        public override void UnloadFile()
        {
#if CLIENT
            CharacterHealth.DamageOverlayPrefab.Prefabs.RemoveByFile(this);
#endif
            CPRSettings.Prefabs.RemoveByFile(this);
            AfflictionPrefab.Prefabs.RemoveByFile(this);
        }

        public override void Sort()
        {
#if CLIENT
            CharacterHealth.DamageOverlayPrefab.Prefabs.Sort();
#endif
            CPRSettings.Prefabs.Sort();
            AfflictionPrefab.Prefabs.SortAll();
        }
    }
}