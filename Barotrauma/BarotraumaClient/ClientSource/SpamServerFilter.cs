#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Barotrauma.IO;
using Barotrauma.Networking;
using RestSharp;
using XmlWriter = Barotrauma.IO.XmlWriter;

namespace Barotrauma
{
    public enum SpamServerFilterType
    {
        Invalid,
        NameEquals,
        NameContains,
        NameMatchesRegex,
        MessageEquals,
        MessageContains,
        MessageMatchesRegex,
        PlayerCountLarger,
        PlayerCountExact,
        MaxPlayersLarger,
        MaxPlayersExact,
        GameModeEquals,
        PlayStyleEquals,
        Endpoint,
        LanguageEquals,
        LobbyId
    }

    internal readonly record struct SpamFilter(ImmutableHashSet<(SpamServerFilterType Type, string Value, string NormalizedValue)> Filters)
    {
        public bool IsFiltered(ServerInfo info)
        {
            if (Filters.IsEmpty) { return false; }

            foreach (var (type, value, normalizedValue) in Filters)
            {
                try
                {
                    if (!IsFiltered(info, type, value, normalizedValue)) { return false; }
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError($"Failed to check filter type {type} on the server info {(info.ServerName ?? "null")}.", e);
                }
            }

            return true;
        }

        private static bool IsFiltered(ServerInfo info, SpamServerFilterType type, string value, string normalizedValue)
        {
            if (info == null) { return true; }

            int.TryParse(value, out int parsedInt);

            return type switch
            {
                SpamServerFilterType.NameEquals => CompareEquals(info.NormalizedServerName, normalizedValue),
                SpamServerFilterType.NameContains => CompareContains(info.NormalizedServerName, normalizedValue),
                SpamServerFilterType.NameMatchesRegex => CompareRegex(info.NormalizedServerName, value),

                SpamServerFilterType.MessageEquals => CompareEquals(info.NormalizedServerMessage, normalizedValue),
                SpamServerFilterType.MessageContains => CompareContains(info.NormalizedServerMessage, normalizedValue),
                SpamServerFilterType.MessageMatchesRegex => CompareRegex(info.NormalizedServerMessage, value),

                SpamServerFilterType.Endpoint =>
                    info.Endpoints != null &&
                    info.Endpoints.First().StringRepresentation.Equals(value, StringComparison.OrdinalIgnoreCase),

                SpamServerFilterType.LobbyId =>
                    info.MetadataSource.TryUnwrap(out var dataSource) &&
                    dataSource is SteamP2PServerProvider.DataSource steamDataSource &&
                    ulong.TryParse(value, out ulong lobbyIdToFilter) &&
                    steamDataSource.Lobby.Id == lobbyIdToFilter,

                SpamServerFilterType.PlayerCountLarger => info.PlayerCount > parsedInt,
                SpamServerFilterType.PlayerCountExact => info.PlayerCount == parsedInt,

                SpamServerFilterType.MaxPlayersLarger => info.MaxPlayers > parsedInt,
                SpamServerFilterType.MaxPlayersExact => info.MaxPlayers == parsedInt,

                SpamServerFilterType.GameModeEquals => CompareEquals(info.NormalizedGameMode, normalizedValue),
                SpamServerFilterType.PlayStyleEquals => info.PlayStyle.ToIdentifier() == value,

                SpamServerFilterType.LanguageEquals => info.Language.Value == value,
                _ => false
            };

            static bool CompareEquals(string? normalizedA, string? normalizedB)
            {
                if (normalizedA == null || normalizedB == null)
                {
                    return normalizedA == normalizedB;
                }
                // Both strings are already normalized, just do case-insensitive comparison
                return normalizedA.Equals(normalizedB, StringComparison.OrdinalIgnoreCase);
            }

            static bool CompareContains(string? normalizedA, string? normalizedB)
            {
                if (normalizedA == null || normalizedB == null)
                {
                    return normalizedA == normalizedB;
                }
                // Both strings are already normalized, just do case-insensitive contains
                return normalizedA.Contains(normalizedB, StringComparison.OrdinalIgnoreCase);
            }

            static bool CompareRegex(string? a, string? pattern)
            {
                if (a == null || pattern == null)
                {
                    return a == pattern;
                }

                // Use cached compiled regex for performance
                if (SpamServerFilters.GetCachedRegex(pattern) is Regex regex)
                {
                    return regex.IsMatch(a);
                }
                else
                {
                    DebugConsole.ThrowError($"Regex pattern somehow not found in cache: \"{pattern}\"");
                }

                return false;
            }
        }

        public XElement Serialize()
        {
            var element = new XElement("Filter");

            foreach (var (type, value, _) in Filters)
            {
                element.Add(new XAttribute(type.ToString().ToLowerInvariant(), value));
            }

            return element;
        }

        public static bool TryParse(XElement element, out SpamFilter filter)
        {
            var builder = ImmutableHashSet.CreateBuilder<(SpamServerFilterType Type, string Value, string NormalizedValue)>();
            foreach (var attribute in element.Attributes())
            {
                if (!Enum.TryParse(attribute.Name.ToString(), ignoreCase: true, out SpamServerFilterType e))
                {
                    DebugConsole.ThrowError($"Failed to parse spam filter attribute \"{attribute.Name}\"");
                    continue;
                }
                if (e is SpamServerFilterType.Invalid) { continue; }
                string value = attribute.Value;

                // Compile regex patterns during loading (for validation and performance)
                if (e is SpamServerFilterType.NameMatchesRegex or SpamServerFilterType.MessageMatchesRegex)
                {
                    // Skip invalid regex filters (will throw error to the log though)
                    if (!SpamServerFilters.TryCompileAndCacheRegex(value)) { continue; }
                }

                // Only normalize values for filter types that actually use homoglyph comparison
                string normalizedValue = e is SpamServerFilterType.NameEquals
                    or SpamServerFilterType.NameContains
                    or SpamServerFilterType.MessageEquals
                    or SpamServerFilterType.MessageContains
                    or SpamServerFilterType.GameModeEquals
                    ? Homoglyphs.Normalize(value)
                    : value;

                builder.Add((e, value, normalizedValue));
            }

            if (builder.Any())
            {
                filter = new SpamFilter(builder.ToImmutable());
                return true;
            }

            filter = default;
            return false;
        }

        public override string ToString()
        {
            return !Filters.Any() ? "Invalid Filter" : string.Join(", ", Filters.Select(static f => $"{f.Type}: {f.Value}"));
        }
    }

    internal sealed class SpamServerFilter
    {
        public readonly ImmutableArray<SpamFilter> Filters;

        public bool IsFiltered(ServerInfo info)
        {
            foreach (var f in Filters)
            {
                if (f.IsFiltered(info)) { return true; }
            }

            return false;
        }

        public SpamServerFilter(XElement element)
        {
            var builder = ImmutableArray.CreateBuilder<SpamFilter>();
            foreach (var subElement in element.Elements())
            {
                if (SpamFilter.TryParse(subElement, out var filter))
                {
                    builder.Add(filter);
                }
            }
            Filters = builder.ToImmutable();
        }

        public SpamServerFilter(ImmutableArray<SpamFilter> filters)
            => Filters = filters;

        public readonly static string SavePath = Path.Combine("Data", "serverblacklist.xml");

        public void Save(string path)
        {
            var comment = new XComment(SpamServerFilters.LocalFilterComment);
            var doc = new XDocument(comment, new XElement("Filters"));
            foreach (var filter in Filters)
            {
                doc.Root?.Add(filter.Serialize());
            }

            try
            {
                using var writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true });
                doc.SaveSafe(writer);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving spam filter failed.", e);
            }
        }
    }

    internal static class SpamServerFilters
    {
        public static Option<SpamServerFilter> LocalSpamFilter;
        public static Option<SpamServerFilter> GlobalSpamFilter;

        private static readonly Dictionary<string, Regex> CompiledRegexCache = new Dictionary<string, Regex>();

        /// <summary>
        /// Attempts to compile a regex pattern and cache it. Returns false if the pattern is invalid.
        /// Compilation validates the regex is correct, avoiding crashes at runtime, and subsequent use will be more performant.
        /// </summary>
        internal static bool TryCompileAndCacheRegex(string pattern)
        {
            if (CompiledRegexCache.ContainsKey(pattern)) { return true; }

            try
            {
                var regex = new Regex(pattern, RegexOptions.Compiled);
                CompiledRegexCache[pattern] = regex;
                return true;
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Invalid regex pattern in spam filter: \"{pattern}\"", e);
                return false;
            }
        }

        /// <summary>
        /// Attempts to get a cached compiled regex, returns null if not found.
        /// </summary>
        internal static Regex? GetCachedRegex(string pattern)
        {
            return CompiledRegexCache.GetValueOrDefault(pattern);
        }

        public const string LocalFilterComment = @"
This file contains a list of filters that can be used to hide servers from the server list.
You can add filters by right-clicking a server in the server list and selecting ""Hide server"" or by reporting the server and choosing ""Report and hide server"".
The filters are saved in this file, which you can edit manually if you want to.

The available filter types are:
- NameEquals: The server name must equal the specified value. Homoglyphs are also checked.
- NameContains: The server name must contain the specified value. Homoglyphs are also checked.
- NameMatchesRegex: The server name must match the specified regular expression pattern. Use inline options like (?i) for case-insensitive matching.
- MessageEquals: The server description must equal the specified value. Homoglyphs are also checked.
- MessageContains: The server description must contain the specified value. Homoglyphs are also checked.
- MessageMatchesRegex: The server description must match the specified regular expression pattern. Use inline options like (?i) for case-insensitive matching.
- PlayerCountLarger: The player count must be larger than the specified value.
- PlayerCountExact: The player count must match the specified value exactly.
- MaxPlayersLarger: The max player count must be larger than the specified value.
- MaxPlayersExact: The max player count must match the specified value exactly.
- GameModeEquals: The game mode identifier must match the specified value exactly. Homoglyphs are also checked.
- PlayStyleEquals: The play style must match the specified value exactly.
- Endpoint: The server endpoint, which is a Steam ID or an IP address, must match the specified value exactly. Steam ID is in the format of STEAM_X:Y:Z.
- LanguageEquals: The server language must match the specified value exactly.
- LobbyId: The Steam lobby ID must match the specified value exactly. This is the most efficient way to filter Steam P2P lobbies, when we have already identified harmful ones.

The filter values are case-insensitive and adding multiple conditions on one filter will require all of them to be met.
Homoglyph comparison is used for name, message, and game mode filters, which means that it checks whether the words look the same, meaning you can't abuse identical-looking but different symbols to work around the filter. For example ""lmaobox"" and ""lmаobox"" (with a cyrillic a) are considered equal, and ""dіscord.gg"" (with a cyrillic i) will be caught by a ""discord.gg"" contains filter.

Examples:
<Filters>
  <Filter namecontains=""discord.gg"" />
  <Filter messagecontains=""discord.gg"" />
  <Filter nameequals=""get good get lmaobox"" maxplayersexact=""999"" />
  <Filter lobbyid=""109775241070418378"" />
  <Filter namematchesregex=""(?i)(buy|sell|trade).*cheap"" />
  <Filter messagematchesregex=""(?i)join.*discord\.(gg|com)"" />
</Filters>
These will hide all servers that have a discord.gg link in their name or description, servers with the name ""get good get lmaobox"" that have 999 max players, the specific lobby with ID 109775241070418378, servers with names matching the pattern for buying/selling/trading (case-insensitive), and servers with messages containing discord links (case-insensitive)..
";
        static SpamServerFilters()
        {
            XDocument? doc;
            if (!File.Exists(SpamServerFilter.SavePath))
            {
                var comment = new XComment(LocalFilterComment);

                doc = new XDocument(comment, new XElement("Filters"));

                try
                {
                    using var writer = XmlWriter.Create(SpamServerFilter.SavePath, new XmlWriterSettings { Indent = true });
                    doc.SaveSafe(writer);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Saving spam filter failed.", e);
                }
            }
            else
            {
                doc = XMLExtensions.TryLoadXml(SpamServerFilter.SavePath);
            }

            if (doc?.Root is { } root)
            {
                LocalSpamFilter = Option.Some(new SpamServerFilter(root));
            }
        }

        public static bool IsFiltered(ServerInfo info)
        {
            if (LocalSpamFilter.TryUnwrap(out var localFilter) && localFilter.IsFiltered(info)) { return true; }
            if (GlobalSpamFilter.TryUnwrap(out var globalFilter) && globalFilter.IsFiltered(info)) { return true; }
            return false;
        }

        public static void AddServerToLocalSpamList(ServerInfo info)
        {
            if (!LocalSpamFilter.TryUnwrap(out var localFilter)) { return; }
            if (localFilter.IsFiltered(info)) { return; }

            var filters = localFilter.Filters.Add(new SpamFilter(ImmutableHashSet.Create((SpamServerFilterType.NameEquals, info.ServerName, info.NormalizedServerName))));
            var newFilter = new SpamServerFilter(filters);
            newFilter.Save(SpamServerFilter.SavePath);
            LocalSpamFilter = Option.Some(newFilter);
        }

        public static void ClearLocalSpamFilter()
        {
            var newFilter = new SpamServerFilter(ImmutableArray<SpamFilter>.Empty);
            newFilter.Save(SpamServerFilter.SavePath);
            LocalSpamFilter = Option.Some(newFilter);
        }

        public static void RequestGlobalSpamFilter()
        {
            if (GameSettings.CurrentConfig.DisableGlobalSpamList) { return; }

            string remoteContentUrl = GameSettings.CurrentConfig.RemoteMainMenuContentUrl;
            if (string.IsNullOrEmpty(remoteContentUrl)) { return; }

            try
            {
                var client = new RestClient($"{remoteContentUrl}spamfilter")
                {
                    CachePolicy = new HttpRequestCachePolicy(HttpRequestCacheLevel.NoCacheNoStore)
                };
                client.AddDefaultHeader("Cache-Control", "no-cache");
                client.AddDefaultHeader("Pragma", "no-cache");
                var request = new RestRequest("serve_spamlist.php", Method.GET);
                TaskPool.Add("RequestGlobalSpamFilter", client.ExecuteAsync(request), RemoteContentReceived);
            }
            catch (Exception e)
            {
#if DEBUG
                DebugConsole.ThrowError("Fetching global spam list failed.", e);
#endif
                GameAnalyticsManager.AddErrorEventOnce("SpamServerFilters.RequestGlobalSpamFilter:Exception", GameAnalyticsManager.ErrorSeverity.Error,
                    "Fetching global spam list failed. " + e.Message);
            }

            static void RemoteContentReceived(Task t)
            {
                try
                {
                    if (!t.TryGetResult(out IRestResponse? remoteContentResponse)) { throw new Exception("Task did not return a valid result"); }
                    if (remoteContentResponse.StatusCode != HttpStatusCode.OK)
                    {
                        DebugConsole.AddWarning(
                            "Failed to receive global spam filter." +
                            "There may be an issue with your internet connection, or the master server might be temporarily unavailable " +
                            $"(error code: {remoteContentResponse.StatusCode})");
                        return;
                    }
                    string data = remoteContentResponse.Content;
                    if (string.IsNullOrWhiteSpace(data)) { return; }

                    if (XDocument.Parse(data).Root is { } root)
                    {
                        GlobalSpamFilter = Option.Some(new SpamServerFilter(root));
                    }
                }
                catch (Exception e)
                {
#if DEBUG
                    DebugConsole.ThrowError("Reading received global spam filter failed.", e);
#endif
                    GameAnalyticsManager.AddErrorEventOnce("SpamServerFilters.RemoteContentReceived:Exception", GameAnalyticsManager.ErrorSeverity.Error,
                        "Reading received global spam filter failed. " + e.Message);
                }
            }
        }
    }
}