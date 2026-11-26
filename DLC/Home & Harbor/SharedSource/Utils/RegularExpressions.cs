using System.Text.RegularExpressions;

namespace HomeAndHarbor
{
    public static partial class RegularExpressions
    {
        public const string TypeGroup = "type",
                            PathGroup = "path";

        [GeneratedRegex($"(?<{TypeGroup}>\\w+)::(?<{PathGroup}>.+)")]
        public static partial Regex LinkedResourceRegex();
    }
}