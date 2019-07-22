using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class Traitor
    {
        public class GoalDetonateLocations : Goal
        {
            public class Location { } // TODO(xxx): Best way to identify target locations?
            public readonly List<Location> Locations = new List<Location>();

            public override bool IsCompleted => throw new System.NotImplementedException();
        }
    }
}
