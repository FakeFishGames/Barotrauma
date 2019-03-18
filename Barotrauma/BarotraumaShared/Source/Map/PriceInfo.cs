using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class PriceInfo
    {
        public readonly int BuyPrice;

        //minimum number of items available at a given store
        public readonly int MinAvailableAmount;
        //maximum number of items available at a given store
        public readonly int MaxAvailableAmount;

        public PriceInfo (XElement element)
        {
            BuyPrice = element.GetAttributeInt("buyprice", 0);
            MinAvailableAmount = element.GetAttributeInt("minamount", 0);
            MaxAvailableAmount = element.GetAttributeInt("maxamount", 0);
        }
    }
}
