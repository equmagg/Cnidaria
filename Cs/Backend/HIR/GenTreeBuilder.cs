using Cnidaria.C;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;

namespace Cnidaria.Cs
{
    public sealed class GenTreeProgram
    {
        internal ImmutableArray<GenTreeMethod> Methods { get; }
        internal IReadOnlyDictionary<int, GenTreeMethod> MethodsByRuntimeMethodId { get; }
        public RuntimeTypeSystem? TypeSystem { get; }
        public TargetInfo Target => TypeSystem?.Target ?? (Methods.IsDefaultOrEmpty ? TargetInfo.Default : Methods[0].Target);
        internal GenTreeProgram(ImmutableArray<GenTreeMethod> methods)
            : this(null, methods)
        {
        }

        internal GenTreeProgram(RuntimeTypeSystem? typeSystem, ImmutableArray<GenTreeMethod> methods)
        {
            TypeSystem = typeSystem;
            Methods = methods.IsDefault ? ImmutableArray<GenTreeMethod>.Empty : methods;
            var map = new Dictionary<int, GenTreeMethod>();
            foreach (var m in Methods)
                map[m.RuntimeMethod.MethodId] = m;
            MethodsByRuntimeMethodId = map;
        }

    }
    internal sealed class GenTreeBuildException : Exception
    {
        public GenTreeBuildException(string message) : base(message) { }
        public GenTreeBuildException(string message, Exception innerException) : base(message, innerException) { }
    }
    public sealed class GenTreeBuilder
    {
        private readonly IReadOnlyDictionary<string, RuntimeModule> _modules;
        private readonly RuntimeTypeSystem _rts;
        private readonly Dictionary<int, (RuntimeModule module, CilMethodBody body, RuntimeMethod method)> _bodyByMethodId = new();
        private readonly Dictionary<int, GenTreeMethod> _built = new();
        private readonly Dictionary<int, ImmutableArray<RuntimeMethod>> _virtualTargetCache = new();
        private readonly Dictionary<int, RuntimeType> _liveInstantiatedTypes = new();
        private readonly HashSet<int> _scannedLiveConstructedGenericTypeIds = new();
        private readonly HashSet<int> _scannedLiveConstructedGenericCctorTypeIds = new();
        private int _liveInstantiatedTypeVersion;
        private int _virtualTargetScanVersion;
        public GenTreeBuilder(IReadOnlyDictionary<string, RuntimeModule> modules, RuntimeTypeSystem rts)
        {
            _modules = modules ?? throw new ArgumentNullException(nameof(modules));
            _rts = rts ?? throw new ArgumentNullException(nameof(rts));
        }

        public static GenTreeProgram BuildLinkedProgram(IReadOnlyDictionary<string, RuntimeModule> modules, RuntimeTypeSystem rts)
            => new GenTreeBuilder(modules, rts).BuildAllBodies();

        public static GenTreeProgram BuildReachableProgram(
            IReadOnlyDictionary<string, RuntimeModule> modules,
            RuntimeTypeSystem rts,
            RuntimeModule entryModule,
            int entryMethodToken)
            => new GenTreeBuilder(modules, rts).BuildReachable(entryModule, ImmutableArray.Create(entryMethodToken));

        public static GenTreeProgram BuildReachableProgram(
            IReadOnlyDictionary<string, RuntimeModule> modules,
            RuntimeTypeSystem rts,
            RuntimeModule entryModule,
            ImmutableArray<int> entryMethodTokens)
            => new GenTreeBuilder(modules, rts).BuildReachable(entryModule, entryMethodTokens);
        public GenTreeProgram BuildAllBodies()
        {
            IndexBodies();
            foreach (var item in _bodyByMethodId.Values)
                BuildOne(item.module, item.body, item.method);

            if (!_rts.Target.IsRegisterBytecode)
            {
                foreach (GenTreeMethod method in _built.Values)
                {
                    foreach (RuntimeMethod declaredVirtual in method.VirtualDependencies)
                    {
                        var targets = GetVirtualTargets(declaredVirtual);
                        for (int t = 0; t < targets.Length; t++)
                            MarkIndirectTypeInitializationTarget(targets[t]);
                    }
                }
            }

            _rts.EnsureAllTypesReady();
            return new GenTreeProgram(_rts, SortedBuiltMethods());
        }

        public GenTreeProgram BuildReachable(RuntimeModule entryModule, ImmutableArray<int> entryMethodTokens)
        {
            if (entryModule is null)
                throw new ArgumentNullException(nameof(entryModule));

            if (entryMethodTokens.IsDefaultOrEmpty)
                throw new ArgumentException("At least one entry method token is required.", nameof(entryMethodTokens));

            var queue = new Queue<RuntimeMethod>();
            var scheduledOrBuilt = new HashSet<int>();
            var virtualDependencies = new List<RuntimeMethod>();
            var virtualDependencyIds = new HashSet<int>();

            foreach (int entryMethodToken in entryMethodTokens)
            {
                RuntimeMethod entryMethod = _rts.ResolveMethodInMethodContext(
                    entryModule,
                    entryMethodToken,
                    methodContext: null);

                Enqueue(entryMethod);

                if (!entryMethod.DeclaringType.IsBeforeFieldInit &&
                    !StringComparer.Ordinal.Equals(entryMethod.Name, ".cctor") &&
                    (entryMethod.IsStatic ||
                     entryMethod.DeclaringType.IsValueType ||
                     StringComparer.Ordinal.Equals(entryMethod.Name, ".ctor")))
                {
                    _rts.EnsureConstructedMembers(entryMethod.DeclaringType);
                    RuntimeMethod? cctor = GenTreeMethodBuilder.FindTypeInitializer(entryMethod.DeclaringType);
                    if (cctor is not null) Enqueue(cctor);
                }
            }

            for (; ; )
            {
                while (queue.Count != 0)
                {
                    RuntimeMethod scheduled = queue.Dequeue();

                    if (!TryGetBuildableBody(
                            scheduled,
                            out RuntimeModule bodyModule,
                            out CilMethodBody body,
                            out RuntimeMethod buildMethod))
                    {
                        continue;
                    }

                    GenTreeMethod ir = BuildOne(bodyModule, body, buildMethod);

                    foreach (RuntimeMethod dep in ir.DirectDependencies)
                        Enqueue(dep);

                    foreach (RuntimeMethod declaredVirtual in ir.VirtualDependencies)
                    {
                        if (virtualDependencyIds.Add(declaredVirtual.MethodId))
                            virtualDependencies.Add(declaredVirtual);
                        var targets = GetVirtualTargets(declaredVirtual);
                        for (int t = 0; t < targets.Length; t++)
                        {
                            RuntimeMethod target = targets[t];
                            RuntimeMethod? cctor = MarkIndirectTypeInitializationTarget(target);
                            if (cctor is not null)
                                Enqueue(cctor);
                            Enqueue(target);
                        }
                    }
                }
                bool added = EnqueueConstructedGenericBodiesDiscoveredDuringImport();

                if (!added)
                    break;
            }

            _rts.EnsureAllTypesReady();
            return new GenTreeProgram(_rts, SortedBuiltMethods());

            bool Enqueue(RuntimeMethod method)
            {
                if (method is null)
                    return false;

                if (!TryGetBuildableBody(method, out _, out _, out RuntimeMethod buildMethod))
                {
                    return false;
                }

                if (!scheduledOrBuilt.Add(buildMethod.MethodId))
                    return false;

                queue.Enqueue(buildMethod);
                return true;
            }

            bool TryGetBuildableBody(
                RuntimeMethod method,
                out RuntimeModule bodyModule,
                out CilMethodBody body,
                out RuntimeMethod buildMethod)
            {
                buildMethod = method;

                if (method.BodyModule is not null && method.CilBody is CilMethodBody cilBody)
                {
                    bodyModule = method.BodyModule;
                    body = cilBody;
                    return true;
                }

                if (_bodyByMethodId.TryGetValue(method.MethodId, out var indexedBody))
                {
                    bodyModule = indexedBody.module;
                    body = indexedBody.body;
                    buildMethod = indexedBody.method;
                    return true;
                }

                bodyModule = null!;
                body = null!;
                return false;
            }

            bool EnqueueConstructedGenericBodiesDiscoveredDuringImport()
            {
                bool added = false;

                foreach (RuntimeType type in _liveInstantiatedTypes.Values)
                {
                    if (type.GenericTypeDefinition is null)
                        continue;

                    if (_scannedLiveConstructedGenericTypeIds.Add(type.TypeId))
                        _rts.EnsureConstructedMembers(type);

                    if (_scannedLiveConstructedGenericCctorTypeIds.Add(type.TypeId))
                    {
                        RuntimeMethod? cctor = GenTreeMethodBuilder.FindTypeInitializer(type);
                        if (cctor is not null)
                            added |= Enqueue(cctor);
                    }
                }

                if (_virtualTargetScanVersion != _liveInstantiatedTypeVersion && virtualDependencies.Count != 0)
                {
                    _virtualTargetCache.Clear();
                    _virtualTargetScanVersion = _liveInstantiatedTypeVersion;

                    for (int i = 0; i < virtualDependencies.Count; i++)
                    {
                        RuntimeMethod declaredVirtual = virtualDependencies[i];
                        var targets = GetVirtualTargets(declaredVirtual);
                        for (int t = 0; t < targets.Length; t++)
                        {
                            RuntimeMethod target = targets[t];
                            RuntimeMethod? cctor = MarkIndirectTypeInitializationTarget(target);
                            if (cctor is not null)
                                added |= Enqueue(cctor);
                            added |= Enqueue(target);
                        }
                    }
                }

                return added;
            }
        }

        private RuntimeMethod? MarkIndirectTypeInitializationTarget(RuntimeMethod target)
        {
            if (_rts.Target.IsRegisterBytecode ||
                target.DeclaringType.IsBeforeFieldInit ||
                StringComparer.Ordinal.Equals(target.Name, ".cctor") ||
                (!target.IsStatic && !target.DeclaringType.IsValueType))
            {
                return null;
            }

            _rts.EnsureConstructedMembers(target.DeclaringType);
            RuntimeMethod? cctor = GenTreeMethodBuilder.FindTypeInitializer(target.DeclaringType);
            if (cctor is null)
                return null;

            target.RequiresClassInitializationEntryCheck = true;
            return cctor;
        }

        private ImmutableArray<GenTreeMethod> SortedBuiltMethods()
        {
            var list = new List<GenTreeMethod>(_built.Values);
            list.Sort(static (a, b) => a.RuntimeMethod.MethodId.CompareTo(b.RuntimeMethod.MethodId));
            return list.ToImmutableArray();
        }

        private void IndexBodies()
        {
            foreach (var module in _modules.Values)
            {
                int methodCount = module.Md.GetRowCount(MetadataTableKind.MethodDef);
                for (int rid = 1; rid <= methodCount; rid++)
                {
                    int token = MetadataToken.MethodDef | rid;
                    if (module.GetCilBody(token) is not CilMethodBody body)
                        continue;
                    RuntimeMethod method;
                    try
                    {
                        method = _rts.ResolveMethodInMethodContext(module, token, methodContext: null);
                    }
                    catch (Exception ex)
                    {
                        throw new GenTreeBuildException($"Cannot resolve body method {module.Name}:0x{token:X8}.", ex);
                    }

                    _bodyByMethodId[method.MethodId] = (module, body, method);
                }
            }
        }

        private GenTreeMethod BuildOne(RuntimeModule module, CilMethodBody body, RuntimeMethod method)
        {
            if (_built.TryGetValue(method.MethodId, out var cached))
                return cached;

            var builder = new GenTreeMethodBuilder(_rts, module, body, method);
            var result = builder.Build();

            MarkLiveInstantiatedTypes(builder.InstantiatedTypes);

            _built.Add(method.MethodId, result);
            return result;
        }

        private void MarkLiveInstantiatedTypes(ImmutableArray<RuntimeType> types)
        {
            if (types.IsDefaultOrEmpty)
                return;

            for (int i = 0; i < types.Length; i++)
                MarkLiveInstantiatedType(types[i]);
        }

        private bool MarkLiveInstantiatedType(RuntimeType? type)
        {
            if (!CanHaveRuntimeInstance(type))
                return false;

            if (!_liveInstantiatedTypes.TryAdd(type!.TypeId, type))
                return false;

            _liveInstantiatedTypeVersion++;
            _virtualTargetCache.Clear();
            return true;
        }

        private static bool CanHaveRuntimeInstance(RuntimeType? type)
        {
            if (type is null)
                return false;

            return type.Kind is RuntimeTypeKind.Class or RuntimeTypeKind.Struct or RuntimeTypeKind.Enum or RuntimeTypeKind.Array;
        }
        private ImmutableArray<RuntimeMethod> GetVirtualTargets(RuntimeMethod declared)
        {
            if (_virtualTargetCache.TryGetValue(declared.MethodId, out var cached))
                return cached;

            var result = ImmutableArray.CreateBuilder<RuntimeMethod>();
            var yielded = new HashSet<int>();

            foreach (var target in EnumerateVirtualTargetsCore(declared))
            {
                if (target.CilBody is null)
                    continue;

                if (yielded.Add(target.MethodId))
                    result.Add(target);
            }

            var frozen = result.ToImmutable();
            _virtualTargetCache[declared.MethodId] = frozen;
            return frozen;
        }
        private IEnumerable<RuntimeMethod> EnumerateVirtualTargetsCore(RuntimeMethod declared)
        {
            if (declared.CilBody is not null)
                yield return declared;

            foreach (RuntimeType runtimeType in _liveInstantiatedTypes.Values)
            {
                RuntimeMethod? target = _rts.ResolveVirtualMethod(declared, runtimeType);
                if (target is not null && target.MethodId != declared.MethodId)
                    yield return target;
            }
        }
    }
    internal sealed class GenTreeMethodBuilder
    {
        private readonly RuntimeTypeSystem _rts;
        private readonly RuntimeModule _module;
        private readonly CilMethodBody _body;
        private short[] _pops = Array.Empty<short>();
        private short[] _pushes = Array.Empty<short>();
        private HashSet<int> _catchEntryPcs = new();
        private readonly List<byte> _staticData = new();
        private readonly Dictionary<int, (int Offset, int Length)> _staticDataOffsets = new();
        private readonly RuntimeMethod _method;

        private readonly List<GenTemp> _temps = new();
        private readonly HashSet<int> _materializedImporterTempIds = new();
        private readonly HashSet<int> _structMaterializationTempIds = new();
        private readonly Dictionary<(int StartPc, int Depth), GenTemp> _stackEntryTemps = new();
        private readonly Dictionary<int, GenTemp> _dupTemps = new();
        private readonly HashSet<int> _createdDupTempIds = new();
        private readonly HashSet<int> _directDependencyIds = new();
        private readonly HashSet<int> _virtualDependencyIds = new();
        private readonly List<RuntimeMethod> _directDependencies = new();
        private readonly List<RuntimeMethod> _virtualDependencies = new();
        private readonly Dictionary<int, RuntimeType> _instantiatedTypes = new();
        private readonly HashSet<int> _activeInlineMethods = new();
        private readonly List<GenTreeBlock> _deferredInlineBlocks = new();

        private bool[] _addressExposedArgs = Array.Empty<bool>();
        private bool[] _addressExposedLocals = Array.Empty<bool>();

        private const int InlineAlwaysBudget = 24;
        private const int InlineDiscretionaryBudget = 48;
        private const int InlineForceBudget = 128;
        private const int InlineSmallOverBudgetSize = 12;
        private const int InlineMaxDepth = 4;
        private const int InlineMaxForceDepth = 1;
        private const int InlineTotalBudget = 512;
        private const int InlineMaxBasicBlocks = 24;
        private int _inlineBudgetRemaining = InlineTotalBudget;
        private int _nextSyntheticPc;
        private int _nextDynamicBlockId;

        private RuntimeType[] _argTypes = Array.Empty<RuntimeType>();
        private RuntimeType[] _localTypes = Array.Empty<RuntimeType>();
        private const int UnreachableStackDepth = -1;
        private int[] _stackDepthAtPc = Array.Empty<int>();
        private Dictionary<int, int> _pcToBlockId = new();
        private int _nextNodeId;
        private int _nextTempIndex;

        public GenTreeMethodBuilder(
            RuntimeTypeSystem rts,
            RuntimeModule module,
            CilMethodBody body,
            RuntimeMethod method)
        {
            _rts = rts;
            _module = module;
            _body = body;
            _method = method;
            _nextTempIndex = 0;
        }

        public GenTreeMethod Build()
        {
            MarkInstantiatedMethodContext(_method);
            _argTypes = BuildArgTypes(_method);
            _localTypes = _rts.ResolveLocalSignatureInMethodContext(_module, _body.LocalSignatureToken, _method);
            (_pops, _pushes) = _body.GetStackEffects(_module.Md, !IsVoid(_method.ReturnType));
            _catchEntryPcs = new HashSet<int>();
            foreach (var clause in _body.ExceptionClauses)
            {
                if (clause.Kind is not (CilExceptionClauseKind.Catch or CilExceptionClauseKind.Finally))
                    throw Fail(clause.HandlerStartPc, ILOpCode.Nop, $"Unsupported exception clause kind {clause.Kind}.");
                if (clause.Kind == CilExceptionClauseKind.Catch)
                    _catchEntryPcs.Add(clause.HandlerStartPc);
            }
            ComputeImportAddressExposure();
            _stackDepthAtPc = ComputeStackDepths(_body, _pops, _pushes, _module, _method);

            var leaders = ComputeLeaders(_body, _stackDepthAtPc, splitAfterCalls: true, _module, _method);
            _rootFrame = new ImportFrame(_module, _method, _body, inlineDepth: 0, callPc: -1, _localTypes, null, null, null,
                DefaultValueLocalsOf(_body), pc => _pcToBlockId.ContainsKey(pc));
            var blocks = BuildBlocks(leaders);

            var handlers = ImmutableArray.CreateBuilder<ExceptionHandler>(_body.ExceptionClauses.Length);
            foreach (var clause in _body.ExceptionClauses)
            {
                handlers.Add(new ExceptionHandler(clause.TryStartPc, clause.TryEndPc, clause.HandlerStartPc, clause.HandlerEndPc,
                    clause.Kind == CilExceptionClauseKind.Catch ? clause.CatchTypeToken : -1));
            }

            return new GenTreeMethod(
                _module,
                _method,
                _rts.Target,
                handlers.MoveToImmutable(),
                _staticData.ToImmutableArray(),
                _argTypes.ToImmutableArray(),
                _localTypes.ToImmutableArray(),
                _addressExposedArgs.ToImmutableArray(),
                _addressExposedLocals.ToImmutableArray(),
                _temps.ToImmutableArray(),
                blocks,
                _directDependencies.ToImmutableArray(),
                _virtualDependencies.ToImmutableArray());
        }

        private void ComputeImportAddressExposure()
        {
            _addressExposedArgs = new bool[_argTypes.Length];
            _addressExposedLocals = new bool[_localTypes.Length];

            var instructions = _body.Instructions;
            for (int i = 0; i < instructions.Length; i++)
            {
                var ins = instructions[i];
                if (ins.Op == ILOpCode.Ldarga)
                {
                    if ((uint)ins.Int32 < (uint)_addressExposedArgs.Length && AddressUseMayEscape(i))
                        _addressExposedArgs[ins.Int32] = true;
                }
                else if (ins.Op == ILOpCode.Ldloca)
                {
                    if ((uint)ins.Int32 < (uint)_addressExposedLocals.Length && AddressUseMayEscape(i))
                        _addressExposedLocals[ins.Int32] = true;
                }
            }
        }

        // Follows the address through straight-line stack traffic to the instruction that consumes it.
        private bool AddressUseMayEscape(int addressProducerIndex)
        {
            var instructions = _body.Instructions;
            int positionFromTop = 0;
            for (int i = addressProducerIndex + 1; i < instructions.Length; i++)
            {
                var op = instructions[i].Op;
                int pop = _pops[i];
                if (pop > positionFromTop)
                    return op is not (ILOpCode.Ldfld or ILOpCode.Stfld or ILOpCode.Initobj);

                positionFromTop = positionFromTop - pop + _pushes[i];

                if (op == ILOpCode.Dup && positionFromTop == 1)
                    return true;

                if (IsBlockTerminator(op) || op is ILOpCode.Brtrue or ILOpCode.Brfalse or ILOpCode.Switch ||
                    op is >= ILOpCode.Beq and <= ILOpCode.Blt_Un)
                {
                    return true;
                }
            }

            return true;
        }

        private GenTreeBlock BuildBlock(int blockId, int startPc, int hardEndPc)
        {
            var statements = new List<GenTree>();
            var stack = CreateEntryStack(startPc);
            int pc = startPc;
            var successorPcs = new List<int>(2);
            var instructions = _body.Instructions;

            while (pc < hardEndPc)
            {
                var ins = instructions[pc];
                switch (ins.Op)
                {
                    case ILOpCode.Br:
                        AddSuccessor(successorPcs, ins.TargetPc);
                        SpillStackForBoundaries(statements, stack, successorPcs, pc, ins.Op);
                        statements.Add(Node(GenTreeKind.Branch, pc, ins.Op, targetPc: ins.TargetPc, targetBlockId: BlockIdForPc(ins.TargetPc)));
                        return CreateBlock(blockId, startPc, pc + 1, statements, successorPcs, stack.Count);

                    case ILOpCode.Leave:
                        AddSuccessor(successorPcs, ins.TargetPc);
                        DiscardStackForLeave(statements, stack, pc, ins.Op);
                        statements.Add(Node(GenTreeKind.Branch, pc, GenTreeOperator.Leave, targetPc: ins.TargetPc, targetBlockId: BlockIdForPc(ins.TargetPc)));
                        return CreateBlock(blockId, startPc, pc + 1, statements, successorPcs, stack.Count);

                    case ILOpCode.Brtrue:
                    case ILOpCode.Brfalse:
                        {
                            var cond = Pop(stack, pc, ins.Op);
                            AddConditionalBranch(statements, stack, successorPcs, pc, ins, cond.Node, ins.Op == ILOpCode.Brtrue);
                            return CreateBlock(blockId, startPc, pc + 1, statements, successorPcs, stack.Count);
                        }

                    case ILOpCode.Beq:
                    case ILOpCode.Bne_Un:
                    case ILOpCode.Bgt:
                    case ILOpCode.Bgt_Un:
                    case ILOpCode.Blt:
                    case ILOpCode.Blt_Un:
                    case ILOpCode.Bge:
                    case ILOpCode.Bge_Un:
                    case ILOpCode.Ble:
                    case ILOpCode.Ble_Un:
                        {
                            var right = Pop(stack, pc, ins.Op);
                            var left = Pop(stack, pc, ins.Op);
                            bool floating = left.StackKind is GenStackKind.R4 or GenStackKind.R8;
                            var (compare, branchWhenTrue) = CompareForBranch(ins.Op, floating);
                            var cond = Node(GenTreeKind.Binary, pc, compare, stackKind: GenStackKind.I4, operands: Two(left.Node, right.Node));
                            AddConditionalBranch(statements, stack, successorPcs, pc, ins, cond, branchWhenTrue);
                            return CreateBlock(blockId, startPc, pc + 1, statements, successorPcs, stack.Count);
                        }

                    case ILOpCode.Ret:
                        if (IsVoid(_method.ReturnType))
                        {
                            statements.Add(Node(GenTreeKind.Return, pc, ins.Op));
                        }
                        else
                        {
                            var value = Pop(stack, pc, ins.Op);
                            statements.Add(Node(GenTreeKind.Return, pc, ins.Op, operands: One(CoerceToStorage(value.Node, _method.ReturnType, pc))));
                        }
                        return CreateBlock(blockId, startPc, pc + 1, statements, successorPcs, stack.Count);

                    case ILOpCode.Throw:
                        {
                            var value = Pop(stack, pc, ins.Op);
                            statements.Add(Node(GenTreeKind.Throw, pc, ins.Op, operands: One(value.Node)));
                            return CreateBlock(blockId, startPc, pc + 1, statements, successorPcs, stack.Count);
                        }

                    case ILOpCode.Rethrow:
                        statements.Add(Node(GenTreeKind.Rethrow, pc, ins.Op));
                        return CreateBlock(blockId, startPc, pc + 1, statements, successorPcs, stack.Count);

                    case ILOpCode.Endfinally:
                        statements.Add(Node(GenTreeKind.EndFinally, pc, ins.Op));
                        return CreateBlock(blockId, startPc, pc + 1, statements, successorPcs, stack.Count);

                    case ILOpCode.Switch:
                    case ILOpCode.Jmp:
                    case ILOpCode.Endfilter:
                        throw Fail(pc, ins.Op, $"Unsupported control-flow opcode '{ins.Op}'.");

                    default:
                        {
                            int consumed = ImportInstruction(_rootFrame, stack, statements, successorPcs, pc, out bool terminatedBlock);
                            pc += consumed;
                            if (terminatedBlock)
                                return CreateBlock(blockId, startPc, pc, statements, successorPcs, stack.Count);
                            continue;
                        }
                }
            }

            if (pc < instructions.Length)
            {
                AddSuccessor(successorPcs, pc);
                SpillStackForBoundaries(statements, stack, successorPcs, pc - 1, ILOpCode.Nop);
            }

            return CreateBlock(blockId, startPc, pc, statements, successorPcs, stack.Count);
        }

        private void AddConditionalBranch(List<GenTree> statements, List<StackValue> stack, List<int> successorPcs, int pc, in CilInstruction ins, GenTree condition, bool branchWhenTrue)
        {
            if (TryGetImportConstant(condition, out var constant) && constant.Kind != GenTreeConstantKind.I8)
            {
                int takenPc = (constant.I4 != 0) == branchWhenTrue ? ins.TargetPc : pc + 1;
                AddSuccessor(successorPcs, takenPc);
                SpillStackForBoundaries(statements, stack, successorPcs, pc, ins.Op);
                statements.Add(Node(GenTreeKind.Branch, pc, ILOpCode.Br, targetPc: takenPc, targetBlockId: BlockIdForPc(takenPc)));
                return;
            }

            AddSuccessor(successorPcs, ins.TargetPc);
            if (pc + 1 < _body.Instructions.Length)
                AddSuccessor(successorPcs, pc + 1);
            SpillStackForBoundaries(statements, stack, successorPcs, pc, ins.Op);
            statements.Add(Node(branchWhenTrue ? GenTreeKind.BranchTrue : GenTreeKind.BranchFalse,
                pc, ins.Op, operands: One(condition), targetPc: ins.TargetPc, targetBlockId: BlockIdForPc(ins.TargetPc)));
        }

        // A condition the importer can evaluate: constants, sizeof, and integral operators over them.
        private bool TryGetImportConstant(GenTree node, out GenTreeConstantValue value)
        {
            switch (node.Kind)
            {
                case GenTreeKind.SizeOf when node.RuntimeType is RuntimeType sizedType:
                    _rts.EnsureRuntimeTypeReady(sizedType);
                    value = GenTreeConstantValue.ForI4(sizedType.SizeOf);
                    return true;

                case GenTreeKind.Unary when node.Operands.Length == 1 && TryGetImportConstant(node.Operands[0], out var operand):
                    return GenTreeFolder.TryFoldUnary(node, operand, _rts.Target, out value);

                case GenTreeKind.Conv when node.Operands.Length == 1 && TryGetImportConstant(node.Operands[0], out var converted):
                    return GenTreeFolder.TryFoldConversion(node, converted, _rts.Target, out value);

                case GenTreeKind.Binary when node.Operands.Length == 2 &&
                    TryGetImportConstant(node.Operands[0], out var left) && TryGetImportConstant(node.Operands[1], out var right):
                    return GenTreeFolder.TryFoldBinary(node, node.Operands[0], left, right, _rts.Target, out value);

                default:
                    return GenTreeFolder.TryGetConstant(node, out value);
            }
        }

        // bge and friends branch on the negated comparison so that unordered floats take the right edge.
        private static (ILOpCode Compare, bool BranchWhenTrue) CompareForBranch(ILOpCode op, bool floating) => op switch
        {
            ILOpCode.Beq => (ILOpCode.Ceq, true),
            ILOpCode.Bne_Un => (ILOpCode.Ceq, false),
            ILOpCode.Bgt => (ILOpCode.Cgt, true),
            ILOpCode.Bgt_Un => (ILOpCode.Cgt_Un, true),
            ILOpCode.Blt => (ILOpCode.Clt, true),
            ILOpCode.Blt_Un => (ILOpCode.Clt_Un, true),
            ILOpCode.Bge => (floating ? ILOpCode.Clt_Un : ILOpCode.Clt, false),
            ILOpCode.Bge_Un => (floating ? ILOpCode.Clt : ILOpCode.Clt_Un, false),
            ILOpCode.Ble => (floating ? ILOpCode.Cgt_Un : ILOpCode.Cgt, false),
            ILOpCode.Ble_Un => (floating ? ILOpCode.Cgt : ILOpCode.Cgt_Un, false),
            _ => throw new GenTreeBuildException($"Not a compare-and-branch opcode: {op}."),
        };

        private bool PcInExceptionRegion(int pc)
        {
            foreach (var clause in _body.ExceptionClauses)
            {
                if ((uint)(pc - clause.TryStartPc) < (uint)(clause.TryEndPc - clause.TryStartPc) ||
                    (uint)(pc - clause.HandlerStartPc) < (uint)(clause.HandlerEndPc - clause.HandlerStartPc))
                {
                    return true;
                }
            }
            return false;
        }

        private bool PcInExceptionHandlerRegion(int pc)
        {
            foreach (var clause in _body.ExceptionClauses)
            {
                if ((uint)(pc - clause.HandlerStartPc) < (uint)(clause.HandlerEndPc - clause.HandlerStartPc))
                    return true;
            }
            return false;
        }

        private GenTreeBlockFlags ComputeBlockFlags(int blockId, int startPc, int endPc, int entryStackDepth, int exitStackDepth, int successorCount)
        {
            GenTreeBlockFlags flags = GenTreeBlockFlags.None;
            if (blockId == 0) flags |= GenTreeBlockFlags.Entry;
            if (entryStackDepth != 0) flags |= GenTreeBlockFlags.HasStackEntry;
            if (successorCount != 0 && exitStackDepth != 0) flags |= GenTreeBlockFlags.HasStackExit;

            foreach (var clause in _body.ExceptionClauses)
            {
                if (clause.TryStartPc == startPc) flags |= GenTreeBlockFlags.TryEntry;
                if (clause.HandlerStartPc == startPc) flags |= GenTreeBlockFlags.HandlerEntry;
                if (RangesIntersect(startPc, endPc, clause.TryStartPc, clause.TryEndPc)) flags |= GenTreeBlockFlags.InTryRegion;
                if (RangesIntersect(startPc, endPc, clause.HandlerStartPc, clause.HandlerEndPc)) flags |= GenTreeBlockFlags.InHandlerRegion;
            }

            return flags;
        }

        // A catch handler is entered with the exception object as its only stack value.
        private List<StackValue> CreateEntryStack(int startPc)
        {
            if (!TryGetStackDepthAtPc(startPc, out int depth))
                throw Fail(startPc, ILOpCode.Nop, "Missing stack-depth state for block entry.");

            var stack = new List<StackValue>(Math.Max(depth, 4));
            if (_catchEntryPcs.Contains(startPc))
            {
                Push(stack, Node(GenTreeKind.ExceptionObject, startPc, ILOpCode.Nop, stackKind: GenStackKind.Ref));
                return stack;
            }
            for (int i = 0; i < depth; i++)
            {
                var temp = GetStackEntryTemp(startPc, i, null, GenStackKind.Unknown);
                Push(stack, TempLoad(startPc, ILOpCode.Nop, temp));
            }
            return stack;
        }

        private int EntryStackDepth(int startPc)
        {
            int depth = TryGetStackDepthAtPc(startPc, out int d) ? d : 0;
            return _catchEntryPcs.Contains(startPc) ? 0 : depth;
        }

        private int[] ComputeStackDepths(CilMethodBody body, short[] pops, short[] pushes, RuntimeModule module, RuntimeMethod method)
        {
            var instructions = body.Instructions;
            var result = new int[instructions.Length];
            Array.Fill(result, UnreachableStackDepth);

            var queue = new Queue<int>();

            AddEntry(0, 0);
            foreach (var clause in body.ExceptionClauses)
                AddEntry(clause.HandlerStartPc, clause.Kind is CilExceptionClauseKind.Catch or CilExceptionClauseKind.Filter ? 1 : 0);

            while (queue.Count != 0)
            {
                int pc = queue.Dequeue();
                if ((uint)pc >= (uint)instructions.Length)
                    continue;

                int inDepth = result[pc];
                var ins = instructions[pc];

                int outDepth = ins.Op == ILOpCode.Leave ? 0 : checked(inDepth - pops[pc] + pushes[pc]);
                if (outDepth < 0)
                    throw Fail(pc, ins.Op, $"Negative evaluation stack depth. In={inDepth}, pop={pops[pc]}, push={pushes[pc]}.");
                if (outDepth > body.MaxStack)
                    throw Fail(pc, ins.Op, $"Evaluation stack depth {outDepth} exceeds MaxStack {body.MaxStack}.");

                switch (ins.Op)
                {
                    case ILOpCode.Br:
                    case ILOpCode.Leave:
                        AddEntry(ins.TargetPc, outDepth);
                        break;

                    case ILOpCode.Brtrue:
                    case ILOpCode.Brfalse:
                    case >= ILOpCode.Beq and <= ILOpCode.Blt_Un:
                        AddEntry(ins.TargetPc, outDepth);
                        AddEntry(pc + 1, outDepth);
                        break;

                    case ILOpCode.Switch:
                        foreach (int target in body.GetSwitchTargets(ins))
                            AddEntry(target, outDepth);
                        AddEntry(pc + 1, outDepth);
                        break;

                    case ILOpCode.Ret:
                    case ILOpCode.Throw:
                    case ILOpCode.Rethrow:
                    case ILOpCode.Endfinally:
                    case ILOpCode.Endfilter:
                    case ILOpCode.Jmp:
                        break;

                    default:
                        if (!IsNoReturnCall(module, method, ins))
                            AddEntry(pc + 1, outDepth);
                        break;
                }
            }

            return result;

            void AddEntry(int pc, int depth)
            {
                if ((uint)pc >= (uint)instructions.Length)
                    return;

                int existing = result[pc];
                if (existing != UnreachableStackDepth)
                {
                    if (existing != depth)
                        throw Fail(pc, ILOpCode.Nop, $"Inconsistent stack depth at pc {pc}: existing={existing}, incoming={depth}.");
                    return;
                }

                result[pc] = depth;
                queue.Enqueue(pc);
            }
        }

        private List<int> ComputeLeaders(CilMethodBody body, int[] stackDepthAtPc, bool splitAfterCalls, RuntimeModule module, RuntimeMethod method)
        {
            var instructions = body.Instructions;
            int instructionCount = instructions.Length;
            if (instructionCount == 0)
                return new List<int>();

            var isLeader = new bool[instructionCount];

            AddReachableLeader(0);

            for (int pc = 0; pc < instructionCount; pc++)
            {
                if (stackDepthAtPc[pc] == UnreachableStackDepth)
                    continue;

                var ins = instructions[pc];
                switch (ins.Op)
                {
                    case ILOpCode.Br:
                    case ILOpCode.Leave:
                        AddReachableLeader(ins.TargetPc);
                        break;

                    case ILOpCode.Brtrue:
                    case ILOpCode.Brfalse:
                    case >= ILOpCode.Beq and <= ILOpCode.Blt_Un:
                        AddReachableLeader(ins.TargetPc);
                        AddReachableLeader(pc + 1);
                        break;

                    case ILOpCode.Switch:
                        foreach (int target in body.GetSwitchTargets(ins))
                            AddReachableLeader(target);
                        AddReachableLeader(pc + 1);
                        break;
                }

                if (splitAfterCalls && IsInlineContinuationBoundary(body, module, method, pc, ins))
                    AddReachableLeader(pc + 1);

                if (IsBlockTerminator(ins.Op) || IsNoReturnCall(module, method, ins))
                    AddReachableLeader(pc + 1);
            }

            foreach (var clause in body.ExceptionClauses)
            {
                AddReachableLeader(clause.TryStartPc);
                AddReachableLeader(clause.TryEndPc);
                AddReachableLeader(clause.HandlerStartPc);
                AddReachableLeader(clause.HandlerEndPc);
            }

            var leaders = new List<int>();
            for (int pc = 0; pc < isLeader.Length; pc++)
            {
                if (isLeader[pc])
                    leaders.Add(pc);
            }

            return leaders;

            void AddReachableLeader(int pc)
            {
                if ((uint)pc < (uint)instructionCount && stackDepthAtPc[pc] != UnreachableStackDepth)
                    isLeader[pc] = true;
            }
        }

        private bool IsInlineContinuationBoundary(CilMethodBody body, RuntimeModule module, RuntimeMethod method, int pc, in CilInstruction instruction)
        {
            if (instruction.Op is not (ILOpCode.Call or ILOpCode.Callvirt) || (instruction.Prefixes & CilPrefix.Constrained) != 0)
                return false;

            if (pc + 1 >= body.Instructions.Length)
                return false;

            try
            {
                var callee = _rts.ResolveMethodInMethodContext(module, instruction.Token, method);
                var calleeBody = callee.CilBody;
                var calleeModule = callee.BodyModule;
                if (calleeBody is null || calleeModule is null)
                    return false;

                if (callee.MethodId == _method.MethodId || callee.HasInternalCall || callee.HasNoInlining || callee.DoesNotReturn)
                    return false;

                if (StringComparer.Ordinal.Equals(callee.Name, ".cctor"))
                    return false;

                if (RequiresTypeInitializationBeforeCall(callee) && FindTypeInitializer(callee.DeclaringType) is not null)
                    return false;

                if (calleeBody.ExceptionClauses.Length != 0)
                    return false;

                int argCount = callee.HasThis ? callee.ParameterTypes.Length + 1 : callee.ParameterTypes.Length;
                if (!AnalyzeInlineCandidate(callee, calleeModule, calleeBody, argCount, out var info))
                    return false;

                if (info.HasControlFlow && info.HasCall)
                    return false;

                if (info.HasBackwardBranch && !callee.HasAggressiveInlining)
                    return false;

                return info.HasControlFlow;
            }
            catch (GenTreeBuildException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        // A call to a [DoesNotReturn] method ends its block like throw; through a virtual slot an override might return.
        private bool IsNoReturnCall(RuntimeModule module, RuntimeMethod context, in CilInstruction ins)
            => ins.Op is ILOpCode.Call or ILOpCode.Callvirt &&
               IsNoReturnCallee(_rts.ResolveMethodInMethodContext(module, ins.Token, context), ins.Op);

        private static bool IsNoReturnCallee(RuntimeMethod method, ILOpCode op)
            => method.DoesNotReturn && (op == ILOpCode.Call || !method.IsVirtual || method.IsFinal);

        private static bool IsBlockTerminator(ILOpCode op)
            => op is ILOpCode.Br or ILOpCode.Leave or ILOpCode.Brtrue or ILOpCode.Brfalse or ILOpCode.Switch or
                ILOpCode.Ret or ILOpCode.Throw or ILOpCode.Rethrow or ILOpCode.Endfinally or ILOpCode.Endfilter or ILOpCode.Jmp or
                (>= ILOpCode.Beq and <= ILOpCode.Blt_Un);

        public ImmutableArray<RuntimeType> InstantiatedTypes
            => _instantiatedTypes.Count == 0
                ? ImmutableArray<RuntimeType>.Empty
                : _instantiatedTypes.Values.ToImmutableArray();

        private RuntimeType[] BuildArgTypes(RuntimeMethod method)
        {
            int count = method.HasThis ? method.ParameterTypes.Length + 1 : method.ParameterTypes.Length;
            var result = new RuntimeType[count];
            for (int i = 0; i < count; i++)
                result[i] = GetArgType(method, i);
            return result;
        }

        internal static bool IsLengthGetter(RuntimeMethod? method)
        {
            return method is not null &&
                   method.HasThis &&
                   !method.IsStatic &&
                   method.ParameterTypes.Length == 0 &&
                   method.ReturnType.PrimitiveKind == RuntimePrimitiveKind.Int32 &&
                   StringComparer.Ordinal.Equals(method.Name, "get_Length") &&
                   StringComparer.Ordinal.Equals(method.DeclaringType.Namespace, "System") &&
                   StringComparer.Ordinal.Equals(method.DeclaringType.Name, "Array");
        }
        private RuntimeType GetArgType(RuntimeMethod method, int argIndex)
        {
            if (method.HasThis)
            {
                if (argIndex == 0)
                {
                    if (method.DeclaringType.IsValueType)
                        return _rts.GetByRefType(method.DeclaringType);
                    return method.DeclaringType;
                }
                return method.ParameterTypes[argIndex - 1];
            }
            return method.ParameterTypes[argIndex];
        }

        private ImmutableArray<GenTreeBlock> BuildBlocks(List<int> leaders)
        {
            _pcToBlockId = new Dictionary<int, int>(leaders.Count);
            _deferredInlineBlocks.Clear();

            for (int i = 0; i < leaders.Count; i++)
                _pcToBlockId[leaders[i]] = i;

            _nextDynamicBlockId = leaders.Count;
            _nextSyntheticPc = _body.Instructions.Length + 1;

            // Blocks are imported from the entry and the handler entries along the edges that remain after branches on
            // constants are folded; a block nothing reaches stays empty, so its calls never become dependencies.
            var blocks = new GenTreeBlock?[leaders.Count];
            var pending = new SortedSet<int> { 0 };
            foreach (var clause in _body.ExceptionClauses)
            {
                if (_pcToBlockId.TryGetValue(clause.HandlerStartPc, out int handlerBlock))
                    pending.Add(handlerBlock);
            }
            int scannedInlineBlocks = 0;
            while (pending.Count != 0)
            {
                int i = pending.Min;
                pending.Remove(i);
                if (blocks[i] is not null)
                    continue;

                int startPc = leaders[i];
                int hardEndPc = (i + 1 < leaders.Count) ? leaders[i + 1] : _body.Instructions.Length;
                var block = BuildBlock(i, startPc, hardEndPc);
                blocks[i] = block;
                AddPendingRootSuccessors(block, blocks, pending);
                for (; scannedInlineBlocks < _deferredInlineBlocks.Count; scannedInlineBlocks++)
                    AddPendingRootSuccessors(_deferredInlineBlocks[scannedInlineBlocks], blocks, pending);
            }

            var built = new List<GenTreeBlock>(leaders.Count);
            for (int i = 0; i < leaders.Count; i++)
            {
                int startPc = leaders[i];
                int hardEndPc = (i + 1 < leaders.Count) ? leaders[i + 1] : _body.Instructions.Length;
                built.Add(blocks[i] ?? CreateBlock(i, startPc, hardEndPc, new List<GenTree>(), new List<int>(), exitStackDepth: 0));
            }

            _deferredInlineBlocks.Sort(static (left, right) => left.Id.CompareTo(right.Id));
            for (int i = 0; i < _deferredInlineBlocks.Count; i++)
            {
                var block = _deferredInlineBlocks[i];
                if (block.Id != built.Count)
                    throw Fail(block.StartPc, ILOpCode.Nop, $"Non-dense inline block id {block.Id}; next expected id is {built.Count}.");
                built.Add(block);
            }

            return built.ToImmutableArray();
        }

        private static void AddPendingRootSuccessors(GenTreeBlock block, GenTreeBlock?[] rootBlocks, SortedSet<int> pending)
        {
            foreach (int successor in block.SuccessorBlockIds)
            {
                if ((uint)successor < (uint)rootBlocks.Length && rootBlocks[successor] is null)
                    pending.Add(successor);
            }
        }

        // Arguments and locals of the method being imported, or of an inlinee mapped onto caller temps.
        private sealed class ImportFrame
        {
            public RuntimeModule Module { get; }
            public RuntimeMethod Method { get; }
            public CilMethodBody Body { get; }
            public int InlineDepth { get; }
            public int CallPc { get; }
            public RuntimeType[] LocalTypes { get; }
            public GenTemp[]? ArgTemps { get; }
            public StackValue?[]? ArgSubstitutions { get; }
            public GenTemp[]? LocalTemps { get; }
            public HashSet<int> DefaultValueLocals { get; }
            public Func<int, bool> IsLeader { get; }

            public ImportFrame(
                RuntimeModule module,
                RuntimeMethod method,
                CilMethodBody body,
                int inlineDepth,
                int callPc,
                RuntimeType[] localTypes,
                GenTemp[]? argTemps,
                StackValue?[]? argSubstitutions,
                GenTemp[]? localTemps,
                HashSet<int> defaultValueLocals,
                Func<int, bool> isLeader)
            {
                Module = module;
                Method = method;
                Body = body;
                InlineDepth = inlineDepth;
                CallPc = callPc;
                LocalTypes = localTypes;
                ArgTemps = argTemps;
                ArgSubstitutions = argSubstitutions;
                LocalTemps = localTemps;
                DefaultValueLocals = defaultValueLocals;
                IsLeader = isLeader;
            }

            public bool IsInline => CallPc >= 0;
            public int NodePc(int pc) => CallPc >= 0 ? CallPc : pc;
            public bool HasInstruction(int pc, ILOpCode op)
                => (uint)pc < (uint)Body.Instructions.Length && Body.Instructions[pc].Op == op && !IsLeader(pc);
        }

        private ImportFrame _rootFrame = null!;

        private RuntimeType ResolveTypeIn(ImportFrame frame, int token)
            => _rts.ResolveTypeInMethodContext(frame.Module, token, frame.Method);

        private RuntimeMethod ResolveMethodIn(ImportFrame frame, int token)
            => _rts.ResolveMethodInMethodContext(frame.Module, token, frame.Method);

        // A local qualifies when every access is "ldloca x; initobj T; ldloc x": a default-value producer.
        private static HashSet<int> FindDefaultValueLocals(CilMethodBody body)
        {
            var result = new HashSet<int>();
            var instructions = body.Instructions;
            var rejected = new HashSet<int>();
            for (int pc = 0; pc < instructions.Length; pc++)
            {
                var ins = instructions[pc];
                if (ins.Op is not (ILOpCode.Ldloc or ILOpCode.Stloc or ILOpCode.Ldloca))
                    continue;
                int local = ins.Int32;
                if (ins.Op == ILOpCode.Ldloca &&
                    pc + 2 < instructions.Length &&
                    instructions[pc + 1].Op == ILOpCode.Initobj &&
                    instructions[pc + 2].Op == ILOpCode.Ldloc &&
                    instructions[pc + 2].Int32 == local)
                {
                    result.Add(local);
                    pc += 2;
                    continue;
                }
                rejected.Add(local);
            }
            result.ExceptWith(rejected);
            return result;
        }

        private StackValue LoadArgument(ImportFrame frame, int index, int pc, ILOpCode op)
        {
            if (!frame.IsInline)
            {
                var t = CheckedArgType(index, pc);
                return new StackValue(Node(GenTreeKind.Arg, pc, op, type: t, stackKind: StackKindOf(t), int32: index), t, StackKindOf(t));
            }
            return LoadInlineArg(frame.ArgTemps!, frame.ArgSubstitutions!, index, pc, op);
        }

        private StackValue ArgumentAddress(ImportFrame frame, int index, int pc, ILOpCode op)
        {
            if (!frame.IsInline)
            {
                var byRef = _rts.GetByRefType(CheckedArgType(index, pc));
                return new StackValue(Node(GenTreeKind.ArgAddr, pc, op, type: byRef, stackKind: GenStackKind.ByRef, int32: index), byRef, GenStackKind.ByRef);
            }
            return TempAddress(pc, op, CheckedInlineArgTemp(frame.ArgTemps!, index, pc, op));
        }

        private void StoreArgument(ImportFrame frame, List<GenTree> statements, List<StackValue> stack, int index, GenTree value, int pc, ILOpCode op)
        {
            if (!frame.IsInline)
            {
                var targetType = CheckedArgType(index, pc);
                AppendLocalLikeStore(statements, stack, pc, op, GenTreeKind.StoreArg, GenTreeKind.ArgAddr, index, targetType, CoerceToStorage(value, targetType, pc));
                return;
            }
            var temp = CheckedInlineArgTemp(frame.ArgTemps!, index, pc, op);
            AppendLocalLikeStore(statements, stack, pc, op, GenTreeKind.StoreTemp, GenTreeKind.TempAddr, temp.Index, temp.Type, CoerceToStorage(value, temp.Type, pc));
        }

        private StackValue LoadLocalValue(ImportFrame frame, int index, int pc, ILOpCode op)
        {
            if (!frame.IsInline)
            {
                var t = CheckedLocalType(index, pc);
                return new StackValue(Node(GenTreeKind.Local, pc, op, type: t, stackKind: StackKindOf(t), int32: index), t, StackKindOf(t));
            }
            return TempLoad(pc, op, CheckedInlineLocalTemp(frame.LocalTemps!, index, pc, op));
        }

        private StackValue LocalAddress(ImportFrame frame, int index, int pc, ILOpCode op)
        {
            if (!frame.IsInline)
            {
                var byRef = _rts.GetByRefType(CheckedLocalType(index, pc));
                return new StackValue(Node(GenTreeKind.LocalAddr, pc, op, type: byRef, stackKind: GenStackKind.ByRef, int32: index), byRef, GenStackKind.ByRef);
            }
            return TempAddress(pc, op, CheckedInlineLocalTemp(frame.LocalTemps!, index, pc, op));
        }

        private void StoreLocalValue(ImportFrame frame, List<GenTree> statements, List<StackValue> stack, int index, GenTree value, int pc, ILOpCode op)
        {
            if (!frame.IsInline)
            {
                var targetType = CheckedLocalType(index, pc);
                AppendLocalLikeStore(statements, stack, pc, op, GenTreeKind.StoreLocal, GenTreeKind.LocalAddr, index, targetType, CoerceToStorage(value, targetType, pc));
                return;
            }
            var temp = CheckedInlineLocalTemp(frame.LocalTemps!, index, pc, op);
            AppendLocalLikeStore(statements, stack, pc, op, GenTreeKind.StoreTemp, GenTreeKind.TempAddr, temp.Index, temp.Type, CoerceToStorage(value, temp.Type, pc));
        }

        // IL lets an unmanaged pointer flow into byref storage; the tree makes that conversion explicit.
        private GenTree CoerceToStorage(GenTree value, RuntimeType? targetType, int pc)
        {
            if (targetType?.Kind != RuntimeTypeKind.ByRef || value.StackKind is not (GenStackKind.Ptr or GenStackKind.NativeInt or GenStackKind.NativeUInt))
                return value;
            return Node(GenTreeKind.Unary, pc, GenTreeOperator.PtrToByRef, type: targetType, stackKind: GenStackKind.ByRef, operands: One(value));
        }

        // Imports one non-branching instruction, or an idiom starting at it. Returns the instructions consumed.
        private int ImportInstruction(
            ImportFrame frame,
            List<StackValue> stack,
            List<GenTree> statements,
            List<int>? successorPcs,
            int pc,
            out bool terminatedBlock)
        {
            terminatedBlock = false;
            var ins = frame.Body.Instructions[pc];
            int npc = frame.NodePc(pc);
            var op = ins.Op;
            switch (op)
            {
                case ILOpCode.Nop:
                    return 1;

                case ILOpCode.Ldc_I4:
                    Push(stack, Node(GenTreeKind.ConstI4, npc, op, stackKind: GenStackKind.I4, int32: ins.Int32));
                    return 1;

                case ILOpCode.Ldc_I8:
                    Push(stack, Node(GenTreeKind.ConstI8, npc, op, stackKind: GenStackKind.I8, int64: ins.Operand));
                    return 1;

                case ILOpCode.Ldc_R4:
                    Push(stack, Node(GenTreeKind.ConstR4Bits, npc, op, stackKind: GenStackKind.R4, int32: ins.Int32));
                    return 1;

                case ILOpCode.Ldc_R8:
                    Push(stack, Node(GenTreeKind.ConstR8Bits, npc, op, stackKind: GenStackKind.R8, int64: ins.Operand));
                    return 1;

                case ILOpCode.Ldnull:
                    Push(stack, Node(GenTreeKind.ConstNull, npc, op, stackKind: GenStackKind.Null));
                    return 1;

                case ILOpCode.Ldstr:
                    MarkInstantiatedType(_rts.SystemString);
                    Push(stack, Node(GenTreeKind.ConstString, npc, op, type: _rts.SystemString, stackKind: GenStackKind.Ref,
                        int32: ins.Token, text: frame.Module.Md.GetUserString(MetadataToken.Rid(ins.Token))));
                    return 1;

                case ILOpCode.Ldarg:
                    Push(stack, LoadArgument(frame, ins.Int32, npc, op));
                    return 1;

                case ILOpCode.Ldarga:
                    Push(stack, ArgumentAddress(frame, ins.Int32, npc, op));
                    return 1;

                case ILOpCode.Starg:
                    StoreArgument(frame, statements, stack, ins.Int32, Pop(stack, npc, op).Node, npc, op);
                    return 1;

                case ILOpCode.Ldloc:
                    Push(stack, LoadLocalValue(frame, ins.Int32, npc, op));
                    return 1;

                case ILOpCode.Ldloca:
                    if (frame.DefaultValueLocals.Contains(ins.Int32) &&
                        frame.HasInstruction(pc + 1, ILOpCode.Initobj) &&
                        frame.HasInstruction(pc + 2, ILOpCode.Ldloc))
                    {
                        var t = ResolveTypeIn(frame, frame.Body.Instructions[pc + 1].Token);
                        if (t.IsValueType)
                            MarkInstantiatedType(t);
                        Push(stack, Node(GenTreeKind.DefaultValue, npc, ILOpCode.Initobj, type: t, stackKind: StackKindOf(t), runtimeType: t));
                        return 3;
                    }
                    Push(stack, LocalAddress(frame, ins.Int32, npc, op));
                    return 1;

                case ILOpCode.Stloc:
                    StoreLocalValue(frame, statements, stack, ins.Int32, Pop(stack, npc, op).Node, npc, op);
                    return 1;

                case ILOpCode.Pop:
                    {
                        var value = Pop(stack, npc, op);
                        if (value.Node.Kind != GenTreeKind.ExceptionObject)
                            AppendImporterStatement(statements, stack, CreateDiscardStatement(value.Node, npc, op));
                        return 1;
                    }

                case ILOpCode.Dup:
                    {
                        var value = Pop(stack, npc, op);
                        var temp = CreateDupTemp(value.Type, value.StackKind);
                        AppendImporterStatement(statements, stack, Node(GenTreeKind.StoreTemp, npc, op, operands: One(value.Node), int32: temp.Index));
                        Push(stack, TempLoad(npc, op, temp));
                        Push(stack, TempLoad(npc, op, temp));
                        return 1;
                    }

                case ILOpCode.Neg:
                case ILOpCode.Not:
                    {
                        var value = Pop(stack, npc, op);
                        PushImportedValue(stack, statements, Node(GenTreeKind.Unary, npc, op, type: ActualType(value.StackKind) ?? value.Type, stackKind: value.StackKind,
                            operands: One(value.Node)));
                        return 1;
                    }

                case ILOpCode.Castclass:
                case ILOpCode.Isinst:
                case ILOpCode.Box:
                case ILOpCode.Unbox_Any:
                    EmitTypeOperation(frame, stack, statements, npc, ins);
                    return 1;

                case ILOpCode.Add:
                case ILOpCode.Add_Ovf:
                case ILOpCode.Add_Ovf_Un:
                case ILOpCode.Sub:
                case ILOpCode.Sub_Ovf:
                case ILOpCode.Sub_Ovf_Un:
                case ILOpCode.Mul:
                case ILOpCode.Mul_Ovf:
                case ILOpCode.Mul_Ovf_Un:
                case ILOpCode.Div:
                case ILOpCode.Div_Un:
                case ILOpCode.Rem:
                case ILOpCode.Rem_Un:
                case ILOpCode.And:
                case ILOpCode.Or:
                case ILOpCode.Xor:
                case ILOpCode.Shl:
                case ILOpCode.Shr:
                case ILOpCode.Shr_Un:
                case ILOpCode.Ceq:
                case ILOpCode.Clt:
                case ILOpCode.Clt_Un:
                case ILOpCode.Cgt:
                case ILOpCode.Cgt_Un:
                    EmitBinary(stack, statements, npc, op);
                    return 1;

                case ILOpCode.Conv_I1:
                case ILOpCode.Conv_I2:
                case ILOpCode.Conv_I4:
                case ILOpCode.Conv_I8:
                case ILOpCode.Conv_R4:
                case ILOpCode.Conv_R8:
                case ILOpCode.Conv_U4:
                case ILOpCode.Conv_U8:
                case ILOpCode.Conv_U2:
                case ILOpCode.Conv_U1:
                case ILOpCode.Conv_I:
                case ILOpCode.Conv_U:
                case ILOpCode.Conv_R_Un:
                case ILOpCode.Conv_Ovf_I1:
                case ILOpCode.Conv_Ovf_U1:
                case ILOpCode.Conv_Ovf_I2:
                case ILOpCode.Conv_Ovf_U2:
                case ILOpCode.Conv_Ovf_I4:
                case ILOpCode.Conv_Ovf_U4:
                case ILOpCode.Conv_Ovf_I8:
                case ILOpCode.Conv_Ovf_U8:
                case ILOpCode.Conv_Ovf_I:
                case ILOpCode.Conv_Ovf_U:
                case ILOpCode.Conv_Ovf_I1_Un:
                case ILOpCode.Conv_Ovf_I2_Un:
                case ILOpCode.Conv_Ovf_I4_Un:
                case ILOpCode.Conv_Ovf_I8_Un:
                case ILOpCode.Conv_Ovf_U1_Un:
                case ILOpCode.Conv_Ovf_U2_Un:
                case ILOpCode.Conv_Ovf_U4_Un:
                case ILOpCode.Conv_Ovf_U8_Un:
                case ILOpCode.Conv_Ovf_I_Un:
                case ILOpCode.Conv_Ovf_U_Un:
                    return EmitConversion(frame, stack, statements, pc, npc, op);

                case ILOpCode.Call:
                case ILOpCode.Callvirt:
                    return EmitCall(frame, stack, statements, successorPcs, pc, out terminatedBlock);

                case ILOpCode.Newobj:
                    EmitNewObject(frame, stack, statements, npc, ins);
                    return 1;

                case ILOpCode.Ldftn:
                    return EmitFunctionPointerOrDelegate(frame, stack, statements, pc, npc);

                case ILOpCode.Calli:
                    EmitIndirectCall(frame, stack, statements, npc, ins);
                    return 1;

                case ILOpCode.Ldtoken:
                    return EmitTypeTokenIdiom(frame, stack, statements, pc, npc);

                case ILOpCode.Ldfld:
                case ILOpCode.Ldflda:
                case ILOpCode.Stfld:
                case ILOpCode.Ldsfld:
                case ILOpCode.Ldsflda:
                case ILOpCode.Stsfld:
                    return EmitField(frame, stack, statements, pc, npc);

                case ILOpCode.Ldobj:
                    EmitLoadIndirect(stack, statements, npc, op, ResolveTypeIn(frame, ins.Token));
                    return 1;

                case ILOpCode.Ldind_I1:
                case ILOpCode.Ldind_U1:
                case ILOpCode.Ldind_I2:
                case ILOpCode.Ldind_U2:
                case ILOpCode.Ldind_I4:
                case ILOpCode.Ldind_U4:
                case ILOpCode.Ldind_I8:
                case ILOpCode.Ldind_I:
                case ILOpCode.Ldind_R4:
                case ILOpCode.Ldind_R8:
                case ILOpCode.Ldind_Ref:
                    EmitLoadIndirect(stack, statements, npc, op, null);
                    return 1;

                case ILOpCode.Stobj:
                    EmitStoreIndirect(stack, statements, npc, op, ResolveTypeIn(frame, ins.Token));
                    return 1;

                case ILOpCode.Stind_I1:
                case ILOpCode.Stind_I2:
                case ILOpCode.Stind_I4:
                case ILOpCode.Stind_I8:
                case ILOpCode.Stind_I:
                case ILOpCode.Stind_R4:
                case ILOpCode.Stind_R8:
                case ILOpCode.Stind_Ref:
                    EmitStoreIndirect(stack, statements, npc, op, null);
                    return 1;

                case ILOpCode.Initobj:
                    EmitInitobj(stack, statements, npc, ResolveTypeIn(frame, ins.Token));
                    return 1;

                case ILOpCode.Newarr:
                    {
                        var length = Pop(stack, npc, op);
                        var elemType = ResolveTypeIn(frame, ins.Token);
                        var arrayType = _rts.GetArrayType(elemType);
                        MarkInstantiatedType(arrayType);
                        PushImportedValue(stack, statements, Node(GenTreeKind.NewArray, npc, op, type: arrayType, stackKind: GenStackKind.Ref,
                            operands: One(length.Node), runtimeType: elemType));
                        return 1;
                    }

                case ILOpCode.Ldlen:
                    {
                        var array = Pop(stack, npc, op);
                        PushImportedValue(stack, statements, Node(GenTreeKind.ArrayLength, npc, op, type: _rts.FindPrimitive(RuntimePrimitiveKind.Int32),
                            stackKind: GenStackKind.I4, operands: One(array.Node)));
                        return 1;
                    }

                case ILOpCode.Ldelem:
                case ILOpCode.Ldelem_I1:
                case ILOpCode.Ldelem_U1:
                case ILOpCode.Ldelem_I2:
                case ILOpCode.Ldelem_U2:
                case ILOpCode.Ldelem_I4:
                case ILOpCode.Ldelem_U4:
                case ILOpCode.Ldelem_I8:
                case ILOpCode.Ldelem_I:
                case ILOpCode.Ldelem_R4:
                case ILOpCode.Ldelem_R8:
                case ILOpCode.Ldelem_Ref:
                    {
                        var index = Pop(stack, npc, op);
                        var array = Pop(stack, npc, op);
                        var elemType = ArrayElementType(frame, array, ins);
                        PushImportedValue(stack, statements, Node(GenTreeKind.ArrayElement, npc, op, type: elemType, stackKind: StackKindOf(elemType),
                            operands: Two(array.Node, index.Node), runtimeType: elemType));
                        return 1;
                    }

                case ILOpCode.Ldelema:
                    {
                        var index = Pop(stack, npc, op);
                        var array = Pop(stack, npc, op);
                        var elemType = ResolveTypeIn(frame, ins.Token);
                        var byRef = _rts.GetByRefType(elemType);
                        PushImportedValue(stack, statements, Node(GenTreeKind.ArrayElementAddr, npc, op, type: byRef, stackKind: GenStackKind.ByRef,
                            operands: Two(array.Node, index.Node), runtimeType: elemType));
                        return 1;
                    }

                case ILOpCode.Stelem:
                case ILOpCode.Stelem_I:
                case ILOpCode.Stelem_I1:
                case ILOpCode.Stelem_I2:
                case ILOpCode.Stelem_I4:
                case ILOpCode.Stelem_I8:
                case ILOpCode.Stelem_R4:
                case ILOpCode.Stelem_R8:
                case ILOpCode.Stelem_Ref:
                    {
                        var value = Pop(stack, npc, op);
                        var index = Pop(stack, npc, op);
                        var array = Pop(stack, npc, op);
                        var elemType = ArrayElementType(frame, array, ins);
                        AppendImporterStatement(statements, stack, Node(GenTreeKind.StoreArrayElement, npc, op,
                            operands: ImmutableArray.Create(array.Node, index.Node, value.Node), runtimeType: elemType));
                        return 1;
                    }

                case ILOpCode.Sizeof:
                    Push(stack, Node(GenTreeKind.SizeOf, npc, op, stackKind: GenStackKind.I4, runtimeType: ResolveTypeIn(frame, ins.Token)));
                    return 1;

                case ILOpCode.Localloc:
                    EmitLocalloc(stack, statements, npc);
                    return 1;

                default:
                    throw Fail(npc, op, $"Unsupported opcode '{op}'.");
            }
        }

        private RuntimeType ArrayElementType(ImportFrame frame, StackValue array, in CilInstruction ins)
        {
            if (ins.Op is ILOpCode.Ldelem or ILOpCode.Stelem)
                return ResolveTypeIn(frame, ins.Token);

            RuntimeType? declared = array.Type is { Kind: RuntimeTypeKind.Array } arrayType ? arrayType.ElementType : null;
            RuntimeType opcodeType = ins.Op switch
            {
                ILOpCode.Ldelem_I1 or ILOpCode.Stelem_I1 => _rts.FindPrimitive(RuntimePrimitiveKind.Int8),
                ILOpCode.Ldelem_U1 => _rts.FindPrimitive(RuntimePrimitiveKind.UInt8),
                ILOpCode.Ldelem_I2 or ILOpCode.Stelem_I2 => _rts.FindPrimitive(RuntimePrimitiveKind.Int16),
                ILOpCode.Ldelem_U2 => _rts.FindPrimitive(RuntimePrimitiveKind.UInt16),
                ILOpCode.Ldelem_I4 or ILOpCode.Stelem_I4 => _rts.FindPrimitive(RuntimePrimitiveKind.Int32),
                ILOpCode.Ldelem_U4 => _rts.FindPrimitive(RuntimePrimitiveKind.UInt32),
                ILOpCode.Ldelem_I8 or ILOpCode.Stelem_I8 => _rts.FindPrimitive(RuntimePrimitiveKind.Int64),
                ILOpCode.Ldelem_I or ILOpCode.Stelem_I => _rts.FindPrimitive(RuntimePrimitiveKind.NativeInt),
                ILOpCode.Ldelem_R4 or ILOpCode.Stelem_R4 => _rts.FindPrimitive(RuntimePrimitiveKind.Single),
                ILOpCode.Ldelem_R8 or ILOpCode.Stelem_R8 => _rts.FindPrimitive(RuntimePrimitiveKind.Double),
                _ => _rts.SystemObject,
            };
            // The declared element type keeps the tree as precise as the array; the opcode only fixes its storage shape.
            if (declared is not null && (opcodeType == _rts.SystemObject ? declared.IsReferenceType : SameStorage(declared, opcodeType)))
                return declared;
            return opcodeType;
        }

        private bool SameStorage(RuntimeType left, RuntimeType right)
        {
            if (left.IsReferenceType || right.IsReferenceType)
                return left.IsReferenceType && right.IsReferenceType;
            _rts.EnsureRuntimeTypeReady(left);
            _rts.EnsureRuntimeTypeReady(right);
            return left.SizeOf == right.SizeOf && StackKindOf(left) == StackKindOf(right);
        }

        private RuntimeType? IndirectionType(ILOpCode op, GenTree address)
        {
            RuntimeType? pointee = address.Type is { Kind: RuntimeTypeKind.ByRef or RuntimeTypeKind.Pointer } addressType ? addressType.ElementType : null;
            RuntimeType opcodeType = op switch
            {
                ILOpCode.Ldind_I1 or ILOpCode.Stind_I1 => _rts.FindPrimitive(RuntimePrimitiveKind.Int8),
                ILOpCode.Ldind_U1 => _rts.FindPrimitive(RuntimePrimitiveKind.UInt8),
                ILOpCode.Ldind_I2 or ILOpCode.Stind_I2 => _rts.FindPrimitive(RuntimePrimitiveKind.Int16),
                ILOpCode.Ldind_U2 => _rts.FindPrimitive(RuntimePrimitiveKind.UInt16),
                ILOpCode.Ldind_I4 or ILOpCode.Stind_I4 => _rts.FindPrimitive(RuntimePrimitiveKind.Int32),
                ILOpCode.Ldind_U4 => _rts.FindPrimitive(RuntimePrimitiveKind.UInt32),
                ILOpCode.Ldind_I8 or ILOpCode.Stind_I8 => _rts.FindPrimitive(RuntimePrimitiveKind.Int64),
                ILOpCode.Ldind_I or ILOpCode.Stind_I => _rts.FindPrimitive(RuntimePrimitiveKind.NativeInt),
                ILOpCode.Ldind_R4 or ILOpCode.Stind_R4 => _rts.FindPrimitive(RuntimePrimitiveKind.Single),
                ILOpCode.Ldind_R8 or ILOpCode.Stind_R8 => _rts.FindPrimitive(RuntimePrimitiveKind.Double),
                _ => _rts.SystemObject,
            };
            if (pointee is not null && (opcodeType == _rts.SystemObject ? pointee.IsReferenceType : SameStorage(pointee, opcodeType)))
                return pointee;
            return opcodeType;
        }

        private void EmitLoadIndirect(List<StackValue> stack, List<GenTree> statements, int pc, ILOpCode op, RuntimeType? type)
        {
            var address = Pop(stack, pc, op);
            var t = type ?? IndirectionType(op, address.Node)!;
            PushImportedValue(stack, statements, Node(GenTreeKind.LoadIndirect, pc, op, type: t, stackKind: StackKindOf(t), operands: One(address.Node), runtimeType: t));
        }

        private void EmitStoreIndirect(List<StackValue> stack, List<GenTree> statements, int pc, ILOpCode op, RuntimeType? type)
        {
            var value = Pop(stack, pc, op);
            var address = Pop(stack, pc, op);
            var t = type ?? IndirectionType(op, address.Node)!;
            if (!TryRetargetStructMaterializationToAddress(statements, pc, op, address.Node, t, value.Node))
                AppendImporterStatement(statements, stack, Node(GenTreeKind.StoreIndirect, pc, op, operands: Two(address.Node, CoerceToStorage(value.Node, t, pc)), runtimeType: t));
        }

        private void EmitInitobj(List<StackValue> stack, List<GenTree> statements, int pc, RuntimeType type)
        {
            var address = Pop(stack, pc, ILOpCode.Initobj);
            if (type.IsValueType)
                MarkInstantiatedType(type);
            var init = Node(GenTreeKind.DefaultValue, pc, ILOpCode.Initobj, type: type, stackKind: StackKindOf(type), runtimeType: type);
            GenTree target = address.Node;
            GenTreeKind? store = target.Kind switch
            {
                GenTreeKind.LocalAddr => GenTreeKind.StoreLocal,
                GenTreeKind.ArgAddr => GenTreeKind.StoreArg,
                GenTreeKind.TempAddr => GenTreeKind.StoreTemp,
                _ => null,
            };
            if (store is GenTreeKind storeKind)
            {
                AppendLocalLikeStore(statements, stack, pc, ILOpCode.Initobj, storeKind, target.Kind, target.Int32, type, init);
                return;
            }
            AppendImporterStatement(statements, stack, MarkExplicitInit(Node(GenTreeKind.StoreIndirect, pc, ILOpCode.Initobj, operands: Two(target, init), runtimeType: type)));
        }

        private void EmitTypeOperation(ImportFrame frame, List<StackValue> stack, List<GenTree> statements, int pc, in CilInstruction ins)
        {
            var op = ins.Op;
            var value = Pop(stack, pc, op);
            var operandType = ResolveTypeIn(frame, ins.Token);
            GenTreeKind kind;
            RuntimeType type;
            GenStackKind stackKind;
            switch (op)
            {
                case ILOpCode.Castclass:
                case ILOpCode.Isinst:
                    kind = op == ILOpCode.Castclass ? GenTreeKind.CastClass : GenTreeKind.IsInst;
                    type = operandType.IsValueType ? _rts.SystemObject : operandType;
                    stackKind = GenStackKind.Ref;
                    if (op == ILOpCode.Castclass &&
                        value.Node.Kind is GenTreeKind.DelegateCombine or GenTreeKind.DelegateRemove &&
                        value.Type is not null && _rts.IsAssignableTo(value.Type, operandType))
                    {
                        Push(stack, value);
                        return;
                    }
                    break;
                case ILOpCode.Box:
                    if (!operandType.IsValueType)
                    {
                        Push(stack, value);
                        return;
                    }
                    MarkInstantiatedType(operandType);
                    kind = GenTreeKind.Box;
                    type = _rts.SystemObject;
                    stackKind = GenStackKind.Ref;
                    break;
                default:
                    if (!operandType.IsValueType)
                    {
                        kind = GenTreeKind.CastClass;
                        type = operandType;
                        stackKind = GenStackKind.Ref;
                        break;
                    }
                    kind = GenTreeKind.UnboxAny;
                    type = operandType;
                    stackKind = StackKindOf(operandType);
                    break;
            }

            if (IsProvenTypeCheck(kind, value, statements, operandType))
            {
                Push(stack, value);
                return;
            }

            PushImportedValue(stack, statements, Node(kind, pc, op, type: type, stackKind: stackKind, operands: One(value.Node), int32: ins.Token, runtimeType: operandType));
        }

        private void EmitBinary(List<StackValue> stack, List<GenTree> statements, int pc, ILOpCode op)
        {
            var right = Pop(stack, pc, op);
            var left = Pop(stack, pc, op);

            if (op is ILOpCode.Add or ILOpCode.Sub &&
                left.StackKind is GenStackKind.Ptr or GenStackKind.ByRef &&
                right.StackKind is GenStackKind.Ptr or GenStackKind.ByRef)
            {
                if (op == ILOpCode.Add)
                    throw Fail(pc, op, "Adding two pointers is not valid IL.");
                PushImportedValue(stack, statements, Node(GenTreeKind.PointerDiff, pc, op, stackKind: GenStackKind.NativeInt,
                    operands: Two(left.Node, right.Node), int32: 1));
                return;
            }

            if (op is ILOpCode.Add or ILOpCode.Sub && left.StackKind is GenStackKind.Ptr or GenStackKind.ByRef)
            {
                var (index, elementSize) = DecomposeScaledIndex(right.Node);
                if (op == ILOpCode.Sub)
                    index = Node(GenTreeKind.Unary, pc, ILOpCode.Neg, type: index.Type, stackKind: index.StackKind, operands: One(index));
                PushImportedValue(stack, statements, Node(GenTreeKind.PointerElementAddr, pc, op, stackKind: GenStackKind.Ptr,
                    operands: Two(left.Node, index), int32: elementSize));
                return;
            }

            if (op == ILOpCode.Div &&
                left.Node is { Kind: GenTreeKind.PointerDiff, Int32: 1 } difference &&
                right.Node is { Kind: GenTreeKind.ConstI4 } size && size.Int32 > 0)
            {
                PushImportedValue(stack, statements, Node(GenTreeKind.PointerDiff, pc, op, stackKind: GenStackKind.NativeInt,
                    operands: difference.Operands, int32: size.Int32));
                return;
            }

            RuntimeType? type;
            GenStackKind stackKind;
            if (op is ILOpCode.Ceq or ILOpCode.Clt or ILOpCode.Clt_Un or ILOpCode.Cgt or ILOpCode.Cgt_Un)
            {
                type = null;
                stackKind = GenStackKind.I4;
            }
            else
            {
                // ECMA-335 III.1.5: int32 combined with native int widens; a shift keeps its value operand's kind.
                stackKind = op is not (ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Shr_Un) &&
                    left.StackKind == GenStackKind.I4 && right.StackKind is GenStackKind.NativeInt or GenStackKind.NativeUInt
                    ? right.StackKind
                    : left.StackKind;
                type = ActualType(stackKind) ?? left.Type;
            }

            PushImportedValue(stack, statements, Node(GenTreeKind.Binary, pc, op, type: type, stackKind: stackKind, operands: Two(left.Node, right.Node)));
        }

        // Arithmetic yields the stack's own type, never the narrow or enum type its operands were loaded as.
        private RuntimeType? ActualType(GenStackKind kind) => kind switch
        {
            GenStackKind.I4 => _rts.FindPrimitive(RuntimePrimitiveKind.Int32),
            GenStackKind.I8 => _rts.FindPrimitive(RuntimePrimitiveKind.Int64),
            GenStackKind.NativeInt => _rts.FindPrimitive(RuntimePrimitiveKind.NativeInt),
            GenStackKind.NativeUInt => _rts.FindPrimitive(RuntimePrimitiveKind.NativeUInt),
            GenStackKind.R4 => _rts.FindPrimitive(RuntimePrimitiveKind.Single),
            GenStackKind.R8 => _rts.FindPrimitive(RuntimePrimitiveKind.Double),
            _ => null,
        };

        // "conv.i idx; ldc size; mul" is how IL scales a pointer offset; the tree keeps the index and the scale apart.
        private static (GenTree Index, int ElementSize) DecomposeScaledIndex(GenTree offset)
        {
            if (offset is { Kind: GenTreeKind.Binary, Operator: GenTreeOperator.Mul } multiply &&
                multiply.Operands[1] is { Kind: GenTreeKind.ConstI4 } scale &&
                scale.Int32 > 0)
            {
                return (multiply.Operands[0], scale.Int32);
            }
            return (offset, 1);
        }

        private int EmitConversion(ImportFrame frame, List<StackValue> stack, List<GenTree> statements, int pc, int npc, ILOpCode op)
        {
            var value = Pop(stack, npc, op);
            var (kind, flags) = ConversionOf(op);
            int consumed = 1;

            // conv.r.un followed by a narrowing to r4 or r8 is one unsigned-to-float conversion.
            if (op == ILOpCode.Conv_R_Un)
            {
                if (frame.HasInstruction(pc + 1, ILOpCode.Conv_R4))
                {
                    kind = NumericConvKind.R4;
                    consumed = 2;
                }
                else if (frame.HasInstruction(pc + 1, ILOpCode.Conv_R8))
                {
                    consumed = 2;
                }
            }

            bool isChecked = (flags & NumericConvFlags.Checked) != 0;
            if (!isChecked && IsNoOpConversion(kind, value.StackKind))
            {
                Push(stack, value);
                return consumed;
            }

            PushImportedValue(stack, statements, Node(GenTreeKind.Conv, npc, op, stackKind: StackKindOf(kind), operands: One(value.Node),
                convKind: kind, convFlags: flags));
            return consumed;
        }

        // Stack values of 32 bits or fewer are int32 already; pointers are native integers.
        private static bool IsNoOpConversion(NumericConvKind kind, GenStackKind source)
            => (kind is NumericConvKind.I4 or NumericConvKind.U4 && source == GenStackKind.I4) ||
               (kind is NumericConvKind.NativeInt or NumericConvKind.NativeUInt && source == GenStackKind.Ptr);

        private static (NumericConvKind Kind, NumericConvFlags Flags) ConversionOf(ILOpCode op) => op switch
        {
            ILOpCode.Conv_I1 => (NumericConvKind.I1, NumericConvFlags.None),
            ILOpCode.Conv_U1 => (NumericConvKind.U1, NumericConvFlags.None),
            ILOpCode.Conv_I2 => (NumericConvKind.I2, NumericConvFlags.None),
            ILOpCode.Conv_U2 => (NumericConvKind.U2, NumericConvFlags.None),
            ILOpCode.Conv_I4 => (NumericConvKind.I4, NumericConvFlags.None),
            ILOpCode.Conv_U4 => (NumericConvKind.U4, NumericConvFlags.None),
            ILOpCode.Conv_I8 => (NumericConvKind.I8, NumericConvFlags.None),
            ILOpCode.Conv_U8 => (NumericConvKind.U8, NumericConvFlags.SourceUnsigned),
            ILOpCode.Conv_R4 => (NumericConvKind.R4, NumericConvFlags.None),
            ILOpCode.Conv_R8 => (NumericConvKind.R8, NumericConvFlags.None),
            ILOpCode.Conv_I => (NumericConvKind.NativeInt, NumericConvFlags.None),
            ILOpCode.Conv_U => (NumericConvKind.NativeUInt, NumericConvFlags.SourceUnsigned),
            ILOpCode.Conv_R_Un => (NumericConvKind.R8, NumericConvFlags.SourceUnsigned),
            ILOpCode.Conv_Ovf_I1 => (NumericConvKind.I1, NumericConvFlags.Checked),
            ILOpCode.Conv_Ovf_U1 => (NumericConvKind.U1, NumericConvFlags.Checked),
            ILOpCode.Conv_Ovf_I2 => (NumericConvKind.I2, NumericConvFlags.Checked),
            ILOpCode.Conv_Ovf_U2 => (NumericConvKind.U2, NumericConvFlags.Checked),
            ILOpCode.Conv_Ovf_I4 => (NumericConvKind.I4, NumericConvFlags.Checked),
            ILOpCode.Conv_Ovf_U4 => (NumericConvKind.U4, NumericConvFlags.Checked),
            ILOpCode.Conv_Ovf_I8 => (NumericConvKind.I8, NumericConvFlags.Checked),
            ILOpCode.Conv_Ovf_U8 => (NumericConvKind.U8, NumericConvFlags.Checked),
            ILOpCode.Conv_Ovf_I => (NumericConvKind.NativeInt, NumericConvFlags.Checked),
            ILOpCode.Conv_Ovf_U => (NumericConvKind.NativeUInt, NumericConvFlags.Checked),
            ILOpCode.Conv_Ovf_I1_Un => (NumericConvKind.I1, NumericConvFlags.Checked | NumericConvFlags.SourceUnsigned),
            ILOpCode.Conv_Ovf_I2_Un => (NumericConvKind.I2, NumericConvFlags.Checked | NumericConvFlags.SourceUnsigned),
            ILOpCode.Conv_Ovf_I4_Un => (NumericConvKind.I4, NumericConvFlags.Checked | NumericConvFlags.SourceUnsigned),
            ILOpCode.Conv_Ovf_I8_Un => (NumericConvKind.I8, NumericConvFlags.Checked | NumericConvFlags.SourceUnsigned),
            ILOpCode.Conv_Ovf_U1_Un => (NumericConvKind.U1, NumericConvFlags.Checked | NumericConvFlags.SourceUnsigned),
            ILOpCode.Conv_Ovf_U2_Un => (NumericConvKind.U2, NumericConvFlags.Checked | NumericConvFlags.SourceUnsigned),
            ILOpCode.Conv_Ovf_U4_Un => (NumericConvKind.U4, NumericConvFlags.Checked | NumericConvFlags.SourceUnsigned),
            ILOpCode.Conv_Ovf_U8_Un => (NumericConvKind.U8, NumericConvFlags.Checked | NumericConvFlags.SourceUnsigned),
            ILOpCode.Conv_Ovf_I_Un => (NumericConvKind.NativeInt, NumericConvFlags.Checked | NumericConvFlags.SourceUnsigned),
            ILOpCode.Conv_Ovf_U_Un => (NumericConvKind.NativeUInt, NumericConvFlags.Checked | NumericConvFlags.SourceUnsigned),
            _ => throw new GenTreeBuildException($"Not a conversion opcode: {op}."),
        };

        private void EmitLocalloc(List<StackValue> stack, List<GenTree> statements, int pc)
        {
            var size = Pop(stack, pc, ILOpCode.Localloc).Node;
            GenTree count = size;
            int elementSize = 1;
            if (count is { Kind: GenTreeKind.Binary, Operator: GenTreeOperator.MulOvfUn } multiply &&
                multiply.Operands[1] is { Kind: GenTreeKind.ConstI4 } scale && scale.Int32 > 0)
            {
                count = multiply.Operands[0];
                elementSize = scale.Int32;
            }
            if (count is { Kind: GenTreeKind.Conv, ConvKind: NumericConvKind.NativeUInt } widen)
                count = widen.Operands[0];

            if (count.Kind == GenTreeKind.ConstI4)
            {
                long byteCount = unchecked((long)(uint)count.Int32 * elementSize);
                if (byteCount == 0)
                {
                    PushImportedValue(stack, statements, Node(GenTreeKind.ConstI4, pc, ILOpCode.Localloc, stackKind: GenStackKind.Ptr, int32: 0));
                    return;
                }
                if (byteCount < uint.MaxValue)
                {
                    PushImportedValue(stack, statements, Node(GenTreeKind.StackAlloc, pc, ILOpCode.Localloc, stackKind: GenStackKind.Ptr,
                        int32: elementSize, int64: byteCount));
                    return;
                }
            }

            PushImportedValue(stack, statements, Node(GenTreeKind.StackAlloc, pc, ILOpCode.Localloc, stackKind: GenStackKind.Ptr,
                operands: One(count), int32: elementSize));
        }

        // Type predicates and comparisons fold; any other typeof yields the type's canonical RuntimeType.
        private int EmitTypeTokenIdiom(ImportFrame frame, List<StackValue> stack, List<GenTree> statements, int pc, int npc)
        {
            var instructions = frame.Body.Instructions;
            if (MetadataToken.Table(instructions[pc].Token) is MetadataToken.TypeDef or MetadataToken.TypeRef or MetadataToken.TypeSpec &&
                frame.HasInstruction(pc + 1, ILOpCode.Call) &&
                IsWellKnownTypeMethod(ResolveMethodIn(frame, instructions[pc + 1].Token), "GetTypeFromHandle"))
            {
                var type = ResolveTypeIn(frame, instructions[pc].Token);
                if (frame.HasInstruction(pc + 2, ILOpCode.Callvirt))
                {
                    var predicate = ResolveMethodIn(frame, instructions[pc + 2].Token);
                    if (IsWellKnownTypeMethod(predicate, predicate.Name) && predicate.Name is "get_IsValueType" or "get_IsPrimitive" or "get_IsEnum")
                    {
                        Push(stack, Node(GenTreeKind.ConstI4, npc, ILOpCode.Ldtoken, stackKind: GenStackKind.I4,
                            int32: RuntimeTypePredicate(predicate.Name, type, npc) ? 1 : 0));
                        return 3;
                    }
                }
                if (frame.HasInstruction(pc + 2, ILOpCode.Ldtoken) &&
                    frame.HasInstruction(pc + 3, ILOpCode.Call) &&
                    IsWellKnownTypeMethod(ResolveMethodIn(frame, instructions[pc + 3].Token), "GetTypeFromHandle") &&
                    frame.HasInstruction(pc + 4, ILOpCode.Ceq))
                {
                    var other = ResolveTypeIn(frame, instructions[pc + 2].Token);
                    Push(stack, Node(GenTreeKind.ConstI4, npc, ILOpCode.Ldtoken, stackKind: GenStackKind.I4,
                        int32: RuntimeTypesEqual(type, other, npc, ILOpCode.Ldtoken) ? 1 : 0));
                    return 5;
                }
                PushImportedValue(stack, statements, CreateTypeObject(type, npc));
                return 2;
            }
            throw Fail(npc, ILOpCode.Ldtoken, "ldtoken is only supported for type objects.");
        }

        private GenTree CreateTypeObject(RuntimeType type, int pc)
        {
            if (type.Kind == RuntimeTypeKind.TypeParam)
                throw Fail(pc, ILOpCode.Ldtoken, "typeof requires a closed generic context.");

            RuntimeMethod fromHandle = RuntimeTypeFromHandle();
            AddDirectDependency(fromHandle);
            GenTree handle = Node(GenTreeKind.TypeHandle, pc, ILOpCode.Ldtoken, stackKind: GenStackKind.NativeInt, runtimeType: type);
            return Node(GenTreeKind.Call, pc, ILOpCode.Call, type: fromHandle.ReturnType, stackKind: GenStackKind.Ref,
                operands: One(handle), int32: 1, method: fromHandle);
        }

        private RuntimeMethod RuntimeTypeFromHandle()
        {
            RuntimeType runtimeType = _rts.GetRequiredNamedType("std", "System", "RuntimeType");
            _rts.EnsureConstructedMembers(runtimeType);
            foreach (RuntimeMethod method in runtimeType.Methods)
            {
                if (method.IsStatic && StringComparer.Ordinal.Equals(method.Name, "FromHandle"))
                    return method;
            }
            throw new MissingMethodException("System.RuntimeType.FromHandle is missing from the core library.");
        }

        // Native type handles are MethodTables, with the type's names and flags in an info block one pointer before
        // the MethodTable; the register VM answers these queries from its type system.
        private bool TryImportRuntimeTypeQuery(List<StackValue> stack, List<GenTree> statements, RuntimeMethod method, int pc, ILOpCode op)
        {
            if (method.Name == "RhGetObjectTypeHandle")
            {
                var obj = Pop(stack, pc, op);
                PushImportedValue(stack, statements, LoadObjectTypeHandle(obj.Node, pc));
                return true;
            }
            if (_rts.Target.IsRegisterBytecode)
                return false;

            int slot = method.Name switch
            {
                "RhGetTypeName" => 0,
                "RhGetTypeNamespace" => 1,
                "RhGetTypeFullName" => 2,
                "RhGetTypeDisplayName" => 3,
                "RhGetTypeAssemblyName" => 4,
                "RhGetTypeFlags" => 5,
                _ => -1,
            };
            if (slot < 0)
                return false;

            var handle = Pop(stack, pc, op);
            int pointerSize = _rts.Target.PointerSize;
            GenTree infoSlot = Node(GenTreeKind.PointerElementAddr, pc, GenTreeOperator.None, stackKind: GenStackKind.Ptr,
                operands: Two(handle.Node, ConstI4(pc, op, -1)), int32: pointerSize);
            GenTree info = Node(GenTreeKind.LoadIndirect, pc, ILOpCode.Ldind_I, stackKind: GenStackKind.Ptr, operands: One(infoSlot));
            GenTree field = Node(GenTreeKind.PointerElementAddr, pc, GenTreeOperator.None, stackKind: GenStackKind.Ptr,
                operands: Two(info, ConstI4(pc, op, slot)), int32: pointerSize);
            RuntimeType resultType = method.ReturnType;
            PushImportedValue(stack, statements, Node(GenTreeKind.LoadIndirect, pc, slot == 5 ? ILOpCode.Ldind_I4 : ILOpCode.Ldind_Ref,
                type: resultType, stackKind: StackKindOf(resultType), operands: One(field), runtimeType: resultType));
            return true;
        }

        private GenTree LoadObjectTypeHandle(GenTree receiver, int pc)
        {
            if (_rts.Target.IsRegisterBytecode)
            {
                return Node(GenTreeKind.Conv, pc, ILOpCode.Conv_U, stackKind: GenStackKind.NativeInt,
                    operands: One(LoadRuntimeObjectTypeId(receiver, pc)), convKind: NumericConvKind.NativeUInt, convFlags: NumericConvFlags.None);
            }

            GenTree objectAddress = Node(GenTreeKind.PointerElementAddr, pc, GenTreeOperator.None, stackKind: GenStackKind.Ptr,
                operands: Two(receiver, ConstI4(pc, ILOpCode.Ldc_I4, 0)), int32: 1);
            return Node(GenTreeKind.LoadIndirect, pc, ILOpCode.Ldind_I, type: _rts.FindPrimitive(RuntimePrimitiveKind.NativeInt),
                stackKind: GenStackKind.NativeInt, operands: One(objectAddress));
        }

        private static bool IsWellKnownTypeMethod(RuntimeMethod method, string name)
            => method.DeclaringType.Namespace == "System" && method.DeclaringType.Name == "Type" &&
               StringComparer.Ordinal.Equals(method.Name, name);

        private int EmitFunctionPointerOrDelegate(ImportFrame frame, List<StackValue> stack, List<GenTree> statements, int pc, int npc)
        {
            var ldftn = frame.Body.Instructions[pc];
            var targetMethod = ResolveMethodIn(frame, ldftn.Token);
            if (frame.HasInstruction(pc + 1, ILOpCode.Newobj))
            {
                var ctor = ResolveMethodIn(frame, frame.Body.Instructions[pc + 1].Token);
                if (IsDelegateType(ctor.DeclaringType) && ctor.ParameterTypes.Length == 2)
                {
                    var target = Pop(stack, npc, ILOpCode.Newobj);
                    EmitNewDelegate(stack, statements, npc, ctor.DeclaringType, targetMethod,
                        target.Node.Kind == GenTreeKind.ConstNull ? ImmutableArray<GenTree>.Empty : One(target.Node));
                    return 2;
                }
            }
            EmitFunctionPointerLoad(stack, statements, npc, targetMethod);
            return 1;
        }

        private bool IsDelegateType(RuntimeType type)
        {
            for (RuntimeType? t = type.BaseType; t is not null; t = t.BaseType)
            {
                if (t.Namespace == "System" && t.Name is "MulticastDelegate")
                    return true;
            }
            return false;
        }
        private void EmitFunctionPointerLoad(List<StackValue> stack, List<GenTree> statements, int pc, RuntimeMethod targetMethod)
        {
            if (!targetMethod.IsStatic)
                throw Fail(pc, ILOpCode.Ldftn, "Function pointer target must be static.");
            if (targetMethod.IsExtern || targetMethod.HasInternalCall || targetMethod.CilBody is null)
                throw Fail(pc, ILOpCode.Ldftn, "Function pointer target must use a managed method body.");

            AddDirectDependency(targetMethod);
            if (RequiresTypeInitializationBeforeCall(targetMethod))
            {
                _rts.EnsureConstructedMembers(targetMethod.DeclaringType);
                RuntimeMethod? cctor = FindTypeInitializer(targetMethod.DeclaringType);
                if (cctor is not null)
                {
                    targetMethod.RequiresClassInitializationEntryCheck = true;
                    AddDirectDependency(cctor);
                }
            }

            PushImportedValue(stack, statements, Node(
                GenTreeKind.FunctionPointer,
                pc,
                ILOpCode.Ldftn,
                stackKind: GenStackKind.Ptr,
                int64: targetMethod.MethodId,
                method: targetMethod));
        }

        private void EmitIndirectCall(ImportFrame frame, List<StackValue> stack, List<GenTree> statements, int pc, in CilInstruction ins)
        {
            var signature = _rts.ResolveCalliSignatureInMethodContext(frame.Module, ins.Token, frame.Method);
            if (signature.Kind != RuntimeTypeKind.FunctionPointer)
                throw Fail(pc, ins.Op, $"Calli signature resolved to '{signature.Kind}'.");
            if (signature.FunctionPointerCallingConvention != 0)
                throw Fail(pc, ins.Op, "Only managed function pointer calling convention is supported.");
            int argumentCount = signature.FunctionPointerParameterTypes.Length;
            var operands = PopMany(stack, checked(argumentCount + 1), pc, ins.Op);
            var reordered = ImmutableArray.CreateBuilder<GenTree>(operands.Length);
            reordered.Add(operands[^1]);
            for (int i = 0; i < argumentCount; i++)
                reordered.Add(operands[i]);
            MarkInstantiatedTypeClosure(signature);

            RuntimeType returnElementType = signature.FunctionPointerReturnType
                ?? throw Fail(pc, ins.Op, "Function pointer signature has no return type.");
            bool returnsVoid = !signature.FunctionPointerReturnByRef && IsVoid(returnElementType);
            RuntimeType? returnType = returnsVoid
                ? null
                : signature.FunctionPointerReturnByRef
                    ? _rts.GetByRefType(returnElementType)
                    : returnElementType;

            SpillEvaluationStackForImportBarrier(statements, stack, pc, ins.Op);

            var call = Node(
                GenTreeKind.IndirectCall,
                pc,
                ins.Op,
                type: returnType,
                stackKind: returnsVoid ? GenStackKind.Void : StackKindOf(returnType),
                operands: reordered.MoveToImmutable(),
                int32: argumentCount,
                int64: ins.Token,
                runtimeType: signature);

            if (returnsVoid)
                AppendImporterStatement(statements, stack, Node(GenTreeKind.Eval, pc, ins.Op, operands: One(call)));
            else
                PushImportedValue(stack, statements, call);
        }

        private void EmitNewDelegate(List<StackValue> stack, List<GenTree> statements, int pc, RuntimeType delegateType, RuntimeMethod targetMethod, ImmutableArray<GenTree> operands)
        {
            MarkInstantiatedType(delegateType);
            AddDirectDependency(targetMethod);
            if (!_rts.Target.IsRegisterBytecode && RequiresTypeInitializationBeforeCall(targetMethod))
            {
                _rts.EnsureConstructedMembers(targetMethod.DeclaringType);
                RuntimeMethod? cctor = FindTypeInitializer(targetMethod.DeclaringType);
                if (cctor is not null)
                {
                    targetMethod.RequiresClassInitializationEntryCheck = true;
                    AddDirectDependency(cctor);
                }
            }

            PushImportedValue(stack, statements, Node(GenTreeKind.NewDelegate, pc, ILOpCode.Newobj, type: delegateType, stackKind: GenStackKind.Ref,
                operands: operands, int64: targetMethod.MethodId, runtimeType: delegateType, method: targetMethod));
        }

        private bool IsDelegateInvoke(RuntimeMethod method)
            => !method.IsStatic && StringComparer.Ordinal.Equals(method.Name, "Invoke") && IsDelegateType(method.DeclaringType);

        private static bool IsSystemMethod(RuntimeMethod method, string ns, string type, string name)
            => method.DeclaringType.Namespace == ns && method.DeclaringType.Name == type && StringComparer.Ordinal.Equals(method.Name, name);

        private void EmitDelegateInvoke(List<StackValue> stack, List<GenTree> statements, int pc, ILOpCode op, RuntimeMethod invoke, ImmutableArray<GenTree> args, int token)
        {
            SpillEvaluationStackForImportBarrier(statements, stack, pc, op);

            bool returnsVoid = IsVoid(invoke.ReturnType);
            var call = Node(GenTreeKind.DelegateInvoke,
                pc,
                op,
                type: returnsVoid ? null : invoke.ReturnType,
                stackKind: returnsVoid ? GenStackKind.Void : StackKindOf(invoke.ReturnType),
                operands: args,
                int32: args.Length,
                int64: token,
                method: invoke);

            if (returnsVoid)
                AppendImporterStatement(statements, stack, Node(GenTreeKind.Eval, pc, op, operands: One(call)));
            else
                PushImportedValue(stack, statements, call);
        }

        // The receiver of a constrained call is an address; resolve it the way the runtime would for a closed type.
        private ImmutableArray<GenTree> ResolveConstrainedReceiver(
            ImportFrame frame,
            int pc,
            ILOpCode op,
            RuntimeType constrainedType,
            ref RuntimeMethod method,
            ref bool isVirtual,
            ImmutableArray<GenTree> args)
        {
            var receiver = args[0];
            if (!constrainedType.IsValueType)
            {
                receiver = Node(GenTreeKind.LoadIndirect, pc, op, type: constrainedType, stackKind: StackKindOf(constrainedType),
                    operands: One(receiver), runtimeType: constrainedType);
            }
            else
            {
                RuntimeMethod? implementation = method.IsVirtual || method.DeclaringType.Kind == RuntimeTypeKind.Interface
                    ? _rts.ResolveVirtualMethod(method, constrainedType)
                    : null;
                if (implementation is not null && ReferenceEquals(implementation.DeclaringType, constrainedType))
                {
                    method = implementation;
                    isVirtual = false;
                    return args;
                }

                MarkInstantiatedType(constrainedType);
                var value = Node(GenTreeKind.LoadIndirect, pc, op, type: constrainedType, stackKind: StackKindOf(constrainedType),
                    operands: One(receiver), runtimeType: constrainedType);
                receiver = Node(GenTreeKind.Box, pc, ILOpCode.Box, type: _rts.SystemObject, stackKind: GenStackKind.Ref,
                    operands: One(value), int32: constrainedType.TypeId, runtimeType: constrainedType);
            }

            var rewritten = args.ToBuilder();
            rewritten[0] = receiver;
            return rewritten.ToImmutable();
        }

        // Returns the instructions consumed; idioms built on a call can take the instructions after it too.
        private int EmitCall(ImportFrame frame, List<StackValue> stack, List<GenTree> statements, List<int>? successorPcs, int pc, out bool terminatedBlock)
        {
            terminatedBlock = false;
            var ins = frame.Body.Instructions[pc];
            int npc = frame.NodePc(pc);
            var op = ins.Op;
            bool isVirtual = op == ILOpCode.Callvirt;
            var method = ResolveMethodIn(frame, ins.Token);
            int total = method.ParameterTypes.Length + (method.HasThis ? 1 : 0);
            bool doesNotReturn = IsNoReturnCallee(method, op);

            if (isVirtual &&
                IsSystemMethod(method, "System", "Object", "GetType") &&
                frame.HasInstruction(pc + 1, ILOpCode.Ldtoken) &&
                frame.HasInstruction(pc + 2, ILOpCode.Call) &&
                IsWellKnownTypeMethod(ResolveMethodIn(frame, frame.Body.Instructions[pc + 2].Token), "GetTypeFromHandle") &&
                frame.HasInstruction(pc + 3, ILOpCode.Ceq))
            {
                var receiver = Pop(stack, npc, op);
                var receiverType = receiver.Type ?? _rts.SystemObject;
                if (receiver.Node is { Kind: GenTreeKind.Box, RuntimeType: RuntimeType boxedType } box)
                {
                    receiverType = boxedType;
                    receiver = new StackValue(box.Operands[0], boxedType, StackKindOf(boxedType));
                }
                var targetType = ResolveTypeIn(frame, frame.Body.Instructions[pc + 1].Token);
                ImportObjectTypeEquals(stack, statements, receiver, receiverType, targetType, npc, op);
                return 4;
            }

            if (!isVirtual &&
                (IsSystemMethod(method, "System", "Delegate", "Combine") || IsSystemMethod(method, "System", "Delegate", "Remove")) &&
                method.ParameterTypes.Length == 2)
            {
                var operands = PopMany(stack, 2, npc, op);
                RuntimeType? resultType = operands[0].Type ?? operands[1].Type;
                PushImportedValue(stack, statements, Node(
                    method.Name == "Combine" ? GenTreeKind.DelegateCombine : GenTreeKind.DelegateRemove,
                    npc,
                    op,
                    type: resultType,
                    stackKind: GenStackKind.Ref,
                    operands: operands));
                return 1;
            }

            if (!isVirtual && method.DeclaringType.Namespace == "System" && method.DeclaringType.Name == "Activator" &&
                method.Name == "CreateInstance" && method.MethodGenericArguments.Length == 1)
            {
                EmitCreateInstance(frame, stack, statements, npc, op, method.MethodGenericArguments[0]);
                return 1;
            }
            if (!isVirtual && method.DeclaringType.Namespace == "System.Runtime" && method.DeclaringType.Name == "RuntimeImports" &&
                TryImportRuntimeTypeQuery(stack, statements, method, npc, op))
            {
                return 1;
            }

            if (!isVirtual && RuntimeTypeSystem.IsHardwareAccelerationQuery(method))
            {
                Push(stack, Node(GenTreeKind.ConstI4, npc, op, stackKind: GenStackKind.I4, int32: 0));
                return 1;
            }

            if (!isVirtual &&
                IsSystemMethod(method, "System.Runtime.CompilerServices", "RuntimeHelpers", "IsBitwiseEquatable") &&
                method.MethodGenericArguments.Length == 1)
            {
                Push(stack, Node(GenTreeKind.ConstI4, npc, op, stackKind: GenStackKind.I4,
                    int32: RuntimeTypeSystem.IsBitwiseEquatable(method.MethodGenericArguments[0]) ? 1 : 0));
                return 1;
            }

            if (!isVirtual &&
                IsSystemMethod(method, "System.Runtime.InteropServices", "MemoryMarshal", "GetArrayDataReference"))
            {
                var array = Pop(stack, npc, op);
                PushImportedValue(stack, statements, Node(GenTreeKind.ArrayDataRef, npc, op, stackKind: GenStackKind.ByRef, operands: One(array.Node)));
                return 1;
            }

            var args = PopMany(stack, total, npc, op);

            if ((ins.Prefixes & CilPrefix.Constrained) != 0)
            {
                RuntimeType constrainedType = ResolveTypeIn(frame, ins.ConstrainedToken);
                if (method.HasThis)
                {
                    args = ResolveConstrainedReceiver(frame, npc, op, constrainedType, ref method, ref isVirtual, args);
                }
                else
                {
                    // A static abstract or virtual member resolves against the exact type argument it is called through.
                    method = _rts.ResolveVirtualMethod(method, constrainedType)
                        ?? throw Fail(npc, op, $"{constrainedType.Name} does not implement {method.DeclaringType.Name}.{method.Name}.");
                    isVirtual = false;
                }
            }

            if (isVirtual && IsDelegateInvoke(method))
            {
                EmitDelegateInvoke(stack, statements, npc, op, method, args, ins.Token);
                return 1;
            }

            bool isArrayLength = args.Length == 1 && IsLengthGetter(method);
            bool requiresCallvirtNullCheck = false;
            DevirtualizationReceiverTransform receiverTransform = DevirtualizationReceiverTransform.None;

            if (isVirtual && !isArrayLength)
            {
                if (TryDevirtualizeCall(
                    method,
                    args,
                    statements,
                    out RuntimeMethod devirtualizedMethod,
                    out requiresCallvirtNullCheck,
                    out receiverTransform))
                {
                    method = devirtualizedMethod;
                    isVirtual = false;
                }
                else
                {
                    AddVirtualDependency(method);
                }
            }

            bool requiresTypeInitialization = !isArrayLength && RequiresTypeInitializationBeforeCall(method);
            if (requiresTypeInitialization)
                AddTypeInitializerDependency(method.DeclaringType);

            if (isArrayLength)
            {
                PushImportedValue(stack, statements, Node(
                    GenTreeKind.ArrayLength,
                    npc,
                    op,
                    type: method.ReturnType,
                    stackKind: GenStackKind.I4,
                    operands: args));
                return 1;
            }

            SpillEvaluationStackForImportBarrier(statements, stack, npc, op);
            args = RewriteDevirtualizedReceiver(statements, npc, op, args, method, receiverTransform);

            if (requiresTypeInitialization && NeedsTypeInitialization(method.DeclaringType))
            {
                args = MaterializeTypeInitializationOperands(statements, npc, op, args);
                AppendTypeInitialization(stack, statements, npc, op, method.DeclaringType);
            }

            if (requiresCallvirtNullCheck)
                args = MaterializeCallVirtOperandsAndAppendNullCheck(statements, npc, op, args);

            RuntimeIntrinsicInfo runtimeIntrinsic = default;
            bool hasRuntimeIntrinsic = !isVirtual && RuntimeIntrinsics.TryResolve(method, _rts.Target, out runtimeIntrinsic);
            bool isRuntimeIntrinsic = hasRuntimeIntrinsic && runtimeIntrinsic.IsSpecialImport;
            bool suppressIntrinsicInline = hasRuntimeIntrinsic && runtimeIntrinsic.IsNoInline;

            if (!isVirtual && !isRuntimeIntrinsic && !suppressIntrinsicInline)
            {
                bool rootCall = !frame.IsInline;
                if (TryInlineCall(
                    method,
                    args,
                    statements,
                    rootCall ? successorPcs : null,
                    rootCall ? stack : null,
                    npc,
                    op,
                    out var inlineResult,
                    out bool inlinedGraph,
                    frame.InlineDepth + 1))
                {
                    if (inlinedGraph)
                    {
                        terminatedBlock = true;
                        return 1;
                    }
                    if (inlineResult is not null)
                        Push(stack, inlineResult);
                    return 1;
                }
            }

            if (isRuntimeIntrinsic && runtimeIntrinsic.Id is RuntimeIntrinsicId.VolatileRead or RuntimeIntrinsicId.VolatileWrite)
            {
                EmitVolatileAccess(stack, statements, npc, op, method, args, runtimeIntrinsic.Id);
                return 1;
            }

            if (!isVirtual && !isRuntimeIntrinsic)
                AddDirectDependency(method);

            bool returnsVoid = IsVoid(method.ReturnType);
            GenTreeKind callKind = isVirtual
                ? GenTreeKind.VirtualCall
                : isRuntimeIntrinsic
                    ? GenTreeKind.Intrinsic
                    : GenTreeKind.Call;
            var call = Node(callKind,
                npc,
                op,
                type: returnsVoid ? null : method.ReturnType,
                stackKind: returnsVoid ? GenStackKind.Void : StackKindOf(method.ReturnType),
                operands: args,
                int32: total,
                int64: ins.Token,
                method: method,
                intrinsicId: runtimeIntrinsic.Id);

            if (returnsVoid || doesNotReturn)
                AppendImporterStatement(statements, stack, Node(GenTreeKind.Eval, npc, op, operands: One(call)));
            else
                PushImportedValue(stack, statements, call);

            terminatedBlock = doesNotReturn;
            return 1;
        }

        // Acquire: the load completes in its own statement before the barrier; release: the operands complete before it
        private void EmitVolatileAccess(List<StackValue> stack, List<GenTree> statements, int pc, ILOpCode op, RuntimeMethod method,
            ImmutableArray<GenTree> args, RuntimeIntrinsicId id)
        {
            var valueType = method.ParameterTypes[0].ElementType!;
            var barrier = Node(GenTreeKind.Intrinsic, pc, op, stackKind: GenStackKind.Void, method: method, intrinsicId: id);
            foreach (var arg in args)
                Push(stack, arg);
            if (id == RuntimeIntrinsicId.VolatileRead)
            {
                EmitLoadIndirect(stack, statements, pc, ILOpCode.Ldobj, valueType);
                SpillEvaluationStackForImportBarrier(statements, stack, pc, op);
                AppendImporterStatement(statements, stack, Node(GenTreeKind.Eval, pc, op, operands: One(barrier)));
            }
            else
            {
                SpillEvaluationStackForImportBarrier(statements, stack, pc, op);
                AppendImporterStatement(statements, stack, Node(GenTreeKind.Eval, pc, op, operands: One(barrier)));
                EmitStoreIndirect(stack, statements, pc, ILOpCode.Stobj, valueType);
            }
        }

        private void EmitNewObject(ImportFrame frame, List<StackValue> stack, List<GenTree> statements, int pc, in CilInstruction ins)
            => EmitNewObject(frame, stack, statements, pc, ins.Op, ins.Token, ResolveMethodIn(frame, ins.Token));

        // Activator.CreateInstance<T>, which 'new T()' binds to, for the exact T of this instantiation
        private void EmitCreateInstance(ImportFrame frame, List<StackValue> stack, List<GenTree> statements, int pc, ILOpCode op, RuntimeType type)
        {
            _rts.EnsureConstructedMembers(type);
            foreach (var method in type.Methods)
            {
                if (!method.IsStatic && method.ParameterTypes.Length == 0 && StringComparer.Ordinal.Equals(method.Name, ".ctor"))
                {
                    EmitNewObject(frame, stack, statements, pc, op, token: 0, method);
                    return;
                }
            }
            if (!type.IsValueType)
                throw Fail(pc, op, $"'{type.Namespace}.{type.Name}' has no parameterless constructor.");
            PushImportedValue(stack, statements, Node(GenTreeKind.DefaultValue, pc, GenTreeOperator.None, type: type, stackKind: StackKindOf(type), runtimeType: type));
        }

        private void EmitNewObject(ImportFrame frame, List<StackValue> stack, List<GenTree> statements, int pc, ILOpCode op, int token, RuntimeMethod ctor)
        {
            int argCount = ctor.ParameterTypes.Length;
            var args = PopMany(stack, argCount, pc, op);
            if (RequiresTypeInitializationBeforeNewObject(ctor.DeclaringType))
                AddTypeInitializerDependency(ctor.DeclaringType);

            var t = ctor.DeclaringType;
            if (t.Kind == RuntimeTypeKind.Array)
                throw Fail(pc, op, "Multi-dimensional array construction is not supported by the backend.");
            if (RequiresTypeInitializationBeforeNewObject(t) && NeedsTypeInitialization(t))
            {
                SpillEvaluationStackForImportBarrier(statements, stack, pc, op);
                args = MaterializeTypeInitializationOperands(statements, pc, op, args);
                AppendTypeInitialization(stack, statements, pc, op, t);
            }
            MarkInstantiatedType(t);

            if (t.IsValueType)
            {
                EmitValueTypeNewObject(frame, stack, statements, pc, op, token, argCount, args, ctor, t);
                return;
            }

            AddDirectDependency(ctor);
            PushImportedValue(stack, statements, Node(GenTreeKind.NewObject, pc, op, type: t, stackKind: StackKindOf(t), operands: args,
                int32: argCount, int64: token, method: ctor, runtimeType: t));
        }

        private int EmitField(ImportFrame frame, List<StackValue> stack, List<GenTree> statements, int pc, int npc)
        {
            var ins = frame.Body.Instructions[pc];
            var op = ins.Op;
            var field = _rts.ResolveFieldInMethodContext(frame.Module, ins.Token, frame.Method);
            switch (op)
            {
                case ILOpCode.Ldfld:
                    {
                        var receiver = Pop(stack, npc, op);
                        PushImportedValue(stack, statements, Node(GenTreeKind.Field, npc, op, type: field.FieldType, stackKind: StackKindOf(field.FieldType),
                            operands: One(receiver.Node), field: field, int64: ins.Token));
                        return 1;
                    }

                case ILOpCode.Ldflda:
                    {
                        var receiver = Pop(stack, npc, op);
                        var byRef = _rts.GetByRefType(field.FieldType);
                        PushImportedValue(stack, statements, Node(GenTreeKind.FieldAddr, npc, op, type: byRef, stackKind: GenStackKind.ByRef,
                            operands: One(receiver.Node), field: field, int64: ins.Token));
                        return 1;
                    }

                case ILOpCode.Stfld:
                    {
                        var value = Pop(stack, npc, op);
                        var receiver = Pop(stack, npc, op);
                        AppendImporterStatement(statements, stack, Node(GenTreeKind.StoreField, npc, op,
                            operands: Two(receiver.Node, CoerceToStorage(value.Node, field.FieldType, npc)), field: field, int64: ins.Token));
                        return 1;
                    }

                case ILOpCode.Ldsfld:
                    AddTypeInitializerDependency(field.DeclaringType);
                    AppendTypeInitialization(stack, statements, npc, op, field.DeclaringType);
                    PushImportedValue(stack, statements, Node(GenTreeKind.StaticField, npc, op, type: field.FieldType, stackKind: StackKindOf(field.FieldType),
                        field: field, int64: ins.Token));
                    return 1;

                case ILOpCode.Ldsflda:
                    {
                        if (field.Rva != 0)
                        {
                            var (offset, length) = AddStaticData(field);
                            PushImportedValue(stack, statements, Node(GenTreeKind.StaticData, npc, op, stackKind: GenStackKind.Ptr, int32: offset, int64: length));
                            return frame.HasInstruction(pc + 1, ILOpCode.Conv_U) || frame.HasInstruction(pc + 1, ILOpCode.Conv_I) ? 2 : 1;
                        }
                        AddTypeInitializerDependency(field.DeclaringType);
                        AppendTypeInitialization(stack, statements, npc, op, field.DeclaringType);
                        var byRef = _rts.GetByRefType(field.FieldType);
                        PushImportedValue(stack, statements, Node(GenTreeKind.StaticFieldAddr, npc, op, type: byRef, stackKind: GenStackKind.ByRef,
                            field: field, int64: ins.Token));
                        return 1;
                    }

                default:
                    {
                        AddTypeInitializerDependency(field.DeclaringType);
                        var value = Pop(stack, npc, op);
                        GenTree storedValue = CoerceToStorage(value.Node, field.FieldType, npc);
                        if (NeedsTypeInitialization(field.DeclaringType))
                        {
                            SpillEvaluationStackForImportBarrier(statements, stack, npc, op);
                            storedValue = MaterializeTypeInitializationOperand(statements, npc, op, storedValue);
                            AppendTypeInitialization(stack, statements, npc, op, field.DeclaringType);
                        }
                        AppendImporterStatement(statements, stack, Node(GenTreeKind.StoreStaticField, npc, op, operands: One(storedValue), field: field, int64: ins.Token));
                        return 1;
                    }
            }
        }

        private (int Offset, int Length) AddStaticData(RuntimeField field)
        {
            if (_staticDataOffsets.TryGetValue(field.FieldId, out var range))
                return range;
            byte[] data = _rts.GetFieldRvaData(field);
            range = (_staticData.Count, data.Length);
            _staticData.AddRange(data);
            _staticDataOffsets.Add(field.FieldId, range);
            return range;
        }
        private bool TryInlineCall(
            RuntimeMethod callee,
            ImmutableArray<GenTree> args,
            List<GenTree> statements,
            List<int>? successorPcs,
            List<StackValue>? callerContinuationStack,
            int callPc,
            ILOpCode callOp,
            out GenTree? result,
            out bool terminatedBlock,
            int inlineDepth)
        {
            terminatedBlock = false;
            result = null;

            var body = callee.CilBody;
            var bodyModule = callee.BodyModule;
            if (body is null || bodyModule is null)
                return false;

            if (PcInExceptionHandlerRegion(callPc))
                return false;

            if (!CanInline(callee, bodyModule, body, args, inlineDepth, out var inlineInfo))
                return false;

            var calleeArgTypes = BuildArgTypes(callee);
            if (calleeArgTypes.Length != args.Length)
                return false;

            // Exception regions span contiguous block ids, and inlinee blocks are appended after all of them.
            if (inlineInfo.HasControlFlow &&
                (successorPcs is null || callerContinuationStack is null || inlineDepth > 1 || !_pcToBlockId.ContainsKey(callPc + 1) ||
                 PcInExceptionRegion(callPc)))
            {
                return false;
            }

            if (!_activeInlineMethods.Add(callee.MethodId))
                return false;

            _inlineBudgetRemaining = Math.Max(0, _inlineBudgetRemaining - inlineInfo.Cost);

            try
            {
                MarkInstantiatedMethodContext(callee);
                if (inlineInfo.HasControlFlow)
                {
                    TryImportInlineGraph(callee, bodyModule, body, args, statements, successorPcs!, callerContinuationStack!, callPc, callOp, inlineInfo);
                    terminatedBlock = true;
                    return true;
                }

                var argTemps = new GenTemp[calleeArgTypes.Length];
                var argSubstitutions = new StackValue?[calleeArgTypes.Length];
                for (int i = 0; i < argTemps.Length; i++)
                {
                    var t = calleeArgTypes[i];
                    if (inlineInfo.CanSubstituteArgument(i, args[i]))
                    {
                        argSubstitutions[i] = new StackValue(args[i], args[i].Type, args[i].StackKind);
                        continue;
                    }

                    var temp = CreateInlineTemp(GenTempKind.InlineArg, t, StackKindOf(t));
                    argTemps[i] = temp;
                    statements.Add(Node(GenTreeKind.StoreTemp, callPc, callOp, operands: One(CoerceToStorage(args[i], t, callPc)), int32: temp.Index));
                }

                var localTypes = _rts.ResolveLocalSignatureInMethodContext(bodyModule, body.LocalSignatureToken, callee);
                var localTemps = new GenTemp[localTypes.Length];
                for (int i = 0; i < localTypes.Length; i++)
                {
                    var t = localTypes[i];
                    var temp = CreateInlineTemp(GenTempKind.InlineLocal, t, StackKindOf(t));
                    localTemps[i] = temp;

                    if (inlineInfo.LocalNeedsInit(i))
                    {
                        var init = Node(GenTreeKind.DefaultValue, callPc, ILOpCode.Initobj, type: t, stackKind: StackKindOf(t), runtimeType: t);
                        statements.Add(MarkExplicitInit(Node(GenTreeKind.StoreTemp, callPc, ILOpCode.Stloc, operands: One(init), int32: temp.Index)));
                    }
                }

                var frame = new ImportFrame(bodyModule, callee, body, inlineDepth, callPc, localTypes, argTemps, argSubstitutions, localTemps,
                    DefaultValueLocalsOf(body), static pc => pc == 0);
                var inlineStack = new List<StackValue>(Math.Max(body.MaxStack, 4));
                var instructions = body.Instructions;
                for (int pc = 0; pc < instructions.Length;)
                {
                    var ins = instructions[pc];
                    if (ins.Op == ILOpCode.Ret)
                    {
                        result = IsVoid(callee.ReturnType) ? null : CoerceToStorage(Pop(inlineStack, callPc, ins.Op).Node, callee.ReturnType, callPc);
                        for (int tail = pc + 1; tail < instructions.Length; tail++)
                        {
                            if (instructions[tail].Op != ILOpCode.Nop)
                                throw Fail(callPc, instructions[tail].Op, "Unexpected instruction after inlined return.");
                        }
                        return true;
                    }

                    pc += ImportInstruction(frame, inlineStack, statements, null, pc, out _);
                }

                throw Fail(callPc, ILOpCode.Ret, "Inline candidate has no return.");
            }
            finally
            {
                _activeInlineMethods.Remove(callee.MethodId);
            }
        }

        private readonly Dictionary<CilMethodBody, HashSet<int>> _defaultValueLocalsByBody = new(ReferenceEqualityComparer.Instance);

        private HashSet<int> DefaultValueLocalsOf(CilMethodBody body)
        {
            if (!_defaultValueLocalsByBody.TryGetValue(body, out var locals))
            {
                locals = FindDefaultValueLocals(body);
                _defaultValueLocalsByBody.Add(body, locals);
            }
            return locals;
        }

        private void TryImportInlineGraph(
            RuntimeMethod callee,
            RuntimeModule bodyModule,
            CilMethodBody body,
            ImmutableArray<GenTree> args,
            List<GenTree> callStatements,
            List<int> callSuccessorPcs,
            List<StackValue> callerContinuationStack,
            int callPc,
            ILOpCode callOp,
            InlineCandidateInfo inlineInfo)
        {
            if (inlineInfo.Plan is null)
                throw Fail(callPc, callOp, "Missing inline graph plan.");

            int continuationPc = callPc + 1;
            if (!_pcToBlockId.ContainsKey(continuationPc))
                throw Fail(callPc, callOp, $"Missing continuation block for inlined call at pc {callPc}.");

            var continuationPrefix = new List<StackValue>(callerContinuationStack);
            var calleeArgTypes = BuildArgTypes(callee);
            var argTemps = new GenTemp[calleeArgTypes.Length];
            var argSubstitutions = new StackValue?[calleeArgTypes.Length];

            for (int i = 0; i < argTemps.Length; i++)
            {
                var t = calleeArgTypes[i];
                if (inlineInfo.CanSubstituteArgument(i, args[i]))
                {
                    argSubstitutions[i] = new StackValue(args[i], args[i].Type, args[i].StackKind);
                    continue;
                }

                var temp = CreateInlineGraphTemp(t, StackKindOf(t));
                argTemps[i] = temp;
                callStatements.Add(Node(GenTreeKind.StoreTemp, callPc, callOp, operands: One(CoerceToStorage(args[i], t, callPc)), int32: temp.Index));
            }

            var localTypes = _rts.ResolveLocalSignatureInMethodContext(bodyModule, body.LocalSignatureToken, callee);
            var localTemps = new GenTemp[localTypes.Length];
            for (int i = 0; i < localTypes.Length; i++)
            {
                var t = localTypes[i];
                var temp = CreateInlineGraphTemp(t, StackKindOf(t));
                localTemps[i] = temp;

                if (inlineInfo.LocalNeedsInit(i))
                {
                    var init = Node(GenTreeKind.DefaultValue, callPc, ILOpCode.Initobj, type: t, stackKind: StackKindOf(t), runtimeType: t);
                    callStatements.Add(MarkExplicitInit(Node(GenTreeKind.StoreTemp, callPc, ILOpCode.Stloc, operands: One(init), int32: temp.Index)));
                }
            }

            GenTemp? returnTemp = null;
            if (!IsVoid(callee.ReturnType))
                returnTemp = CreateInlineGraphTemp(callee.ReturnType, StackKindOf(callee.ReturnType));

            var plan = inlineInfo.Plan;
            var leaders = new HashSet<int>(plan.Leaders);
            var frame = new ImportFrame(bodyModule, callee, body, inlineDepth: 1, callPc, localTypes, argTemps, argSubstitutions, localTemps,
                DefaultValueLocalsOf(body), leaders.Contains);
            var context = CreateInlineGraphContext(frame, plan, callPc, continuationPc, returnTemp, continuationPrefix);
            int entrySyntheticPc = context.SyntheticPcForCalleePc(plan.Leaders[0]);

            AddSuccessor(callSuccessorPcs, entrySyntheticPc);
            callStatements.Add(Node(GenTreeKind.Branch, callPc, callOp, targetPc: entrySyntheticPc, targetBlockId: BlockIdForPc(entrySyntheticPc)));
            callerContinuationStack.Clear();

            for (int i = 0; i < plan.Leaders.Length; i++)
            {
                int calleeStartPc = plan.Leaders[i];
                int calleeEndPc = i + 1 < plan.Leaders.Length ? plan.Leaders[i + 1] : body.Instructions.Length;
                _deferredInlineBlocks.Add(BuildInlineGraphBlock(context, calleeStartPc, calleeEndPc));
            }
        }

        private InlineGraphContext CreateInlineGraphContext(
            ImportFrame frame,
            InlineGraphPlan plan,
            int callPc,
            int continuationPc,
            GenTemp? returnTemp,
            List<StackValue> callerContinuationStack)
        {
            var syntheticPcs = new Dictionary<int, int>(plan.Leaders.Length);
            var blockIds = new Dictionary<int, int>(plan.Leaders.Length);

            for (int i = 0; i < plan.Leaders.Length; i++)
            {
                int calleePc = plan.Leaders[i];
                int syntheticPc = _nextSyntheticPc++;
                int blockId = _nextDynamicBlockId++;
                syntheticPcs.Add(calleePc, syntheticPc);
                blockIds.Add(calleePc, blockId);
                _pcToBlockId.Add(syntheticPc, blockId);
            }

            return new InlineGraphContext(frame, plan, callPc, continuationPc, syntheticPcs, blockIds, returnTemp, callerContinuationStack);
        }

        private GenTreeBlock BuildInlineGraphBlock(InlineGraphContext context, int calleeStartPc, int calleeEndPc)
        {
            var frame = context.Frame;
            var instructions = frame.Body.Instructions;
            var statements = new List<GenTree>();
            var stack = CreateInlineGraphEntryStack(context, calleeStartPc);
            var successorPcs = new List<int>(2);
            int pc = calleeStartPc;
            int syntheticStartPc = context.SyntheticPcForCalleePc(calleeStartPc);
            int blockId = context.BlockIdForCalleePc(calleeStartPc);
            int entryDepth = context.StackDepthAt(calleeStartPc);

            while (pc < calleeEndPc)
            {
                var ins = instructions[pc];
                switch (ins.Op)
                {
                    case ILOpCode.Br:
                        {
                            int targetPc = context.SyntheticPcForCalleePc(ins.TargetPc);
                            AddSuccessor(successorPcs, targetPc);
                            SpillStackForBoundaries(statements, stack, successorPcs, context.CallPc, ins.Op);
                            statements.Add(Node(GenTreeKind.Branch, context.CallPc, ins.Op, targetPc: targetPc, targetBlockId: BlockIdForPc(targetPc)));
                            return CreateInlineGraphBlock(blockId, syntheticStartPc, statements, successorPcs, entryDepth, stack.Count);
                        }

                    case ILOpCode.Brtrue:
                    case ILOpCode.Brfalse:
                        {
                            var cond = Pop(stack, context.CallPc, ins.Op);
                            int targetPc = context.SyntheticPcForCalleePc(ins.TargetPc);
                            AddSuccessor(successorPcs, targetPc);
                            if (pc + 1 < instructions.Length)
                                AddSuccessor(successorPcs, context.SyntheticPcForCalleePc(pc + 1));
                            SpillStackForBoundaries(statements, stack, successorPcs, context.CallPc, ins.Op);
                            statements.Add(Node(ins.Op == ILOpCode.Brtrue ? GenTreeKind.BranchTrue : GenTreeKind.BranchFalse,
                                context.CallPc, ins.Op, operands: One(cond.Node), targetPc: targetPc, targetBlockId: BlockIdForPc(targetPc)));
                            return CreateInlineGraphBlock(blockId, syntheticStartPc, statements, successorPcs, entryDepth, stack.Count);
                        }

                    case ILOpCode.Ret:
                        {
                            if (context.ReturnTemp.HasValue)
                            {
                                var returnValue = Pop(stack, context.CallPc, ins.Op);
                                var returnTemp = context.ReturnTemp.Value;
                                statements.Add(Node(GenTreeKind.StoreTemp, context.CallPc, ins.Op,
                                    operands: One(CoerceToStorage(returnValue.Node, returnTemp.Type, context.CallPc)), int32: returnTemp.Index));
                            }

                            var continuationStack = CloneStackValues(context.CallerContinuationStack, context.CallPc, ins.Op);
                            if (context.ReturnTemp.HasValue)
                                continuationStack.Add(TempLoad(context.CallPc, ins.Op, context.ReturnTemp.Value));

                            AddSuccessor(successorPcs, context.ContinuationPc);
                            SpillStackForBoundary(statements, continuationStack, context.ContinuationPc, context.CallPc, ins.Op);
                            statements.Add(Node(GenTreeKind.Branch, context.CallPc, ins.Op, targetPc: context.ContinuationPc, targetBlockId: BlockIdForPc(context.ContinuationPc)));
                            stack.Clear();
                            return CreateInlineGraphBlock(blockId, syntheticStartPc, statements, successorPcs, entryDepth, exitStackDepth: 0);
                        }

                    case ILOpCode.Throw:
                        {
                            var value = Pop(stack, context.CallPc, ins.Op);
                            statements.Add(Node(GenTreeKind.Throw, context.CallPc, ins.Op, operands: One(value.Node)));
                            stack.Clear();
                            return CreateInlineGraphBlock(blockId, syntheticStartPc, statements, successorPcs, entryDepth, exitStackDepth: 0);
                        }

                    default:
                        pc += ImportInstruction(frame, stack, statements, null, pc, out bool terminatedBlock);
                        if (terminatedBlock)
                        {
                            stack.Clear();
                            return CreateInlineGraphBlock(blockId, syntheticStartPc, statements, successorPcs, entryDepth, exitStackDepth: 0);
                        }
                        continue;
                }
            }

            if (pc < instructions.Length)
            {
                int successorPc = context.SyntheticPcForCalleePc(pc);
                AddSuccessor(successorPcs, successorPc);
                SpillStackForBoundary(statements, stack, successorPc, context.CallPc, ILOpCode.Nop);
            }

            return CreateInlineGraphBlock(blockId, syntheticStartPc, statements, successorPcs, entryDepth, stack.Count);
        }

        private bool CanInline(
            RuntimeMethod callee,
            RuntimeModule bodyModule,
            CilMethodBody body,
            ImmutableArray<GenTree> args,
            int inlineDepth,
            out InlineCandidateInfo info)
        {
            info = null!;

            if (callee.MethodId == _method.MethodId)
                return false;
            if (_activeInlineMethods.Contains(callee.MethodId))
                return false;
            if (callee.HasInternalCall || callee.HasNoInlining || callee.DoesNotReturn)
                return false;
            if (StringComparer.Ordinal.Equals(callee.Name, ".cctor"))
                return false;
            if (RequiresTypeInitializationBeforeCall(callee) && FindTypeInitializer(callee.DeclaringType) is not null)
                return false;
            if (body.ExceptionClauses.Length != 0)
                return false;
            if (args.Length != (callee.HasThis ? callee.ParameterTypes.Length + 1 : callee.ParameterTypes.Length))
                return false;
            if (CilStackEffects.GetLocalCount(bodyModule.Md, body.LocalSignatureToken) > (callee.HasAggressiveInlining ? 64 : 24))
                return false;
            if (!callee.HasAggressiveInlining && body.MaxStack > 32)
                return false;
            if (inlineDepth > InlineMaxDepth)
                return false;

            if (!AnalyzeInlineCandidate(callee, bodyModule, body, args.Length, out var candidate))
                return false;

            if (candidate.HasControlFlow && inlineDepth > 1)
                return false;
            if (candidate.HasControlFlow && candidate.HasCall)
                return false;
            if (candidate.HasBackwardBranch && !callee.HasAggressiveInlining)
                return false;
            if (!callee.HasAggressiveInlining && candidate.BasicBlockCount > InlineMaxBasicBlocks)
                return false;

            int budget = DetermineInlineBudget(callee, candidate, args, inlineDepth);
            bool forceInline = callee.HasAggressiveInlining;
            bool allowOverBudget = forceInline && inlineDepth <= InlineMaxForceDepth;
            if (!allowOverBudget && candidate.CodeSize <= InlineSmallOverBudgetSize)
                allowOverBudget = true;

            if (candidate.Cost > budget && !allowOverBudget)
                return false;

            if (candidate.Cost > _inlineBudgetRemaining && !allowOverBudget)
                return false;

            info = candidate;
            return true;
        }

        private bool AnalyzeInlineCandidate(
            RuntimeMethod callee,
            RuntimeModule bodyModule,
            CilMethodBody body,
            int argCount,
            out InlineCandidateInfo info)
        {
            info = null!;

            InlineGraphPlan plan;
            try
            {
                var (pops, pushes) = body.GetStackEffects(bodyModule.Md, !IsVoid(callee.ReturnType));
                var stackDepths = ComputeStackDepths(body, pops, pushes, bodyModule, callee);
                var leaders = ComputeLeaders(body, stackDepths, splitAfterCalls: false, bodyModule, callee);
                if (leaders.Count == 0)
                    return false;
                plan = new InlineGraphPlan(stackDepths, leaders.ToImmutableArray());
            }
            catch (GenTreeBuildException)
            {
                return false;
            }

            int localCount = CilStackEffects.GetLocalCount(bodyModule.Md, body.LocalSignatureToken);
            var argLoadCounts = new int[argCount];
            var argStoreCounts = new int[argCount];
            var argAddressCounts = new int[argCount];
            var localAddressCounts = new int[localCount];
            var localNeedsInit = new bool[localCount];
            var localDefinitelyAssigned = new bool[localCount];

            int cost = 0;
            int instructionCount = 0;
            int loadStoreCount = 0;
            int callCount = 0;
            int returnCount = 0;
            bool returnsValue = false;
            bool hasControlFlow = plan.Leaders.Length > 1;
            bool hasBackwardBranch = false;
            bool hasThrow = false;

            var instructions = body.Instructions;
            for (int i = 0; i < instructions.Length; i++)
            {
                if (plan.StackDepths[i] == UnreachableStackDepth)
                    continue;

                var ins = instructions[i];
                if (!CanTranslateInlineOpcode(ins.Op))
                    return false;

                if (!NoteInlineOperandUse(ins, argLoadCounts, argStoreCounts, argAddressCounts, localAddressCounts, localNeedsInit, localDefinitelyAssigned))
                    return false;

                instructionCount++;
                if (IsInlineLoadStoreOpcode(ins.Op))
                    loadStoreCount++;
                if (IsNoReturnCall(bodyModule, callee, ins))
                {
                    hasControlFlow = true;
                    hasThrow = true;
                }
                else if (ins.Op is ILOpCode.Call or ILOpCode.Callvirt)
                {
                    callCount++;
                }
                if (ins.Op is ILOpCode.Br or ILOpCode.Brtrue or ILOpCode.Brfalse)
                {
                    hasControlFlow = true;
                    if (ins.TargetPc <= i)
                        hasBackwardBranch = true;
                }
                if (ins.Op == ILOpCode.Throw)
                {
                    hasControlFlow = true;
                    hasThrow = true;
                }
                if (ins.Op == ILOpCode.Ret)
                {
                    returnCount++;
                    returnsValue |= !IsVoid(callee.ReturnType);
                }

                cost += InlineOpcodeCost(ins.Op);
            }

            if (returnCount == 0)
                return false;

            if (hasControlFlow)
            {
                for (int i = 0; i < localNeedsInit.Length; i++)
                    localNeedsInit[i] = true;
            }

            bool mostlyLoadStore = instructionCount != 0 &&
                ((instructionCount - loadStoreCount) < 4 || (loadStoreCount * 10) >= instructionCount * 9);
            bool looksLikeWrapper = callCount == 1 && instructionCount <= 8 && !hasControlFlow;

            info = new InlineCandidateInfo(
                cost,
                instructions.Length,
                plan.Leaders.Length,
                argLoadCounts,
                argStoreCounts,
                argAddressCounts,
                localAddressCounts,
                localNeedsInit,
                mostlyLoadStore,
                looksLikeWrapper,
                hasCall: callCount != 0,
                returnsValue: returnsValue,
                hasControlFlow: hasControlFlow,
                hasBackwardBranch: hasBackwardBranch,
                hasThrow: hasThrow,
                plan: hasControlFlow ? plan : null);
            return true;
        }

        private static bool NoteInlineOperandUse(
            in CilInstruction ins,
            int[] argLoadCounts,
            int[] argStoreCounts,
            int[] argAddressCounts,
            int[] localAddressCounts,
            bool[] localNeedsInit,
            bool[] localDefinitelyAssigned)
        {
            int index = ins.Int32;
            switch (ins.Op)
            {
                case ILOpCode.Ldarg:
                    if ((uint)index >= (uint)argLoadCounts.Length)
                        return false;
                    argLoadCounts[index]++;
                    return true;

                case ILOpCode.Ldarga:
                    if ((uint)index >= (uint)argAddressCounts.Length)
                        return false;
                    argAddressCounts[index]++;
                    return true;

                case ILOpCode.Starg:
                    if ((uint)index >= (uint)argStoreCounts.Length)
                        return false;
                    argStoreCounts[index]++;
                    return true;

                case ILOpCode.Ldloc:
                    if ((uint)index >= (uint)localNeedsInit.Length)
                        return false;
                    if (!localDefinitelyAssigned[index])
                        localNeedsInit[index] = true;
                    return true;

                case ILOpCode.Ldloca:
                    if ((uint)index >= (uint)localAddressCounts.Length)
                        return false;
                    localAddressCounts[index]++;
                    if (!localDefinitelyAssigned[index])
                        localNeedsInit[index] = true;
                    return true;

                case ILOpCode.Stloc:
                    if ((uint)index >= (uint)localDefinitelyAssigned.Length)
                        return false;
                    localDefinitelyAssigned[index] = true;
                    return true;

                default:
                    return true;
            }
        }

        private StackValue LoadInlineArg(GenTemp[] argTemps, StackValue?[] argSubstitutions, int index, int pc, ILOpCode op)
        {
            if ((uint)index >= (uint)argTemps.Length)
                throw Fail(pc, op, $"Inline argument index {index} is out of range. Argument count: {argTemps.Length}.");

            if (argSubstitutions[index].HasValue)
                return argSubstitutions[index]!.Value;

            return TempLoad(pc, op, CheckedInlineArgTemp(argTemps, index, pc, op));
        }

        private static bool IsInlineLoadStoreOpcode(ILOpCode op)
        {
            return op is ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Ldloc or ILOpCode.Ldloca or
                         ILOpCode.Ldc_I4 or ILOpCode.Ldc_I8 or ILOpCode.Ldc_R4 or ILOpCode.Ldc_R8 or ILOpCode.Ldnull or
                         ILOpCode.Ldstr or ILOpCode.Initobj or ILOpCode.Sizeof or ILOpCode.Ldtoken or
                         ILOpCode.Starg or ILOpCode.Stloc or ILOpCode.Ldfld or ILOpCode.Ldflda or
                         ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Ldobj or ILOpCode.Stobj or
                         ILOpCode.Ldelem or ILOpCode.Ldelema or ILOpCode.Stelem or ILOpCode.Pop or
                         >= ILOpCode.Ldind_I1 and <= ILOpCode.Ldind_Ref or
                         >= ILOpCode.Stind_Ref and <= ILOpCode.Stind_R8 or
                         >= ILOpCode.Ldelem_I1 and <= ILOpCode.Ldelem_Ref or
                         >= ILOpCode.Stelem_I and <= ILOpCode.Stelem_Ref;
        }

        // Control transfer out of the inlinee and runtime-dependent constructs stay out of inlining.
        private static bool CanTranslateInlineOpcode(ILOpCode op)
            => op is not (ILOpCode.Leave or ILOpCode.Endfinally or ILOpCode.Rethrow or ILOpCode.Switch or ILOpCode.Jmp or
                ILOpCode.Localloc or ILOpCode.Calli or ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Endfilter or
                ILOpCode.Cpblk or ILOpCode.Initblk or ILOpCode.Cpobj or ILOpCode.Arglist or ILOpCode.Mkrefany or
                ILOpCode.Refanyval or ILOpCode.Refanytype or ILOpCode.Unbox or ILOpCode.Ckfinite or ILOpCode.Break or
                ILOpCode.Beq or ILOpCode.Bge or ILOpCode.Bgt or ILOpCode.Ble or ILOpCode.Blt or ILOpCode.Bne_Un or
                ILOpCode.Bge_Un or ILOpCode.Bgt_Un or ILOpCode.Ble_Un or ILOpCode.Blt_Un);

        private static int InlineOpcodeCost(ILOpCode op)
        {
            return op switch
            {
                ILOpCode.Nop => 0,
                ILOpCode.Ldarg or ILOpCode.Ldloc or ILOpCode.Ldc_I4 or ILOpCode.Ldc_I8 or ILOpCode.Ldc_R4 or ILOpCode.Ldc_R8 or ILOpCode.Ldnull => 1,
                ILOpCode.Starg or ILOpCode.Stloc or ILOpCode.Dup => 2,
                ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Ldobj or ILOpCode.Ldelem or ILOpCode.Ldelema => 3,
                >= ILOpCode.Ldind_I1 and <= ILOpCode.Ldind_Ref => 3,
                >= ILOpCode.Ldelem_I1 and <= ILOpCode.Ldelem_Ref => 3,
                ILOpCode.Stfld or ILOpCode.Stsfld or ILOpCode.Stobj or ILOpCode.Stelem => 4,
                >= ILOpCode.Stind_Ref and <= ILOpCode.Stind_R8 => 4,
                >= ILOpCode.Stelem_I and <= ILOpCode.Stelem_Ref => 4,
                ILOpCode.Newobj or ILOpCode.Newarr or ILOpCode.Box => 8,
                ILOpCode.Call => 10,
                ILOpCode.Callvirt => 14,
                ILOpCode.Div or ILOpCode.Div_Un or ILOpCode.Rem or ILOpCode.Rem_Un => 4,
                ILOpCode.Br => 4,
                ILOpCode.Brtrue or ILOpCode.Brfalse => 5,
                ILOpCode.Throw => 24,
                _ => 1,
            };
        }
        private GenTreeBlock CreateBlock(int blockId, int startPc, int endPc, List<GenTree> statements, List<int> successorPcs, int exitStackDepth)
        {
            var succBlockIds = new List<int>(successorPcs.Count);
            for (int i = 0; i < successorPcs.Count; i++)
                succBlockIds.Add(BlockIdForPc(successorPcs[i]));

            int entryDepth = EntryStackDepth(startPc);
            var jumpKind = ClassifyBlockJump(statements, successorPcs);
            var flags = ComputeBlockFlags(blockId, startPc, endPc, entryDepth, exitStackDepth, successorPcs.Count);

            return new GenTreeBlock(
                blockId,
                startPc,
                endPc,
                entryDepth,
                exitStackDepth,
                jumpKind,
                flags,
                statements.ToImmutableArray(),
                succBlockIds.ToImmutableArray(),
                successorPcs.ToImmutableArray());
        }

        private GenTreeBlockJumpKind ClassifyBlockJump(List<GenTree> statements, List<int> successorPcs)
        {
            if (statements.Count == 0)
                return successorPcs.Count == 0 ? GenTreeBlockJumpKind.None : GenTreeBlockJumpKind.FallThrough;

            return statements[statements.Count - 1].Kind switch
            {
                GenTreeKind.Branch => GenTreeBlockJumpKind.Always,
                GenTreeKind.BranchTrue or GenTreeKind.BranchFalse => GenTreeBlockJumpKind.Conditional,
                GenTreeKind.Return => GenTreeBlockJumpKind.Return,
                GenTreeKind.Throw => GenTreeBlockJumpKind.Throw,
                GenTreeKind.Rethrow => GenTreeBlockJumpKind.Rethrow,
                GenTreeKind.EndFinally => GenTreeBlockJumpKind.EndFinally,
                _ => successorPcs.Count == 0 ? GenTreeBlockJumpKind.None : GenTreeBlockJumpKind.FallThrough,
            };
        }

        private void DiscardStackForLeave(List<GenTree> statements, List<StackValue> stack, int pc, ILOpCode sourceOp)
        {
            if (stack.Count == 0)
                return;
            for (int i = 0; i < stack.Count; i++)
            {
                GenTree value = stack[i].Node;
                statements.Add(CreateDiscardStatement(value, pc, sourceOp));
            }
            stack.Clear();
        }
        private GenTree CreateDiscardStatement(GenTree value, int pc, ILOpCode sourceOp)
            => Node(GenTreeKind.Eval, pc, sourceOp, operands: One(value));

        private static bool RangesIntersect(int aStart, int aEnd, int bStart, int bEnd)
            => aStart < bEnd && bStart < aEnd;

        private bool TryGetStackDepthAtPc(int pc, out int depth)
        {
            if ((uint)pc < (uint)_stackDepthAtPc.Length)
            {
                depth = _stackDepthAtPc[pc];
                return depth != UnreachableStackDepth;
            }

            depth = 0;
            return false;
        }

        private void SpillStackForBoundaries(List<GenTree> statements, List<StackValue> stack, IReadOnlyList<int> successorPcs, int pc, ILOpCode sourceOp)
        {
            if (stack.Count == 0 || successorPcs.Count == 0)
                return;

            var uniqueSuccessors = new List<int>(successorPcs.Count);
            for (int i = 0; i < successorPcs.Count; i++)
            {
                int successorPc = successorPcs[i];
                if (!uniqueSuccessors.Contains(successorPc))
                    uniqueSuccessors.Add(successorPc);
            }

            if (uniqueSuccessors.Count == 1)
            {
                SpillStackForBoundary(statements, stack, uniqueSuccessors[0], pc, sourceOp);
                return;
            }

            for (int i = 0; i < stack.Count; i++)
            {
                var value = stack[i];
                var firstTemp = GetStackEntryTemp(uniqueSuccessors[0], i, value.Type, value.StackKind);
                statements.Add(Node(GenTreeKind.StoreTemp, pc, sourceOp, operands: One(value.Node), int32: firstTemp.Index));

                for (int s = 1; s < uniqueSuccessors.Count; s++)
                {
                    var targetTemp = GetStackEntryTemp(uniqueSuccessors[s], i, value.Type, value.StackKind);
                    var reload = TempLoad(pc, sourceOp, firstTemp);
                    statements.Add(Node(GenTreeKind.StoreTemp, pc, sourceOp, operands: One(reload.Node), int32: targetTemp.Index));
                }
            }
        }

        private void SpillStackForBoundary(List<GenTree> statements, List<StackValue> stack, int targetPc, int pc, ILOpCode sourceOp)
        {
            for (int i = 0; i < stack.Count; i++)
            {
                var value = stack[i];
                var temp = GetStackEntryTemp(targetPc, i, value.Type, value.StackKind);
                statements.Add(Node(GenTreeKind.StoreTemp, pc, sourceOp, operands: One(value.Node), int32: temp.Index));
            }
        }

        private GenTemp GetStackEntryTemp(int startPc, int depth, RuntimeType? type, GenStackKind stackKind)
        {
            var key = (StartPc: startPc, Depth: depth);
            if (_stackEntryTemps.TryGetValue(key, out var existing))
            {
                if (IsUnspecifiedStackTempRequest(type, stackKind))
                    return existing;

                GenStackKind mergedKind = MergeStackKind(existing.StackKind, stackKind);
                RuntimeType? mergedType = MergeStackType(existing.Type, existing.StackKind, type, stackKind, mergedKind);

                if (AreIncompatibleStackTempShapes(existing.Type, existing.StackKind, type, stackKind, mergedKind))
                {
                    throw Fail(
                        startPc,
                        ILOpCode.Nop,
                        $"Incompatible stack temp at block entry pc {startPc}, depth {depth}: " +
                        $"existing {existing.StackKind}/{existing.Type}, incoming {stackKind}/{type}.");
                }

                if (!ReferenceEquals(mergedType, existing.Type) || mergedKind != existing.StackKind)
                {
                    existing = new GenTemp(existing.Index, existing.Kind, mergedType, mergedKind);
                    ReplaceTemp(existing);
                    _stackEntryTemps[key] = existing;
                }
                return existing;
            }

            var temp = new GenTemp(_nextTempIndex++, GenTempKind.StackSpill, type, stackKind);
            _stackEntryTemps.Add(key, temp);
            _temps.Add(temp);
            _materializedImporterTempIds.Add(temp.Index);
            return temp;
        }

        private static bool IsUnspecifiedStackTempRequest(RuntimeType? type, GenStackKind stackKind)
            => type is null && stackKind == GenStackKind.Unknown;

        private static RuntimeType? MergeStackType(
            RuntimeType? leftType,
            GenStackKind leftKind,
            RuntimeType? rightType,
            GenStackKind rightKind,
            GenStackKind mergedKind)
        {
            if (leftType is null) return rightType;
            if (rightType is null) return leftType;
            if (ReferenceEquals(leftType, rightType)) return leftType;

            if (mergedKind == GenStackKind.Ref && IsObjectReferenceStackKind(leftKind) && IsObjectReferenceStackKind(rightKind))
                return null;

            return null;
        }

        private static bool AreIncompatibleStackTempShapes(
            RuntimeType? leftType,
            GenStackKind leftKind,
            RuntimeType? rightType,
            GenStackKind rightKind,
            GenStackKind mergedKind)
        {
            if (leftKind == GenStackKind.Unknown || rightKind == GenStackKind.Unknown)
                return false;

            if (mergedKind == GenStackKind.Unknown)
                return true;

            // IL merges numeric and unmanaged pointer values by stack type alone, whatever they were loaded as.
            if (mergedKind is GenStackKind.I4 or GenStackKind.I8 or GenStackKind.NativeInt or GenStackKind.NativeUInt or
                GenStackKind.R4 or GenStackKind.R8 or GenStackKind.Ptr)
            {
                return false;
            }

            if (leftType is not null && rightType is not null && !ReferenceEquals(leftType, rightType))
                return !(mergedKind == GenStackKind.Ref && IsObjectReferenceStackKind(leftKind) && IsObjectReferenceStackKind(rightKind));

            return false;
        }

        private static bool IsObjectReferenceStackKind(GenStackKind kind)
            => kind is GenStackKind.Ref or GenStackKind.Null;

        private static GenStackKind MergeStackKind(GenStackKind left, GenStackKind right)
        {
            if (left == right) return left;
            if (left == GenStackKind.Unknown) return right;
            if (right == GenStackKind.Unknown) return left;
            if (left == GenStackKind.Null && right == GenStackKind.Ref) return GenStackKind.Ref;
            if (left == GenStackKind.Ref && right == GenStackKind.Null) return GenStackKind.Ref;
            return GenStackKind.Unknown;
        }

        private void AppendImporterStatement(List<GenTree> statements, List<StackValue> stack, GenTree statement)
        {
            if (RequiresImporterStackBarrier(statement))
                SpillEvaluationStackForImportBarrier(statements, stack, statement.Pc, statement.Operator);

            statements.Add(statement);
        }

        private void PushImportedValue(List<StackValue> stack, List<GenTree> statements, GenTree value)
        {
            if (!RequiresImmediateMaterialization(value))
            {
                Push(stack, value);
                return;
            }

            SpillEvaluationStackForImportBarrier(statements, stack, value.Pc, value.Operator);

            var temp = CreateImporterSpillTemp(value.Type, value.StackKind);
            statements.Add(Node(GenTreeKind.StoreTemp, value.Pc, value.Operator, operands: One(value), int32: temp.Index));
            Push(stack, TempLoad(value.Pc, value.Operator, temp));
        }
        private bool IsAlreadyImporterSpillTemp(StackValue value)
        {
            GenTree node = value.Node;
            return node.Kind == GenTreeKind.Temp && _materializedImporterTempIds.Contains(node.Int32);
        }
        private StackValue MaterializeForImporterBarrier(
            List<GenTree> statements,
            StackValue value,
            int pc,
            GenTreeOperator oper)
        {
            if (IsAlreadyImporterSpillTemp(value))
                return value;

            var temp = CreateImporterSpillTemp(value.Type, value.StackKind);
            statements.Add(Node(
                GenTreeKind.StoreTemp,
                pc,
                oper,
                operands: One(value.Node),
                int32: temp.Index));

            return TempLoad(pc, oper, temp);
        }
        private void SpillEvaluationStackForImportBarrier(List<GenTree> statements, List<StackValue> stack, int pc, ILOpCode sourceOp)
            => SpillEvaluationStackForImportBarrier(statements, stack, pc, ToOperator(sourceOp));
        private void SpillEvaluationStackForImportBarrier(List<GenTree> statements, List<StackValue> stack, int pc, GenTreeOperator oper)
        {
            if (stack.Count == 0)
                return;

            for (int i = 0; i < stack.Count; i++)
                stack[i] = MaterializeForImporterBarrier(statements, stack[i], pc, oper);
        }

        private GenTemp CreateImporterSpillTemp(RuntimeType? type, GenStackKind stackKind)
        {
            int index = _nextTempIndex++;
            var temp = new GenTemp(index, GenTempKind.StackSpill, type, stackKind);
            _temps.Add(temp);
            _materializedImporterTempIds.Add(index);
            return temp;
        }

        private static bool RequiresImporterStackBarrier(GenTree statement)
        {
            if (statement is null)
                return false;

            if ((statement.Flags & (GenTreeFlags.LocalDef | GenTreeFlags.MemoryWrite | GenTreeFlags.GlobalRef | GenTreeFlags.ContainsCall | GenTreeFlags.ControlFlow | GenTreeFlags.ExceptionFlow)) != 0)
                return true;

            return statement.Kind is
                GenTreeKind.StoreLocal or
                GenTreeKind.StoreArg or
                GenTreeKind.StoreTemp or
                GenTreeKind.StoreIndirect or
                GenTreeKind.StoreField or
                GenTreeKind.StoreStaticField or
                GenTreeKind.StoreArrayElement or
                GenTreeKind.Eval or
                GenTreeKind.Return or
                GenTreeKind.Throw or
                GenTreeKind.Rethrow or
                GenTreeKind.EndFinally;
        }

        private static bool RequiresImmediateMaterialization(GenTree value)
        {
            if (value is null)
                return false;

            if (value.StackKind == GenStackKind.Void)
                return false;

            if (value.Kind is GenTreeKind.Intrinsic or GenTreeKind.Call or GenTreeKind.IndirectCall or GenTreeKind.VirtualCall or GenTreeKind.DelegateInvoke)
                return false;

            const GenTreeFlags materializeFlags =
                GenTreeFlags.ContainsCall |
                GenTreeFlags.CanThrow |
                GenTreeFlags.SideEffect |
                GenTreeFlags.MemoryRead |
                GenTreeFlags.MemoryWrite |
                GenTreeFlags.GlobalRef |
                GenTreeFlags.Indirect |
                GenTreeFlags.Allocation;

            if ((value.Flags & materializeFlags) != 0)
                return true;

            return value.Kind is
                GenTreeKind.Intrinsic or
                GenTreeKind.Call or
                GenTreeKind.IndirectCall or
                GenTreeKind.VirtualCall or
                GenTreeKind.NewObject or
                GenTreeKind.NewArray or
                GenTreeKind.ArrayElement or
                GenTreeKind.ArrayElementAddr or
                GenTreeKind.ArrayDataRef or
                GenTreeKind.Field or
                GenTreeKind.FieldAddr or
                GenTreeKind.StaticField or
                GenTreeKind.StaticFieldAddr or
                GenTreeKind.LoadIndirect or
                GenTreeKind.StaticData or
                GenTreeKind.StackAlloc or
                GenTreeKind.Box or
                GenTreeKind.UnboxAny or
                GenTreeKind.CastClass;
        }

        private GenTemp CreateDupTemp(RuntimeType? type, GenStackKind stackKind)
        {
            int index = _nextTempIndex++;
            var temp = new GenTemp(index, GenTempKind.DupSpill, type, stackKind);
            _dupTemps.Add(index, temp);
            _createdDupTempIds.Add(index);
            _temps.Add(temp);
            _materializedImporterTempIds.Add(index);
            return temp;
        }

        private void ReplaceTemp(GenTemp temp)
        {
            for (int i = 0; i < _temps.Count; i++)
            {
                if (_temps[i].Index == temp.Index && _temps[i].Kind == temp.Kind)
                {
                    _temps[i] = temp;
                    return;
                }
            }
            _temps.Add(temp);
        }

        private StackValue TempLoad(int pc, ILOpCode sourceOp, GenTemp temp)
            => TempLoad(pc, ToOperator(sourceOp), temp);
        private StackValue TempLoad(int pc, GenTreeOperator oper, GenTemp temp)
        {
            return new StackValue(Node(GenTreeKind.Temp, pc, oper, type: temp.Type, stackKind: temp.StackKind, int32: temp.Index), temp.Type, temp.StackKind);
        }

        private StackValue TempAddress(int pc, ILOpCode sourceOp, GenTemp temp)
        {
            var byRefType = temp.Type is null ? null : _rts.GetByRefType(temp.Type);
            return new StackValue(Node(GenTreeKind.TempAddr, pc, sourceOp, type: byRefType, stackKind: GenStackKind.ByRef, int32: temp.Index), byRefType, GenStackKind.ByRef);
        }

        private GenTemp CreateStructMaterializationTemp(RuntimeType type)
        {
            int index = _nextTempIndex++;
            var temp = new GenTemp(index, GenTempKind.StructMaterialization, type, StackKindOf(type));
            _temps.Add(temp);
            _structMaterializationTempIds.Add(index);
            return temp;
        }

        private bool TryGetTempByIndex(int index, out GenTemp temp)
        {
            for (int i = 0; i < _temps.Count; i++)
            {
                if (_temps[i].Index == index)
                {
                    temp = _temps[i];
                    return true;
                }
            }

            temp = default;
            return false;
        }

        private void AppendLocalLikeStore(
            List<GenTree> statements,
            List<StackValue> stack,
            int pc,
            ILOpCode sourceOp,
            GenTreeKind storeKind,
            GenTreeKind addressKind,
            int index,
            RuntimeType? targetType,
            GenTree value)
        {
            if (targetType is not null &&
                TryRetargetStructMaterializationToLocalLikeStore(statements, pc, sourceOp, storeKind, addressKind, index, targetType, value))
            {
                return;
            }

            if (targetType is not null &&
                CanUseFieldWiseStructStoreForDestination(addressKind, index) &&
                TryAppendFieldWiseStructStore(statements, stack, pc, sourceOp, addressKind, index, targetType, value))
            {
                return;
            }

            AppendImporterStatement(statements, stack, Node(storeKind, pc, sourceOp, operands: One(value), int32: index));
        }
        private bool TryRetargetStructMaterializationToLocalLikeStore(
            List<GenTree> statements,
            int pc,
            ILOpCode sourceOp,
            GenTreeKind storeKind,
            GenTreeKind addressKind,
            int index,
            RuntimeType targetType,
            GenTree value)
        {
            if (addressKind == GenTreeKind.TempAddr && value.Kind == GenTreeKind.Temp && value.Int32 == index)
                return false;

            if (PcInExceptionRegion(pc))
                return false;

            if (TryGetTrailingStructMaterialization(statements, value, targetType, out var temp, out var ctorCall, out int rewriteStart))
            {
                if (!CanRetargetStructMaterializationToLocalLikeDestination(addressKind, index, ctorCall))
                    return false;

                statements.RemoveRange(rewriteStart, statements.Count - rewriteStart);
                EmitStructDefaultInitializationToLocalLike(statements, pc, sourceOp, storeKind, addressKind, index, targetType);
                var destinationAddress = CreateLocalLikeAddress(pc, sourceOp, addressKind, index, targetType);
                AppendRetargetedStructConstructorCall(statements, ctorCall, destinationAddress);
                RemoveEliminatedStructMaterializationTemp(temp);
                return true;
            }

            if (TryGetTrailingInlineStructMaterializationThroughCopyChain(statements, value, targetType, out temp, out var inlinePlan, out var copyTempIds))
            {
                if (!CanRetargetInlineStructMaterializationToLocalLikeDestination(addressKind, index, inlinePlan))
                    return false;

                statements.RemoveRange(inlinePlan.RewriteStart, statements.Count - inlinePlan.RewriteStart);
                if (inlinePlan.NeedsDefaultInitialization)
                    EmitStructDefaultInitializationToLocalLike(statements, pc, sourceOp, storeKind, addressKind, index, targetType);

                AppendRetargetedInlineStructMaterialization(statements, inlinePlan, temp, () => CreateLocalLikeAddress(pc, sourceOp, addressKind, index, targetType));
                RemoveEliminatedStructMaterializationTemp(temp);
                RemoveEliminatedTempIndexes(inlinePlan.AliasTempIds);
                RemoveEliminatedTempIndexes(copyTempIds);
                return true;
            }

            if (TryGetTrailingInlineStructMaterialization(statements, value, targetType, out temp, out inlinePlan))
            {
                if (!CanRetargetInlineStructMaterializationToLocalLikeDestination(addressKind, index, inlinePlan))
                    return false;

                statements.RemoveRange(inlinePlan.RewriteStart, statements.Count - inlinePlan.RewriteStart);
                if (inlinePlan.NeedsDefaultInitialization)
                    EmitStructDefaultInitializationToLocalLike(statements, pc, sourceOp, storeKind, addressKind, index, targetType);

                AppendRetargetedInlineStructMaterialization(statements, inlinePlan, temp, () => CreateLocalLikeAddress(pc, sourceOp, addressKind, index, targetType));
                RemoveEliminatedStructMaterializationTemp(temp);
                RemoveEliminatedTempIndexes(inlinePlan.AliasTempIds);
                return true;
            }

            return false;
        }

        private bool TryRetargetStructMaterializationToAddress(
            List<GenTree> statements,
            int pc,
            ILOpCode sourceOp,
            GenTree destinationAddress,
            RuntimeType targetType,
            GenTree value)
        {
            if (!IsReusableStructDestinationAddress(destinationAddress))
                return false;

            if (PcInExceptionRegion(pc))
                return false;

            if (TryGetTrailingStructMaterialization(statements, value, targetType, out var temp, out var ctorCall, out int rewriteStart))
            {
                if (!CanRetargetStructMaterializationToMemoryDestination(ctorCall))
                    return false;

                statements.RemoveRange(rewriteStart, statements.Count - rewriteStart);
                EmitStructDefaultInitializationThroughAddress(statements, pc, sourceOp, targetType, () => CloneAddressNode(destinationAddress));
                AppendRetargetedStructConstructorCall(statements, ctorCall, CloneAddressNode(destinationAddress));
                RemoveEliminatedStructMaterializationTemp(temp);
                return true;
            }

            if (TryGetTrailingInlineStructMaterializationThroughCopyChain(statements, value, targetType, out temp, out var inlinePlan, out var copyTempIds))
            {
                if (!CanRetargetInlineStructMaterializationToMemoryDestination(inlinePlan))
                    return false;

                statements.RemoveRange(inlinePlan.RewriteStart, statements.Count - inlinePlan.RewriteStart);
                if (inlinePlan.NeedsDefaultInitialization)
                    EmitStructDefaultInitializationThroughAddress(statements, pc, sourceOp, targetType, () => CloneAddressNode(destinationAddress));

                AppendRetargetedInlineStructMaterialization(statements, inlinePlan, temp, () => CloneAddressNode(destinationAddress));
                RemoveEliminatedStructMaterializationTemp(temp);
                RemoveEliminatedTempIndexes(inlinePlan.AliasTempIds);
                RemoveEliminatedTempIndexes(copyTempIds);
                return true;
            }

            if (TryGetTrailingInlineStructMaterialization(statements, value, targetType, out temp, out inlinePlan))
            {
                if (!CanRetargetInlineStructMaterializationToMemoryDestination(inlinePlan))
                    return false;

                statements.RemoveRange(inlinePlan.RewriteStart, statements.Count - inlinePlan.RewriteStart);
                if (inlinePlan.NeedsDefaultInitialization)
                    EmitStructDefaultInitializationThroughAddress(statements, pc, sourceOp, targetType, () => CloneAddressNode(destinationAddress));

                AppendRetargetedInlineStructMaterialization(statements, inlinePlan, temp, () => CloneAddressNode(destinationAddress));
                RemoveEliminatedStructMaterializationTemp(temp);
                RemoveEliminatedTempIndexes(inlinePlan.AliasTempIds);
                return true;
            }

            return false;
        }

        private bool CanRetargetStructMaterializationToLocalLikeDestination(GenTreeKind destinationAddressKind, int destinationIndex, GenTree ctorCall)
        {
            for (int i = 1; i < ctorCall.Operands.Length; i++)
            {
                var arg = ctorCall.Operands[i];
                if (HasStructMaterializationOrderingHazard(arg))
                    return false;

                if (ReferencesLocalLikeDestination(arg, destinationAddressKind, destinationIndex))
                    return false;

                if (DestinationMayBeExternallyAliased(destinationAddressKind, destinationIndex) && HasAliasingMemoryAccess(arg))
                    return false;
            }

            return true;
        }

        private static bool CanRetargetStructMaterializationToMemoryDestination(GenTree ctorCall)
        {
            for (int i = 1; i < ctorCall.Operands.Length; i++)
            {
                if (HasStructMaterializationOrderingHazard(ctorCall.Operands[i]) || HasAliasingMemoryAccess(ctorCall.Operands[i]))
                    return false;
            }

            return true;
        }

        private sealed class InlineStructMaterializationPlan
        {
            public int RewriteStart;
            public bool SawDefaultInitialization;
            public bool WritesAllInstanceFields;
            public readonly List<GenTree> FieldStores = new();
            public readonly HashSet<int> AliasTempIds = new();
            public readonly HashSet<RuntimeField> WrittenFields = new();

            public bool NeedsDefaultInitialization => SawDefaultInitialization && !WritesAllInstanceFields;
        }

        private bool CanRetargetInlineStructMaterializationToLocalLikeDestination(
            GenTreeKind destinationAddressKind,
            int destinationIndex,
            InlineStructMaterializationPlan plan)
        {
            for (int i = 0; i < plan.FieldStores.Count; i++)
            {
                var value = plan.FieldStores[i].Operands[1];
                if (HasStructMaterializationOrderingHazard(value))
                    return false;

                if (ReferencesLocalLikeDestination(value, destinationAddressKind, destinationIndex))
                    return false;

                if (DestinationMayBeExternallyAliased(destinationAddressKind, destinationIndex) && HasAliasingMemoryAccess(value))
                    return false;
            }

            return true;
        }

        private static bool CanRetargetInlineStructMaterializationToMemoryDestination(InlineStructMaterializationPlan plan)
        {
            for (int i = 0; i < plan.FieldStores.Count; i++)
            {
                var value = plan.FieldStores[i].Operands[1];
                if (HasStructMaterializationOrderingHazard(value) || HasAliasingMemoryAccess(value))
                    return false;
            }

            return true;
        }

        private bool TryGetTrailingInlineStructMaterializationThroughCopyChain(
            List<GenTree> statements,
            GenTree value,
            RuntimeType targetType,
            out GenTemp temp,
            out InlineStructMaterializationPlan plan,
            out HashSet<int> copyTempIds)
        {
            temp = default;
            plan = null!;
            copyTempIds = new HashSet<int>();

            if (value.Kind != GenTreeKind.Temp || statements.Count == 0)
                return false;

            int end = statements.Count;
            var current = value;
            while (end > 0)
            {
                if (current.Kind != GenTreeKind.Temp)
                    return false;

                int copyTempIndex = current.Int32;
                if (!TryGetTempByIndex(copyTempIndex, out var copyTemp) || !IsElidableStructCopyTemp(copyTemp) || !ReferenceEquals(copyTemp.Type, targetType))
                    return false;

                var copy = statements[end - 1];
                if (!TryGetTrailingTempStructCopy(copy, copyTempIndex, targetType, out var source))
                    return false;

                if (HasTempReferenceBefore(statements, end - 1, copyTempIndex))
                    return false;

                copyTempIds.Add(copyTempIndex);
                end--;

                var prefix = end == statements.Count ? statements : statements.GetRange(0, end);
                if (TryGetTrailingInlineStructMaterialization(prefix, source, targetType, out temp, out plan))
                    return true;

                current = source;
            }

            return false;
        }

        private static bool TryGetTrailingTempStructCopy(GenTree statement, int destinationTempIndex, RuntimeType targetType, out GenTree source)
        {
            source = null!;

            if (statement.Kind != GenTreeKind.StoreTemp || statement.Int32 != destinationTempIndex || statement.Operands.Length != 1)
                return false;

            source = statement.Operands[0];
            return source.Kind == GenTreeKind.Temp && ReferenceEquals(source.Type, targetType);
        }

        private static bool IsElidableStructCopyTemp(GenTemp temp)
        {
            return temp.Kind is GenTempKind.StructMaterialization or GenTempKind.InlineLocal or GenTempKind.InlineReturn or GenTempKind.StackSpill;
        }

        private static bool HasTempReferenceBefore(List<GenTree> statements, int stop, int tempIndex)
        {
            for (int i = 0; i < stop; i++)
            {
                if (ReferencesTempIndex(statements[i], tempIndex))
                    return true;
            }

            return false;
        }

        private static bool ReferencesTempIndex(GenTree tree, int tempIndex)
        {
            if (tree.Int32 == tempIndex && tree.Kind is GenTreeKind.Temp or GenTreeKind.TempAddr or GenTreeKind.StoreTemp)
                return true;

            var operands = tree.Operands;
            for (int i = 0; i < operands.Length; i++)
            {
                if (ReferencesTempIndex(operands[i], tempIndex))
                    return true;
            }

            return false;
        }

        private bool TryGetTrailingInlineStructMaterialization(
            List<GenTree> statements,
            GenTree value,
            RuntimeType targetType,
            out GenTemp temp,
            out InlineStructMaterializationPlan plan)
        {
            temp = default;
            plan = null!;

            if (value.Kind != GenTreeKind.Temp)
                return false;

            if (!TryGetTempByIndex(value.Int32, out temp) || !IsElidableStructConstructionTemp(temp) || !ReferenceEquals(temp.Type, targetType))
                return false;

            if (!ReferenceEquals(value.Type, targetType) || statements.Count == 0)
                return false;

            for (int start = 0; start < statements.Count; start++)
            {
                if (!TryAnalyzeInlineStructMaterializationSegment(statements, start, temp, targetType, out var candidate))
                    continue;

                if (HasStructMaterializationReferenceBefore(statements, start, temp, candidate.AliasTempIds))
                    continue;

                plan = candidate;
                return true;
            }

            return false;
        }

        private static bool IsElidableStructConstructionTemp(GenTemp temp)
        {
            return temp.Kind is GenTempKind.StructMaterialization or GenTempKind.InlineLocal or GenTempKind.InlineReturn;
        }

        private bool TryAnalyzeInlineStructMaterializationSegment(
            List<GenTree> statements,
            int start,
            GenTemp temp,
            RuntimeType targetType,
            out InlineStructMaterializationPlan plan)
        {
            plan = new InlineStructMaterializationPlan { RewriteStart = start };
            bool beforeCtorStores = true;
            bool sawMaterializationStatement = false;

            for (int i = start; i < statements.Count; i++)
            {
                var statement = statements[i];

                if (TryGetStructMaterializationAliasDefinition(statement, temp, plan.AliasTempIds, out int aliasTempId))
                {
                    plan.AliasTempIds.Add(aliasTempId);
                    sawMaterializationStatement = true;
                    continue;
                }

                if (beforeCtorStores && IsStructDefaultInitializationForTempOrAlias(statement, temp, targetType, plan.AliasTempIds))
                {
                    plan.SawDefaultInitialization = true;
                    sawMaterializationStatement = true;
                    continue;
                }

                if (TryGetStructMaterializationFieldStore(statement, temp, targetType, plan.AliasTempIds, out var field, out var fieldValue))
                {
                    if (ReferencesStructMaterializationStorage(fieldValue, temp, plan.AliasTempIds))
                        return false;

                    beforeCtorStores = false;
                    sawMaterializationStatement = true;
                    plan.FieldStores.Add(statement);
                    plan.WrittenFields.Add(field);
                    continue;
                }

                return false;
            }

            if (!sawMaterializationStatement)
                return false;

            if (plan.FieldStores.Count == 0 && !plan.SawDefaultInitialization)
                return false;

            plan.WritesAllInstanceFields = plan.FieldStores.Count != 0 && AllInstanceFieldsWritten(targetType, plan.WrittenFields);
            if (!plan.SawDefaultInitialization && !plan.WritesAllInstanceFields)
                return false;

            return true;
        }

        private static bool TryGetStructMaterializationAliasDefinition(
            GenTree statement,
            GenTemp temp,
            HashSet<int> aliasTempIds,
            out int aliasTempId)
        {
            aliasTempId = -1;

            if (statement.Kind != GenTreeKind.StoreTemp || statement.Operands.Length != 1)
                return false;

            if (!IsStructMaterializationAddress(statement.Operands[0], temp, aliasTempIds))
                return false;

            if (statement.Int32 == temp.Index || aliasTempIds.Contains(statement.Int32))
                return false;

            aliasTempId = statement.Int32;
            return true;
        }

        private static bool IsStructDefaultInitializationForTempOrAlias(
            GenTree statement,
            GenTemp temp,
            RuntimeType valueType,
            HashSet<int> aliasTempIds)
        {
            if (statement.Kind == GenTreeKind.StoreTemp && statement.Int32 == temp.Index)
                return statement.Operands.Length == 1 && IsDefaultValueOfType(statement.Operands[0], valueType);

            if (statement.Kind == GenTreeKind.StoreField && statement.Operands.Length == 2)
            {
                if (!IsStructMaterializationAddress(statement.Operands[0], temp, aliasTempIds))
                    return false;

                if (statement.Field is null || statement.Field.IsStatic || !ReferenceEquals(statement.Field.DeclaringType, valueType))
                    return false;

                return IsDefaultValue(statement.Operands[1]);
            }

            return false;
        }

        private static bool TryGetStructMaterializationFieldStore(
            GenTree statement,
            GenTemp temp,
            RuntimeType targetType,
            HashSet<int> aliasTempIds,
            out RuntimeField field,
            out GenTree fieldValue)
        {
            field = null!;
            fieldValue = null!;

            if (statement.Kind != GenTreeKind.StoreField || statement.Operands.Length != 2)
                return false;

            if (statement.Field is null || statement.Field.IsStatic || !ReferenceEquals(statement.Field.DeclaringType, targetType))
                return false;

            if (!IsStructMaterializationAddress(statement.Operands[0], temp, aliasTempIds))
                return false;

            field = statement.Field;
            fieldValue = statement.Operands[1];
            return true;
        }

        private static bool IsStructMaterializationAddress(GenTree node, GenTemp temp, HashSet<int> aliasTempIds)
        {
            if (node.Kind == GenTreeKind.TempAddr && node.Int32 == temp.Index)
                return true;

            if (node.Kind == GenTreeKind.Temp && aliasTempIds.Contains(node.Int32))
                return true;

            return false;
        }

        private static bool ReferencesStructMaterializationStorage(GenTree tree, GenTemp temp, HashSet<int> aliasTempIds)
        {
            if (IsStructMaterializationAddress(tree, temp, aliasTempIds))
                return true;

            var operands = tree.Operands;
            for (int i = 0; i < operands.Length; i++)
            {
                if (ReferencesStructMaterializationStorage(operands[i], temp, aliasTempIds))
                    return true;
            }

            return false;
        }

        private static bool HasStructMaterializationReferenceBefore(
            List<GenTree> statements,
            int stop,
            GenTemp temp,
            HashSet<int> aliasTempIds)
        {
            for (int i = 0; i < stop; i++)
            {
                if (ReferencesStructMaterializationStorage(statements[i], temp, aliasTempIds))
                    return true;

                if (statements[i].Kind == GenTreeKind.StoreTemp && aliasTempIds.Contains(statements[i].Int32))
                    return true;
            }

            return false;
        }

        private static bool AllInstanceFieldsWritten(RuntimeType targetType, HashSet<RuntimeField> writtenFields)
        {
            if (targetType.InlineArrayLength > 0)
                return false;

            var fields = targetType.InstanceFields;
            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (!field.IsStatic && !writtenFields.Contains(field))
                    return false;
            }

            return true;
        }

        private void AppendRetargetedInlineStructMaterialization(
            List<GenTree> statements,
            InlineStructMaterializationPlan plan,
            GenTemp temp,
            Func<GenTree> createDestinationAddress)
        {
            for (int i = 0; i < plan.FieldStores.Count; i++)
            {
                var store = plan.FieldStores[i];
                var fieldValue = CloneTreeReplacingStructMaterializationStorage(store.Operands[1], temp, plan.AliasTempIds, createDestinationAddress);
                statements.Add(Node(GenTreeKind.StoreField, store.Pc, store.Operator,
                    operands: Two(createDestinationAddress(), fieldValue), field: store.Field, int64: store.Int64, runtimeType: store.RuntimeType));
            }
        }

        private GenTree CloneTreeReplacingStructMaterializationStorage(
            GenTree node,
            GenTemp temp,
            HashSet<int> aliasTempIds,
            Func<GenTree> createDestinationAddress)
        {
            if (IsStructMaterializationAddress(node, temp, aliasTempIds))
                return createDestinationAddress();

            var operands = node.Operands;
            ImmutableArray<GenTree> clonedOperands = ImmutableArray<GenTree>.Empty;
            if (operands.Length != 0)
            {
                var builder = ImmutableArray.CreateBuilder<GenTree>(operands.Length);
                for (int i = 0; i < operands.Length; i++)
                    builder.Add(CloneTreeReplacingStructMaterializationStorage(operands[i], temp, aliasTempIds, createDestinationAddress));
                clonedOperands = builder.ToImmutable();
            }

            return Node(node.Kind, node.Pc, node.Operator,
                type: node.Type,
                stackKind: node.StackKind,
                operands: clonedOperands,
                int32: node.Int32,
                int64: node.Int64,
                text: node.Text,
                runtimeType: node.RuntimeType,
                field: node.Field,
                method: node.Method,
                convKind: node.ConvKind,
                convFlags: node.ConvFlags,
                targetPc: node.TargetPc,
                targetBlockId: node.TargetBlockId,
                boundsCheckIndexOverride: node.BoundsCheckIndexOverride);
        }

        private bool DestinationMayBeExternallyAliased(GenTreeKind destinationAddressKind, int destinationIndex)
        {
            return destinationAddressKind switch
            {
                GenTreeKind.LocalAddr => (uint)destinationIndex < (uint)_addressExposedLocals.Length && _addressExposedLocals[destinationIndex],
                GenTreeKind.ArgAddr => (uint)destinationIndex < (uint)_addressExposedArgs.Length && _addressExposedArgs[destinationIndex],
                _ => false,
            };
        }

        private static bool ReferencesLocalLikeDestination(GenTree tree, GenTreeKind destinationAddressKind, int destinationIndex)
        {
            if (IsDestinationLocalLikeUse(tree, destinationAddressKind, destinationIndex))
                return true;

            var operands = tree.Operands;
            for (int i = 0; i < operands.Length; i++)
            {
                if (ReferencesLocalLikeDestination(operands[i], destinationAddressKind, destinationIndex))
                    return true;
            }

            return false;
        }

        private static bool IsDestinationLocalLikeUse(GenTree tree, GenTreeKind destinationAddressKind, int destinationIndex)
        {
            return destinationAddressKind switch
            {
                GenTreeKind.LocalAddr => tree.Int32 == destinationIndex && (tree.Kind == GenTreeKind.Local || tree.Kind == GenTreeKind.LocalAddr || tree.Kind == GenTreeKind.StoreLocal),
                GenTreeKind.ArgAddr => tree.Int32 == destinationIndex && (tree.Kind == GenTreeKind.Arg || tree.Kind == GenTreeKind.ArgAddr || tree.Kind == GenTreeKind.StoreArg),
                GenTreeKind.TempAddr => tree.Int32 == destinationIndex && (tree.Kind == GenTreeKind.Temp || tree.Kind == GenTreeKind.TempAddr || tree.Kind == GenTreeKind.StoreTemp),
                _ => false,
            };
        }

        private static bool HasStructMaterializationOrderingHazard(GenTree tree)
        {
            const GenTreeFlags hazardFlags = GenTreeFlags.ContainsCall |
                                             GenTreeFlags.CanThrow |
                                             GenTreeFlags.SideEffect |
                                             GenTreeFlags.MemoryRead |
                                             GenTreeFlags.MemoryWrite |
                                             GenTreeFlags.GlobalRef |
                                             GenTreeFlags.Indirect |
                                             GenTreeFlags.Allocation |
                                             GenTreeFlags.ControlFlow |
                                             GenTreeFlags.ExceptionFlow;
            return (tree.Flags & hazardFlags) != 0;
        }

        private static bool HasAliasingMemoryAccess(GenTree tree)
        {
            const GenTreeFlags aliasingFlags = GenTreeFlags.ContainsCall |
                                               GenTreeFlags.SideEffect |
                                               GenTreeFlags.MemoryRead |
                                               GenTreeFlags.MemoryWrite |
                                               GenTreeFlags.GlobalRef |
                                               GenTreeFlags.Indirect |
                                               GenTreeFlags.AddressExposed;
            return (tree.Flags & aliasingFlags) != 0;
        }

        private static bool IsReusableStructDestinationAddress(GenTree address)
        {
            return address.Kind == GenTreeKind.LocalAddr ||
                   address.Kind == GenTreeKind.ArgAddr ||
                   address.Kind == GenTreeKind.TempAddr;
        }

        private bool TryGetTrailingStructMaterialization(
            List<GenTree> statements,
            GenTree value,
            RuntimeType targetType,
            out GenTemp temp,
            out GenTree ctorCall,
            out int rewriteStart)
        {
            temp = default;
            ctorCall = null!;
            rewriteStart = -1;

            if (value.Kind != GenTreeKind.Temp || !_structMaterializationTempIds.Contains(value.Int32))
                return false;

            if (!TryGetTempByIndex(value.Int32, out temp) || temp.Kind != GenTempKind.StructMaterialization || !ReferenceEquals(temp.Type, targetType))
                return false;

            if (!ReferenceEquals(value.Type, targetType) || statements.Count == 0)
                return false;

            var trailing = statements[statements.Count - 1];
            if (trailing.Kind != GenTreeKind.Eval || trailing.Operands.Length != 1)
                return false;

            ctorCall = trailing.Operands[0];
            if (ctorCall.Kind != GenTreeKind.Call || ctorCall.StackKind != GenStackKind.Void || ctorCall.Method is null)
                return false;

            if (ctorCall.Operands.Length == 0 || !ReferenceEquals(ctorCall.Method.DeclaringType, targetType))
                return false;

            if (!IsTempAddressFor(ctorCall.Operands[0], temp))
                return false;

            int first = statements.Count - 1;
            bool sawInitialization = false;
            while (first > 0 && IsStructDefaultInitializationForTemp(statements[first - 1], temp, targetType))
            {
                sawInitialization = true;
                first--;
            }

            if (HasInstanceFields(targetType) && !sawInitialization)
                return false;

            rewriteStart = first;
            return true;
        }

        private static bool IsTempAddressFor(GenTree node, GenTemp temp)
        {
            return node.Kind == GenTreeKind.TempAddr && node.Int32 == temp.Index;
        }

        private static bool IsStructDefaultInitializationForTemp(GenTree statement, GenTemp temp, RuntimeType valueType)
        {
            if (statement.Kind == GenTreeKind.StoreTemp && statement.Int32 == temp.Index)
                return IsDefaultValueOfType(statement.Operands[0], valueType);

            if (statement.Kind == GenTreeKind.StoreField && statement.Operands.Length == 2)
            {
                if (!IsTempAddressFor(statement.Operands[0], temp))
                    return false;

                if (statement.Field is null || statement.Field.IsStatic)
                    return false;

                return IsDefaultValue(statement.Operands[1]);
            }

            return false;
        }

        private static bool IsDefaultValueOfType(GenTree node, RuntimeType type)
        {
            return node.Kind == GenTreeKind.DefaultValue && ReferenceEquals(node.RuntimeType, type);
        }

        private static bool IsDefaultValue(GenTree node)
        {
            return node.Kind == GenTreeKind.DefaultValue;
        }

        private static bool HasInstanceFields(RuntimeType type)
        {
            if (type.Kind != RuntimeTypeKind.Struct)
                return false;

            var fields = type.InstanceFields;
            for (int i = 0; i < fields.Length; i++)
            {
                if (!fields[i].IsStatic)
                    return true;
            }

            return false;
        }

        private void EmitStructDefaultInitializationToLocalLike(
            List<GenTree> statements,
            int pc,
            ILOpCode sourceOp,
            GenTreeKind storeKind,
            GenTreeKind addressKind,
            int index,
            RuntimeType valueType)
        {
            if (EmitStructDefaultInitializationToAddress(statements, pc, sourceOp, valueType,
                    () => CreateLocalLikeAddress(pc, sourceOp, addressKind, index, valueType)))
            {
                return;
            }

            var init = Node(GenTreeKind.DefaultValue, pc, GenTreeOperator.None, type: valueType, stackKind: StackKindOf(valueType), runtimeType: valueType);
            statements.Add(MarkExplicitInit(Node(storeKind, pc, sourceOp, operands: One(init), int32: index)));
        }

        private void EmitStructDefaultInitializationThroughAddress(
            List<GenTree> statements,
            int pc,
            ILOpCode sourceOp,
            RuntimeType valueType,
            Func<GenTree> createDestinationAddress)
        {
            if (EmitStructDefaultInitializationToAddress(statements, pc, sourceOp, valueType, createDestinationAddress))
                return;

            var init = Node(GenTreeKind.DefaultValue, pc, GenTreeOperator.None, type: valueType, stackKind: StackKindOf(valueType), runtimeType: valueType);
            statements.Add(Node(GenTreeKind.StoreIndirect, pc, sourceOp, operands: Two(createDestinationAddress(), init), runtimeType: valueType));
        }

        private bool EmitStructDefaultInitializationToAddress(
            List<GenTree> statements,
            int pc,
            ILOpCode sourceOp,
            RuntimeType valueType,
            Func<GenTree> createDestinationAddress)
        {
            if (valueType.Kind != RuntimeTypeKind.Struct)
                return false;

            if (valueType.InstanceFields.Length == 0)
                return true;

            if (!CanExpandStructFieldWise(valueType))
                return false;

            var fields = valueType.InstanceFields;
            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (field.IsStatic)
                    continue;

                var fieldDefault = Node(GenTreeKind.DefaultValue, pc, GenTreeOperator.None, type: field.FieldType, stackKind: StackKindOf(field.FieldType), runtimeType: field.FieldType);
                statements.Add(MarkExplicitInit(Node(GenTreeKind.StoreField, pc, sourceOp,
                    operands: Two(createDestinationAddress(), fieldDefault), field: field, runtimeType: field.FieldType)));
            }

            return true;
        }

        private void AppendRetargetedStructConstructorCall(
            List<GenTree> statements,
            GenTree originalCall,
            GenTree destinationAddress)
        {
            var argsBuilder = ImmutableArray.CreateBuilder<GenTree>(originalCall.Operands.Length);
            argsBuilder.Add(destinationAddress);
            for (int i = 1; i < originalCall.Operands.Length; i++)
                argsBuilder.Add(originalCall.Operands[i]);

            var call = Node(GenTreeKind.Call, originalCall.Pc, originalCall.Operator, stackKind: GenStackKind.Void,
                operands: argsBuilder.ToImmutable(), int32: originalCall.Int32, int64: originalCall.Int64, method: originalCall.Method);
            statements.Add(Node(GenTreeKind.Eval, originalCall.Pc, originalCall.Operator, operands: One(call)));
        }

        private void RemoveEliminatedStructMaterializationTemp(GenTemp temp)
        {
            RemoveEliminatedTempIndex(temp.Index);
        }

        private void RemoveEliminatedTempIndexes(IEnumerable<int> tempIndexes)
        {
            foreach (int tempIndex in tempIndexes)
                RemoveEliminatedTempIndex(tempIndex);
        }

        private void RemoveEliminatedTempIndex(int tempIndex)
        {
            for (int i = _temps.Count - 1; i >= 0; i--)
            {
                if (_temps[i].Index == tempIndex)
                    _temps.RemoveAt(i);
            }

            _structMaterializationTempIds.Remove(tempIndex);
            _materializedImporterTempIds.Remove(tempIndex);
            _dupTemps.Remove(tempIndex);
            _createdDupTempIds.Remove(tempIndex);
        }

        private bool CanUseFieldWiseStructStoreForDestination(GenTreeKind destinationAddressKind, int destinationIndex)
        {
            return destinationAddressKind switch
            {
                GenTreeKind.LocalAddr => (uint)destinationIndex >= (uint)_addressExposedLocals.Length || !_addressExposedLocals[destinationIndex],
                GenTreeKind.ArgAddr => (uint)destinationIndex >= (uint)_addressExposedArgs.Length || !_addressExposedArgs[destinationIndex],
                _ => true,
            };
        }

        private bool TryAppendFieldWiseStructStore(
            List<GenTree> statements,
            List<StackValue> stack,
            int pc,
            ILOpCode sourceOp,
            GenTreeKind destinationAddressKind,
            int destinationIndex,
            RuntimeType destinationType,
            GenTree value)
        {
            if (!CanExpandStructFieldWise(destinationType))
                return false;

            bool sourceIsDefault = value.Kind == GenTreeKind.DefaultValue && ReferenceEquals(value.RuntimeType, destinationType);
            GenTree? sourceAddress = null;
            if (!sourceIsDefault && !TryCreateAddressForStructValue(pc, sourceOp, value, destinationType, out sourceAddress))
                return false;

            var fields = destinationType.InstanceFields;
            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (field.IsStatic)
                    continue;

                GenTree fieldValue = sourceIsDefault
                    ? Node(GenTreeKind.DefaultValue, pc, sourceOp, type: field.FieldType, stackKind: StackKindOf(field.FieldType), runtimeType: field.FieldType)
                    : Node(GenTreeKind.Field, pc, ILOpCode.Ldfld, type: field.FieldType, stackKind: StackKindOf(field.FieldType), operands: One(CloneAddressNode(sourceAddress!)), field: field, runtimeType: field.FieldType);

                GenTree destinationAddress = CreateLocalLikeAddress(pc, sourceOp, destinationAddressKind, destinationIndex, destinationType);
                AppendImporterStatement(statements, stack, Node(GenTreeKind.StoreField, pc, sourceOp, operands: Two(destinationAddress, fieldValue), field: field, runtimeType: field.FieldType));
            }

            return true;
        }

        private bool TryCreateAddressForStructValue(int pc, ILOpCode sourceOp, GenTree value, RuntimeType expectedType, out GenTree address)
        {
            if (!ReferenceEquals(value.Type, expectedType))
            {
                address = null!;
                return false;
            }

            switch (value.Kind)
            {
                case GenTreeKind.Local:
                    address = CreateLocalLikeAddress(pc, sourceOp, GenTreeKind.LocalAddr, value.Int32, expectedType);
                    return true;

                case GenTreeKind.Arg:
                    address = CreateLocalLikeAddress(pc, sourceOp, GenTreeKind.ArgAddr, value.Int32, expectedType);
                    return true;

                case GenTreeKind.Temp:
                    if (!TryGetTempByIndex(value.Int32, out var temp) || !ReferenceEquals(temp.Type, expectedType))
                    {
                        address = null!;
                        return false;
                    }
                    address = TempAddress(pc, sourceOp, temp).Node;
                    return true;
            }

            address = null!;
            return false;
        }

        private GenTree CreateLocalLikeAddress(int pc, ILOpCode sourceOp, GenTreeKind addressKind, int index, RuntimeType targetType)
        {
            var byRefType = _rts.GetByRefType(targetType);
            return Node(addressKind, pc, sourceOp, type: byRefType, stackKind: GenStackKind.ByRef, int32: index);
        }

        private GenTree CloneAddressNode(GenTree address)
        {
            return Node(address.Kind, address.Pc, address.Operator, type: address.Type, stackKind: address.StackKind, int32: address.Int32);
        }

        private static bool CanExpandStructFieldWise(RuntimeType? type)
        {
            if (type is null || !type.IsValueType || type.Kind != RuntimeTypeKind.Struct || type.InstanceFields.Length == 0)
                return false;

            // An inline array's one declared field covers only its first element.
            if (type.InlineArrayLength > 0 || StackKindOf(type) != GenStackKind.Value)
                return false;

            return HasNonOverlappingInstanceFields(type.InstanceFields);
        }

        private static bool HasNonOverlappingInstanceFields(RuntimeField[] fields)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].IsStatic)
                    continue;

                int iStart = fields[i].Offset;
                int iEnd = iStart + Math.Max(1, fields[i].FieldType.SizeOf);
                for (int j = i + 1; j < fields.Length; j++)
                {
                    if (fields[j].IsStatic)
                        continue;

                    int jStart = fields[j].Offset;
                    int jEnd = jStart + Math.Max(1, fields[j].FieldType.SizeOf);
                    if (iStart < jEnd && jStart < iEnd)
                        return false;
                }
            }

            return true;
        }

        // The code generator turns a surviving cast into an inline walk of the type hierarchy, so an
        // upcast off a known exact type - the receiver of a fresh allocation, say - is worth proving here
        private bool IsProvenTypeCheck(
            GenTreeKind kind,
            StackValue value,
            List<GenTree> statements,
            RuntimeType? targetType)
        {
            if (kind is not (GenTreeKind.CastClass or GenTreeKind.IsInst) ||
                targetType is null ||
                targetType.IsValueType ||
                targetType.Kind == RuntimeTypeKind.TypeParam ||
                value.StackKind != GenStackKind.Ref)
            {
                return false;
            }

            DevirtualizationReceiverInfo info = GetDevirtualizationReceiverInfo(
                value.Node,
                statements,
                statements.Count,
                new HashSet<int>());

            if (!info.IsExact ||
                info.Type is null ||
                info.Type.Kind == RuntimeTypeKind.TypeParam ||
                !_rts.IsAssignableTo(info.Type, targetType))
            {
                return false;
            }

            // isinst yields null for a null operand, which is the operand itself, but only a non-null
            // proof rules out a runtime type that the exact type does not describe
            return kind == GenTreeKind.CastClass || info.IsNonNull;
        }

        private void MarkInstantiatedMethodContext(RuntimeMethod method)
        {
            if (method.HasThis)
                MarkInstantiatedTypeClosure(method.DeclaringType);

            MarkInstantiatedTypeClosure(method.ReturnType);

            RuntimeType[] methodArgs = method.MethodGenericArguments;
            for (int i = 0; i < methodArgs.Length; i++)
                MarkInstantiatedTypeClosure(methodArgs[i]);

            RuntimeType[] ownerArgs = method.DeclaringType.GenericTypeArguments;
            for (int i = 0; i < ownerArgs.Length; i++)
                MarkInstantiatedTypeClosure(ownerArgs[i]);

            RuntimeType[] parameters = method.ParameterTypes;
            for (int i = 0; i < parameters.Length; i++)
                MarkInstantiatedTypeClosure(parameters[i]);
        }

        private void MarkInstantiatedTypeClosure(RuntimeType? type)
        {
            if (type is null)
                return;

            if (type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef)
            {
                MarkInstantiatedTypeClosure(type.ElementType);
                return;
            }

            if (type.Kind == RuntimeTypeKind.FunctionPointer)
            {
                MarkInstantiatedTypeClosure(type.FunctionPointerReturnType);
                RuntimeType[] parameters = type.FunctionPointerParameterTypes;
                for (int i = 0; i < parameters.Length; i++)
                    MarkInstantiatedTypeClosure(parameters[i]);
                return;
            }

            MarkInstantiatedType(type);

            if (type.ElementType is not null)
                MarkInstantiatedTypeClosure(type.ElementType);

            RuntimeType[] args = type.GenericTypeArguments;
            for (int i = 0; i < args.Length; i++)
                MarkInstantiatedTypeClosure(args[i]);
        }

        private void MarkInstantiatedType(RuntimeType? type)
        {
            if (type is null)
                return;

            if (type.Kind is RuntimeTypeKind.Class or RuntimeTypeKind.Struct or RuntimeTypeKind.Enum or RuntimeTypeKind.Array)
                _instantiatedTypes.TryAdd(type.TypeId, type);
        }

        private GenTree ConstI4(int pc, ILOpCode sourceOp, int value)
            => Node(GenTreeKind.ConstI4, pc, sourceOp, stackKind: GenStackKind.I4, int32: value);

        private enum DevirtualizationReceiverTransform : byte
        {
            None,
            Unbox,
            TakeAddress,
            Box,
        }

        private readonly struct DevirtualizationReceiverInfo
        {
            public RuntimeType? Type { get; }
            public bool IsExact { get; }
            public bool IsNonNull { get; }
            public bool IsBoxedValue { get; }
            public bool IsUnboxedValue { get; }

            public DevirtualizationReceiverInfo(
                RuntimeType? type,
                bool isExact,
                bool isNonNull,
                bool isBoxedValue = false,
                bool isUnboxedValue = false)
            {
                Type = type;
                IsExact = isExact;
                IsNonNull = isNonNull;
                IsBoxedValue = isBoxedValue;
                IsUnboxedValue = isUnboxedValue;
            }
        }

        private bool TryDevirtualizeCall(
            RuntimeMethod declaredMethod,
            ImmutableArray<GenTree> args,
            List<GenTree> statements,
            out RuntimeMethod targetMethod,
            out bool requiresNullCheck,
            out DevirtualizationReceiverTransform receiverTransform)
        {
            targetMethod = declaredMethod;
            requiresNullCheck = false;
            receiverTransform = DevirtualizationReceiverTransform.None;

            if (!declaredMethod.HasThis || args.IsDefaultOrEmpty)
                return false;

            GenTree receiver = args[0];
            bool isReferenceReceiver = receiver.StackKind is GenStackKind.Ref or GenStackKind.Null;
            bool isValueReceiver = receiver.Type is not null &&
                receiver.Type.IsValueType &&
                receiver.StackKind is not (GenStackKind.ByRef or GenStackKind.Ptr);
            if (!isReferenceReceiver && !isValueReceiver)
                return false;

            DevirtualizationReceiverInfo receiverInfo = GetDevirtualizationReceiverInfo(
                receiver,
                statements,
                statements.Count,
                new HashSet<int>());

            RuntimeMethod resolvedMethod;
            if (!declaredMethod.IsVirtual && declaredMethod.DeclaringType.Kind != RuntimeTypeKind.Interface)
            {
                resolvedMethod = declaredMethod;
            }
            else
            {
                RuntimeType? objectType = receiverInfo.Type;
                if (objectType is null ||
                    objectType.Kind is RuntimeTypeKind.Interface or RuntimeTypeKind.TypeParam ||
                    (!objectType.IsReferenceType && !objectType.IsValueType))
                {
                    return false;
                }

                RuntimeMethod? candidate = _rts.ResolveVirtualMethod(declaredMethod, objectType);
                if (candidate is null)
                    return false;

                bool canDevirtualize = receiverInfo.IsExact ||
                    objectType.IsFinal ||
                    (declaredMethod.DeclaringType.Kind != RuntimeTypeKind.Interface && candidate.IsFinal);

                if (!canDevirtualize)
                    return false;

                resolvedMethod = candidate;
            }

            targetMethod = resolvedMethod;
            requiresNullCheck = isReferenceReceiver && !receiverInfo.IsNonNull;
            if (receiverInfo.IsBoxedValue && resolvedMethod.DeclaringType.IsValueType)
            {
                receiverTransform = DevirtualizationReceiverTransform.Unbox;
            }
            else if (receiverInfo.IsUnboxedValue)
            {
                receiverTransform = resolvedMethod.DeclaringType.IsValueType
                    ? DevirtualizationReceiverTransform.TakeAddress
                    : DevirtualizationReceiverTransform.Box;
            }

            return true;
        }

        private DevirtualizationReceiverInfo GetDevirtualizationReceiverInfo(
            GenTree node, List<GenTree> statements, int statementLimit, HashSet<int> visitingTemps)
        {
            if (node.Kind != GenTreeKind.Box &&
                node.Type is not null &&
                node.Type.IsValueType &&
                node.StackKind is not (GenStackKind.ByRef or GenStackKind.Ptr))
            {
                return new DevirtualizationReceiverInfo(
                    node.Type,
                    isExact: true,
                    isNonNull: true,
                    isUnboxedValue: true);
            }

            switch (node.Kind)
            {
                case GenTreeKind.NewObject:
                    return ExactNonNullReference(node.RuntimeType ?? node.Type);

                case GenTreeKind.NewArray:
                case GenTreeKind.NewDelegate:
                    return ExactNonNullReference(node.Type ?? node.RuntimeType);

                case GenTreeKind.ConstString:
                    return new DevirtualizationReceiverInfo(_rts.SystemString, isExact: true, isNonNull: true);

                case GenTreeKind.Box:
                    {
                        RuntimeType? boxedType = node.RuntimeType;
                        if (boxedType is not null && IsSystemNullableRuntimeType(boxedType))
                        {
                            // A boxed Nullable<T> is a boxed T, or null when it has no value.
                            return new DevirtualizationReceiverInfo(
                                boxedType.GenericTypeArguments[0],
                                isExact: true,
                                isNonNull: false,
                                isBoxedValue: true);
                        }
                        if (boxedType is not null && boxedType.IsValueType)
                        {
                            return new DevirtualizationReceiverInfo(
                                boxedType,
                                isExact: true,
                                isNonNull: true,
                                isBoxedValue: true);
                        }
                        return StaticReference(node.Type, isNonNull: true);
                    }

                case GenTreeKind.Copy:
                case GenTreeKind.Reload:
                case GenTreeKind.Spill:
                    if (node.Operands.Length == 1)
                        return GetDevirtualizationReceiverInfo(node.Operands[0], statements, statementLimit, visitingTemps);
                    break;

                case GenTreeKind.CastClass:
                    {
                        RuntimeType? castType = node.RuntimeType ?? node.Type;
                        if (node.Operands.Length != 1)
                            return StaticReference(castType, isNonNull: false);

                        DevirtualizationReceiverInfo sourceInfo = GetDevirtualizationReceiverInfo(
                            node.Operands[0],
                            statements,
                            statementLimit,
                            visitingTemps);
                        if (sourceInfo.IsExact &&
                            sourceInfo.Type is not null &&
                            castType is not null &&
                            _rts.IsAssignableTo(sourceInfo.Type, castType))
                        {
                            return sourceInfo;
                        }

                        return StaticReference(castType, sourceInfo.IsNonNull);
                    }

                case GenTreeKind.IsInst:
                    {
                        RuntimeType? targetType = node.RuntimeType ?? node.Type;
                        if (node.Operands.Length == 1)
                        {
                            DevirtualizationReceiverInfo sourceInfo = GetDevirtualizationReceiverInfo(
                                node.Operands[0],
                                statements,
                                statementLimit,
                                visitingTemps);
                            if (sourceInfo.IsExact &&
                                sourceInfo.IsNonNull &&
                                sourceInfo.Type is not null &&
                                targetType is not null &&
                                _rts.IsAssignableTo(sourceInfo.Type, targetType))
                            {
                                return sourceInfo;
                            }
                        }

                        return StaticReference(node.Type, isNonNull: false);
                    }

                case GenTreeKind.Temp:
                    {
                        int tempIndex = node.Int32;
                        if (!visitingTemps.Add(tempIndex))
                            return StaticReference(node.Type, isNonNull: false);

                        try
                        {
                            if (HasTempAddressUse(statements, tempIndex, statementLimit))
                                return StaticReference(node.Type, isNonNull: false);

                            for (int i = statementLimit - 1; i >= 0; i--)
                            {
                                GenTree statement = statements[i];
                                if (statement.Kind != GenTreeKind.StoreTemp ||
                                    statement.Int32 != tempIndex ||
                                    statement.Operands.Length != 1)
                                {
                                    continue;
                                }

                                DevirtualizationReceiverInfo storedInfo = GetDevirtualizationReceiverInfo(
                                    statement.Operands[0],
                                    statements,
                                    i,
                                    visitingTemps);

                                if (storedInfo.Type is not null)
                                    return storedInfo;

                                return StaticReference(node.Type, storedInfo.IsNonNull);
                            }
                        }
                        finally
                        {
                            visitingTemps.Remove(tempIndex);
                        }

                        return StaticReference(node.Type, isNonNull: false);
                    }

                case GenTreeKind.Arg:
                    return StaticReference(
                        node.Type,
                        _method.HasThis && node.Int32 == 0 && node.StackKind == GenStackKind.Ref);

                case GenTreeKind.Local:
                case GenTreeKind.Field:
                case GenTreeKind.StaticField:
                case GenTreeKind.ArrayElement:
                case GenTreeKind.Intrinsic:
                case GenTreeKind.Call:
                case GenTreeKind.VirtualCall:
                case GenTreeKind.DelegateInvoke:
                case GenTreeKind.DefaultValue:
                    return StaticReference(node.Type, isNonNull: false);
            }

            return default;
        }

        private static DevirtualizationReceiverInfo ExactNonNullReference(RuntimeType? type)
        {
            if (type is null || !type.IsReferenceType || type.Kind == RuntimeTypeKind.TypeParam)
                return default;
            return new DevirtualizationReceiverInfo(type, isExact: true, isNonNull: true);
        }

        private static DevirtualizationReceiverInfo StaticReference(RuntimeType? type, bool isNonNull)
        {
            if (type is null || !type.IsReferenceType || type.Kind == RuntimeTypeKind.TypeParam)
                return new DevirtualizationReceiverInfo(null, isExact: false, isNonNull: isNonNull);
            return new DevirtualizationReceiverInfo(type, isExact: false, isNonNull: isNonNull);
        }

        private static bool HasTempAddressUse(List<GenTree> statements, int tempIndex, int statementLimit)
        {
            for (int i = 0; i < statementLimit; i++)
            {
                if (TreeContainsTempAddress(statements[i], tempIndex))
                    return true;
            }
            return false;
        }

        private static bool TreeContainsTempAddress(GenTree node, int tempIndex)
        {
            if (node.Kind == GenTreeKind.TempAddr && node.Int32 == tempIndex)
                return true;

            ImmutableArray<GenTree> operands = node.Operands;
            for (int i = 0; i < operands.Length; i++)
            {
                if (TreeContainsTempAddress(operands[i], tempIndex))
                    return true;
            }
            return false;
        }

        private ImmutableArray<GenTree> RewriteDevirtualizedReceiver(
            List<GenTree> statements,
            int pc,
            ILOpCode sourceOp,
            ImmutableArray<GenTree> operands,
            RuntimeMethod targetMethod,
            DevirtualizationReceiverTransform transform)
        {
            if (transform == DevirtualizationReceiverTransform.None)
                return operands;
            if (operands.IsDefaultOrEmpty)
                throw Fail(pc, sourceOp, "Devirtualized call has no receiver.");

            GenTree receiver = operands[0];
            GenTree rewrittenReceiver;
            switch (transform)
            {
                case DevirtualizationReceiverTransform.Unbox:
                    {
                        if (!targetMethod.DeclaringType.IsValueType)
                            throw Fail(pc, sourceOp, "Devirtualized unboxing target is not a value-type method.");

                        GenTree payloadPointer = Node(
                            GenTreeKind.PointerElementAddr,
                            pc,
                            sourceOp,
                            stackKind: GenStackKind.Ptr,
                            operands: Two(receiver, ConstI4(pc, sourceOp, 1)),
                            int32: _rts.Target.ManagedObjectHeaderSize,
                            runtimeType: targetMethod.DeclaringType);
                        RuntimeType payloadByRefType = _rts.GetByRefType(targetMethod.DeclaringType);
                        rewrittenReceiver = Node(
                            GenTreeKind.Unary,
                            pc,
                            GenTreeOperator.PtrToByRef,
                            type: payloadByRefType,
                            stackKind: GenStackKind.ByRef,
                            operands: One(payloadPointer),
                            runtimeType: targetMethod.DeclaringType);
                        break;
                    }

                case DevirtualizationReceiverTransform.TakeAddress:
                    {
                        if (receiver.Type is null || !receiver.Type.IsValueType || !targetMethod.DeclaringType.IsValueType)
                            throw Fail(pc, sourceOp, "Devirtualized value-type call has an invalid receiver.");

                        GenTemp receiverTemp = CreateImporterSpillTemp(receiver.Type, receiver.StackKind);
                        statements.Add(Node(
                            GenTreeKind.StoreTemp,
                            pc,
                            sourceOp,
                            operands: One(receiver),
                            int32: receiverTemp.Index));
                        rewrittenReceiver = TempAddress(pc, sourceOp, receiverTemp).Node;
                        break;
                    }

                case DevirtualizationReceiverTransform.Box:
                    {
                        if (receiver.Type is null || !receiver.Type.IsValueType || targetMethod.DeclaringType.IsValueType)
                            throw Fail(pc, sourceOp, "Devirtualized boxing call has an invalid receiver.");

                        MarkInstantiatedType(receiver.Type);
                        rewrittenReceiver = Node(
                            GenTreeKind.Box,
                            pc,
                            ILOpCode.Box,
                            type: _rts.SystemObject,
                            stackKind: GenStackKind.Ref,
                            operands: One(receiver),
                            int32: receiver.Type.TypeId,
                            runtimeType: receiver.Type);
                        break;
                    }

                default:
                    throw Fail(pc, sourceOp, "Unknown devirtualized receiver transform.");
            }

            var rewritten = operands.ToBuilder();
            rewritten[0] = rewrittenReceiver;
            return rewritten.ToImmutable();
        }

        private ImmutableArray<GenTree> MaterializeCallVirtOperandsAndAppendNullCheck(
            List<GenTree> statements, int pc, ILOpCode sourceOp, ImmutableArray<GenTree> operands)
        {
            if (operands.IsDefaultOrEmpty)
                throw Fail(pc, sourceOp, "Devirtualized callvirt has no receiver.");

            var materialized = ImmutableArray.CreateBuilder<GenTree>(operands.Length);
            GenTree receiver = operands[0];
            GenTemp receiverTemp = CreateImporterSpillTemp(receiver.Type, receiver.StackKind);
            statements.Add(Node(GenTreeKind.StoreTemp, pc, sourceOp, operands: One(receiver), int32: receiverTemp.Index));
            materialized.Add(TempLoad(pc, sourceOp, receiverTemp).Node);

            for (int i = 1; i < operands.Length; i++)
            {
                GenTree operand = operands[i];
                GenTemp temp = CreateImporterSpillTemp(operand.Type, operand.StackKind);
                statements.Add(Node(GenTreeKind.StoreTemp, pc, sourceOp, operands: One(operand), int32: temp.Index));
                materialized.Add(TempLoad(pc, sourceOp, temp).Node);
            }

            statements.Add(Node(GenTreeKind.NullCheck, pc, sourceOp, operands: One(TempLoad(pc, sourceOp, receiverTemp).Node)));
            return materialized.ToImmutable();
        }

        private void EmitValueTypeNewObject(
            ImportFrame frame,
            List<StackValue> stack,
            List<GenTree> statements,
            int pc,
            ILOpCode sourceOp,
            int methodToken,
            int userArgCount,
            ImmutableArray<GenTree> userArgs,
            RuntimeMethod ctor,
            RuntimeType valueType)
        {
            if (!valueType.IsValueType)
                throw Fail(pc, sourceOp, "Value-type materialization requires a value-type constructor.");

            SpillEvaluationStackForImportBarrier(statements, stack, pc, sourceOp);

            var temp = CreateStructMaterializationTemp(valueType);
            EmitStructDefaultInitialization(statements, stack, pc, sourceOp, temp, valueType);

            var ctorArgsBuilder = ImmutableArray.CreateBuilder<GenTree>(userArgCount + 1);
            ctorArgsBuilder.Add(TempAddress(pc, sourceOp, temp).Node);
            ctorArgsBuilder.AddRange(userArgs);
            var ctorArgs = ctorArgsBuilder.ToImmutable();

            if (TryInlineCall(ctor, ctorArgs, statements, null, null, pc, sourceOp, out var inlineResult, out bool terminatedBlock, frame.InlineDepth + 1) &&
                !terminatedBlock)
            {
                if (inlineResult is not null)
                    AppendImporterStatement(statements, stack, Node(GenTreeKind.Eval, pc, sourceOp, operands: One(inlineResult)));

                Push(stack, TempLoad(pc, sourceOp, temp));
                return;
            }

            AddDirectDependency(ctor);
            var call = Node(GenTreeKind.Call, pc, sourceOp, stackKind: GenStackKind.Void, operands: ctorArgs,
                int32: userArgCount + 1, int64: methodToken, method: ctor);
            AppendImporterStatement(statements, stack, Node(GenTreeKind.Eval, pc, sourceOp, operands: One(call)));
            Push(stack, TempLoad(pc, sourceOp, temp));
        }

        private void EmitStructDefaultInitialization(List<GenTree> statements, List<StackValue> stack, int pc, ILOpCode sourceOp, GenTemp temp, RuntimeType valueType)
        {
            if (TryEmitFieldWiseStructDefaultInitialization(statements, stack, pc, sourceOp, temp, valueType))
                return;

            var init = Node(GenTreeKind.DefaultValue, pc, GenTreeOperator.None, type: valueType, stackKind: StackKindOf(valueType), runtimeType: valueType);
            AppendImporterStatement(statements, stack, MarkExplicitInit(Node(GenTreeKind.StoreTemp, pc, ILOpCode.Stloc, operands: One(init), int32: temp.Index)));
        }

        private bool TryEmitFieldWiseStructDefaultInitialization(List<GenTree> statements, List<StackValue> stack, int pc, ILOpCode sourceOp, GenTemp temp, RuntimeType valueType)
        {
            if (valueType.Kind != RuntimeTypeKind.Struct)
                return false;

            if (valueType.InstanceFields.Length == 0)
                return true;

            if (!CanExpandStructFieldWise(valueType))
                return false;

            var fields = valueType.InstanceFields;
            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (field.IsStatic)
                    continue;

                var fieldDefault = Node(GenTreeKind.DefaultValue, pc, GenTreeOperator.None, type: field.FieldType, stackKind: StackKindOf(field.FieldType), runtimeType: field.FieldType);
                AppendImporterStatement(statements, stack, MarkExplicitInit(Node(GenTreeKind.StoreField, pc, sourceOp,
                    operands: Two(TempAddress(pc, sourceOp, temp).Node, fieldDefault), field: field, runtimeType: field.FieldType)));
            }

            return true;
        }

        private GenTreeBlock CreateInlineGraphBlock(
            int blockId,
            int syntheticStartPc,
            List<GenTree> statements,
            List<int> successorPcs,
            int entryStackDepth,
            int exitStackDepth)
        {
            var succBlockIds = new List<int>(successorPcs.Count);
            for (int i = 0; i < successorPcs.Count; i++)
                succBlockIds.Add(BlockIdForPc(successorPcs[i]));

            var jumpKind = ClassifyBlockJump(statements, successorPcs);
            GenTreeBlockFlags flags = GenTreeBlockFlags.None;
            if (entryStackDepth != 0) flags |= GenTreeBlockFlags.HasStackEntry;
            if (successorPcs.Count != 0 && exitStackDepth != 0) flags |= GenTreeBlockFlags.HasStackExit;

            return new GenTreeBlock(
                blockId,
                syntheticStartPc,
                syntheticStartPc + 1,
                entryStackDepth,
                exitStackDepth,
                jumpKind,
                flags,
                statements.ToImmutableArray(),
                succBlockIds.ToImmutableArray(),
                successorPcs.ToImmutableArray());
        }

        private List<StackValue> CreateInlineGraphEntryStack(InlineGraphContext context, int calleeStartPc)
        {
            int depth = context.StackDepthAt(calleeStartPc);
            var stack = new List<StackValue>(Math.Max(depth, 4));
            int syntheticPc = context.SyntheticPcForCalleePc(calleeStartPc);
            for (int i = 0; i < depth; i++)
            {
                var temp = GetStackEntryTemp(syntheticPc, i, null, GenStackKind.Unknown);
                Push(stack, TempLoad(context.CallPc, ILOpCode.Nop, temp));
            }
            return stack;
        }

        private List<StackValue> CloneStackValues(IReadOnlyList<StackValue> values, int pc, ILOpCode sourceOp)
        {
            var result = new List<StackValue>(values.Count);
            for (int i = 0; i < values.Count; i++)
                result.Add(CloneStackValue(values[i], pc, sourceOp));
            return result;
        }

        private StackValue CloneStackValue(StackValue value, int pc, ILOpCode sourceOp)
        {
            var node = value.Node;

            if (node.Kind == GenTreeKind.Temp && TryGetTempByIndex(node.Int32, out var temp))
                return TempLoad(pc, sourceOp, temp);

            if (node.Kind == GenTreeKind.TempAddr && TryGetTempByIndex(node.Int32, out temp))
                return TempAddress(pc, sourceOp, temp);

            var clone = CloneTree(node);
            return new StackValue(clone, clone.Type, clone.StackKind);
        }

        private GenTree CloneTree(GenTree node)
        {
            var operands = node.Operands;
            ImmutableArray<GenTree> clonedOperands = ImmutableArray<GenTree>.Empty;
            if (!operands.IsDefaultOrEmpty)
            {
                var builder = ImmutableArray.CreateBuilder<GenTree>(operands.Length);
                for (int i = 0; i < operands.Length; i++)
                    builder.Add(CloneTree(operands[i]));
                clonedOperands = builder.ToImmutable();
            }

            return Node(
                node.Kind,
                node.Pc,
                node.Operator,
                type: node.Type,
                stackKind: node.StackKind,
                operands: clonedOperands,
                int32: node.Int32,
                int64: node.Int64,
                text: node.Text,
                runtimeType: node.RuntimeType,
                field: node.Field,
                method: node.Method,
                convKind: node.ConvKind,
                convFlags: node.ConvFlags,
                targetPc: node.TargetPc,
                targetBlockId: node.TargetBlockId,
                boundsCheckIndexOverride: node.BoundsCheckIndexOverride);
        }

        private int DetermineInlineBudget(RuntimeMethod callee, InlineCandidateInfo candidate, ImmutableArray<GenTree> args, int inlineDepth)
        {
            int budget = callee.HasAggressiveInlining
                ? InlineForceBudget
                : candidate.CodeSize <= InlineSmallOverBudgetSize ? InlineAlwaysBudget : InlineDiscretionaryBudget;

            if (candidate.LooksLikeWrapper)
                budget += 32;
            if (candidate.MostlyLoadStore)
                budget += 32;
            if (candidate.HasCall)
                budget += candidate.LooksLikeWrapper ? 16 : 8;
            if (candidate.HasControlFlow)
                budget += Math.Min(64, candidate.BasicBlockCount * 6);
            if (candidate.HasBackwardBranch && !callee.HasAggressiveInlining)
                budget -= Math.Min(32, candidate.BasicBlockCount * 3);

            int substitutableArgs = 0;
            for (int i = 0; i < args.Length; i++)
            {
                if (candidate.CanSubstituteArgument(i, args[i]))
                    substitutableArgs++;
            }
            budget += Math.Min(24, substitutableArgs * 4);

            if (inlineDepth > 1)
                budget = Math.Max(InlineAlwaysBudget, budget - ((inlineDepth - 1) * 12));

            return Math.Max(InlineAlwaysBudget, budget);
        }

        private static bool IsPureInlineArgument(GenTree arg)
        {
            const GenTreeFlags badFlags =
                GenTreeFlags.ContainsCall |
                GenTreeFlags.CanThrow |
                GenTreeFlags.SideEffect |
                GenTreeFlags.MemoryRead |
                GenTreeFlags.MemoryWrite |
                GenTreeFlags.GlobalRef |
                GenTreeFlags.Indirect |
                GenTreeFlags.Allocation |
                GenTreeFlags.ControlFlow |
                GenTreeFlags.ExceptionFlow |
                GenTreeFlags.Ordered |
                GenTreeFlags.AddressExposed;

            var disallowed = badFlags;
            if (arg.Kind == GenTreeKind.TempAddr)
                disallowed &= ~GenTreeFlags.AddressExposed;

            if ((arg.Flags & disallowed) != 0)
                return false;

            return arg.Kind is GenTreeKind.ConstI4 or GenTreeKind.ConstI8 or GenTreeKind.ConstR4Bits or GenTreeKind.ConstR8Bits or
                GenTreeKind.ConstNull or GenTreeKind.ConstString or GenTreeKind.TypeHandle or GenTreeKind.Local or GenTreeKind.Arg or GenTreeKind.Temp or
                GenTreeKind.TempAddr or GenTreeKind.DefaultValue or GenTreeKind.SizeOf or GenTreeKind.Unary or GenTreeKind.Binary or GenTreeKind.Conv;
        }

        private sealed class InlineGraphPlan
        {
            public int[] StackDepths { get; }
            public ImmutableArray<int> Leaders { get; }

            public InlineGraphPlan(int[] stackDepths, ImmutableArray<int> leaders)
            {
                StackDepths = stackDepths ?? Array.Empty<int>();
                Leaders = leaders.IsDefault ? ImmutableArray<int>.Empty : leaders;
            }
        }

        private sealed class InlineGraphContext
        {
            private readonly Dictionary<int, int> _syntheticPcsByCalleePc;
            private readonly Dictionary<int, int> _blockIdsByCalleePc;

            public ImportFrame Frame { get; }
            public InlineGraphPlan Plan { get; }
            public int CallPc { get; }
            public int ContinuationPc { get; }
            public GenTemp? ReturnTemp { get; }
            public List<StackValue> CallerContinuationStack { get; }

            public InlineGraphContext(
                ImportFrame frame,
                InlineGraphPlan plan,
                int callPc,
                int continuationPc,
                Dictionary<int, int> syntheticPcsByCalleePc,
                Dictionary<int, int> blockIdsByCalleePc,
                GenTemp? returnTemp,
                List<StackValue> callerContinuationStack)
            {
                Frame = frame;
                Plan = plan;
                CallPc = callPc;
                ContinuationPc = continuationPc;
                _syntheticPcsByCalleePc = syntheticPcsByCalleePc;
                _blockIdsByCalleePc = blockIdsByCalleePc;
                ReturnTemp = returnTemp;
                CallerContinuationStack = callerContinuationStack;
            }

            public int StackDepthAt(int calleePc)
            {
                if ((uint)calleePc >= (uint)Plan.StackDepths.Length)
                    return 0;
                int depth = Plan.StackDepths[calleePc];
                return depth == UnreachableStackDepth ? 0 : depth;
            }

            public int SyntheticPcForCalleePc(int calleePc)
            {
                if (!_syntheticPcsByCalleePc.TryGetValue(calleePc, out int syntheticPc))
                    throw new GenTreeBuildException($"No synthetic inline block for callee pc {calleePc}.");
                return syntheticPc;
            }

            public int BlockIdForCalleePc(int calleePc)
            {
                if (!_blockIdsByCalleePc.TryGetValue(calleePc, out int blockId))
                    throw new GenTreeBuildException($"No inline block id for callee pc {calleePc}.");
                return blockId;
            }
        }

        private sealed class InlineCandidateInfo
        {
            private readonly int[] _argLoadCounts;
            private readonly int[] _argStoreCounts;
            private readonly int[] _argAddressCounts;
            private readonly int[] _localAddressCounts;
            private readonly bool[] _localNeedsInit;

            public int Cost { get; }
            public int CodeSize { get; }
            public int BasicBlockCount { get; }
            public bool MostlyLoadStore { get; }
            public bool LooksLikeWrapper { get; }
            public bool HasCall { get; }
            public bool ReturnsValue { get; }
            public bool HasControlFlow { get; }
            public bool HasBackwardBranch { get; }
            public bool HasThrow { get; }
            public InlineGraphPlan? Plan { get; }

            public InlineCandidateInfo(
                int cost,
                int codeSize,
                int basicBlockCount,
                int[] argLoadCounts,
                int[] argStoreCounts,
                int[] argAddressCounts,
                int[] localAddressCounts,
                bool[] localNeedsInit,
                bool mostlyLoadStore,
                bool looksLikeWrapper,
                bool hasCall,
                bool returnsValue,
                bool hasControlFlow,
                bool hasBackwardBranch,
                bool hasThrow,
                InlineGraphPlan? plan)
            {
                Cost = cost;
                CodeSize = codeSize;
                BasicBlockCount = basicBlockCount;
                _argLoadCounts = argLoadCounts ?? Array.Empty<int>();
                _argStoreCounts = argStoreCounts ?? Array.Empty<int>();
                _argAddressCounts = argAddressCounts ?? Array.Empty<int>();
                _localAddressCounts = localAddressCounts ?? Array.Empty<int>();
                _localNeedsInit = localNeedsInit ?? Array.Empty<bool>();
                MostlyLoadStore = mostlyLoadStore;
                LooksLikeWrapper = looksLikeWrapper;
                HasCall = hasCall;
                ReturnsValue = returnsValue;
                HasControlFlow = hasControlFlow;
                HasBackwardBranch = hasBackwardBranch;
                HasThrow = hasThrow;
                Plan = plan;
            }

            public bool CanSubstituteArgument(int index, GenTree arg)
            {
                if (HasControlFlow)
                    return false;

                if ((uint)index >= (uint)_argLoadCounts.Length ||
                    (uint)index >= (uint)_argStoreCounts.Length ||
                    (uint)index >= (uint)_argAddressCounts.Length)
                {
                    return false;
                }

                return _argStoreCounts[index] == 0 &&
                    _argAddressCounts[index] == 0 &&
                    _argLoadCounts[index] == 1 &&
                    IsPureInlineArgument(arg);
            }

            public bool LocalNeedsInit(int index)
                => (uint)index < (uint)_localNeedsInit.Length && _localNeedsInit[index];
        }

        private GenTemp CreateInlineGraphTemp(RuntimeType? type, GenStackKind stackKind)
        {
            int index = _nextTempIndex++;
            var temp = new GenTemp(index, GenTempKind.StackSpill, type, stackKind);
            _temps.Add(temp);
            _materializedImporterTempIds.Add(index);
            return temp;
        }

        private GenTemp CreateInlineTemp(GenTempKind kind, RuntimeType? type, GenStackKind stackKind)
        {
            int index = _nextTempIndex++;
            var temp = new GenTemp(index, kind, type, stackKind);
            _temps.Add(temp);
            return temp;
        }

        private GenTemp CheckedInlineArgTemp(GenTemp[] temps, int index, int pc, ILOpCode op)
        {
            if ((uint)index >= (uint)temps.Length)
                throw Fail(pc, op, $"Inline argument index {index} is out of range. Argument count: {temps.Length}.");
            return temps[index];
        }

        private GenTemp CheckedInlineLocalTemp(GenTemp[] temps, int index, int pc, ILOpCode op)
        {
            if ((uint)index >= (uint)temps.Length)
                throw Fail(pc, op, $"Inline local index {index} is out of range. Local count: {temps.Length}.");
            return temps[index];
        }

        private static bool RequiresTypeInitializationBeforeCall(RuntimeMethod target)
        {
            if (StringComparer.Ordinal.Equals(target.Name, ".cctor") || target.DeclaringType.IsBeforeFieldInit)
                return false;

            return target.IsStatic ||
                target.DeclaringType.IsValueType ||
                StringComparer.Ordinal.Equals(target.Name, ".ctor");
        }

        private static bool RequiresTypeInitializationBeforeNewObject(RuntimeType type)
            => !type.IsBeforeFieldInit;

        private ImmutableArray<GenTree> MaterializeTypeInitializationOperands(
            List<GenTree> statements,
            int pc,
            ILOpCode sourceOp,
            ImmutableArray<GenTree> operands)
        {
            if (operands.IsDefaultOrEmpty)
                return ImmutableArray<GenTree>.Empty;

            var result = ImmutableArray.CreateBuilder<GenTree>(operands.Length);
            for (int i = 0; i < operands.Length; i++)
            {
                GenTree operand = operands[i];
                var temp = CreateImporterSpillTemp(operand.Type, operand.StackKind);
                statements.Add(Node(GenTreeKind.StoreTemp, pc, sourceOp, operands: One(operand), int32: temp.Index));
                result.Add(TempLoad(pc, sourceOp, temp).Node);
            }
            return result.ToImmutable();
        }

        private GenTree MaterializeTypeInitializationOperand(
            List<GenTree> statements,
            int pc,
            ILOpCode sourceOp,
            GenTree operand)
        {
            var temp = CreateImporterSpillTemp(operand.Type, operand.StackKind);
            statements.Add(Node(GenTreeKind.StoreTemp, pc, sourceOp, operands: One(operand), int32: temp.Index));
            return TempLoad(pc, sourceOp, temp).Node;
        }

        private bool NeedsTypeInitialization(RuntimeType type)
        {
            if (_rts.Target.IsRegisterBytecode)
                return false;
            _rts.EnsureConstructedMembers(type);
            if (FindTypeInitializer(type) is null)
                return false;
            return !(StringComparer.Ordinal.Equals(_method.Name, ".cctor") && ReferenceEquals(_method.DeclaringType, type));
        }

        private bool AppendTypeInitialization(
            List<StackValue> stack,
            List<GenTree> statements,
            int pc,
            ILOpCode sourceOp,
            RuntimeType type)
        {
            if (!NeedsTypeInitialization(type))
                return false;
            RuntimeMethod cctor = FindTypeInitializer(type)!;
            AddDirectDependency(cctor);
            SpillEvaluationStackForImportBarrier(statements, stack, pc, sourceOp);
            statements.Add(Node(GenTreeKind.ClassInit, pc, sourceOp, runtimeType: type));
            return true;
        }

        private void AddTypeInitializerDependency(RuntimeType type)
        {
            _rts.EnsureConstructedMembers(type);

            RuntimeMethod? cctor = FindTypeInitializer(type);
            if (cctor is not null)
                AddDirectDependency(cctor);
        }

        internal static RuntimeMethod? FindTypeInitializer(RuntimeType type)
        {
            for (int i = 0; i < type.Methods.Length; i++)
            {
                RuntimeMethod method = type.Methods[i];
                if (method.IsStatic && method.ParameterTypes.Length == 0 && StringComparer.Ordinal.Equals(method.Name, ".cctor"))
                    return method;
            }
            return null;
        }

        private void AddDirectDependency(RuntimeMethod method)
        {
            if (method.CilBody is null)
                return;
            if (_directDependencyIds.Add(method.MethodId))
                _directDependencies.Add(method);
        }

        private void AddVirtualDependency(RuntimeMethod method)
        {
            if (_virtualDependencyIds.Add(method.MethodId))
                _virtualDependencies.Add(method);
        }

        private ImmutableArray<GenTree> PopMany(List<StackValue> stack, int count, int pc, ILOpCode op)
        {
            if (count < 0)
                throw Fail(pc, op, $"Negative pop count {count}.");
            if (stack.Count < count)
                throw Fail(pc, op, $"Evaluation stack underflow. Need {count}, have {stack.Count}.");

            var result = new GenTree[count];
            for (int i = count - 1; i >= 0; i--)
                result[i] = Pop(stack, pc, op).Node;
            return result.ToImmutableArray();
        }

        private RuntimeType CheckedLocalType(int index, int pc)
        {
            if ((uint)index >= (uint)_localTypes.Length)
                throw Fail(pc, ILOpCode.Ldloc, $"Local index {index} is out of range. Local count: {_localTypes.Length}.");
            return _localTypes[index];
        }

        private bool RuntimeTypePredicate(string name, RuntimeType type, int pc)
        {
            if (type.Kind == RuntimeTypeKind.TypeParam)
                throw Fail(pc, ILOpCode.Ldtoken, $"{name} requires a closed generic context.");

            if (name == "get_IsPrimitive")
            {
                _rts.EnsureRuntimeTypeReady(type);
                return IsPrimitiveRuntimeType(type);
            }

            return name == "get_IsValueType" ? type.IsValueType : type.Kind == RuntimeTypeKind.Enum;
        }

        private bool RuntimeTypesEqual(RuntimeType left, RuntimeType right, int pc, ILOpCode op)
        {
            if (left.Kind == RuntimeTypeKind.TypeParam || right.Kind == RuntimeTypeKind.TypeParam)
                throw Fail(pc, op, "Type equality requires a closed generic context.");

            return left.TypeId == right.TypeId;
        }

        private static bool IsPrimitiveRuntimeType(RuntimeType type)
        {
            return type.PrimitiveKind is
                RuntimePrimitiveKind.Boolean or
                RuntimePrimitiveKind.Char or
                RuntimePrimitiveKind.Int8 or
                RuntimePrimitiveKind.UInt8 or
                RuntimePrimitiveKind.Int16 or
                RuntimePrimitiveKind.UInt16 or
                RuntimePrimitiveKind.Int32 or
                RuntimePrimitiveKind.UInt32 or
                RuntimePrimitiveKind.Int64 or
                RuntimePrimitiveKind.UInt64 or
                RuntimePrimitiveKind.NativeInt or
                RuntimePrimitiveKind.NativeUInt or
                RuntimePrimitiveKind.Single or
                RuntimePrimitiveKind.Double;
        }

        private void ImportObjectTypeEquals(
            List<StackValue> stack,
            List<GenTree> statements,
            StackValue receiver,
            RuntimeType receiverType,
            RuntimeType targetType,
            int pc,
            ILOpCode op)
        {
            if (receiverType.Kind == RuntimeTypeKind.TypeParam || targetType.Kind == RuntimeTypeKind.TypeParam)
                throw Fail(pc, op, "GetType equality requires a closed generic context.");

            GenTree runtimeReceiver = receiver.Node;
            if (receiverType.IsValueType)
            {
                if (!IsSystemNullableRuntimeType(receiverType))
                {
                    AppendImporterStatement(statements, stack, CreateDiscardStatement(receiver.Node, pc, op));
                    Push(stack, Node(
                        GenTreeKind.ConstI4,
                        pc,
                        op,
                        stackKind: GenStackKind.I4,
                        int32: receiverType.TypeId == targetType.TypeId ? 1 : 0));
                    return;
                }

                MarkInstantiatedType(receiverType);
                runtimeReceiver = Node(
                    GenTreeKind.Box,
                    pc,
                    ILOpCode.Box,
                    type: _rts.SystemObject,
                    stackKind: GenStackKind.Ref,
                    operands: One(receiver.Node),
                    int32: receiverType.TypeId,
                    runtimeType: receiverType);
            }

            GenTemp receiverTemp = CreateImporterSpillTemp(runtimeReceiver.Type, runtimeReceiver.StackKind);
            AppendImporterStatement(statements, stack, Node(
                GenTreeKind.StoreTemp,
                pc,
                op,
                operands: One(runtimeReceiver),
                int32: receiverTemp.Index));
            AppendImporterStatement(statements, stack, Node(
                GenTreeKind.NullCheck,
                pc,
                op,
                operands: One(TempLoad(pc, op, receiverTemp).Node)));

            GenTree actualTypeId = LoadRuntimeObjectTypeId(TempLoad(pc, op, receiverTemp).Node, pc);
            GenTree targetTypeId = Node(
                GenTreeKind.ConstI4,
                pc,
                ILOpCode.Ldc_I4,
                stackKind: GenStackKind.I4,
                int32: targetType.TypeId);
            PushImportedValue(stack, statements, Node(
                GenTreeKind.Binary,
                pc,
                ILOpCode.Ceq,
                stackKind: GenStackKind.I4,
                operands: Two(actualTypeId, targetTypeId)));
        }

        private static bool IsSystemNullableRuntimeType(RuntimeType type)
        {
            if (!type.IsValueType || type.GenericTypeArguments.Length != 1)
                return false;

            RuntimeType definition = type.GenericTypeDefinition ?? type;
            return definition.Namespace == "System" &&
                definition.Name.StartsWith("Nullable", StringComparison.Ordinal);
        }

        private GenTree LoadRuntimeObjectTypeId(GenTree receiver, int pc)
        {
            GenTree objectAddress = Node(
                GenTreeKind.PointerElementAddr,
                pc,
                GenTreeOperator.None,
                stackKind: GenStackKind.Ptr,
                operands: Two(receiver, ConstI4(pc, ILOpCode.Ldc_I4, 0)),
                int32: 1);

            if (_rts.Target.IsRegisterBytecode)
            {
                return Node(
                    GenTreeKind.LoadIndirect,
                    pc,
                    ILOpCode.Ldobj,
                    stackKind: GenStackKind.I4,
                    operands: One(objectAddress));
            }

            GenTree methodTable = Node(
                GenTreeKind.LoadIndirect,
                pc,
                ILOpCode.Ldobj,
                stackKind: GenStackKind.Ptr,
                operands: One(objectAddress));
            GenTree typeIdAddress = Node(
                GenTreeKind.PointerElementAddr,
                pc,
                GenTreeOperator.None,
                stackKind: GenStackKind.Ptr,
                operands: Two(methodTable, ConstI4(pc, ILOpCode.Ldc_I4, 12 + _rts.Target.PointerSize)),
                int32: 1);
            return Node(
                GenTreeKind.LoadIndirect,
                pc,
                ILOpCode.Ldobj,
                stackKind: GenStackKind.I4,
                operands: One(typeIdAddress));
        }

        private RuntimeType CheckedArgType(int index, int pc)
        {
            if ((uint)index >= (uint)_argTypes.Length)
                throw Fail(pc, ILOpCode.Ldarg, $"Argument index {index} is out of range. Argument count: {_argTypes.Length}.");
            return _argTypes[index];
        }

        private GenTree Node(
            GenTreeKind kind,
            int pc,
            ILOpCode sourceOp,
            RuntimeType? type = null,
            GenStackKind stackKind = GenStackKind.Void,
            ImmutableArray<GenTree> operands = default,
            int int32 = 0,
            long int64 = 0,
            string? text = null,
            RuntimeType? runtimeType = null,
            RuntimeField? field = null,
            RuntimeMethod? method = null,
            NumericConvKind convKind = default,
            NumericConvFlags convFlags = default,
            int targetPc = -1,
            int targetBlockId = -1,
            int boundsCheckIndexOverride = -1,
            RuntimeIntrinsicId intrinsicId = RuntimeIntrinsicId.None)
            => Node(kind, pc, ToOperator(sourceOp), type, stackKind, operands, int32, int64, text, runtimeType, field, method,
                convKind, convFlags, targetPc, targetBlockId, boundsCheckIndexOverride, intrinsicId);
        private static GenTreeOperator ToOperator(ILOpCode op) => op switch
        {
            ILOpCode.Add => GenTreeOperator.Add,
            ILOpCode.Add_Ovf => GenTreeOperator.AddOvf,
            ILOpCode.Add_Ovf_Un => GenTreeOperator.AddOvfUn,
            ILOpCode.Sub => GenTreeOperator.Sub,
            ILOpCode.Sub_Ovf => GenTreeOperator.SubOvf,
            ILOpCode.Sub_Ovf_Un => GenTreeOperator.SubOvfUn,
            ILOpCode.Mul => GenTreeOperator.Mul,
            ILOpCode.Mul_Ovf => GenTreeOperator.MulOvf,
            ILOpCode.Mul_Ovf_Un => GenTreeOperator.MulOvfUn,
            ILOpCode.Div => GenTreeOperator.Div,
            ILOpCode.Div_Un => GenTreeOperator.DivUn,
            ILOpCode.Rem => GenTreeOperator.Rem,
            ILOpCode.Rem_Un => GenTreeOperator.RemUn,
            ILOpCode.And => GenTreeOperator.And,
            ILOpCode.Or => GenTreeOperator.Or,
            ILOpCode.Xor => GenTreeOperator.Xor,
            ILOpCode.Shl => GenTreeOperator.Shl,
            ILOpCode.Shr => GenTreeOperator.Shr,
            ILOpCode.Shr_Un => GenTreeOperator.ShrUn,
            ILOpCode.Ceq => GenTreeOperator.Ceq,
            ILOpCode.Clt => GenTreeOperator.Clt,
            ILOpCode.Clt_Un => GenTreeOperator.CltUn,
            ILOpCode.Cgt => GenTreeOperator.Cgt,
            ILOpCode.Cgt_Un => GenTreeOperator.CgtUn,
            ILOpCode.Neg => GenTreeOperator.Neg,
            ILOpCode.Not => GenTreeOperator.Not,
            _ => GenTreeOperator.None,
        };
        private GenTree Node(
            GenTreeKind kind,
            int pc,
            GenTreeOperator oper,
            RuntimeType? type = null,
            GenStackKind stackKind = GenStackKind.Void,
            ImmutableArray<GenTree> operands = default,
            int int32 = 0,
            long int64 = 0,
            string? text = null,
            RuntimeType? runtimeType = null,
            RuntimeField? field = null,
            RuntimeMethod? method = null,
            NumericConvKind convKind = default,
            NumericConvFlags convFlags = default,
            int targetPc = -1,
            int targetBlockId = -1,
            int boundsCheckIndexOverride = -1,
            RuntimeIntrinsicId intrinsicId = RuntimeIntrinsicId.None)
        {
            var actualOperands = operands.IsDefault ? ImmutableArray<GenTree>.Empty : operands;
            if (kind == GenTreeKind.Eval &&
                actualOperands.Length == 1 &&
                actualOperands[0].Operands.Length == 1 &&
                (actualOperands[0].Operands[0].StackKind is GenStackKind.Ref or GenStackKind.Null) &&
                (actualOperands[0].Kind is GenTreeKind.Field or GenTreeKind.FieldAddr or GenTreeKind.ArrayLength or GenTreeKind.ArrayDataRef))
            {
                GenTree value = actualOperands[0];
                kind = GenTreeKind.NullCheck;
                pc = value.Pc;
                oper = value.Operator;
                type = null;
                stackKind = GenStackKind.Void;
                actualOperands = One(value.Operands[0]);
            }

            if ((kind is GenTreeKind.Call or GenTreeKind.VirtualCall) &&
                actualOperands.Length == 1 &&
                IsLengthGetter(method))
            {
                kind = GenTreeKind.ArrayLength;
                type = method!.ReturnType;
                stackKind = GenStackKind.I4;
                method = null;
            }

            var flags = ComputeFlags(kind, oper, type, stackKind, actualOperands, convFlags, intrinsicId);

            return new GenTree(
                ++_nextNodeId,
                kind,
                pc,
                oper,
                type,
                stackKind,
                flags,
                actualOperands,
                int32,
                int64,
                text,
                runtimeType,
                field,
                method,
                convKind,
                convFlags,
                targetPc,
                targetBlockId,
                boundsCheckIndexOverride,
                intrinsicId);
        }

        private static GenTree MarkExplicitInit(GenTree store)
        {
            store.Flags |= GenTreeFlags.ExplicitInit;
            return store;
        }

        private GenTreeFlags ComputeFlags(GenTreeKind kind, GenTreeOperator oper, RuntimeType? type, GenStackKind stackKind, ImmutableArray<GenTree> operands, NumericConvFlags convFlags, RuntimeIntrinsicId intrinsicId)
        {
            GenTreeFlags flags = GenTreeFlags.None;
            for (int i = 0; i < operands.Length; i++)
                flags |= operands[i].Flags & ~(GenTreeFlags.AssertionProperties | GenTreeFlags.ExplicitInit);

            switch (kind)
            {
                case GenTreeKind.Local:
                case GenTreeKind.Arg:
                case GenTreeKind.Temp:
                    flags |= GenTreeFlags.LocalUse;
                    break;

                case GenTreeKind.LocalAddr:
                case GenTreeKind.ArgAddr:
                case GenTreeKind.TempAddr:
                    flags |= GenTreeFlags.LocalUse;
                    break;

                case GenTreeKind.StoreLocal:
                case GenTreeKind.StoreArg:
                case GenTreeKind.StoreTemp:
                    flags |= GenTreeFlags.SideEffect | GenTreeFlags.LocalDef | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.Field:
                case GenTreeKind.FieldAddr:
                    flags |= GenTreeFlags.MemoryRead | GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.StaticField:
                case GenTreeKind.StaticFieldAddr:
                    flags |= GenTreeFlags.MemoryRead | GenTreeFlags.GlobalRef;
                    break;

                case GenTreeKind.LoadIndirect:
                    flags |= GenTreeFlags.MemoryRead | GenTreeFlags.Indirect | GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.StoreIndirect:
                    flags |= GenTreeFlags.SideEffect | GenTreeFlags.MemoryWrite | GenTreeFlags.Indirect | GenTreeFlags.CanThrow | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.StoreField:
                case GenTreeKind.StoreStaticField:
                    flags |= GenTreeFlags.SideEffect | GenTreeFlags.MemoryWrite | GenTreeFlags.GlobalRef | GenTreeFlags.CanThrow | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.NewObject:
                    flags |= GenTreeFlags.ContainsCall | GenTreeFlags.Allocation | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow | GenTreeFlags.GlobalRef | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.NewDelegate:
                case GenTreeKind.DelegateCombine:
                case GenTreeKind.DelegateRemove:
                case GenTreeKind.NewArray:
                case GenTreeKind.Box:
                    flags |= GenTreeFlags.Allocation | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.NullCheck:
                    flags |= GenTreeFlags.CanThrow | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.ArrayLength:
                case GenTreeKind.ArrayElement:
                case GenTreeKind.ArrayElementAddr:
                case GenTreeKind.ArrayDataRef:
                    flags |= GenTreeFlags.MemoryRead | GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.StoreArrayElement:
                    flags |= GenTreeFlags.SideEffect | GenTreeFlags.MemoryWrite | GenTreeFlags.CanThrow | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.StaticData:
                    flags |= GenTreeFlags.Allocation | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow | GenTreeFlags.GlobalRef | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.StackAlloc:
                    flags |= GenTreeFlags.Allocation | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.CastClass:
                case GenTreeKind.UnboxAny:
                    flags |= GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.Conv:
                    if ((convFlags & NumericConvFlags.Checked) != 0)
                        flags |= GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.Binary:
                    if (GenTreeArithmeticSemantics.BinaryOperationCanThrow(oper, type, stackKind, operands, _rts.Target))
                        flags |= GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.Intrinsic:
                    flags |= GenTreeIntrinsicSemantics.Flags(intrinsicId);
                    break;

                case GenTreeKind.ClassInit:
                case GenTreeKind.Call:
                case GenTreeKind.IndirectCall:
                case GenTreeKind.VirtualCall:
                case GenTreeKind.DelegateInvoke:
                    flags |= GenTreeFlags.ContainsCall | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow | GenTreeFlags.GlobalRef | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.Branch:
                case GenTreeKind.BranchTrue:
                case GenTreeKind.BranchFalse:
                case GenTreeKind.Return:
                case GenTreeKind.EndFinally:
                    flags |= GenTreeFlags.ControlFlow | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.Throw:
                case GenTreeKind.Rethrow:
                    flags |= GenTreeFlags.ControlFlow | GenTreeFlags.ExceptionFlow | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow | GenTreeFlags.Ordered;
                    break;
            }

            return flags;
        }

        private static ImmutableArray<GenTree> One(GenTree node) => ImmutableArray.Create(node);
        private static ImmutableArray<GenTree> Two(GenTree left, GenTree right) => ImmutableArray.Create(left, right);

        private static void Push(List<StackValue> stack, StackValue value) => stack.Add(value);
        private static void Push(List<StackValue> stack, GenTree node) => stack.Add(new StackValue(node, node.Type, node.StackKind));

        private static StackValue Pop(List<StackValue> stack, int pc, ILOpCode op)
        {
            if (stack.Count == 0)
                throw new GenTreeBuildException($"Evaluation stack underflow at pc {pc}, op {op}.");
            int last = stack.Count - 1;
            var value = stack[last];
            stack.RemoveAt(last);
            return value;
        }

        private int BlockIdForPc(int pc)
        {
            if (!_pcToBlockId.TryGetValue(pc, out int id))
                throw Fail(pc, ILOpCode.Nop, $"No block starts at target pc {pc}.");
            return id;
        }

        private static void AddSuccessor(List<int> successors, int pc)
        {
            if (pc < 0) return;
            for (int i = 0; i < successors.Count; i++)
            {
                if (successors[i] == pc)
                    return;
            }
            successors.Add(pc);
        }

        private static bool IsVoid(RuntimeType t)
            => t.Namespace == "System" && t.Name == "Void";

        private static GenStackKind StackKindOf(NumericConvKind kind)
        {
            return kind switch
            {
                NumericConvKind.I8 or NumericConvKind.U8 => GenStackKind.I8,
                NumericConvKind.R4 => GenStackKind.R4,
                NumericConvKind.R8 => GenStackKind.R8,
                NumericConvKind.NativeInt => GenStackKind.NativeInt,
                NumericConvKind.NativeUInt => GenStackKind.NativeUInt,
                _ => GenStackKind.I4,
            };
        }

        private static GenStackKind StackKindOf(RuntimeType? type)
        {
            if (type is null)
                return GenStackKind.Unknown;

            if (IsVoid(type))
                return GenStackKind.Void;

            if (type.IsReferenceType)
                return GenStackKind.Ref;

            if (type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.FunctionPointer)
                return GenStackKind.Ptr;

            if (type.Kind == RuntimeTypeKind.ByRef)
                return GenStackKind.ByRef;

            if (type.Kind == RuntimeTypeKind.TypeParam)
                return GenStackKind.Value;

            if (type.Kind == RuntimeTypeKind.Enum)
                return type.SizeOf <= 4 ? GenStackKind.I4 : GenStackKind.I8;

            if (type.Namespace == "System")
            {
                switch (type.Name)
                {
                    case "Boolean":
                    case "Char":
                    case "SByte":
                    case "Byte":
                    case "Int16":
                    case "UInt16":
                    case "Int32":
                    case "UInt32":
                        return GenStackKind.I4;
                    case "Int64":
                    case "UInt64":
                        return GenStackKind.I8;
                    case "Single":
                        return GenStackKind.R4;
                    case "Double":
                        return GenStackKind.R8;
                    case "IntPtr":
                        return GenStackKind.NativeInt;
                    case "UIntPtr":
                        return GenStackKind.NativeUInt;
                }
            }

            return GenStackKind.Value;
        }

        private GenTreeBuildException Fail(int pc, ILOpCode op, string message)
        {
            return new GenTreeBuildException(
                $"GenTree build failed in {_module.Name}:{_method.DeclaringType.Namespace}.{_method.DeclaringType.Name}.{_method.Name} " +
                $"at pc {pc}, op {op}: {message}");
        }

        private readonly struct StackValue
        {
            public readonly GenTree Node;
            public readonly RuntimeType? Type;
            public readonly GenStackKind StackKind;

            public StackValue(GenTree node, RuntimeType? type, GenStackKind stackKind)
            {
                Node = node;
                Type = type;
                StackKind = stackKind;
            }
        }
    }
}
