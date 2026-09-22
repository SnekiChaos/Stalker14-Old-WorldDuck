using Content.Shared._Stalker_OW.Projectiles.Components;
using Content.Shared.Projectiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._Stalker_OW.Projectiles.Systems;

/// <summary>
/// Handles bolts breaking when they strike something
/// </summary>
public sealed class BoltBreakOnImpactSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly BoltBallisticsSystem _ballistics = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Impact with a wall, mutant, etc
        SubscribeLocalEvent<BoltBreakOnImpactComponent, ProjectileHitEvent>(OnProjectileHit);
    }

    private void OnProjectileHit(
        Entity<BoltBreakOnImpactComponent> ent,
        ref ProjectileHitEvent args)
    {
        // Ballistic calcs stop after impact
        _ballistics.StopFlight(ent.Owner);

        HandleBreakage(ent);
    }

    private void HandleBreakage(Entity<BoltBreakOnImpactComponent> ent)
    {
        if (!_random.Prob(ent.Comp.BreakChance))
            return;

        if (ent.Comp.BrokenPrototype is { } brokenProto)
        {
            var coords = _transform.GetMapCoordinates(ent);
            Spawn(brokenProto, coords);
        }

        QueueDel(ent);
    }
}