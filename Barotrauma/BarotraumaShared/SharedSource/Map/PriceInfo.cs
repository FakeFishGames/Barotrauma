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
        /// Used when neither <see cref="MinAvailableAmount"/> or <see cref="MaxAvailableAmount"/> are defined.
        /// </summary>
        private const int DefaultAmount = 5;

        /// <summary>
        /// How much more the maximum stock is relative to the minimum stock if not defined. Stores will gradually stock up towards the maximum.
        /// </summary>
        private const float DefaultMaxAvailabilityRelativeToMin = 1.2f;

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
            MinLevelDifficulty = element.GetAttributeInt("minleveldifficulty", 0);
            BuyingPriceMultiplier = element.GetAttributeFloat("buyingpricemultiplier", 1f);
            CanBeBought = true;
            MinAvailableAmount = Math.Min(GetMinAmount(element, defaultValue: DefaultAmount), CargoManager.MaxQuantity);
            MaxAvailableAmount = MathHelper.Clamp(GetMaxAmount(element, defaultValue: (int)(MinAvailableAmount * DefaultMaxAvailabilityRelativeToMin)), MinAvailableAmount, CargoManager.MaxQuantity);
            RequiresUnlock = element.GetAttributeBool("requiresunlock", false);

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
            int minAmount = GetMinAmount(element, defaultValue: DefaultAmount);
            int maxAmount = GetMaxAmount(element, defaultValue: (int)(DefaultAmount * DefaultMaxAvailabilityRelativeToMin));
            int minLevelDifficulty = element.GetAttributeInt("minleveldifficulty", 0);
            bool canBeSpecial = element.GetAttributeBool("canbespecial", true);
            float buyingPriceMultiplier = element.GetAttributeFloat("buyingpricemultiplier", 1f);
            bool displayNonEmpty = element.GetAttributeBool("displaynonempty", false);
            bool soldByDefault = element.GetAttributeBool("sold", element.GetAttributeBool("soldbydefault", true));
            bool requiresUnlock = element.GetAttributeBool("requiresunlock", false);
            foreach (XElement childElement in element.GetChildElements("price"))
            {
                float priceMultiplier = childElement.GetAttributeFloat("multiplier", 1.0f);
                bool sold = childElement.GetAttributeBool("sold", soldByDefault); 
                int storeMinLevelDifficulty = childElement.GetAttributeInt("minleveldifficulty", minLevelDifficulty);
                float storeBuyingMultiplier = childElement.GetAttributeFloat("buyingpricemultiplier", buyingPriceMultiplier);
                string backwardsCompatibleIdentifier = childElement.GetAttributeString("locationtype", "");
                if (!string.IsNullOrEmpty(backwardsCompatibleIdentifier))
                {
                    backwardsCompatibleIdentifier = $"merchant{backwardsCompatibleIdentifier}";
                }
                string storeIdentifier = childElement.GetAttributeString("storeidentifier", backwardsCompatibleIdentifier);
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
                    storeIdentifier: storeIdentifier);
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
                requiresUnlock: requiresUnlock);
            defaultPrice.LoadReputationRestrictions(element);
            return priceInfos;
        }

        private static int GetMinAmount(XElement element, int defaultValue) => element != null ?
            element.GetAttributeInt("minamount", element.GetAttributeInt("minavailable", defaultValue)) :
            defaultValue;

        private static int GetMaxAmount(XElement element, int defaultValue) => element != null ?
            element.GetAttributeInt("maxamount", element.GetAttributeInt("maxavailable", defaultValue)) :
            defaultValue;
    }
}
