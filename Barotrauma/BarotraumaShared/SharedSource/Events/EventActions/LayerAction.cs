using Microsoft.Xna.Framework;

namespace Barotrauma;

/// <summary>
/// Enable or disable a specific layer in a specific submarine.
/// </summary>
class LayerAction : EventAction
{
    [Serialize("", IsPropertySaveable.Yes, description: "Which layer to enable/disable. Use \"All\" to apply it to all layers.")]
    public Identifier Layer { get; set; }

    [Serialize(false, IsPropertySaveable.Yes, description: "Whether to enable or disable the layer.")]
    public bool Enabled { get; set; }

    [Serialize(0.0f, IsPropertySaveable.Yes, description: "Makes the layer shake if set to a value.")]
    public float ShakeAmount { get; set; }

    [Serialize("0,0", IsPropertySaveable.Yes, description: "Makes the layer accelerate every frame with specified amount.")]
    public Vector2 Acceleration { get; set; }

    [Serialize(TagAction.SubType.Any, IsPropertySaveable.Yes, description: "The type of submatine to enable or disable the layer in.")]
    public TagAction.SubType SubmarineType { get; set; }

    [Serialize(true, IsPropertySaveable.Yes, description: "Should the action continue if it can't find the specified layer in the specified submarine(s).")]
    public bool ContinueIfNotFound { get; set; }

    public LayerAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

    private bool isFinished;

    public override bool IsFinished(ref string goTo)
    {
        return isFinished;
    }
    public override void Reset(bool shouldEndConversations = true)
    {
        isFinished = false;
    }

    public override void Update(float deltaTime)
    {
        if (isFinished) { return; }

        bool layerFound = false;
        foreach (var submarine in Submarine.Loaded)
        {
            if (!TagAction.SubmarineTypeMatches(submarine, SubmarineType)) { continue; }
            if (submarine.LayerExists(Layer))
            {
                submarine.SetLayerEnabled(
                    layer: Layer, 
                    enabled: Enabled, 
                    shakeAmount: ShakeAmount, 
                    acceleration: Acceleration, 
                    sendNetworkEvent: true);

                layerFound = true;
            }
        }
        if (ContinueIfNotFound)
        {
            isFinished = true;
        }
        else
        {
            if (layerFound) { isFinished = true; }
        }
    }

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(LayerAction)} -> ({(Enabled ? "Enable" : "Disable")} {Layer.ColorizeObject()})";
    }
}