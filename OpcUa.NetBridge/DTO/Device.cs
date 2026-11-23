namespace OpcUa.NetBridge.DTO;


/// <summary>
/// Device on the OPC UA Server
/// </summary>
public class Device
{
    private string _name = string.Empty;

    /// <summary>
    /// Name of the device
    /// </summary>
    public String Name 
    { 
        get
        {
            // Se il nome è stato valorizzato esplicitamente (es. dal DisplayName del Browse), usa quello
            if (!string.IsNullOrWhiteSpace(_name))
                return _name;

            if (string.IsNullOrWhiteSpace(Address))
                return string.Empty;

            // Caso 1: È un NodeID standard (contiene ns= o i= o s=)
            if (Address.Contains("ns=") || Address.Contains("i=") || Address.Contains("s="))
            {
                // Estrae la parte identificativa dopo 's=' o 'i='
                int sIndex = Address.LastIndexOf("s=");
                if (sIndex != -1) return Address.Substring(sIndex + 2);

                int iIndex = Address.LastIndexOf("i=");
                if (iIndex != -1) return Address.Substring(iIndex + 2);

                return Address; // Fallback estremo per NodeID complessi
            }

            // Caso 2: Vecchio formato a punti (es. "Linea1.Stazione2.DeviceA")
            int lastDot = Address.LastIndexOf('.');
            if (lastDot == -1 || lastDot == Address.Length - 1)
                return Address;

            return Address.Substring(lastDot + 1);
        }
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
