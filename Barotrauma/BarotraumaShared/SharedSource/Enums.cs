namespace Barotrauma
{
    public enum TransitionMode
    {
        Linear,
        Smooth,
        Smoother,
        EaseIn,
        EaseOut,
        Exponential
    }

    public enum ActionType
    {
        Always, OnPicked, OnUse, OnSecondaryUse,
        OnWearing, OnContaining, OnContained, OnNotContained,
        OnActive, OnFailure, OnBroken,
        OnFire, InWater, NotInWater,
        OnImpact,
        OnEating,
        OnDeath = OnBroken,
        OnDamaged,
        OnSevered,
        OnProduceSpawned,
        OnOpen, OnClose,
    }
}
