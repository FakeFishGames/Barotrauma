using System;
using System.Collections.Generic;
using FarseerPhysics.Dynamics;

namespace FarseerPhysics.Controllers
{
    /// <summary>
    /// Put a limit on the linear (translation - the movespeed) and angular (rotation) velocity
    /// of bodies added to this controller.
    /// </summary>
    public class VelocityLimitController : Controller
    {
        public bool LimitAngularVelocity = true;
        public bool LimitLinearVelocity = true;
        private List<Body> _bodies = new List<Body>();
        private float _maxAngularSqared;
        private float _maxAngularVelocity;
        private float _maxLinearSqared;
        private float _maxLinearVelocity;

        /// <summary>
        /// Initializes a new instance of the <see cref="VelocityLimitController"/> class.
        /// Sets the max linear velocity to Settings.MaxTranslation
        /// Sets the max angular velocity to Settings.MaxRotation
        /// </summary>
        public VelocityLimitController()
            : base(ControllerType.VelocityLimitController)
        {
            MaxLinearVelocity = Settings.MaxTranslation;
            MaxAngularVelocity = Settings.MaxRotation;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VelocityLimitController"/> class.
        /// Pass in 0 or float.MaxValue to disable the limit.
        /// maxAngularVelocity = 0 will disable the angular velocity limit.
        /// </summary>
        /// <param name="maxLinearVelocity">The max linear velocity.</param>
        /// <param name="maxAngularVelocity">The max angular velocity.</param>
        public VelocityLimitController(float maxLinearVelocity, float maxAngularVelocity)
            : base(ControllerType.VelocityLimitController)
        {
            if (maxLinearVelocity == 0 || maxLinearVelocity == float.MaxValue)
                LimitLinearVelocity = false;

            if (maxAngularVelocity == 0 || maxAngularVelocity == float.MaxValue)
                LimitAngularVelocity = false;

            MaxLinearVelocity = maxLinearVelocity;
            MaxAngularVelocity = maxAngularVelocity;
        }

        /// <summary>
        /// Gets or sets the max angular velocity.
        /// </summary>
        /// <value>The max angular velocity.</value>
        public float MaxAngularVelocity
        {
            get { return _maxAngularVelocity; }
            set
            {
                _maxAngularVelocity = value;
                _maxAngularSqared = _maxAngularVelocity * _maxAngularVelocity;
            }
        }

        /// <summary>
        /// Gets or sets the max linear velocity.
        /// </summary>
        /// <value>The max linear velocity.</value>
        public float MaxLinearVelocity
        {
            get { return _maxLinearVelocity; }
            set
            {
                _maxLinearVelocity = value;
                _maxLinearSqared = _maxLinearVelocity * _maxLinearVelocity;
            }
        }

        public override void Update(float dt)
        {
            foreach (Body body in _bodies)
            {
                if (!IsActiveOn(body))
                    continue;

                if (LimitLinearVelocity)
                {
                    //Translation
                    // Check for large velocities.
                    float translationX = dt * body._linearVelocity.X;
                    float translationY = dt * body._linearVelocity.Y;
                    float result = translationX * translationX + translationY * translationY;

                    if (result > dt * _maxLinearSqared)
                    {
                        float sq = (float)Math.Sqrt(result);

                        float ratio = _maxLinearVelocity / sq;
                        body._linearVelocity.X *= ratio;
                        body._linearVelocity.Y *= ratio;
                    }
                }

                if (LimitAngularVelocity)
                {
                    //Rotation
                    float rotation = dt * body._angularVelocity;
                    if (rotation * rotation > _maxAngularSqared)
                    {
                        float ratio = _maxAngularVelocity / Math.Abs(rotation);
                        body._angularVelocity *= ratio;
                    }
                }
            }
        }

        public void AddBody(Body body)
        {
            _bodies.Add(body);
        }

        public void RemoveBody(Body body)
        {
            _bodies.Remove(body);
        }
    }
}