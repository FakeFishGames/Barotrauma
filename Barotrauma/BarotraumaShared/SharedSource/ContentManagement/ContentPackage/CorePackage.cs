using Barotrauma.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma
{
    [AttributeUsage(AttributeTargets.Class)]
    public class RequiredByCorePackage : Attribute
    {
        public readonly ImmutableHashSet<Type> AlternativeTypes;
        public RequiredByCorePackage(params Type[] alternativeTypes)
        {
            AlternativeTypes = alternativeTypes.ToImmutableHashSet();
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class AlternativeContentTypeNames : Attribute
    {
        public readonly ImmutableHashSet<Identifier> Names;
        public AlternativeContentTypeNames(params string[] names)
        {
            Names = names.ToIdentifiers().ToImmutableHashSet();
        }
    }

    public class CorePackage : ContentPackage
    {
        public CorePackage(XDocument doc, string path) : base(doc, path)
        {
            AssertCondition(doc.Root.GetAttributeBool("corepackage", false), 
                "Expected a core package, got a regular package");

            var missingFileTypes = ContentFile.Types.Where(
                t => t.RequiredByCorePackage
                     && !Files.Any(f => t.Type == f.GetType()
                                       || t.AlternativeTypes.Contains(f.GetType())));
            AssertCondition(!missingFileTypes.Any(),
                    "Core package requires at least one of the following content types: " +
                            string.Join(", ", missingFileTypes.Select(t => t.Type.Name)));
        }

        protected override void HandleLoadException(Exception e)
        {
            throw new Exception($"An exception was thrown while loading \"{Name}\"", e);
        }
    }
}