#nullable enable

using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Barotrauma.IO;

namespace Barotrauma
{
    public sealed class ContentPath
    {
        public readonly static ContentPath Empty = new ContentPath(null, "");

        public const string ModDirStr = "%ModDir%";
        public const string OtherModDirFmt = "%ModDir:{0}%";
        private static readonly Regex OtherModDirRegex = new Regex(
            string.Format(OtherModDirFmt, "(.+?)"));
        
        public readonly string? RawValue;

        public readonly ContentPackage? ContentPackage;

        private string? cachedValue;
        private string? cachedFullPath;

        public string Value
        {
            get
            {
                if (RawValue.IsNullOrEmpty()) { return ""; }
                if (!cachedValue.IsNullOrEmpty()) { return cachedValue!; }

                string? modName = ContentPackage?.Name;

                var otherMods = OtherModDirRegex.Matches(RawValue ?? throw new NullReferenceException($"{nameof(RawValue)} is null."))
                    .Select(m => m.Groups[1].Value.Trim().ToIdentifier())
                    .Distinct().Where(id => !id.IsEmpty && id != modName).ToHashSet();
                cachedValue = RawValue!;
                if (!(ContentPackage is null))
                {
                    string modPath = Path.GetDirectoryName(ContentPackage.Path)!;
                    cachedValue = cachedValue
                        .Replace(ModDirStr, modPath, StringComparison.OrdinalIgnoreCase)
                        .Replace(string.Format(OtherModDirFmt, ContentPackage.Name), modPath, StringComparison.OrdinalIgnoreCase);
                    if (ContentPackage.UgcId.TryUnwrap(out var ugcId))
                    {
                        cachedValue = cachedValue
                            .Replace(string.Format(OtherModDirFmt, ugcId.StringRepresentation), modPath, StringComparison.OrdinalIgnoreCase);
                    }
                }
                var allPackages = ContentPackageManager.AllPackages;
#if CLIENT
                if (GameMain.ModDownloadScreen?.DownloadedPackages != null) { allPackages = allPackages.Concat(GameMain.ModDownloadScreen.DownloadedPackages); }
#endif
                foreach (Identifier otherModName in otherMods)
                {
                    Option<ContentPackageId> ugcId = ContentPackageId.Parse(otherModName.Value);
                    ContentPackage? otherMod =
                        allPackages.FirstOrDefault(p => ugcId == p.UgcId)
                        ?? allPackages.FirstOrDefault(p => p.Name == otherModName)
                        ?? allPackages.FirstOrDefault(p => p.NameMatches(otherModName))
                        ?? throw new MissingContentPackageException(ContentPackage, otherModName.Value);
                    cachedValue = cachedValue.Replace(string.Format(OtherModDirFmt, otherModName.Value), Path.GetDirectoryName(otherMod.Path));
                }
                cachedValue = cachedValue.CleanUpPath();
                return cachedValue;
            }
        }

        public string FullPath
        {
            get
            {
                if (cachedFullPath.IsNullOrEmpty())
                {
                    if (Value.IsNullOrEmpty())
                    {
                        return "";
                    }
                    cachedFullPath = Path.GetFullPath(Value).CleanUpPathCrossPlatform(correctFilenameCase: false);
                }
                return cachedFullPath!;
            }
        }

        private ContentPath(ContentPackage? contentPackage, string? rawValue)
        {
            ContentPackage = contentPackage;
            RawValue = rawValue;
            cachedValue = null;
            cachedFullPath = null;
        }

        public static ContentPath FromRaw(string? rawValue)
            => new ContentPath(null, rawValue);
        
        public static ContentPath FromRaw(ContentPackage? contentPackage, string? rawValue)
            => new ContentPath(contentPackage, rawValue);

        public static ContentPath FromEvaluated(ContentPackage? contentPackage, string? evaluatedValue)
        {
            throw new NotImplementedException();
        }
        
        private static bool StringEquality(string? a, string? b)
        {
            if (a.IsNullOrEmpty() || b.IsNullOrEmpty())
            {
                return a.IsNullOrEmpty() == b.IsNullOrEmpty();
            }
            return string.Equals(Path.GetFullPath(a.CleanUpPathCrossPlatform(false) ?? ""),
                    Path.GetFullPath(b.CleanUpPathCrossPlatform(false) ?? ""), StringComparison.OrdinalIgnoreCase);
        }

        public static bool operator==(ContentPath a, ContentPath b)
            => StringEquality(a?.Value, b?.Value);

        public static bool operator!=(ContentPath a, ContentPath b) => !(a == b);

        public static bool operator==(ContentPath a, string? b)
            => StringEquality(a?.Value, b);

        public static bool operator!=(ContentPath a, string? b) => !(a == b);

        public static bool operator==(string? a, ContentPath b)
            => StringEquality(a, b?.Value);

        public static bool operator!=(string? a, ContentPath b) => !(a == b);

        protected bool Equals(ContentPath other)
        {
            return RawValue == other.RawValue && Equals(ContentPackage, other.ContentPackage) && cachedValue == other.cachedValue && cachedFullPath == other.cachedFullPath;
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ContentPath)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(RawValue, ContentPackage, cachedValue, cachedFullPath);
        }

        public bool IsNullOrEmpty() => string.IsNullOrEmpty(Value);
        public bool IsNullOrWhiteSpace() => string.IsNullOrWhiteSpace(Value);

        public bool EndsWith(string suffix) => Value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        
        public override string? ToString() => Value;
    }
}