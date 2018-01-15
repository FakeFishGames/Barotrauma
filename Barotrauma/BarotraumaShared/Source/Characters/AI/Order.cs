using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace Barotrauma
{
    class Order
    {
        private static string ConfigFile = Path.Combine("Content", "Orders.xml");

        public static List<Order> PrefabList;

        public readonly string Name;
        public readonly string DoingText;

        public readonly Sprite SymbolSprite;

        public readonly Type ItemComponentType;
        public readonly string ItemName;

        public readonly Color Color;

        public readonly bool UseController;

        public ItemComponent TargetItem;

        public readonly string[] Options;

        static Order()
        {
            PrefabList = new List<Order>();

            XDocument doc = XMLExtensions.TryLoadXml(ConfigFile);
            if (doc == null || doc.Root == null) return;

            foreach (XElement orderElement in doc.Root.Elements())
            {
                if (orderElement.Name.ToString().ToLowerInvariant() != "order") continue;

                PrefabList.Add(new Order(orderElement));
            }
        }

        private Order(XElement orderElement)
        {
            Name = orderElement.GetAttributeString("name", "Name not found");
            DoingText = orderElement.GetAttributeString("doingtext", "");

            string targetItemName = orderElement.GetAttributeString("targetitemtype", "");

            if (!string.IsNullOrWhiteSpace(targetItemName))
            {
                try
                {
                    ItemComponentType = Type.GetType("Barotrauma.Items.Components." + targetItemName, true, true);
                }

                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error in " + ConfigFile + ", item component type " + targetItemName + " not found", e);
                }
            }

            ItemName = orderElement.GetAttributeString("targetitemname", "");

            Color = new Color(orderElement.GetAttributeVector4("color", new Vector4(1.0f, 1.0f, 1.0f, 1.0f)));

            UseController = orderElement.GetAttributeBool("usecontroller", false);

            string optionStr = orderElement.GetAttributeString("options", "");

            if (string.IsNullOrWhiteSpace(optionStr))
            {
                Options = new string[0];
            }
            else
            {
                Options = optionStr.Split(',');

                for (int i = 0; i<Options.Length; i++)
                {
                    Options[i] = Options[i].Trim();
                }
            }



            foreach (XElement subElement in orderElement.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "sprite") continue;
                SymbolSprite = new Sprite(subElement);
                break;
            }
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
            Name                = prefab.Name;
            DoingText           = prefab.DoingText;
            ItemComponentType   = prefab.ItemComponentType;
            Options             = prefab.Options;
            SymbolSprite        = prefab.SymbolSprite;
            Color               = prefab.Color;
            UseController       = prefab.UseController;

            TargetItem = targetItem;
        }

        private Order(string name, string doingText, string[] parameters = null)
            :this (name,doingText, null, parameters)
        {
        }
    }

}
