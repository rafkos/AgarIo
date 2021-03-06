﻿namespace AgarIo.Server.Logic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using AgarIo.Server.Logic.Blobs;
    using AgarIo.Server.Logic.GameModes;
    using AgarIo.Server.Logic.Physics;
    using AgarIo.SystemExtension;

    using NLog;

    using NodaTime;

    public class Game : IGame
    {
        public const double Epsilon = 0.0001;

        public const int TickDurationMs = 50;

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private static readonly object BlobsListLock = new object();

        private readonly List<Blob> _blobs;

        private readonly IPhysics _physics;

        private readonly IStateTracker _stateTracker;

        private readonly IPlayerRepository _playerRepository;

        private readonly IClock _clock;

        public Game(IRandom random, IPhysics physics, IStateTracker stateTracker, IPlayerRepository playerRepository, IClock clock)
        {
            _physics = physics;
            _stateTracker = stateTracker;
            _playerRepository = playerRepository;
            _clock = clock;
            Random = random;

            _blobs = new List<Blob>();
            Settings = new WorldSettings();
            GameMode = new ClassicGameMode(this, physics, stateTracker, playerRepository);

            Stop();
        }

        public IRandom Random { get; }

        public int Size { get; private set; }

        public ulong TickCount { get; private set; }

        public Instant TurnEndInstant { get; private set; }

        public IReadOnlyList<Blob> Blobs
        {
            get
            {
                lock (BlobsListLock)
                {
                    return _blobs.Where(blob => blob.IsReady).ToArray();
                }
            }
        }

        public WorldSettings Settings { get; set; }

        public IGameMode GameMode { get; private set; }

        public bool IsStarted { get; private set; }

        public void Start(int size, IGameMode gameMode)
        {
            _physics.Start(size);

            Size = size;

            GameMode = gameMode;

            GameMode.OnStart();

            TickCount = 0;

            SetTurnMinutes(Settings.TurnMinutes);

            IsStarted = true;

            Log.Info($"Started game with world size = {size}");
        }

        public void Stop()
        {
            foreach (var player in _playerRepository.Players)
            {
                player.SignalTick();
            }

            GameMode.OnFinish();

            lock (BlobsListLock)
            {
                foreach (var blob in _blobs)
                {
                    _stateTracker.RemoveBlob(blob);
                }

                foreach (var player in _playerRepository.Players)
                {
                    player.Clear();
                }

                _blobs.Clear();
            }

            IsStarted = false;
            _physics.Stop();

            Log.Info("Stopped game");
        }

        public void Reset()
        {
            Stop();
            Start(Size, GameMode);
        }

        public void AddBlob(Blob blob)
        {
            lock (BlobsListLock)
            {
                blob.OnCreate();
                _blobs.Add(blob);
            }
        }

        public void RemoveBlob(Blob blob)
        {
            lock (BlobsListLock)
            {
                blob.OnRemove();
                _blobs.Remove(blob);
            }
        }

        public Vector RemoveFoodAndGetSpawnPosition()
        {
            lock (BlobsListLock)
            {
                var foodBlobs = _blobs.OfType<FoodBlob>().ToArray();
                if (!foodBlobs.Any())
                {
                    return GetRandomPosition();
                }

                var randomFoodBlob = foodBlobs[Random.Next(foodBlobs.Length)];

                var position = randomFoodBlob.Position;

                RemoveBlob(randomFoodBlob);

                return position;
            }
        }

        public Vector GetRandomPosition()
        {
            return new Vector(Random.Next(-Size, Size + 1), Random.Next(-Size, Size + 1));
        }

        public void SetTurnMinutes(uint turnMinutes)
        {
            TurnEndInstant = _clock.Now + Duration.FromMinutes(turnMinutes);
        }

        public void Update()
        {
            if (!IsStarted)
            {
                return;
            }

            if (_clock.Now >= TurnEndInstant)
            {
                Reset();
                return;
            }

            _stateTracker.Reset();

            GameMode.OnUpdate();

            ApplyPlayerDecisions();

            UpdatePhysics();

            UpdateBlobs();

            TickCount++;

            foreach (var player in _playerRepository.Players)
            {
                player.SignalTick();
            }
            _playerRepository.RemoveUnregisteredAndDead();
        }

        private void ApplyPlayerDecisions()
        {
            lock (BlobsListLock)
            {
                foreach (var player in _playerRepository.Players)
                {
                    if (player.Join)
                    {
                        var position = RemoveFoodAndGetSpawnPosition();

                        var oldPlayerBlobs = _blobs.OfType<PlayerBlob>().Where(b => b.Owner.Name == player.Name).ToList();

                        foreach (var oldBlob in oldPlayerBlobs)
                        {
                            RemoveBlob(oldBlob);
                        }

                        var playerBlob = new PlayerBlob(player, this, _physics, _stateTracker, position, true);

                        AddBlob(playerBlob);

                        player.Join = false;

                        continue;
                    }

                    if (!player.Blobs.Any())
                    {
                        continue;
                    }

                    var center = DetermineGoosCenter(player.Blobs);

                    var direction = player.PlayerDecisions.Velocity;
                    foreach (var blob in player.Blobs.Where(x => x.ControlledByPlayer))
                    {
                        var normalizedVelocity = (center + direction - blob.Position).Normalize();
                        var speed = Math.Min(direction.Length, PlayerBlob.GetMaxSpeed(blob.Mass));
                        blob.Velocity = normalizedVelocity * speed;

                        switch (player.PlayerDecisions.Activity)
                        {
                            case Activity.None:
                                break;
                            case Activity.Split:
                                blob.Split();
                                break;
                            case Activity.EjectMass:
                                blob.EjectMass();
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }

                    player.PlayerDecisions.Activity = Activity.None;
                }
            }
        }

        private static Vector DetermineGoosCenter(IReadOnlyCollection<Blob> blobs)
        {
            var positionsSum = blobs.Select(blob => blob.Position).Aggregate((current, position) => current + position);

            return new Vector(positionsSum.X / blobs.Count, positionsSum.Y / blobs.Count);
        }

        private void UpdatePhysics()
        {
            lock (BlobsListLock)
            {
                foreach (var blob in _blobs)
                {
                    blob.SyncWithPhysics(false);
                }
            }

            _physics.Update();
        }

        private void UpdateBlobs()
        {
            lock (BlobsListLock)
            {
                foreach (var blob in _blobs)
                {
                    blob.Update();
                }
            }
        }
    }
}