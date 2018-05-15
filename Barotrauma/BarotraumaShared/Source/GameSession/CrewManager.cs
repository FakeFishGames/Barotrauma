using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma
{
    partial class CrewManager
    {        
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

            UpdateProjectSpecific(deltaTime);
        }

        partial void UpdateProjectSpecific(float deltaTime);
    }
}
