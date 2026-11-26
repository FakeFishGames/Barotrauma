using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma;

/// <summary>
/// Checks whether there's currently a fire in a submarine.
/// </summary>
class CheckFireAction : BinaryOptionAction
{
    [Serialize(SubmarineType.Player, IsPropertySaveable.Yes, description: "The type of the submarine that should be checked for fires.")]
    public SubmarineType SubmarineType { get; set; }

    [Serialize(false, IsPropertySaveable.Yes, description: "Should connected submarines also be checked for fires?")]
    public bool CheckForConnectedSubmarines { get; set; }

    [Serialize(1.0f, IsPropertySaveable.Yes, description: "The minimum required combined width of all active fires in a particular submarine for the check to pass.")]
    public float MinFireSize { get; set; }

    public CheckFireAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

    protected override bool? DetermineSuccess()
    {
        foreach (Submarine submarine in Submarine.Loaded)
        {
            if (submarine.Info.Type != SubmarineType) { continue; }

            List<Hull> hulls = submarine.GetHulls(alsoFromConnectedSubs: CheckForConnectedSubmarines);

            float combinedFireSize = hulls.SelectMany(hull => hull.FireSources).Sum(fire => fire.Size.X);
            
            if (combinedFireSize >= MinFireSize)
            {
                return true;
            }
        }

        return false;
    }

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(DetermineFinished())} {nameof(CheckFireAction)} -> (MinFireSize: {MinFireSize.ColorizeObject()}," +
               $" Success: {succeeded.ColorizeObject()})";
    }
}