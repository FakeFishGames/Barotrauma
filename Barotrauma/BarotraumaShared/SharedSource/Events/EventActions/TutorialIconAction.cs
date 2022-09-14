using System.Linq;

namespace Barotrauma;

class TutorialIconAction : EventAction
{
    public enum ActionType { Add, Remove, RemoveTarget, RemoveIcon, Clear };

    [Serialize(ActionType.Add, IsPropertySaveable.Yes)]
    public ActionType Type { get; set; }

    [Serialize("", IsPropertySaveable.Yes)]
    public Identifier TargetTag { get; set; }

    [Serialize("", IsPropertySaveable.Yes)]
    public string IconStyle { get; set; }

    private bool isFinished;

    public TutorialIconAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

    public override void Update(float deltaTime)
    {
        if (isFinished) { return; }
#if CLIENT
        if (GameMain.GameSession?.GameMode is TutorialMode tutorialMode)
        {
            if (ParentEvent.GetTargets(TargetTag).FirstOrDefault() is Entity target)
            {
                if (Type == ActionType.Add)
                {
                    tutorialMode.Tutorial?.Icons.Add((target, IconStyle));
                }
                else if(Type == ActionType.Remove)
                {
                    tutorialMode.Tutorial?.Icons.RemoveAll(i => i.entity == target && i.iconStyle.Equals(IconStyle, System.StringComparison.OrdinalIgnoreCase));
                }
                else if (Type == ActionType.RemoveTarget)
                {
                    tutorialMode.Tutorial?.Icons.RemoveAll(i => i.entity == target);
                }
                else if (Type == ActionType.RemoveIcon)
                {
                    tutorialMode.Tutorial?.Icons.RemoveAll(i => i.iconStyle.Equals(IconStyle, System.StringComparison.OrdinalIgnoreCase));
                }
                else if (Type == ActionType.Clear)
                {
                    tutorialMode.Tutorial?.Icons.Clear();
                }
            }
        }
#endif
        isFinished = true;
    }

    public override bool IsFinished(ref string goToLabel)
    {
        return isFinished;
    }

    public override void Reset()
    {
        isFinished = false;
    }
}