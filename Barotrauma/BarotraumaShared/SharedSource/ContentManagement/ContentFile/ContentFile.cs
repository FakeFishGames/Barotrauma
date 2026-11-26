#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Barotrauma.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

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
            private readonly MethodInfo? contentPathMutator;

            public TypeInfo(Type type)
            {
                Type = type;
                
                var reqByCoreAttribute = type.GetCustomAttribute<RequiredByCorePackage>();
                RequiredByCorePackage = reqByCoreAttribute != null;
                var notSyncedInMultiplayerAttribute = type.GetCustomAttribute<NotSyncedInMultiplayer>();
                NotSyncedInMultiplayer = notSyncedInMultiplayerAttribute != null;
                AlternativeTypes = reqByCoreAttribute?.AlternativeTypes;
                contentPathMutator
                    = Type.GetMethod(nameof(MutateContentPath), BindingFlags.Static | BindingFlags.Public);

                HashSet<Identifier> names = new HashSet<Identifier> { type.Name.RemoveFromEnd("File").ToIdentifier() };
                if (type.GetCustomAttribute<AlternativeContentTypeNames>(inherit: false)?.Names is { } altNames)
                {
                    names.UnionWith(altNames);
                }
                if (type.GetCustomAttribute<TypePreviouslyKnownAs>() is { } knownAsAttribute)
                {
                    names.Add(knownAsAttribute.PreviousName.RemoveFromEnd("File").ToIdentifier());
                }

                Names = names.ToImmutableHashSet();
            }

            public ContentPath MutateContentPath(ContentPath path)
                => (ContentPath?)contentPathMutator?.Invoke(null, new object[] { path })
                   ?? path;
            
            public ContentFile? CreateInstance(ContentPackage contentPackage, ContentPath path) =>
                (ContentFile?)Activator.CreateInstance(Type, contentPackage, path);
        }

        public static readonly ImmutableHashSet<TypeInfo> Types;

        /// <summary>
        /// The maximum number of instances of this content file type that can be loaded in a single content package.
        /// Used currently by plugin content type since having multiple plugin instances per content package would
        /// cause issues and there's no real reason to have more than one.
        /// </summary>
        public virtual int MaxAllowedInstances
            => int.MaxValue;

        /// <summary>
        /// Can content packages containing this content file be downloaded by clients when joining a server?
        /// </summary>
        public virtual bool CanBeDownloadedFromServer
            => true;

        static ContentFile()
        {
            Types = ReflectionUtils.GetDerivedNonAbstract<ContentFile>()
                .Select(t => new TypeInfo(t))
                .ToImmutableHashSet();
        }

        public static bool IsLegacyContentType(XElement contentFileElement, ContentPackage package, bool logWarning)
        {
            Identifier elemName = contentFileElement.NameAsIdentifier();
            if (elemName == "TraitorMissions")
            {
                if (logWarning)
                {
                    DebugConsole.AddWarning(
                      $"The content type \"TraitorMission\" in content package \"{package.Name}\" is no longer supported." +
                      $" Traitor missions should be implemented using the scripted event system and the content type TraitorEvents.",
                      package);
                }
                return true;
            }
            return false;
        }

        public static Result<ContentFile, ContentPackage.LoadError> CreateFromXElement(ContentPackage contentPackage, XElement element)
        {
            static Result<ContentFile, ContentPackage.LoadError> fail(string error, Exception? exception = null)
                => Result<ContentFile, ContentPackage.LoadError>.Failure(new ContentPackage.LoadError(error, exception));

            Identifier elemName = element.NameAsIdentifier();

            // first check vanilla types
            var type = Types.FirstOrDefault(t => t.Names.Contains(elemName))
                       // check if the given type is implemented in a plugin already
                       ?? PluginData.AllDynamicTypes.FirstOrDefault(t => t.Names.Contains(elemName))
                       // if the type is still not found, we assume it will be implemented by a plugin so assign it to DynamicallyLoadedFile
                       ?? Types.First(static t => t.Type == typeof(DynamicallyLoadedFile));

            var filePath = element.GetAttributeContentPath("file", contentPackage);
            if (filePath is null)
            {
                return fail($"No content path defined for file of type \"{elemName}\"");
            }

            using var errorCatcher = DebugConsole.ErrorCatcher.Create();
            try
            {
                filePath = type.MutateContentPath(filePath);
                if (!File.Exists(filePath.FullPath))
                {
                    return fail($"Failed to load file \"{filePath}\" of type \"{elemName}\": file not found.");
                }

                ContentFile? file =
                    type.Type == typeof(DynamicallyLoadedFile)
                        // exception for dynamically loaded files since they take an extra parameter
                        ? new DynamicallyLoadedFile(elemName.ToString(), contentPackage, filePath)
                        // use reflection for everything else
                        : type.CreateInstance(contentPackage, filePath);

                if (file is null) { return fail($"Content type {type.Type.Name} is not implemented correctly"); }

                if (errorCatcher.Errors.Any())
                {
                    return fail(
                        $"Errors were issued to the debug console when loading \"{filePath}\" of type \"{elemName}\"");
                }
                return Result<ContentFile, ContentPackage.LoadError>.Success(file);
            }
            catch (Exception e)
            {
                return fail($"Failed to load file \"{filePath}\" of type \"{elemName}\": {e.Message}", e);
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

        public virtual bool NotSyncedInMultiplayer => Types.Any(t => t.Type == GetType() && t.NotSyncedInMultiplayer);
    }
}