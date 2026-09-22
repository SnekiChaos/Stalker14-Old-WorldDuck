namespace Content.Server._Stalker.TrashSerchable;

[RegisterComponent]
public sealed partial class TrashSerchableComponent : Component
{
    public TimeSpan NextSearchTime = TimeSpan.Zero; // ST:OW

    [DataField]
    public TimeSpan SearchCooldown = TimeSpan.FromSeconds(900); // ST:OW - 15 minute CD
}
