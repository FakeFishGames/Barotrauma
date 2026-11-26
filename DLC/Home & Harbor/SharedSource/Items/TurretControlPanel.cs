using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace HomeAndHarbor
{
    /// <summary>
    /// An UI that can be used to rotate and launch a turret, or an item that uses the turret component to rotate (such as a searchlight or a thruster).
    /// </summary>
    public partial class TurretControlPanel : ItemComponent, IClientSerializable, IServerSerializable
    {
        private float rotationDeg;

        [Serialize(defaultValue: 0.0f, isSaveable: IsPropertySaveable.Yes)]
        public float RotationDeg
        {
            get => rotationDeg;
            set
            {
                if (MathUtils.NearlyEqual(value, rotationDeg)) { return; }
                rotationDeg = value;
                float angleRad = MathUtils.WrapAngleTwoPi(MathHelper.ToRadians(value - 90));
                rotationSignal = MathUtils.RoundToInt(MathHelper.ToDegrees(angleRad)).ToString();
            }
        }

        private string buttonText = string.Empty;
        [Serialize(defaultValue: "", isSaveable: IsPropertySaveable.Yes), Editable]
        public string ButtonText
        {
            get => buttonText;
            set
            {
                buttonText = value;
#if CLIENT
                if (firingButton != null)
                {
                    firingButton.Text =
                        buttonText.IsNullOrWhiteSpace() ?
                            TextManager.Get("tutorial.basics.fire") :
                            TextManager.Get(value).Fallback(value);
                }
#endif
            }
        }


        private string uiLabel = string.Empty;
        [Serialize(defaultValue: "", IsPropertySaveable.Yes, description: "An optional text displayed above the UI."), Editable]
        public string UILabel
        {
            get => uiLabel;
            set
            {
                uiLabel = value;
#if CLIENT
                if (labelText != null)
                {
                    labelText.Text =
                        uiLabel.IsNullOrWhiteSpace() ?
                            item.Prefab.Name :
                            TextManager.Get(uiLabel).Fallback(uiLabel);
                }
#endif
            }
        }

        [Serialize(defaultValue: true, IsPropertySaveable.Yes, description: "Should the battery states of the turrets/devices be shown when operating the control panel?"), Editable]
        public bool ShowBatteryState { get; private set; }

        [Serialize(defaultValue: true, IsPropertySaveable.Yes, description: "Should the firing button be disabled when the connected device(s) are out of power?"), Editable]
        public bool DisableFiringButtonIfOutOfPower { get; private set; }

        private string rotationSignal = "0";

        private float firingButtonPressedTimer;
        private bool sendFiringSignal;

        private Character? user;

        private List<Turret> targetTurrets = new List<Turret>();

        public TurretControlPanel(Item item, ContentXElement element) : base(item, element)
        {
            IsActive = true;
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(ContentXElement element);

        public override void Update(float deltaTime, Camera cam)
        {
            targetTurrets ??= new List<Turret>();
            if (user != null)
            {
                // Clear the user of the previously controlled turrets. The ones that still receive the rotation signal get the user
                // re-assigned below, but ones that no longer do (e.g. because the circuit routed the signal elsewhere)
                // immediately stop being considered actively used, instead of waiting for the timeout in the turret to run out.
                foreach (var targetTurret in targetTurrets)
                {
                    targetTurret.User = null;
                }
            }
            targetTurrets.Clear();

            base.Update(deltaTime, cam);
            item.SendSignal(rotationSignal, connectionName: "rotation_out");

            for (int i = item.LastSentSignalRecipients.Count - 1; i >= 0; i--)
            {
                if (item.LastSentSignalRecipients[i].Item.Condition <= 0.0f || item.LastSentSignalRecipients[i].IsPower) { continue; }
                if (item.LastSentSignalRecipients[i].Item.GetComponent<Turret>() is { } turret)
                {
                    targetTurrets.Add(turret);
                }
            }

            if (user != null)
            {
                if (!user.CanInteractWith(item)) { user = null; }
                foreach (var targetTurret in targetTurrets)
                {
                    targetTurret.User = user;
                }
            }

            if (sendFiringSignal)
            {
                foreach (var targetTurret in targetTurrets)
                {
                    targetTurret.LastFiredTime = (float)Timing.TotalTimeUnpaused;
                }
                item.SendSignal("1", connectionName: "trigger_out");
                firingButtonPressedTimer -= deltaTime;
                if (firingButtonPressedTimer <= 0.0f)
                {
                    sendFiringSignal = false;
                }
            }

            UpdateProjSpecific(deltaTime);
        }

        partial void UpdateProjSpecific(float deltaTime);

    }
}