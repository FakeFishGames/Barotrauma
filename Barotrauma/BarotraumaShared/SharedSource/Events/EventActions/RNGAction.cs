namespace Barotrauma
{
    /// <summary>
    /// Randomly executes either of the child actions (Success or Failure).
    /// </summary>
    class RNGAction : BinaryOptionAction
    {
        [Serialize(0.0f, IsPropertySaveable.Yes, description: "The probability of executing the Success actions. A value between 0-1.")]
        public float Chance { get; set; }

        public RNGAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
        { 
            if (Chance >= 1.0f)
            {
                DebugConsole.ThrowError($"Incorrectly configured RNG Action in event \"{parentEvent.Prefab.Identifier}\". Probability is 1.0 (100%) or more, the action will always succeed.",
                    contentPackage: element.ContentPackage);
            }
            else if (Chance <= 0.0f)
            {
                DebugConsole.ThrowError($"Incorrectly configured RNG Action in event \"{parentEvent.Prefab.Identifier}\". Probability is 0 or less, the action will never succeed.",
                    contentPackage: element.ContentPackage);
            }
        }

        private bool isFinished;

        protected override bool? DetermineSuccess()
        {
            isFinished = true;
            return Rand.Range(0.0, 1.0) <= Chance;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(RNGAction)} -> (Chance: {Chance.ColorizeObject()}, "+
                   $"Succeeded: {succeeded.ColorizeObject()})";
        }
    }
}