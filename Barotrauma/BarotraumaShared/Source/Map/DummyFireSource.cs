using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    partial class DummyFireSource : FireSource
    {
        private Vector2 maxSize;

        public bool Removed
        {
            get { return removed; }
        }

        public DummyFireSource(Vector2 maxSize, Vector2 worldPosition, Hull spawningHull = null, bool isNetworkMessage = false) : base(worldPosition, spawningHull, isNetworkMessage)
        {
            this.maxSize = maxSize;
        }

        public override float DamageRange
        {
            get { return 5f; }
        }

        protected override void LimitSize()
        {
            if (hull == null) return;

            position.X = Math.Max(hull.Rect.X, position.X);
            position.Y = Math.Min(hull.Rect.Y, position.Y);

            size.X = Math.Min(maxSize.X, size.X);
            size.Y = Math.Min(maxSize.Y, size.Y);
        }

        protected override void AdjustXPos(float growModifier, float deltaTime)
        {
            
        }

        protected override void ReduceOxygen(float deltaTime)
        {
            
        }
    }
}
