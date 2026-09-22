using System.Numerics;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Content.Shared._Stalker.Anomaly.Prototypes;
using Content.Shared._Stalker.Anomaly.Data;

namespace Content.Server._Stalker.Anomaly.Generation.Jobs;

public sealed partial class STAnomalyGenerationJob
{
    #region Turf

    private bool IsTileBlocked(TileRef turf, CollisionGroup mask, Func<EntityUid, bool>? predicate = null, float minIntersectionArea = 0.1f)
    {
        return IsTileBlocked(turf.GridUid, turf.GridIndices, mask, predicate: predicate, minIntersectionArea: minIntersectionArea);
    }

    private bool IsTileBlocked(EntityUid gridUid,
        Vector2i indices,
        CollisionGroup mask,
        Func<EntityUid, bool>? predicate = null,
        MapGridComponent? grid = null,
        TransformComponent? gridXform = null,
        float minIntersectionArea = 0.1f)
    {
        if (!_entityManager.TryGetComponent(gridUid, out grid))
            return false;

        if (!_entityManager.TryGetComponent(gridUid, out gridXform))
            return false;

        var xformQuery = _entityManager.GetEntityQuery<TransformComponent>();
        var (gridPos, gridRot, matrix) = _transform.GetWorldPositionRotationMatrix(gridXform, xformQuery);

        var size = grid.TileSize;
        var localPos = new Vector2(indices.X * size + size / 2f, indices.Y * size + size / 2f);
        var worldPos = Vector2.Transform(localPos, matrix);

        // This is scaled to 95 % so it doesn't encompass walls on other tiles.
        var tileAabb = Box2.UnitCentered.Scale(0.95f * size);
        var worldBox = new Box2Rotated(tileAabb.Translated(worldPos), gridRot, worldPos);
        tileAabb = tileAabb.Translated(localPos);

        var intersectionArea = 0f;
        var fixtureQuery = _entityManager.GetEntityQuery<FixturesComponent>();

        predicate ??= _ => false;

        foreach (var ent in _entityLookup.GetEntitiesIntersecting(gridUid, worldBox, LookupFlags.Dynamic | LookupFlags.Static))
        {
            if (predicate.Invoke(ent))
                continue;

            if (!fixtureQuery.TryGetComponent(ent, out var fixtures))
                continue;

            // get grid local coordinates
            var (pos, rot) = _transform.GetWorldPositionRotation(xformQuery.GetComponent(ent), xformQuery);
            rot -= gridRot;
            pos = (-gridRot).RotateVec(pos - gridPos);

            var xform = new Transform(pos, (float) rot.Theta);

            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (!fixture.Hard)
                    continue;

                if ((fixture.CollisionLayer & (int) mask) == 0)
                    continue;

                for (var i = 0; i < fixture.Shape.ChildCount; i++)
                {
                    var intersection = fixture.Shape.ComputeAABB(xform, i).Intersect(tileAabb);
                    intersectionArea += intersection.Width * intersection.Height;
                    if (intersectionArea > minIntersectionArea)
                        return true;
                }
            }
        }

        return false;
    }

    #endregion
}
