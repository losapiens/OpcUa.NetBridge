using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

/// <summary>
/// Provides helper methods to resolve secure, writable directory paths and configure
/// access permissions for OPC-UA certificate stores across development, desktop, and service environments.
/// </summary>
internal static class OpcCertificatePathHelper
{
    private static readonly string LibraryFolderName = SanitizeFolderName(
        typeof(OpcCertificatePathHelper).Assembly.GetName().Name ?? "OpcUa.NetBridge");

    /// <summary>
    /// Initializes the directory structure required for OPC-UA certificate stores
    /// (<c>Application</c>, <c>Trusted</c>, <c>TrustedPeer</c>, and <c>Rejected</c>).
    /// </summary>
    /// <param name="appName">
    /// The OPC-UA application name used to isolate certificate stores per client instance.
    /// </param>
    /// <param name="customBasePath">
    /// An optional custom root directory. If <see langword="null"/> or whitespace, the path defaults to:
    /// <list type="bullet">
    /// <item><description><see cref="AppContext.BaseDirectory"/> when a debugger is attached (regardless of Debug or Release build).</description></item>
    /// <item><description><see cref="Environment.SpecialFolder.LocalApplicationData"/> for interactive desktop apps.</description></item>
    /// <item><description><see cref="Environment.SpecialFolder.CommonApplicationData"/> for Windows services.</description></item>
    /// </list>
    /// </param>
    /// <returns>
    /// The full path to the initialized <c>Certificates</c> directory for the specified application.
    /// </returns>
    public static string InitializeCertificateStores(string appName, string? customBasePath = null)
    {
        bool useDebuggerFolder = string.IsNullOrWhiteSpace(customBasePath) && Debugger.IsAttached;

        // 1. Resolve root folder automatically:
        //    - Use customBasePath if explicitly provided by the host application
        //    - Use AppContext.BaseDirectory directly (bin\Debug or bin\Release) when debugging
        //    - Use LocalApplicationData (%LOCALAPPDATA%\<AssemblyName>) for desktop apps
        //    - Use CommonApplicationData (C:\ProgramData\<AssemblyName>) for Windows services
        string rootPath;
        if (!string.IsNullOrWhiteSpace(customBasePath))
        {
            rootPath = customBasePath;
        }
        else if (useDebuggerFolder)
        {
            rootPath = AppContext.BaseDirectory;
        }
        else
        {
            var specialFolder = Environment.UserInteractive
                ? Environment.SpecialFolder.LocalApplicationData
                : Environment.SpecialFolder.CommonApplicationData;

            rootPath = Path.Combine(Environment.GetFolderPath(specialFolder), LibraryFolderName);
        }

        Directory.CreateDirectory(rootPath);

        // 2. Restrict folder ACLs when running as a Windows service inside ProgramData
        if (OperatingSystem.IsWindows() && !Environment.UserInteractive && string.IsNullOrWhiteSpace(customBasePath))
        {
            RestrictFolderToCurrentIdentityAndAdmins(rootPath);
        }

        // 3. Isolate certificate stores per appName (sanitized to prevent invalid path characters)
        string safeAppName = SanitizeFolderName(appName);
        string certPath = Path.Combine(rootPath, safeAppName, "Certificates");

        Directory.CreateDirectory(Path.Combine(certPath, "Application"));
        Directory.CreateDirectory(Path.Combine(certPath, "Trusted"));
        Directory.CreateDirectory(Path.Combine(certPath, "TrustedPeer"));
        Directory.CreateDirectory(Path.Combine(certPath, "Rejected"));

        return certPath;
    }

    /// <summary>
    /// Replaces invalid file system characters in the application name with underscores.
    /// </summary>
    /// <param name="name">The raw application name.</param>
    /// <returns>A sanitized folder-safe string, or <c>"DefaultClient"</c> if the input is empty.</returns>
    private static string SanitizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "DefaultClient";

        char[] invalidChars = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
    }

    /// <summary>
    /// Disables permission inheritance on the specified Windows directory and grants full control
    /// exclusively to the current process identity and local Administrators.
    /// </summary>
    /// <param name="folderPath">The directory path to secure.</param>
    [SupportedOSPlatform("windows")]
    private static void RestrictFolderToCurrentIdentityAndAdmins(string folderPath)
    {
        var dirInfo = new DirectoryInfo(folderPath);
        var security = dirInfo.GetAccessControl();

        // Disable inheritance from C:\ProgramData and remove inherited permissions
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        // Grant full control to the service/process identity
        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser != null)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        // Grant full control to local Administrators for maintenance and backup
        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.AddAccessRule(new FileSystemAccessRule(
            adminsSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        dirInfo.SetAccessControl(security);
    }
}
