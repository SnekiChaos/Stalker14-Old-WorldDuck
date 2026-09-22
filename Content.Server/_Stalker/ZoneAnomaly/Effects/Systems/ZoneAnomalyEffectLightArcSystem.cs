using Content.Server.Lightning;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Stalker.ZoneAnomaly.Components;
using Content.Shared._Stalker.ZoneAnomaly.Effects.Components;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Whitelist;
using Robust.Shared.Map.Components;

namespace Content.Server._Stalker.ZoneAnomaly.Effects.Systems;

public sealed class ZoneAnomalyEffectLightArcSystem : EntitySystem
{
    [Dependency] private readonly PredictedBatterySystem _battery = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly LightningSystem _lightning = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ZoneAnomalyEffectLightArcComponent, ZoneAnomalyActivateEvent>(OnActivate);
    }

    private void OnActivate(Entity<ZoneAnomalyEffectLightArcComponent> effect, ref ZoneAnomalyActivateEvent args)
    {
        // ST:OW begin
        foreach (var trigger in args.Triggers)
        {
            if (HasComp<ZoneAnomalyEffectActivatorComponent>(trigger))
                _lightning.ShootLightning(effect, trigger, effect.Comp.Lighting);
        }
        
        var targetCount = 0;
        var maxTargets = effect.Comp.MaxTargets;
        // Seek out things in range to arc to
        var entities = _lookup.GetEntitiesInRange(Transform(effect).Coordinates, effect.Comp.Distance);

        foreach (var entity in entities)
        {
            // Only target the max number of targets this pulse
            if (maxTargets > 0 && targetCount >= maxTargets)
                break;

            // Skip over non-whitelisted entities and prevent multi-firing on nested entities
            // e.g. Hit the player and not all the items they're wearing
            if (!_whitelistSystem.IsWhitelistPass(effect.Comp.Whitelist, entity) || IsValidRecursively(effect, entity))
                continue;
            // ST:OW end

            TryRecharge(effect, entity);
            _lightning.ShootLightning(effect, entity, effect.Comp.Lighting);

            targetCount++;
        }
    }

    private void TryRecharge(Entity<ZoneAnomalyEffectLightArcComponent> effect, EntityUid target)
    {
        if (!TryComp<PredictedBatteryComponent>(target, out var battery))
            return;

        _battery.SetCharge((target, battery), battery.LastCharge + battery.MaxCharge * effect.Comp.ChargePercent);
    }

    private bool IsValidRecursively(Entity<ZoneAnomalyEffectLightArcComponent> effect, EntityUid uid)
    {
        var parent = Transform(uid).ParentUid;
        if (HasComp<MapComponent>(parent) || HasComp<MapGridComponent>(parent))
            return false;

        return _whitelistSystem.IsWhitelistPass(effect.Comp.Whitelist, parent) || IsValidRecursively(effect, parent);
    }
}
