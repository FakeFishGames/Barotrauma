using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Optimizable : ItemComponent
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
            return currentlyOptimizable.Contains(this) && DegreeOfSuccess(character) > 0.5f;
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
                if (optimizedTimer <= 0.0f) isOptimized = false;
            }
            else if (currentlyOptimizable.Contains(this))
            {
                if (currentOptimizer == null || currentOptimizer.SelectedConstruction != item)
                {
                    //this item is now optimizable until the timer runs out
                    optimizableTimer -= deltaTime;
                    if (optimizableTimer <= 0.0f)
                    {
                        optimizationProgress = 0.0f;
                        currentlyOptimizable.Remove(this);
                        cooldownTimer = OptimizationCoolDown;
                        isOptimized = false;
                    }
                }
                else
                {
                    float degreeOfSuccess = DegreeOfSuccess(currentOptimizer);
                    optimizationProgress = MathHelper.Clamp(optimizationProgress + degreeOfSuccess / 10.0f * deltaTime, 0.0f, 1.0f);
                    if (optimizationProgress >= 1.0f)
                    {
                        Optimize(currentOptimizer);
                        currentOptimizer.SelectedConstruction = null;
                        currentOptimizer = null;
                    }
                }
            }
            else if (currentlyOptimizable.Any(o => o.item.Submarine == item.Submarine))
            {
                //something already optimizable in the same submarine
                return;
            }
            else
            {
                //nothing optimizable in this sub, activate the first item whose cooldown timer runs out
                cooldownTimer -= deltaTime;
                if (cooldownTimer <= 0.0f)
                {
                    currentlyOptimizable.Add(this);
                    optimizableTimer = Rand.Range(30.0f, 60.0f);
                    optimizationProgress = 0.0f;
                    cooldownTimer = OptimizationCoolDown;
                    currentOptimizer = null;
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
    }
}
