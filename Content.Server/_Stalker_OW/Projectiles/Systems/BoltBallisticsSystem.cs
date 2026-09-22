using System;
using System.Numerics;
using Content.Shared._Stalker_OW.Projectiles.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Stalker_OW.Projectiles.Systems;

public sealed class BoltGroundHitEvent : EntityEventArgs
{
}

/// <summary>
/// Manages flight duration, tracks active flight, and handles "recycling"
/// </summary>
public sealed class BoltBallisticsSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    
    private readonly HashSet<EntityUid> _activeBolts = new();
    private readonly List<EntityUid> _finishedBolts = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GunComponent, AmmoShotEvent>(OnAmmoShot);
    }

    private void OnAmmoShot(Entity<GunComponent> gun, ref AmmoShotEvent args)
    {
        foreach (var uid in args.FiredProjectiles)
        {
            if (!TryComp<BoltBallisticsComponent>(uid, out var ballistics) ||
                !TryComp<PhysicsComponent>(uid, out var physics))
            {
                continue;
            }

            BeginFlight(uid, ballistics, physics);
            _activeBolts.Add(uid);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_activeBolts.Count == 0)
            return;

        _finishedBolts.Clear();
        foreach (var uid in _activeBolts)
        {
            if (!TryComp<BoltBallisticsComponent>(uid, out var ballistics) ||
                !TryComp<PhysicsComponent>(uid, out var physics) ||
                !TryComp<ProjectileComponent>(uid, out var projectile))
            {
                _finishedBolts.Add(uid);
                continue;
            }

            if (projectile.Weapon == null)
            {
                ResetFlight(ballistics);
                _finishedBolts.Add(uid);
                continue;
            }

            if (!ballistics.InFlight)
                BeginFlight(uid, ballistics, physics);

            ballistics.FlightTimeRemaining -= frameTime;

            if (ballistics.FlightTimeRemaining <= 0f)
            {
                FinishFlight(uid, ballistics, physics, projectile);
                _finishedBolts.Add(uid);
            }
        }

        foreach (var uid in _finishedBolts)
            _activeBolts.Remove(uid);
    }

    /// <summary>
    /// Starts a new flight cycle
    /// </summary>
    private void BeginFlight(
        EntityUid uid,
        BoltBallisticsComponent ballistics,
        PhysicsComponent physics)
    {
        ballistics.InFlight = true;

        var requestedTime = ballistics.FlightTimeForCurrentLaunch >= 0f
            ? ballistics.FlightTimeForCurrentLaunch
            : ballistics.MaxFlightTime;

        ballistics.FlightTimeRemaining = Math.Clamp(
            requestedTime,
            0f,
            ballistics.MaxFlightTime);

        // Apply speed multiplier
        if (physics.BodyType == BodyType.Dynamic)
        {
            var speedMultiplier = Math.Max(0f, ballistics.SpeedMultiplier);
            var velocity = physics.LinearVelocity * speedMultiplier;

            _physics.SetLinearVelocity(uid, velocity, body: physics);
        }
    }
    
    /// <summary>
    /// Manually overrides flight duration
    /// </summary>
    public void SetFlightTime(EntityUid uid, float requestedTime, BoltBallisticsComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        component.FlightTimeForCurrentLaunch = Math.Clamp(requestedTime, 0f, component.MaxFlightTime);
        if (component.InFlight)
            component.FlightTimeRemaining = component.FlightTimeForCurrentLaunch;
    }

    /// <summary>
    /// Stops flight w/o removing components
    /// Allows recovered bolts to be fired again
    /// </summary>
    public void StopFlight(EntityUid uid, BoltBallisticsComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        ResetFlight(component);
        _activeBolts.Remove(uid);
    }

    /// <summary>
    /// Resets projectile entity
    /// </summary>
    private void FinishFlight(EntityUid uid, BoltBallisticsComponent ballistics, PhysicsComponent physics, ProjectileComponent projectile)
    {
        if (physics.BodyType == BodyType.Dynamic)
            _physics.SetLinearVelocity(uid, Vector2.Zero, body: physics);

        ResetFlight(ballistics);

        // Restore reusable state
        projectile.Weapon = null;
        projectile.Shooter = null;
        projectile.ProjectileSpent = false;
        projectile.PenetrationAmount = FixedPoint2.Zero;

        Dirty(uid, projectile);
        
        RaiseLocalEvent(uid, new BoltGroundHitEvent());
    }

    /// <summary>
    /// Clears flight timers
    /// </summary>
    private static void ResetFlight(BoltBallisticsComponent ballistics)
    {
        ballistics.InFlight = false;
        ballistics.FlightTimeRemaining = 0f;
        ballistics.FlightTimeForCurrentLaunch = -1f;
    }
}