using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using System.Linq;

namespace Barotrauma
{
    class Order
    {
        public static Dictionary<string, Order> Prefabs { get; private set; }
        public static List<Order> PrefabList { get; private set; }
        public static Order GetPrefab(string identifier)
        {
            if (!Prefabs.TryGetValue(identifier, out Order order))
            {
                DebugConsole.ThrowError($"Cannot find an order with the identifier '{identifier}'!");
            }
            return order;
        }
        
        public Order Prefab
        {
            get;
            private set;
        }

        public readonly string Name;

        public readonly Sprite SymbolSprite;

        public readonly Type ItemComponentType;
        public readonly string[] ItemIdentifiers;

        public readonly string Identifier;

        public readonly Color Color;

        //if true, the order is issued to all available characters
        public bool TargetAllCharacters;

        public readonly float FadeOutTime;

        public Entity TargetEntity; 
        public ItemComponent TargetItemComponent;
        public readonly bool UseController;
        public Controller ConnectedController;

        public Character OrderGiver;
        
        //legacy support
        public readonly string[] AppropriateJobs;
        public readonly string[] Options;
        public readonly string[] OptionNames;

        static Order()
        {
            Prefabs = new Dictionary<string, Order>();

            foreach (string file in GameMain.Instance.GetFilesOfType(ContentType.Orders))
            {
                XDocument doc = XMLExtensions.TryLoadXml(file);
                if (doc == null) { continue; }
                var mainElement = doc.Root;
                bool allowOverriding = false;
                if (doc.Root.IsOverride())
                {
                    mainElement = doc.Root.FirstElement();
                    allowOverriding = true;
                }
                foreach (XElement sourceElement in mainElement.Elements())
                {
                    var orderElement = sourceElement.IsOverride() ? sourceElement.FirstElement() : sourceElement;
                    string name = orderElement.Name.ToString();
                    if (name.Equals("order", StringComparison.OrdinalIgnoreCase))
                    {
                        string identifier = orderElement.GetAttributeString("identifier", null);
                        if (string.IsNullOrWhiteSpace(identifier))
                        {
                            DebugConsole.ThrowError($"Error in file {file}: The order element '{name}' does not have an identifier! All orders must have a unique identifier.");
                            continue;
                        }
                        if (Prefabs.TryGetValue(identifier, out Order duplicate))
                        {
                            if (allowOverriding || sourceElement.IsOverride())
                            {
                                DebugConsole.NewMessage($"Overriding an existing order '{identifier}' with another one defined in '{file}'", Color.Yellow);
                                Prefabs.Remove(identifier);
                            }
                            else
                            {
                                DebugConsole.ThrowError($"Error in file {file}: Duplicate element with the idenfitier '{identifier}' found in '{file}'! All orders must have a unique identifier. Use <override></override> tags to override an order with the same identifier.");
                                continue;
                            }
                        }
                        var newOrder = new Order(orderElement);
                        newOrder.Prefab = newOrder;
                        Prefabs.Add(identifier, newOrder);
                    }
                }
            }
            PrefabList = new List<Order>(Prefabs.Values);
        }

        private Order(XElement orderElement)
        {
            Identifier = orderElement.GetAttributeString("identifier", "");
            Name = TextManager.Get("OrderName." + Identifier, true) ?? "Name not found";

            string targetItemType = orderElement.GetAttributeString("targetitemtype", "");
            if (!string.IsNullOrWhiteSpace(targetItemType))
            {
                try
                {
                    ItemComponentType = Type.GetType("Barotrauma.Items.Components." + targetItemType, true, true);
                }

                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error in the order definitions: item component type " + targetItemType + " not found", e);
                }
            }

            ItemIdentifiers = orderElement.GetAttributeStringArray("targetitemidentifiers", new string[0], trim: true, convertToLowerInvariant: true);
            Color = orderElement.GetAttributeColor("color", Color.White);
            FadeOutTime = orderElement.GetAttributeFloat("fadeouttime", 0.0f);
            UseController = orderElement.GetAttributeBool("usecontroller", false);
            TargetAllCharacters = orderElement.GetAttributeBool("targetallcharacters", false);
            AppropriateJobs = orderElement.GetAttributeStringArray("appropriatejobs", new string[0]);
            Options = orderElement.GetAttributeStringArray("options", new string[0]);

            string translatedOptionNames = TextManager.Get("OrderOptions." + Identifier, true);
            if (translatedOptionNames == null)
            {
                OptionNames = orderElement.GetAttributeStringArray("optionnames", new string[0]);
            }
            else
            {
                string[] splitOptionNames = translatedOptionNames.Split(',', '，');
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
                        SymbolSprite = new Sprite(subElement, lazyLoad: true);
                        break;
                }
            }
        }
        
        public Order(Order prefab, Entity targetEntity, ItemComponent targetItem, Character orderGiver = null)
        {
            Prefab = prefab;

            Name                = prefab.Name;
            Identifier          = prefab.Identifier;
            ItemComponentType   = prefab.ItemComponentType;
            Options             = prefab.Options;
            SymbolSprite        = prefab.SymbolSprite;
            Color               = prefab.Color;
            UseController       = prefab.UseController;
            TargetAllCharacters = prefab.TargetAllCharacters;
            AppropriateJobs     = prefab.AppropriateJobs;
            FadeOutTime         = prefab.FadeOutTime;
            OrderGiver          = orderGiver;

            TargetEntity = targetEntity;
            if (targetItem != null)
            {
                if (UseController)
                {
                    //try finding the controller with the simpler non-recursive method first
                    ConnectedController = 
                        targetItem.Item.GetConnectedComponents<Controller>().FirstOrDefault() ?? 
                        targetItem.Item.GetConnectedComponents<Controller>(recursive: true).FirstOrDefault();
                }
                TargetEntity = targetItem.Item;
                TargetItemComponent = targetItem;
            }
        }
        
        public bool HasAppropriateJob(Character character)
        {
            if (character.Info == null || character.Info.Job == null) { return false; }
            if (character.Info.Job.Prefab.AppropriateOrders.Any(appropriateOrderId => Identifier == appropriateOrderId)) { return true; }

            if (!JobPrefab.List.Values.Any(jp => jp.AppropriateOrders.Contains(Identifier)) &&
                (AppropriateJobs == null || AppropriateJobs.Length == 0))
            {
                return true;
            }
            for (int i = 0; i < AppropriateJobs.Length; i++)
            {
                if (character.Info.Job.Prefab.Identifier.ToLowerInvariant() == AppropriateJobs[i].ToLowerInvariant()) return true;
            }
            return false;
        }

        public string GetChatMessage(string targetCharacterName, string targetRoomName, bool givingOrderToSelf, string orderOption = "")
        {
            orderOption = orderOption ?? "";

            string messageTag = (givingOrderToSelf && !TargetAllCharacters ? "OrderDialogSelf." : "OrderDialog.") + Identifier;
            if (!string.IsNullOrEmpty(orderOption)) messageTag += "." + orderOption;

            if (targetCharacterName == null) targetCharacterName = "";
            if (targetRoomName == null) targetRoomName = "";
            string msg = TextManager.GetWithVariables(messageTag, new string[2] { "[name]", "[roomname]" }, new string[2] { targetCharacterName, targetRoomName }, new bool[2] { false, true }, true);
            if (msg == null) return "";

            return msg;
        }
    }

}
