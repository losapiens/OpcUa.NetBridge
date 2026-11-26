namespace OpcUa.NetBridge.Tests;

public abstract class OpcTestBase
{
    // Protected allows derived classes to access the connected client instance directly
    protected UaClient Client { get; private set; }

    private string _serverUrl = "opc.tcp://localhost:48400";
    private readonly ushort _namespaceIndex = 3;

    [SetUp]
    public async Task BaseInit()
    {
        _serverUrl = OpcServerFixture.ServerUrl;
        Client = new UaClient("NUnitTestServer", _serverUrl, _namespaceIndex, false, true);
        await Client.ConnectAsync(30);
    }

    [TearDown]
    public async Task BaseCleanup()
    {
        await Client.DisconnectAsync();
        await Client.DisposeAsync();
    }
}
