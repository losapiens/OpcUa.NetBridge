# [OpcUa.NetBridge](https://www.nuget.org/packages/OpcUa.NetBridge/)
Client for OPC UA Server

## Build with
[OPC UA Foundation](https://github.com/OPCFoundation/UA-.NETStandard) library

> **Note:** Based on the original [OPCUaClient](https://www.nuget.org/packages/OPCUaClient/) project created by [Joc-Luis](https://github.com/joc-luis/OPCUaClient).

## Certificates

Certificate stores (`Application`, `Trusted`, `TrustedPeer`, and `Rejected`) are automatically isolated per `ApplicationName` and stored in a secure, writable location depending on the runtime environment (avoiding permission issues on network shares or protected installation folders):

| Environment | Default Base Path |
| :--- | :--- |
| **Debug** (Debugger attached) | `<AppContext.BaseDirectory>\<AppName>\Certificates` |
| **Desktop Application** (Interactive) | `%LOCALAPPDATA%\OpcUa.NetBridge\<AppName>\Certificates` |
| **Windows Service** (Non-interactive) | `C:\ProgramData\OpcUa.NetBridge\<AppName>\Certificates` *(restricted ACLs)* |

> **Note:** On Windows services, access permissions on the root folder in `C:\ProgramData` are automatically restricted to the service identity and local Administrators to protect private keys. You can also override the default root directory by providing a custom base path in the client configuration.

# TODO...

## How to use 
### Install
```
dotnet add package OpcUa.NetBridge
```
### Import

```cs
 using OpcUa.NetBridge;
```

> **Note:** Asynchronous methods (`*Async`) are the standard and recommended way to interact with the server. All synchronous methods are marked as `[Obsolete]` and will be removed in a future release.

### Getting Started

#### Create a Client Instance
```csharp
// Anonymous authentication
await using var client = new UaClient("test", "opc.tcp://localhost:52240", true, true);

// Username and password authentication
await using var authClient = new UaClient("test", "opc.tcp://localhost:52240", true, true, "admin", "password");
```

#### Connect and Disconnect
```csharp
uint timeOut = 30;
bool keepAlive = true;

// Open a session on the server
await client.ConnectAsync(timeOut, keepAlive);

// Perform read/write operations...

// Explicitly close the session (also handled automatically when disposing the client)
await client.DisconnectAsync();
```

---
### Supported Address Formats

All operations (reading, writing, subscriptions, etc.) support three node address formats:

* **String identifier** (uses the configured default namespace): `"Device.Counter.OK"`
* **Numeric identifier** (uses the configured default namespace): `"1001"`
* **Raw NodeId** (explicit namespace and identifier type): `"ns=2;s=Device.Counter.OK"` or `"ns=2;i=1001"`

---

### Reading Tags

You can read tags either as full `Tag` objects (including metadata such as quality and status code) or directly as strongly-typed values.

#### Read a Full Tag Object
```csharp
Tag tag = await client.ReadAsync("Device.Counter.OK");

Console.WriteLine(\$"Name: {tag.Name}");
Console.WriteLine(\$"Address: {tag.Address}");
Console.WriteLine(\$"Value: {tag.Value}");
Console.WriteLine(\$"Quality: {tag.Quality}");
Console.WriteLine(\$"Code: {tag.Code}");
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

### Read multiple tags

```cs
var address = new List<String>
{
  "Device.Counter.OK",
  "Device.Counter.NG",
  "Device.Counter.Model",
  "Device.Counter.CycleTime"
}
 
 tags await = client.ReadAsync(address);
 
 foreach(var tag in tags)
 {
    Console.WriteLine($"Name: {tag.Name}");
    Console.WriteLine($"Address: {tag.Address}");
    Console.WriteLine($"Value: {tag.Value}");
    Console.WriteLine($"Quality: {tag.Quality}");
    Console.WriteLine($"Code: {tag.Code}");
 }
```

### Write a tag

```cs
 client.Write("Device.Counter.Model", "NewModel");
 //Or
 await client.WriteAsync("Device.Counter.Model", "NewModel");
```


### Write multiple tags

```cs
var tags = new List<Tag>
{
  new Tag {
    Address = "Device.Counter.OK",
    Value = 0,
  },
  new Tag {
    Address = "Device.Counter.NG",
    Value = 0,
  },
  new Tag {
    Address = "Device.Counter.Model",
    Value = "OtherModel",
  },
  new Tag {
    Address = "Device.Counter.CycleTime",
    Value = 10,
  },
}
client.Write(tags);
//Or
 await client.WriteAsync(tags);
```

### Monitoring a tag

```cs
 client.Monitoring("Device.Counter.OK", 500, (_, e) => {
   // Anything you need to be executed when the value changes
 
   // Get the value of the tag being monitored
    var monitored = (MonitoredItemNotification)e.NotificationValue;
    Console.WriteLine(monitored.Value);
 });
```

### Scan OPC UA Server

```cs
 var devices = client.Devices(true);
 //Or
 devices = await client.DevicesAsync(true);
 
 foreach(var device in devices)
 {
    Console.WriteLine($"Name: {device.Name}");
    Console.WriteLine($"Address: {device.Address}");
    Console.WriteLine($"Groups: {device.Groups.Count()}");
    Console.WriteLine($"Tags: {device.Tags.Count()}");
 }
```

### Scan group

```cs
 var groups = client.Group("Device", true);
 //Or
groups = await client.GroupAsync("Device", true); 
 
 foreach(var group in groups)
 {
    Console.WriteLine($"Name: {group.Name}");
    Console.WriteLine($"Address: {group.Address}");
    Console.WriteLine($"Groups: {group.Groups.Count()}");
    Console.WriteLine($"Tags: {group.Tags.Count()}");
 }
```

### Scan an address and recovery the tags

```cs
 var tags = client.Tags("Device.Counter");
 //Or
 tags = await client.TagsAsync("Device.Counter");
 
 foreach(var tag in tags)
 {
    Console.WriteLine($"Name: {tag.Name}");
    Console.WriteLine($"Address: {tag.Address}");
 }
```

### License

[MIT](./LICENSE.md)

Icon for [Freepik](https://www.flaticon.com/authors/freepik)
