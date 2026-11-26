using Opc.Ua;
using Opc.Ua.Server;

namespace OpcUa.NetBridge.Tests;

// Using CustomNodeManager2 ensures 100% compatibility across all SDK versions
public class TestNodeManager(IServerInternal server, ApplicationConfiguration configuration) : CustomNodeManager2(server, configuration, new string[] {
            "http://opcfoundation.org/UA/DI",       // Namespace Index 2
            "http://yourcompany.com",               // Namespace Index 3
            "http://yourcompany.com/alt",           // Namespace Index 4
        })
{
    //http://opcfoundation_org",             // Namespace Index 1

    // Correct signature for CustomNodeManager2 using reference mapping dictionary
    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        // Ensure base components and system locks are properly loaded first
        lock (Lock)
        {
            base.CreateAddressSpace(externalReferences);

            //// Define the physical path of the nodeset XML profile template
            //string nodeSetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OpcServerNodeset.xml");

            //if (!File.Exists(nodeSetPath))
            //{
            //    throw new FileNotFoundException($"[MANAGER] Nodeset resource layout template missing: {nodeSetPath}");
            //}

            //// FIX FOR SERIALIZATION ERROR (2,2): Instead of manual streams that trigger rigid XmlSerializer contracts,
            //// we leverage the native CustomNodeManager2 file loader layout by mapping an explicit FileStream
            //// directly into the server subsystem if the assembly context lookup fails.
            //try
            //{
            //    // Attempting standard file-system ingestion via core importer framework
            //    var nodeSet = Opc.Ua.NodeSetImporter.Load(nodeSetPath);
            //    if (nodeSet != null)
            //    {
            //        NodeStateCollection predefinedNodes = new NodeStateCollection();
            //        // This handles the namespace mapping array seamlessly
            //        nodeSet.Import(SystemContext, predefinedNodes);

            //        foreach (NodeState node in predefinedNodes)
            //        {
            //            this.PredefinedNodes[node.NodeId] = node;
            //            AddPredefinedNode(SystemContext, node);
            //        }
            //    }
            //}
            //catch (Exception)
            //{
            //    // Fallback to manual creation if NodeSetImporter is not packaged in your specific build context.
            //    // This forces a safe boot under any condition, preserving the structure.
            //    InitializeFallbackAddressSpace(externalReferences);
            //    return;
            //}

            ISystemContext systemContext = SystemContext;

            ushort namespaceIndex = NamespaceIndexes[1]; // Captures index 3 from the initialization array
            ushort altNamespaceIndex = NamespaceIndexes[2]; // Captures index 4

            // 1. First level: MyMeter folder
            var rootFolder = CreateFolder(null, "MyMeter", namespaceIndex);

            // 2. Second level: Test subfolder inside rootfolder
            var testFolder = CreateFolder(rootFolder, "Test", namespaceIndex);
            var numericFolder = CreateFolder(rootFolder, "Numeric", namespaceIndex);

            // 2a. Third level
            var subFolder = CreateFolder(testFolder, "SubGroup", namespaceIndex);

            // 3. Add all test variables using the helper (NodeIds: MyMeter.Test.<Name>)
            CreateScalarVariable(testFolder, "Boolean", DataTypeIds.Boolean, false, namespaceIndex, numericId: 1001, 2, numericFolder);
            CreateScalarVariable(testFolder, "UInteger", DataTypeIds.UInt32, 0u, namespaceIndex, numericId: 1002, 2, numericFolder);
            CreateScalarVariable(testFolder, "Integer", DataTypeIds.Int32, 0, namespaceIndex, numericId: 1003, 2, numericFolder);
            CreateScalarVariable(testFolder, "Double", DataTypeIds.Double, 0.0, namespaceIndex, numericId: 1004, 2, numericFolder);
            CreateScalarVariable(testFolder, "Float", DataTypeIds.Float, 0.0f, namespaceIndex, numericId: 1005, 2, numericFolder);
            CreateScalarVariable(testFolder, "String", DataTypeIds.String, string.Empty, namespaceIndex, numericId: 1006, 2, numericFolder);

            CreateArrayVariable(numericFolder, "BooleanArray", DataTypeIds.Boolean, new bool[100], namespaceIndex);
            for (int i = 1; i <= 999; i++)
            {
                CreateScalarVariable(numericFolder, $"Bool{i:D3}", DataTypeIds.Boolean, false, 2);
            }
            for (int i = 1; i <= 38; i++)
            {
                CreateScalarVariable(numericFolder, $"Uint{i:D2}", DataTypeIds.UInt32, 0u, 2);
            }

            CreateScalarVariable(subFolder, "TestVar", DataTypeIds.UInt32, 0u, namespaceIndex);

            // 4. Create the second root device ("ServerInterface") on an alternate namespace (e.g., ns=4 vs MyMeter on ns=3)
            var secondDevice = CreateFolder(null, "ServerInterface", altNamespaceIndex);
            CreateScalarVariable(secondDevice, "Status", DataTypeIds.Int32, 1, altNamespaceIndex);

            // 5. Link both root devices to ObjectsFolder and register their full hierarchies
            RegisterRootNode(systemContext, externalReferences, rootFolder);
            RegisterRootNode(systemContext, externalReferences, secondDevice);

        }
    }

    /// <summary>
    /// Links a root node under the standard <see cref="ObjectIds.ObjectsFolder"/> and registers its entire node hierarchy.
    /// </summary>
    private void RegisterRootNode(ISystemContext systemContext,
                                  IDictionary<NodeId, IList<IReference>> externalReferences,
                                  NodeState rootNode)
    {
        if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out IList<IReference>? references))
        {
            externalReferences[ObjectIds.ObjectsFolder] = references = [];
        }

        rootNode.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
        references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, rootNode.NodeId));

        AddPredefinedNode(systemContext, rootNode);
    }
    
    /// <summary>
         /// Creates a <see cref="FolderState"/> node, optionally linking it to a parent node and deriving its hierarchical NodeId path.
         /// </summary>
         /// <param name="parent">The parent <see cref="NodeState"/>, or <see langword="null"/> to create a root folder.</param>
         /// <param name="name">The symbolic, browse, and display name of the folder.</param>
         /// <param name="namespaceIndex">The namespace index for the folder's NodeId and BrowseName.</param>
         /// <returns>The created <see cref="FolderState"/> instance.</returns>
    private static FolderState CreateFolder(NodeState? parent, string name, ushort namespaceIndex)
    {
        string nodeIdString = parent is null
            ? name
            : $"{parent.NodeId.Identifier}.{name}";

        var folder = new FolderState(parent)
        {
            SymbolicName = name,
            ReferenceTypeId = ReferenceTypes.Organizes,
            TypeDefinitionId = ObjectTypes.FolderType,
            NodeId = new NodeId(nodeIdString, namespaceIndex),
            BrowseName = new QualifiedName(name, namespaceIndex),
            DisplayName = new LocalizedText(name)
        };

        parent?.AddChild(folder);
        return folder;
    }

    /// <summary>
    /// Creates a scalar variable with a string NodeId and, optionally, a synchronized twin variable with a numeric NodeId.
    /// </summary>
    /// <typeparam name="T">The data type of the variable value.</typeparam>
    /// <param name="parent">The parent folder or object node for the primary string variable.</param>
    /// <param name="name">The base symbolic and display name for the variable.</param>
    /// <param name="dataType">The OPC UA <see cref="NodeId"/> representing the variable's data type.</param>
    /// <param name="defaultValue">The initial value assigned to the variable(s).</param>
    /// <param name="namespaceIndex">The namespace index used for the primary string NodeId.</param>
    /// <param name="numericId">Optional numeric identifier. When provided, creates a linked numeric node.</param>
    /// <param name="numericNamespaceIndex">Optional namespace index for the numeric NodeId. Defaults to <paramref name="namespaceIndex"/> if omitted.</param>
    /// <param name="numericParent">Optional parent node for the numeric variable. Defaults to <paramref name="parent"/> if omitted.</param>
    /// <returns>The primary string <see cref="BaseDataVariableState{T}"/> instance.</returns>
    private static BaseDataVariableState<T> CreateScalarVariable<T>(
        NodeState parent,
        string name,
        NodeId dataType,
        T defaultValue,
        ushort namespaceIndex,
        uint? numericId = null,
        ushort? numericNamespaceIndex = null,
        NodeState? numericParent = null)
    {
        var stringVar = CreateSingleScalarVariable(
            parent,
            name,
            dataType,
            defaultValue,
            namespaceIndex);

        if (!numericId.HasValue)
        {
            return stringVar;
        }

        var numericVar = CreateSingleScalarVariable(
            numericParent ?? parent,
            $"{name}_Num",
            dataType,
            defaultValue,
            numericNamespaceIndex ?? namespaceIndex,
            numericId: numericId.Value);

        numericVar.OnSimpleWriteValue = (ISystemContext context, NodeState node, ref object value) =>
        {
            stringVar.Value = (T)value;
            stringVar.Timestamp = DateTime.UtcNow;
            stringVar.ClearChangeMasks(context, false);
            return ServiceResult.Good;
        };

        stringVar.OnSimpleWriteValue = (ISystemContext context, NodeState node, ref object value) =>
        {
            numericVar.Value = (T)value;
            numericVar.Timestamp = DateTime.UtcNow;
            numericVar.ClearChangeMasks(context, false);
            return ServiceResult.Good;
        };

        return stringVar;
    }

    private static BaseDataVariableState<T> CreateSingleScalarVariable<T>(
        NodeState parent,
        string name,
        NodeId dataType,
        T defaultValue,
        ushort namespaceIndex,
        uint? numericId = null)
    {
        NodeId nodeId = numericId.HasValue
            ? new NodeId(numericId.Value, namespaceIndex)
            : new NodeId($"{parent.NodeId.Identifier}.{name}", namespaceIndex);

        var variable = new BaseDataVariableState<T>(parent)
        {
            SymbolicName = name,
            ReferenceTypeId = ReferenceTypes.HasComponent,
            TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
            NodeId = nodeId,
            BrowseName = new QualifiedName(name, namespaceIndex),
            DisplayName = new LocalizedText(name),
            DataType = dataType,
            ValueRank = ValueRanks.Scalar,
            AccessLevel = AccessLevels.CurrentReadOrWrite,
            UserAccessLevel = AccessLevels.CurrentReadOrWrite,
            Value = defaultValue,
            StatusCode = StatusCodes.Good,
            Timestamp = DateTime.UtcNow
        };

        parent.AddChild(variable);
        return variable;
    }

    /// <summary>
    /// Creates a read/write 1D array variable under the specified parent folder and links it automatically.
    /// </summary>
    private static BaseDataVariableState<T[]> CreateArrayVariable<T>(
        NodeState parent,
        string name,
        NodeId dataType,
        T[] defaultValue,
        ushort namespaceIndex)
    {
        string fullNodeId = $"{parent.NodeId.Identifier}.{name}";

        var variable = new BaseDataVariableState<T[]>(parent)
        {
            SymbolicName = name,
            ReferenceTypeId = ReferenceTypes.HasComponent,
            TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
            NodeId = new NodeId(fullNodeId, namespaceIndex),
            BrowseName = new QualifiedName(name, namespaceIndex),
            DisplayName = new LocalizedText(name),
            DataType = dataType,
            ValueRank = ValueRanks.OneDimension,
            ArrayDimensions = new ReadOnlyList<uint>([(uint)defaultValue.Length]), // 0 = dynamic length if you pass an empty array
            AccessLevel = AccessLevels.CurrentReadOrWrite,
            UserAccessLevel = AccessLevels.CurrentReadOrWrite,
            Value = defaultValue,
            StatusCode = StatusCodes.Good,
            Timestamp = DateTime.UtcNow
        };

        parent.AddChild(variable);
        return variable;
    }
}
