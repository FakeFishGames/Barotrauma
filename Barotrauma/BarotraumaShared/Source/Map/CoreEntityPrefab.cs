using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma
{
    class CoreEntityPrefab : MapEntityPrefab
    {
        public static readonly PrefabCollection<CoreEntityPrefab> Prefabs = new PrefabCollection<CoreEntityPrefab>();

        private bool disposed = false;
        public override void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            Prefabs.Remove(this);
        }
    }
}
