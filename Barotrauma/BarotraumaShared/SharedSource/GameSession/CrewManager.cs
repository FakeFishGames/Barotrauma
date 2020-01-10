using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    partial class CrewManager
    {
        const float ConversationIntervalMin = 100.0f;
        const float ConversationIntervalMax = 180.0f;
        private float conversationTimer, conversationLineTimer;
        private List<Pair<Character, string>> pendingConversationLines = new List<Pair<Character, string>>();

        //orders that have not been issued to a specific character
        private List<Pair<Order, float>> activeOrders = new List<Pair<Order, float>>();
        public List<Pair<Order, float>> ActiveOrders
        {
            get { return activeOrders; }
        }
        
        private bool isSinglePlayer;
        public bool IsSinglePlayer
        {
            get { return isSinglePlayer; }
        }

        public CrewManager(bool isSinglePlayer)
        {
            this.isSinglePlayer = isSinglePlayer;
            conversationTimer = 5.0f;

            InitProjectSpecific();
        }

        partial void InitProjectSpecific();

        public bool AddOrder(Order order, float fadeOutTime)
        {
            if (order.TargetEntity == null)
            {
                DebugConsole.ThrowError("Attempted to add an order with no target entity to CrewManager!\n" + Environment.StackTrace);
                return false;
            }

            Pair<Order, float> existingOrder = activeOrders.Find(o => o.First.Prefab == order.Prefab && o.First.TargetEntity == order.TargetEntity);
            if (existingOrder != null)
            {
                existingOrder.Second = fadeOutTime;
                return false;
            }
            else
            {
                activeOrders.Add(new Pair<Order, float>(order, fadeOutTime));
                return true;
            }
        }

        public void RemoveOrder(Order order)
        {
            activeOrders.RemoveAll(o => o.First == order);
        }

        public void Update(float deltaTime)
        {
            foreach (Pair<Order, float> order in activeOrders)
            {
                order.Second -= deltaTime;
            }
            activeOrders.RemoveAll(o => o.Second <= 0.0f);

            UpdateConversations(deltaTime);
            UpdateProjectSpecific(deltaTime);
        }

        #region Dialog

        public void AddConversation(List<Pair<Character, string>> conversationLines)
        {
            if (conversationLines == null || conversationLines.Count == 0) { return; }
            pendingConversationLines.AddRange(conversationLines);
        }

        partial void CreateRandomConversation();

        private void UpdateConversations(float deltaTime)
        {
            conversationTimer -= deltaTime;
            if (conversationTimer <= 0.0f)
            {
                CreateRandomConversation();
                conversationTimer = Rand.Range(ConversationIntervalMin, ConversationIntervalMax);
            }

            if (pendingConversationLines.Count > 0)
            {
                conversationLineTimer -= deltaTime;
                if (conversationLineTimer <= 0.0f)
                {
                    //speaker of the next line can't speak, interrupt the conversation
                    if (pendingConversationLines[0].First.SpeechImpediment >= 100.0f)
                    {
                        pendingConversationLines.Clear();
                        return;
                    }

                    pendingConversationLines[0].First.Speak(pendingConversationLines[0].Second, null);
                    if (pendingConversationLines.Count > 1)
                    {
                        conversationLineTimer = MathHelper.Clamp(pendingConversationLines[0].Second.Length * 0.1f, 1.0f, 5.0f);
                    }
                    pendingConversationLines.RemoveAt(0);
                }
            }
        }

#endregion

        partial void UpdateProjectSpecific(float deltaTime);
    }
}
