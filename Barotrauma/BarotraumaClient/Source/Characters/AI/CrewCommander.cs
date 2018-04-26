using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class CrewCommander
    {
        private CrewManager crewManager;
        
        //listbox for report buttons that appear at the corner of the screen 
        //when there's something to report in the hull the character is currently in
        private GUIListBox reportButtonContainer;
        
        public CrewCommander(CrewManager crewManager)
        {
            this.crewManager = crewManager;
            //PH: make space for the icon part of the report button
            Rectangle rect = HUDLayoutSettings.ReportArea;
            rect = new Rectangle(rect.X, rect.Y + 64, rect.Width, rect.Height);
            reportButtonContainer = new GUIListBox(rect, null, Alignment.TopRight, null);
            reportButtonContainer.Color = Color.Transparent;
            reportButtonContainer.Spacing = 50;
            reportButtonContainer.HideChildrenOutsideFrame = false;
        }

        private bool ReportButtonsVisible()
        {
            return CharacterHealth.OpenHealthWindow == null;
        }
        
        private GUIButton CreateOrderButton(Rectangle rect, Order order, GUIComponent parent, bool createSymbol = true)
        {
            var orderButton = new GUIButton(rect, order.Name, null, Alignment.TopCenter, Alignment.Center, "GUITextBox", parent);
            orderButton.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
            orderButton.UserData = order;
            orderButton.OnClicked = OrderButtonClicked;
            
            if (createSymbol)
            {
                var symbol = new GUIImage(new Rectangle(0,-60,64,64), order.SymbolSprite, Alignment.TopCenter, orderButton);
                symbol.Color = order.Color;

                orderButton.children.Insert(0, symbol);
                orderButton.children.RemoveAt(orderButton.children.Count-1);
            }
            
            return orderButton;
        }
        
        private WifiComponent GetHeadset(Character character, bool requireEquipped)
        {
            if (character?.Inventory == null) return null;

            var radioItem = character.Inventory.Items.FirstOrDefault(it => it != null && it.GetComponent<WifiComponent>() != null);
            if (radioItem == null) return null;
            if (requireEquipped && !character.HasEquippedItem(radioItem)) return null;
            
            return  radioItem.GetComponent<WifiComponent>();
        }
        
        public void SetOrder(Character character, Order order, Character orderGiver)
        {
            if (order.TargetAllCharacters)
            {
                if (orderGiver == null || orderGiver.CurrentHull == null) return;
                crewManager.AddOrder(new Order(order.Prefab, orderGiver.CurrentHull, null), order.Prefab.FadeOutTime);

                if (crewManager.IsSinglePlayer)
                {
                    orderGiver.Speak(
                        order.GetChatMessage("", orderGiver.CurrentHull?.RoomName), ChatMessageType.Order);
                }
                else
                {
                    OrderChatMessage msg = new OrderChatMessage(order, "", orderGiver.CurrentHull, null, orderGiver);
                    if (GameMain.Client != null)
                    {
                        GameMain.Client.SendChatMessage(msg);
                    }
                    else if (GameMain.Server != null)
                    {
                        GameMain.Server.SendOrderChatMessage(msg);
                    }
                }
                return;
            }

            character.SetOrder(order, "", orderGiver);
            if (crewManager.IsSinglePlayer)
            {
                orderGiver?.Speak(
                    order.GetChatMessage(character.Name, orderGiver.CurrentHull?.RoomName), ChatMessageType.Order);
            }
            else
            {
                OrderChatMessage msg = new OrderChatMessage(order, "", order.TargetItemComponent?.Item, character, orderGiver);
                if (GameMain.Client != null)
                {
                    GameMain.Client.SendChatMessage(msg);
                }
                else if (GameMain.Server != null)
                {
                    GameMain.Server.SendOrderChatMessage(msg);
                }
            }            
        }

        private bool OrderButtonClicked(GUIButton button, object userData)
        {
            //order targeted to all characters
            Order order = userData as Order;
            if (order.TargetAllCharacters)
            {
                if (Character.Controlled == null || Character.Controlled.CurrentHull == null) return false;
                crewManager.AddOrder(new Order(order.Prefab, Character.Controlled.CurrentHull, null), order.Prefab.FadeOutTime);
                SetOrder(null, order, "", Character.Controlled);
            }            
            return true;
        }
        
        public bool SetOrder(Character character, Order order, string option, Character orderGiver)
        {
            if (crewManager.IsSinglePlayer)
            {
                orderGiver?.Speak(
                    order.GetChatMessage(character == null ? "" : character.Name, orderGiver.CurrentHull?.RoomName, option), ChatMessageType.Order);
            }
            else
            {
                OrderChatMessage msg = new OrderChatMessage(order, option, order.TargetItemComponent?.Item, character, orderGiver);
                if (GameMain.Client != null)
                {
                    GameMain.Client.SendChatMessage(msg);
                }
                else if (GameMain.Server != null)
                {
                    GameMain.Server.SendOrderChatMessage(msg);
                }
            }
            
            character?.SetOrder(order, option, orderGiver);

            return true;
        }

        public void AddToGUIUpdateList()
        {
            if (reportButtonContainer.CountChildren > 0 && ReportButtonsVisible())
            {
                reportButtonContainer.AddToGUIUpdateList();
            }
        }

        public void Update(float deltaTime)
        {
            bool hasRadio = false;
            if (Character.Controlled?.CurrentHull != null && Character.Controlled.CanSpeak)
            {
                WifiComponent radio = GetHeadset(Character.Controlled, true);
                hasRadio = radio != null && radio.CanTransmit();
            }

            if (hasRadio)
            {
                bool hasFires = Character.Controlled.CurrentHull.FireSources.Count > 0;
                ToggleReportButton("reportfire", hasFires);

                bool hasLeaks = Character.Controlled.CurrentHull.ConnectedGaps.Any(g => !g.IsRoomToRoom && g.Open > 0.0f);
                ToggleReportButton("reportbreach", hasLeaks);

                bool hasIntruders = Character.CharacterList.Any(c => 
                    c.CurrentHull == Character.Controlled.CurrentHull && !c.IsDead &&
                    (c.AIController is EnemyAIController || c.TeamID != Character.Controlled.TeamID));

                ToggleReportButton("reportintruders", hasIntruders);

                if (reportButtonContainer.CountChildren > 0 && ReportButtonsVisible())
                {
                    reportButtonContainer.Update(deltaTime);
                }
            }
            else
            {
                reportButtonContainer.ClearChildren();
            }            
        }

        private void ToggleReportButton(string orderAiTag, bool enabled)
        {
            Order order = Order.PrefabList.Find(o => o.AITag == orderAiTag);
            var existingButton = reportButtonContainer.GetChild(order);

            //already reported, disable the button
            if (GameMain.GameSession.CrewManager.ActiveOrders.Any(o => 
                o.First.TargetEntity == Character.Controlled.CurrentHull && 
                o.First.AITag == orderAiTag))
            {
                enabled = false;
            }

            if (enabled)
            {
                if (existingButton == null)
                {
                    CreateOrderButton(new Rectangle(0,0,0,20), order, reportButtonContainer, true);
                }
            }
            else
            {
                if (existingButton != null) reportButtonContainer.RemoveChild(existingButton);
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (reportButtonContainer.CountChildren > 0 && ReportButtonsVisible())
            {
                reportButtonContainer.Draw(spriteBatch);
            }
        }
    }
}
