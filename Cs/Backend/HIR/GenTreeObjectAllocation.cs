using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Cnidaria.Cs;

/// <summary>Moves allocations that never escape the method from the GC heap into the frame</summary>
internal static class GenTreeObjectAllocator
{
    private const int MaxStackAllocationSize = 528;

    public static GenTreeMethod AllocateObjects(GenTreeMethod method, RuntimeTypeSystem typeSystem)
    {
        if (method is null)
            throw new ArgumentNullException(nameof(method));
        if (typeSystem is null)
            throw new ArgumentNullException(nameof(typeSystem));
        if (!HasAllocationSite(method))
            return method;

        return new ObjectAllocator(method, typeSystem).Run();
    }

    private static bool HasAllocationSite(GenTreeMethod method)
    {
        for (int b = 0; b < method.Blocks.Length; b++)
        {
            ImmutableArray<GenTree> statements = method.Blocks[b].Statements;
            for (int s = 0; s < statements.Length; s++)
            {
                if (IsAllocationStore(statements[s]))
                    return true;
            }
        }

        return false;
    }

    private static bool IsAllocationStore(GenTree statement)
        => statement.Kind is GenTreeKind.StoreLocal or GenTreeKind.StoreTemp &&
           statement.LocalDescriptor is not null &&
           statement.Operands.Length == 1 &&
           statement.Operands[0].Kind is GenTreeKind.NewArray or GenTreeKind.AllocObject or GenTreeKind.NewDelegate or GenTreeKind.Box &&
           !statement.Operands[0].IsNewDelegateInPlace &&
           !IsReferenceBox(statement.Operands[0]);

    // Boxing a reference yields the same object
    private static bool IsReferenceBox(GenTree node)
        => node.Kind == GenTreeKind.Box && (node.RuntimeType is not RuntimeType type || !type.IsValueType);

    private static bool IsPointerKind(GenStackKind kind)
        => kind is GenStackKind.Ref or GenStackKind.ByRef or GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr;

    // A struct local is a site whose contents are what its fields reference
    private class Site
    {
        public Site(int index, int words)
        {
            Index = index;
            Contents = new ulong[words];
        }

        public int Index { get; }
        public ulong[] Contents { get; }
        public bool Escapes { get; set; }
    }

    private sealed class AllocationSite : Site
    {
        public AllocationSite(int index, GenTree statement, int blockId, int words)
            : base(index, words)
        {
            Statement = statement;
            BlockId = blockId;
        }

        public GenTree Statement { get; }
        public GenTree Allocation => Statement.Operands[0];
        public int BlockId { get; }
        public bool OnStack { get; set; }
    }

    // Points-to sets range over sites, with bit 0 for every object this method did not allocate
    private sealed class ObjectAllocator
    {
        private const int NoNode = -1;
        private const int UnknownBit = 0;

        private readonly GenTreeMethod _method;
        private readonly RuntimeTypeSystem _typeSystem;
        private readonly ImmutableArray<GenLocalDescriptor> _locals;
        private readonly bool[] _tracked;
        private readonly Site?[] _structSites;
        private readonly List<Site> _sites = new();
        private readonly Dictionary<GenTree, AllocationSite> _siteByStatement = new();
        private readonly List<ulong[]> _pointsTo = new();
        private readonly Dictionary<GenTree, int> _valueNodes = new();
        private readonly List<(int Destination, int Source)> _copies = new();
        private readonly List<(int Destination, int Address)> _loads = new();
        private readonly List<(int Address, int Value)> _stores = new();
        private readonly List<(AllocationSite Site, int Value)> _siteStores = new();
        private readonly List<int> _escapes = new();
        private readonly List<int> _contentEscapes = new();
        private readonly List<(GenTree Store, int Address)> _storeAddresses = new();
        private readonly List<(GenTree Invoke, int Delegate)> _delegateInvokes = new();
        private readonly Dictionary<int, bool> _repeatedBlocks = new();
        private ControlFlowGraph? _cfg;
        private int _words;
        private int _unknownNode;
        private int _nextTreeId = -1;

        public ObjectAllocator(GenTreeMethod method, RuntimeTypeSystem typeSystem)
        {
            _method = method;
            _typeSystem = typeSystem;
            _locals = method.AllLocalDescriptors;
            _tracked = new bool[_locals.Length];
            _structSites = new Site?[_locals.Length];
            for (int lclNum = 0; lclNum < _locals.Length; lclNum++)
                _tracked[lclNum] = IsPointerKind(_locals[lclNum].StackKind) || IsTrackedStruct(_locals[lclNum]);
        }

        // Every use of a tracked struct local's address must be a tree the graph walks
        private static bool IsTrackedStruct(GenLocalDescriptor local)
            => local.Type is RuntimeType type &&
               type.Kind == RuntimeTypeKind.Struct &&
               type.ContainsGcPointers &&
               !local.Pinned &&
               !IsPointerKind(local.StackKind);

        public GenTreeMethod Run()
        {
            DoAnalysis();

            ImmutableArray<GenTreeBlock>.Builder blocks = _method.Blocks.ToBuilder();
            if (!MorphAllocObjNodes(blocks))
                return _method;

            RewriteUses(blocks);
            return _method.CloneWithBlocks(blocks.ToImmutable());
        }

        private void DoAnalysis()
        {
            var allocations = new List<(GenTree Statement, int BlockId)>();
            for (int b = 0; b < _method.Blocks.Length; b++)
            {
                ImmutableArray<GenTree> statements = _method.Blocks[b].Statements;
                for (int s = 0; s < statements.Length; s++)
                {
                    if (IsAllocationStore(statements[s]))
                        allocations.Add((statements[s], b));
                }
            }

            int structCount = 0;
            for (int lclNum = 0; lclNum < _locals.Length; lclNum++)
            {
                if (_tracked[lclNum] && !IsPointerKind(_locals[lclNum].StackKind))
                    structCount++;
            }

            _words = (allocations.Count + structCount + 64) / 64;
            foreach ((GenTree statement, int blockId) in allocations)
            {
                var site = new AllocationSite(_sites.Count + 1, statement, blockId, _words);
                _sites.Add(site);
                _siteByStatement.Add(statement, site);
            }

            for (int lclNum = 0; lclNum < _locals.Length; lclNum++)
                _pointsTo.Add(new ulong[_words]);
            _unknownNode = NewNode();
            SetBit(_pointsTo[_unknownNode], UnknownBit);

            for (int lclNum = 0; lclNum < _locals.Length; lclNum++)
            {
                GenLocalDescriptor local = _locals[lclNum];
                if (!_tracked[lclNum])
                    continue;
                if (!IsPointerKind(local.StackKind))
                {
                    var structSite = new Site(_sites.Count + 1, _words);
                    _sites.Add(structSite);
                    _structSites[lclNum] = structSite;
                    SetBit(_pointsTo[lclNum], structSite.Index);
                    if (local.Kind == GenLocalKind.Argument)
                        SetBit(structSite.Contents, UnknownBit);
                    continue;
                }
                if (local.Kind == GenLocalKind.Argument || local.AddressExposed || local.Pinned)
                    SetBit(_pointsTo[lclNum], UnknownBit);
                if (local.AddressExposed || local.Pinned)
                    _escapes.Add(lclNum);
            }

            for (int b = 0; b < _method.Blocks.Length; b++)
            {
                ImmutableArray<GenTree> statements = _method.Blocks[b].Statements;
                for (int s = 0; s < statements.Length; s++)
                {
                    if (_siteByStatement.TryGetValue(statements[s], out AllocationSite? site))
                        BuildSiteConnGraph(site);
                    else
                        BuildConnGraph(statements[s]);
                }
            }

            ComputeEscapingNodes();
        }

        private void BuildSiteConnGraph(AllocationSite site)
        {
            GenTree allocation = site.Allocation;
            if (TryGetTrackedLocal(site.Statement, out int lclNum))
            {
                SetBit(_pointsTo[lclNum], site.Index);
                // The finalizer reads the object and everything it references after the method has returned
                if (allocation.Kind == GenTreeKind.AllocObject && allocation.RuntimeType is RuntimeType objectType && _typeSystem.HasFinalizer(objectType))
                    _escapes.Add(lclNum);
            }
            else
            {
                site.Escapes = true;
            }

            if (allocation.Kind is GenTreeKind.NewDelegate or GenTreeKind.Box && allocation.Operands.Length != 0)
            {
                int stored = ValueNode(allocation.Operands[0]);
                if (stored != NoNode)
                    _siteStores.Add((site, stored));
            }

            for (int i = 0; i < allocation.Operands.Length; i++)
                BuildConnGraph(allocation.Operands[i]);
        }

        private void BuildConnGraph(GenTree node)
        {
            switch (node.Kind)
            {
                case GenTreeKind.StoreLocal:
                case GenTreeKind.StoreTemp:
                    if (!TryGetTrackedLocal(node, out int destination))
                    {
                        Escape(ValueNode(node.Operands[0]));
                    }
                    else if (_structSites[destination] is not null)
                    {
                        int stored = ValueNode(node.Operands[0]);
                        if (stored != NoNode)
                            _stores.Add((destination, stored));
                    }
                    else
                    {
                        AddConnGraphEdge(destination, ValueNode(node.Operands[0]));
                    }
                    break;

                case GenTreeKind.StoreField:
                case GenTreeKind.StoreIndirect:
                case GenTreeKind.StoreArrayElement:
                    int address = AddressNode(node.Operands[0]);
                    _storeAddresses.Add((node, address));
                    int value = ValueNode(node.Operands[^1]);
                    if (value != NoNode)
                        _stores.Add((address, value));
                    break;

                case GenTreeKind.DelegateInvoke:
                    for (int i = 0; i < node.Operands.Length; i++)
                    {
                        int argument = ValueNode(node.Operands[i]);
                        if (argument == NoNode)
                            continue;
                        if (i == 0)
                        {
                            _contentEscapes.Add(argument);
                            _delegateInvokes.Add((node, argument));
                        }
                        else
                        {
                            _escapes.Add(argument);
                        }
                    }
                    break;

                case GenTreeKind.LocalAddr:
                case GenTreeKind.TempAddr:
                case GenTreeKind.ArgAddr:
                    if (TryGetTrackedLocal(node, out int exposed) && _structSites[exposed] is null)
                    {
                        SetBit(_pointsTo[exposed], UnknownBit);
                        _escapes.Add(exposed);
                    }
                    break;

                case GenTreeKind.Binary when node.Operator is GenTreeOperator.Ceq or GenTreeOperator.Clt or
                    GenTreeOperator.CltUn or GenTreeOperator.Cgt or GenTreeOperator.CgtUn or
                    GenTreeOperator.Add or GenTreeOperator.Sub:
                case GenTreeKind.Unary when node.Operator == GenTreeOperator.PtrToByRef:
                case GenTreeKind.BranchTrue:
                case GenTreeKind.BranchFalse:
                case GenTreeKind.Eval:
                case GenTreeKind.NullCheck:
                case GenTreeKind.ArrayLength:
                case GenTreeKind.ArrayElement:
                case GenTreeKind.ArrayElementAddr:
                case GenTreeKind.ArrayDataRef:
                case GenTreeKind.Field:
                case GenTreeKind.FieldAddr:
                case GenTreeKind.LoadIndirect:
                case GenTreeKind.PointerElementAddr:
                case GenTreeKind.CastClass:
                case GenTreeKind.IsInst:
                case GenTreeKind.UnboxAny:
                case GenTreeKind.Box when IsReferenceBox(node):
                    break;

                default:
                    for (int i = 0; i < node.Operands.Length; i++)
                        Escape(ValueNode(node.Operands[i]));
                    break;
            }

            for (int i = 0; i < node.Operands.Length; i++)
                BuildConnGraph(node.Operands[i]);
        }

        // A struct value's node points to what its fields reference
        private int ValueNode(GenTree node)
        {
            bool isStruct = MayHoldGcPointers(node);
            if (!isStruct && !IsPointerKind(node.StackKind))
                return NoNode;
            if (_valueNodes.TryGetValue(node, out int known))
                return known;

            int result = isStruct ? StructValueNode(node) : PointerValueNode(node);
            _valueNodes[node] = result;
            return result;
        }

        private int StructValueNode(GenTree node)
        {
            switch (node.Kind)
            {
                case GenTreeKind.Local:
                case GenTreeKind.Temp:
                case GenTreeKind.Arg:
                    return TryGetTrackedLocal(node, out int lclNum) && _structSites[lclNum] is not null ? LoadNode(lclNum) : _unknownNode;

                case GenTreeKind.Field when node.Operands[0].StackKind == GenStackKind.Value:
                    return CopyNode(ValueNode(node.Operands[0]));

                case GenTreeKind.Field:
                case GenTreeKind.LoadIndirect:
                case GenTreeKind.ArrayElement:
                    return LoadNode(AddressNode(node.Operands[0]));

                case GenTreeKind.DefaultValue:
                    return NoNode;

                default:
                    return _unknownNode;
            }
        }

        private int PointerValueNode(GenTree node)
        {
            int result;
            switch (node.Kind)
            {
                case GenTreeKind.Local:
                case GenTreeKind.Temp:
                case GenTreeKind.Arg:
                    result = TryGetTrackedLocal(node, out int lclNum) && _structSites[lclNum] is null ? lclNum : _unknownNode;
                    break;

                case GenTreeKind.LocalAddr:
                case GenTreeKind.TempAddr:
                case GenTreeKind.ArgAddr:
                    result = TryGetTrackedLocal(node, out int structLocal) && _structSites[structLocal] is not null ? structLocal : _unknownNode;
                    break;

                case GenTreeKind.ArrayElementAddr:
                case GenTreeKind.ArrayDataRef:
                case GenTreeKind.FieldAddr:
                case GenTreeKind.PointerElementAddr:
                case GenTreeKind.CastClass:
                case GenTreeKind.IsInst:
                case GenTreeKind.UnboxAny:
                case GenTreeKind.Box when IsReferenceBox(node):
                case GenTreeKind.Unary when node.Operator == GenTreeOperator.PtrToByRef:
                    result = AddressNode(node.Operands[0]);
                    break;

                case GenTreeKind.Binary when node.Operator is GenTreeOperator.Add or GenTreeOperator.Sub:
                    result = NewNode();
                    for (int i = 0; i < node.Operands.Length; i++)
                        AddConnGraphEdge(result, ValueNode(node.Operands[i]));
                    break;

                case GenTreeKind.Field when node.Operands[0].StackKind == GenStackKind.Value:
                    result = CopyNode(ValueNode(node.Operands[0]));
                    break;

                case GenTreeKind.Field:
                case GenTreeKind.LoadIndirect:
                case GenTreeKind.ArrayElement:
                    result = LoadNode(AddressNode(node.Operands[0]));
                    break;

                default:
                    result = _unknownNode;
                    break;
            }

            return result;
        }

        private int LoadNode(int address)
        {
            int result = NewNode();
            _loads.Add((result, address));
            return result;
        }

        private int CopyNode(int source)
        {
            if (source == NoNode)
                return _unknownNode;

            int result = NewNode();
            AddConnGraphEdge(result, source);
            return result;
        }

        private static bool MayHoldGcPointers(GenTree node)
            => node.StackKind == GenStackKind.Value && ((node.RuntimeType ?? node.Type) is not RuntimeType type || type.ContainsGcPointers);

        private int AddressNode(GenTree node)
        {
            int address = ValueNode(node);
            return address == NoNode ? _unknownNode : address;
        }

        private int NewNode()
        {
            _pointsTo.Add(new ulong[_words]);
            return _pointsTo.Count - 1;
        }

        private bool TryGetTrackedLocal(GenTree node, out int lclNum)
        {
            lclNum = -1;
            if (node.Kind is not (GenTreeKind.Local or GenTreeKind.Temp or GenTreeKind.Arg or
                GenTreeKind.LocalAddr or GenTreeKind.TempAddr or GenTreeKind.ArgAddr or
                GenTreeKind.StoreLocal or GenTreeKind.StoreTemp or GenTreeKind.StoreArg) ||
                node.LocalDescriptor is not GenLocalDescriptor local ||
                (uint)local.LclNum >= (uint)_tracked.Length ||
                !_tracked[local.LclNum])
            {
                return false;
            }

            lclNum = local.LclNum;
            return true;
        }

        private void AddConnGraphEdge(int destination, int source)
        {
            if (source != NoNode)
                _copies.Add((destination, source));
        }

        private void Escape(int node)
        {
            if (node != NoNode)
                _escapes.Add(node);
        }

        private void ComputeEscapingNodes()
        {
            do
            {
                ComputePointsTo();
                MarkEscapingSites();
            }
            while (ExposeEscapedContents());
        }

        private void ComputePointsTo()
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach ((int destination, int source) in _copies)
                    changed |= Union(_pointsTo[destination], _pointsTo[source]);

                foreach ((int destination, int address) in _loads)
                {
                    ulong[] loaded = _pointsTo[destination];
                    foreach (int bit in Bits(_pointsTo[address]))
                        changed |= bit == UnknownBit ? SetBit(loaded, UnknownBit) : Union(loaded, _sites[bit - 1].Contents);
                }

                foreach ((int address, int value) in _stores)
                {
                    foreach (int bit in Bits(_pointsTo[address]))
                    {
                        if (bit != UnknownBit)
                            changed |= Union(_sites[bit - 1].Contents, _pointsTo[value]);
                    }
                }

                foreach ((AllocationSite site, int value) in _siteStores)
                    changed |= Union(site.Contents, _pointsTo[value]);
            }
        }

        private void MarkEscapingSites()
        {
            var pending = new Stack<Site>();
            foreach (int node in _escapes)
                MarkEscaping(_pointsTo[node], pending);

            foreach ((int address, int value) in _stores)
            {
                if (HasBit(_pointsTo[address], UnknownBit))
                    MarkEscaping(_pointsTo[value], pending);
            }

            // An invoked delegate hands its target to the callee
            foreach (int node in _contentEscapes)
            {
                foreach (int bit in Bits(_pointsTo[node]))
                {
                    if (bit != UnknownBit)
                        MarkEscaping(_sites[bit - 1].Contents, pending);
                }
            }

            while (pending.Count != 0)
                MarkEscaping(pending.Pop().Contents, pending);
        }

        private void MarkEscaping(ulong[] pointsTo, Stack<Site> pending)
        {
            foreach (int bit in Bits(pointsTo))
            {
                if (bit == UnknownBit || _sites[bit - 1].Escapes)
                    continue;

                _sites[bit - 1].Escapes = true;
                pending.Push(_sites[bit - 1]);
            }
        }

        // Code outside the method may store anything into an escaped object
        private bool ExposeEscapedContents()
        {
            bool changed = false;
            foreach (Site site in _sites)
            {
                if (site.Escapes)
                    changed |= SetBit(site.Contents, UnknownBit);
            }

            return changed;
        }

        private bool MorphAllocObjNodes(ImmutableArray<GenTreeBlock>.Builder blocks)
        {
            bool didStackAllocate = false;
            for (int b = 0; b < blocks.Count; b++)
            {
                GenTreeBlock block = blocks[b];
                List<GenTree>? statements = null;
                for (int s = 0; s < block.Statements.Length; s++)
                {
                    GenTree statement = block.Statements[s];
                    if (_siteByStatement.TryGetValue(statement, out AllocationSite? site) &&
                        CanAllocateLclVarOnStack(site, out RuntimeType? layout) &&
                        !BlockMayRepeat(block.Id))
                    {
                        statements ??= new List<GenTree>(block.Statements.Length + 3);
                        for (int i = statements.Count; i < s; i++)
                            statements.Add(block.Statements[i]);

                        MorphAllocationIntoStackAlloc(site, layout, statements);
                        site.OnStack = true;
                        didStackAllocate = true;
                    }
                    else if (statements is not null)
                    {
                        statements.Add(statement);
                    }
                }

                if (statements is not null)
                {
                    blocks[b] = new GenTreeBlock(
                        block.Id,
                        block.StartPc,
                        block.EndPcExclusive,
                        block.EntryStackDepth,
                        block.ExitStackDepth,
                        block.JumpKind,
                        block.Flags,
                        statements.ToImmutableArray(),
                        block.SuccessorBlockIds,
                        block.SuccessorPcs,
                        block.Region);
                }
            }

            return didStackAllocate;
        }

        private bool CanAllocateLclVarOnStack(AllocationSite site, [NotNullWhen(true)] out RuntimeType? layout)
        {
            layout = null;
            if (site.Escapes)
                return false;

            GenTree allocation = site.Allocation;
            switch (allocation.Kind)
            {
                case GenTreeKind.NewArray:
                    if (allocation.Type is not RuntimeType arrayType ||
                        !arrayType.IsSzArray ||
                        arrayType.ElementType is not RuntimeType elementType ||
                        !(elementType.IsValueType || elementType.IsReferenceType) ||
                        !TryGetConstantLength(allocation.Operands[0], out long length) ||
                        length < 0 || length > Array.MaxLength ||
                        _method.Target.ArrayDataOffset + length * Math.Max(1, elementType.SizeOf) > MaxStackAllocationSize)
                    {
                        return false;
                    }

                    layout = _typeSystem.GetArrayLayoutType(arrayType, (int)length);
                    return true;

                case GenTreeKind.AllocObject:
                    if (allocation.RuntimeType is not RuntimeType objectType ||
                        objectType.Kind != RuntimeTypeKind.Class ||
                        ReferenceEquals(objectType, _typeSystem.SystemString) ||
                        IsDelegateType(objectType) ||
                        objectType.InstanceSize > MaxStackAllocationSize)
                    {
                        return false;
                    }

                    layout = _typeSystem.GetObjectLayoutType(objectType);
                    return true;

                case GenTreeKind.NewDelegate:
                    if (allocation.RuntimeType is not RuntimeType delegateType ||
                        delegateType.InstanceSize > MaxStackAllocationSize)
                    {
                        return false;
                    }

                    layout = _typeSystem.GetObjectLayoutType(delegateType);
                    return true;

                case GenTreeKind.Box:
                    if (allocation.RuntimeType is not RuntimeType boxedType ||
                        !boxedType.IsValueType ||
                        IsNullable(boxedType) ||
                        _method.Target.ManagedObjectHeaderSize + boxedType.SizeOf > MaxStackAllocationSize)
                    {
                        return false;
                    }

                    layout = _typeSystem.GetBoxLayoutType(boxedType);
                    return true;

                default:
                    return false;
            }
        }

        private static bool TryGetConstantLength(GenTree node, out long length)
        {
            length = node.Kind switch
            {
                GenTreeKind.ConstI4 => node.Int32,
                GenTreeKind.ConstI8 => node.Int64,
                _ => -1,
            };
            return node.Kind is GenTreeKind.ConstI4 or GenTreeKind.ConstI8;
        }

        private static bool IsNullable(RuntimeType type)
            => type.GenericTypeDefinition is RuntimeType definition && definition.Namespace == "System" && definition.Name == "Nullable`1";

        private static bool IsDelegateType(RuntimeType type)
        {
            for (RuntimeType? current = type.BaseType; current is not null; current = current.BaseType)
            {
                if (current.Namespace == "System" && current.Name == "MulticastDelegate")
                    return true;
            }

            return false;
        }

        // A block that can run again while its previous object is still referenced would reuse the frame slot
        private bool BlockMayRepeat(int blockId)
        {
            if (_repeatedBlocks.TryGetValue(blockId, out bool repeated))
                return repeated;

            ControlFlowGraph cfg = _cfg ??= ControlFlowGraph.Build(_method, includeExceptionEdges: true);
            repeated = ReachesItself(cfg, blockId);
            ImmutableArray<int> regions = cfg.Blocks[blockId].HandlerRegionIndexes;
            for (int r = 0; r < regions.Length && !repeated; r++)
                repeated = ReachesItself(cfg, cfg.ExceptionRegions[regions[r]].TryStartBlockId);

            _repeatedBlocks.Add(blockId, repeated);
            return repeated;
        }

        private static bool ReachesItself(ControlFlowGraph cfg, int blockId)
        {
            var visited = new bool[cfg.Blocks.Length];
            var pending = new Stack<int>();
            pending.Push(blockId);
            while (pending.Count != 0)
            {
                ImmutableArray<CfgEdge> successors = cfg.Blocks[pending.Pop()].Successors;
                for (int i = 0; i < successors.Length; i++)
                {
                    int successor = successors[i].ToBlockId;
                    if (successor == blockId)
                        return true;
                    if (!visited[successor])
                    {
                        visited[successor] = true;
                        pending.Push(successor);
                    }
                }
            }

            return false;
        }

        private void MorphAllocationIntoStackAlloc(AllocationSite site, RuntimeType layout, List<GenTree> statements)
        {
            GenTree store = site.Statement;
            GenTree allocation = site.Allocation;
            GenLocalDescriptor stackLocal = _method.AppendCompilerTemp(GenTempKind.StackAllocatedObject, layout, GenStackKind.Value);
            int pc = store.Pc;

            // Frame slots holding GC pointers are zeroed in the prolog
            if (!layout.ContainsGcPointers)
            {
                GenTree zero = NewTree(GenTreeKind.DefaultValue, pc, layout, GenStackKind.Value, runtimeType: layout);
                GenTree init = NewTree(GenTreeKind.StoreIndirect, pc, null, GenStackKind.Void,
                    ImmutableArray.Create(StackLocalAddress(stackLocal, pc), zero), runtimeType: layout);
                init.Flags |= GenTreeFlags.ExplicitInit | GenTreeFlags.TargetNotOnHeap;
                statements.Add(init);
            }

            if (allocation.Kind == GenTreeKind.NewDelegate)
            {
                GenTree delegateInit = new GenTree(
                    NextTreeId(),
                    GenTreeKind.NewDelegate,
                    allocation.Pc,
                    allocation.Operator,
                    type: null,
                    GenStackKind.ByRef,
                    allocation.Flags,
                    allocation.Operands.Add(StackLocalAddress(stackLocal, pc)),
                    int32: 1,
                    int64: allocation.Int64,
                    runtimeType: allocation.RuntimeType,
                    method: allocation.Method);
                store.SetOperands(ImmutableArray.Create(delegateInit));
                statements.Add(store);
                return;
            }

            RuntimeType objectType = allocation.Kind == GenTreeKind.NewArray
                ? allocation.Type ?? throw new InvalidOperationException("Array allocation has no array type.")
                : allocation.RuntimeType ?? throw new InvalidOperationException("Object allocation has no type.");
            GenTree handle = NewTree(GenTreeKind.TypeHandle, pc, null, GenStackKind.NativeInt, runtimeType: objectType);
            statements.Add(NewTree(GenTreeKind.StoreIndirect, pc, null, GenStackKind.Void,
                ImmutableArray.Create(StackLocalAddress(stackLocal, pc), handle),
                runtimeType: _typeSystem.FindPrimitive(RuntimePrimitiveKind.IntPtr)));

            if (allocation.Kind == GenTreeKind.Box)
            {
                GenTree valueAddress = NewTree(GenTreeKind.PointerElementAddr, pc, null, GenStackKind.ByRef,
                    ImmutableArray.Create(StackLocalAddress(stackLocal, pc), NewTree(GenTreeKind.ConstI4, pc, null, GenStackKind.I4, int32: _method.Target.ManagedObjectHeaderSize)),
                    int32: 1);
                GenTree storeValue = NewTree(GenTreeKind.StoreIndirect, pc, null, GenStackKind.Void,
                    ImmutableArray.Create(valueAddress, allocation.Operands[0]), runtimeType: objectType);
                storeValue.Flags |= GenTreeFlags.TargetNotOnHeap;
                statements.Add(storeValue);
            }

            if (allocation.Kind == GenTreeKind.NewArray)
            {
                TryGetConstantLength(allocation.Operands[0], out long count);
                stackLocal.StackAllocatedArrayLength = (int)count;
                GenTree lengthAddress = NewTree(GenTreeKind.PointerElementAddr, pc, null, GenStackKind.ByRef,
                    ImmutableArray.Create(StackLocalAddress(stackLocal, pc), NewTree(GenTreeKind.ConstI4, pc, null, GenStackKind.I4, int32: _method.Target.ArrayLengthOffset)),
                    int32: 1);
                statements.Add(NewTree(GenTreeKind.StoreIndirect, pc, null, GenStackKind.Void,
                    ImmutableArray.Create(lengthAddress, NewTree(GenTreeKind.ConstI4, pc, null, GenStackKind.I4, int32: (int)count)),
                    runtimeType: _typeSystem.FindPrimitive(RuntimePrimitiveKind.Int32)));
            }

            store.SetOperands(ImmutableArray.Create(StackLocalAddress(stackLocal, pc)));
            statements.Add(store);
        }

        private GenTree StackLocalAddress(GenLocalDescriptor stackLocal, int pc)
        {
            GenTree address = NewTree(GenTreeKind.TempAddr, pc, null, GenStackKind.ByRef, int32: stackLocal.Index);
            address.LocalDescriptor = stackLocal;
            return address;
        }

        private GenTree NewTree(
            GenTreeKind kind,
            int pc,
            RuntimeType? type,
            GenStackKind stackKind,
            ImmutableArray<GenTree> operands = default,
            int int32 = 0,
            RuntimeType? runtimeType = null)
            => new GenTree(NextTreeId(), kind, pc, GenTreeOperator.None, type, stackKind, GenTreeFlags.None, operands, int32: int32, runtimeType: runtimeType);

        private int NextTreeId()
        {
            if (_nextTreeId < 0)
                _nextTreeId = GenTreeLocalAddressForwarder.ComputeNextTreeId(_method);
            return _nextTreeId++;
        }

        // Object references that may point into the frame become byrefs, or native ints when they always do
        private void RewriteUses(ImmutableArray<GenTreeBlock>.Builder blocks)
        {
            var stackSites = new ulong[_words];
            foreach (Site site in _sites)
            {
                if (site is AllocationSite allocation && allocation.OnStack)
                    SetBit(stackSites, site.Index);
            }

            var retyped = new GenStackKind[_locals.Length];
            for (int lclNum = 0; lclNum < _locals.Length; lclNum++)
            {
                if (_tracked[lclNum] && _locals[lclNum].StackKind == GenStackKind.Ref && Intersects(_pointsTo[lclNum], stackSites))
                    retyped[lclNum] = IsSubset(_pointsTo[lclNum], stackSites) ? GenStackKind.NativeInt : GenStackKind.ByRef;
            }

            Dictionary<GenTree, AllocationSite> devirtualized = FindDevirtualizableInvokes(stackSites);
            for (int b = 0; b < blocks.Count; b++)
            {
                GenTreeBlock block = blocks[b];
                bool restructured = false;
                for (int s = 0; s < block.Statements.Length; s++)
                    restructured |= RewriteTree(block.Statements[s], retyped, devirtualized);

                if (restructured)
                {
                    blocks[b] = new GenTreeBlock(
                        block.Id,
                        block.StartPc,
                        block.EndPcExclusive,
                        block.EntryStackDepth,
                        block.ExitStackDepth,
                        block.JumpKind,
                        block.Flags,
                        block.Statements,
                        block.SuccessorBlockIds,
                        block.SuccessorPcs,
                        block.Region);
                }
            }

            for (int lclNum = 0; lclNum < retyped.Length; lclNum++)
            {
                if (retyped[lclNum] == GenStackKind.Unknown)
                    continue;

                _locals[lclNum].Type = null;
                _locals[lclNum].StackKind = retyped[lclNum];
            }

            foreach ((GenTree store, int address) in _storeAddresses)
            {
                ulong[] target = _pointsTo[address];
                if (Intersects(target, stackSites) && IsSubset(target, stackSites))
                    store.Flags |= GenTreeFlags.TargetNotOnHeap;
            }
        }

        // A delegate built in the frame by one known creation calls its target directly
        private Dictionary<GenTree, AllocationSite> FindDevirtualizableInvokes(ulong[] stackSites)
        {
            var devirtualized = new Dictionary<GenTree, AllocationSite>();
            foreach ((GenTree invoke, int receiver) in _delegateInvokes)
            {
                if (invoke.Operands[0].Kind is not (GenTreeKind.Local or GenTreeKind.Temp) ||
                    !TryGetSingleBit(_pointsTo[receiver], out int bit) ||
                    !HasBit(stackSites, bit) ||
                    _sites[bit - 1] is not AllocationSite site)
                {
                    continue;
                }

                GenTree creation = site.Allocation;
                if (creation.Kind == GenTreeKind.NewDelegate &&
                    creation.Method is RuntimeMethod target &&
                    (creation.IsClosedNewDelegate || target.IsStatic))
                {
                    devirtualized.Add(invoke, site);
                }
            }

            return devirtualized;
        }

        private GenTree DevirtualizeDelegateInvoke(GenTree invoke, AllocationSite site)
        {
            GenTree creation = site.Allocation;
            RuntimeMethod target = creation.Method ?? throw new InvalidOperationException("Delegate creation has no target method.");
            var operands = ImmutableArray.CreateBuilder<GenTree>(invoke.Operands.Length);
            if (creation.IsClosedNewDelegate)
            {
                RuntimeType delegateType = creation.RuntimeType ?? throw new InvalidOperationException("Delegate creation has no delegate type.");
                RuntimeField targetField = FindInstanceField(delegateType, "_target");
                operands.Add(new GenTree(NextTreeId(), GenTreeKind.Field, invoke.Pc, GenTreeOperator.None, targetField.FieldType, GenStackKind.Ref,
                    GenTreeFlags.None, ImmutableArray.Create(invoke.Operands[0]), field: targetField));
            }

            for (int i = 1; i < invoke.Operands.Length; i++)
                operands.Add(invoke.Operands[i]);

            return new GenTree(NextTreeId(), GenTreeKind.Call, invoke.Pc, GenTreeOperator.None, invoke.Type, invoke.StackKind, invoke.Flags,
                operands.ToImmutable(), int32: operands.Count, method: target);
        }

        private static bool TryGetSingleBit(ulong[] set, out int bit)
        {
            bit = -1;
            for (int i = 0; i < set.Length; i++)
            {
                if (set[i] == 0)
                    continue;
                if (bit >= 0 || BitOperations.PopCount(set[i]) != 1)
                    return false;
                bit = i * 64 + BitOperations.TrailingZeroCount(set[i]);
            }

            return bit >= 0;
        }

        private static RuntimeField FindInstanceField(RuntimeType type, string name)
        {
            for (RuntimeType? current = type; current is not null; current = current.BaseType)
            {
                foreach (RuntimeField field in current.InstanceFields)
                {
                    if (!field.IsStatic && field.Name == name)
                        return field;
                }
            }

            throw new MissingFieldException(type.Name, name);
        }

        private bool RewriteTree(GenTree node, GenStackKind[] retyped, Dictionary<GenTree, AllocationSite> devirtualized)
        {
            RetypeLocalUse(node, retyped);
            bool restructured = false;
            ImmutableArray<GenTree>.Builder? operands = null;
            for (int i = 0; i < node.Operands.Length; i++)
            {
                GenTree operand = node.Operands[i];
                restructured |= RewriteTree(operand, retyped, devirtualized);
                if (devirtualized.TryGetValue(operand, out AllocationSite? site))
                {
                    operands ??= node.Operands.ToBuilder();
                    operands[i] = DevirtualizeDelegateInvoke(operand, site);
                }
            }

            if (operands is null)
                return restructured;

            node.SetOperands(operands.ToImmutable());
            return true;
        }

        private static void RetypeLocalUse(GenTree node, GenStackKind[] retyped)
        {
            if (node.Kind is GenTreeKind.Local or GenTreeKind.Temp &&
                node.LocalDescriptor is GenLocalDescriptor local &&
                (uint)local.LclNum < (uint)retyped.Length &&
                retyped[local.LclNum] != GenStackKind.Unknown)
            {
                node.Type = null;
                node.StackKind = retyped[local.LclNum];
            }
            else if (node.Kind is GenTreeKind.StoreLocal or GenTreeKind.StoreTemp &&
                node.LocalDescriptor is GenLocalDescriptor stored &&
                (uint)stored.LclNum < (uint)retyped.Length &&
                retyped[stored.LclNum] != GenStackKind.Unknown &&
                (node.Operands[0].Kind == GenTreeKind.TempAddr || node.Operands[0].IsNewDelegateInPlace))
            {
                node.Operands[0].StackKind = retyped[stored.LclNum];
            }
        }

        private static bool SetBit(ulong[] set, int bit)
        {
            ulong mask = 1UL << (bit & 63);
            if ((set[bit >> 6] & mask) != 0)
                return false;
            set[bit >> 6] |= mask;
            return true;
        }

        private static bool HasBit(ulong[] set, int bit)
            => (set[bit >> 6] & (1UL << (bit & 63))) != 0;

        private static bool Union(ulong[] destination, ulong[] source)
        {
            bool changed = false;
            for (int i = 0; i < destination.Length; i++)
            {
                ulong merged = destination[i] | source[i];
                changed |= merged != destination[i];
                destination[i] = merged;
            }

            return changed;
        }

        private static bool Intersects(ulong[] left, ulong[] right)
        {
            for (int i = 0; i < left.Length; i++)
            {
                if ((left[i] & right[i]) != 0)
                    return true;
            }

            return false;
        }

        private static bool IsSubset(ulong[] set, ulong[] of)
        {
            for (int i = 0; i < set.Length; i++)
            {
                if ((set[i] & ~of[i]) != 0)
                    return false;
            }

            return true;
        }

        private static IEnumerable<int> Bits(ulong[] set)
        {
            for (int i = 0; i < set.Length; i++)
            {
                for (ulong word = set[i]; word != 0; word &= word - 1)
                    yield return i * 64 + BitOperations.TrailingZeroCount(word);
            }
        }
    }
}
