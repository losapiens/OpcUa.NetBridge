using System.Collections.Concurrent;

namespace OpcUa.NetBridge.Tests;

public class ReadWrite : OpcTestBase
{
    private const string TestFolder = "MyMeter.Test";
    private const ushort NumericNamespaceIndex = 2;
    private const ushort NamespaceIndex = 3;

    private const string boolAddress = $"{TestFolder}.Boolean";
    private const string uintAddress = $"{TestFolder}.UInteger";
    const string intAddress = $"{TestFolder}.Integer";
    const string doubleAddress = $"{TestFolder}.Double";
    const string floatAddress = $"{TestFolder}.Float";
    const string stringAddress = $"{TestFolder}.String";

    //[Test]
    //public void Boolean()
    //{
    //    Client.Write(boolAddress, true);
    //    var tag = Client.Read(boolAddress);
    //    Assert.That(tag.Value, Is.EqualTo(true));

    //    Client.Write(boolAddress, false);
    //    tag = Client.Read(boolAddress);
    //    Assert.That(tag.Value, Is.EqualTo(false));
    //}

    //[Test]
    //public void UInteger()
    //{
    //    uint expected = 12337u;

    //    Client.Write(uintAddress, expected);
    //    uint value = Client.Read<uint>(uintAddress);

    //    Assert.That(value, Is.EqualTo(expected));
    //}
    
    //[Test]
    //public void Integer()
    //{
    //    int value = new Random().Next(int.MinValue, int.MaxValue);
    //    Client.Write(intAddress, value);
    //    var tag = Client.Read(intAddress);
    //    Assert.AreEqual(value, tag.Value);

    //    value = new Random().Next(int.MinValue, int.MaxValue);
    //    Client.Write(intAddress, value);
    //    tag = Client.Read(intAddress);
    //    int read = Client.Read<int>(intAddress);
    //    Assert.AreEqual(value, tag.Value);
    //    Assert.AreEqual(value, read);
    //}

    //[Test]
    //public void Double()
    //{
    //    double value = new Random().NextDouble();
    //    Client.Write(doubleAddress, value);
    //    var tag = Client.Read(doubleAddress);
    //    Assert.AreEqual(value, tag.Value);

    //    value = new Random().NextDouble();
    //    Client.Write(doubleAddress, value);
    //    tag = Client.Read(doubleAddress);
    //    double read = Client.Read<double>(doubleAddress);
    //    Assert.AreEqual(value, tag.Value);
    //    Assert.AreEqual(value, read);
    //}

    //[Test]
    //public void Float()
    //{
    //    float value = new Random().NextSingle();
    //    Client.Write(floatAddress, value);
    //    var tag = Client.Read(floatAddress);
    //    Assert.AreEqual(value, tag.Value);
        
    //    value = new Random().NextSingle();
    //    Client.Write(floatAddress, value);
    //    tag = Client.Read(floatAddress);
    //    float read = Client.Read<float>(floatAddress);
    //    Assert.AreEqual(value, tag.Value);
    //    Assert.AreEqual(value, read);
    //}

    //[Test]
    //public void String()
    //{
    //    string value = "";

    //    while (value.Length < 10)
    //    {
    //        value += (char)new Random().Next(32, 166);
    //    }

    //    Client.Write(stringAddress, value);
    //    var tag = Client.Read(stringAddress);
    //    Assert.AreEqual(value, tag.Value);

    //    while (value.Length < 10)
    //    {
    //        value += (char)new Random().Next(32, 166);
    //    }
    //    Client.Write(stringAddress, value);
    //    tag = Client.Read(stringAddress);
    //    string read = Client.Read<string>(stringAddress);
    //    Assert.AreEqual(value, tag.Value);
    //    Assert.AreEqual(value, read);
    //}


    //[Test]
    //public void Multiple()
    //{
    //    List<Tag> values =
    //    [
    //        new Tag
    //        {
    //            Address = boolAddress,
    //            Value = new Random().Next(0, 2) == 1
    //        },
    //        new Tag
    //        {
    //            Address = doubleAddress,
    //            Value = new Random().NextDouble()
    //        },
    //        new Tag
    //        {
    //            Address = floatAddress,
    //            Value = new Random().NextSingle()
    //        },
    //        new Tag
    //        {
    //            Address = intAddress,
    //            Value = new Random().Next(int.MinValue, int.MaxValue)
    //        },
    //        new Tag
    //        {
    //            Address = stringAddress,
    //            Value = "Hello, World!"
    //        }
    //    ];

    //    Client.Write(values);

    //    var read = Client.Read(values.Select(v => v.Address).ToList());
    //    Assert.AreEqual(values.Count, read.Count);

    //    for (int i = 0; i < read.Count; i++)
    //    {
    //        Assert.AreEqual(values[i].Value, read[i].Value);
    //    }
    //}


    [Test]
    public async Task BooleanAsync()
    {
        string rawStringAddress = $"ns={NamespaceIndex};s={boolAddress}";
        const string numericAddress = "1001";
        string rawNumericAddress = $"ns={NumericNamespaceIndex};i={numericAddress}";

        // 1. Write TRUE to the string node and verify all 4 read formats return TRUE
        await Client.WriteAsync(boolAddress, true);
        var tag = await Client.ReadAsync(boolAddress);

        var res1 = await Client.ReadAsync<bool>(boolAddress);
        var res2 = await Client.ReadAsync<bool>(rawStringAddress);

        bool res3;
        try
        {
            Client.ChangeNamespaceIndex(NumericNamespaceIndex);
            res3 = await Client.ReadAsync<bool>(numericAddress);
        }
        finally
        {
            Client.ChangeNamespaceIndex(NamespaceIndex);
        }

        var res4 = await Client.ReadAsync<bool>(rawNumericAddress);

        Assert.Multiple(() =>
        {
            Assert.That(tag.Value, Is.EqualTo(true));
            Assert.That(res1, Is.True);
            Assert.That(res2, Is.True);
            Assert.That(res3, Is.True);
            Assert.That(res4, Is.True);
        });

        // 2. Reverse write: write FALSE to the numeric node and verify the string node updates to FALSE
        await Client.WriteAsync(rawNumericAddress, false);

        tag = await Client.ReadAsync(boolAddress);
        var updatedStringVal = await Client.ReadAsync<bool>(boolAddress);
        var updatedNumericVal = await Client.ReadAsync<bool>(rawNumericAddress);

        Assert.Multiple(() =>
        {
            Assert.That(tag.Value, Is.EqualTo(false));
            Assert.That(updatedStringVal, Is.False);
            Assert.That(updatedNumericVal, Is.False);
        });
    }

    [Test]
    public async Task UIntegerAsync()
    {
        uint value = (uint)Random.Shared.NextInt64(0, (long)uint.MaxValue + 1);
        await Client.WriteAsync(uintAddress, value);

        var tag = await Client.ReadAsync(uintAddress);
        Assert.AreEqual(value, tag.Value);
    }
    
    [Test]
    public async Task IntegerAsync()
    {
        int value = new Random().Next(int.MinValue, int.MaxValue);
        await Client.WriteAsync(intAddress, value);
        var tag = await Client.ReadAsync(intAddress);
        Assert.AreEqual(value, tag.Value);

        value = new Random().Next(int.MinValue, int.MaxValue);
        await Client.WriteAsync(intAddress, value);
        tag = await Client.ReadAsync(intAddress);
        Assert.AreEqual(value, tag.Value);
    }

    [Test]
    public async Task DoubleAsync()
    {
        double value = new Random().NextDouble();
        await Client.WriteAsync(doubleAddress, value);
        var tag = await Client.ReadAsync(doubleAddress);
        Assert.AreEqual(value, tag.Value);

        value = new Random().NextDouble();
        await Client.WriteAsync(doubleAddress, value);
        tag = await Client.ReadAsync(doubleAddress);
        Assert.AreEqual(value, tag.Value);
    }

    [Test]
    public async Task FloatAsync()
    {
        float value = new Random().NextSingle();
        await Client.WriteAsync(floatAddress, value);
        var tag = await Client.ReadAsync(floatAddress);
        Assert.AreEqual(value, tag.Value);

        value = new Random().NextSingle();
        await Client.WriteAsync(floatAddress, value);
        tag = await Client.ReadAsync(floatAddress);
        Assert.AreEqual(value, tag.Value);
    }

    [Test]
    public async Task StringAsync()
    {
        string value = "Hello World";

        await Client.WriteAsync(stringAddress, value);
        var tag = await Client.ReadAsync(stringAddress);
        Assert.That(tag.Value, Is.EqualTo(value));
    }


    [Test]
    public async Task MultipleAsync()
    {
        List<Tag> values =
        [
            new Tag
            {
                Address = $"{TestFolder}.Boolean",
                Value = new Random().Next(0, 2) == 1
            },
            new Tag
            {
                Address = $"{TestFolder}.Double",
                Value = new Random().NextDouble()
            },
            new Tag
            {
                Address = $"{TestFolder}.Float",
                Value = new Random().NextSingle()
            },
            new Tag
            {
                Address = $"{TestFolder}.Integer",
                Value = new Random().Next(int.MinValue, int.MaxValue)
            },
            new Tag
            {
                Address = $"{TestFolder}.String",
                Value = "Hello, World!"
            }
        ];

        var result = await Client.WriteAsync(values);
        Assert.That(result, Has.All.Property(nameof(Tag.Quality)).True);

        var read = await Client.ReadAsync(values.Select(v => v.Address));
        Assert.AreEqual(values.Count, read.Count());
        Assert.That(read.Select(t => t.Value), Is.EqualTo(values.Select(v => v.Value)));
    }

    [Test]
    public async Task MultipleFailAsync()
    {
        List<Tag> values = [];

        for (int i = 0; i < 1000; i++)
        {
            values.Add(new Tag
            {
                Address = $"{TestFolder}.Tag{i:D3}",
                Value = "Hello"
            });
        }

        var tags = await Client.WriteAsync(values);

        var tag = tags.First(t => t.Name == "Tag989");
        Assert.AreEqual(tag.Quality, false);
    }

    [Test]
    public async Task MonitoringAsync()
    {
        const string address = $"{TestFolder}.Integer";

        const int initialValue = 10;
        const int updatedValue = 42;

        // 1. Set a known initial value before starting monitoring
        await Client.WriteAsync(address, initialValue);

        var receivedValues = new List<int>();
        var updatedValueReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 2. Start monitoring at 100ms interval
        await Client.MonitoringAsync(address, 100, (_, e) =>
        {
            if (e.Tag.Value is int val)
            {
                receivedValues.Add(val);
                if (val == updatedValue)
                {
                    updatedValueReceived.TrySetResult(true);
                }
            }
        });

        // 3. Trigger a change on the server
        await Client.WriteAsync(address, updatedValue);

        // 4. Wait deterministically (with a 3-second safety timeout) for the callback to fire
        var completedTask = await Task.WhenAny(updatedValueReceived.Task, Task.Delay(TimeSpan.FromSeconds(3)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(completedTask, Is.EqualTo(updatedValueReceived.Task), "Timed out waiting for monitoring notification.");
            Assert.That(receivedValues, Does.Contain(updatedValue));
        }
    }

    [Test]
    public async Task MonitoringMultiAsync()
    {
        string boolNumericAddress = $"ns={NumericNamespaceIndex};i=1001"; // Boolean linked node
        string intNumericAddress = $"ns={NumericNamespaceIndex};i=1003"; // Integer linked node

        await Client.WriteAsync(boolNumericAddress, false);
        await Client.WriteAsync(intAddress, 100);

        var receivedTags = new ConcurrentDictionary<string, object?>();
        var allUpdated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Subscribe to both a string address and a raw numeric address in a single batch call
        await Client.MonitoringAsync([boolAddress, intNumericAddress], 100, (_, e) =>
        {
            receivedTags[e.Tag.Address] = e.Tag.Value;

            if (receivedTags.TryGetValue(boolAddress, out var boolVal) && boolVal is true &&
                receivedTags.TryGetValue(intNumericAddress, out var intVal) && intVal is 999)
            {
                allUpdated.TrySetResult(true);
            }
        });

        // Write new values to both nodes
        await Client.WriteAsync(boolAddress, true);
        await Client.WriteAsync(intAddress, 999);

        var completedTask = await Task.WhenAny(allUpdated.Task, Task.Delay(TimeSpan.FromSeconds(3)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(completedTask, Is.EqualTo(allUpdated.Task), "Timed out waiting for batch monitoring notifications.");
            Assert.That(receivedTags[boolAddress], Is.EqualTo(true));
            Assert.That(receivedTags[intNumericAddress], Is.EqualTo(999));
        }
    }

#pragma warning disable CS0618 // Testing deprecated synchronous wrappers until removal
    [Test]
    public void Z_DeprecatedSync_SmokeTest()
    {
        Client.Write(intAddress, 123);
        var value = Client.Read<int>(intAddress);

        using var notified = new ManualResetEventSlim(false);
        int monitoredValue = 0;

        Client.Monitoring(intAddress, 100, (_, e) =>
        {
            if (e.Tag.Value is int val && val == 456)
            {
                monitoredValue = val;
                notified.Set();
            }
        });

        Client.Write(intAddress, 456);
        bool received = notified.Wait(TimeSpan.FromSeconds(3));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.EqualTo(123));
            Assert.That(received, Is.True, "Timed out waiting for sync Monitoring notification.");
            Assert.That(monitoredValue, Is.EqualTo(456));
        }
    }
#pragma warning restore CS0618

}