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

        public Order Prefab
        {
            get;
            private set;
        }

        public readonly string Name;
        public readonly string DoingText;

        public readonly Sprite SymbolSprite;

        public readonly Type ItemComponentType;
        public readonly string ItemName;

        public readonly Color Color;

        public readonly bool UseController;

        public ItemComponent TargetItem;
        
        //key = the given option ("" if no option is given)
        //value = a list of chatmessages sent when the order is issued
        private readonly Dictionary<string, List<string>> chatMessages = new Dictionary<string, List<string>>();

        public readonly string[] AppropriateJobs;
        public readonly string[] Options;

        static Order()
        {
            PrefabList = new List<Order>();

            XDocument doc = XMLExtensions.TryLoadXml(ConfigFile);
            if (doc == null || doc.Root == null) return;

            foreach (XElement orderElement in doc.Root.Elements())
            {
                if (orderElement.Name.ToString().ToLowerInvariant() != "order") continue;
                var newOrder = new Order(orderElement);
                newOrder.Prefab = newOrder;
                PrefabList.Add(newOrder);
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

            string appropriateJobsStr = orderElement.GetAttributeString("appropriatejobs", "");
            if (!string.IsNullOrWhiteSpace(appropriateJobsStr))
            {
                AppropriateJobs = appropriateJobsStr.Split(',');
                for (int i = 0; i<AppropriateJobs.Length; i++)
                {
                    AppropriateJobs[i] = AppropriateJobs[i].Trim();
                }
            }
            
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
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        SymbolSprite = new Sprite(subElement);
                        break;
                    case "chatmessage":
                        string option = subElement.GetAttributeString("option", "");
                        
                        if (!chatMessages.ContainsKey(option))
                        {
                            chatMessages[option] = new List<string>();
                        }

                        chatMessages[option].Add(subElement.GetAttributeString("msg", ""));
                        break;
                }
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
            Prefab = prefab;

            Name                = prefab.Name;
            DoingText           = prefab.DoingText;
            ItemComponentType   = prefab.ItemComponentType;
            Options             = prefab.Options;
            SymbolSprite        = prefab.SymbolSprite;
            Color               = prefab.Color;
            UseController       = prefab.UseController;
            AppropriateJobs     = prefab.AppropriateJobs;
            chatMessages        = prefab.chatMessages;

            TargetItem = targetItem;
        }

        private Order(string name, string doingText, string[] parameters = null)
            :this (name,doingText, null, parameters)
        {
        }

        public bool HasAppropriateJob(Character character)
        {
            if (AppropriateJobs == null || AppropriateJobs.Length == 0) return true;
            if (character.Info == null || character.Info.Job == null) return false;
            for (int i = 0; i < AppropriateJobs.Length; i++)
            {
                if (character.Info.Job.Name.ToLowerInvariant() == AppropriateJobs[i].ToLowerInvariant()) return true;
            }
            return false;
        }

        public string GetChatMessage(string targetCharacterName, string orderOption = "")
        {
            if (!chatMessages.ContainsKey(orderOption))
            {
                return "";
            }
            string message = chatMessages[orderOption].Count > 0 ? chatMessages[orderOption][Rand.Range(0, chatMessages[orderOption].Count)] : "";
            if (targetCharacterName == null) targetCharacterName = "";
            return message.Replace("[name]", targetCharacterName);
        }
    }

}
