#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public class NotSyncedInMultiplayer : Attribute { }
    
    /// <summary>
    /// Base class for content file types, which are loaded
    /// from filelist.xml via reflection.
    /// PLEASE AVOID INHERITING FROM THIS CLASS DIRECTLY.
    /// Inheriting from GenericPrefabFile&lt;T&gt; is likely what
    /// you want.
    /// </summary>
    public abstract class ContentFile
    {
        public class TypeInfo
        {
            public readonly Type Type;
            public readonly bool RequiredByCorePackage;
            public readonly bool NotSyncedInMultiplayer;
            public readonly ImmutableHashSet<Type>? AlternativeTypes;
            public readonly ImmutableHashSet<Identifier> Names;

            public TypeInfo(Type type)
            {
                Type = type;
                
                var reqByCoreAttribute = type.GetCustomAttribute<RequiredByCorePackage>();
                RequiredByCorePackage = reqByCoreAttribute != null;
                var notSyncedInMultiplayerAttribute = type.GetCustomAttribute<NotSyncedInMultiplayer>();
                NotSyncedInMultiplayer = notSyncedInMultiplayerAttribute != null;
                AlternativeTypes = reqByCoreAttribute?.AlternativeTypes;

                HashSet<Identifier> names = new HashSet<Identifier> { type.Name.RemoveFromEnd("File").ToIdentifier() };
                if (type.GetCustomAttribute<AlternativeContentTypeNames>()?.Names is { } altNames)
                {
                    names.UnionWith(altNames);
                }

                Names = names.ToImmutableHashSet();
            }

            public ContentFile? CreateInstance(ContentPackage contentPackage, ContentPath path) =>
                (ContentFile?)Activator.CreateInstance(Type, contentPackage, path);
        }
        
        public readonly static ImmutableHashSet<TypeInfo> Types;
        static ContentFile()
        {
            Types = ReflectionUtils.GetDerivedNonAbstract<ContentFile>()
                .Select(t => new TypeInfo(t))
                .ToImmutableHashSet();
        }

        public static Result<ContentFile, string> CreateFromXElement(ContentPackage contentPackage, XElement element)
        {
            static Result<ContentFile, string> fail(string error)
                => Result<ContentFile, string>.Failure(error);
            
            Identifier elemName = element.NameAsIdentifier();
            var type = Types.FirstOrDefault(t => t.Names.Contains(elemName));
            var filePath = element.GetAttributeContentPath("file", contentPackage);
            if (type is null)
            {
                return fail($"Invalid content type \"{elemName}\"");
            }
            if (filePath is null)
            {
                return fail($"No content path defined for file of type \"{elemName}\"");
            }
            try
            {
                if (!File.Exists(filePath.FullPath))
                {
                    return fail($"Failed to load file \"{filePath}\" of type \"{elemName}\": file not found.");
                }
                var file = type.CreateInstance(contentPackage, filePath);
                return file is null
                    ? throw new Exception($"Content type is not implemented correctly")
                    : Result<ContentFile, string>.Success(file);
            }
            catch (Exception e)
            {
                return fail($"Failed to load file \"{filePath}\" of type \"{elemName}\": {e.Message}\n{e.StackTrace.CleanupStackTrace()}");
            }
        }
        
        protected ContentFile(ContentPackage contentPackage, ContentPath path)
        {
            ContentPackage = contentPackage;
            Path = path;
            Hash = CalculateHash();
        }

        public readonly ContentPackage ContentPackage;
        public readonly ContentPath Path;
        public readonly Md5Hash Hash;
        public abstract void LoadFile();
        public abstract void UnloadFile();
        public abstract void Sort();

        public virtual void Preload(Action<Sprite> addPreloadedSprite) { }

        public virtual Md5Hash CalculateHash()
        {
            return Md5Hash.CalculateForFile(Path.Value, Md5Hash.StringHashOptions.IgnoreWhitespace);
        }

        public bool NotSyncedInMultiplayer => Types.Any(t => t.Type == GetType() && t.NotSyncedInMultiplayer);
    }
}