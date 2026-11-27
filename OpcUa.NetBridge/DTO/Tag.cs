using Opc.Ua;

namespace OpcUa.NetBridge.DTO;


/// <summary>
/// Representation in class of a the tag of OPC UA Server
/// </summary>
public class Tag
{
    private string? _name;

    /// <summary>
    /// Name of the tag
    /// </summary>
    public string Name
    {
        get => !string.IsNullOrEmpty(_name) ? _name : Tag.ExtractNameFromAddress(Address);
        set => _name = value;
    }

    /// <summary>
    /// Address of the tag
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of the tag (source or server timestamp)
    /// </summary>
    public DateTime? Timestamp { get; set; }

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

    internal static string ExtractNameFromAddress(string? address)
    {
        if (string.IsNullOrEmpty(address))
            return string.Empty;

        int dotIndex = address.LastIndexOf('.');
        if (dotIndex >= 0 && dotIndex < address.Length - 1)
            return address[(dotIndex + 1)..].Trim('"');

        // Handle raw NodeIds without dots (e.g., "ns=3;s=Status" -> "Status", "ns=2;i=1001" -> "1001")
        int equalsIndex = address.LastIndexOf('=');
        if (equalsIndex >= 0 && equalsIndex < address.Length - 1)
            return address[(equalsIndex + 1)..].Trim('"');

        return address.Trim('"');
    }
}
