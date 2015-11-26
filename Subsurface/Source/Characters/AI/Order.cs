using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class Order
    {
        public static List<Order> List;

        public readonly string Name;
        public readonly string DoingText;

        Sprite buttonSprite;

        public readonly string[] Options;

        static Order()
        {
            List = new List<Order>();

            new Order("Follow", "Following");
            new Order("Operate Reactor", "Operating reactor", new string[] {"Power Up", "Shutdown"});

            new Order("Dismiss", "Dismissed");
        }

        private Order(string name, string doingText, string[] parameters = null)
        {
            this.Name = name;
            this.DoingText = doingText;
            this.Options = parameters == null ? new string[0] : parameters;

            List.Add(this);
        }
    }

}
