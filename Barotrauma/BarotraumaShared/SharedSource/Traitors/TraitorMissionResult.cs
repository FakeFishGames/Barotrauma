using System.Collections.Generic;

namespace Barotrauma
{
    partial class TraitorMissionResult
    {
        public readonly string EndMessage;

        public readonly Identifier MissionIdentifier;

        public readonly bool Success;

        public readonly List<Character> Characters = new List<Character>();
    }
}
