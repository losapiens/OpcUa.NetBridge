using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace OpcUa.NetBridge.Tests;

// This attribute tells NUnit to execute this fixture BEFORE any tests run within this namespace
[SetUpFixture]
public class OpcServerFixture
{
    private MyCustomServer _server;

    // Static property to expose the server endpoint to your test classes
    public static string ServerUrl { get; private set; } = string.Empty;

    [OneTimeSetUp]
    public async Task StartOpcServerBeforeAllTests()
    {
        _server = new MyCustomServer();

        // 1. Define the path pointing to our copied XML configuration template
        string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OpcServerTest.xml");

        if (!File.Exists(configFilePath))
        {
            throw new FileNotFoundException($"[FIXTURE] Critical template missing: {configFilePath}. Ensure Copy to Output Directory is active!");
        }

        ApplicationInstance application = new ApplicationInstance((ITelemetryContext)null)
        {
            ApplicationName = "NUnitTestServer",
            ApplicationType = ApplicationType.Server
        };

        // This populates application.ApplicationConfiguration automatically
        await application.LoadApplicationConfigurationAsync(configFilePath, false);

        // 4. Asynchronously validate the complete profile context layout
        await application.ApplicationConfiguration.ValidateAsync(ApplicationType.Server);

        // 5. Generate a standard self-signed certificate using pure native .NET Cryptography
        if (application.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate == null)
        {
            using (var rsa = System.Security.Cryptography.RSA.Create(2048))
            {
                var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                    application.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.SubjectName,
                    rsa,
                    System.Security.Cryptography.HashAlgorithmName.SHA256,
                    System.Security.Cryptography.RSASignaturePadding.Pkcs1);

                var sanBuilder = new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder();
                sanBuilder.AddUri(new Uri(application.ApplicationConfiguration.ApplicationUri));
                request.CertificateExtensions.Add(sanBuilder.Build());

                var generatedCert = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddDays(-1),
                    DateTimeOffset.UtcNow.AddDays(3650));

                application.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate = generatedCert;
            }
        }

        //// 5. Generate a standard self-signed certificate matching the XML properties exactly
        //if (application.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate == null)
        //{
        //    using (var rsa = System.Security.Cryptography.RSA.Create(2048))
        //    {
        //        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
        //            application.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.SubjectName,
        //            rsa,
        //            System.Security.Cryptography.HashAlgorithmName.SHA256,
        //            System.Security.Cryptography.RSASignaturePadding.Pkcs1);

        //        // Add the essential Subject Alternative Names (SAN)
        //        var sanBuilder = new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder();
        //        // 1. Add the Application URI (urn:localhost:NUnitTestServer)
        //        sanBuilder.AddUri(new Uri(application.ApplicationConfiguration.ApplicationUri));
        //        // 2. CRUCIAL: Add localhost as an authorized DNS name so the client validator permits the connection
        //        sanBuilder.AddDnsName("localhost");
        //        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);

        //        request.CertificateExtensions.Add(sanBuilder.Build());

        //        // Add mandatory key usages flags for digital signing and encryption
        //        request.CertificateExtensions.Add(new System.Security.Cryptography.X509Certificates.X509KeyUsageExtension(
        //            System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.DigitalSignature |
        //            System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.KeyEncipherment |
        //            System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.DataEncipherment,
        //            critical: true));

        //        // Add Enhanced Key Usages
        //        var ekuOids = new System.Security.Cryptography.OidCollection();
        //        ekuOids.Add(new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1")); // Server Auth
        //        ekuOids.Add(new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.2")); // Client Auth
        //        request.CertificateExtensions.Add(new System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension(ekuOids, critical: false));

        //        var generatedCert = request.CreateSelfSigned(
        //            DateTimeOffset.UtcNow.AddDays(-1),
        //            DateTimeOffset.UtcNow.AddDays(3650));

        //        application.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate = generatedCert;
        //    }
        //}

        // 1. Force the server to blindly trust and accept ANY certificate, ignoring verification errors
        application.ApplicationConfiguration.CertificateValidator.CertificateValidation += (sender, e) =>
        {
            e.Accept = true;
        };

        // 6. FIX FOR CONNECTION REFUSED: Start the server using the instance orchestrator
        // This officially opens raw TCP sockets on port 48400 and links them to our MyCustomServer
        await application.StartAsync(_server);

        if (application.ApplicationConfiguration.ServerConfiguration.BaseAddresses.Count > 0)
        {
            ServerUrl = application.ApplicationConfiguration.ServerConfiguration.BaseAddresses[0];
        }
        else
        {
            throw new InvalidOperationException("[FIXTURE] No BaseAddresses found in the appconfig.xml profile!");
        }

        // Give the operating system network loopback a brief moment (1 second) to fully bind and open port 48400
        await Task.Delay(1000);

        Console.WriteLine($"[FIXTURE] OPC-UA Server successfully started and listening on {ServerUrl}  via XML Context Profile.");
    }



    /*
            [OneTimeSetUp]
            public async Task StartOpcServerBeforeAllTests()
            {
                // 1. Initialize the official OPC Foundation StandardServer instance
                _server = new();

                // 2. Build a minimal, unsecured configuration optimized for local integration testing
                var config = new ApplicationConfiguration
                {
                    ApplicationName = "NUnitTestServer",
                    ApplicationType = ApplicationType.Server,
                    ServerConfiguration = new ServerConfiguration
                    {
                        BaseAddresses = { ServerUrl },
                        SecurityPolicies = { new ServerSecurityPolicy { SecurityMode = MessageSecurityMode.None } }
                    },
                    SecurityConfiguration = new SecurityConfiguration { AutoAcceptUntrustedCertificates = true }
                };

                // 3. Asynchronously validate the server configuration layout
                await config.ValidateAsync(ApplicationType.Server);

                // 4. Asynchronously launch the OPC-UA stack in memory (returns a Task, which can be awaited)
                await _server.StartAsync(config);

                Console.WriteLine($"[FIXTURE] OPC-UA Server successfully started on {ServerUrl} with injected Node Manager.");
            }
    */
    [OneTimeTearDown]
    public void StopOpcServerAfterAllTests()
    {
        // 5. Tear down and dispose of the server resources once all unit tests have completed
        if (_server != null)
        {
            _server.StopAsync().GetAwaiter().GetResult();
            _server.Dispose();
            Console.WriteLine("[FIXTURE] OPC-UA Server gracefully stopped.");
        }
    }
}

// Explicitly inherit from StandardServer to leverage the framework's native factory method
public class MyCustomServer : StandardServer
{
    // This is the correct virtual method implementation across modern v1.5+ and v2.0 SDKs
    protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
    {
        // Instanciate our custom node manager passing the protected server context safely
        var customManagers = new INodeManager[] {
            new TestNodeManager(server, configuration)
        };

        // Return the master orchestrator containing our custom address space
        return new MasterNodeManager(server, configuration, null, customManagers);
    }

    //Explicitly supply product metadata properties to the engine hierarchy
    protected override ServerProperties LoadServerProperties()
    {
        return new ServerProperties
        {
            ManufacturerName = "YourCompany",
            ProductName = "NUnitTestServer",
            ProductUri = "http://yourcompany.com",
            SoftwareVersion = "1.0.0",
            BuildNumber = "1",
            BuildDate = DateTime.UtcNow
        };
    }
}
