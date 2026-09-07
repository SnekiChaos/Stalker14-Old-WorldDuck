using System.Numerics;
using Content.Server._Stalker.ApproachTrigger;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Trigger;
using Robust.Shared.Map;
using Robust.Shared.Player; // ST14-EN: Addition
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Server._Stalker_OW.Spawning.Regions; // ST:OW
using Robust.Shared.Maths; // ST:OW
using Content.Shared.Mobs; // ST:OW
using Content.Shared.Doors.Components; // ST:OW

namespace Content.Server._Stalker.SpawnOnApproach;

public sealed class SpawnOnApproachSystem : EntitySystem
{
    [Robust.Shared.IoC.Dependency] private readonly IRobustRandom _random = default!;
    [Robust.Shared.IoC.Dependency] private readonly IGameTiming _timing = default!;
    [Robust.Shared.IoC.Dependency] private readonly TurfSystem _turf = default!;
    [Robust.Shared.IoC.Dependency] private readonly EntityLookupSystem _lookupSystem = default!;

    // ST:OW begin
    private static readonly Vector2i[] CardinalDirections =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1),
    };
    // ST:OW end
    
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpawnOnApproachComponent, TriggerEvent>(OnTrigger);
        SubscribeLocalEvent<SpawnOnApproachComponent, ComponentInit>(OnInit);
    }

    private void OnInit(Entity<SpawnOnApproachComponent> entity, ref ComponentInit args)
    {
        if (_timing.CurTime < entity.Comp.MinStartAction)
            return;
        // Check components with instant spawn
        if (!entity.Comp.InstantSpawn)
            return;

        SpawnWithOffset(entity);
    }

    private void OnTrigger(Entity<SpawnOnApproachComponent> entity, ref TriggerEvent args)
    {
        if (!entity.Comp.Enabled)
            return;

        if (_timing.CurTime < entity.Comp.MinStartAction)
            return;

        SpawnWithOffset(entity);
    }

    private void SpawnWithOffset(Entity<SpawnOnApproachComponent> entity)
    {
        var comp = entity.Comp;
        if (!_random.Prob(Math.Clamp(comp.Chance, 0f, 1f)))
        {
            if (comp.ShouldTimeoutOnRoll)
            {
                comp.CoolDownTime = _timing.CurTime + TimeSpan.FromSeconds(comp.Cooldown);
                comp.Enabled = false;
            }

            return;
        }

        var xform = Transform(entity);
        // ST:OW begin
        var amount = _random.Next(
            comp.MinAmount,
            comp.MaxAmount + 1);

        if (amount > 0)
        {
            var (reachableTiles, fallbackCoords) =
                BuildSpawnSearchArea(
                    xform.Coordinates,
                    comp.MaxOffset);

            for (var i = 0; i < amount; i++)
            {
                if (!TryFindSpawnPosition(
                        entity.Owner,
                        comp,
                        xform.Coordinates,
                        reachableTiles,
                        fallbackCoords,
                        out var spawnCoords))
                {
                    continue;
                }

                var proto = _random.Pick(comp.EntProtoIds);
                Spawn(proto, spawnCoords);
            }
        }
    }
    // ST:OW end

    private EntityCoordinates RandomizeCoords(SpawnOnApproachComponent comp, EntityCoordinates initial)
    {
        // ST14-EN: commented out
        // var offset = _random.NextFloat(comp.MinOffset, comp.MaxOffset);
        // var xOffset = _random.NextFloat(-offset, offset);
        // var yOffset = _random.NextFloat(-offset, offset);
        // return initial.Offset(new Vector2(xOffset, yOffset));

        // ST14-EN fix: Correct formula for uniform distance in [MinOffset, MaxOffset]
        var lightningDistance = comp.MinOffset + _random.NextFloat() * (comp.MaxOffset - comp.MinOffset);
        return initial.Offset(_random.NextAngle().ToVec() * lightningDistance);
    }

    // ST:OW begin
    private bool TryFindRandomSpawn(EntityUid spawner, 
        SpawnOnApproachComponent comp, 
        EntityCoordinates origin, 
        HashSet<Vector2i> reachableTiles, 
        out EntityCoordinates result)
    {
        for (var attempt = 0;
             attempt < comp.MaxSpawnAttempts;
             attempt++)
        {
            var candidate =
                RandomizeCoords(comp, origin);

            var tile =
                _turf.GetTileRef(candidate);

            if (tile == null ||
                !reachableTiles.Contains(
                    tile.Value.GridIndices))
            {
                continue;
            }

            if (!IsValidSpawnPosition(
                    spawner,
                    candidate,
                    comp))
            {
                continue;
            }

            result = candidate;
            return true;
        }

        result = default;
        return false;
    }
    // ST:OW end

    // ST14-EN: Addition
    private bool CheckPlayerNearby(in EntityCoordinates coords, SpawnOnApproachComponent comp)
    {
        if (comp.SpawnNearPlayers)
            return false;

        var actorQuery = GetEntityQuery<ActorComponent>();
        foreach (var uid in _lookupSystem.GetEntitiesInRange(coords, MathF.Max(comp.MinOffset * 0.75f, float.Epsilon) /* a debug assert throws if this is 0 or negative */, flags: LookupFlags.Approximate | LookupFlags.Dynamic))
        {
            if (actorQuery.HasComponent(uid))
                return true;
        }

        return false;
    }
    
    // ST:OW begin
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        var query =
            EntityQueryEnumerator<SpawnOnApproachComponent>();

        while (query.MoveNext(out var uid, out var spawner))
        {
            if (spawner.Enabled)
                continue;

            if (spawner.CoolDownTime > now)
                continue;

            if (TryComp<ApproachTriggerComponent>(uid, out var approach))
            {
                approach.Enabled = true;
            }

            spawner.Enabled = true;
        }
    }
    
    // Use regular system to find spawn spot
    // If that fails then look for nearby floor tiles
    private bool TryFindSpawnPosition(
        EntityUid spawner,
        SpawnOnApproachComponent comp,
        EntityCoordinates origin,
        HashSet<Vector2i> reachableTiles,
        List<EntityCoordinates> fallbackCoords,
        out EntityCoordinates result)
    {
        if (TryFindRandomSpawn(
                spawner,
                comp,
                origin,
                reachableTiles,
                out result))
        {
            return true;
        }

        return TryFindConnectedFallback(
            spawner,
            comp,
            fallbackCoords,
            out result);
    }
    
    // Uses a breadth-first search flood-fill system :D
    // Calculates the valid area around the spawner and returns a set of reachable grids & fallback coordinates
    private (HashSet<Vector2i> ReachableTiles, List<EntityCoordinates> FallbackCoords) BuildSpawnSearchArea(
        EntityCoordinates origin, 
        float maxOffset)
    {
        var reachableTiles = new HashSet<Vector2i>();
        var fallbackCoords = new List<EntityCoordinates>();

        var visited = new HashSet<Vector2i>();
        var queue = new Queue<Vector2i>();

        var start = Vector2i.Zero;

        visited.Add(start);
        queue.Enqueue(start);

        var fallbackMaxSq = maxOffset * maxOffset;
        var searchOffset = maxOffset + 1f;
        var searchMaxSq = searchOffset * searchOffset;

        while (queue.Count > 0)
        {
            var offset = queue.Dequeue();
            var distanceSq = offset.X * offset.X + offset.Y * offset.Y;

            if (distanceSq > searchMaxSq)
                continue;

            var coords = origin.Offset(new Vector2(offset.X, offset.Y));

            if (!TryGetTraversableSpawnTile(coords, out var gridIndices))
                continue;

            reachableTiles.Add(gridIndices);

            if (distanceSq <= fallbackMaxSq)
                fallbackCoords.Add(coords);

            foreach (var direction in CardinalDirections)
            {
                var next = offset + direction;

                if (!visited.Add(next))
                    continue;

                var nextDistanceSq = next.X * next.X + next.Y * next.Y;

                if (nextDistanceSq > searchMaxSq)
                    continue;

                queue.Enqueue(next);
            }
        }

        return (reachableTiles, fallbackCoords);
    }
    
    // Selects a random position from the previously calculated area
    private bool TryFindConnectedFallback(
        EntityUid spawner,
        SpawnOnApproachComponent comp,
        List<EntityCoordinates> fallbackCoords,
        out EntityCoordinates result)
    {
        result = default;
        var validCount = 0;

        foreach (var coords in fallbackCoords)
        {
            if (!IsValidSpawnPosition(spawner, coords, comp))
                continue;

            validCount++;

            if (_random.Next(validCount) == 0)
                result = coords;
        }

        return validCount > 0;
    }
    
    // Determine if the tile is "traversable"
    // AKA not a wall, space, etc.
    private bool TryGetTraversableSpawnTile(
        EntityCoordinates coords,
        out Vector2i gridIndices)
    {
        gridIndices = default;

        var tile = _turf.GetTileRef(coords);

        if (tile == null || tile.Value.Tile.IsEmpty)
            return false;

        var boundaryQuery = GetEntityQuery<STSpawnBoundaryComponent>();
        var doorQuery = GetEntityQuery<DoorComponent>();

        foreach (var uid in _lookupSystem.GetLocalEntitiesIntersecting(tile.Value, 0f))
        {
            if (boundaryQuery.HasComponent(uid) || doorQuery.HasComponent(uid))
                return false;
        }

        if (_turf.IsTileBlocked(tile.Value, CollisionGroup.Impassable))
            return false;

        gridIndices = tile.Value.GridIndices;
        return true;
    }
    
    
    // Checks if an area is a legal spawn spot
    // Considers tiles, collisions, and other entities
    private bool IsValidSpawnPosition(
        EntityUid spawner,
        EntityCoordinates coords,
        SpawnOnApproachComponent comp)
    {
        var tile = _turf.GetTileRef(coords);

        if (tile == null || tile.Value.Tile.IsEmpty)
            return false;

        if (!comp.SpawnInside && _turf.IsTileBlocked(tile.Value, CollisionGroup.Impassable))
            return false;

        var checkRestricted = comp.RestrictedProtos.Count > 0;
        var checkMobs = !comp.SpawnInside;

        if (checkRestricted || checkMobs)
        {
            var metaQuery = GetEntityQuery<MetaDataComponent>();
            var mobQuery = GetEntityQuery<MobStateComponent>();

            foreach (var uid in _lookupSystem.GetLocalEntitiesIntersecting(tile.Value, 0f))
            {
                if (checkRestricted &&
                    metaQuery.TryGetComponent(uid, out var meta) &&
                    meta.EntityPrototype != null &&
                    comp.RestrictedProtos.Contains(meta.EntityPrototype.ID))
                {
                    return false;
                }

                if (checkMobs &&
                    uid != spawner &&
                    mobQuery.TryGetComponent(uid, out var mobState) &&
                    mobState.CurrentState != MobState.Dead)
                {
                    return false;
                }
            }
        }

        if (CheckPlayerNearby(coords, comp))
            return false;

        return true;
    }
    // ST:OW end
}
