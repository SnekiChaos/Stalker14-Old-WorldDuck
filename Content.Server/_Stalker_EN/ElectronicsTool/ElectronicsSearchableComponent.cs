namespace Content.Server._Stalker_EN.ElectronicsSearchable;

[RegisterComponent]
public sealed partial class ElectronicsSearchableComponent : Component
{
    public TimeSpan NextSearchTime = TimeSpan.Zero; // ST:OW
    
    [DataField]
    public TimeSpan SearchCooldown = TimeSpan.FromSeconds(900); // ST:OW - 15 minute CD
}
