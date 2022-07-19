using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Scanner : ItemComponent
    {
        [Serialize(1.0f, IsPropertySaveable.No, description: "How long it takes for the scan to be completed.")]
        public float ScanDuration { get; set; }
        [Serialize(0.0f, IsPropertySaveable.No, description: "How far along the scan is. When the timer goes above ScanDuration, the scan is completed.")]
        public float ScanTimer
        {
            get
            {
                return scanTimer;
            }
            set
            {
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
                if (Holdable == null) { return; }
                bool wasScanCompletedPreviously = IsScanCompleted;
                scanTimer = Math.Max(0.0f, value);
                if (!wasScanCompletedPreviously && IsScanCompleted)
                {
                    OnScanCompleted?.Invoke(this);
                }
#if SERVER
                if (wasScanCompletedPreviously != IsScanCompleted || Math.Abs(LastSentScanTimer - scanTimer) > 0.1f)
                {
                    item.CreateServerEvent(this);
                    LastSentScanTimer = scanTimer;
                }
#endif
            }
        }
        [Serialize(1.0f, IsPropertySaveable.No, description: "How far the scanner can be from the target for the scan to be successful.")]
        public float ScanRadius { get; set; }
        [Serialize(true, IsPropertySaveable.No, description: "Should the progress bar always be displayed when the item has been attached.")]
        public bool AlwaysDisplayProgressBar { get; set; }

        private Holdable Holdable { get; set; }
        /// <summary>
        /// Should the progress bar be displayed. Use when AlwaysDisplayProgressBar is set to false.
        /// </summary>
        public bool DisplayProgressBar { get; set; } = false;
        private bool IsScanCompleted => scanTimer >= ScanDuration;

        private float scanTimer;

        public Action<Scanner> OnScanStarted, OnScanCompleted;

        public Scanner(Item item, ContentXElement element) : base(item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (Holdable != null && Holdable.Attachable && Holdable.Attached)
            {
                if (ScanTimer <= 0.0f)
                {
                    OnScanStarted?.Invoke(this);
                }
                ScanTimer += deltaTime;
                item.AiTarget?.IncreaseSoundRange(deltaTime, speed: 2.0f);
                ApplyStatusEffects(ActionType.OnActive, deltaTime);
            }
            else
            {
                ScanTimer = 0.0f;
                DisplayProgressBar = false;
            }
            UpdateProjSpecific();
        }

        partial void UpdateProjSpecific();

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            Holdable = item.GetComponent<Holdable>();
            if (Holdable == null || !Holdable.Attachable)
            {
                DebugConsole.ThrowError("Error in initializing a Scanner component: an attachable Holdable component is required on the same item and none was found");
                IsActive = false;
            }
        }
    }
}
