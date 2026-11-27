# OpcUa.NetBridge
Modern, asynchronous .NET client for OPC UA Servers.

## Built with
[OPC UA Foundation](https://github.com) library

> **Note:** Based on the original [OPCUaClient](https://nuget.org) project created by [Joc-Luis](https://github.com).
>
> **Key Architectural Difference:** Unlike `OPCUaClient`, `OpcUa.NetBridge` completely decouples consumer applications from the underlying `OPCFoundation` SDK types. All operations—including subscription notifications—work exclusively with standard .NET types and the library's own `Tag`, `Group`, and `Device` models, shielding your code from breaking changes in the underlying OPC UA stack.

## Certificates

Certificate stores (`Application`, `Trusted`, `TrustedPeer`, and `Rejected`) are automatically isolated per `ApplicationName` and stored in a secure, writable location depending on the runtime environment (avoiding permission issues on network shares or protected installation folders):

| Environment | Default Base Path |
| :--- | :--- |
| **Debug** (Debugger attached) | `<AppContext.BaseDirectory>\<AppName>\Certificates` |
| **Desktop Application** (Interactive) | `%LOCALAPPDATA%\OpcUa.NetBridge\<AppName>\Certificates` |
| **Windows Service** (Non-interactive) | `C:\ProgramData\OpcUa.NetBridge\<AppName>\Certificates` *(restricted ACLs)* |

> **Note:** On Windows services, access permissions on the root folder in `C:\ProgramData` are automatically restricted to the service identity and local Administrators to protect private keys. You can also override the default root directory by providing a custom base path in the client configuration.

---

## How to Use

### Install
> *NuGet package publishing coming soon. In the meantime, you can reference the project directly or build it from source.*

```bash
dotnet add package OpcUa.NetBridge
```

### Import
```csharp
using OpcUa.NetBridge;
```

> **Note:** Asynchronous methods (`*Async`) are the standard and recommended way to interact with the server. All legacy synchronous methods are marked as `[Obsolete]` and will be removed in a future major release.

---

### Getting Started

#### Create a Client Instance
```csharp
// Anonymous authentication
await using var client = new UaClient("test", "opc.tcp://localhost:52240", true, true);

// Username and password authentication
await using var authClient = new UaClient("test", "opc.tcp://localhost:52240", true, true, "admin", "password");
```

#### Connect, Set Default Namespace, and Disconnect
```csharp
uint timeOut = 30;
bool keepAlive = true;

// Open a session on the server
await client.ConnectAsync(timeOut, keepAlive);

// Optional: set the default namespace index used for shorthand string/numeric addresses
client.ChangeNamespaceIndex(3);

// Perform read/write/monitoring operations...

// Explicitly close the session (also handled automatically when disposing the client)
await client.DisconnectAsync();
```

---

### Supported Address Formats

All operations (reading, writing, subscriptions, browsing) support three node address formats—even mixed together in the same batch call:

* **String identifier** (uses the configured default namespace): `"Device.Counter.OK"`
* **Numeric identifier** (uses the configured default namespace): `"1001"`
* **Raw NodeId** (explicit namespace and identifier type): `"ns=2;s=Device.Counter.OK"` or `"ns=2;i=1001"`

---

### The `Tag` Model

Every read, write, and monitoring operation works with the `Tag` class:

| Property | Type | Description |
| :--- | :--- | :--- |
| **`Address`** | `string` | The node address or full `NodeId` string (e.g., `"Device.Counter.OK"` or `"ns=2;i=1001"`). |
| **`Name`** | `string` | The node's `DisplayName` (when discovered via browsing) or automatically extracted from `Address`. |
| **`Value`** | `object?` | The value read from, written to, or received from the OPC UA node. |
| **`Quality`** | `bool` | `true` if the OPC UA `StatusCode` is `Good`; otherwise `false`. |
| **`Code`** | `StatusCode` | The detailed OPC UA `StatusCode` returned by the server. |
| **`Timestamp`** | `DateTime?` | The UTC timestamp of the value (`SourceTimestamp`, falling back to `ServerTimestamp`). |

---

### Reading Tags

You can read tags either as full `Tag` objects or directly as strongly-typed values.

#### Read a Full Tag Object
```csharp
Tag tag = await client.ReadAsync("Device.Counter.OK");

Console.WriteLine(\$"Name: {tag.Name}");
Console.WriteLine(\$"Address: {tag.Address}");
Console.WriteLine(\$"Value: {tag.Value}");
Console.WriteLine(\$"Quality: {tag.Quality}");
Console.WriteLine(\$"Code: {tag.Code}");
Console.WriteLine(\$"Timestamp: {tag.Timestamp}");
```

#### Read a Strongly-Typed Value
```csharp
// Using a string identifier
int value = await client.ReadAsync<int>("Device.Counter.Number");

// Using a numeric identifier
int numericTag = await client.ReadAsync<int>("1001");

// Using a raw NodeId
int rawTag = await client.ReadAsync<int>("ns=2;i=1001");
```

#### Read Multiple Tags (Batch)
```csharp
string[] addresses =
[
    "Device.Counter.OK",
    "Device.Counter.NG",
    "1001",
    "ns=2;s=Device.Counter.CycleTime"
];

IEnumerable<Tag> tags = await client.ReadAsync(addresses);

foreach (var tag in tags)
{
    Console.WriteLine(\$"Name: {tag.Name} | Address: {tag.Address} | Value: {tag.Value} | Quality: {tag.Quality} | Timestamp: {tag.Timestamp}");
}
```

---

### Writing Tags

#### Write a Single Tag
```csharp
// By address and value
Tag result = await client.WriteAsync("Device.Counter.Model", "NewModel");

// Or using a Tag object
Tag tagToWrite = new Tag { Address = "Device.Counter.Model", Value = "NewModel" };
Tag updatedTag = await client.WriteAsync(tagToWrite);

Console.WriteLine(\$"Write Quality: {result.Quality} (Code: {result.Code})");
```

#### Write Multiple Tags (Batch)
```csharp
Tag[] tags =
[
    new Tag { Address = "Device.Counter.OK", Value = 0 },
    new Tag { Address = "Device.Counter.NG", Value = 0 },
    new Tag { Address = "1001", Value = "OtherModel" },
    new Tag { Address = "ns=2;s=Device.Counter.CycleTime", Value = 10 }
];

Tag[] results = await client.WriteAsync(tags);

foreach (var res in results)
{
    Console.WriteLine(\$"Address: {res.Address} | Quality: {res.Quality} | Code: {res.Code}");
}
```

---

### Monitoring Tags (Subscriptions)

Unlike the original `OPCUaClient`—which exposed raw `MonitoredItemNotification` objects from the OPC Foundation SDK—subscriptions in `OpcUa.NetBridge` emit decoupled `TagEventArgs` notifications carrying a standard `Tag` object (`Address`, `Name`, `Value`, `Quality`, `Code`, and `Timestamp`). Your application never needs to reference `Opc.Ua.*` namespaces directly.

#### Monitor a Single Tag
```csharp
await client.MonitoringAsync("Device.Counter.OK", 500, (_, e) =>
{
    Console.WriteLine(\$"[{e.Tag.Timestamp}] {e.Tag.Name} ({e.Tag.Address}) = {e.Tag.Value} (Quality: {e.Tag.Quality})");
});
```

#### Monitor Multiple Tags (Batch)
```csharp
string[] monitoredAddresses = ["Device.Counter.OK", "1001", "ns=2;s=Device.Counter.CycleTime"];

await client.MonitoringAsync(monitoredAddresses, 500, (_, e) =>
{
    Console.WriteLine(\$"[{e.Tag.Timestamp}] {e.Tag.Name} = {e.Tag.Value}");
});
```

---

### Browsing the Server Address Space

#### Scan Root Devices
```csharp
// Set recursive = true to scan the full hierarchy, or false for top-level groups only
List<Device> devices = await client.DevicesAsync(recursive: true);

foreach (var device in devices)
{
    Console.WriteLine(\$"Device: {device.Name} ({device.Address}) - Groups: {device.Groups.Count}, Direct Tags: {device.Tags.Count}");
}
```

#### Scan Groups Under a Node
```csharp
List<Group> groups = await client.GroupsAsync("Device", recursive: true);

foreach (var group in groups)
{
    Console.WriteLine(\$"Group: {group.Name} ({group.Address}) - Subgroups: {group.Groups.Count}, Tags: {group.Tags.Count}");
}
```

#### Scan Direct Tags Under a Node
```csharp
List<Tag> tags = await client.TagsAsync("Device.Counter");

foreach (var tag in tags)
{
    Console.WriteLine(\$"Tag: {tag.Name} ({tag.Address})");
}
```

---

### License

[MIT](./LICENSE.md)

Icon for [Freepik](https://flaticon.com)
