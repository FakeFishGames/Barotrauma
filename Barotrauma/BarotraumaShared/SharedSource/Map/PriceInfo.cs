using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    class PriceInfo
    {
        public int Price { get; }
        public bool CanBeBought { get; }
        //minimum number of items available at a given store
        public int MinAvailableAmount { get; }
        //maximum number of items available at a given store
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

        /// <summary>
        /// Used when both <see cref="MinAvailableAmount"/> and <see cref="MaxAvailableAmount"/> are set to 0.
        /// </summary>
        public const int DefaultAmount = 5;

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
            int minAmount = GetMinAmount(element);
            MinAvailableAmount = Math.Min(minAmount, CargoManager.MaxQuantity);
            int maxAmount = GetMaxAmount(element);
            maxAmount = Math.Min(maxAmount, CargoManager.MaxQuantity);
            MaxAvailableAmount = Math.Max(maxAmount, MinAvailableAmount);
        }

        public PriceInfo(int price, bool canBeBought,
            int minAmount = 0, int maxAmount = 0, bool canBeSpecial = true, int minLevelDifficulty = 0, float buyingPriceMultiplier = 1f,
            bool displayNonEmpty = false, string storeIdentifier = null)
        {
            Price = price;
            CanBeBought = canBeBought;
            MinAvailableAmount = Math.Min(minAmount, CargoManager.MaxQuantity);
            BuyingPriceMultiplier = buyingPriceMultiplier;
            maxAmount = Math.Min(maxAmount, CargoManager.MaxQuantity);
            MaxAvailableAmount = Math.Max(maxAmount, minAmount);
            MinLevelDifficulty = minLevelDifficulty;
            CanBeSpecial = canBeSpecial;
            DisplayNonEmpty = displayNonEmpty;
            StoreIdentifier = new Identifier(storeIdentifier);
        }

        public static List<PriceInfo> CreatePriceInfos(XElement element, out PriceInfo defaultPrice)
        {
            var priceInfos = new List<PriceInfo>();
            defaultPrice = null;
            int basePrice = element.GetAttributeInt("baseprice", 0);
            int minAmount = GetMinAmount(element);
            int maxAmount = GetMaxAmount(element);
            int minLevelDifficulty = element.GetAttributeInt("minleveldifficulty", 0);
            bool canBeSpecial = element.GetAttributeBool("canbespecial", true);
            float buyingPriceMultiplier = element.GetAttributeFloat("buyingpricemultiplier", 1f);
            bool displayNonEmpty = element.GetAttributeBool("displaynonempty", false);
            bool soldByDefault = element.GetAttributeBool("sold", element.GetAttributeBool("soldbydefault", true));
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
                string[] storeIdentifiers = childElement.GetAttributeStringArray("storeidentifiers", new string[1] { backwardsCompatibleIdentifier });
                foreach (string id in storeIdentifiers)
                {
                    if (string.IsNullOrEmpty(id)) { continue; }
                    // TODO: Add some error messages if we have defined the min or max amount while the item is not sold
                    var priceInfo = new PriceInfo((int)(priceMultiplier * basePrice),
                        sold,
                        sold ? GetMinAmount(childElement, minAmount) : 0,
                        sold ? GetMaxAmount(childElement, maxAmount) : 0,
                        canBeSpecial,
                        storeMinLevelDifficulty,
                        storeBuyingMultiplier,
                        displayNonEmpty,
                        id);
                    priceInfos.Add(priceInfo);
                }
            }
            bool soldElsewhere = soldByDefault && element.GetAttributeBool("soldelsewhere", element.GetAttributeBool("soldeverywhere", false));
            defaultPrice = new PriceInfo(basePrice,
                soldElsewhere,
                soldElsewhere ? minAmount : 0,
                soldElsewhere ? maxAmount : 0,
                canBeSpecial,
                minLevelDifficulty,
                buyingPriceMultiplier,
                displayNonEmpty);
            return priceInfos;
        }

        private static int GetMinAmount(XElement element, int defaultValue = 0) => element != null ?
            element.GetAttributeInt("minamount", element.GetAttributeInt("minavailable", defaultValue)) :
            defaultValue;

        private static int GetMaxAmount(XElement element, int defaultValue = 0) => element != null ?
            element.GetAttributeInt("maxamount", element.GetAttributeInt("maxavailable", defaultValue)) :
            defaultValue;
    }
}
