using System.Threading.Tasks;
using Content.Shared._Stalker.Anomaly.Data;
using Robust.Shared.Map.Components;

namespace Content.Server._Stalker.Anomaly.Generation.Jobs;

public sealed partial class STAnomalyGenerationJob
{
    // ST:OW begin
    private async Task<EntityUid> TrySpawn(
        STAnomalyGeneratorAnomalyEntry anomalyEntry,
        TileKey key)
    {
        if (!_tileCoordinates.TryGetValue(key, out var tileRef))
            return EntityUid.Invalid;

        if (!await PlaceFree(anomalyEntry, key))
            return EntityUid.Invalid;
    
        if (!_entityManager.TryGetComponent<MapGridComponent>(
                key.Grid,
                out var gridComp))
        {
            return EntityUid.Invalid;
        }

        var targetCoords = _map.GridTileToWorld(
            key.Grid,
            gridComp,
            tileRef.GridIndices);

        return _entityManager.Spawn(anomalyEntry.ProtoId, targetCoords);
    }

    private async Task<bool> PlaceFree(
        STAnomalyGeneratorAnomalyEntry anomalyEntry,
        TileKey key)
    {
        var radius = _anomalySizes[anomalyEntry.ProtoId];
        
        for (var x = key.Indices.X - radius;
             x <= key.Indices.X + radius;
             x++)
        {
            for (var y = key.Indices.Y - radius;
                 y <= key.Indices.Y + radius;
                 y++)
            {
                await MakeOperation();

                var footprintKey =
                    new TileKey(key.Grid, new Vector2i(x, y));

                if (!_tileCoordinates.ContainsKey(footprintKey))
                    return false;
            }
        }

        return true;
    }
}
// ST:OW end