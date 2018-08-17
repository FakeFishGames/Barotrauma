using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Lidgren.Network;

namespace Barotrauma.Items.Components
{
    partial class Optimizable : ItemComponent, IServerSerializable, IClientSerializable
    {
        private static HashSet<Optimizable> currentlyOptimizable = new HashSet<Optimizable>();
        
        //how long will this item be optimizable
        private float optimizableTimer;

        //how long until this item can become optimizable again
        private float cooldownTimer;
        
        //how long the item will stay optimized
        private float optimizedTimer;
        private bool isOptimized;

        private Character currentOptimizer;
        private float optimizationProgress;

        [Serialize("", false)]
        public string OptimizationType
        {
            get;
            set;
        }

        [Serialize(360.0f, false)]
        public float OptimizationDuration
        {
            get;
            set;
        }

        [Serialize(360.0f, false)]
        public float OptimizationCoolDown
        {
            get;
            set;
        }

        [Serialize(60.0f, false)]
        public float OptimizableDuration
        {
            get;
            set;
        }

        public bool IsOptimized
        {
            get { return isOptimized; }
        }

        public static HashSet<Optimizable> CurrentlyOptimizable
        {
            get { return currentlyOptimizable; }
        }

        public Optimizable(Item item, XElement element) 
            : base(item, element)
        {
            cooldownTimer = Rand.Range(1.0f, 10.0f);
            IsActive = true;
            canBeSelected = true;

            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        public override bool Select(Character character)
        {
            return currentlyOptimizable.Contains(this) && DegreeOfSuccess(character) >= 0.5f;
        }

        private void Optimize(Character user)
        {
            if (user == null || !currentlyOptimizable.Contains(this)) return;

            currentlyOptimizable.Remove(this);
            float degreeOfSuccess = DegreeOfSuccess(user);
            optimizedTimer = OptimizationDuration * degreeOfSuccess;
            optimizationProgress = 0.0f;
            isOptimized = true;
        }
        
        public override void Update(float deltaTime, Camera cam)
        {
            if (isOptimized)
            {
                optimizedTimer -= deltaTime;
                if (optimizedTimer <= 0.0f && GameMain.Client == null)
                {
                    //clients don't set the item back to non-optimized until the server says so
                    isOptimized = false;
                    item.CreateServerEvent(this);
                }
            }
            else if (currentlyOptimizable.Contains(this))
            {
                if (currentOptimizer == null || currentOptimizer.SelectedConstruction != item)
                {
                    //this item is now optimizable until the timer runs out
                    optimizableTimer -= deltaTime;
                    if (optimizableTimer <= 0.0f && GameMain.Client == null)
                    {
                        optimizationProgress = 0.0f;
                        currentlyOptimizable.Remove(this);
                        cooldownTimer = OptimizationCoolDown;
                        isOptimized = false;
                        item.CreateServerEvent(this);
                    }
                }
                else
                {
                    float degreeOfSuccess = DegreeOfSuccess(currentOptimizer);
                    optimizationProgress = MathHelper.Clamp(optimizationProgress + degreeOfSuccess / 10.0f * deltaTime, 0.0f, 1.0f);
                    if (optimizationProgress >= 1.0f && GameMain.Client == null)
                    {
                        Optimize(currentOptimizer);
                        currentOptimizer.SelectedConstruction = null;
                        currentOptimizer = null;

                        item.CreateServerEvent(this);
                    }
                }
            }
            else if (currentlyOptimizable.Any(o => o.item.Submarine == item.Submarine))
            {
                //something already optimizable in the same submarine
                return;
            }
            else if (GameMain.Client == null)
            {
                //nothing optimizable in this sub, activate the first item whose cooldown timer runs out
                cooldownTimer -= deltaTime;
                if (cooldownTimer <= 0.0f)
                {
                    currentlyOptimizable.Add(this);
                    optimizableTimer = OptimizableDuration;
                    optimizationProgress = 0.0f;
                    cooldownTimer = OptimizationCoolDown;
                    currentOptimizer = null;

                    item.CreateServerEvent(this);
                }
            }
        }

        protected override void RemoveComponentSpecific()
        {
            currentlyOptimizable.Remove(this);
        }

        public override void OnMapLoaded()
        {
            currentlyOptimizable.Clear();
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.Write(IsOptimized);
            if (IsOptimized)
            {
                msg.WriteRangedSingle(MathHelper.Clamp(optimizedTimer, 0.0f, OptimizationDuration), 0.0f, OptimizationDuration, 16);
            }
            else
            {
                msg.Write(currentlyOptimizable.Contains(this));
                if (currentlyOptimizable.Contains(this))
                {
                    msg.WriteRangedSingle(MathHelper.Clamp(optimizableTimer, 0.0f, OptimizableDuration), 0.0f, OptimizableDuration, 16);
                    msg.WriteRangedSingle(MathHelper.Clamp(optimizationProgress, 0.0f, 1.0f), 0.0f, 1.0f, 8);
                }
            }
        }
        
        public void ServerRead(ClientNetObject type, NetBuffer msg, Client c)
        {
            bool isOptimizing = msg.ReadBoolean();

            if (!item.CanClientAccess(c)) return;
            if (DegreeOfSuccess(c.Character) < 0.5f) return;

            if (isOptimizing)
            {
                if (currentlyOptimizable.Contains(this)) currentOptimizer = c.Character;
            }
            else
            {
                if (currentOptimizer == c.Character) currentOptimizer = null;
            }
        }
    }
}
