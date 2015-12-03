using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class Order
    {
        public static List<Order> PrefabList;

        public readonly string Name;
        public readonly string DoingText;

        //Sprite buttonSprite;

        public readonly Type ItemComponentType;

        public ItemComponent TargetItem;

        public readonly string[] Options;

        static Order()
        {
            PrefabList = new List<Order>();

            PrefabList.Add(new Order("Follow", "Following"));

            PrefabList.Add(new Order("Dismiss", "Dismissed"));

            PrefabList.Add(new Order("Wait", "Wait"));

            PrefabList.Add(new Order("Operate Reactor", "Operating reactor", typeof(Reactor), new string[] {"Power up", "Shutdown"}));
            PrefabList.Add(new Order("Operate Railgun", "Operating railgun", typeof(Turret), new string[] { "Fire at will", "Hold fire" }));


        }

        private Order(string name, string doingText, Type itemComponentType, string[] parameters = null)
        {
            Name = name;
            DoingText = doingText;
            ItemComponentType = itemComponentType;
            Options = parameters == null ? new string[0] : parameters;
        }

        public Order(Order prefab, ItemComponent targetItem)
        {
            Name = prefab.Name;
            DoingText = prefab.DoingText;
            ItemComponentType = prefab.ItemComponentType;
            Options = prefab.Options;

            TargetItem = targetItem;
        }

        private Order(string name, string doingText, string[] parameters = null)
            :this (name,doingText, null, parameters)
        {
        }
    }

}
