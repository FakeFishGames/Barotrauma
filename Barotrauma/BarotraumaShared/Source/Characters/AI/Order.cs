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
        public readonly string[] ItemIdentifiers;

        public readonly string AITag;

        public readonly Color Color;

        //if true, the order is issued to all available characters
        public bool TargetAllCharacters;

        public readonly float FadeOutTime;

        public Entity TargetEntity; 
        public ItemComponent TargetItemComponent;
        public readonly bool UseController;
        public Controller ConnectedController; 
        
        public readonly string[] AppropriateJobs;
        public readonly string[] Options;
        public readonly string[] OptionNames;

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
            AITag = orderElement.GetAttributeString("aitag", "");
            Name = TextManager.Get("OrderName." + AITag, true) ?? orderElement.GetAttributeString("name", "Name not found");
            DoingText = TextManager.Get("OrderNameDoing." + AITag, true) ?? orderElement.GetAttributeString("doingtext", "");

            string targetItemType = orderElement.GetAttributeString("targetitemtype", "");
            if (!string.IsNullOrWhiteSpace(targetItemType))
            {
                try
                {
                    ItemComponentType = Type.GetType("Barotrauma.Items.Components." + targetItemType, true, true);
                }

                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error in " + ConfigFile + ", item component type " + targetItemType + " not found", e);
                }
            }

            ItemIdentifiers = orderElement.GetAttributeStringArray("targetitemidentifiers", new string[0], trim: true, convertToLowerInvariant: true);
            Color = orderElement.GetAttributeColor("color", Color.White);
            FadeOutTime = orderElement.GetAttributeFloat("fadeouttime", 0.0f);
            UseController = orderElement.GetAttributeBool("usecontroller", false);
            TargetAllCharacters = orderElement.GetAttributeBool("targetallcharacters", false);
            AppropriateJobs = orderElement.GetAttributeStringArray("appropriatejobs", new string[0]);
            Options = orderElement.GetAttributeStringArray("options", new string[0]);

            string translatedOptionNames = TextManager.Get("OrderOptions." + AITag, true);
            if (translatedOptionNames == null)
            {
                OptionNames = orderElement.GetAttributeStringArray("optionnames", new string[0]);
            }
            else
            {
                string[] splitOptionNames = translatedOptionNames.Split(',');
                OptionNames = new string[Options.Length];
                for (int i = 0; i < Options.Length && i < splitOptionNames.Length; i++)
                {
                    OptionNames[i] = splitOptionNames[i].Trim();
                }
            }

            if (OptionNames.Length != Options.Length)
            {
                DebugConsole.ThrowError("Error in Order " + Name + " - the number of option names doesn't match the number of options.");
                OptionNames = Options;
            }

            foreach (XElement subElement in orderElement.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        SymbolSprite = new Sprite(subElement);
                        break;
                }
            }
        }
        
        public Order(Order prefab, Entity targetEntity, ItemComponent targetItem)
        {
            Prefab = prefab;

            Name                = prefab.Name;
            AITag               = prefab.AITag;
            DoingText           = prefab.DoingText;
            ItemComponentType   = prefab.ItemComponentType;
            Options             = prefab.Options;
            SymbolSprite        = prefab.SymbolSprite;
            Color               = prefab.Color;
            UseController       = prefab.UseController;
            TargetAllCharacters = prefab.TargetAllCharacters;
            AppropriateJobs     = prefab.AppropriateJobs;
            FadeOutTime         = prefab.FadeOutTime;

            TargetEntity = targetEntity;
            if (targetItem != null)
            {
                if (UseController)
                {
                    var controllers = targetItem.Item.GetConnectedComponents<Controller>();
                    if (controllers.Count > 0) ConnectedController = controllers[0];
                }
                TargetEntity = targetItem.Item;
                TargetItemComponent = targetItem;
            }
        }
        
        public bool HasAppropriateJob(Character character)
        {
            if (AppropriateJobs == null || AppropriateJobs.Length == 0) return true;
            if (character.Info == null || character.Info.Job == null) return false;
            for (int i = 0; i < AppropriateJobs.Length; i++)
            {
                if (character.Info.Job.Prefab.Identifier.ToLowerInvariant() == AppropriateJobs[i].ToLowerInvariant()) return true;
            }
            return false;
        }

        public string GetChatMessage(string targetCharacterName, string targetRoomName, string orderOption = "")
        {
            orderOption = orderOption ?? "";

            string messageTag = "OrderDialog." + AITag;
            if (!string.IsNullOrEmpty(orderOption)) messageTag += "." + orderOption;

            string msg = TextManager.Get(messageTag, true);
            if (msg == null) return "";
            
            if (targetCharacterName == null) targetCharacterName = "";
            if (targetRoomName == null) targetRoomName = "";            
            return msg.Replace("[name]", targetCharacterName).Replace("[roomname]", targetRoomName);
        }
    }

}
