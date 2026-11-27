using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using OpcUa.NetBridge.Exceptions;
using OpcUa.NetBridge.DTO;
using System.Net;

namespace OpcUa.NetBridge
{
    /// <summary>
    /// Client for OPCUA Server
    /// </summary>
    public class UaClient : IDisposable, IAsyncDisposable
    {
        #region Private Fields
        private bool _disposed;

        private readonly ConfiguredEndpoint _endpoint;
        private Session? _session;
        private ISession ActiveSession => _session ?? throw new ServiceResultException(
            StatusCodes.BadServerNotConnected,
            "The OPC UA client is not connected to the server, or the session has been closed.");

        private readonly UserIdentity _userIdentity;
        private readonly ApplicationConfiguration _appConfig;
        private const int ReconnectPeriod = 10000;
        private readonly object _lock = new object();
        private SessionReconnectHandler? _reconnectHandler;
        
        private ushort _namespaceIndex;
        
        // Keep track of a single subscription for each time interval (e.g., 1000ms, 500ms)
        private readonly Dictionary<int, Subscription> _activeSubscriptions = new();
        #endregion

        #region Private methods

        private void KeepAlive(ISession session, KeepAliveEventArgs e)
        {
            try
            {
                if (!ServiceResult.IsBad(e.Status)) return;
                lock (_lock)
                {
                    if (_reconnectHandler != null) return;
                    _reconnectHandler = new SessionReconnectHandler(new SilentTelemetry(), true);
                    _reconnectHandler.BeginReconnect(session, ReconnectPeriod, Reconnect);
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private void Reconnect(object? sender, EventArgs e)
        {
            lock (_lock)
            {
                if (!ReferenceEquals(sender, _reconnectHandler) || _reconnectHandler == null)
                {
                    return;
                }

                if (_reconnectHandler.Session != null)
                {
                    var oldSession = _session;
                    _session = (Session)_reconnectHandler.Session;

                    // Synchronize the local dictionary with the subscriptions already transferred by SessionReconnectHandler
                    _activeSubscriptions.Clear();

                    foreach (var subscription in _session.Subscriptions)
                    {
                        int interval = (int)subscription.PublishingInterval;
                        _activeSubscriptions[interval] = subscription;
                    }

                    // Dispose the replaced session if a new instance was created
                    if (oldSession != null && !ReferenceEquals(oldSession, _session))
                    {
                        oldSession.Dispose();
                    }
                }

                _reconnectHandler.Dispose();
                _reconnectHandler = null;
            }
        }

        private async Task<List<Group>> BrowseGroupsInternalAsync(Browser browser,
                                                                  NodeId parentNodeId,
                                                                  bool recursive,
                                                                  HashSet<NodeId> visited,
                                                                  CancellationToken ct)
        {
            ReferenceDescriptionCollection? browseResults = await browser.BrowseAsync(parentNodeId, ct);

            if (browseResults is not { Count: > 0 })
            {
                return [];
            }

            var groups = new List<Group>();

            foreach (var result in browseResults)
            {
                ct.ThrowIfCancellationRequested();

                if (result.NodeClass is not (NodeClass.Object or NodeClass.Variable))
                {
                    continue;
                }

                // Convert ExpandedNodeId to local NodeId BEFORE checking NamespaceIndex
                NodeId targetNodeId = ExpandedNodeId.ToNodeId(result.NodeId, ActiveSession.NamespaceUris);

                if (NodeId.IsNull(targetNodeId) || targetNodeId.NamespaceIndex == 0)
                {
                    continue;
                }

                // Browse children once to inspect both variables (Tags) and nested structures (Groups)
                ReferenceDescriptionCollection? children = await browser.BrowseAsync(targetNodeId, ct);

                if (children is not { Count: > 0 })
                {
                    continue;
                }

                var tags = new List<Tag>();
                bool hasChildObjectsOrStructs = false;

                foreach (var child in children)
                {
                    NodeId childNodeId = ExpandedNodeId.ToNodeId(child.NodeId, ActiveSession.NamespaceUris);
                    if (NodeId.IsNull(childNodeId) || childNodeId.NamespaceIndex == 0)
                    {
                        continue;
                    }

                    if (child.NodeClass == NodeClass.Variable)
                    {
                        tags.Add(new Tag
                        {
                            Address = childNodeId.ToString(),
                            Name = child.DisplayName?.Text ?? child.BrowseName?.Name ?? string.Empty
                        });
                    }

                    // Recurse only if the child is an Object (folder) or a non-built-in Variable (potential UDT/struct)
                    if (child.NodeClass == NodeClass.Object || child.TypeDefinition != VariableTypeIds.BaseDataVariableType)
                    {
                        hasChildObjectsOrStructs = true;
                    }
                }

                var group = new Group
                {
                    Address = targetNodeId.ToString(),
                    Name = result.DisplayName?.Text ?? result.BrowseName?.Name ?? string.Empty,
                    Tags = tags,
                    Groups = (recursive && hasChildObjectsOrStructs && visited.Add(targetNodeId))
                        ? await BrowseGroupsInternalAsync(browser, targetNodeId, true, visited, ct)
                        : []
                };

                groups.Add(group);
            }

            return groups;
        }

        private Subscription Subscription(int milliseconds)
        {
            var subscription = new Subscription(new SilentTelemetry())
            {
                PublishingEnabled = true,
                PublishingInterval = milliseconds,
                Priority = 1,
                KeepAliveCount = 10,
                LifetimeCount = 20,
                MaxNotificationsPerPublish = 1000
            };

            return subscription;
        }

        private (Subscription subscription, bool IsNew) GetOrCreateSubscription(int milliseconds)
        {
            lock (_lock)
            {
                // If a subscription for the specified interval already exists, return it
                if (_activeSubscriptions.TryGetValue(milliseconds, out var existingSubscription))
                {
                    return (existingSubscription, false);
                }

                // Otherwise, create a new subscription for the specified interval
                var newSubscription = Subscription(milliseconds);

                ActiveSession.AddSubscription(newSubscription);
                _activeSubscriptions[milliseconds] = newSubscription;

                return (newSubscription, true);
            }
        }


        /// <summary>
        /// Parses a string address into a native OPC UA <see cref="NodeId"/>, supporting raw NodeId formats, numeric identifiers, and string identifiers.
        /// </summary>
        /// <param name="address">The node address (e.g. "ns=2;i=1001", "1001", or "MyTag").</param>
        /// <returns>A native <see cref="NodeId"/> object.</returns>
        private NodeId ParseNodeId(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new ArgumentException("Node address cannot be null or empty.", nameof(address));
            }

            if (address.StartsWith("ns=", StringComparison.OrdinalIgnoreCase) ||
                address.StartsWith("i=", StringComparison.OrdinalIgnoreCase) ||
                address.StartsWith("s=", StringComparison.OrdinalIgnoreCase) ||
                address.StartsWith("g=", StringComparison.OrdinalIgnoreCase) ||
                address.StartsWith("b=", StringComparison.OrdinalIgnoreCase))
            {
                return NodeId.Parse(address);
            }

            if (uint.TryParse(address, out uint numericId))
            {
                return new NodeId(numericId, _namespaceIndex);
            }

            return new NodeId(address, _namespaceIndex);
        }
        
        /// <summary>
        /// Selects the most suitable endpoint from a collection of OPC UA server endpoints based on the requested security preference.
        /// </summary>
        /// <param name="endpoints">The collection of available <see cref="EndpointDescription"/> objects retrieved from the server.</param>
        /// <param name="useSecurity">If set to <c>true</c>, prioritizes an endpoint with message security enabled; otherwise, prefers an unsecured endpoint.</param>
        /// <returns>The first matching <see cref="EndpointDescription"/>, or the first available endpoint in the collection as a fallback.</returns>
        /// <exception cref="ServiceResultException">Thrown when the <paramref name="endpoints"/> collection is null or empty.</exception>
        private EndpointDescription SelectEndpointFromList(EndpointDescriptionCollection endpoints, bool useSecurity)
        {
            if (endpoints == null || endpoints.Count == 0)
            {
                throw new ServiceResultException(StatusCodes.BadServerUriInvalid, "No endpoints found on the specified server.");
            }

            foreach (var endpoint in endpoints)
            {
                // If security is required (e.g., Basic256Sha256) and the endpoint security mode is not 'None'
                if (useSecurity && endpoint.SecurityMode != MessageSecurityMode.None)
                {
                    return endpoint;
                }
                // If an unsecured connection is requested
                if (!useSecurity && endpoint.SecurityMode == MessageSecurityMode.None)
                {
                    return endpoint;
                }
            }

            // Fallback: se non trova corrispondenze esatte, restituisce il primo disponibile
            return endpoints[0];
        }

        #endregion

        #region Public fields

        /// <summary>
        /// Indicates if the instance is connected to the server.
        /// </summary>
        public bool IsConnected => _session is { Connected: true };
        /// <summary>
        /// Gets the namespace index.
        /// </summary>
        public ushort NamespaceIndex => _namespaceIndex;

        #endregion

        #region Public methods

        /// <summary>
        /// Create a new instance
        /// </summary>
        /// <param name="appName">
        /// Name of the application
        /// </param>
        /// <param name="serverUrl">
        /// Url of server
        /// </param>
        /// <param name="namespaceIndex">
        /// Namespace index
        /// </param>
        /// <param name="security">
        /// Enable or disable the security
        /// </param>
        /// <param name="untrusted">
        /// Accept untrusted certificates
        /// </param>
        /// <param name="user">
        /// User of the OPC UA Server
        /// </param>
        /// <param name="password">
        /// Password of the user
        /// </param>
        public UaClient(string appName, string serverUrl, ushort namespaceIndex = 2, bool security = false, bool untrusted=true, string user = "",
            string password = "")
        {
            string path = OpcCertificatePathHelper.InitializeCertificateStores(appName);
            string hostName = Dns.GetHostName();

            _userIdentity = user.Length > 0
                ? new UserIdentity(new UserNameIdentityToken { UserName = user, DecryptedPassword = System.Text.Encoding.UTF8.GetBytes(password) })
                : new UserIdentity();
            
            _appConfig = new ApplicationConfiguration
            {
                ApplicationName = appName,
                ApplicationUri = Utils.Format(@"urn:{0}" + appName, hostName),
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StorePath = Path.Combine(path, "Application"),
                        SubjectName = $"CN={appName}, DC={hostName}"
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = @"Directory",
                        StorePath = Path.Combine(path, "Trusted")
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = @"Directory",
                        StorePath = Path.Combine(path, "TrustedPeer")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = @"Directory",
                        StorePath = Path.Combine(path, "Rejected")
                    },
                    AutoAcceptUntrustedCertificates = true,
                    AddAppCertToTrustedStore = true,
                    RejectSHA1SignedCertificates = false
                },
                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas { OperationTimeout = 20000 },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 5000 },
                DisableHiResClock = false
            };
            _appConfig.ValidateAsync(ApplicationType.Client).GetAwaiter().GetResult();

            if (_appConfig.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                _appConfig.CertificateValidator.CertificateValidation += (s, ee) => { ee.Accept = (ee.Error.StatusCode == StatusCodes.BadCertificateUntrusted && untrusted); };
            }

            var application = new ApplicationInstance(_appConfig, new SilentTelemetry())
            {
                ApplicationName = appName,
                ApplicationType = ApplicationType.Client
            };
            
            application.CheckApplicationInstanceCertificatesAsync(true, 2048).GetAwaiter().GetResult();

            using (var discoveryClient = DiscoveryClient.CreateAsync(_appConfig, new Uri(serverUrl)).GetAwaiter().GetResult())
            {
                // 1. Retrieve the EndpointDescriptionCollection directly from the server
                var endpoints = discoveryClient.GetEndpointsAsync(null).GetAwaiter().GetResult();

                // 2. Select the appropriate endpoint based on the 'security' flag using the private helper
                var endpointDescription = SelectEndpointFromList(endpoints, security);

                // 3. Build the final configured endpoint
                var endpointConfig = EndpointConfiguration.Create(_appConfig);
                _endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfig);
            }

            _namespaceIndex = namespaceIndex;
        }


        /// <summary>
        /// Change the namespace of the connection.
        /// </summary>
        /// <param name="namespaceIndex">
        ///  Index of the namespace
        /// </param>
        public void ChangeNamespaceIndex(ushort namespaceIndex)
        {
            _namespaceIndex = namespaceIndex;
        }

        /// <summary>
        /// Open the connection with the OPC UA Server
        /// </summary>
        /// <param name="timeOut">
        /// Timeout to try to connect with the server in seconds.
        /// </param>
        /// <param name="keepAlive">
        /// Sets whether to try to connect to the server in case the connection is lost.
        /// </param>
        /// <exception cref="ServerException"></exception>
        [Obsolete("This synchronous method is deprecated and will be removed in future versions. Use ConnectAsync() instead.")]
        public void Connect(uint timeOut = 5, bool keepAlive = false)
        {
            Task.Run(() => ConnectAsync(timeOut, keepAlive)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Close the connection with the OPC UA Server
        /// </summary>
        [Obsolete("This synchronous method is deprecated and will be removed in future versions. Use DisconnectAsync() instead.")]
        public void Disconnect()
        {
            Task.Run(() => DisconnectAsync()).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Read a tag of the specific address
        /// </summary>
        /// <param name="address">
        /// Address of the tag
        /// </param>
        /// <returns>
        /// <see cref="Tag"/>
        /// </returns>
        [Obsolete("This synchronous method is deprecated and will be removed in future versions. Use ReadAsync() instead.")]
        public Tag Read(string address)
        {
            return Task.Run(() => ReadAsync(address)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Read an address
        /// </summary>
        /// <param name="address">
        /// Address to read.
        /// </param>
        /// <typeparam name="TValue">
        /// Type of value to read.
        /// </typeparam>
        /// <returns></returns>
        /// <exception cref="ReadException">
        /// If the status of read action is not good <see cref="StatusCodes"/>
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// If the type is not supported.
        /// </exception>
        [Obsolete("This synchronous method is deprecated and will be removed in future versions. Use ReadAsync() instead.")]
        public TValue Read<TValue>(string address)
        {
            return Task.Run(() => ReadAsync<TValue>(address)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Read a list of tags on the OPCUA Server
        /// </summary>
        /// <param name="address">
        /// List of address to read.
        /// </param>
        /// <returns>
        /// A list of tags <see cref="Tag"/>
        /// </returns>
        [Obsolete("This synchronous method is deprecated and will be removed in future versions. Use ReadAsync() instead.")]
        public List<Tag> Read(IEnumerable<string> address)
        {
            return Task.Run(() => ReadAsync(address)).GetAwaiter().GetResult().ToList();
        }

        /// <summary>
        /// Write a value on a tag
        /// </summary>
        /// <param name="address">
        /// Address of the tag
        /// </param>
        /// <param name="value">
        /// Value to write
        /// </param>
        /// <exception cref="WriteException"></exception>
        [Obsolete("This synchronous method is deprecated and will be removed in future versions. Use WriteAsync() instead.")]
        public void Write(string address, object? value)
        {
            Task.Run(() => WriteAsync(address, value)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Write a value on a tag
        /// </summary>
        /// <param name="tag"> <see cref="DTO.Tag"/></param>
        /// <exception cref="WriteException"></exception>
        [Obsolete("This synchronous method is deprecated and will be removed in future versions. Use WriteAsync() instead.")]
        public void Write(Tag tag)
        {
            Task.Run(() => WriteAsync(tag.Address, tag.Value!)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Write a lis of values
        /// </summary>
        /// <param name="tags"> <see cref="Tag"/></param>
        /// <exception cref="WriteException"></exception>
        [Obsolete("This synchronous method is deprecated and will be removed in future versions. Use WriteAsync() instead.")]
        public void Write(IEnumerable<Tag> tags)
        {
            Task.Run(() => WriteAsync(tags)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Monitoring a tag and execute a function when the value change
        /// </summary>
        /// <param name="address">
        /// Address of the tag
        /// </param>
        /// <param name="milliseconds">
        /// Sets the time to check changes in the tag
        /// </param>
        /// <param name="monitor">
        /// Function to execute when the value changes.
        /// </param>
        [Obsolete("This synchronous method is deprecated and will be removed in future versions. Use MonitoringAsync() instead.")]
        public void Monitoring(string address, int milliseconds, MonitoredTagNotificationEventHandler monitor)
        {
            Task.Run(() => MonitoringAsync(address, milliseconds, monitor)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Monitoring a list of tags and execute a function when the value change
        /// </summary>
        /// <param name="addresses">
        /// Addresses of the tags
        /// </param>
        /// <param name="milliseconds">
        /// Sets the time to check changes in the tag
        /// </param>
        /// <param name="monitor">
        /// Function to execute when the value changes.
        /// </param>
        [Obsolete("Use MonitoringAsync instead.")]
        public void Monitoring(IEnumerable<string> addresses, int milliseconds, MonitoredTagNotificationEventHandler monitor)
        {
            Task.Run(() => MonitoringAsync(addresses, milliseconds, monitor)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Scan root folder of OPC UA server and get all devices
        /// </summary>
        /// <param name="recursive">
        /// Indicates whether to search within device groups
        /// </param>
        /// <returns>
        /// List of <see cref="Device"/>
        /// </returns>
        [Obsolete("This synchronous method is deprecated and will be removed in future versions. Use DevicesAsync() instead.")]
        public List<Device> Devices(bool recursive = false)
        {
            return Task.Run(() => DevicesAsync(recursive)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Scan an address and retrieve the tags and groups
        /// </summary>
        /// <param name="address">
        /// Address to search
        /// </param>
        /// <param name="recursive">
        /// Indicates whether to search within group groups
        /// </param>
        /// <returns>
        /// List of <see cref="DTO.Group"/>
        /// </returns>
        [Obsolete("This synchronous method is deprecated and will be removed in future versions. Use GroupsAsync() instead.")]
        public List<Group> Groups(string address, bool recursive = false)
        {
            return Task.Run(() => GroupsAsync(address, recursive)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Scan an address and retrieve the tags.
        /// </summary>
        /// <param name="address">
        /// Address to search
        /// </param>
        /// <returns>
        /// List of <see cref="Tag"/>
        /// </returns>
        [Obsolete("This synchronous method is deprecated and will be removed in future versions. Use TagsAsync() instead.")]
        public List<Tag> Tags(string address)
        {
            return Task.Run(() => TagsAsync(address)).GetAwaiter().GetResult();
        }
        #endregion

        #region Async methods

        /// <summary>
        /// Open the connection with the OPC UA Server
        /// </summary>
        /// <param name="timeOut">
        /// Timeout to try to connect with the server in seconds.
        /// </param>
        /// <param name="keepAlive">
        /// Sets whether to try to connect to the server in case the connection is lost.
        /// </param>
        /// <param name="ct">
        /// Cancellation token
        /// </param>
        /// <exception cref="ServerException"></exception>
        public async Task ConnectAsync(uint timeOut = 5, bool keepAlive = false, CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            
            await DisconnectAsync(ct);

            // 1. Instantiate the official session factory with a silent telemetry context
            ISessionFactory sessionFactory = new DefaultSessionFactory(new SilentTelemetry());

            // 2. Create the session asynchronously using the 9-parameter factory overload
            var session = await sessionFactory.CreateAsync(_appConfig,
                                                           _endpoint,
                                                           false,                      // updateBeforeConnect
                                                           false,                      // checkDomain
                                                           _appConfig.ApplicationName, // sessionName
                                                           timeOut * 1000,             // sessionTimeout
                                                           _userIdentity,              // identity
                                                           null,                       // preferredLocales
                                                           ct                          // cancellationToken
                                                           );

            // 3. Cast to concrete Session to match the internal class field type
            _session = (Session)session;

            if (keepAlive)
            {
                _session.KeepAlive += KeepAlive;
            }

            if (_session is not { Connected: true })
            {
                throw new ServerException("Error creating a session on the server");
            }
        }

        /// <summary>
        /// Close the connection with the OPC UA Server
        /// </summary>
        public async Task DisconnectAsync(CancellationToken ct = default)
        {
            // Stop and dispose any active reconnection attempt in a thread-safe manner
            lock (_lock)
            {
                _reconnectHandler?.Dispose();
                _reconnectHandler = null;
            }

            if (_session == null)
            {
                return;
            }

            try
            {
                // Unsubscribe from KeepAlive to prevent memory leaks
                _session.KeepAlive -= KeepAlive;

                if (_session.Connected)
                {
                    // Delete active subscriptions (iterate over a copy to avoid collection modification errors)
                    if (_session.Subscriptions != null && _session.Subscriptions.Any())
                    {
                        // Copy to a list to avoid modifying the collection while iterating
                        foreach (var subscription in _session.Subscriptions.ToList())
                        {
                            await subscription.DeleteAsync(true, ct);
                        }
                    }

                    await _session.CloseAsync(ct);
                }
            }
            finally
            {
                lock (_lock)
                {
                    _activeSubscriptions.Clear();
                }

                // Always release session resources even if already disconnected or if CloseAsync fails
                _session.Dispose();
                _session = null;
            }
        }

        /// <summary>
        /// Scan root folder of OPC UA server and get all devices
        /// </summary>
        /// <param name="recursive">
        /// Indicates whether to search within device groups
        /// </param>
        /// <param name="ct">
        /// Cancellation token
        /// </param>
        /// <returns>
        /// List of <see cref="Device"/>
        /// </returns>
        public async Task<List<Device>> DevicesAsync(bool recursive = false, CancellationToken ct = default)
        {
            Browser browser = new Browser(ActiveSession)
            {
                BrowseDirection = BrowseDirection.Forward,
                NodeClassMask = (int)NodeClass.Object, // | (int)NodeClass.Variable,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences
            };

            // Execute the browse operation asynchronously
            ReferenceDescriptionCollection? browseResults = await browser.BrowseAsync(ObjectIds.ObjectsFolder, ct);

            if (browseResults is not { Count: > 0 })
            {
                return [];
            }

            var devices = new List<Device>(browseResults.Count);

            foreach(var result in browseResults)
            {
                ct.ThrowIfCancellationRequested();
                // Convert ExpandedNodeId to standard NodeId safely using the session's namespace table
                NodeId targetNodeId = ExpandedNodeId.ToNodeId(result.NodeId, ActiveSession.NamespaceUris);

                // Filter out system nodes (NamespaceIndex == 0) to avoid processing system/server objects
                if (NodeId.IsNull(targetNodeId) || targetNodeId.NamespaceIndex == 0 || result.NodeClass != NodeClass.Object)
                {
                    continue;
                }

                string deviceAddress = targetNodeId.ToString();

                devices.Add(new Device
                {
                    Address = deviceAddress,
                    Name = result.DisplayName?.Text ?? result.BrowseName?.Name ?? deviceAddress,
                    Groups = await GroupsAsync(deviceAddress, recursive, ct),
                    Tags = await TagsAsync(deviceAddress, ct)
                });
            }

            return devices;
        }


        /// <summary>
        /// Scan an address and retrieve the tags and groups
        /// </summary>
        /// <param name="address">
        /// Address to search
        /// </param>
        /// <param name="recursive">
        /// Indicates whether to search within group groups
        /// </param>
        /// <param name="ct">
        /// Cancellation token
        /// </param>
        /// <returns>
        /// List of <see cref="Group"/>
        /// </returns>
        public async Task<List<Group>> GroupsAsync(string address, bool recursive = false, CancellationToken ct = default)
        {
            var rootNodeId = ParseNodeId(address);
            var visited = new HashSet<NodeId> { rootNodeId };

            var browser = new Browser(ActiveSession)
            {
                BrowseDirection = BrowseDirection.Forward,
                NodeClassMask = (int)NodeClass.Object | (int)NodeClass.Variable,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences
            };

            return await BrowseGroupsInternalAsync(browser, rootNodeId, recursive, visited, ct);
        }

        /// <summary>
        /// Scan an address and retrieve the tags.
        /// </summary>
        /// <param name="address">
        /// Address to search
        /// </param>
        /// <param name="ct">
        ///  Cancellation token
        /// </param>
        /// <returns>
        /// List of <see cref="Tag"/>
        /// </returns>
        public async Task<List<Tag>> TagsAsync(string address, CancellationToken ct = default)
        {
            // Parse the node ID and execute the browse operation asynchronously
            var nodeId = ParseNodeId(address);
            
            //var tags = new List<Tag>();
            var browser = new Browser(ActiveSession)
            {
                BrowseDirection = BrowseDirection.Forward,
                //NodeClassMask = (int)NodeClass.Object | (int)NodeClass.Variable,
                NodeClassMask = (int)NodeClass.Variable,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences
            };

            ReferenceDescriptionCollection? browseResults = await browser.BrowseAsync(nodeId, ct);

            if (browseResults is not { Count: > 0 })
            {
                return [];
            }

            var tags = new List<Tag>(browseResults.Count);

            foreach (var result in browseResults)
            {
                // Abort operations if cancellation has been requested
                ct.ThrowIfCancellationRequested();

                // Convert ExpandedNodeId to standard NodeId safely using the session's namespace table
                NodeId targetNodeId = ExpandedNodeId.ToNodeId(result.NodeId, ActiveSession.NamespaceUris);

                // Skip standard system nodes (NamespaceIndex == 0) and filter for variables only
                if (targetNodeId.NamespaceIndex != 0 && result.NodeClass == NodeClass.Variable)
                {
                    tags.Add(new Tag
                    {
                        Name = result.DisplayName?.Text ?? result.BrowseName?.Name ?? string.Empty,
                        Address = targetNodeId.ToString()
                    });
                }
            }

            return tags;
        }

        /// <summary>
        /// Read a list of tags on the OPCUA Server
        /// </summary>
        /// <param name="address">
        /// List of address to read.
        /// </param>
        /// <param name="ct">
        /// Cancellation token
        /// </param>
        /// <returns>
        /// A list of tags <see cref="Tag"/>
        /// </returns>
        public async Task<IEnumerable<Tag>> ReadAsync(IEnumerable<string> address, CancellationToken ct = default)
        {
            IList<string> addressList = address as IList<string> ?? address?.ToList() ?? [];

            if (addressList.Count == 0)
                return [];

            var readValues = new ReadValueIdCollection(addressList.Count);
            readValues.AddRange(addressList.Select(a => new ReadValueId
            {
                NodeId = ParseNodeId(a),
                AttributeId = Attributes.Value
            }));

            var dataValues = await ActiveSession.ReadAsync(null, 0, TimestampsToReturn.Both, readValues, ct);

            if (dataValues?.Results is null)
                return [];

            return [.. dataValues.Results.Select((t, i) => new Tag
                {
                    Address = addressList[i],
                    Value = t.Value,   
                    Timestamp = t.SourceTimestamp != DateTime.MinValue ? t.SourceTimestamp : t.ServerTimestamp,
                    Code = t.StatusCode
                })];
        }

        /// <summary>
        /// Read a tag of the specific address
        /// </summary>
        /// <param name="address">
        /// Address of the tag
        /// </param>
        /// <param name="ct">
        /// Cancellation token
        /// </param>
        /// <returns>
        /// <see cref="Tag"/>
        /// </returns>
        public async Task<Tag> ReadAsync(string address, CancellationToken ct = default)
        {
            var results = await ReadAsync([address], ct);
            return results.FirstOrDefault() ?? new Tag
            {
                Address = address,
                Value = null
            };
        }

        /// <summary>
        /// Read an address
        /// </summary>
        /// <param name="address">
        /// Address to read.
        /// </param>
        /// <param name="ct">
        ///  Cancellation token
        /// </param>
        /// <typeparam name="TValue">
        /// Type of value to read.
        /// </typeparam>
        /// <returns></returns>
        /// <exception cref="ReadException">
        /// If the status of read action is not good <see cref="StatusCodes"/>
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// If the type is not supported.
        /// </exception>
        public async Task<TValue> ReadAsync<TValue>(string address, CancellationToken ct = default)
        {
            var results = await ReadAsync([address], ct);
            var tag = results.FirstOrDefault() ?? new Tag
            {
                Address = address,
                Value = null,
                Code = StatusCodes.BadUnexpectedError
            };

            if (StatusCode.IsNotGood(tag.Code))
            {
                throw new ReadException(tag.Code.Code.ToString());
            }

            if (tag.Value is TValue typedValue)
            {
                return typedValue;
            }

            if (tag.Value is null)
            {
                return default!;
            }

            return (TValue)Convert.ChangeType(tag.Value, typeof(TValue));
        }

        /// <summary>
        /// Write a lis of values
        /// </summary>
        /// <param name="tags"><see cref="Tag"/></param>
        /// <param name="ct">
        /// Cancellation token
        /// </param>
        public async Task<IEnumerable<Tag>> WriteAsync(IEnumerable<Tag> tags, CancellationToken ct = default)
        {
            IList<Tag> tagList = tags as IList<Tag> ?? tags?.ToList() ?? [];

            if (tagList.Count == 0)
                return [];

            var writeValues = new WriteValueCollection(tagList.Count);
            writeValues.AddRange(tagList.Select(tag => new WriteValue
            {
                NodeId = ParseNodeId(tag.Address),
                AttributeId = Attributes.Value,
                Value = new DataValue()
                {
                    Value = tag.Value
                }
            }));

            var response = await ActiveSession.WriteAsync(null, writeValues, ct);

            if (response?.Results is null)
                return [];

            return [.. tagList.Select((t, i) => new Tag
            {
                Address = t.Address,
                Value = t.Value,
                Code = i < response.Results.Count ? response.Results[i] : StatusCodes.BadUnexpectedError
            })];
        }

        /// <summary>
        /// Write a value on a tag
        /// </summary>
        /// <param name="address">
        /// Address of the tag
        /// </param>
        /// <param name="value">
        /// Value to write
        /// </param>
        /// <param name="ct">
        /// Cancellation token
        /// </param>
        public async Task<Tag> WriteAsync(string address, object? value, CancellationToken ct = default)
        {
            var results = await WriteAsync([new Tag { Address = address, Value = value }], ct);

            return results.FirstOrDefault() ?? new Tag
            {
                Address = address,
                Value = value,
                Code = StatusCodes.BadUnexpectedError
            };
        }

        /// <summary>
        /// Write a value on a tag
        /// </summary>
        /// <param name="tag"> <see cref="Tag"/></param>
        /// <param name="ct"> Cancellation token</param>
        public async Task<Tag> WriteAsync(Tag tag, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(tag);

            var results = await WriteAsync([tag], ct);
            return results.FirstOrDefault() ?? new Tag
            {
                Address = tag.Address,
                Name = tag.Name,
                Value = tag.Value,
                Code = StatusCodes.BadUnexpectedError
            };
        }

        /// <summary>
        /// Monitoring a a list of tags and execute a function when the value change
        /// </summary>
        /// <param name="addresses">
        /// Addresses of the tags
        /// </param>
        /// <param name="milliseconds">
        /// Sets the time to check changes in the tag
        /// </param>
        /// <param name="monitor">
        /// Function to execute when the value changes.
        /// </param>
        /// <param name="ct">
        /// Cancellation token
        /// </param>
        public async Task MonitoringAsync(IEnumerable<string> addresses,
                                          int milliseconds,
                                          MonitoredTagNotificationEventHandler monitor,
                                          CancellationToken ct = default)
        {
            IList<string> addressList = addresses as IList<string> ?? addresses?.ToList() ?? [];

            if (addressList.Count == 0)
                return;

            var (subscription, isNew) = GetOrCreateSubscription(milliseconds);

            foreach (var address in addressList)
            {
                var nodeId = ParseNodeId(address);

                var monitored = new MonitoredItem(new SilentTelemetry())
                {
                    StartNodeId = nodeId,
                    AttributeId = Attributes.Value,
                    SamplingInterval = milliseconds
                };

                monitored.Notification += (_, e) =>
                {
                    try
                    {
                        if (e.NotificationValue is MonitoredItemNotification notification)
                        {
                            var dataValue = notification.Value;

                            var tagUpdate = new Tag
                            {
                                Address = address,
                                Value = dataValue.Value,
                                Timestamp = dataValue.SourceTimestamp != DateTime.MinValue ? dataValue.SourceTimestamp : dataValue.ServerTimestamp,
                                Code = dataValue.StatusCode
                            };

                            monitor?.Invoke(this, new TagEventArgs(tagUpdate));
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error in monitoring notification for '{address}': {ex.Message}");
                    }
                };

                subscription.AddItem(monitored);
            }

            if (isNew)
            {
                await subscription.CreateAsync(ct);
            }
            else
            {
                await subscription.ApplyChangesAsync(ct);
            }
        }

        /// <summary>
        /// Monitoring a tag and execute a function when the value change
        /// </summary>
        /// <param name="address">
        /// Address of the tag
        /// </param>
        /// <param name="milliseconds">
        /// Sets the time to check changes in the tag
        /// </param>
        /// <param name="monitor">
        /// Function to execute when the value changes.
        /// </param>
        /// <param name="ct">
        /// Cancellation token
        /// </param>
        public Task MonitoringAsync(string address,
                                    int milliseconds,
                                    MonitoredTagNotificationEventHandler monitor,
                                    CancellationToken ct = default)
        {
            return MonitoringAsync([address], milliseconds, monitor, ct);
        }

        #endregion

        #region Dispose methods
        /// <summary>
        /// Synchronously disconnects the active OPC UA session and releases all managed resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Task.Run(() => DisconnectAsync()).GetAwaiter().GetResult();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Asynchronously disconnects the active OPC UA session and releases all managed resources.
        /// </summary>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous dispose operation.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await DisconnectAsync();
            GC.SuppressFinalize(this);
        }
        #endregion
    }


    internal class SilentTelemetry : TelemetryContextBase
    {
        public SilentTelemetry() : base(new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory())
        {
        }
    }

    /// <summary>
    /// Provides data for the node value variation event in OPC UA.
    /// Contains the updated information of the monitored tag.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="TagEventArgs"/> class.
    /// </remarks>
    /// <param name="tag">The <see cref="Tag"/> object containing the updated node data.</param>
    public class TagEventArgs(Tag tag) : EventArgs
    {
        /// <summary>
        /// Gets the updated tag information including address, value, status code, and timestamp.
        /// </summary>
        public Tag Tag { get; } = tag;
    }

    /// <summary>
    /// Represents the method that will handle the data change notification event for a monitored OPC UA tag.
    /// </summary>
    /// <param name="sender">The source of the event, typically the OPC UA client instance.</param>
    /// <param name="e">A <see cref="TagEventArgs"/> instance containing the updated tag details.</param>
    public delegate void MonitoredTagNotificationEventHandler(object sender, TagEventArgs e);

}