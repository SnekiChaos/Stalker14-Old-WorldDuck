using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Shared._Stalker.ZoneAnomaly.Effects.Components;

[RegisterComponent]
public sealed partial class ZoneAnomalyEffectLightArcComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public EntityWhitelist Whitelist = new();

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public EntProtoId Lighting = "SourceLightning";

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float ChargePercent = 0.2f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Distance = 6f;
    
    [DataField, ViewVariables(VVAccess.ReadWrite)] // ST:OW
    public int MaxTargets = 13; // ST:OW - Default number that's already implemented
}
