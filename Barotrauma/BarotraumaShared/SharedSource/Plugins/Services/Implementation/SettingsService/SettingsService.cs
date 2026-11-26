#nullable enable

using Barotrauma.IO;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma.Plugins
{
    internal sealed partial class SettingsService : ISettingsService, ITransientService
    {
        private string settingPathIdentifier
        {
            get
            {
                return File.SanitizeName(ContentPackage.Name);
            }
        }

        public string SaveFolder
        {
            get
            {
#if DEBUG
                string path = Path.Combine(Directory.GetCurrentDirectory(), "ModData", settingPathIdentifier);
#else
                string path = Path.Combine(SaveUtil.DefaultSaveFolder, "ModData", settingPathIdentifier);
#endif
                System.IO.Directory.CreateDirectory(path);
                return path;
            }
        }

        public ContentPackage ContentPackage => pluginData.ContentPackage;
#if CLIENT
        public SettingOverrideUIDelegate? OverrideUI { get; set; }
#endif
        public IEnumerable<ISetting> RegisteredSettings => registeredSettings;

        private string settingsFilePath => Path.Combine(SaveFolder, $"{settingPathIdentifier}Settings.xml");

        public Assembly PluginAssembly => pluginAssembly;
        public PluginData PluginData => pluginData;

        private readonly Assembly pluginAssembly;
        private readonly PluginData pluginData;
        private readonly List<ISetting> registeredSettings = new List<ISetting>();
        private readonly PluginMetadata pluginMetadata;

        public SettingsService(PluginData data, Assembly assembly)
        {
            pluginData = data;
            pluginAssembly = assembly;
            PluginLoader.AddTransientService(assembly, service: this);
            PluginData.SettingsServices.Add(this);

            if (!PluginLoader.TryFindMetadataForAssembly(PluginAssembly).TryUnwrap(out pluginMetadata))
            {
                DebugConsole.ThrowError("Failed to find metadata for plugin assembly.", pluginAssembly: PluginAssembly);
            }
        }

        public void RegisterSetting(ISetting setting)
        {
            if (registeredSettings.Any(c => c.Identifier == setting.Identifier))
            {
                throw new ArgumentException($"Tried to register setting with id {setting.Identifier} twice.");
            }

            registeredSettings.Add(setting);
        }

        public T? RetrieveSetting<T>(Identifier identifier) where T : ISetting
        {
            ISetting? setting = registeredSettings.FirstOrDefault(c => c.Identifier == identifier);
            if (setting == null) { return default; }
            if (setting is T typedSetting)
            {
                return typedSetting;
            }
            else
            {
                throw new ArgumentException($"Tried to retrieve setting {identifier} with a mismatching type.");
            }
        }

        public void Load()
        {
            DebugConsole.Log($"Loading settings for content package {ContentPackage.Name}");

            foreach (ISetting setting in registeredSettings)
            {
                setting.ResetToDefault();
            }

            if (!File.Exists(settingsFilePath))
            {
                return;
            }

            XElement root = XElement.Load(settingsFilePath);
            foreach (XElement element in root.Elements())
            {
                Identifier identifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);

                ISetting? setting = RetrieveSetting<ISetting>(identifier);

                if (setting != null)
                {
                    setting.Load(element);
                }
                else
                {
                    DebugConsole.AddWarning($"Tried to load a non-registered setting with identifier \"{identifier}\"");
                }
            }
        }

        public void Save()
        {
            DebugConsole.Log($"Saving settings for content package {ContentPackage.Name}");

            XElement root = new XElement("Settings");
            foreach (ISetting setting in registeredSettings)
            {
                if (!setting.ShouldSave()) { continue; }

                XElement settingElement = new XElement("Setting");
                settingElement.SetAttributeValue("identifier", setting.Identifier);
                setting.Save(settingElement);
                root.Add(settingElement);
            }
            root.Save(settingsFilePath);
        }

        public void Dispose()
        {
            registeredSettings.Clear();
            PluginData.SettingsServices.Remove(this);
        }
    }
}