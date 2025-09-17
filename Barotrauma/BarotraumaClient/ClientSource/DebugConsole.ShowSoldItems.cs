using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    internal static partial class DebugConsole
    {
        private static void InitShowSoldItems()
        {
            commands.Add(new Command("showsolditems",
            "showsolditems [filter (no-defined/only-min/only-max/name:pattern)] [Include stores (true/false)] [Sold only (true/false)] [limit (number)] [Hide store overrides from output. (true/false)]: " +
            "Lists items and their shop availability settings. Filter can be availability filter or name pattern (e.g. 'name:*rifle*'). Include stores controls whether to check store-specific overrides (default true). Sold only controls whether to show only sold items (default true). Limit parameter controls how many items to show (default 50). Hide store overrides from output, defaults to false.",
            (string[] args) =>
            {
                string filter = args.Length > 0 ? args[0].ToLowerInvariant() : null;
                bool includeStores = args.Length <= 1 || !args[1].Equals("false", StringComparison.InvariantCultureIgnoreCase);
                bool soldOnly = args.Length <= 2 || !args[2].Equals("false", StringComparison.InvariantCultureIgnoreCase);
                int limit = 50;
                if (args.Length > 3 && int.TryParse(args[3], out int parsedLimit))
                {
                    limit = Math.Max(1, parsedLimit);
                }
                bool hideStoreOverrides = args.Length > 4 && args[4].Equals("true", StringComparison.InvariantCultureIgnoreCase);
                
                var itemsWithPrice = ItemPrefab.Prefabs
                    .Where(item => item.ConfigElement.Element.Element("Price") != null);

                // apply filtering
                var matchingItems = itemsWithPrice
                    .OrderBy(i => i.Name.Value)
                    .Where(item => MatchesFilter(item, filter, includeStores, soldOnly))
                    .ToList();

                // output results
                NewMessage("=== Shop Item Availability ===", Color.Cyan);
                NewMessage($"Filter: {filter ?? "all"}, IncludeStores: {includeStores}, SoldOnly: {soldOnly}, Limit: {limit}, HideStoreOverrides: {hideStoreOverrides}", Color.Yellow);
                NewMessage($"Items: {matchingItems.Count} matching out of {itemsWithPrice.Count()} being sold (showing first {Math.Min(limit, matchingItems.Count)})", Color.LightGreen);
                NewMessage("", Color.White);

                foreach (var item in matchingItems.Take(limit))
                {
                    PrintItemInfo(item, hideStoreOverrides);
                }
            },
            getValidArgs: () =>
            [
                ["all", "no-defined", "only-min", "only-max", "name:*"], // filter
                ["true", "false"], // includeStores 
                ["true", "false"], // soldOnly
                ["10", "25", "50", "100"], // limit suggestions
                ["false", "true"] // hidestoreoverrides
            ]));
        }

        private static bool MatchesFilter(ItemPrefab item, string filter, bool includeStores, bool soldOnly)
        {
            var priceElement = item.ConfigElement.Element.Element("Price");
            if (priceElement == null) { return false; } // No price = not sold = don't include
            if (!includeStores) { return MatchesPriceElement(priceElement, item, filter, soldOnly); }
            
            // Check if Base element matches first...
            if (MatchesPriceElement(priceElement, item, filter, soldOnly)) { return true; }
            
            // ...then check store-specific price element matches
            foreach (var storeElement in priceElement.Elements().Where(e => e.Name == "Price"))
            {
                if (MatchesPriceElement(storeElement, item, filter, soldOnly)) { return true; }
            }
            
            return false;
        }

        private static bool MatchesPriceElement(XElement priceEl, ItemPrefab itemPrefab, string filter, bool soldOnly)
        {
            bool isSold = PriceInfo.GetSold(priceEl, true);
            if (soldOnly && !isSold) { return false; }
            if (filter == null) { return true; }

            // Handle name pattern matching
            if (filter.StartsWith("name:"))
            {
                string pattern = filter[5..];
                string name = itemPrefab.Name.Value.ToLowerInvariant();
                string identifier = itemPrefab.Identifier.Value.ToLowerInvariant();

                // If pattern contains '*', treat as wildcard (convert to regex)
                if (pattern.Contains('*'))
                {
                    // Escape regex special chars except *
                    string regexPattern = System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*");
                    return System.Text.RegularExpressions.Regex.IsMatch(name, $"^{regexPattern}$")
                        || System.Text.RegularExpressions.Regex.IsMatch(identifier, $"^{regexPattern}$");
                }
                else
                {
                    // No wildcards: match exactly
                    return name == pattern || identifier == pattern;
                }
            }

            bool hasMin = PriceInfo.HasMinAmountDefined(priceEl);
            bool hasMax = PriceInfo.HasMaxAmountDefined(priceEl);
            
            // Apply the filter logic
            return filter switch
            {
                "no-defined" => !hasMin && !hasMax,        // Neither min nor max defined
                "only-min" => hasMin && !hasMax,           // Only min defined
                "only-max" => !hasMin && hasMax,           // Only max defined
                _ => true                                   // No filter or show all
            };
        }

        private static void PrintItemInfo(ItemPrefab item, bool hideStoreOverrides = false)
        {
            var priceElement = item.ConfigElement.Element.Element("Price");
            if (priceElement == null) { return; }
            
            bool hasMinDefined = PriceInfo.HasMinAmountDefined(priceElement);
            bool hasMaxDefined = PriceInfo.HasMaxAmountDefined(priceElement);
            
            string minRaw = PriceInfo.GetMinAmountString(priceElement);
            string maxRaw = PriceInfo.GetMaxAmountString(priceElement);
            int minLevelDifficulty = PriceInfo.GetMinLevelDifficulty(priceElement, 0);
            
            // Get the resolved values (what PriceInfo would actually use)
            var priceInfo = new PriceInfo(priceElement);
            int resolvedMin = priceInfo.MinAvailableAmount;
            int resolvedMax = priceInfo.MaxAvailableAmount;
            
            string minStatus = hasMinDefined ? $"XML:{minRaw}" : "DEFAULT:1";
            string maxStatus = hasMaxDefined ? $"XML:{maxRaw}" : "DEFAULT:5";
            
            string minLevelInfo = minLevelDifficulty > 0 ? $" | MinLvl: {minLevelDifficulty}" : "";
            NewMessage($"{item.Name} ({item.Identifier}) | Min: {minStatus} → {resolvedMin} | Max: {maxStatus} → {resolvedMax}{minLevelInfo}", Color.White);
            
            if (hideStoreOverrides) { return; }

            var storeOverrides = priceElement.Elements().Where(e => e.Name == "Price")
                .Select(p => {
                    string storeId = PriceInfo.GetStoreIdentifier(p, "unknown");
                    string storeMin = PriceInfo.GetMinAmountString(p);
                    string storeMax = PriceInfo.GetMaxAmountString(p);
                    bool? storeSold = PriceInfo.HasSoldDefined(p) ? PriceInfo.GetSold(p, true) : null;
                    
                    // Check if this store overrides anything
                    if (storeMin != null || storeMax != null || storeSold != null)
                    {
                        var parts = new List<string>();
                        if (storeMin != null || storeMax != null)
                        {
                            parts.Add($"min:{storeMin ?? "base"}, max:{storeMax ?? "base"}");
                        }
                        if (storeSold != null)
                        {
                            parts.Add($"sold:{storeSold.Value.ToString().ToLowerInvariant()}");
                        }
                        return $"{storeId}({string.Join(", ", parts)})";
                    }
                    return null;
                })
                .Where(s => s != null)
                .ToList();
                
            if (storeOverrides.Count != 0)
            {
                NewMessage($"  Store overrides: {string.Join(", ", storeOverrides)}", Color.Gray);
            }
        }
    }
} 