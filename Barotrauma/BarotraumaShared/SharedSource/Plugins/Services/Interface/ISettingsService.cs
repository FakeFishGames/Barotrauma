#nullable enable

using Barotrauma.Networking;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Plugins
{
#if CLIENT
    public delegate void SettingOverrideUIDelegate(GUIFrame content);
#endif

    /// <summary>
    /// Allows plugins to create their own settings that can be saved to disk, edited in game and networked.
    /// </summary>
    public interface ISettingsService : IPluginService
    {
        /// <summary>
        /// The content package that this settings service belongs to.
        /// </summary>
        ContentPackage ContentPackage { get; }

        /// <summary>
        /// A save folder dedicated to this plugin that can be used to store files.
        /// </summary>
        string SaveFolder { get; }

#if CLIENT
        /// <summary>
        /// If not null, the default setting UI creation for this plugin is overriden and the specified delegate will be called instead. 
        /// This is useful if you want to completely take control over the UI of your plugin settings.
        /// </summary>
        /// <remarks>
        /// Note that this means that <see cref="ISetting.AddUI"/> wont get called automatically for all settings.
        /// </remarks>
        SettingOverrideUIDelegate? OverrideUI { get; set; }
#endif

        /// <summary>
        /// Returns all registered settings in this settings service.
        /// </summary>
        IEnumerable<ISetting> RegisteredSettings { get; }

        /// <summary>
        /// Register a new setting that can be retrieved using the identifier.
        /// </summary>
        /// <example>
        /// SettingsService.RegisterSetting(new FloatSetting($"foobar".ToIdentifier(), 5.0f, label: $"Foobar")
        /// {
        ///     ShowInUI = true,
        ///     ToolTip = "This is foobar",
        ///     LabelFunc = (x) => $"{MathF.Round(x)} foobar(s)",
        ///     StepValue = 1f,
        ///     Range = (0f, 100f),
        ///     SyncMode = SettingSyncMode.ServerAuthority,
        ///     IsAllowedToSet = (setting, client) => client == null || client.Permissions.HasFlag(Barotrauma.Networking.ClientPermissions.ManageRound)
        /// });
        /// </example>
        void RegisterSetting(ISetting setting);

        /// <summary>
        /// Retrieve a setting by it's identifier.
        /// </summary>
        /// <example>
        /// FloatSetting? setting = settingsService.RetrieveSetting&lt;FloatSetting&gt;($"foobar".ToIdentifier());
        /// if (setting != null)
        /// {
        ///     setting.Set(200f);
        ///     DebugConsole.NewMessage($"Value: {setting.Value}");
        /// }
        /// </example>
        T? RetrieveSetting<T>(Identifier identifier) where T : ISetting;

        /// <summary>
        /// Load all settings.
        /// </summary>
        void Load();

        /// <summary>
        /// Save all settings.
        /// </summary>
        void Save();
    }

    public enum SettingSyncMode
    {
        /// <summary>
        /// Does not sync between the client and server.
        /// </summary>
        NoSync,
        /// <summary>
        /// The client is allowed to make changes BUT will not be synced to the server. Any changes made by the server are synced to clients.
        /// </summary>
        ClientPermissiveDesync,
        /// <summary>
        /// Clients that pass <see cref="ISetting.IsAllowedToSet"/> are allowed to set this setting server-side. Any changes made by the server are also synced to clients.
        /// </summary>
        ServerAuthority
    }

    public delegate bool SettingIsAllowedToSetDelegate(ISetting setting, Client? client);

    public interface ISetting
    {
        /// <summary>
        /// An identifier that needs to be unique per settings service
        /// </summary>
        Identifier Identifier { get; }
        /// <summary>
        /// Determines how this setting should be synced in multiplayer, see <see cref="SettingSyncMode"/> for more info.
        /// </summary>
        SettingSyncMode SyncMode { get; }
        /// <summary>
        /// Determines if this setting can be set, on the server-side it's used to verify if a client is 
        /// indeed allowed to change this setting and on the client-side it's used to gray out the UI and prevent user interaction.
        /// </summary>
        SettingIsAllowedToSetDelegate IsAllowedToSet { get; }
        /// <summary>
        /// Determines if there's any pending changes in this setting.
        /// </summary>
        bool HasPendingChanges { get; }
        /// <summary>
        /// Resets this setting back to it's default state.
        /// </summary>
        void ResetToDefault();
        /// <summary>
        /// Applies any pending changes in this setting.
        /// </summary>
        void Apply();
        /// <summary>
        /// Reverts the pending changes back to the previous setting.
        /// </summary>
        void Revert();
        /// <summary>
        /// Indicates that this setting was changed and must be propagated to all clients or the server.
        /// </summary>
        bool IsDirty { get; set; }
        /// <summary>
        /// Indicates whether this setting should be added to the settings UI automatically.
        /// </summary>
        public bool ShowInUI { get; set; }
        /// <summary>
        /// Return false to skip saving this setting, used to avoid unnecessarily saving default values.
        /// </summary>
        bool ShouldSave();
        void Load(XElement element);
        void Save(XElement element);
        public abstract void WriteMessage(IWriteMessage writeMessage);
        public abstract void ReadMessage(IReadMessage readMessage);
#if CLIENT
        /// <summary>
        /// Called by settings UI to add any UI this setting has to the GUIList content.
        /// </summary>
        void AddUI(GUIFrame content);
#endif
    }
}