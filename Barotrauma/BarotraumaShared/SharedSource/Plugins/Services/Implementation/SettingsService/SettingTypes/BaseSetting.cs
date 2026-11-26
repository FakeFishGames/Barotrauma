#nullable enable

using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace Barotrauma.Plugins
{
    public abstract class BaseSetting<T> : ISetting where T : IEquatable<T>
    {
        public Identifier Identifier { get; init; }
        public T PendingValue { get; protected set; }

        public T Value
        {
            get => this.value;

            [MemberNotNull(nameof(value))]
            [MemberNotNull(nameof(PendingValue))]
            set
            {
                this.value = value;
                PendingValue = value;
            }
        }
        public T DefaultValue { get; init; }
        public SettingSyncMode SyncMode { get; set; }
        public bool IsDirty { get; set; }
        public bool ShowInUI { get; set; }
        public bool HasPendingChanges => !PendingValue.Equals(Value);
        public SettingIsAllowedToSetDelegate IsAllowedToSet { get; set; } = 
            (ISetting setting, Client? client) => setting.SyncMode != SettingSyncMode.ServerAuthority || client == null;

        private T value = default!;

#if CLIENT
        protected GUIFrame? MainUI { get; set; }

        /// <summary>
        /// Returns whether this client is allowed to edit this setting right now.
        /// </summary>
        protected bool CanClientEdit
        {
            get
            {
                return IsAllowedToSet(this, GameMain.Client?.MyClient);
            }
        }
#endif

        public BaseSetting(Identifier identifier, T defaultValue)
        {
            Identifier = identifier;
            DefaultValue = defaultValue;

            Value = DefaultValue;
        }

        public virtual void ResetToDefault()
        {
            Value = DefaultValue;
        }

        public virtual void Apply()
        {
            Set(PendingValue);
        }

        public virtual void Revert()
        {
            PendingValue = Value;
        }

        public virtual void Set(T newValue)
        {
            if (newValue.Equals(Value))
            {
                return;
            }

#if CLIENT
            if (!CanClientEdit)
            {
                DebugConsole.AddWarning($"Tried to edit setting {Identifier}, but this client isn't allowed to edit it.");
                return;
            }
#endif

            Value = newValue;

            if (GameMain.NetworkMember != null)
            {
                IsDirty = true;
            }
        }

        public bool ShouldSave()
        {
            return !DefaultValue.Equals(Value);
        }

        public abstract void Load(XElement element);
        public abstract void Save(XElement element);
        public virtual void WriteMessage(IWriteMessage writeMessage) { throw new NotImplementedException(); }
        public virtual void ReadMessage(IReadMessage readMessage) { throw new NotImplementedException(); }
#if CLIENT
        public void AddUI(GUIFrame content)
        {
            if (MainUI == null)
            {
                CreateUI();
            }

            if (MainUI != null)
            {
                MainUI.RectTransform.Parent = content.RectTransform;
            }
        }
        public virtual void CreateUI() { }
#endif
    }
}