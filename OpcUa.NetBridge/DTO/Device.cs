namespace OpcUa.NetBridge.DTO;


/// <summary>
/// Device on the OPC UA Server
/// </summary>
public class Device
{
    private string? _name;

    /// <summary>
    /// Name of the device
    /// </summary>
    public string Name
    {
        get => !string.IsNullOrEmpty(_name) ? _name : Tag.ExtractNameFromAddress(Address);
        set => _name = value;
    }

    /// <summary>
    /// Address of the device
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Groups into the device <see cref="Group"/>
    /// </summary>
    public List<Group> Groups { get; set; } = [];

    /// <summary>
    /// Tags into the device <see cref="Tag"/>
    /// </summary>
    public List<Tag> Tags { get; set; } = [];
}
