using System;

namespace Barotrauma
{
    /// <summary>
    /// Adjusts the price multiplier for stores or mechanical repairs in the current location.
    /// </summary>
    class SetPriceMultiplierAction : EventAction
    {
        public enum OperationType
        {
            Set,
            Multiply,
            Min,
            Max
        }

        public enum PriceMultiplierType
        {
            Store,
            Mechanical
        }

        [Serialize(1.0f, IsPropertySaveable.Yes, description: "Value to set as the multiplier, or to multiply, min or max the current multiplier with.")]
        public float Multiplier { get; set; }

        [Serialize(OperationType.Set, IsPropertySaveable.Yes, description: "Do you want to set the value as the multiplier, multiply the existing multiplier with it, or take the smaller or larger of the values.")]
        public OperationType Operation { get; set; }

        [Serialize(PriceMultiplierType.Store, IsPropertySaveable.Yes, description: "Do you want to set the price multiplier for stores or for mechanical services (hull and item repairs and restoring lost shuttles)?")]
        public PriceMultiplierType TargetMultiplier { get; set; }

        public SetPriceMultiplierAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        private bool isFinished = false;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset()
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }
            if (GameMain.GameSession?.GameMode is CampaignMode campaign && campaign.Map?.CurrentLocation != null)
            {
                float newMultiplier = GetCurrentMultiplier(campaign.Map.CurrentLocation);

                switch (Operation)
                {
                    case OperationType.Set:
                        newMultiplier = Multiplier;
                        break;
                    case OperationType.Multiply:
                        newMultiplier *= Multiplier;
                        break;
                    case OperationType.Min:
                        newMultiplier = Math.Min(Multiplier, campaign.Map.CurrentLocation.PriceMultiplier);
                        break;
                    case OperationType.Max:
                        newMultiplier = Math.Max(Multiplier, campaign.Map.CurrentLocation.PriceMultiplier);
                        break;
                    default:
                        throw new NotImplementedException();
                }

                SetCurrentMultiplier(campaign.Map.CurrentLocation, newMultiplier);
            }
            isFinished = true;
        }

        private float GetCurrentMultiplier(Location location)
        {
            return TargetMultiplier switch
            {
                PriceMultiplierType.Store => location.PriceMultiplier,
                PriceMultiplierType.Mechanical => location.MechanicalPriceMultiplier,
                _ => throw new NotImplementedException()
            };
        }

        private void SetCurrentMultiplier(Location location, float value)
        {
            switch (TargetMultiplier)
            {
                case PriceMultiplierType.Store:
                    location.PriceMultiplier = value;
                    break;
                case PriceMultiplierType.Mechanical:
                    location.MechanicalPriceMultiplier = value;
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(SetPriceMultiplierAction)} -> (Multiplier: {Multiplier.ColorizeObject()}, " +
                   $"Operation: {Operation.ColorizeObject()}, Target: {TargetMultiplier})";
        }
    }
}