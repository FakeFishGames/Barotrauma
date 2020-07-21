using System;
using System.Collections.Generic;
using System.Linq;
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
        /// Support for the old style of determining item prices
        /// when there were individual Price elements for each location type
        /// where the item was for sale.
        /// </summary>
        public PriceInfo (XElement element)
        {
            Price = element.GetAttributeInt("buyprice", 0);
            CanBeBought = true;
            MinAvailableAmount = GetMinAmount(element);
            MaxAvailableAmount = GetMaxAmount(element);
        }

        public PriceInfo(int price, bool canBeBought, int minAmount = 0, int maxAmount = 0)
        {
            Price = price;
            CanBeBought = canBeBought;
            MinAvailableAmount = minAmount;
            MaxAvailableAmount = maxAmount;
        }

        public static List<Tuple<string, PriceInfo>> CreatePriceInfos(XElement element, out PriceInfo defaultPrice)
        {
            defaultPrice = null;
            var basePrice = element.GetAttributeInt("baseprice", 0);
            var soldByDefault = element.GetAttributeBool("soldbydefault", true);
            var minAmount = GetMinAmount(element);
            var maxAmount = GetMaxAmount(element);
            var priceInfos = new List<Tuple<string, PriceInfo>>();

            foreach (XElement childElement in element.GetChildElements("price"))
            {
                var priceMultiplier = childElement.GetAttributeFloat("multiplier", 1.0f);
                var sold = childElement.GetAttributeBool("sold", soldByDefault);
                priceInfos.Add(new Tuple<string, PriceInfo>(childElement.GetAttributeString("locationtype", "").ToLowerInvariant(),
                    new PriceInfo(price: (int)(priceMultiplier * basePrice), canBeBought: sold,
                        minAmount: sold ? GetMinAmount(childElement, minAmount) : 0,
                        maxAmount: sold ? GetMaxAmount(childElement, maxAmount) : 0)));
            }

            var canBeBoughtAtOtherLocations = soldByDefault && element.GetAttributeBool("soldeverywhere", true);
            defaultPrice = new PriceInfo(basePrice, canBeBoughtAtOtherLocations,
                minAmount: canBeBoughtAtOtherLocations ? minAmount : 0,
                maxAmount: canBeBoughtAtOtherLocations ? maxAmount : 0);
            
            return priceInfos;
        }

        private static int GetMinAmount(XElement element, int defaultValue = 0) => element != null ?
            element.GetAttributeInt("minamount", element.GetAttributeInt("minavailable", defaultValue)) : defaultValue;

        private static int GetMaxAmount(XElement element, int defaultValue = 0) => element != null ?
            element.GetAttributeInt("maxamount", element.GetAttributeInt("maxavailable", defaultValue)) : defaultValue;
    }
}
