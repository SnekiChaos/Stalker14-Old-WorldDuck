using Content.Shared._Stalker.ZoneAnomaly.Components;
using Content.Shared._Stalker.ZoneAnomaly.Effects.Components;
using Content.Shared.Whitelist;

namespace Content.Shared._Stalker.ZoneAnomaly.Effects.Systems;

public sealed class ZoneAnomalyEffectActivatorSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly ZoneAnomalySystem _anomalySystem = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ZoneAnomalyEffectActivatorComponent, ZoneAnomalyActivateEvent>(OnActivate);
    }
    // ST:OW begin
    private void OnActivate(Entity<ZoneAnomalyEffectActivatorComponent> effect, ref ZoneAnomalyActivateEvent args)
    {
        var comp = effect.Comp;

        if (comp.StopRecursiveActivation)
        {
            foreach (var trigger in args.Triggers)
            {
                if (HasComp<ZoneAnomalyEffectActivatorComponent>(trigger))
                    return;
            }
        }

        var maxTargets = comp.MaxTargets;
        var whitelist = comp.Whitelist;
        var owner = effect.Owner;

        var entities = _lookup.GetEntitiesInRange(
            Transform(effect).Coordinates,
            comp.Distance);

        var activated = 0;

        foreach (var entity in entities)
        {
            if (maxTargets > 0 && activated >= maxTargets)
                break;

            if (!_whitelist.IsWhitelistPass(whitelist, entity))
                continue;

            if (!TryComp<ZoneAnomalyComponent>(entity, out var anomaly))
                continue;
        
            if (_anomalySystem.TryActivate((entity, anomaly), owner))
                activated++;
        }
    }
    // ST:OW end
}
