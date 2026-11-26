using System.Diagnostics.Metrics;

namespace OpcUa.NetBridge.Tests;

public class Server : OpcTestBase
{
    private const string MyMeterFolder = "MyMeter";
    private const string NumericFolder = $"{MyMeterFolder}.Numeric";
    private const string TestFolder = $"{MyMeterFolder}.Test";

    [Test]
    public void Connection()
    {
        Assert.Pass();
    }

    [Test]
    public async Task DevicesAsync()
    {
        // 1. Non-recursive (default): loads devices, direct tags, and top-level groups only
        var shallowDevices = await Client.DevicesAsync(recursive: false);
        var shallowMyMeter = shallowDevices.FirstOrDefault(d => d.Name == "MyMeter");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(shallowDevices, Has.Count.EqualTo(2));
            Assert.That(shallowMyMeter, Is.Not.Null);
            Assert.That(shallowMyMeter!.Groups, Has.Count.EqualTo(2));
            Assert.That(shallowMyMeter.Groups.SelectMany(g => g.Groups), Is.Empty);
        }
    }

        [Test]
    public async Task DevicesAsync_FullScan()
    {
        var devices = await Client.DevicesAsync(recursive: true);

        var myMeter = devices.FirstOrDefault(d => d.Name == "MyMeter");
        var srvInterface = devices.FirstOrDefault(d => d.Name == "ServerInterface");

        using (Assert.EnterMultipleScope())
        {
            // 1. Finds both custom devices across different namespaces and excludes system "Server" node
            Assert.That(devices, Has.Count.EqualTo(2));
            Assert.That(myMeter, Is.Not.Null, "Expected to find root device 'MyMeter'.");
            Assert.That(srvInterface, Is.Not.Null, "Expected to find root device 'ServerInterface'.");
            Assert.That(devices.Any(d => d.Name.Equals("Server", StringComparison.OrdinalIgnoreCase)), Is.False,
                "System node 'Server' (ns=0) should be excluded.");

            // 2. MyMeter (ns=3): 2 groups (with nested recursion) and 0 direct tags
            Assert.That(myMeter!.Address, Does.StartWith("ns=3;"));
            Assert.That(myMeter.Groups, Has.Count.EqualTo(2));
            Assert.That(myMeter.Tags, Is.Empty);
            Assert.That(myMeter.Groups.First(g => g.Address.EndsWith("MyMeter.Test")).Groups, Has.Count.EqualTo(1));

            // 3. SecondDevice (ns=2): 0 groups and 1 direct tag ("Status")
            Assert.That(srvInterface!.Address, Does.StartWith("ns=4;"));
            Assert.That(srvInterface.Groups, Is.Empty);
            Assert.That(srvInterface.Tags, Has.Count.EqualTo(1));
            Assert.That(srvInterface.Tags[0].Name, Is.EqualTo("Status"));
        }

        // 4. Verify the tag discovered on SecondDevice (ns=2) can be read directly without changing client namespace
        int statusValue = await Client.ReadAsync<int>(srvInterface!.Tags[0].Address);
        Assert.That(statusValue, Is.EqualTo(1));
    }

    [Test]
    public async Task GroupsAsync()
    {
        // 1. Non-recursive: returns the 2 top-level groups with empty child Groups lists
        var groups = await Client.GroupsAsync(MyMeterFolder, recursive: false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(groups, Has.Count.EqualTo(2));
            Assert.That(groups.SelectMany(g => g.Groups), Is.Empty);
        }

        // 2. Recursive: still 2 top-level groups, but the subfolder is populated inside its parent's Groups
        groups = await Client.GroupsAsync(MyMeterFolder, recursive: true);
        var testGroup = groups.First(g => g.Address.EndsWith("Test"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(groups, Has.Count.EqualTo(2));
            Assert.That(testGroup.Groups, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task TagsAsync()
    {
        var tags = await Client.TagsAsync(TestFolder);
        Assert.AreEqual(6, tags.Count);

        tags = await Client.TagsAsync(NumericFolder);
        Assert.AreEqual(1044, tags.Count);
    }
}
