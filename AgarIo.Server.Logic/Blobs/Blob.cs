﻿namespace AgarIo.Server.Logic.Blobs
{
    using System;

    using AgarIo.Server.Logic.Physics;

    public abstract class Blob
    {
        private readonly IPhysics _physics;

        private const double MassSizeFactor = 100.0;

        private float _radius;

        private float _mass;

        private Vector _position;

        private Vector _velocity;

        private IBody _body;

        private bool _isStatic;

        private bool _radiusOverride;

        private bool _positionOverride;

        private bool _velocityOverride;

        private bool _massOverride;

        private bool _isCreated;

        protected Blob(IGame game, IPhysics physics, Vector position, float mass)
        {
            _physics = physics;

            Game = game;
            Position = position;
            Mass = mass;

            _isStatic = false;
            _isCreated = false;
        }

        internal IGame Game { get; }

        public bool IsReady => Body != null && Body.IsReady;

        public float Radius
        {
            get { return _radius; }

            set
            {
                _radius = value;
                _radiusOverride = true;
            }
        }

        public virtual Vector Position
        {
            get { return _position; }

            internal set
            {
                _position = value;
                _positionOverride = true;
            }
        }

        public virtual Vector Velocity
        {
            get { return _velocity; }

            internal set
            {
                _velocity = value;
                _velocityOverride = true;
            }
        }

        public virtual float Mass
        {
            get { return _mass; }

            internal set
            {
                _mass = value;
                _massOverride = true;

                Radius = GetRadius(value);
            }
        }

        private IBody Body
        {
            get { return _body; }

            set
            {
                _body = value;
                SyncWithPhysics(true);
            }
        }

        public void SyncWithPhysics(bool overrideAll)
        {
            if (_radiusOverride || overrideAll)
            {
                _body.Radius = _radius;
                _radiusOverride = false;
            }
            else
            {
                _radius = _body.Radius;
            }

            if (_positionOverride || overrideAll)
            {
                _body.Position = _position;
                _positionOverride = false;
            }
            else
            {
                _position = _body.Position;
            }

            if (_body.IsStatic)
            {
                return;
            }

            if (_velocityOverride || overrideAll)
            {
                _body.LinearVelocity = _velocity;
                _velocityOverride = false;
            }
            else
            {
                _velocity = _body.LinearVelocity;
            }

            if (_massOverride || overrideAll)
            {
                _body.Mass = _mass;
                _massOverride = false;
            }
            else
            {
                _mass = _body.Mass;
            }
        }

        internal static int GetRadius(float mass)
        {
            return (int)Math.Ceiling(Math.Sqrt(MassSizeFactor * mass));
        }

        internal virtual void OnCreate()
        {
            if (_isCreated)
            {
                return;
            }

            Body = _physics.CreateBody(this, Radius, Mass, _isStatic);
            _isCreated = true;
        }

        internal virtual void OnRemove()
        {
            if (!_isCreated)
            {
                return;
            }

            _physics.DestroyBody(Body);
            _isCreated = false;
        }

        internal virtual void Update()
        {
            DecaySpeed();
        }

        internal virtual bool OnCollision(Blob otherBlob)
        {
            return false;
        }

        protected void MakeStatic()
        {
            _isStatic = true;
            if (Body != null)
            {
                _physics.MakeBodyStatic(Body);
            }
        }

        protected void MakeDynamic()
        {
            _isStatic = false;
            if (Body != null)
            {
                _physics.MakeBodyDynamic(Body);
            }
        }

        private void DecaySpeed()
        {
            Velocity *= Game.Settings.MoveDecayPerTick;
        }
    }
}