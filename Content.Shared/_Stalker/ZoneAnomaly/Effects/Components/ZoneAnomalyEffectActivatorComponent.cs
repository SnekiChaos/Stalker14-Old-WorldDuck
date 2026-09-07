using Content.Shared.Whitelist;

namespace Content.Shared._Stalker.ZoneAnomaly.Effects.Components;

[RegisterComponent]
public sealed partial class ZoneAnomalyEffectActivatorComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public EntityWhitelist Whitelist = new();

    [DataField]
    public float Distance = 8f;
    
    // ST:OW begin
    // Max number of anomalies that can be activated
    [DataField]
    public int MaxTargets = int.MaxValue;

    // If an anomaly is activated and activates another anomaly,
    // then the second one will not continue the chain to other anomalies
    // NO MORE GARLAND CHAINS!!!
    [DataField]
    public bool StopRecursiveActivation = false;
    // ST:OW end
}
