using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    class PriceInfo
    {
        public readonly int Price;
        public readonly bool CanBeBought;
        //minimum number of items available at a given store
        public readonly int MinAvailableAmount;
        //maximum number of items available at a given store
        public readonly int MaxAvailableAmount;
        /// <summary>
        /// Used when both <see cref="MinAvailableAmount"/> and <see cref="MaxAvailableAmount"/> are set to 0.
        /// </summary>
        public const int DefaultAmount = 5;
        /// <summary>
        /// Can the item be a Daily Special or a Requested Good
        /// </summary>
        public readonly bool CanBeSpecial;
        /// <summary>
        /// The item isn't available in stores unless the level's difficulty is above this value
        /// </summary>
        public readonly int MinLevelDifficulty;
        /// <summary>
        /// The cost of item when sold by the store. Higher modifier means the item costs more to buy from the store.
        /// </summary>
        public readonly float BuyingPriceMultiplier = 1f;
        public bool DisplayNonEmpty { get; } = false;


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
            var minAmount = GetMinAmount(element);
            MinAvailableAmount = Math.Min(minAmount, CargoManager.MaxQuantity);
            var maxAmount = GetMaxAmount(element);
            maxAmount = Math.Min(maxAmount, CargoManager.MaxQuantity);
            MaxAvailableAmount = Math.Max(maxAmount, MinAvailableAmount);
        }

        public PriceInfo(int price, bool canBeBought, int minAmount = 0, int maxAmount = 0, bool canBeSpecial = true, int minLevelDifficulty = 0, float buyingPriceMultiplier = 1f, bool displayNonEmpty = false)
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
        }

        public static List<Tuple<Identifier, PriceInfo>> CreatePriceInfos(XElement element, out PriceInfo defaultPrice)
        {
            defaultPrice = null;
            int basePrice = element.GetAttributeInt("baseprice", 0);
            bool soldByDefault = element.GetAttributeBool("soldbydefault", true);
            int minAmount = GetMinAmount(element);
            int maxAmount = GetMaxAmount(element);
            int minLevelDifficulty = element.GetAttributeInt("minleveldifficulty", 0);
            bool canBeSpecial = element.GetAttributeBool("canbespecial", true);
            float buyingPriceMultiplier = element.GetAttributeFloat("buyingpricemultiplier", 1f);
            bool displayNonEmpty = element.GetAttributeBool("displaynonempty", false);
            var priceInfos = new List<Tuple<Identifier, PriceInfo>>();

            foreach (XElement childElement in element.GetChildElements("price"))
            {
                float priceMultiplier = childElement.GetAttributeFloat("multiplier", 1.0f);
                bool sold = childElement.GetAttributeBool("sold", soldByDefault);
                priceInfos.Add(new Tuple<Identifier, PriceInfo>(childElement.GetAttributeIdentifier("locationtype", ""),
                    new PriceInfo((int)(priceMultiplier * basePrice), sold,
                        sold ? GetMinAmount(childElement, minAmount) : 0,
                        sold ? GetMaxAmount(childElement, maxAmount) : 0,
                        canBeSpecial,
                        childElement.GetAttributeInt("minleveldifficulty", minLevelDifficulty),
                        childElement.GetAttributeFloat("buyingpricemultiplier", buyingPriceMultiplier),
                        displayNonEmpty)));
            }

            bool canBeBoughtAtOtherLocations = soldByDefault && element.GetAttributeBool("soldeverywhere", true);
            defaultPrice = new PriceInfo(basePrice, canBeBoughtAtOtherLocations,
                canBeBoughtAtOtherLocations ? minAmount : 0,
                canBeBoughtAtOtherLocations ? maxAmount : 0,
                canBeSpecial, minLevelDifficulty, buyingPriceMultiplier, displayNonEmpty);
            
            return priceInfos;
        }

        private static int GetMinAmount(XElement element, int defaultValue = 0) => element != null ?
            element.GetAttributeInt("minamount", element.GetAttributeInt("minavailable", defaultValue)) : defaultValue;

        private static int GetMaxAmount(XElement element, int defaultValue = 0) => element != null ?
            element.GetAttributeInt("maxamount", element.GetAttributeInt("maxavailable", defaultValue)) : defaultValue;
    }
}
