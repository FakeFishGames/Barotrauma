using Barotrauma;

namespace HomeAndHarbor;

/// <summary>
/// Check whether a module has been constructed or in the process of being constructed.
/// </summary>
class CheckModuleConstructionAction : BinaryOptionAction
{
    public enum ModuleState
    {
        InProgress,
        InProgressOrDone,
        Done
    }

    [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the module to check.")]
    public Identifier Identifier { get; set; }

    [Serialize("", IsPropertySaveable.Yes, description: "Category of the module to check.")]
    public Identifier Category { get; set; }

    [Serialize(ModuleState.InProgressOrDone, IsPropertySaveable.Yes)]
    public ModuleState State { get; set; }

    public CheckModuleConstructionAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
    { 
        if (Identifier.IsEmpty && Category.IsEmpty)
        {
            DebugConsole.ThrowError($"Error in {nameof(CheckModuleConstructionAction)} in event {ParentEvent?.Prefab?.Identifier.Value ?? "null"} - neither {nameof(Identifier)} or {nameof(Category)} set.");
        }
    }

    protected override bool? DetermineSuccess()
    {
        if (GameMain.GameSession?.Campaign is not { } campaign)
        {
            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: cannot use {nameof(CheckModuleConstructionAction)} when a campaign is not active.");
            return false;
        }
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(campaign.CurrentLocation);
        if (outpostData == null) { return false; }

        bool inProgress = outpostData.PendingOutpostModules.Any(m => PurchasableModuleMatches(m.ModulePrefab));

        bool done = 
            Level.Loaded.StartOutpost != null && 
            Level.Loaded.StartOutpost.OutpostModules.Any(m =>
                FindPurchasableModule(m) is PurchasableOutpostModulePrefab purchasableModule && PurchasableModuleMatches(purchasableModule));

        bool PurchasableModuleMatches(PurchasableOutpostModulePrefab? module)
        {
            return 
                module != null &&
                (Identifier.IsEmpty || module.Identifier == Identifier) && 
                (Category.IsEmpty || module.Category == Category);
        }

        PurchasableOutpostModulePrefab? FindPurchasableModule(PlacedModule placedModule)
        {
            foreach (var module in PurchasableOutpostModulePrefab.Prefabs)
            {
                if (module.OutpostInfo.FilePath == placedModule.Info.FilePath)
                {
                    return module;
                }
            }
            return null;
        }

        return State switch
        {
            ModuleState.InProgress => inProgress,
            ModuleState.InProgressOrDone => inProgress || done,
            ModuleState.Done => done,
            _ => (bool?)false,
        };
    }

    public override string ToDebugString()
    {
        return 
            $"{ToolBox.GetDebugSymbol(succeeded.HasValue)} {nameof(CheckModuleConstructionAction)} -> ({(Identifier.IsEmpty ? Category : Identifier)} is {State}" +
            $" Succeeded: {(succeeded.HasValue ? succeeded.Value.ToString() : "not determined").ColorizeObject()})";
    }
}
