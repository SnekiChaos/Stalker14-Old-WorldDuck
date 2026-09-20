namespace Content.Server._Stalker_OW.Food;

/// <summary>
/// Marks a stack as a batch of curing items
/// </summary>
[RegisterComponent]
public sealed partial class STOWCuringStackComponent : Component
{
    /// <summary>
    /// Full curing duration in seconds
    /// </summary>
    [DataField(required: true)]
    public float Duration;
}