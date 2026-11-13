using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal enum ContinuationContextHandling : byte
    {
        None,
        ContinueOnCapturedContext,
        ContinueOnThreadPool,
    }

    internal sealed partial class GenTreeMethodBuilder
    {
        private const string CompilerServicesNamespace = "System.Runtime.CompilerServices";

        private sealed class AsyncCallSite
        {
            public AsyncCallSite(GenTree continuationStatement, GenTemp? result, GenTemp continuation, ContinuationContextHandling contextHandling, bool isTailAwait, int pc)
            {
                ContinuationStatement = continuationStatement;
                Result = result;
                Continuation = continuation;
                ContextHandling = contextHandling;
                IsTailAwait = isTailAwait;
                Pc = pc;
            }

            public GenTree ContinuationStatement { get; }
            public GenTemp? Result { get; }
            public GenTemp Continuation { get; }
            public ContinuationContextHandling ContextHandling { get; }
            public bool IsTailAwait { get; }
            public int Pc { get; }
            public int CallBlock { get; set; } = -1;
            public int Remainder { get; set; } = -1;
            public int RemainderPc { get; set; } = -1;
            public int Suspension { get; set; } = -1;
            public int SuspensionPc { get; set; } = -1;
            public int Resumption { get; set; } = -1;
            public int ResumptionPc { get; set; } = -1;
            public GenTreeEhRegion Region { get; set; }
        }

        private readonly List<AsyncCallSite> _asyncCallSites = new();
        private GenTree? _nextCallAsyncContinuation;
        private bool _nextAwaitIsTail;
        private GenTemp? _lastAsyncContinuation;

        private static bool IsAsyncSuspend(RuntimeMethod method)
            => IsSystemMethod(method, CompilerServicesNamespace, "AsyncHelpers", "AsyncSuspend");

        private bool TryImportAsyncHelper(List<StackValue> stack, List<GenTree> statements, RuntimeMethod method, int pc, ILOpCode op, out bool terminatedBlock)
        {
            terminatedBlock = false;
            if (!method.IsStatic || method.DeclaringType.AssemblyName != "std" || method.DeclaringType.Namespace != CompilerServicesNamespace)
                return false;

            if (method.DeclaringType.Name == "RuntimeHelpers" && method.Name == "SetNextCallAsyncContinuation")
            {
                StackValue value = Pop(stack, pc, op);
                GenTemp temp = CreateImporterSpillTemp(_rts.ContinuationType, GenStackKind.Ref);
                statements.Add(Node(GenTreeKind.StoreTemp, pc, op, operands: One(value.Node), int32: temp.Index));
                _nextCallAsyncContinuation = TempLoad(pc, op, temp).Node;
                return true;
            }

            if (method.DeclaringType.Name != "AsyncHelpers")
                return false;

            switch (method.Name)
            {
                case "AsyncSuspend":
                    {
                        StackValue continuation = Pop(stack, pc, op);
                        GenTree suspend = Node(GenTreeKind.Return, pc, op, operands: One(continuation.Node));
                        suspend.Flags |= GenTreeFlags.AsyncSuspend;
                        statements.Add(suspend);
                        terminatedBlock = true;
                        return true;
                    }
                case "AsyncCallContinuation":
                    Push(stack, _lastAsyncContinuation is GenTemp last
                        ? TempLoad(pc, op, last).Node
                        : Node(GenTreeKind.ConstNull, pc, op, stackKind: GenStackKind.Null));
                    return true;
                case "TailAwait":
                    _nextAwaitIsTail = true;
                    return true;
                default:
                    return false;
            }
        }

        // call Task M(); [stloc x; ldloca x;] [ldc.i4 b; call ConfigureAwait(bool);] call AsyncHelpers.Await(...)
        private bool TryMatchTaskAwaitPattern(ImportFrame frame, int pc, out int consumed, out ContinuationContextHandling handling)
        {
            consumed = 0;
            handling = ContinuationContextHandling.ContinueOnCapturedContext;
            ImmutableArray<CilInstruction> instructions = frame.Body.Instructions;
            int next = pc + 1;
            bool storedValueTask = frame.HasInstruction(next, ILOpCode.Stloc) && frame.HasInstruction(next + 1, ILOpCode.Ldloca) &&
                instructions[next].Int32 == instructions[next + 1].Int32;
            if (storedValueTask)
                next += 2;

            if (frame.HasInstruction(next, ILOpCode.Ldc_I4) &&
                (frame.HasInstruction(next + 1, ILOpCode.Call) || frame.HasInstruction(next + 1, ILOpCode.Callvirt)) &&
                IsConfigureAwait(ResolveMethodIn(frame, instructions[next + 1].Token)))
            {
                handling = instructions[next].Int32 == 0
                    ? ContinuationContextHandling.ContinueOnThreadPool
                    : ContinuationContextHandling.ContinueOnCapturedContext;
                next += 2;
            }
            else if (storedValueTask)
            {
                return false;
            }

            if (!frame.HasInstruction(next, ILOpCode.Call) ||
                !IsSystemMethod(ResolveMethodIn(frame, instructions[next].Token), CompilerServicesNamespace, "AsyncHelpers", "Await"))
            {
                return false;
            }

            consumed = next - pc;
            return true;
        }

        private static bool IsConfigureAwait(RuntimeMethod method)
            => method.Name == "ConfigureAwait" && method.HasThis && method.ParameterTypes.Length == 1 &&
               method.ParameterTypes[0].PrimitiveKind == RuntimePrimitiveKind.Boolean &&
               RuntimeTypeSystem.IsTaskOrValueTask(method.DeclaringType);

        private void EmitAsyncCall(List<StackValue> stack, List<GenTree> statements, int pc, ILOpCode op, RuntimeMethod method,
            ImmutableArray<GenTree> args, ContinuationContextHandling handling)
        {
            GenTree continuationArgument = _nextCallAsyncContinuation ?? Node(GenTreeKind.ConstNull, pc, op, stackKind: GenStackKind.Null);
            _nextCallAsyncContinuation = null;
            bool isTailAwait = _nextAwaitIsTail;
            _nextAwaitIsTail = false;
            if (isTailAwait)
                handling = ContinuationContextHandling.None;

            AddDirectDependency(method);
            SpillEvaluationStackForImportBarrier(statements, stack, pc, op);
            bool returnsVoid = IsVoid(method.ReturnType);
            ImmutableArray<GenTree> operands = args.Add(continuationArgument);
            GenTree call = Node(GenTreeKind.Call, pc, op,
                type: returnsVoid ? null : method.ReturnType,
                stackKind: returnsVoid ? GenStackKind.Void : StackKindOf(method.ReturnType),
                operands: operands,
                int32: operands.Length,
                method: method);

            GenTemp? result = null;
            if (returnsVoid)
            {
                statements.Add(Node(GenTreeKind.Eval, pc, op, operands: One(call)));
            }
            else
            {
                GenTemp resultTemp = CreateImporterSpillTemp(method.ReturnType, StackKindOf(method.ReturnType));
                statements.Add(Node(GenTreeKind.StoreTemp, pc, op, operands: One(call), int32: resultTemp.Index));
                result = resultTemp;
            }

            GenTemp continuation = CreateImporterSpillTemp(_rts.ContinuationType, GenStackKind.Ref);
            RuntimeMethod reader = _rts.GetAsyncHelper("AsyncCallContinuation", 0);
            GenTree read = Node(GenTreeKind.Intrinsic, pc, op, type: _rts.ContinuationType, stackKind: GenStackKind.Ref,
                method: reader, intrinsicId: RuntimeIntrinsicId.AsyncCallContinuation);
            GenTree continuationStatement = Node(GenTreeKind.StoreTemp, pc, op, operands: One(read), int32: continuation.Index);
            statements.Add(continuationStatement);

            _lastAsyncContinuation = continuation;
            _asyncCallSites.Add(new AsyncCallSite(continuationStatement, result, continuation, handling, isTailAwait, pc));
            if (result is GenTemp value)
                Push(stack, TempLoad(pc, op, value));
        }

        private void EmitAsyncVersionReturn(List<StackValue> stack, List<GenTree> statements, int pc, ILOpCode op)
        {
            if (_method.AsyncVariantTarget is not RuntimeMethod target)
                throw Fail(pc, op, "An async version has no task returning target.");

            StackValue task = Pop(stack, pc, op);
            _nextAwaitIsTail = true;
            EmitAsyncCall(stack, statements, pc, op, _rts.GetTransparentAwait(target.ReturnType), One(task.Node), ContinuationContextHandling.None);
            if (IsVoid(_method.ReturnType))
                statements.Add(Node(GenTreeKind.Return, pc, op));
            else
                statements.Add(Node(GenTreeKind.Return, pc, op, operands: One(CoerceToStorage(Pop(stack, pc, op).Node, _method.ReturnType, pc))));
        }

        private ImmutableArray<GenTreeBlock> TransformAsync(ImmutableArray<GenTreeBlock> imported, ImmutableArray<ExceptionHandler>.Builder handlers)
        {
            bool saveContexts = _method.RequiresSaveRestoreOfAsyncContexts;
            if (!_method.IsAsyncCall || (_asyncCallSites.Count == 0 && !saveContexts))
                return imported;

            var blocks = new List<GenTreeBlock?>(imported);
            if (blocks.Count != _nextDynamicBlockId)
                throw new GenTreeBuildException($"Block ids of {_method.Name} are not dense before the async transformation.");

            int bodyBlockCount = SplitAtAsyncCalls(blocks);

            AsyncContextTemps? context = saveContexts ? CreateContextTemps() : null;
            int restoreClause = -1;
            if (context is AsyncContextTemps temps)
                restoreClause = WrapInContextRestore(blocks, bodyBlockCount, handlers, temps);

            Dictionary<AsyncCallSite, HashSet<long>> live = ComputeLiveAtRemainders(blocks, bodyBlockCount);
            var states = new List<AsyncCallSite>();
            foreach (AsyncCallSite site in _asyncCallSites)
            {
                if (site.IsTailAwait)
                {
                    BuildTailSuspension(blocks, site);
                    continue;
                }

                site.Region = blocks[site.CallBlock]!.Region;
                BuildSuspensionAndResumption(blocks, site, states.Count, live[site], context, restoreClause);
                states.Add(site);
            }

            BuildEntry(blocks, states, context);
            return FinishAsyncBlocks(blocks, restoreClause);
        }

        private int SplitAtAsyncCalls(List<GenTreeBlock?> blocks)
        {
            var sites = new Dictionary<GenTree, AsyncCallSite>();
            foreach (AsyncCallSite site in _asyncCallSites)
                sites.Add(site.ContinuationStatement, site);

            for (int b = 0; b < blocks.Count; b++)
            {
                if (blocks[b] is not GenTreeBlock block)
                    continue;
                ImmutableArray<GenTree> statements = block.Statements;
                for (int s = 0; s < statements.Length; s++)
                {
                    if (!sites.TryGetValue(statements[s], out AsyncCallSite? site))
                        continue;

                    site.CallBlock = b;
                    site.Suspension = NewAsyncBlock(blocks, out int suspensionPc);
                    site.SuspensionPc = suspensionPc;
                    _layoutSuffix.Add(site.Suspension);
                    site.Remainder = NewAsyncBlock(blocks, out int remainderPc);
                    site.RemainderPc = remainderPc;
                    _syntheticBlockHosts.Add(site.Remainder, (HostOf(b), false));

                    ImmutableArray<GenTree> rest = statements.RemoveRange(0, s + 1);
                    blocks[site.Remainder] = WithExplicitTerminator(new GenTreeBlock(site.Remainder, remainderPc, remainderPc + 1, 0, block.ExitStackDepth,
                        block.JumpKind, block.Flags & ~(GenTreeBlockFlags.Entry | GenTreeBlockFlags.TryEntry | GenTreeBlockFlags.HandlerEntry), rest,
                        block.SuccessorBlockIds, block.SuccessorPcs, block.Region));

                    var head = ImmutableArray.CreateBuilder<GenTree>(s + 3);
                    head.AddRange(statements, s + 1);
                    head.Add(Node(GenTreeKind.BranchTrue, site.Pc, ILOpCode.Brtrue, operands: One(TempLoad(site.Pc, ILOpCode.Ldloc, site.Continuation).Node),
                        targetPc: suspensionPc, targetBlockId: site.Suspension));
                    head.Add(Node(GenTreeKind.Branch, site.Pc, ILOpCode.Br, targetPc: remainderPc, targetBlockId: site.Remainder));
                    blocks[b] = new GenTreeBlock(block.Id, block.StartPc, block.EndPcExclusive, block.EntryStackDepth, 0, GenTreeBlockJumpKind.Conditional,
                        block.Flags, head.MoveToImmutable(), ImmutableArray.Create(site.Suspension, site.Remainder),
                        ImmutableArray.Create(suspensionPc, remainderPc), block.Region);
                    break;
                }
            }
            return blocks.Count;
        }

        private int HostOf(int blockId)
            => _syntheticBlockHosts.TryGetValue(blockId, out var host) ? host.Host : blockId;

        private int NewAsyncBlock(List<GenTreeBlock?> blocks, out int pc)
        {
            pc = _nextSyntheticPc++;
            int id = _nextDynamicBlockId++;
            _pcToBlockId.Add(pc, id);
            blocks.Add(null);
            return id;
        }

        // A split off block lands apart from its predecessor, so it cannot fall through
        private GenTreeBlock WithExplicitTerminator(GenTreeBlock block)
        {
            int fallThrough = -1;
            int fallThroughPc = -1;
            GenTreeBlockJumpKind jumpKind = block.JumpKind;
            if (jumpKind == GenTreeBlockJumpKind.FallThrough && block.SuccessorBlockIds.Length == 1)
            {
                fallThrough = block.SuccessorBlockIds[0];
                fallThroughPc = block.SuccessorPcs[0];
                jumpKind = GenTreeBlockJumpKind.Always;
            }
            else if (jumpKind == GenTreeBlockJumpKind.Conditional && block.Statements.Length != 0 &&
                block.Statements[^1].Kind is GenTreeKind.BranchTrue or GenTreeKind.BranchFalse)
            {
                int taken = block.Statements[^1].TargetBlockId;
                for (int i = 0; i < block.SuccessorBlockIds.Length; i++)
                {
                    if (block.SuccessorBlockIds[i] != taken || block.SuccessorBlockIds.Length == 1)
                    {
                        fallThrough = block.SuccessorBlockIds[i];
                        fallThroughPc = block.SuccessorPcs[i];
                    }
                }
            }

            if (fallThrough < 0)
                return block;

            int pc = block.Statements.Length != 0 ? block.Statements[^1].Pc : block.StartPc;
            ImmutableArray<GenTree> statements = block.Statements.Add(Node(GenTreeKind.Branch, pc, ILOpCode.Br, targetPc: fallThroughPc, targetBlockId: fallThrough));
            return new GenTreeBlock(block.Id, block.StartPc, block.EndPcExclusive, block.EntryStackDepth, block.ExitStackDepth, jumpKind, block.Flags,
                statements, block.SuccessorBlockIds, block.SuccessorPcs, block.Region);
        }

        private sealed class AsyncContextTemps
        {
            public AsyncContextTemps(GenTemp resumed, GenTemp thread, GenTemp executionContext, GenTemp synchronizationContext)
            {
                Resumed = resumed;
                Thread = thread;
                ExecutionContext = executionContext;
                SynchronizationContext = synchronizationContext;
            }

            public GenTemp Resumed { get; }
            public GenTemp Thread { get; }
            public GenTemp ExecutionContext { get; }
            public GenTemp SynchronizationContext { get; }
            public int Capture { get; set; } = -1;

            public bool Contains(int tempIndex)
                => tempIndex == Resumed.Index || tempIndex == Thread.Index || tempIndex == ExecutionContext.Index || tempIndex == SynchronizationContext.Index;
        }

        private AsyncContextTemps CreateContextTemps()
            => new AsyncContextTemps(
                CreateImporterSpillTemp(_rts.FindPrimitive(RuntimePrimitiveKind.Int32), GenStackKind.I4),
                CreateImporterSpillTemp(_rts.GetRequiredNamedType("std", "System.Threading", "Thread"), GenStackKind.Ref),
                CreateImporterSpillTemp(_rts.GetRequiredNamedType("std", "System.Threading", "ExecutionContext"), GenStackKind.Ref),
                CreateImporterSpillTemp(_rts.GetRequiredNamedType("std", "System.Threading", "SynchronizationContext"), GenStackKind.Ref));

        // The body runs under a fault that restores the contexts captured on entry, and every return leaves through a block that restores them
        private int WrapInContextRestore(List<GenTreeBlock?> blocks, int bodyBlockCount, ImmutableArray<ExceptionHandler>.Builder handlers, AsyncContextTemps temps)
        {
            int clause = handlers.Count;
            int fault = NewAsyncBlock(blocks, out int faultPc);
            int exit = NewAsyncBlock(blocks, out int exitPc);
            _layoutSuffix.Add(fault);
            _layoutSuffix.Add(exit);
            handlers.Add(new ExceptionHandler(0, _body.Instructions.Length, faultPc, faultPc + 1, -1));

            GenTemp? returnValue = IsVoid(_method.ReturnType) ? null : CreateImporterSpillTemp(_method.ReturnType, StackKindOf(_method.ReturnType));
            for (int b = 0; b < bodyBlockCount; b++)
            {
                if (blocks[b] is not GenTreeBlock block)
                    continue;
                GenTreeEhRegion region = block.Region.TryIndex < 0 ? new GenTreeEhRegion(clause, block.Region.HandlerIndex) : block.Region;
                ImmutableArray<GenTree> statements = block.Statements;
                GenTreeBlockJumpKind jumpKind = block.JumpKind;
                ImmutableArray<int> successors = block.SuccessorBlockIds;
                ImmutableArray<int> successorPcs = block.SuccessorPcs;
                if (statements.Length != 0 && statements[^1].Kind == GenTreeKind.Return && (statements[^1].Flags & GenTreeFlags.AsyncSuspend) == 0)
                {
                    GenTree ret = statements[^1];
                    ImmutableArray<GenTree>.Builder rewritten = statements.RemoveAt(statements.Length - 1).ToBuilder();
                    if (returnValue is GenTemp value)
                        rewritten.Add(Node(GenTreeKind.StoreTemp, ret.Pc, ILOpCode.Stloc, operands: One(ret.Operands[0]), int32: value.Index));
                    rewritten.Add(Node(GenTreeKind.Branch, ret.Pc, ILOpCode.Br, targetPc: exitPc, targetBlockId: exit));
                    statements = rewritten.ToImmutable();
                    jumpKind = GenTreeBlockJumpKind.Always;
                    successors = ImmutableArray.Create(exit);
                    successorPcs = ImmutableArray.Create(exitPc);
                }
                blocks[b] = new GenTreeBlock(block.Id, block.StartPc, block.EndPcExclusive, block.EntryStackDepth, block.ExitStackDepth, jumpKind,
                    block.Flags, statements, successors, successorPcs, region);
            }

            blocks[fault] = new GenTreeBlock(fault, faultPc, faultPc + 1, 0, 0, GenTreeBlockJumpKind.EndFinally, GenTreeBlockFlags.None,
                ImmutableArray.Create(RestoreContextsCall(faultPc, temps), Node(GenTreeKind.EndFinally, faultPc, ILOpCode.Endfinally)),
                ImmutableArray<int>.Empty, ImmutableArray<int>.Empty, new GenTreeEhRegion(-1, clause));

            GenTree exitReturn = returnValue is GenTemp result
                ? Node(GenTreeKind.Return, exitPc, ILOpCode.Ret, operands: One(TempLoad(exitPc, ILOpCode.Ldloc, result).Node))
                : Node(GenTreeKind.Return, exitPc, ILOpCode.Ret);
            blocks[exit] = new GenTreeBlock(exit, exitPc, exitPc + 1, 0, 0, GenTreeBlockJumpKind.Return, GenTreeBlockFlags.None,
                ImmutableArray.Create(RestoreContextsCall(exitPc, temps), exitReturn), ImmutableArray<int>.Empty, ImmutableArray<int>.Empty,
                GenTreeEhRegion.None);

            temps.Capture = NewAsyncBlock(blocks, out int capturePc);
            _layoutPrefix.Add(temps.Capture);
            RuntimeMethod capture = _rts.GetAsyncHelper("CaptureContexts", 3);
            AddDirectDependency(capture);
            blocks[temps.Capture] = new GenTreeBlock(temps.Capture, capturePc, capturePc + 1, 0, 0, GenTreeBlockJumpKind.Always, GenTreeBlockFlags.None,
                ImmutableArray.Create(
                    Node(GenTreeKind.StoreTemp, capturePc, ILOpCode.Stloc, operands: One(ConstI4(capturePc, ILOpCode.Ldc_I4, 0)), int32: temps.Resumed.Index),
                    Node(GenTreeKind.Eval, capturePc, ILOpCode.Call, operands: One(Node(GenTreeKind.Call, capturePc, ILOpCode.Call,
                        operands: ImmutableArray.Create(
                            TempAddress(capturePc, ILOpCode.Ldloca, temps.Thread).Node,
                            TempAddress(capturePc, ILOpCode.Ldloca, temps.ExecutionContext).Node,
                            TempAddress(capturePc, ILOpCode.Ldloca, temps.SynchronizationContext).Node),
                        int32: 3, method: capture))),
                    Node(GenTreeKind.Branch, capturePc, ILOpCode.Br, targetPc: blocks[0]!.StartPc, targetBlockId: 0)),
                ImmutableArray.Create(0), ImmutableArray.Create(blocks[0]!.StartPc), GenTreeEhRegion.None);
            return clause;
        }

        private GenTree RestoreContextsCall(int pc, AsyncContextTemps temps)
        {
            RuntimeMethod restore = _rts.GetAsyncHelper("RestoreContexts", 4);
            AddDirectDependency(restore);
            return Node(GenTreeKind.Eval, pc, ILOpCode.Call, operands: One(Node(GenTreeKind.Call, pc, ILOpCode.Call,
                operands: ImmutableArray.Create(
                    TempLoad(pc, ILOpCode.Ldloc, temps.Resumed).Node,
                    TempLoad(pc, ILOpCode.Ldloc, temps.Thread).Node,
                    TempLoad(pc, ILOpCode.Ldloc, temps.ExecutionContext).Node,
                    TempLoad(pc, ILOpCode.Ldloc, temps.SynchronizationContext).Node),
                int32: 4, method: restore)));
        }

        private static long SlotKey(GenTreeKind kind, int index)
            => ((long)kind << 32) | (uint)index;

        // Whatever is live at the start of the remainder survives the suspension, an address taken counts as a use
        private Dictionary<AsyncCallSite, HashSet<long>> ComputeLiveAtRemainders(List<GenTreeBlock?> blocks, int bodyBlockCount)
        {
            int count = bodyBlockCount;
            var uses = new HashSet<long>[count];
            var defs = new HashSet<long>[count];
            var liveIn = new HashSet<long>[count];
            var exceptionSuccessors = new List<int>[count];
            for (int b = 0; b < count; b++)
            {
                uses[b] = new HashSet<long>();
                defs[b] = new HashSet<long>();
                liveIn[b] = new HashSet<long>();
                GenTreeBlock? block = blocks[b];
                if (block is null)
                    continue;
                foreach (GenTree statement in block.Statements)
                    CollectUsesAndDefs(statement, uses[b], defs[b]);
                exceptionSuccessors[b] = HandlerEntriesFor(block.Region.TryIndex);
            }

            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int b = count - 1; b >= 0; b--)
                {
                    GenTreeBlock? block = blocks[b];
                    if (block is null)
                        continue;
                    var result = new HashSet<long>();
                    foreach (int successor in block.SuccessorBlockIds)
                    {
                        if (successor < count)
                            result.UnionWith(liveIn[successor]);
                    }
                    result.ExceptWith(defs[b]);
                    result.UnionWith(uses[b]);
                    foreach (int handler in exceptionSuccessors[b])
                        result.UnionWith(liveIn[handler]);
                    if (!result.SetEquals(liveIn[b]))
                    {
                        liveIn[b] = result;
                        changed = true;
                    }
                }
            }

            var live = new Dictionary<AsyncCallSite, HashSet<long>>();
            foreach (AsyncCallSite site in _asyncCallSites)
            {
                if (!site.IsTailAwait)
                    live.Add(site, liveIn[site.Remainder]);
            }
            return live;
        }

        private static void CollectUsesAndDefs(GenTree node, HashSet<long> uses, HashSet<long> defs)
        {
            foreach (GenTree operand in node.Operands)
                CollectUsesAndDefs(operand, uses, defs);

            long key;
            switch (node.Kind)
            {
                case GenTreeKind.Arg:
                case GenTreeKind.ArgAddr:
                    key = SlotKey(GenTreeKind.Arg, node.Int32);
                    break;
                case GenTreeKind.Local:
                case GenTreeKind.LocalAddr:
                    key = SlotKey(GenTreeKind.Local, node.Int32);
                    break;
                case GenTreeKind.Temp:
                case GenTreeKind.TempAddr:
                    key = SlotKey(GenTreeKind.Temp, node.Int32);
                    break;
                case GenTreeKind.StoreArg:
                    defs.Add(SlotKey(GenTreeKind.Arg, node.Int32));
                    return;
                case GenTreeKind.StoreLocal:
                    defs.Add(SlotKey(GenTreeKind.Local, node.Int32));
                    return;
                case GenTreeKind.StoreTemp:
                    defs.Add(SlotKey(GenTreeKind.Temp, node.Int32));
                    return;
                default:
                    return;
            }

            if (!defs.Contains(key))
                uses.Add(key);
        }

        private List<int> HandlerEntriesFor(int tryIndex)
        {
            var entries = new List<int>();
            if (tryIndex < 0)
                return entries;

            ImmutableArray<CilExceptionClause> clauses = _body.ExceptionClauses;
            if (tryIndex >= clauses.Length)
                return entries;

            CilExceptionClause inner = clauses[tryIndex];
            foreach (CilExceptionClause clause in clauses)
            {
                if (clause.TryStartPc <= inner.TryStartPc && inner.TryEndPc <= clause.TryEndPc)
                    entries.Add(BlockIdForPc(clause.HandlerRegionStartPc));
            }
            return entries;
        }

        private readonly struct SavedSlot
        {
            public SavedSlot(GenTreeKind kind, int index, RuntimeType type, int slot)
            {
                Kind = kind;
                Index = index;
                Type = type;
                Slot = slot;
            }

            public GenTreeKind Kind { get; }
            public int Index { get; }
            public RuntimeType Type { get; }
            public int Slot { get; }
        }

        private RuntimeType SlotType(GenTreeKind kind, int index, int pc)
        {
            switch (kind)
            {
                case GenTreeKind.Arg:
                    return _argTypes[index];
                case GenTreeKind.Local:
                    return _localTypes[index];
                default:
                    foreach (GenTemp temp in _temps)
                    {
                        if (temp.Index != index)
                            continue;
                        if (temp.Type is RuntimeType type)
                            return type;
                        return temp.StackKind switch
                        {
                            GenStackKind.I4 => _rts.FindPrimitive(RuntimePrimitiveKind.Int32),
                            GenStackKind.I8 => _rts.FindPrimitive(RuntimePrimitiveKind.Int64),
                            GenStackKind.R4 => _rts.FindPrimitive(RuntimePrimitiveKind.Single),
                            GenStackKind.R8 => _rts.FindPrimitive(RuntimePrimitiveKind.Double),
                            GenStackKind.NativeInt or GenStackKind.Ptr => _rts.FindPrimitive(RuntimePrimitiveKind.NativeInt),
                            GenStackKind.NativeUInt => _rts.FindPrimitive(RuntimePrimitiveKind.NativeUInt),
                            GenStackKind.Ref or GenStackKind.Null => _rts.SystemObject,
                            _ => throw Fail(pc, ILOpCode.Nop, $"Temp {index} of kind {temp.StackKind} cannot live across an await."),
                        };
                    }
                    throw Fail(pc, ILOpCode.Nop, $"Temp {index} is unknown.");
            }
        }

        private int SlotCount(RuntimeType type)
        {
            int pointerSize = _rts.Target.PointerSize;
            if (!type.IsValueType)
                return 1;
            _rts.EnsureRuntimeTypeReady(type);
            return Math.Max(1, (type.SizeOf + pointerSize - 1) / pointerSize);
        }

        private void MarkObjectSlots(List<bool> objectSlots, int slot, RuntimeType type, int pc)
        {
            while (objectSlots.Count < slot + SlotCount(type))
                objectSlots.Add(false);
            if (type.Kind is RuntimeTypeKind.ByRef || type.IsByRefLike)
                throw Fail(pc, ILOpCode.Nop, $"A value of type {type.Name} cannot live across an await.");
            if (!type.IsValueType)
            {
                objectSlots[slot] = type.Kind is not (RuntimeTypeKind.Pointer or RuntimeTypeKind.FunctionPointer);
                return;
            }
            foreach (int offset in type.GcPointerOffsets)
                objectSlots[slot + offset / _rts.Target.PointerSize] = true;
        }

        // A tail await hands the callee's continuation on, this frame never resumes
        private void BuildTailSuspension(List<GenTreeBlock?> blocks, AsyncCallSite site)
        {
            int pc = site.SuspensionPc;
            blocks[site.Suspension] = new GenTreeBlock(site.Suspension, pc, pc + 1, 0, 0, GenTreeBlockJumpKind.Return, GenTreeBlockFlags.None,
                ImmutableArray.Create(SuspendReturn(pc, TempLoad(pc, ILOpCode.Ldloc, site.Continuation).Node)),
                ImmutableArray<int>.Empty, ImmutableArray<int>.Empty, GenTreeEhRegion.None);
        }

        private GenTree SuspendReturn(int pc, GenTree continuation)
        {
            GenTree suspend = Node(GenTreeKind.Return, pc, ILOpCode.Ret, operands: One(continuation));
            suspend.Flags |= GenTreeFlags.AsyncSuspend;
            return suspend;
        }

        private void BuildSuspensionAndResumption(List<GenTreeBlock?> blocks, AsyncCallSite site, int state, HashSet<long> live,
            AsyncContextTemps? context, int restoreClause)
        {
            int suspensionPc = site.SuspensionPc;
            bool needsException = site.Region.TryIndex >= 0 && site.Region.TryIndex != restoreClause;
            bool needsContinuationContext = site.ContextHandling == ContinuationContextHandling.ContinueOnCapturedContext;

            var objectSlots = new List<bool>();
            int nextSlot = 0;
            int executionContextSlot = nextSlot++;
            objectSlots.Add(true);
            int continuationContextSlot = -1;
            if (needsContinuationContext)
            {
                continuationContextSlot = nextSlot++;
                objectSlots.Add(true);
            }
            int exceptionSlot = -1;
            if (needsException)
            {
                exceptionSlot = nextSlot++;
                objectSlots.Add(true);
            }

            int resultSlot = -1;
            RuntimeType? resultType = null;
            if (site.Result is GenTemp resultTemp)
            {
                resultType = SlotType(GenTreeKind.Temp, resultTemp.Index, site.Pc);
                resultSlot = nextSlot;
                MarkObjectSlots(objectSlots, resultSlot, resultType, site.Pc);
                nextSlot += SlotCount(resultType);
            }

            var candidates = new List<(GenTreeKind Kind, int Index, RuntimeType Type)>();
            foreach (long key in live)
            {
                var kind = (GenTreeKind)(key >> 32);
                int index = (int)(uint)key;
                if (kind == GenTreeKind.Temp &&
                    (index == site.Continuation.Index || (site.Result is GenTemp r && r.Index == index) || (context is not null && context.Contains(index))))
                {
                    continue;
                }
                candidates.Add((kind, index, SlotType(kind, index, site.Pc)));
            }
            candidates.Sort(static (a, b) =>
            {
                int byRef = (a.Type.IsValueType ? 1 : 0).CompareTo(b.Type.IsValueType ? 1 : 0);
                if (byRef != 0)
                    return byRef;
                int byKind = a.Kind.CompareTo(b.Kind);
                return byKind != 0 ? byKind : a.Index.CompareTo(b.Index);
            });

            var saved = new List<SavedSlot>(candidates.Count);
            foreach (var candidate in candidates)
            {
                MarkObjectSlots(objectSlots, nextSlot, candidate.Type, site.Pc);
                saved.Add(new SavedSlot(candidate.Kind, candidate.Index, candidate.Type, nextSlot));
                nextSlot += SlotCount(candidate.Type);
            }

            RuntimeType continuationType = _rts.GetContinuationType(objectSlots.ToArray());
            MarkInstantiatedType(continuationType);
            RuntimeField SlotField(int slot) => continuationType.InstanceFields[slot];

            int flags = (executionContextSlot + 1) << 4;
            if (continuationContextSlot >= 0)
                flags |= (continuationContextSlot + 1) << 6;
            if (exceptionSlot >= 0)
                flags |= (exceptionSlot + 1) << 8;
            if (resultSlot >= 0)
                flags |= (resultSlot + 1) << 11;
            if (site.ContextHandling == ContinuationContextHandling.ContinueOnThreadPool)
                flags |= 1;

            // Suspension: link a new continuation after the callee's, fill it and hand it to the caller
            int pc = suspensionPc;
            GenTemp newContinuation = CreateImporterSpillTemp(continuationType, GenStackKind.Ref);
            GenTree NewContinuation() => TempLoad(pc, ILOpCode.Ldloc, newContinuation).Node;
            RuntimeMethod accessor = _rts.GetAsyncResumeInfoAccessor(_method);
            AddDirectDependency(accessor);
            var suspension = ImmutableArray.CreateBuilder<GenTree>();
            suspension.Add(Node(GenTreeKind.StoreTemp, pc, ILOpCode.Newobj, operands: One(Node(GenTreeKind.AllocObject, pc, ILOpCode.Newobj,
                type: continuationType, stackKind: GenStackKind.Ref, runtimeType: continuationType)), int32: newContinuation.Index));
            suspension.Add(StoreContinuationField(pc, TempLoad(pc, ILOpCode.Ldloc, site.Continuation).Node, _rts.GetContinuationField("Next"), NewContinuation()));
            suspension.Add(StoreContinuationField(pc, NewContinuation(), _rts.GetContinuationField("ResumeInfo"),
                Node(GenTreeKind.Call, pc, ILOpCode.Call, type: accessor.ReturnType, stackKind: StackKindOf(accessor.ReturnType), int32: 0, method: accessor)));
            suspension.Add(StoreContinuationField(pc, NewContinuation(), _rts.GetContinuationField("State"), ConstI4(pc, ILOpCode.Ldc_I4, state)));
            suspension.Add(StoreContinuationField(pc, NewContinuation(), _rts.GetContinuationField("Flags"), ConstI4(pc, ILOpCode.Ldc_I4, flags)));
            foreach (SavedSlot slot in saved)
            {
                suspension.Add(StoreToSlot(pc, NewContinuation(), SlotField(slot.Slot), slot.Type, LoadSlot(pc, slot)));
            }

            GenTree SlotAddress(GenTree receiver, int slot)
                => Node(GenTreeKind.FieldAddr, pc, ILOpCode.Ldflda, type: _rts.GetByRefType(SlotField(slot).FieldType), stackKind: GenStackKind.ByRef,
                    operands: One(receiver), field: SlotField(slot));
            GenTree FlagsAddress()
            {
                RuntimeField field = _rts.GetContinuationField("Flags");
                return Node(GenTreeKind.FieldAddr, pc, ILOpCode.Ldflda, type: _rts.GetByRefType(field.FieldType), stackKind: GenStackKind.ByRef,
                    operands: One(NewContinuation()), field: field);
            }

            if (context is AsyncContextTemps temps)
            {
                RuntimeMethod finish = needsContinuationContext
                    ? _rts.GetAsyncHelper("FinishSuspensionWithContinuationContext", 6)
                    : _rts.GetAsyncHelper("FinishSuspensionNoContinuationContext", 4);
                var arguments = ImmutableArray.CreateBuilder<GenTree>(6);
                if (needsContinuationContext)
                {
                    arguments.Add(SlotAddress(NewContinuation(), continuationContextSlot));
                    arguments.Add(FlagsAddress());
                }
                arguments.Add(SlotAddress(NewContinuation(), executionContextSlot));
                arguments.Add(TempLoad(pc, ILOpCode.Ldloc, temps.Resumed).Node);
                arguments.Add(TempLoad(pc, ILOpCode.Ldloc, temps.ExecutionContext).Node);
                arguments.Add(TempLoad(pc, ILOpCode.Ldloc, temps.SynchronizationContext).Node);
                suspension.Add(CallStatement(pc, finish, arguments.ToImmutable()));
            }
            else
            {
                if (needsContinuationContext)
                    suspension.Add(CallStatement(pc, _rts.GetAsyncHelper("CaptureContinuationContext", 2), ImmutableArray.Create(SlotAddress(NewContinuation(), continuationContextSlot), FlagsAddress())));
                RuntimeMethod captureExecution = _rts.GetAsyncHelper("CaptureExecutionContext", 0);
                AddDirectDependency(captureExecution);
                suspension.Add(StoreToSlot(pc, NewContinuation(), SlotField(executionContextSlot), _rts.SystemObject,
                    Node(GenTreeKind.Call, pc, ILOpCode.Call, type: captureExecution.ReturnType, stackKind: GenStackKind.Ref, int32: 0, method: captureExecution)));
            }
            suspension.Add(SuspendReturn(pc, NewContinuation()));
            blocks[site.Suspension] = new GenTreeBlock(site.Suspension, pc, pc + 1, 0, 0, GenTreeBlockJumpKind.Return, GenTreeBlockFlags.None,
                suspension.ToImmutable(), ImmutableArray<int>.Empty, ImmutableArray<int>.Empty, GenTreeEhRegion.None);

            site.Resumption = NewAsyncBlock(blocks, out int resumptionPc);
            site.ResumptionPc = resumptionPc;
            _layoutSuffix.Add(site.Resumption);
            pc = resumptionPc;
            int continuationArg = _argTypes.Length - 1;
            GenTree CastContinuation() => Node(GenTreeKind.Arg, pc, ILOpCode.Ldarg, type: continuationType, stackKind: GenStackKind.Ref, int32: continuationArg);
            var resumption = ImmutableArray.CreateBuilder<GenTree>();
            foreach (SavedSlot slot in saved)
                resumption.Add(StoreSlot(pc, slot, LoadFromSlot(pc, CastContinuation(), SlotField(slot.Slot), slot.Type)));
            if (context is AsyncContextTemps contextTemps)
            {
                resumption.Add(Node(GenTreeKind.StoreTemp, pc, ILOpCode.Stloc, operands: One(ConstI4(pc, ILOpCode.Ldc_I4, 1)), int32: contextTemps.Resumed.Index));
                foreach (GenTemp temp in new[] { contextTemps.Thread, contextTemps.ExecutionContext, contextTemps.SynchronizationContext })
                    resumption.Add(Node(GenTreeKind.StoreTemp, pc, ILOpCode.Stloc, operands: One(Node(GenTreeKind.ConstNull, pc, ILOpCode.Ldnull, stackKind: GenStackKind.Null)), int32: temp.Index));
            }
            if (site.Result is GenTemp result && resultType is not null)
                resumption.Add(Node(GenTreeKind.StoreTemp, pc, ILOpCode.Stloc, operands: One(LoadFromSlot(pc, CastContinuation(), SlotField(resultSlot), resultType)), int32: result.Index));

            int remainderPc = site.RemainderPc;
            if (needsException)
            {
                RuntimeType exceptionType = _rts.GetRequiredNamedType("std", "System", "Exception");
                GenTemp exception = CreateImporterSpillTemp(exceptionType, GenStackKind.Ref);
                resumption.Add(Node(GenTreeKind.StoreTemp, pc, ILOpCode.Stloc, operands: One(LoadFromSlot(pc, CastContinuation(), SlotField(exceptionSlot), exceptionType)), int32: exception.Index));
                resumption.Add(StoreToSlot(pc, CastContinuation(), SlotField(exceptionSlot), _rts.SystemObject, Node(GenTreeKind.ConstNull, pc, ILOpCode.Ldnull, stackKind: GenStackKind.Null)));

                int rethrow = NewAsyncBlock(blocks, out int rethrowPc);
                _syntheticBlockHosts.Add(rethrow, (HostOf(site.CallBlock), false));
                blocks[rethrow] = new GenTreeBlock(rethrow, rethrowPc, rethrowPc + 1, 0, 0, GenTreeBlockJumpKind.Throw, GenTreeBlockFlags.None,
                    ImmutableArray.Create(Node(GenTreeKind.Throw, site.Pc, ILOpCode.Throw, operands: One(TempLoad(site.Pc, ILOpCode.Ldloc, exception).Node))),
                    ImmutableArray<int>.Empty, ImmutableArray<int>.Empty, site.Region);

                resumption.Add(Node(GenTreeKind.BranchTrue, pc, ILOpCode.Brtrue, operands: One(TempLoad(pc, ILOpCode.Ldloc, exception).Node),
                    targetPc: rethrowPc, targetBlockId: rethrow));
                resumption.Add(Node(GenTreeKind.Branch, pc, ILOpCode.Br, targetPc: remainderPc, targetBlockId: site.Remainder));
                blocks[site.Resumption] = new GenTreeBlock(site.Resumption, pc, pc + 1, 0, 0, GenTreeBlockJumpKind.Conditional, GenTreeBlockFlags.None,
                    resumption.ToImmutable(), ImmutableArray.Create(rethrow, site.Remainder), ImmutableArray.Create(rethrowPc, remainderPc), GenTreeEhRegion.None);
            }
            else
            {
                resumption.Add(Node(GenTreeKind.Branch, pc, ILOpCode.Br, targetPc: remainderPc, targetBlockId: site.Remainder));
                blocks[site.Resumption] = new GenTreeBlock(site.Resumption, pc, pc + 1, 0, 0, GenTreeBlockJumpKind.Always, GenTreeBlockFlags.None,
                    resumption.ToImmutable(), ImmutableArray.Create(site.Remainder), ImmutableArray.Create(remainderPc), GenTreeEhRegion.None);
            }
        }

        private GenTree CallStatement(int pc, RuntimeMethod method, ImmutableArray<GenTree> arguments)
        {
            AddDirectDependency(method);
            return Node(GenTreeKind.Eval, pc, ILOpCode.Call, operands: One(Node(GenTreeKind.Call, pc, ILOpCode.Call,
                operands: arguments, int32: arguments.Length, method: method)));
        }

        private GenTree StoreContinuationField(int pc, GenTree receiver, RuntimeField field, GenTree value)
            => Node(GenTreeKind.StoreField, pc, ILOpCode.Stfld, operands: Two(receiver, CoerceToStorage(value, field.FieldType, pc)), field: field);

        private GenTree StoreToSlot(int pc, GenTree continuation, RuntimeField slot, RuntimeType type, GenTree value)
        {
            GenTree address = Node(GenTreeKind.FieldAddr, pc, ILOpCode.Ldflda, type: _rts.GetByRefType(slot.FieldType), stackKind: GenStackKind.ByRef,
                operands: One(continuation), field: slot);
            GenTree store = Node(GenTreeKind.StoreIndirect, pc, ILOpCode.Stobj, operands: Two(address, CoerceToStorage(value, type, pc)), runtimeType: type);
            if (type.IsValueType && type.AlignOf > _rts.Target.PointerSize)
                store.Flags |= GenTreeFlags.Unaligned;
            return store;
        }

        private GenTree LoadFromSlot(int pc, GenTree continuation, RuntimeField slot, RuntimeType type)
        {
            GenTree address = Node(GenTreeKind.FieldAddr, pc, ILOpCode.Ldflda, type: _rts.GetByRefType(slot.FieldType), stackKind: GenStackKind.ByRef,
                operands: One(continuation), field: slot);
            GenTree load = Node(GenTreeKind.LoadIndirect, pc, ILOpCode.Ldobj, type: type, stackKind: StackKindOf(type), operands: One(address), runtimeType: type);
            if (type.IsValueType && type.AlignOf > _rts.Target.PointerSize)
                load.Flags |= GenTreeFlags.Unaligned;
            return load;
        }

        private GenTree LoadSlot(int pc, SavedSlot slot)
            => slot.Kind switch
            {
                GenTreeKind.Arg => Node(GenTreeKind.Arg, pc, ILOpCode.Ldarg, type: slot.Type, stackKind: StackKindOf(slot.Type), int32: slot.Index),
                GenTreeKind.Local => Node(GenTreeKind.Local, pc, ILOpCode.Ldloc, type: slot.Type, stackKind: StackKindOf(slot.Type), int32: slot.Index),
                _ => Node(GenTreeKind.Temp, pc, ILOpCode.Ldloc, type: slot.Type, stackKind: StackKindOf(slot.Type), int32: slot.Index),
            };

        private GenTree StoreSlot(int pc, SavedSlot slot, GenTree value)
            => slot.Kind switch
            {
                GenTreeKind.Arg => Node(GenTreeKind.StoreArg, pc, ILOpCode.Starg, operands: One(value), int32: slot.Index),
                GenTreeKind.Local => Node(GenTreeKind.StoreLocal, pc, ILOpCode.Stloc, operands: One(value), int32: slot.Index),
                _ => Node(GenTreeKind.StoreTemp, pc, ILOpCode.Stloc, operands: One(value), int32: slot.Index),
            };

        // A non-null continuation argument resumes at the state it records, otherwise the method starts afresh
        private void BuildEntry(List<GenTreeBlock?> blocks, List<AsyncCallSite> states, AsyncContextTemps? context)
        {
            if (states.Count == 0)
                return;

            int start = context?.Capture ?? 0;
            int startPc = blocks[start]!.StartPc;
            int continuationArg = _argTypes.Length - 1;
            RuntimeField stateField = _rts.GetContinuationField("State");
            GenTree ContinuationArg(int pc)
                => Node(GenTreeKind.Arg, pc, ILOpCode.Ldarg, type: _argTypes[continuationArg], stackKind: GenStackKind.Ref, int32: continuationArg);

            int entry = NewAsyncBlock(blocks, out int entryPc);
            _layoutPrefix.Insert(0, entry);

            int next = states[^1].Resumption;
            int nextPc = states[^1].ResumptionPc;
            for (int i = states.Count - 2; i >= 0; i--)
            {
                int test = NewAsyncBlock(blocks, out int testPc);
                _layoutPrefix.Insert(1, test);
                GenTree state = Node(GenTreeKind.Field, testPc, ILOpCode.Ldfld, type: stateField.FieldType, stackKind: GenStackKind.I4,
                    operands: One(ContinuationArg(testPc)), field: stateField);
                GenTree matches = Node(GenTreeKind.Binary, testPc, ILOpCode.Ceq, stackKind: GenStackKind.I4,
                    operands: Two(state, ConstI4(testPc, ILOpCode.Ldc_I4, i)));
                blocks[test] = new GenTreeBlock(test, testPc, testPc + 1, 0, 0, GenTreeBlockJumpKind.Conditional, GenTreeBlockFlags.None,
                    ImmutableArray.Create(
                        Node(GenTreeKind.BranchTrue, testPc, ILOpCode.Brtrue, operands: One(matches), targetPc: states[i].ResumptionPc, targetBlockId: states[i].Resumption),
                        Node(GenTreeKind.Branch, testPc, ILOpCode.Br, targetPc: nextPc, targetBlockId: next)),
                    ImmutableArray.Create(states[i].Resumption, next), ImmutableArray.Create(states[i].ResumptionPc, nextPc), GenTreeEhRegion.None);
                next = test;
                nextPc = testPc;
            }

            blocks[entry] = new GenTreeBlock(entry, entryPc, entryPc + 1, 0, 0, GenTreeBlockJumpKind.Conditional, GenTreeBlockFlags.Entry,
                ImmutableArray.Create(
                    Node(GenTreeKind.BranchTrue, entryPc, ILOpCode.Brtrue, operands: One(ContinuationArg(entryPc)), targetPc: nextPc, targetBlockId: next),
                    Node(GenTreeKind.Branch, entryPc, ILOpCode.Br, targetPc: startPc, targetBlockId: start)),
                ImmutableArray.Create(next, start), ImmutableArray.Create(nextPc, startPc), GenTreeEhRegion.None);
        }

        private ImmutableArray<GenTreeBlock> FinishAsyncBlocks(List<GenTreeBlock?> blocks, int restoreClause)
        {
            int entry = _layoutPrefix.Count != 0 ? _layoutPrefix[0] : 0;
            var result = ImmutableArray.CreateBuilder<GenTreeBlock>(blocks.Count);
            for (int b = 0; b < blocks.Count; b++)
            {
                GenTreeBlock block = blocks[b] ?? throw new GenTreeBuildException($"Async block {b} of {_method.Name} was never built.");
                GenTreeBlockFlags flags = block.Flags & ~(GenTreeBlockFlags.Entry | GenTreeBlockFlags.InTryRegion | GenTreeBlockFlags.InHandlerRegion);
                if (b == entry)
                    flags |= GenTreeBlockFlags.Entry;
                if (block.Region.TryIndex >= 0)
                    flags |= GenTreeBlockFlags.InTryRegion;
                if (block.Region.HandlerIndex >= 0)
                    flags |= GenTreeBlockFlags.InHandlerRegion;
                if (restoreClause >= 0 && b == 0)
                    flags |= GenTreeBlockFlags.TryEntry;
                if (restoreClause >= 0 && block.Region.HandlerIndex == restoreClause)
                    flags |= GenTreeBlockFlags.HandlerEntry;
                result.Add(flags == block.Flags ? block : new GenTreeBlock(block.Id, block.StartPc, block.EndPcExclusive, block.EntryStackDepth,
                    block.ExitStackDepth, block.JumpKind, flags, block.Statements, block.SuccessorBlockIds, block.SuccessorPcs, block.Region));
            }
            return result.MoveToImmutable();
        }
    }
}
