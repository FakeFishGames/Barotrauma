#nullable enable

using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Barotrauma.IO;
using System.Xml.Linq;

namespace Barotrauma
{
    public sealed class ContentPath
    {
        public readonly static ContentPath Empty = new ContentPath((ContentPackage?)null, "");

        public const string ModDirStr = "%ModDir%";
        public const string OtherModDirFmt = "%ModDir:{0}%";
        private static readonly Regex OtherModDirRegex = new Regex(
            string.Format(OtherModDirFmt, "(.+?)"));

        public readonly string? RawValue;

        public readonly ContentPackage? ContentPackage;

        private string? cachedValue;
        private string? cachedFullPath;

        // these refer to the content of the raw string.
        // the package name is NOT the same as father
        // content package name.
        private string? cachedRelativePath;
        private string? cachedPackageName;
        private string? cachedPackagePath;

        // the directory containing the file refering to
        // this path (created from attribute)
        public string? base_path { get; private set; }

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
                    if (ContentPackage.SteamWorkshopId != 0)
                    {
                        cachedValue = cachedValue
                            .Replace(string.Format(OtherModDirFmt, ContentPackage.SteamWorkshopId.ToString(CultureInfo.InvariantCulture)), modPath, StringComparison.OrdinalIgnoreCase);
                    }
                }
                var allPackages = ContentPackageManager.AllPackages;
#if CLIENT
                if (GameMain.ModDownloadScreen?.DownloadedPackages != null) { allPackages = allPackages.Concat(GameMain.ModDownloadScreen.DownloadedPackages); }
#endif
                foreach (Identifier otherModName in otherMods)
                {
                    if (!UInt64.TryParse(otherModName.Value, out UInt64 workshopId)) { workshopId = 0; }
                    ContentPackage? otherMod =
                        allPackages.FirstOrDefault(p => workshopId != 0 && p.SteamWorkshopId != 0 && workshopId == p.SteamWorkshopId)
                        ?? allPackages.FirstOrDefault(p => p.Name == otherModName)
                        ?? allPackages.FirstOrDefault(p => p.NameMatches(otherModName))
                        ?? throw new MissingContentPackageException(ContentPackage, otherModName.Value);
                    cachedValue = cachedValue.Replace(string.Format(OtherModDirFmt, otherModName.Value), Path.GetDirectoryName(otherMod.Path));
                }
                cachedValue = cachedValue.CleanUpPath();
                return cachedValue;
            }
        }

        // path relative to the package's root
        public string RelativePath {
            get
            {
                return cachedRelativePath??"";
            }
		}

        // package of %ModDir:xxxx%, xxxx, or vanilla path for Content/
        public string PackagePath {
            get {
                if (!cachedPackagePath.IsNullOrEmpty()) return cachedPackagePath!;
                var allPackages = ContentPackageManager.AllPackages;
#if CLIENT
                if (GameMain.ModDownloadScreen?.DownloadedPackages != null) { allPackages = allPackages.Concat(GameMain.ModDownloadScreen.DownloadedPackages); }
#endif
                {
                    if (!UInt64.TryParse(cachedPackageName, out UInt64 workshopId)) { workshopId = 0; }
                    ContentPackage? otherMod =
                        allPackages.FirstOrDefault(p => workshopId != 0 && p.SteamWorkshopId != 0 && workshopId == p.SteamWorkshopId)
                        ?? allPackages.FirstOrDefault(p => p.Name == cachedPackageName)
                        ?? allPackages.FirstOrDefault(p => p.NameMatches(cachedPackageName!))
                        ?? throw new MissingContentPackageException(ContentPackage, cachedPackageName);
                    cachedPackagePath = Path.GetDirectoryName(otherMod.Path);
                }
                cachedPackagePath = cachedPackagePath.CleanUpPathCrossPlatform();
                return cachedPackagePath;
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

        private ContentPath(ContentPath? parent, string? rawValue)
        {
            ContentPackage = parent?.ContentPackage;
            RawValue = rawValue;
            cachedValue = null;
            cachedFullPath = null;

            if (!(parent is null))
            {
                string dir;
                if(parent.ContentPackage is CorePackage){
                    dir = "";
                }
                else{
                    dir = parent.ContentPackage?.Dir ?? "";
                }
                EvaluateRelativePath(parent.FullPath, dir, parent.ContentPackage is CorePackage);
            }
        }

        private ContentPath(ContentPackage? parent, string? rawValue)
        {
            ContentPackage = parent;
            RawValue = rawValue;
            cachedValue = null;
            cachedFullPath = null;

            if (!(parent is null))
            {
                string dir;
                if (parent is CorePackage)
                {
                    dir = "";
                }
                else
                {
                    dir = parent.Dir;
                }
                EvaluateRelativePath("", dir, parent is CorePackage);
            }
        }

        private void EvaluateRelativePath(string? parent_full_path, string parent_package_path, bool parent_is_vanilla){
            string? modName = ContentPackage?.Name;

            var cleanvalue = RawValue.CleanUpPathCrossPlatform();
            if (cleanvalue.IsNullOrEmpty())
            {
                return;
            }
            if (cleanvalue.StartsWith("Content/") || cleanvalue.StartsWith("Submarines/") || cleanvalue.StartsWith("Data/"))
            {
                // could be null if loading vanilla
                //cachedPackageName = ContentPackageManager.VanillaCorePackage!.Name;
                cachedPackageName = "Vanilla";
                cachedRelativePath = cleanvalue;
                isVanilla = true;
            }
            // typically when loading filelist.xml from LocalMods/
            else if(cleanvalue.StartsWith("LocalMods/")){
                // typically only used when loading filelist.xml.
                // mod internal use should never use this.
                cachedPackageName = "Vanilla";
                cachedRelativePath = cleanvalue;
                isVanilla = true;
            }
            else if (cleanvalue.StartsWith(ModDirStr + "/", StringComparison.OrdinalIgnoreCase))
            {
                cachedPackageName = modName;
                cachedRelativePath = cleanvalue.Substring(ModDirStr.Length + 1);
                isVanilla = false;
            }
            else
            {
                var mod_dir_regex = new Regex("^%ModDir:(.+?)%/(.+?)$", RegexOptions.IgnoreCase);
                var otherMod = mod_dir_regex.Match(cleanvalue);
                if (!otherMod.Success)
                {
                    if (parent_full_path.IsNullOrEmpty())
                    {
                        // what is this??? user specified relative path in character editor's texture config?
                        throw new Exception("Relative path relative to nothing.");
                    }
                    string location = System.IO.Path.GetDirectoryName(parent_full_path).CleanUpPathCrossPlatform();
                    cachedPackageName = modName;
                    if(parent_is_vanilla) {
                        cachedRelativePath = System.IO.Path.GetRelativePath(".", string.Join('/', location, cleanvalue));
                    }
                    else {
                        cachedRelativePath = System.IO.Path.GetRelativePath(parent_package_path, string.Join('/', location, cleanvalue));
                    }
                    isVanilla = parent_is_vanilla;
                }
                else
                {
                    cachedPackageName = otherMod.Groups[1].Value.Trim();
                    cachedRelativePath = otherMod.Groups[2].Value;
                    isVanilla = false;
                }
            }
        }

        public bool isVanilla { get; private set; }

        public static ContentPath FromRaw(string? rawValue)
            => new ContentPath((ContentPackage?)null, rawValue);

        public static ContentPath FromRawNoConcrete(ContentPackage? contentPackage, string? rawValue)
            => new ContentPath(contentPackage, rawValue);

        public static ContentPath FromRaw(ContentPath? parent, string? rawValue)
            => new ContentPath(parent, rawValue);

        public ContentPath MutateContentPath(ContentPath newParent) {
            if(isVanilla){
                return FromRaw(newParent, RelativePath);
			}
            else {
                return FromRaw(newParent, string.Format("%ModDir:{0}%/{1}", cachedPackageName, RelativePath));
            }
		}

        public string ToAttrString(){
            if(isVanilla){
                return RelativePath;
			}
            else{
                return string.Format("%ModDir:{0}%/{1}", cachedPackageName, RelativePath);
            }
		}

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
