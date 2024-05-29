using System.Linq;

namespace Barotrauma;

/// <summary>
/// Displays a tutorial icon next to a specific target.
/// </summary>
class TutorialIconAction : EventAction
{
    public enum ActionType { Add, Remove, RemoveTarget, RemoveIcon, Clear };

    [Serialize(ActionType.Add, IsPropertySaveable.Yes, description: "What to do with the icon. Add = add an icon, Remove = remove the icon that has the specific target and style, RemoveTarget = remove all icons assigned to the specific target, RemoveIcon = remove all icons with the specific style, Remove = remove all icons.")]
    public ActionType Type { get; set; }

    [Serialize("", IsPropertySaveable.Yes, description: "Tag of the target to assign the icon to.")]
    public Identifier TargetTag { get; set; }

    [Serialize("", IsPropertySaveable.Yes, description: "Style of the icon.")]
    public Identifier IconStyle { get; set; }

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
                    tutorialMode.Tutorial?.Icons.RemoveAll(i => i.entity == target && i.iconStyle == IconStyle);
                }
                else if (Type == ActionType.RemoveTarget)
                {
                    tutorialMode.Tutorial?.Icons.RemoveAll(i => i.entity == target);
                }
                else if (Type == ActionType.RemoveIcon)
                {
                    tutorialMode.Tutorial?.Icons.RemoveAll(i => i.iconStyle == IconStyle);
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