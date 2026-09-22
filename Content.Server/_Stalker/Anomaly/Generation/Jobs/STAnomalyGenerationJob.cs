using System.Collections.Frozen;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Stalker.Anomaly.Generation.Components;
using Content.Shared._RD.Area;
using Content.Shared._Stalker.Anomaly.Data;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Shared.CPUJob.JobQueues;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Stalker.Anomaly.Generation.Jobs;

public sealed partial class STAnomalyGenerationJob : Job<STAnomalyGenerationJobData>
{
    private static readonly ProtoId<TagPrototype> TagGenerationIntersectionSkip = "STAnomalyGenerationIntersectionSkip";

    [Dependency] private readonly IEntityManager _entityManager = null!;
    [Dependency] private readonly IMapManager _mapManager = null!;
    [Dependency] private readonly IPrototypeManager _prototype = null!;
    [Dependency] private readonly IRobustRandom _random = null!;

    public readonly STAnomalyGenerationOptions Options;

    private readonly EntityLookupSystem _entityLookup;
    private readonly TagSystem _tag;
    private readonly TransformSystem _transform;
    private readonly MapSystem _map;
    private readonly TurfSystem _turf;

    private readonly RDAreaSystem _rdAreas;

    private readonly FrozenDictionary<EntProtoId, int> _anomalySizes;

    // ST:OW begin
    private readonly record struct TileKey(EntityUid Grid, Vector2i Indices);
    private readonly Dictionary<TileKey, TileRef> _tileCoordinates = new();
    private readonly List<TileKey> _spawnTiles = new();
    private readonly Dictionary<TileKey, int> _spawnTileIndices = new();

    private void AddSpawnTile(TileKey key)
    {
        if (!_spawnTileIndices.TryAdd(key, _spawnTiles.Count))
            return;

        _spawnTiles.Add(key);
    }

    private void RemoveSpawnTile(TileKey key)
    {
        if (!_spawnTileIndices.Remove(key, out var index))
            return;

        var lastIndex = _spawnTiles.Count - 1;
        
        if (index != lastIndex)
        {
            var movedKey = _spawnTiles[lastIndex];
            _spawnTiles[index] = movedKey;
            _spawnTileIndices[movedKey] = index;
        }

        _spawnTiles.RemoveAt(lastIndex);
    }
    // ST:OW end

    public STAnomalyGenerationJob(STAnomalyGenerationOptions options,  double maxTime, CancellationToken cancellation = default) : base(maxTime, cancellation)
    {
        Options = options;

        // Include IoC
        IoCManager.InjectDependencies(this);

        // Include entity systems
        _entityLookup = _entityManager.System<EntityLookupSystem>();
        _tag = _entityManager.System<TagSystem>();
        _transform = _entityManager.System<TransformSystem>();
        _map = _entityManager.System<MapSystem>();
        _turf = _entityManager.System<TurfSystem>();

        _rdAreas = _entityManager.System<RDAreaSystem>();

        // Hashing
        _anomalySizes = GetHashAnomalySize();
    }

    private FrozenDictionary<EntProtoId, int> GetHashAnomalySize()
    {
        var dictionary = new Dictionary<EntProtoId, int>();

        foreach (var anomalyEntry in Options.AnomalyEntries)
        {
            var entityPrototype = _prototype.Index<EntityPrototype>(anomalyEntry.ProtoId);
            var componentRegistry = entityPrototype.Components;

            if (!componentRegistry.TryGetComponent("Fixtures", out var component))
                continue;

            if (component is not FixturesComponent fixturesComponent)
                continue;

            if (!fixturesComponent.Fixtures.TryGetValue("fix1", out var fixture))
                continue;

            // Here we get the radius of the anomaly, we need to subtract 0.5,
            // which would not take into account the skeleton of the anomaly itself,
            // in fact we turn the volumetric object into an abstract point.
            var size = int.Max((int) float.Round(fixture.Shape.Radius) - 1, 0);

            dictionary.Add(anomalyEntry.ProtoId, size);
        }

        return dictionary.ToFrozenDictionary();
    }

    protected override async Task<STAnomalyGenerationJobData?> Process()
    {
        var result = new STAnomalyGenerationJobData();

        // ST:OW begin
        // Do not scan the map if no anomaly spawns are possible
        if (Options.TotalCount <= 0 || Options.AnomalyEntries.Count == 0)
            return result;
        
        await LoadTiles();
        await RemoveByBlockers();

        for (var i = 0; i < Options.TotalCount; i++)
        {
            // Potential spawn locations shrink as anomalies are placed and blockers are applied
            // So once there are no more valid spots, spawn in all the anomalies available
            if (_spawnTiles.Count == 0)
                break;

            var anomaly = GetRandomAnomalyEntry(Options, _random);
            if (anomaly is null)
                continue;

            var entry = anomaly.Value;
            var radius = _anomalySizes[entry.ProtoId];

            // Bound amount of work spent trying to place an anomaly
            // Prevents infinite looping
            for (var attempt = 0; attempt < 100; attempt++)
            {
                await MakeOperation();

                if (_spawnTiles.Count == 0)
                    break;

                var key = _spawnTiles[_random.Next(_spawnTiles.Count)];
                var entity = await TrySpawn(entry, key);

                if (entity == EntityUid.Invalid)
                    continue;

                result.SpawnedAnomalies.Add(entity);

                // Occupied tiles are not considered valid for subsequent anomaly spawns
                _tileCoordinates.Remove(key);

                // Prevent anomalies from spawning within another anomaly's occupied radius
                for (var x = key.Indices.X - radius;
                     x <= key.Indices.X + radius;
                     x++)
                {
                    for (var y = key.Indices.Y - radius;
                         y <= key.Indices.Y + radius;
                         y++)
                    {
                        await MakeOperation();

                        RemoveSpawnTile(
                            new TileKey(key.Grid, new Vector2i(x, y)));
                    }
                }
                // ST:OW end
                break;
            }
        }

        return result;
    }

    // ST:OW begin
    private async Task RemoveByBlockers()
    {
        var blockerAreas = new List<(EntityUid Grid, Box2i Bounds)>();

        var entities = _entityManager.AllEntityQueryEnumerator<
            STAnomalyGeneratorSpawnBlockerComponent,
            TransformComponent>();

        while (entities.MoveNext(out var uid, out var blocker, out var transform))
        {
            if (_transform.GetMapId(uid) != Options.MapId)
                continue;

            var tile = _turf.GetTileRef(transform.Coordinates);
            if (tile is null)
                continue;

            var coordinates = tile.Value.GridIndices;
            var size = blocker.Size;

            var bounds = new Box2i(
                coordinates.X - size,
                coordinates.Y - size,
                coordinates.X + size + 1,
                coordinates.Y + size + 1);

            blockerAreas.Add((tile.Value.GridUid, bounds));
        }

        foreach (var (grid, bounds) in blockerAreas)
        {
            for (var x = bounds.Left; x < bounds.Right; x++)
            {
                for (var y = bounds.Bottom; y < bounds.Top; y++)
                {
                    await MakeOperation();

                    var key = new TileKey(grid, new Vector2i(x, y));
                    
                    _tileCoordinates.Remove(key);
                    RemoveSpawnTile(key);
                }
            }
        }
    }

    private async Task LoadTiles()
    {
        var gridList = _mapManager.GetAllGrids(Options.MapId).ToList();

        Func<EntityUid, bool> skipIntersection =
            uid => _tag.HasTag(uid, TagGenerationIntersectionSkip);

        foreach (var grid in gridList)
        {
            var allTiles = _map.GetAllTiles(grid, grid).ToList();

            foreach (var tileRef in allTiles)
            {
                await MakeOperation();

                if (_rdAreas.TryGetArea(
                        grid.Owner,
                        tileRef.GridIndices,
                        out Entity<RDAreaComponent> areaUid) &&
                    _entityManager.HasComponent<
                        STAnomalyGeneratorSpawnBlockerComponent>(areaUid))
                {
                    continue;
                }

                var key = new TileKey(tileRef.GridUid, tileRef.GridIndices);

                if (TileSolidAndNotBlocked(tileRef))
                {
                    _tileCoordinates.TryAdd(key, tileRef);
                    AddSpawnTile(key);
                    
                    continue;
                }
                
                if (TileSolidAndNotBlocked(tileRef, skipIntersection))
                    _tileCoordinates.TryAdd(key, tileRef);
            }
            // ST:OW end
        }
    }

    private bool TileSolidAndNotBlocked(TileRef tile, Func<EntityUid, bool>? predicate = null)
    {
        return _turf.GetContentTileDefinition(tile).Sturdy && !IsTileBlocked(tile, CollisionGroup.LowImpassable, predicate: predicate);
    }

    private static STAnomalyGeneratorAnomalyEntry? GetRandomAnomalyEntry(STAnomalyGenerationOptions options, IRobustRandom random)
    {
        var sumRation = 0f;
        foreach (var entry in options.AnomalyEntries)
        {
            sumRation += entry.Weight;
        }

        var roll = random.NextFloat(0, sumRation);

        sumRation = 0f;
        foreach (var entry in options.AnomalyEntries)
        {
            sumRation += entry.Weight;
            if (roll <= sumRation)
                return entry;
        }

        return options.AnomalyEntries.LastOrDefault();
    }
}
