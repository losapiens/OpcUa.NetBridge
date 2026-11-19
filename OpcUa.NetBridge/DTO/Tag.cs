using Opc.Ua;

namespace OpcUa.NetBridge.DTO;


/// <summary>
/// Representation in class of a the tag of OPC UA Server
/// </summary>
public class Tag
{

    /// <summary>
    /// Name of the tag
    /// </summary>
    public string Name => Address[(Address.LastIndexOf('.') + 1)..];

    /// <summary>
    /// Address of the tag
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Value of the tag
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// Status code of the tag
    /// </summary>
    public StatusCode Code { get; set; }

    /// <summary>
    /// Quality of the tag
    /// </summary>
    public bool Quality => StatusCode.IsGood(Code);
}
