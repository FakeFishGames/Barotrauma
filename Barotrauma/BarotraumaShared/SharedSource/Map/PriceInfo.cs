using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    class PriceInfo
    {
        public int Price { get; }
        public bool CanBeBought { get; }

        /// <summary>
        /// Minimum number of items available at a given store
        /// </summary>
        public int MinAvailableAmount { get; }

        /// <summary>
        /// Maximum number of items available at a given store. Defaults to 20% more than the minimum amount.
        /// </summary>
        public int MaxAvailableAmount { get; }
        /// <summary>
        /// Can the item be a Daily Special or a Requested Good
        /// </summary>
        public bool CanBeSpecial { get; }
        /// <summary>
        /// The item isn't available in stores unless the level's difficulty is above this value
        /// </summary>
        public int MinLevelDifficulty { get; }
        /// <summary>
        /// The cost of item when sold by the store. Higher modifier means the item costs more to buy from the store.
        /// </summary>
        public float BuyingPriceMultiplier { get; } = 1f;
        public bool DisplayNonEmpty { get; } = false;
        public Identifier StoreIdentifier { get; }

        public bool RequiresUnlock { get; }

        /// <summary>
        /// Default minimum amount when no MinAvailableAmount is defined.
        /// </summary>
        private const int DefaultMinAmount = 1;

        /// <summary>
        /// Default maximum amount when no MaxAvailableAmount is defined.
        /// </summary>
        private const int DefaultMaxAmount = 5;

        /// <summary>
        /// If set, the item is only available in outposts with this faction.
        /// </summary>
        public Identifier RequiredFaction { get; private set; }

        private readonly Dictionary<Identifier, float> minReputation = new Dictionary<Identifier, float>();

        /// <summary>
        /// Minimum reputation needed to buy the item (Key = faction ID, Value = min rep)
        /// </summary>
        public IReadOnlyDictionary<Identifier, float> MinReputation => minReputation;

        /// <summary>
        /// Support for the old style of determining item prices
        /// when there were individual Price elements for each location type
        /// where the item was for sale.
        /// </summary>
        public PriceInfo(XElement element)
        {
            Price = element.GetAttributeInt("buyprice", 0);
            MinLevelDifficulty = GetMinLevelDifficulty(element, 0);
            BuyingPriceMultiplier = element.GetAttributeFloat("buyingpricemultiplier", 1f);
            CanBeBought = true;
            MinAvailableAmount = Math.Min(GetMinAmount(element, defaultValue: DefaultMinAmount), CargoManager.MaxQuantity);
            int maxAmount = GetMaxAmount(element, defaultValue: DefaultMaxAmount);
            MaxAvailableAmount = MathHelper.Clamp(maxAmount, MinAvailableAmount, CargoManager.MaxQuantity);
            RequiresUnlock = element.GetAttributeBool("requiresunlock", false);
            RequiredFaction = element.GetAttributeIdentifier(nameof(RequiredFaction), Identifier.Empty);
            System.Diagnostics.Debug.Assert(MaxAvailableAmount >= MinAvailableAmount);
        }

        public PriceInfo(int price, bool canBeBought,
            int minAmount = 0, int maxAmount = 0, bool canBeSpecial = true, int minLevelDifficulty = 0, float buyingPriceMultiplier = 1f,
            bool displayNonEmpty = false, bool requiresUnlock = false, string storeIdentifier = null)
        {
            Price = price;
            CanBeBought = canBeBought;
            MinAvailableAmount = Math.Min(minAmount, CargoManager.MaxQuantity);
            MaxAvailableAmount = Math.Max(Math.Min(maxAmount, CargoManager.MaxQuantity), minAmount);
            BuyingPriceMultiplier = buyingPriceMultiplier;
            MinLevelDifficulty = minLevelDifficulty;
            CanBeSpecial = canBeSpecial;
            DisplayNonEmpty = displayNonEmpty;
            StoreIdentifier = new Identifier(storeIdentifier);
            RequiresUnlock = requiresUnlock;

            System.Diagnostics.Debug.Assert(MaxAvailableAmount >= MinAvailableAmount);
        }

        private void LoadReputationRestrictions(XElement priceInfoElement)
        {
            foreach (XElement childElement in priceInfoElement.GetChildElements("reputation"))
            {
                Identifier factionId = childElement.GetAttributeIdentifier("faction", Identifier.Empty);
                float rep = childElement.GetAttributeFloat("min", 0.0f);
                if (!factionId.IsEmpty && rep > 0)
                {
                    minReputation.Add(factionId, rep);
                }
            }
        }

        public static List<PriceInfo> CreatePriceInfos(XElement element, out PriceInfo defaultPrice)
        {
            var priceInfos = new List<PriceInfo>();
            defaultPrice = null;
            int basePrice = element.GetAttributeInt("baseprice", 0);
            int minAmount = GetMinAmount(element, defaultValue: DefaultMinAmount);
            int maxAmount = GetMaxAmount(element, defaultValue: DefaultMaxAmount);
            int minLevelDifficulty = GetMinLevelDifficulty(element, 0);
            bool canBeSpecial = element.GetAttributeBool("canbespecial", true);
            float buyingPriceMultiplier = element.GetAttributeFloat("buyingpricemultiplier", 1f);
            bool displayNonEmpty = element.GetAttributeBool("displaynonempty", false);
            bool soldByDefault = GetSold(element, element.GetAttributeBool("soldbydefault", true));
            bool requiresUnlock = element.GetAttributeBool("requiresunlock", false);
            Identifier requiredFactionByDefault = element.GetAttributeIdentifier(nameof(RequiredFaction), Identifier.Empty);
            foreach (XElement childElement in element.GetChildElements("price"))
            {
                float priceMultiplier = childElement.GetAttributeFloat("multiplier", 1.0f);
                bool sold = GetSold(childElement, soldByDefault); 
                int storeMinLevelDifficulty = GetMinLevelDifficulty(childElement, minLevelDifficulty);
                float storeBuyingMultiplier = childElement.GetAttributeFloat("buyingpricemultiplier", buyingPriceMultiplier);
                string backwardsCompatibleIdentifier = childElement.GetAttributeString("locationtype", "");
                if (!string.IsNullOrEmpty(backwardsCompatibleIdentifier))
                {
                    backwardsCompatibleIdentifier = $"merchant{backwardsCompatibleIdentifier}";
                }
                string storeIdentifier = GetStoreIdentifier(childElement, backwardsCompatibleIdentifier);
                // TODO: Add some error messages if we have defined the min or max amount while the item is not sold
                var priceInfo = new PriceInfo(price: (int)(priceMultiplier * basePrice),
                    canBeBought: sold,
                    minAmount: sold ? GetMinAmount(childElement, minAmount) : 0,
                    maxAmount: sold ? GetMaxAmount(childElement, maxAmount) : 0,
                    canBeSpecial: canBeSpecial,
                    minLevelDifficulty: storeMinLevelDifficulty,
                    buyingPriceMultiplier: storeBuyingMultiplier,
                    displayNonEmpty: displayNonEmpty,
                    requiresUnlock: requiresUnlock,
                    storeIdentifier: storeIdentifier)
                {
                    RequiredFaction = childElement.GetAttributeIdentifier(nameof(RequiredFaction), requiredFactionByDefault)
                };
                priceInfo.LoadReputationRestrictions(childElement);
                priceInfos.Add(priceInfo);
            }
            bool soldElsewhere = soldByDefault && element.GetAttributeBool("soldelsewhere", element.GetAttributeBool("soldeverywhere", false));
            defaultPrice = new PriceInfo(price: basePrice,
                canBeBought: soldElsewhere,
                minAmount: soldElsewhere ? minAmount : 0,
                maxAmount: soldElsewhere ? maxAmount : 0,
                canBeSpecial: canBeSpecial,
                minLevelDifficulty: minLevelDifficulty,
                buyingPriceMultiplier: buyingPriceMultiplier,
                displayNonEmpty: displayNonEmpty,
                requiresUnlock: requiresUnlock)
            {
                RequiredFaction = requiredFactionByDefault
            };
            defaultPrice.LoadReputationRestrictions(element);
            return priceInfos;
        }

        private static int GetMinAmount(XElement element, int defaultValue) => 
            element?.GetAttributeInt("minamount", element.GetAttributeInt("minavailable", defaultValue)) ?? defaultValue;

        private static int GetMaxAmount(XElement element, int defaultValue) => 
            element?.GetAttributeInt("maxamount", element.GetAttributeInt("maxavailable", defaultValue)) ?? defaultValue;
        
        public static bool HasMinAmountDefined(XElement element) => element != null &&
            (element.GetAttribute("minamount") != null || element.GetAttribute("minavailable") != null);

        public static bool HasMaxAmountDefined(XElement element) => element != null &&
            (element.GetAttribute("maxamount") != null || element.GetAttribute("maxavailable") != null);

        public static bool HasSoldDefined(XElement element) => element != null &&
            element.GetAttribute("sold") != null;

        public static string GetMinAmountString(XElement element)
        {
            if (element == null) { return null; }
            return element.GetAttributeString("minamount", null) ?? element.GetAttributeString("minavailable", null);
        }

        public static string GetMaxAmountString(XElement element)
        {
            if (element == null) { return null; }
            return element.GetAttributeString("maxamount", null) ?? element.GetAttributeString("maxavailable", null);
        }
        
        public static bool GetSold(XElement element, bool defaultValue = true) => 
            element?.GetAttributeBool("sold", defaultValue) ?? defaultValue;

        public static int GetMinLevelDifficulty(XElement element, int defaultValue = 0) => 
            element?.GetAttributeInt("minleveldifficulty", defaultValue) ?? defaultValue;

        public static string GetStoreIdentifier(XElement element, string defaultValue = "unknown") => 
            element?.GetAttributeString("storeidentifier", defaultValue) ?? defaultValue;
    }
}
