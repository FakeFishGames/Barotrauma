using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace HomeAndHarbor;

public partial class SingularityComponent : ItemComponent
{
    private float particleTimer;

    [Serialize(defaultValue: "singularity", IsPropertySaveable.Yes, description: "Identifier of the particle the singularity should spawn. Scaled according to the strength of the singularity.")]
    public string ParticlePrefab { get; set; } = string.Empty;

    public Vector2 DrawSize
    {
        //use the extents of the item as the draw size
        get { return Vector2.Zero; }
    }

    partial void UpdateProjSpecific(float deltaTime)
    {
        if (!isContained && !ParticlePrefab.IsNullOrEmpty())
        {
            float particlesPerSec = MathHelper.Lerp(5, 25, NormalizedStrength);
            float particleInterval = 1.0f / particlesPerSec;
            particleTimer += deltaTime;
            while (particleTimer > particleInterval)
            {
                var particle = GameMain.ParticleManager.CreateParticle(ParticlePrefab, item.WorldPosition,
                    currentVelocity,
                    0.0f, item.CurrentHull);
                if (particle != null)
                {
                    particle.Size *= item.Scale;
                    particle.ParentEntity = item;
                }
                particleTimer -= particleInterval;
            }
        }

        var lightComponent = item.GetComponent<LightComponent>();
        if (lightComponent != null)
        {
            lightComponent.Range = MathHelper.Lerp(100.0f, 2048.0f, NormalizedStrength);
            lightComponent.LightColor = new Color(lightComponent.LightColor, NormalizedStrength);
        }        
    }

    public void ClientEventRead(IReadMessage msg, float sendingTime)
    {
        IsContained = msg.ReadBoolean();
        Strength = msg.ReadRangedSingle(MinStrength, MaxStrength, bitCount: 8);
        if (!IsContained)
        {
            movementTimer = msg.ReadSingle();
            currentVelocity.X = msg.ReadSingle();
            currentVelocity.Y = msg.ReadSingle();
        }
    }
}
