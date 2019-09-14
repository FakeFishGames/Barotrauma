namespace Barotrauma
{
    partial class Traitor
    {
        public abstract class HumanoidGoal : Goal
        {
            public override bool Start(Traitor traitor)
            {
                if (!base.Start(traitor))
                {
                    return false;
                }
                return traitor?.Character?.IsHumanoid ?? false;
            }
        }
    }
}