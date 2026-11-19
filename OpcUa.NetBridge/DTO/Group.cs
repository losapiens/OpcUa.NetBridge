namespace OpcUa.NetBridge.DTO;


/// <summary>
/// Group of tags.
/// </summary>
public class Group
{

    /// <summary>
    /// Name of the group
    /// </summary>
    public string Name => Address[(Address.LastIndexOf('.') + 1)..];

    /// <summary>
    /// Address of the group
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Groups into the group <see cref="Group"/>
    /// </summary>
    public List<Group> Groups { get; set; } = [];

    /// <summary>
    /// Tags into the group <see cref="Tag"/>
    /// </summary>
    public List<Tag> Tags { get; set; } = [];
}
