using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal sealed class CodeGeneratorOptions
    {
        public static CodeGeneratorOptions Default { get; } = new CodeGeneratorOptions();

        public bool EmitExceptionRegions { get; set; } = true;
        public bool EmitGcInfo { get; set; } = true;
        public bool EmitUnwindInfo { get; set; } = true;
        public bool VerifyImage { get; set; } = true;
    }


    internal sealed class BackendOptions
    {
        public static BackendOptions Default { get; } = new BackendOptions();

        public bool IncludeExceptionEdges { get; set; } = true;
        public bool SplitCriticalEdgesBeforeSsa { get; set; } = true;
        public bool BuildSsa { get; set; } = true;
        public bool OptimizeSsa { get; set; } = true;
        public bool ValidateHir { get; set; } = true;
        public bool ValidateSsa { get; set; } = true;

        public SsaOptimizationOptions SsaOptimizationOptions { get; set; } = SsaOptimizationOptions.DefaultWithoutValidation;
        public LinearRationalizationOptions RationalizationOptions { get; set; } = LinearRationalizationOptions.Default;
        public RegisterAllocatorOptions RegisterAllocatorOptions { get; set; } = RegisterAllocatorOptions.Default;
        public CodeGeneratorOptions CodeGeneratorOptions { get; set; } = CodeGeneratorOptions.Default;
    }

    internal sealed class BackendResult
    {
        public GenTreeProgram HirProgram { get; }
        public SsaProgram? SsaProgram { get; }
        public GenTreeProgram RationalizedProgram { get; }
        public GenTreeProgram LoweredProgram { get; }
        public GenTreeProgram RegisterAllocatedProgram { get; }
        public CodeImage Image { get; }

        public BackendResult(
            GenTreeProgram hirProgram,
            SsaProgram? ssaProgram,
            GenTreeProgram rationalizedProgram,
            GenTreeProgram loweredProgram,
            GenTreeProgram registerAllocatedProgram,
            CodeImage image)
        {
            HirProgram = hirProgram ?? throw new ArgumentNullException(nameof(hirProgram));
            SsaProgram = ssaProgram;
            RationalizedProgram = rationalizedProgram ?? throw new ArgumentNullException(nameof(rationalizedProgram));
            LoweredProgram = loweredProgram ?? throw new ArgumentNullException(nameof(loweredProgram));
            RegisterAllocatedProgram = registerAllocatedProgram ?? throw new ArgumentNullException(nameof(registerAllocatedProgram));
            Image = image ?? throw new ArgumentNullException(nameof(image));
        }
    }

    internal sealed class GenTreeBackendPipelineResult
    {
        public GenTreeProgram HirProgram { get; }
        public SsaProgram? SsaProgram { get; }
        public GenTreeProgram RationalizedProgram { get; }
        public GenTreeProgram LoweredProgram { get; }
        public GenTreeProgram RegisterAllocatedProgram { get; }

        public GenTreeBackendPipelineResult(
            GenTreeProgram hirProgram,
            SsaProgram? ssaProgram,
            GenTreeProgram rationalizedProgram,
            GenTreeProgram loweredProgram,
            GenTreeProgram registerAllocatedProgram)
        {
            HirProgram = hirProgram ?? throw new ArgumentNullException(nameof(hirProgram));
            SsaProgram = ssaProgram;
            RationalizedProgram = rationalizedProgram ?? throw new ArgumentNullException(nameof(rationalizedProgram));
            LoweredProgram = loweredProgram ?? throw new ArgumentNullException(nameof(loweredProgram));
            RegisterAllocatedProgram = registerAllocatedProgram ?? throw new ArgumentNullException(nameof(registerAllocatedProgram));
        }
    }

    internal static class BackendPipeline
    {
        public static BackendResult CompileProgram(GenTreeProgram program, BackendOptions? options = null)
        {
            if (program is null)
                throw new ArgumentNullException(nameof(program));

            options ??= BackendOptions.Default;
            var lowered = GenTreeBackendPipeline.RunProgram(program, options);
            var image = CodeGenerator.Build(lowered.RegisterAllocatedProgram, options.CodeGeneratorOptions);
            return new BackendResult(
                lowered.HirProgram,
                lowered.SsaProgram,
                lowered.RationalizedProgram,
                lowered.LoweredProgram,
                lowered.RegisterAllocatedProgram,
                image);
        }

        public static BackendResult CompileMethod(GenTreeMethod method, BackendOptions? options = null)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            options ??= BackendOptions.Default;
            var lowered = GenTreeBackendPipeline.RunMethod(method, options);
            var image = CodeGenerator.Build(lowered.RegisterAllocatedProgram, options.CodeGeneratorOptions);
            return new BackendResult(
                lowered.HirProgram,
                lowered.SsaProgram,
                lowered.RationalizedProgram,
                lowered.LoweredProgram,
                lowered.RegisterAllocatedProgram,
                image);
        }
    }

    internal static class GenTreeBackendPipeline
    {
        public static GenTreeBackendPipelineResult RunProgram(GenTreeProgram program, BackendOptions options)
        {
            if (program is null)
                throw new ArgumentNullException(nameof(program));
            if (options is null)
                throw new ArgumentNullException(nameof(options));

            var hirMethods = ImmutableArray.CreateBuilder<GenTreeMethod>(program.Methods.Length);
            var ssaMethods = options.BuildSsa ? ImmutableArray.CreateBuilder<SsaMethod>(program.Methods.Length) : null;
            var rationalizedMethods = ImmutableArray.CreateBuilder<GenTreeMethod>(program.Methods.Length);
            var loweredMethods = ImmutableArray.CreateBuilder<GenTreeMethod>(program.Methods.Length);
            var allocatedMethods = ImmutableArray.CreateBuilder<GenTreeMethod>(program.Methods.Length);

            for (int i = 0; i < program.Methods.Length; i++)
            {
                var method = CompileMethodThroughLsra(program.Methods[i], options, out var hir, out var ssa, out var rationalized, out var lowered);
                hirMethods.Add(hir);
                if (ssa is not null)
                    ssaMethods!.Add(ssa);
                rationalizedMethods.Add(rationalized);
                loweredMethods.Add(lowered);
                allocatedMethods.Add(method);
            }

            return new GenTreeBackendPipelineResult(
                new GenTreeProgram(hirMethods.ToImmutable()),
                ssaMethods is null ? null : new SsaProgram(ssaMethods.ToImmutable()),
                new GenTreeProgram(rationalizedMethods.ToImmutable()),
                new GenTreeProgram(loweredMethods.ToImmutable()),
                new GenTreeProgram(allocatedMethods.ToImmutable()));
        }

        public static GenTreeBackendPipelineResult RunMethod(GenTreeMethod method, BackendOptions options)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (options is null)
                throw new ArgumentNullException(nameof(options));

            var allocated = CompileMethodThroughLsra(method, options, out var hir, out var ssa, out var rationalized, out var lowered);
            return new GenTreeBackendPipelineResult(
                new GenTreeProgram(ImmutableArray.Create(hir)),
                ssa is null ? null : new SsaProgram(ImmutableArray.Create(ssa)),
                new GenTreeProgram(ImmutableArray.Create(rationalized)),
                new GenTreeProgram(ImmutableArray.Create(lowered)),
                new GenTreeProgram(ImmutableArray.Create(allocated)));
        }

        private static GenTreeMethod CompileMethodThroughLsra(
            GenTreeMethod importedMethod,
            BackendOptions options,
            out GenTreeMethod hirMethod,
            out SsaMethod? ssaMethod,
            out GenTreeMethod rationalizedMethod,
            out GenTreeMethod loweredMethod)
        {
            hirMethod = PrepareHir(importedMethod, options);
            ssaMethod = null;

            if (options.BuildSsa)
            {
                ssaMethod = GenTreeSsaBuilder.BuildMethod(
                    hirMethod,
                    hirMethod.Cfg,
                    hirMethod.HirLiveness,
                    validate: options.ValidateSsa);
                ssaMethod = SsaValueNumbering.BuildMethod(ssaMethod);
                hirMethod = ssaMethod.GenTreeMethod;
                hirMethod.AttachSsa(ssaMethod, optimized: false);

                if (options.OptimizeSsa)
                {
                    ssaMethod = SsaOptimizer.OptimizeMethod(ssaMethod, options.SsaOptimizationOptions);
                    ssaMethod = SsaValueNumbering.BuildMethod(ssaMethod);
                    hirMethod.AttachSsa(ssaMethod, optimized: true);
                }

                if (options.ValidateSsa)
                    SsaVerifier.Verify(ssaMethod);
            }

            var lirOptions = CreateLirOptions(options);
            rationalizedMethod = GenTreeLinearIrRationalizer.RationalizeMethod(hirMethod, ssaMethod, lirOptions);
            loweredMethod = GenTreeLinearLowerer.LowerMethod(rationalizedMethod, lirOptions);
            return LinearScanRegisterAllocator.AllocateMethod(loweredMethod, options.RegisterAllocatorOptions);
        }

        private static GenTreeMethod PrepareHir(GenTreeMethod method, BackendOptions options)
        {
            method = GenTreeMorpher.MorphMethod(method);
            method = GenTreeLocalRewriter.RewriteMethod(method);

            if (options.SplitCriticalEdgesBeforeSsa)
            {
                var split = GenTreeCriticalEdgeSplitter.SplitCriticalEdges(method);
                if (!ReferenceEquals(split, method))
                {
                    method = GenTreeMorpher.MorphMethod(split);
                    method = GenTreeLocalRewriter.RewriteMethod(method);
                }
            }

            var cfg = ControlFlowGraph.Build(method, options.IncludeExceptionEdges);
            method.AttachFlowGraph(cfg);

            var liveness = GenTreeLocalLiveness.Build(method, cfg);
            method.AttachHirLiveness(liveness);

            if (options.ValidateHir)
                GenTreeHirVerifier.Verify(method, cfg, liveness);

            return method;
        }

        private static LinearRationalizationOptions CreateLirOptions(BackendOptions options)
        {
            return new LinearRationalizationOptions
            {
                IncludeExceptionEdges = options.IncludeExceptionEdges,
                Validate = options.RationalizationOptions.Validate,
            };
        }
    }

    internal static class GenTreeMorpher
    {
        public static GenTreeMethod MorphMethod(GenTreeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var statements = method.Blocks[b].Statements;
                for (int s = 0; s < statements.Length; s++)
                    NormalizeFlags(statements[s]);
            }

            method.SetPhase(GenTreeMethodPhase.MorphedHir);
            return method;
        }

        private static GenTreeFlags NormalizeFlags(GenTree node)
        {
            var flags = node.Flags;
            for (int i = 0; i < node.Operands.Length; i++)
            {
                var childFlags = NormalizeFlags(node.Operands[i]);
                if ((childFlags & GenTreeFlags.ContainsCall) != 0)
                    flags |= GenTreeFlags.ContainsCall;
                if ((childFlags & GenTreeFlags.CanThrow) != 0)
                    flags |= GenTreeFlags.CanThrow;
                if ((childFlags & GenTreeFlags.SideEffect) != 0)
                    flags |= GenTreeFlags.SideEffect;
                if ((childFlags & GenTreeFlags.MemoryRead) != 0)
                    flags |= GenTreeFlags.MemoryRead;
                if ((childFlags & GenTreeFlags.MemoryWrite) != 0)
                    flags |= GenTreeFlags.MemoryWrite;
            }

            switch (node.Kind)
            {
                case GenTreeKind.Call:
                case GenTreeKind.VirtualCall:
                case GenTreeKind.NewObject:
                    flags |= GenTreeFlags.ContainsCall | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.NewArray:
                case GenTreeKind.Box:
                    flags |= GenTreeFlags.Allocation | GenTreeFlags.ContainsCall | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.Field:
                case GenTreeKind.StaticField:
                case GenTreeKind.LoadIndirect:
                case GenTreeKind.ArrayElement:
                    flags |= GenTreeFlags.MemoryRead;
                    break;

                case GenTreeKind.StoreLocal:
                case GenTreeKind.StoreArg:
                case GenTreeKind.StoreTemp:
                    flags |= GenTreeFlags.LocalDef | GenTreeFlags.SideEffect;
                    break;

                case GenTreeKind.Local:
                case GenTreeKind.Arg:
                case GenTreeKind.Temp:
                    flags |= GenTreeFlags.LocalUse;
                    break;

                case GenTreeKind.StoreField:
                case GenTreeKind.StoreStaticField:
                case GenTreeKind.StoreIndirect:
                case GenTreeKind.StoreArrayElement:
                    flags |= GenTreeFlags.MemoryWrite | GenTreeFlags.SideEffect;
                    break;

                case GenTreeKind.Branch:
                case GenTreeKind.BranchTrue:
                case GenTreeKind.BranchFalse:
                case GenTreeKind.Return:
                case GenTreeKind.Throw:
                case GenTreeKind.Rethrow:
                case GenTreeKind.EndFinally:
                    flags |= GenTreeFlags.ControlFlow;
                    break;
            }

            node.Flags = flags;
            return flags;
        }
    }

    internal static class GenTreeLocalRewriter
    {
        public static GenTreeMethod RewriteMethod(GenTreeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            ResetDescriptors(method.ArgDescriptors);
            ResetDescriptors(method.LocalDescriptors);
            ResetDescriptors(method.TempDescriptors);

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var statements = method.Blocks[b].Statements;
                for (int s = 0; s < statements.Length; s++)
                    MarkAddressExposed(statements[s]);
            }

            method.EnsurePromotedStructFieldLocals();

            SealDescriptors(method.ArgDescriptors);
            SealDescriptors(method.LocalDescriptors);
            SealDescriptors(method.TempDescriptors);
            method.SetPhase(GenTreeMethodPhase.LocalRewrittenHir);
            return method;
        }

        private static void ResetDescriptors(ImmutableArray<GenLocalDescriptor> descriptors)
        {
            for (int i = 0; i < descriptors.Length; i++)
            {
                var descriptor = descriptors[i];
                descriptor.ResetPreSsaClassification();
            }
        }

        private static void SealDescriptors(ImmutableArray<GenLocalDescriptor> descriptors)
        {
            for (int i = 0; i < descriptors.Length; i++)
            {
                var descriptor = descriptors[i];
                descriptor.ClassifySpecialStorage();
                if (descriptor.AddressExposed)
                {
                    descriptor.MarkAddressExposed();
                }
                else if (descriptor.MemoryAliased)
                {
                    descriptor.MarkMemoryAliased();
                }
                else if (descriptor.IsCompilerTemp)
                {
                    descriptor.MarkUntracked();
                    descriptor.Category = GenLocalCategory.CompilerTemp;
                }
                else if (descriptor.Category == GenLocalCategory.Unclassified)
                {
                    descriptor.Category = GenLocalCategory.UntrackedLocal;
                }
            }
        }

        private static void MarkAddressExposed(GenTree node)
        {
            MarkAddressExposed(node, parent: null, operandIndex: -1);
        }

        private static void MarkAddressExposed(GenTree node, GenTree? parent, int operandIndex)
        {
            if (SsaSlotHelpers.TryGetAddressExposedSlot(node, out _))
            {
                if (parent is not null && SsaSlotHelpers.IsContainedLocalFieldAddressUse(parent, operandIndex))
                {
                    node.Flags &= ~GenTreeFlags.AddressExposed;
                    node.LocalDescriptor?.MarkPromotedStructParent();
                }
                else if (node.LocalDescriptor is not null)
                {
                    node.LocalDescriptor.MarkAddressExposed();
                    node.Flags |= GenTreeFlags.AddressExposed;
                }
            }

            for (int i = 0; i < node.Operands.Length; i++)
                MarkAddressExposed(node.Operands[i], node, i);

            if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var localFieldAccess))
            {
                var descriptor = localFieldAccess.Receiver?.LocalDescriptor ?? node.LocalDescriptor;
                if (descriptor is not null)
                    descriptor.MarkPromotedStructParent();

                GenTreeFlags flags = GenTreeFlags.None;
                for (int i = 0; i < node.Operands.Length; i++)
                    flags |= node.Operands[i].Flags;

                flags |= GenTreeFlags.Indirect;
                if (localFieldAccess.IsUse || localFieldAccess.IsPartialDefinition)
                    flags |= GenTreeFlags.LocalUse;
                if (localFieldAccess.IsDefinition)
                {
                    flags |= GenTreeFlags.LocalDef | GenTreeFlags.VarDef | GenTreeFlags.SideEffect | GenTreeFlags.Ordered;
                    if (localFieldAccess.IsPartialDefinition)
                        flags |= GenTreeFlags.VarUseAsg;
                }

                node.Flags = flags;
            }
        }
    }

    internal static class GenTreeHirVerifier
    {
        public static void Verify(GenTreeMethod method, ControlFlowGraph cfg, GenTreeLocalLiveness liveness)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));
            if (liveness is null)
                throw new ArgumentNullException(nameof(liveness));
            if (cfg.Blocks.Length != method.Blocks.Length)
                throw new InvalidOperationException("HIR verifier found a CFG/method block count mismatch.");
            if (liveness.Cfg != cfg)
                throw new InvalidOperationException("HIR verifier found liveness for a different CFG instance.");
            if (liveness.LiveIn.Length != method.Blocks.Length || liveness.LiveOut.Length != method.Blocks.Length)
                throw new InvalidOperationException("HIR verifier found malformed liveness block sets.");

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var block = method.Blocks[b];
                if (block.Id != b)
                    throw new InvalidOperationException($"HIR block id mismatch: expected B{b}, found B{block.Id}.");

                for (int i = 0; i < block.SuccessorBlockIds.Length; i++)
                {
                    int succ = block.SuccessorBlockIds[i];
                    if ((uint)succ >= (uint)method.Blocks.Length)
                        throw new InvalidOperationException($"HIR block B{b} has invalid successor B{succ}.");
                }

                for (int s = 0; s < block.Statements.Length; s++)
                    VerifyTree(block.Statements[s], expectedParent: null, blockId: b, operandIndex: -1);
            }
        }

        private static void VerifyTree(GenTree node, GenTree? expectedParent, int blockId, int operandIndex)
        {
            if (node.Parent != expectedParent)
                throw new InvalidOperationException($"HIR parent link mismatch in B{blockId}: {node}.");

            if (node.LinearId >= 0)
                throw new InvalidOperationException($"HIR node already has LIR id before rationalization: {node.LinearId}.");

            if (SsaSlotHelpers.TryGetAddressExposedSlot(node, out _) &&
                node.LocalDescriptor is { AddressExposed: false } &&
                (expectedParent is null || !SsaSlotHelpers.IsContainedLocalFieldAddressUse(expectedParent, operandIndex)))
                throw new InvalidOperationException($"HIR address-exposed local was not marked: {node}.");

            for (int i = 0; i < node.Operands.Length; i++)
                VerifyTree(node.Operands[i], node, blockId, i);
        }
    }

    internal static class CodeGenerator
    {
        public static CodeImage Build(GenTreeProgram program, CodeGeneratorOptions? options = null)
        {
            if (program is null) throw new ArgumentNullException(nameof(program));
            options ??= CodeGeneratorOptions.Default;

            var state = new BuildState();
            var asm = new Assembler();

            ImageFlags flags = ImageFlags.LittleEndian;
            if (TargetArchitecture.PointerSize == 4)
                flags |= ImageFlags.Target32;

            for (int i = 0; i < program.Methods.Length; i++)
            {
                var method = program.Methods[i];
                if (method.Phase < GenTreeMethodPhase.RegisterAllocated)
                    throw new InvalidOperationException("Code generation requires LSRA-annotated LIR. Run LinearScanRegisterAllocator.AllocateMethod before CodeGenerator.Build.");
                if (method.StackFrame.UsesFramePointer)
                    flags |= ImageFlags.UsesFramePointer;
                new MethodEmitter(asm, state, method, options).Emit();
                method.SetPhase(GenTreeMethodPhase.CodeGenerated);
            }

            var image = asm.Build(flags);
            if (options.VerifyImage)
                image.Validate();
            return image;
        }

        public static byte[] BuildBytes(GenTreeProgram program, CodeGeneratorOptions? options = null)
            => ImageSerializer.ToBytes(Build(program, options));

        private sealed class BuildState
        {
            public int GcRootCount;
        }

        private sealed class MethodEmitter
        {
            private readonly Assembler _asm;
            private readonly BuildState _state;
            private readonly GenTreeMethod _method;
            private readonly CodeGeneratorOptions _options;
            private readonly Label[] _blockLabels;
            private readonly int[] _blockStartPc;
            private readonly int[] _blockEndPc;
            private readonly Dictionary<int, int> _nodePc = new Dictionary<int, int>();
            private readonly Dictionary<int, int> _nodePositions = new Dictionary<int, int>();
            private readonly Dictionary<int, int> _blockStartPositions = new Dictionary<int, int>();
            private readonly Dictionary<int, int> _blockEndPositions = new Dictionary<int, int>();
            private readonly HashSet<int> _emittedGcSafePointPcs = new HashSet<int>();

            public MethodEmitter(Assembler asm, BuildState state, GenTreeMethod method, CodeGeneratorOptions options)
            {
                _asm = asm ?? throw new ArgumentNullException(nameof(asm));
                _state = state ?? throw new ArgumentNullException(nameof(state));
                _method = method ?? throw new ArgumentNullException(nameof(method));
                _options = options ?? throw new ArgumentNullException(nameof(options));

                int blockCount = method.Blocks.Length;
                _blockLabels = new Label[blockCount];
                _blockStartPc = new int[blockCount];
                _blockEndPc = new int[blockCount];
                for (int i = 0; i < blockCount; i++)
                {
                    _blockLabels[i] = _asm.CreateLabel();
                    _blockStartPc[i] = -1;
                    _blockEndPc[i] = -1;
                }

                BuildPositionMaps();
            }

            public void Emit()
            {
                int runtimeMethodId = _method.RuntimeMethod.MethodId;
                ushort methodFlags = _method.StackFrame.UsesFramePointer
                    ? (ushort)MethodFlags.UsesFramePointer
                    : (ushort)MethodFlags.None;
                _asm.BeginMethod(runtimeMethodId, _method.StackFrame, methodFlags);

                int methodEntryPc = _asm.Pc;
                var order = _method.LinearBlockOrder;
                for (int i = 0; i < order.Length; i++)
                {
                    int blockId = order[i];
                    int nextBlockId = i + 1 < order.Length ? order[i + 1] : -1;
                    var block = _method.Blocks[blockId];
                    _asm.Bind(_blockLabels[blockId]);
                    _blockStartPc[blockId] = _asm.Pc;
                    for (int j = 0; j < block.LinearNodes.Length; j++)
                        EmitGenTreeNode(block.LinearNodes[j]);

                    EmitBlockLayoutFixup(blockId, nextBlockId);

                    if (_asm.Pc == _blockStartPc[blockId])
                        _asm.Nop();

                    _blockEndPc[blockId] = _asm.Pc;
                }

                if (_asm.Pc == methodEntryPc)
                    _asm.Nop();

                if (_options.EmitExceptionRegions)
                    EmitExceptionRegions();
                if (_options.EmitUnwindInfo)
                    EmitUnwindInfo();

                _asm.EndMethod();
            }

            private void EmitBlockLayoutFixup(int blockId, int nextBlockId)
            {
                var successors = _method.Cfg.Blocks[blockId].Successors;
                if (successors.Length == 0)
                    return;

                int fallThroughBlockId = -1;
                for (int i = 0; i < successors.Length; i++)
                {
                    var edge = successors[i];
                    if (edge.Kind == CfgEdgeKind.FallThrough)
                    {
                        fallThroughBlockId = edge.ToBlockId;
                        break;
                    }
                }

                if (fallThroughBlockId < 0 || fallThroughBlockId == nextBlockId)
                    return;

                _asm.J(_blockLabels[fallThroughBlockId]);
            }

            private void BuildPositionMaps()
            {
                int position = 0;
                var linear = _method;
                for (int i = 0; i < linear.LinearBlockOrder.Length; i++)
                {
                    int blockId = linear.LinearBlockOrder[i];
                    _blockStartPositions[blockId] = position;
                    var nodes = linear.Blocks[blockId].LinearNodes;
                    for (int n = 0; n < nodes.Length; n++)
                    {
                        var node = nodes[n];
                        _nodePositions[node.LinearId] = position;

                        if (node.IsPhiCopy)
                        {
                            while (n + 1 < nodes.Length && SamePhiCopyGroup(node, nodes[n + 1]))
                            {
                                n++;
                                _nodePositions[nodes[n].LinearId] = position;
                            }
                        }

                        position += 2;
                    }
                    _blockEndPositions[blockId] = position;
                    position += 2;
                }
            }

            private static bool SamePhiCopyGroup(GenTree left, GenTree right)
                => left.IsPhiCopy &&
                   right.IsPhiCopy &&
                   left.LinearPhiCopyFromBlockId == right.LinearPhiCopyFromBlockId &&
                   left.LinearPhiCopyToBlockId == right.LinearPhiCopyToBlockId;

            private static int ExecutionOrderNodeId(GenTree node)
                => node.LinearId >= 0 ? node.LinearId : node.Id;

            private static GenTree CreateSyntheticMoveTree(
                GenTree parent,
                RegisterOperand destination,
                RegisterOperand source,
                GenTree? destinationValue,
                GenTree? sourceValue,
                string? comment,
                MoveFlags moveFlags = MoveFlags.None)
            {
                return GenTreeLirFactory.Move(
                    ExecutionOrderNodeId(parent),
                    parent.BlockId,
                    parent.Ordinal,
                    destination,
                    source,
                    destinationValue,
                    sourceValue,
                    comment,
                    moveFlags);
            }

            private void EmitGenTreeNode(GenTree instruction)
            {
                int pc = _asm.Pc;
                _nodePc[ExecutionOrderNodeId(instruction)] = pc;

                switch (instruction.TreeKind)
                {
                    case GenTreeKind.Copy:
                    case GenTreeKind.Reload:
                    case GenTreeKind.Spill:
                        EmitMove(instruction);
                        break;
                    case GenTreeKind.GcPoll:
                        _asm.GcPoll();
                        break;
                    case GenTreeKind.StackFrameOp:
                        EmitFrameOperation(instruction);
                        break;
                    default:
                        EmitTree(instruction);
                        break;
                }

                if (_options.EmitGcInfo && ShouldEmitGcReportPoint(instruction))
                    EmitGcSafePoint(pc, instruction);

            }

            private void EmitMove(GenTree instruction)
            {
                if (instruction.Uses.Length != 1)
                    throw Unsupported(instruction, "move without exactly one source");

                if (instruction.Results.Length == 1 && instruction.Results[0].Equals(instruction.Uses[0]))
                    return;

                var moveKind = instruction.MoveKind;
                if (moveKind == MoveKind.None)
                    return;

                var dst = RequireSingleResult(instruction);
                var src = instruction.Uses[0];
                RuntimeType? type = TypeForMove(instruction);
                GenStackKind kind = StackKindForMove(instruction);

                switch (moveKind)
                {
                    case MoveKind.Register:
                        EmitRegisterMove(dst.Register, src.Register, type, kind, dst.RegisterClass);
                        return;
                    case MoveKind.Load:
                        EmitLoad(dst.Register, src, type, kind);
                        return;
                    case MoveKind.Store:
                        EmitStore(dst, src.Register, type, kind);
                        return;
                    case MoveKind.MemoryToMemory:
                        EmitMemoryToMemory(dst, src, type, kind);
                        return;
                    case MoveKind.LoadAddress:
                        EmitLoadAddress(dst.Register, src);
                        return;
                    case MoveKind.StoreAddress:
                        EmitLoadAddress(MachineRegisters.BackendScratch, src);
                        EmitStore(dst, MachineRegisters.BackendScratch, type, GenStackKind.Ptr);
                        return;
                    case MoveKind.None:
                        return;
                    default:
                        throw Unsupported(instruction, "unknown move shape");
                }
            }

            private void EmitFrameOperation(GenTree instruction)
            {
                switch (instruction.FrameOperation)
                {
                    case FrameOperation.AllocateFrame:
                        if (instruction.Immediate != 0)
                            EmitStackPointerSubImm(instruction.Immediate);
                        return;
                    case FrameOperation.FreeFrame:
                        if (instruction.Immediate != 0)
                            EmitStackPointerAddImm(instruction.Immediate);
                        return;
                    case FrameOperation.EstablishFramePointer:
                        _asm.MovPtr(MachineRegisters.FramePointer, MachineRegisters.StackPointer);
                        return;
                    case FrameOperation.RestoreStackPointerFromFramePointer:
                        _asm.MovPtr(MachineRegisters.StackPointer, MachineRegisters.FramePointer);
                        return;
                    case FrameOperation.EnterFuncletFrame:
                    case FrameOperation.LeaveFuncletFrame:
                        _asm.Nop();
                        return;
                    case FrameOperation.SaveReturnAddress:
                    case FrameOperation.SaveCalleeSavedRegister:
                        if (!RequireSingleResult(instruction).IsFrameSlot || instruction.Uses.Length != 1 || !instruction.Uses[0].IsRegister)
                            throw Unsupported(instruction, "invalid save-frame operand shape");
                        EmitStore(RequireSingleResult(instruction), instruction.Uses[0].Register, null, RegisterClassKind(instruction.Uses[0].Register));
                        return;
                    case FrameOperation.RestoreReturnAddress:
                    case FrameOperation.RestoreCalleeSavedRegister:
                        if (!RequireSingleResult(instruction).IsRegister || instruction.Uses.Length != 1 || !instruction.Uses[0].IsFrameSlot)
                            throw Unsupported(instruction, "invalid restore-frame operand shape");
                        EmitLoad(RequireSingleResult(instruction).Register, instruction.Uses[0], null, RegisterClassKind(RequireSingleResult(instruction).Register));
                        return;
                    default:
                        throw Unsupported(instruction, "unsupported frame operation " + instruction.FrameOperation);
                }
            }
            private static int UserStringRid(int token)
            {
                if (MetadataToken.Table(token) != MetadataToken.UserString)
                    throw new InvalidOperationException($"ConstString expects UserString token, got 0x{token:X8}.");
                return MetadataToken.Rid(token);
            }
            private void EmitTree(GenTree instruction)
            {
                var source = instruction;
                switch (instruction.TreeKind)
                {
                    case GenTreeKind.Nop:
                    case GenTreeKind.Eval:
                        _asm.Nop();
                        return;
                    case GenTreeKind.ConstI4:
                        _asm.LiI32(RequireResultRegister(instruction), source.Int32);
                        return;
                    case GenTreeKind.ConstI8:
                        _asm.LiI64(RequireResultRegister(instruction), source.Int64);
                        return;
                    case GenTreeKind.ConstR4Bits:
                    case GenTreeKind.ConstR8Bits:
                        EmitFloatConstant(instruction, source);
                        return;
                    case GenTreeKind.ConstNull:
                        _asm.LiNull(RequireResultRegister(instruction));
                        return;
                    case GenTreeKind.ConstString:
                        _asm.LiString(RequireResultRegister(instruction), UserStringRid(source.Int32));
                        return;
                    case GenTreeKind.DefaultValue:
                        EmitDefaultValue(instruction, source);
                        return;
                    case GenTreeKind.SizeOf:
                        _asm.LiI32(RequireResultRegister(instruction), (source.RuntimeType ?? source.Type)?.SizeOf ?? source.Int32);
                        return;
                    case GenTreeKind.Local:
                    case GenTreeKind.Arg:
                    case GenTreeKind.Temp:
                    case GenTreeKind.StoreLocal:
                    case GenTreeKind.StoreArg:
                    case GenTreeKind.StoreTemp:
                        EmitLocalLike(instruction);
                        return;
                    case GenTreeKind.LocalAddr:
                    case GenTreeKind.ArgAddr:
                        EmitAddressTree(instruction, source);
                        return;
                    case GenTreeKind.ExceptionObject:
                        _asm.Emit(new InstrDesc(Op.LdExceptionRef, RegisterVmIsa.EncodeRegister(RequireResultRegister(instruction))));
                        return;
                    case GenTreeKind.Unary:
                        EmitUnary(instruction, source);
                        return;
                    case GenTreeKind.Binary:
                        EmitBinary(instruction, source);
                        return;
                    case GenTreeKind.Conv:
                        EmitConversion(instruction, source);
                        return;
                    case GenTreeKind.Branch:
                        if (source.SourceOp == BytecodeOp.Leave)
                            _asm.Leave(LabelForTarget(source));
                        else
                            _asm.J(LabelForTarget(source));
                        return;
                    case GenTreeKind.BranchTrue:
                    case GenTreeKind.BranchFalse:
                        EmitConditionalBranch(instruction, source);
                        return;
                    case GenTreeKind.Return:
                        EmitReturn(instruction, source);
                        return;
                    case GenTreeKind.Throw:
                        if (instruction.Uses.Length != 1)
                            throw Unsupported(instruction, "throw requires exception operand");
                        _asm.Emit(new InstrDesc(Op.Throw, rs1: RegisterVmIsa.EncodeRegister(RequireUseRegister(instruction, 0)),
                            aux: Aux.Instruction(InstructionFlags.MayThrow | InstructionFlags.PreserveException)));
                        return;
                    case GenTreeKind.Rethrow:
                        _asm.Emit(InstrDesc.Op0(Op.Rethrow, Aux.Instruction(InstructionFlags.MayThrow | InstructionFlags.PreserveException)));
                        return;
                    case GenTreeKind.EndFinally:
                        _asm.Emit(InstrDesc.Op0(Op.EndFinally));
                        return;
                    case GenTreeKind.Call:
                    case GenTreeKind.VirtualCall:
                    case GenTreeKind.NewObject:
                        EmitCallLike(instruction, source);
                        return;
                    case GenTreeKind.NewArray:
                        _asm.NewSZArray(RequireResultRegister(instruction), RequireUseRegister(instruction, 0), RequireRuntimeType(source).TypeId);
                        return;
                    case GenTreeKind.CastClass:
                        EmitRuntimeTypeCheck(instruction, source, Op.CastClass);
                        return;
                    case GenTreeKind.IsInst:
                        EmitRuntimeTypeCheck(instruction, source, Op.IsInst);
                        return;
                    case GenTreeKind.Box:
                        EmitBox(instruction, source);
                        return;
                    case GenTreeKind.UnboxAny:
                        EmitUnboxAny(instruction, source);
                        return;
                    case GenTreeKind.Field:
                    case GenTreeKind.FieldAddr:
                    case GenTreeKind.StoreField:
                        EmitField(instruction, source);
                        return;
                    case GenTreeKind.StaticField:
                    case GenTreeKind.StaticFieldAddr:
                    case GenTreeKind.StoreStaticField:
                        EmitStaticField(instruction, source);
                        return;
                    case GenTreeKind.LoadIndirect:
                    case GenTreeKind.StoreIndirect:
                        EmitIndirect(instruction, source);
                        return;
                    case GenTreeKind.ArrayElement:
                    case GenTreeKind.ArrayElementAddr:
                    case GenTreeKind.StoreArrayElement:
                    case GenTreeKind.ArrayDataRef:
                        EmitArray(instruction, source);
                        return;
                    case GenTreeKind.StackAlloc:
                        _asm.Emit(new InstrDesc(Op.StackAlloc, RegisterVmIsa.EncodeRegister(RequireResultRegister(instruction)),
                            RegisterVmIsa.EncodeRegister(RequireUseRegister(instruction, 0)), aux: Aux.Instruction(InstructionFlags.MayThrow), imm: source.Int32));
                        return;
                    case GenTreeKind.PointerElementAddr:
                        EmitPointerElementAddress(instruction, source);
                        return;
                    case GenTreeKind.PointerToByRef:
                        _asm.Emit(new InstrDesc(Op.PtrToByRef, RegisterVmIsa.EncodeRegister(RequireResultRegister(instruction)), RegisterVmIsa.EncodeRegister(RequireUseRegister(instruction, 0))));
                        return;
                    case GenTreeKind.PointerDiff:
                        _asm.Emit(new InstrDesc(Op.PtrDiff, RegisterVmIsa.EncodeRegister(RequireResultRegister(instruction)),
                            RegisterVmIsa.EncodeRegister(RequireUseRegister(instruction, 0)), RegisterVmIsa.EncodeRegister(RequireUseRegister(instruction, 1)), imm: source.Int32));
                        return;
                    default:
                        throw Unsupported(instruction, "unsupported tree kind " + instruction.TreeKind);
                }
            }

            private void EmitFloatConstant(GenTree instruction, GenTree source)
            {
                var rd = RequireResultRegister(instruction);
                if (IsFloat32(source.Type, source.StackKind, RequireSingleResult(instruction).RegisterClass))
                    _asm.LiF32Bits(rd, source.TreeKind == GenTreeKind.ConstR4Bits ? source.Int32 : unchecked((int)source.Int64));
                else
                    _asm.LiF64Bits(rd, source.Int64);
            }

            private void EmitDefaultValue(GenTree instruction, GenTree source)
            {
                if (instruction.Results.Length > 1)
                {
                    var type = source.RuntimeType ?? source.Type;
                    var segments = RegisterSegmentsForStorage(type, source.StackKind);
                    if (segments.Length != instruction.Results.Length)
                        throw Unsupported(instruction, "default value fragment count does not match storage ABI");

                    for (int i = 0; i < instruction.Results.Length; i++)
                    {
                        var destination = instruction.Results[i];
                        var segment = segments[i];
                        if (destination.IsRegister)
                        {
                            EmitZeroToRegister(destination.Register, null, StackKindForSegment(segment), segment.RegisterClass, segment.Size);
                            continue;
                        }

                        if (destination.IsFrameSlot)
                        {
                            EmitZeroToOperand(instruction, destination, null, StackKindForSegment(segment));
                            continue;
                        }

                        throw Unsupported(instruction, "default value fragment destination is not scalar");
                    }
                    return;
                }

                if (RequireSingleResult(instruction).IsRegister || RequireSingleResult(instruction).IsFrameSlot)
                {
                    EmitZeroToOperand(instruction, RequireSingleResult(instruction), source.RuntimeType ?? source.Type, source.StackKind);
                    return;
                }

                throw Unsupported(instruction, "default value destination is not scalar");
            }

            private void EmitZeroToOperand(GenTree instruction, RegisterOperand destination, RuntimeType? type, GenStackKind kind)
            {
                if (destination.IsRegister)
                {
                    if (type is not null && type.IsValueType && !IsScalarValueType(type) && !CanRepresentAsSingleRegister(destination.FrameSlotSize, type))
                        throw Unsupported(instruction, "aggregate default value cannot be written to a single register");
                    EmitZeroToRegister(destination.Register, type, kind, destination.RegisterClass, destination.FrameSlotSize);
                    return;
                }

                if (!destination.IsFrameSlot)
                    throw Unsupported(instruction, "default value destination is not scalar");

                if (IsAggregateStorage(destination, type, kind))
                {
                    EmitInitAggregate(destination, type, kind);
                    return;
                }

                var scratch = destination.RegisterClass == RegisterClass.Float
                    ? MachineRegisters.FloatBackendScratch
                    : MachineRegisters.BackendScratch;
                EmitZeroToRegister(scratch, type, kind, destination.RegisterClass, destination.FrameSlotSize);
                EmitStore(destination, scratch, ScalarizedMoveType(type, destination, RegisterOperand.None), kind);
            }

            private void EmitZeroToRegister(MachineRegister rd, RuntimeType? type, GenStackKind kind, RegisterClass registerClass, int storageSize)
            {
                if (kind is GenStackKind.Ref or GenStackKind.Null)
                {
                    _asm.LiNull(rd);
                    return;
                }

                if (registerClass == RegisterClass.Float || MachineRegisters.GetClass(rd) == RegisterClass.Float)
                {
                    if (type?.Name == "Single" || storageSize == 4)
                        _asm.LiF32Bits(rd, 0);
                    else
                        _asm.LiF64Bits(rd, 0);
                    return;
                }

                if (Is64BitInteger(type, kind) || storageSize == 8 || (type is not null && type.SizeOf > 4))
                    _asm.LiI64(rd, 0);
                else
                    _asm.LiI32(rd, 0);
            }

            private void EmitLocalLike(GenTree instruction)
            {
                var source = instruction;

                if (instruction.Results.Length > 1 || instruction.Uses.Length > 1)
                {
                    if (instruction.Uses.Length == 0 &&
                        instruction.TreeKind is GenTreeKind.Local or GenTreeKind.Arg or GenTreeKind.Temp)
                    {
                        EmitLocalLikeMultiLoad(instruction, source);
                        return;
                    }

                    if (instruction.Results.Length == 0 && instruction.Uses.Length > 1 &&
                        instruction.TreeKind is GenTreeKind.StoreLocal or GenTreeKind.StoreArg or GenTreeKind.StoreTemp)
                    {
                        EmitLocalLikeMultiStore(instruction, source);
                        return;
                    }

                    if (instruction.Results.Length == instruction.Uses.Length)
                    {
                        for (int i = 0; i < instruction.Results.Length; i++)
                        {
                            var pseudo = CreateSyntheticMoveTree(
                                instruction,
                                instruction.Results[i],
                                instruction.Uses[i],
                                i < instruction.RegisterResults.Length ? instruction.RegisterResults[i] : null,
                                i < instruction.RegisterUses.Length ? instruction.RegisterUses[i] : null,
                                instruction.Comment);
                            EmitMove(pseudo);
                        }
                        return;
                    }

                    throw Unsupported(instruction, "multi-register local/argument/temp shape has mismatched source and destination fragment counts");
                }

                if (instruction.Uses.Length == 0 &&
                    instruction.TreeKind is GenTreeKind.Local or GenTreeKind.Arg or GenTreeKind.Temp)
                {
                    var valueType = source.RuntimeType ?? source.Type;
                    var slot = FrameSlotOperandForLocalLike(source, valueType, source.StackKind,
                        RequireSingleResult(instruction).RegisterClass == RegisterClass.Invalid ? RegisterClassForStorage(valueType, source.StackKind) : RequireSingleResult(instruction).RegisterClass);
                    if (RequireSingleResult(instruction).IsRegister)
                    {
                        EmitLoad(RequireSingleResult(instruction).Register, slot, ScalarizedMoveType(valueType, RequireSingleResult(instruction), slot), source.StackKind);
                        return;
                    }
                    if (RequireSingleResult(instruction).IsFrameSlot)
                    {
                        EmitMemoryToMemory(RequireSingleResult(instruction), slot, valueType, source.StackKind);
                        return;
                    }
                }

                if (instruction.Results.Length == 0 && instruction.Uses.Length == 1 &&
                    instruction.TreeKind is GenTreeKind.StoreLocal or GenTreeKind.StoreArg or GenTreeKind.StoreTemp)
                {
                    var valueType = LocalLikeStorageType(instruction, source, 0);
                    var valueKind = LocalLikeStorageKind(instruction, source, valueType, 0);
                    var use = instruction.Uses[0];
                    var slot = FrameSlotOperandForLocalLike(source, valueType, valueKind,
                        use.RegisterClass == RegisterClass.Invalid ? RegisterClassForStorage(valueType, valueKind) : use.RegisterClass);
                    if (IsAggregateStorage(use, valueType, valueKind))
                    {
                        EmitMemoryToMemory(slot, use, valueType, valueKind);
                        return;
                    }
                    EmitStore(slot, RequireUseRegister(instruction, 0), ScalarizedMoveType(valueType, slot, use), valueKind);
                    return;
                }

                if (instruction.Results.Length == 1 && instruction.Uses.Length == 1)
                {
                    var pseudo = CreateSyntheticMoveTree(
                        instruction,
                        RequireSingleResult(instruction),
                        instruction.Uses[0],
                        SingleRegisterResultOrNull(instruction),
                        instruction.RegisterUses.Length == 0 ? null : instruction.RegisterUses[0],
                        instruction.Comment);
                    EmitMove(pseudo);
                    return;
                }

                if (instruction.Results.Length == 0 && instruction.Uses.Length <= 1)
                {
                    _asm.Nop();
                    return;
                }

                throw Unsupported(instruction, "unsupported local/argument/temp operand shape");
            }

            private void EmitLocalLikeMultiLoad(GenTree instruction, GenTree source)
            {
                var valueType = source.RuntimeType ?? source.Type;
                var valueKind = source.StackKind;
                var slot = FrameSlotOperandForLocalLike(source, valueType, valueKind, RegisterClass.General);
                var segments = RegisterSegmentsForStorage(valueType, valueKind);
                if (segments.Length != instruction.Results.Length)
                    throw Unsupported(instruction, "multi-register local/argument/temp load fragment count does not match storage ABI");

                for (int i = 0; i < segments.Length; i++)
                {
                    var srcFragment = FrameSlotFragment(slot, segments[i]);
                    var dst = instruction.Results[i];
                    var fragmentKind = StackKindForSegment(segments[i]);
                    if (dst.IsRegister)
                    {
                        EmitLoad(dst.Register, srcFragment, null, fragmentKind);
                        continue;
                    }

                    if (dst.IsFrameSlot)
                    {
                        EmitMemoryToMemory(dst, srcFragment, null, fragmentKind);
                        continue;
                    }

                    throw Unsupported(instruction, "multi-register local/argument/temp load destination is not addressable");
                }
            }

            private void EmitLocalLikeMultiStore(GenTree instruction, GenTree source)
            {
                var valueType = LocalLikeStorageType(instruction, source, 0);
                var valueKind = LocalLikeStorageKind(instruction, source, valueType, 0);
                var slot = FrameSlotOperandForLocalLike(source, valueType, valueKind, RegisterClass.General);
                var segments = RegisterSegmentsForStorage(valueType, valueKind);
                if (segments.Length != instruction.Uses.Length)
                    throw Unsupported(instruction, "multi-register local/argument/temp store fragment count does not match storage ABI");

                for (int i = 0; i < segments.Length; i++)
                {
                    var dstFragment = FrameSlotFragment(slot, segments[i]);
                    var src = instruction.Uses[i];
                    var fragmentKind = StackKindForSegment(segments[i]);
                    if (src.IsRegister)
                    {
                        EmitStore(dstFragment, src.Register, null, fragmentKind);
                        continue;
                    }

                    if (src.IsFrameSlot)
                    {
                        EmitMemoryToMemory(dstFragment, src, null, fragmentKind);
                        continue;
                    }

                    throw Unsupported(instruction, "multi-register local/argument/temp store source is not addressable");
                }
            }

            private void EmitAddressTree(GenTree instruction, GenTree source)
            {
                if (!RequireSingleResult(instruction).IsRegister)
                    throw Unsupported(instruction, "address tree requires a register result");

                if (instruction.Uses.Length == 1)
                {
                    EmitLoadAddress(RequireSingleResult(instruction).Register, instruction.Uses[0]);
                    return;
                }

                if (instruction.Uses.Length == 0)
                {
                    EmitLoadAddress(RequireSingleResult(instruction).Register, FrameSlotOperandForAddressTree(source));
                    return;
                }

                throw Unsupported(instruction, "address tree requires zero or one address source");
            }

            private RegisterOperand FrameSlotOperandForAddressTree(GenTree source)
            {
                StackFrameSlot slot;
                switch (source.Kind)
                {
                    case GenTreeKind.LocalAddr:
                        if (!_method.StackFrame.TryGetLocalSlot(source.Int32, out slot))
                            throw new InvalidOperationException("No finalized frame slot for local " + source.Int32 + ".");
                        break;
                    case GenTreeKind.ArgAddr:
                        if (!_method.StackFrame.TryGetArgumentSlot(source.Int32, out slot))
                            throw new InvalidOperationException("No finalized frame slot for argument " + source.Int32 + ".");
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported address tree source: " + source.Kind + ".");
                }

                var frameBase = _method.StackFrame.UsesFramePointer
                    ? RegisterFrameBase.FramePointer
                    : RegisterFrameBase.StackPointer;
                int slotSize = slot.Size <= 0 ? TargetArchitecture.PointerSize : slot.Size;

                return RegisterOperand.ForFrameSlot(
                    RegisterClass.General,
                    slot.Kind,
                    frameBase,
                    slot.Index,
                    slot.Offset,
                    slotSize,
                    isAddress: true);
            }


            private RegisterOperand FrameSlotOperandForLocalLike(GenTree source, RuntimeType? type, GenStackKind kind, RegisterClass registerClass)
            {
                StackFrameSlot slot;
                switch (source.Kind)
                {
                    case GenTreeKind.Local:
                    case GenTreeKind.StoreLocal:
                    case GenTreeKind.LocalAddr:
                        if (!_method.StackFrame.TryGetLocalSlot(source.Int32, out slot))
                            throw new InvalidOperationException("No finalized frame slot for local " + source.Int32 + ".");
                        break;
                    case GenTreeKind.Arg:
                    case GenTreeKind.StoreArg:
                    case GenTreeKind.ArgAddr:
                        if (!_method.StackFrame.TryGetArgumentSlot(source.Int32, out slot))
                            throw new InvalidOperationException("No finalized frame slot for argument " + source.Int32 + ".");
                        break;
                    case GenTreeKind.Temp:
                    case GenTreeKind.StoreTemp:
                        if (!_method.StackFrame.TryGetTempSlot(source.Int32, out slot))
                            throw new InvalidOperationException("No finalized frame slot for temp " + source.Int32 + ".");
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported local-like tree source: " + source.Kind + ".");
                }

                var frameBase = _method.StackFrame.UsesFramePointer
                    ? RegisterFrameBase.FramePointer
                    : RegisterFrameBase.StackPointer;

                if (registerClass == RegisterClass.Invalid)
                    registerClass = RegisterClassForStorage(type, kind);

                int slotSize = slot.Size <= 0 ? Math.Max(1, type?.SizeOf ?? TargetArchitecture.PointerSize) : slot.Size;
                return RegisterOperand.ForFrameSlot(
                    registerClass,
                    slot.Kind,
                    frameBase,
                    slot.Index,
                    slot.Offset,
                    slotSize,
                    isAddress: false);
            }

            private void EmitUnary(GenTree instruction, GenTree source)
            {
                var rd = RequireResultRegister(instruction);
                var rs = RequireUseRegister(instruction, 0);
                var kind = OperandStackKind(instruction, source, operandIndex: 0);
                var type = OperandType(instruction, source, operandIndex: 0);

                switch (source.SourceOp)
                {
                    case BytecodeOp.Neg:
                        EmitR(source, IsFloat32(type, kind, RequireSingleResult(instruction).RegisterClass)
                            ? Op.F32Neg
                            : IsFloat64(type, kind, RequireSingleResult(instruction).RegisterClass)
                                ? Op.F64Neg
                                : Is64BitInteger(type, kind)
                                    ? Op.I64Neg
                                    : Op.I32Neg, rd, rs, MachineRegister.Invalid);
                        return;
                    case BytecodeOp.Not:
                        EmitR(source, Is64BitInteger(type, kind) ? Op.I64Not : Op.I32Not, rd, rs, MachineRegister.Invalid);
                        return;
                    default:
                        throw Unsupported(instruction, "unsupported unary opcode " + source.SourceOp);
                }
            }

            private void EmitBinary(GenTree instruction, GenTree source)
            {
                var rd = RequireResultRegister(instruction);
                var a = RequireUseRegister(instruction, 0);
                var kind = OperandStackKind(instruction, source, 0);
                var type = OperandType(instruction, source, 0);
                bool f32 = IsFloat32Value(type, kind);
                bool f64 = IsFloat64Value(type, kind);
                bool i64 = Is64BitInteger(type, kind);
                bool unsigned = IsUnsignedInteger(type, kind) || source.SourceOp is BytecodeOp.Clt_Un or BytecodeOp.Cgt_Un or BytecodeOp.Div_Un or BytecodeOp.Rem_Un or BytecodeOp.Shr_Un or BytecodeOp.Add_Ovf_Un or BytecodeOp.Sub_Ovf_Un or BytecodeOp.Mul_Ovf_Un;

                if (TryGetContainedIntegerImmediate(instruction, 1, out long immediate))
                {
                    if (f32 || f64 || kind is GenStackKind.Ref or GenStackKind.Null)
                        throw Unsupported(instruction, "invalid contained immediate for non-integer binary opcode " + source.SourceOp);

                    EmitBinaryImmediate(instruction, source, rd, a, immediate, i64, unsigned);
                    return;
                }

                var b = RequireUseRegister(instruction, 1);
                Op op;

                if (f32)
                    op = source.SourceOp switch
                    {
                        BytecodeOp.Add => Op.F32Add,
                        BytecodeOp.Sub => Op.F32Sub,
                        BytecodeOp.Mul => Op.F32Mul,
                        BytecodeOp.Div => Op.F32Div,
                        BytecodeOp.Rem => Op.F32Rem,
                        BytecodeOp.Ceq => Op.F32Eq,
                        BytecodeOp.Clt => Op.F32Lt,
                        BytecodeOp.Cgt => Op.F32Gt,
                        _ => throw Unsupported(instruction, "unsupported f32 binary opcode " + source.SourceOp),
                    };
                else if (f64)
                    op = source.SourceOp switch
                    {
                        BytecodeOp.Add => Op.F64Add,
                        BytecodeOp.Sub => Op.F64Sub,
                        BytecodeOp.Mul => Op.F64Mul,
                        BytecodeOp.Div => Op.F64Div,
                        BytecodeOp.Rem => Op.F64Rem,
                        BytecodeOp.Ceq => Op.F64Eq,
                        BytecodeOp.Clt => Op.F64Lt,
                        BytecodeOp.Cgt => Op.F64Gt,
                        _ => throw Unsupported(instruction, "unsupported f64 binary opcode " + source.SourceOp),
                    };
                else if (kind is GenStackKind.Ref or GenStackKind.Null)
                    op = source.SourceOp switch
                    {
                        BytecodeOp.Ceq => Op.RefEq,
                        _ => throw Unsupported(instruction, "unsupported reference binary opcode " + source.SourceOp),
                    };
                else if (i64)
                    op = source.SourceOp switch
                    {
                        BytecodeOp.Add => Op.I64Add,
                        BytecodeOp.Add_Ovf => Op.I64AddOvf,
                        BytecodeOp.Add_Ovf_Un => Op.U64AddOvf,
                        BytecodeOp.Sub => Op.I64Sub,
                        BytecodeOp.Sub_Ovf => Op.I64SubOvf,
                        BytecodeOp.Sub_Ovf_Un => Op.U64SubOvf,
                        BytecodeOp.Mul => Op.I64Mul,
                        BytecodeOp.Mul_Ovf => Op.I64MulOvf,
                        BytecodeOp.Mul_Ovf_Un => Op.U64MulOvf,
                        BytecodeOp.Div => unsigned ? Op.U64Div : Op.I64Div,
                        BytecodeOp.Div_Un => Op.U64Div,
                        BytecodeOp.Rem => unsigned ? Op.U64Rem : Op.I64Rem,
                        BytecodeOp.Rem_Un => Op.U64Rem,
                        BytecodeOp.And => Op.I64And,
                        BytecodeOp.Or => Op.I64Or,
                        BytecodeOp.Xor => Op.I64Xor,
                        BytecodeOp.Shl => Op.I64Shl,
                        BytecodeOp.Shr => unsigned ? Op.U64Shr : Op.I64Shr,
                        BytecodeOp.Shr_Un => Op.U64Shr,
                        BytecodeOp.Ceq => Op.I64Eq,
                        BytecodeOp.Clt => unsigned ? Op.U64Lt : Op.I64Lt,
                        BytecodeOp.Clt_Un => Op.U64Lt,
                        BytecodeOp.Cgt => unsigned ? Op.U64Gt : Op.I64Gt,
                        BytecodeOp.Cgt_Un => Op.U64Gt,
                        _ => throw Unsupported(instruction, "unsupported i64 binary opcode " + source.SourceOp),
                    };
                else
                    op = source.SourceOp switch
                    {
                        BytecodeOp.Add => Op.I32Add,
                        BytecodeOp.Add_Ovf => Op.I32AddOvf,
                        BytecodeOp.Add_Ovf_Un => Op.U32AddOvf,
                        BytecodeOp.Sub => Op.I32Sub,
                        BytecodeOp.Sub_Ovf => Op.I32SubOvf,
                        BytecodeOp.Sub_Ovf_Un => Op.U32SubOvf,
                        BytecodeOp.Mul => Op.I32Mul,
                        BytecodeOp.Mul_Ovf => Op.I32MulOvf,
                        BytecodeOp.Mul_Ovf_Un => Op.U32MulOvf,
                        BytecodeOp.Div => unsigned ? Op.U32Div : Op.I32Div,
                        BytecodeOp.Div_Un => Op.U32Div,
                        BytecodeOp.Rem => unsigned ? Op.U32Rem : Op.I32Rem,
                        BytecodeOp.Rem_Un => Op.U32Rem,
                        BytecodeOp.And => Op.I32And,
                        BytecodeOp.Or => Op.I32Or,
                        BytecodeOp.Xor => Op.I32Xor,
                        BytecodeOp.Shl => Op.I32Shl,
                        BytecodeOp.Shr => unsigned ? Op.U32Shr : Op.I32Shr,
                        BytecodeOp.Shr_Un => Op.U32Shr,
                        BytecodeOp.Ceq => Op.I32Eq,
                        BytecodeOp.Clt => unsigned ? Op.U32Lt : Op.I32Lt,
                        BytecodeOp.Clt_Un => Op.U32Lt,
                        BytecodeOp.Cgt => unsigned ? Op.U32Gt : Op.I32Gt,
                        BytecodeOp.Cgt_Un => Op.U32Gt,
                        _ => throw Unsupported(instruction, "unsupported i32 binary opcode " + source.SourceOp),
                    };

                EmitR(source, op, rd, a, b);
            }

            private bool TryGetContainedIntegerImmediate(GenTree instruction, int operandIndex, out long value)
            {
                value = 0;
                if ((uint)operandIndex >= (uint)instruction.Operands.Length)
                    return false;

                var flags = instruction.OperandFlags.IsDefaultOrEmpty || operandIndex >= instruction.OperandFlags.Length
                    ? LirOperandFlags.None
                    : instruction.OperandFlags[operandIndex];
                if ((flags & LirOperandFlags.Contained) == 0)
                    return false;

                var source = instruction.Operands[operandIndex];
                switch (source.Kind)
                {
                    case GenTreeKind.ConstI4:
                        value = source.Int32;
                        return true;
                    case GenTreeKind.ConstI8:
                        value = source.Int64;
                        return true;
                    default:
                        return false;
                }
            }

            private void EmitBinaryImmediate(
                GenTree instruction,
                GenTree source,
                MachineRegister rd,
                MachineRegister a,
                long value,
                bool i64,
                bool unsigned)
            {
                if (i64)
                {
                    switch (source.SourceOp)
                    {
                        case BytecodeOp.Add: _asm.I64AddImm(rd, a, value); return;
                        case BytecodeOp.Sub: _asm.I64SubImm(rd, a, value); return;
                        case BytecodeOp.Mul: _asm.I64MulImm(rd, a, value); return;
                        case BytecodeOp.And: _asm.I64AndImm(rd, a, value); return;
                        case BytecodeOp.Or: _asm.I64OrImm(rd, a, value); return;
                        case BytecodeOp.Xor: _asm.I64XorImm(rd, a, value); return;
                        case BytecodeOp.Shl: _asm.I64ShlImm(rd, a, unchecked((int)value)); return;
                        case BytecodeOp.Shr: _asm.I64ShrImm(rd, a, unchecked((int)value)); return;
                        case BytecodeOp.Shr_Un: _asm.U64ShrImm(rd, a, unchecked((int)value)); return;
                        case BytecodeOp.Ceq: _asm.I64EqImm(rd, a, value); return;
                        case BytecodeOp.Clt:
                            if (unsigned) _asm.U64LtImm(rd, a, unchecked((ulong)value));
                            else _asm.I64LtImm(rd, a, value);
                            return;
                        case BytecodeOp.Clt_Un: _asm.U64LtImm(rd, a, unchecked((ulong)value)); return;
                        case BytecodeOp.Cgt:
                            if (unsigned) throw Unsupported(instruction, "unsupported unsigned greater-than immediate opcode " + source.SourceOp);
                            _asm.I64GtImm(rd, a, value);
                            return;
                    }
                }
                else
                {
                    int imm = unchecked((int)value);
                    switch (source.SourceOp)
                    {
                        case BytecodeOp.Add: _asm.I32AddImm(rd, a, imm); return;
                        case BytecodeOp.Sub: _asm.I32SubImm(rd, a, imm); return;
                        case BytecodeOp.Mul: _asm.I32MulImm(rd, a, imm); return;
                        case BytecodeOp.And: _asm.I32AndImm(rd, a, imm); return;
                        case BytecodeOp.Or: _asm.I32OrImm(rd, a, imm); return;
                        case BytecodeOp.Xor: _asm.I32XorImm(rd, a, imm); return;
                        case BytecodeOp.Shl: _asm.I32ShlImm(rd, a, imm); return;
                        case BytecodeOp.Shr: _asm.I32ShrImm(rd, a, imm); return;
                        case BytecodeOp.Shr_Un: _asm.U32ShrImm(rd, a, imm); return;
                        case BytecodeOp.Ceq: _asm.I32EqImm(rd, a, imm); return;
                        case BytecodeOp.Clt:
                            if (unsigned) _asm.U32LtImm(rd, a, unchecked((uint)imm));
                            else _asm.I32LtImm(rd, a, imm);
                            return;
                        case BytecodeOp.Clt_Un: _asm.U32LtImm(rd, a, unchecked((uint)imm)); return;
                        case BytecodeOp.Cgt:
                            if (unsigned) throw Unsupported(instruction, "unsupported unsigned greater-than immediate opcode " + source.SourceOp);
                            _asm.I32GtImm(rd, a, imm);
                            return;
                    }
                }

                throw Unsupported(instruction, "unsupported contained-immediate binary opcode " + source.SourceOp);
            }

            private void EmitConversion(GenTree instruction, GenTree source)
            {
                var rd = RequireResultRegister(instruction);
                var rs = RequireUseRegister(instruction, 0);
                var fromKind = OperandStackKind(instruction, source, 0);
                var fromType = OperandType(instruction, source, 0);
                bool fromF32 = IsFloat32Value(fromType, fromKind);
                bool fromF64 = IsFloat64Value(fromType, fromKind);
                bool fromI64 = fromKind == GenStackKind.I8 || Is64BitInteger(fromType, fromKind);
                bool fromI32 = fromKind == GenStackKind.I4 || fromKind is GenStackKind.NativeInt or GenStackKind.NativeUInt;
                bool fromUnsigned = IsUnsignedInteger(fromType, fromKind) || (source.ConvFlags & NumericConvFlags.SourceUnsigned) != 0;
                bool checkedConversion = (source.ConvFlags & NumericConvFlags.Checked) != 0;

                switch (source.ConvKind)
                {
                    case NumericConvKind.Bool:
                        EmitBoolConversion(source, rd, rs, fromF32, fromF64, fromI64);
                        return;

                    case NumericConvKind.I1:
                        EmitNarrowIntegerConversion(source, rd, rs, fromF32, fromF64, fromI64, fromUnsigned, checkedConversion, 1, signedResult: true);
                        return;

                    case NumericConvKind.U1:
                        EmitNarrowIntegerConversion(source, rd, rs, fromF32, fromF64, fromI64, fromUnsigned, checkedConversion, 1, signedResult: false);
                        return;

                    case NumericConvKind.I2:
                        EmitNarrowIntegerConversion(source, rd, rs, fromF32, fromF64, fromI64, fromUnsigned, checkedConversion, 2, signedResult: true);
                        return;

                    case NumericConvKind.U2:
                    case NumericConvKind.Char:
                        EmitNarrowIntegerConversion(source, rd, rs, fromF32, fromF64, fromI64, fromUnsigned, checkedConversion, 2, signedResult: false);
                        return;
                }

                Op? op = null;

                switch (source.ConvKind)
                {
                    case NumericConvKind.I4:
                    case NumericConvKind.NativeInt:
                        if (fromI64) op = CheckedI64ToI32Op(checkedConversion, fromUnsigned);
                        else if (fromF32) op = checkedConversion ? Op.F32ToI32Ovf : Op.F32ToI32;
                        else if (fromF64) op = checkedConversion ? Op.F64ToI32Ovf : Op.F64ToI32;
                        break;
                    case NumericConvKind.U4:
                    case NumericConvKind.NativeUInt:
                        if (fromI64) op = CheckedI64ToU32Op(checkedConversion, fromUnsigned);
                        else if (fromF32) op = checkedConversion ? Op.F32ToI32Ovf : Op.F32ToI32;
                        else if (fromF64) op = checkedConversion ? Op.F64ToI32Ovf : Op.F64ToI32;
                        break;
                    case NumericConvKind.I8:
                        if (fromI32) op = fromUnsigned ? Op.U32ToI64 : Op.I32ToI64;
                        else if (fromF32) op = checkedConversion ? Op.F32ToI64Ovf : Op.F32ToI64;
                        else if (fromF64) op = checkedConversion ? Op.F64ToI64Ovf : Op.F64ToI64;
                        break;
                    case NumericConvKind.U8:
                        if (fromI32) op = fromUnsigned ? Op.U32ToI64 : Op.I32ToI64;
                        else if (fromF32) op = checkedConversion ? Op.F32ToI64Ovf : Op.F32ToI64;
                        else if (fromF64) op = checkedConversion ? Op.F64ToI64Ovf : Op.F64ToI64;
                        break;
                    case NumericConvKind.R4:
                        if (fromI64)
                            op = fromUnsigned ? Op.U64ToF32 : Op.I64ToF32;
                        else if (fromI32)
                            op = fromUnsigned ? Op.U32ToF32 : Op.I32ToF32;
                        else if (fromF64) op = Op.F64ToF32;
                        break;
                    case NumericConvKind.R8:
                        if (fromI64)
                            op = fromUnsigned ? Op.U64ToF64 : Op.I64ToF64;
                        else if (fromI32)
                            op = fromUnsigned ? Op.U32ToF64 : Op.I32ToF64;
                        else if (fromF32) op = Op.F32ToF64;
                        break;
                    default:
                        break;
                }

                if (op is null)
                {
                    EmitRegisterMove(rd, rs, source.Type, source.StackKind, RequireSingleResult(instruction).RegisterClass);
                    return;
                }

                EmitR(source, op.Value, rd, rs, MachineRegister.Invalid);
            }

            private void EmitBoolConversion(GenTree source, MachineRegister rd, MachineRegister rs, bool fromF32, bool fromF64, bool fromI64)
            {
                if (fromF32)
                {
                    _asm.LiF32Bits(MachineRegisters.FloatBackendScratch, 0);
                    EmitR(source, Op.F32Ne, rd, rs, MachineRegisters.FloatBackendScratch);
                    return;
                }

                if (fromF64)
                {
                    _asm.LiF64Bits(MachineRegisters.FloatBackendScratch, 0);
                    EmitR(source, Op.F64Ne, rd, rs, MachineRegisters.FloatBackendScratch);
                    return;
                }

                if (fromI64)
                    _asm.I64NeImm(rd, rs, 0);
                else
                    _asm.I32NeImm(rd, rs, 0);
            }

            private void EmitNarrowIntegerConversion(
                GenTree source,
                MachineRegister rd,
                MachineRegister rs,
                bool fromF32,
                bool fromF64,
                bool fromI64,
                bool fromUnsigned,
                bool checkedConversion,
                int width,
                bool signedResult)
            {
                MachineRegister src32 = rs;

                if (fromF32)
                {
                    EmitR(source, checkedConversion ? Op.F32ToI32Ovf : Op.F32ToI32, rd, rs, MachineRegister.Invalid);
                    src32 = rd;
                }
                else if (fromF64)
                {
                    EmitR(source, checkedConversion ? Op.F64ToI32Ovf : Op.F64ToI32, rd, rs, MachineRegister.Invalid);
                    src32 = rd;
                }
                else if (fromI64)
                {
                    EmitR(source, CheckedI64ToI32Op(checkedConversion, fromUnsigned), rd, rs, MachineRegister.Invalid);
                    src32 = rd;
                }

                if (checkedConversion)
                {
                    EmitR(source, CheckedNarrowI32Op(width, signedResult, fromUnsigned), rd, src32, MachineRegister.Invalid);
                    return;
                }

                if (width == 1)
                {
                    EmitR(source, Op.TruncI32ToI8, rd, src32, MachineRegister.Invalid);
                    EmitR(source, signedResult ? Op.SignExtendI8ToI32 : Op.ZeroExtendI8ToI32, rd, rd, MachineRegister.Invalid);
                    return;
                }

                EmitR(source, Op.TruncI32ToI16, rd, src32, MachineRegister.Invalid);
                EmitR(source, signedResult ? Op.SignExtendI16ToI32 : Op.ZeroExtendI16ToI32, rd, rd, MachineRegister.Invalid);
            }

            private static Op CheckedI64ToI32Op(bool checkedConversion, bool sourceUnsigned)
            {
                if (!checkedConversion)
                    return Op.I64ToI32;
                return sourceUnsigned ? Op.U64ToI32Ovf : Op.I64ToI32Ovf;
            }

            private static Op CheckedI64ToU32Op(bool checkedConversion, bool sourceUnsigned)
            {
                if (!checkedConversion)
                    return Op.I64ToI32;
                return sourceUnsigned ? Op.U64ToU32Ovf : Op.I64ToU32Ovf;
            }

            private static Op CheckedNarrowI32Op(int width, bool signedResult, bool sourceUnsigned)
            {
                return (width, signedResult, sourceUnsigned) switch
                {
                    (1, true, false) => Op.I32ToI8Ovf,
                    (1, true, true) => Op.U32ToI8Ovf,
                    (1, false, false) => Op.I32ToU8Ovf,
                    (1, false, true) => Op.U32ToU8Ovf,
                    (2, true, false) => Op.I32ToI16Ovf,
                    (2, true, true) => Op.U32ToI16Ovf,
                    (2, false, false) => Op.I32ToU16Ovf,
                    (2, false, true) => Op.U32ToU16Ovf,
                    _ => throw new ArgumentOutOfRangeException(nameof(width)),
                };
            }

            private void EmitConditionalBranch(GenTree instruction, GenTree source)
            {
                if (instruction.Uses.Length != 1)
                    throw Unsupported(instruction, "conditional branch requires one condition register");

                var rs = RequireUseRegister(instruction, 0);
                var target = LabelForTarget(source);
                var kind = OperandStackKind(instruction, source, 0);
                bool trueBranch = instruction.TreeKind == GenTreeKind.BranchTrue;

                Op op = kind switch
                {
                    GenStackKind.I8 => trueBranch ? Op.BrTrueI64 : Op.BrFalseI64,
                    GenStackKind.Ref or GenStackKind.Null or GenStackKind.ByRef or GenStackKind.Ptr => trueBranch ? Op.BrTrueRef : Op.BrFalseRef,
                    _ => trueBranch ? Op.BrTrueI32 : Op.BrFalseI32,
                };
                _asm.Branch(op, rs, target);
            }

            private void EmitReturn(GenTree instruction, GenTree source)
            {
                if (instruction.Uses.Length == 0)
                {
                    _asm.RetVoid();
                    return;
                }

                if (instruction.RegisterUses.Length != 0)
                {
                    var value = instruction.RegisterUses[0];
                    var info = _method.GetValueInfo(value);
                    RuntimeType? returnType = info.Type ?? source.Type;
                    GenStackKind returnKind = info.StackKind;
                    var abi = MachineAbi.ClassifyValue(returnType, returnKind, isReturn: true);

                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        if (instruction.Uses.Length > 1)
                        {
                            EmitMultiRegisterReturn(instruction, abi);
                            return;
                        }
                    }

                    if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                    {
                        if (instruction.Uses.Length != 1)
                            throw Unsupported(instruction, "scalar-register return has an unexpected operand shape");

                        EmitScalarReturnFromRegister(RequireUseRegister(instruction, 0), abi, returnType, returnKind);
                        return;
                    }

                    if (abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect || IsAggregateValue(returnType, returnKind))
                    {
                        RegisterOperand sourceLocation = instruction.Uses.Length == 1 ? instruction.Uses[0] : _method.GetHome(value);
                        if (sourceLocation.IsFrameSlot)
                        {
                            EmitLoadAddress(MachineRegisters.BackendScratch, sourceLocation);
                            _asm.RetValue(MachineRegisters.BackendScratch, Math.Max(1, StorageSizeOf(returnType, returnKind, sourceLocation)));
                            return;
                        }
                        if (sourceLocation.IsRegister)
                        {
                            _asm.RetValue(sourceLocation.Register, Math.Max(1, StorageSizeOf(returnType, returnKind, sourceLocation)));
                            return;
                        }
                    }
                }

                if (instruction.Uses.Length != 1)
                    throw Unsupported(instruction, "return has an unsupported post-ABI operand shape");

                var use = instruction.Uses[0];
                var kind = OperandStackKind(instruction, source, 0);
                var type = OperandType(instruction, source, 0);
                MachineRegister rs;
                if (use.IsRegister)
                {
                    rs = use.Register;
                }
                else if (use.IsFrameSlot)
                {
                    rs = (IsFloat32(type, kind, use.RegisterClass) || IsFloat64(type, kind, use.RegisterClass))
                        ? MachineRegisters.FloatBackendScratch
                        : MachineRegisters.BackendScratch;
                    EmitLoad(rs, use, type, kind);
                }
                else
                {
                    throw Unsupported(instruction, "return source is neither register nor frame slot");
                }

                if (IsFloat32(type, kind, MachineRegisters.GetClass(rs)) || IsFloat64(type, kind, MachineRegisters.GetClass(rs)))
                    _asm.RetF(rs);
                else if (kind is GenStackKind.Ref or GenStackKind.Null || (type?.IsReferenceType ?? false))
                    _asm.RetRef(rs);
                else
                    _asm.RetI(rs);
            }

            private void EmitScalarReturnFromRegister(MachineRegister source, AbiValueInfo abi, RuntimeType? type, GenStackKind kind)
            {
                if (abi.RegisterClass == RegisterClass.Float || MachineRegisters.GetClass(source) == RegisterClass.Float)
                    _asm.RetF(source);
                else if (abi.ContainsGcPointers || kind is GenStackKind.Ref or GenStackKind.Null || (type?.IsReferenceType ?? false))
                    _asm.RetRef(source);
                else
                    _asm.RetI(source);
            }

            private void EmitMultiRegisterReturn(GenTree instruction, AbiValueInfo abi)
            {
                var segments = MachineAbi.GetRegisterSegments(abi);
                if (segments.Length == 0 || instruction.Uses.Length != segments.Length)
                    throw Unsupported(instruction, "multi-register return operand count does not match ABI");

                var first = instruction.Uses[0];
                if (!first.IsRegister)
                    throw Unsupported(instruction, "multi-register return first fragment is not an ABI register");

                if (segments[0].RegisterClass == RegisterClass.Float || MachineRegisters.GetClass(first.Register) == RegisterClass.Float)
                    _asm.RetF(first.Register);
                else if (segments[0].ContainsGcPointers)
                    _asm.RetRef(first.Register);
                else
                    _asm.RetI(first.Register);
            }

            private void EmitCallLike(GenTree instruction, GenTree source)
            {
                RuntimeMethod method = source.Method ?? throw Unsupported(instruction, "call-like node has no runtime method");

                bool hasHiddenReturnBufferOperand = HasHiddenReturnBufferOperand(instruction);

                if (instruction.TreeKind == GenTreeKind.NewObject)
                {
                    if (method.DeclaringType.IsValueType)
                    {
                        if (!hasHiddenReturnBufferOperand)
                            throw Unsupported(instruction, "value-type newobj requires a hidden return buffer operand");

                        var flags = BuildCallFlags(method, Op.CallVoid) | CallFlags.HiddenReturnBuffer;
                        _asm.Emit(InstrDesc.Call(Op.CallVoid, method.MethodId, flags));
                        return;
                    }

                    _asm.NewObj(RequireResultRegister(instruction), method.MethodId);
                    return;
                }

                RegisterClass resultClass = RegisterClass.Invalid;
                if (instruction.Results.Length == 1)
                {
                    resultClass = instruction.Results[0].RegisterClass;
                }
                else if (instruction.Results.Length > 1)
                {
                    throw Unsupported(instruction, $"call-like instruction must have zero or one result, actual count: {instruction.Results.Length}");
                }
                Op op = SelectCallOp(instruction.TreeKind, method, method.ReturnType, resultClass);
                CallFlags callFlags = BuildCallFlags(method, op);
                if ((callFlags & CallFlags.HiddenReturnBuffer) != 0 && !hasHiddenReturnBufferOperand)
                    throw Unsupported(instruction, "hidden-return-buffer call has no materialized return buffer operand");
                if (hasHiddenReturnBufferOperand)
                    callFlags |= CallFlags.HiddenReturnBuffer;
                ushort aux = Aux.Call(callFlags);

                if (instruction.TreeKind == GenTreeKind.VirtualCall)
                {
                    var site = new CallSiteRecord(
                        -1,
                        method.MethodId,
                        method.MethodId,
                        method.DeclaringType.TypeId,
                        method.VTableSlot,
                        (CallFlags)aux);
                    switch (op)
                    {
                        case Op.CallVirtVoid: _asm.CallVirtVoid(site); return;
                        case Op.CallVirtI: _asm.CallVirtI(site); return;
                        case Op.CallVirtF: _asm.CallVirtF(site); return;
                        case Op.CallVirtRef: _asm.CallVirtRef(site); return;
                        case Op.CallVirtValue: _asm.CallVirtValue(site); return;
                        case Op.CallIfaceVoid: _asm.CallIfaceVoid(site); return;
                        case Op.CallIfaceI: _asm.CallIfaceI(site); return;
                        case Op.CallIfaceF: _asm.CallIfaceF(site); return;
                        case Op.CallIfaceRef: _asm.CallIfaceRef(site); return;
                        case Op.CallIfaceValue: _asm.CallIfaceValue(site); return;
                    }
                }

                _asm.Emit(InstrDesc.Call(op, method.MethodId, (CallFlags)aux));
            }

            private static bool HasHiddenReturnBufferOperand(GenTree instruction)
            {
                for (int i = 0; i < instruction.UseRoles.Length; i++)
                {
                    if (instruction.UseRoles[i] == OperandRole.HiddenReturnBuffer)
                        return true;
                }

                return false;
            }

            private void EmitRuntimeTypeCheck(GenTree instruction, GenTree source, Op op)
            {
                if (op is not (Op.CastClass or Op.IsInst))
                    throw new ArgumentOutOfRangeException(nameof(op));

                _asm.Emit(new InstrDesc(
                    op,
                    RegisterVmIsa.EncodeRegister(RequireResultRegister(instruction)),
                    RegisterVmIsa.EncodeRegister(RequireUseRegister(instruction, 0)),
                    aux: Aux.Instruction(InstructionFlags.GcSafePoint | InstructionFlags.MayThrow),
                    imm: RequireRuntimeType(source).TypeId));
            }

            private void EmitBox(GenTree instruction, GenTree source)
            {
                var boxedType = RequireRuntimeType(source);
                GenStackKind boxedKind = StackKindOf(boxedType);
                MachineRegister value;
                if (instruction.Uses.Length == 1 && !IsAggregateStorage(instruction.Uses[0], boxedType, boxedKind))
                {
                    value = RequireUseRegister(instruction, 0);
                }
                else if (instruction.Uses.Length == 1)
                {
                    value = MaterializeAggregateAddress(instruction, 0, boxedType, boxedKind, "box source value");
                }
                else
                {
                    value = MaterializeMultiRegisterAggregateHome(instruction, 0, boxedType, boxedKind, "box source value");
                }

                _asm.Emit(new InstrDesc(Op.Box, RegisterVmIsa.EncodeRegister(RequireResultRegister(instruction)),
                    RegisterVmIsa.EncodeRegister(value), aux: Aux.Instruction(InstructionFlags.GcSafePoint | InstructionFlags.MayThrow), imm: boxedType.TypeId));
            }

            private void EmitUnboxAny(GenTree instruction, GenTree source)
            {
                var type = RequireRuntimeType(source);
                if (instruction.Results.Length > 1)
                {
                    MachineRegister address = PickScratchRegister(instruction, RegisterClass.General);
                    _asm.Emit(new InstrDesc(Op.UnboxAddr, RegisterVmIsa.EncodeRegister(address),
                        RegisterVmIsa.EncodeRegister(RequireUseRegister(instruction, 0)), aux: Aux.Instruction(InstructionFlags.GcSafePoint | InstructionFlags.MayThrow), imm: type.TypeId));
                    EmitMultiRegisterLoadFromAddress(instruction, type, StackKindOf(type), address);
                    return;
                }

                if (RequireSingleResult(instruction).IsFrameSlot && IsAggregateStorage(RequireSingleResult(instruction), type, StackKindOf(type)))
                {
                    _asm.Emit(new InstrDesc(Op.UnboxAddr, RegisterVmIsa.EncodeRegister(MachineRegisters.ParallelCopyScratch1),
                        RegisterVmIsa.EncodeRegister(RequireUseRegister(instruction, 0)), aux: Aux.Instruction(InstructionFlags.GcSafePoint | InstructionFlags.MayThrow), imm: type.TypeId));
                    EmitAddressOf(MachineRegisters.BackendScratch, RequireSingleResult(instruction));
                    EmitCopyAddressToAddress(type, StackKindOf(type), MachineRegisters.BackendScratch, MachineRegisters.ParallelCopyScratch1,
                        StorageSizeOf(type, StackKindOf(type), RequireSingleResult(instruction)), InstructionFlags.MayThrow);
                    return;
                }

                _asm.Emit(new InstrDesc(Op.UnboxAny, RegisterVmIsa.EncodeRegister(RequireResultRegister(instruction)),
                    RegisterVmIsa.EncodeRegister(RequireUseRegister(instruction, 0)), aux: Aux.Instruction(InstructionFlags.GcSafePoint | InstructionFlags.MayThrow), imm: type.TypeId));
            }

            private void EmitField(GenTree instruction, GenTree source)
            {
                var field = source.Field ?? throw Unsupported(instruction, "field node without runtime field");
                if (instruction.TreeKind == GenTreeKind.FieldAddr)
                {
                    _asm.Emit(InstrDesc.Field(Op.LdFldAddr, RequireResultRegister(instruction),
                        RequireUseRegister(instruction, 0), field.FieldId, Aux.Instruction(InstructionFlags.MayThrow)));
                    return;
                }

                if (instruction.TreeKind == GenTreeKind.StoreField)
                {
                    int instanceUseIndex = RequireCodegenUseIndexForOperand(instruction, source, 0, "field store instance");
                    int valueUseIndex = RequireCodegenUseIndexForOperand(instruction, source, 1, "field store value");
                    var instance = RequireUseRegister(instruction, instanceUseIndex);
                    ushort aux = FieldStoreAux(field.FieldType);
                    if (instruction.Uses.Length - valueUseIndex > 1)
                    {
                        GenStackKind valueKind = StackKindOf(field.FieldType);
                        if (field.FieldType.ContainsGcPointers || field.FieldType.IsReferenceType)
                        {
                            MachineRegister valueAddress = MaterializeMultiRegisterAggregateHome(
                                instruction,
                                valueUseIndex,
                                field.FieldType,
                                valueKind,
                                "field store value",
                                instance);
                            _asm.Emit(InstrDesc.Field(Op.StFldObj, valueAddress, instance, field.FieldId, aux));
                            return;
                        }

                        MachineRegister address = PickScratchRegister(instruction, RegisterClass.General, instance);
                        _asm.Emit(InstrDesc.Field(Op.LdFldAddr, address, instance, field.FieldId, Aux.Instruction(InstructionFlags.MayThrow)));
                        EmitMultiRegisterStoreToAddress(instruction, valueUseIndex, field.FieldType, valueKind, address);
                        return;
                    }

                    GenStackKind singleValueKind = StackKindOf(field.FieldType);
                    var valueOperand = instruction.Uses[valueUseIndex];
                    if (IsAggregateStorage(valueOperand, field.FieldType, singleValueKind))
                    {
                        EmitAddressOf(MachineRegisters.BackendScratch, valueOperand);
                        _asm.Emit(InstrDesc.Field(Op.StFldObj, MachineRegisters.BackendScratch, instance, field.FieldId, aux));
                        return;
                    }

                    var op = SelectStoreFieldOp(field.FieldType, singleValueKind, valueOperand.RegisterClass, 0);
                    if (op == Op.StFldObj)
                    {
                        MachineRegister valueAddress = MaterializeScalarAggregateHome(
                            instruction,
                            valueUseIndex,
                            field.FieldType,
                            singleValueKind,
                            "field store value",
                            instance);
                        _asm.Emit(InstrDesc.Field(Op.StFldObj, valueAddress, instance, field.FieldId, aux));
                        return;
                    }

                    var value = RequireUseRegister(instruction, valueUseIndex);
                    _asm.Emit(InstrDesc.Field(op, value, instance, field.FieldId, aux));
                    return;
                }

                if (instruction.Results.Length > 1)
                {
                    MachineRegister address = PickScratchRegister(instruction, RegisterClass.General, RequireUseRegister(instruction, 0));
                    _asm.Emit(InstrDesc.Field(Op.LdFldAddr, address, RequireUseRegister(instruction, 0), field.FieldId, Aux.Instruction(InstructionFlags.MayThrow)));
                    EmitMultiRegisterLoadFromAddress(instruction, field.FieldType, StackKindOf(field.FieldType), address);
                    return;
                }

                if (RequireSingleResult(instruction).IsFrameSlot && IsAggregateStorage(RequireSingleResult(instruction), field.FieldType, StackKindOf(field.FieldType)))
                {
                    EmitAddressOf(MachineRegisters.BackendScratch, RequireSingleResult(instruction));
                    _asm.Emit(InstrDesc.Field(Op.LdFldObj, MachineRegisters.BackendScratch,
                        RequireUseRegister(instruction, 0), field.FieldId, Aux.Instruction(InstructionFlags.MayThrow)));
                    return;
                }

                _asm.Emit(InstrDesc.Field(SelectLoadFieldOp(field.FieldType, StackKindOf(field.FieldType), RequireSingleResult(instruction).RegisterClass, 0), RequireResultRegister(instruction),
                    RequireUseRegister(instruction, 0), field.FieldId, Aux.Instruction(InstructionFlags.MayThrow)));
            }

            private void EmitStaticField(GenTree instruction, GenTree source)
            {
                var field = source.Field ?? throw Unsupported(instruction, "static field node without runtime field");
                if (instruction.TreeKind == GenTreeKind.StaticFieldAddr)
                {
                    _asm.Emit(InstrDesc.StaticField(Op.LdSFldAddr, RequireResultRegister(instruction), field.FieldId));
                    return;
                }

                if (instruction.TreeKind == GenTreeKind.StoreStaticField)
                {
                    int valueUseIndex = RequireCodegenUseIndexForOperand(instruction, source, 0, "static-field store value");
                    ushort aux = StaticFieldStoreAux(field.FieldType);
                    if (instruction.Uses.Length - valueUseIndex > 1)
                    {
                        GenStackKind valueKind = StackKindOf(field.FieldType);
                        if (field.FieldType.ContainsGcPointers || field.FieldType.IsReferenceType)
                        {
                            MachineRegister valueAddress = MaterializeMultiRegisterAggregateHome(
                                instruction,
                                valueUseIndex,
                                field.FieldType,
                                valueKind,
                                "static-field store value");
                            _asm.Emit(InstrDesc.StaticField(Op.StSFldObj, valueAddress, field.FieldId, aux));
                            return;
                        }

                        MachineRegister address = PickScratchRegister(instruction, RegisterClass.General);
                        _asm.Emit(InstrDesc.StaticField(Op.LdSFldAddr, address, field.FieldId));
                        EmitMultiRegisterStoreToAddress(instruction, valueUseIndex, field.FieldType, valueKind, address);
                        return;
                    }

                    GenStackKind singleValueKind = StackKindOf(field.FieldType);
                    var valueOperand = instruction.Uses[valueUseIndex];
                    if (IsAggregateStorage(valueOperand, field.FieldType, singleValueKind))
                    {
                        EmitAddressOf(MachineRegisters.BackendScratch, valueOperand);
                        _asm.Emit(InstrDesc.StaticField(Op.StSFldObj, MachineRegisters.BackendScratch, field.FieldId, aux));
                        return;
                    }

                    var op = SelectStoreStaticFieldOp(field.FieldType, singleValueKind, valueOperand.RegisterClass, 0);
                    if (op == Op.StSFldObj)
                    {
                        MachineRegister valueAddress = MaterializeScalarAggregateHome(
                            instruction,
                            valueUseIndex,
                            field.FieldType,
                            singleValueKind,
                            "static-field store value");
                        _asm.Emit(InstrDesc.StaticField(Op.StSFldObj, valueAddress, field.FieldId, aux));
                        return;
                    }

                    _asm.Emit(InstrDesc.StaticField(op, RequireUseRegister(instruction, valueUseIndex), field.FieldId, aux));
                    return;
                }

                if (instruction.Results.Length > 1)
                {
                    MachineRegister address = PickScratchRegister(instruction, RegisterClass.General);
                    _asm.Emit(InstrDesc.StaticField(Op.LdSFldAddr, address, field.FieldId));
                    EmitMultiRegisterLoadFromAddress(instruction, field.FieldType, StackKindOf(field.FieldType), address);
                    return;
                }

                if (RequireSingleResult(instruction).IsFrameSlot && IsAggregateStorage(RequireSingleResult(instruction), field.FieldType, StackKindOf(field.FieldType)))
                {
                    EmitAddressOf(MachineRegisters.BackendScratch, RequireSingleResult(instruction));
                    _asm.Emit(InstrDesc.StaticField(Op.LdSFldObj, MachineRegisters.BackendScratch, field.FieldId));
                    return;
                }

                _asm.Emit(InstrDesc.StaticField(SelectLoadStaticFieldOp(field.FieldType, StackKindOf(field.FieldType), RequireSingleResult(instruction).RegisterClass, 0), RequireResultRegister(instruction), field.FieldId));
            }

            private void EmitIndirect(GenTree instruction, GenTree source)
            {
                var type = source.RuntimeType ?? source.Type;
                if (instruction.TreeKind == GenTreeKind.LoadIndirect)
                {
                    if (instruction.Results.Length > 1)
                    {
                        MachineRegister addressRegister = RequireUseRegister(instruction, 0);
                        if (ContainsRegister(instruction.Results, addressRegister))
                        {
                            MachineRegister addressCopy = PickScratchRegister(instruction, RegisterClass.General, addressRegister);
                            _asm.MovPtr(addressCopy, addressRegister);
                            addressRegister = addressCopy;
                        }

                        EmitMultiRegisterLoadFromAddress(instruction, type, source.StackKind, addressRegister);
                        return;
                    }

                    if (RequireSingleResult(instruction).IsFrameSlot && IsAggregateStorage(RequireSingleResult(instruction), type, source.StackKind))
                    {
                        EmitAddressOf(MachineRegisters.BackendScratch, RequireSingleResult(instruction));
                        EmitCopyAddressToAddress(type, source.StackKind, MachineRegisters.BackendScratch,
                            RequireUseRegister(instruction, 0), StorageSizeOf(type, source.StackKind, RequireSingleResult(instruction)), InstructionFlags.MayThrow);
                        return;
                    }

                    _asm.Emit(InstrDesc.Mem(SelectLoadOpForStorage(type, source.StackKind, RequireSingleResult(instruction).RegisterClass, 0), RequireResultRegister(instruction),
                        RequireUseRegister(instruction, 0), 0, aux: MemoryAuxForRegisterBase(type)));
                    return;
                }

                if (instruction.Uses.Length < 2)
                    throw Unsupported(instruction, "store indirect requires address and value operands");

                int addressUseIndex = RequireCodegenUseIndexForOperand(instruction, source, 0, "indirect store address");
                int valueUseIndex = RequireCodegenUseIndexForOperand(instruction, source, 1, "indirect store value");
                var valueType = StoreTargetType(instruction, source, 1) ?? type;
                var valueKind = StoreTargetKind(instruction, source, valueType, 1);
                var address = RequireUseRegister(instruction, addressUseIndex);

                if (instruction.Uses.Length - valueUseIndex > 1)
                {
                    if (valueType is not null && (valueType.ContainsGcPointers || valueType.IsReferenceType))
                    {
                        MachineRegister valueAddress = MaterializeMultiRegisterAggregateHome(
                            instruction,
                            valueUseIndex,
                            valueType,
                            valueKind,
                            "indirect store value",
                            address);
                        EmitCopyAddressToAddress(valueType, valueKind, address, valueAddress,
                            StorageSizeOf(valueType, valueKind), InstructionFlags.MayThrow);
                        return;
                    }

                    EmitMultiRegisterStoreToAddress(instruction, valueUseIndex, valueType, valueKind, address);
                    return;
                }

                var valueOperand = instruction.Uses[valueUseIndex];
                if (IsAggregateStorage(valueOperand, valueType, valueKind))
                {
                    EmitAddressOf(MachineRegisters.ParallelCopyScratch1, valueOperand);
                    EmitCopyAddressToAddress(valueType, valueKind, address,
                        MachineRegisters.ParallelCopyScratch1, StorageSizeOf(valueType, valueKind, valueOperand), InstructionFlags.MayThrow);
                    return;
                }

                var op = SelectStoreOpForStorage(valueType, valueKind, valueOperand.RegisterClass, 0);
                if (op == Op.StObj)
                {
                    MachineRegister valueAddress = MaterializeScalarAggregateHome(
                        instruction,
                        valueUseIndex,
                        valueType,
                        valueKind,
                        "indirect store value",
                        address);
                    EmitCopyAddressToAddress(valueType, valueKind, address, valueAddress,
                        StorageSizeOf(valueType, valueKind), InstructionFlags.MayThrow);
                    return;
                }

                _asm.Emit(InstrDesc.Mem(op, RequireUseRegister(instruction, valueUseIndex),
                    address, 0, aux: MemoryAuxForRegisterBase(valueType)));
            }

            private void EmitArray(GenTree instruction, GenTree source)
            {
                var elem = source.RuntimeType ?? source.Type ?? (source.Operands.Length != 0 ? source.Operands[0].Type?.ElementType : null);
                int elemTypeId = elem?.TypeId ?? 0;

                switch (instruction.TreeKind)
                {
                    case GenTreeKind.ArrayElement:
                        if (instruction.Results.Length > 1)
                        {
                            var array = RequireUseRegister(instruction, 0);
                            var index = RequireUseRegister(instruction, 1);
                            MachineRegister address = PickScratchRegister(instruction, RegisterClass.General, array, index);
                            _asm.Emit(InstrDesc.Array(Op.LdElemAddr, address, array, index, elemTypeId, Aux.Instruction(InstructionFlags.MayThrow)));
                            EmitMultiRegisterLoadFromAddress(instruction, elem, source.StackKind, address);
                            return;
                        }

                        if (RequireSingleResult(instruction).IsFrameSlot && IsAggregateStorage(RequireSingleResult(instruction), elem, source.StackKind))
                        {
                            EmitAddressOf(MachineRegisters.BackendScratch, RequireSingleResult(instruction));
                            _asm.Emit(InstrDesc.Array(Op.LdElemObj, MachineRegisters.BackendScratch,
                                RequireUseRegister(instruction, 0), RequireUseRegister(instruction, 1), elemTypeId, Aux.Instruction(InstructionFlags.MayThrow)));
                            return;
                        }
                        _asm.Emit(InstrDesc.Array(SelectLoadElementOp(elem, source.StackKind, RequireSingleResult(instruction).RegisterClass, 0), RequireResultRegister(instruction),
                            RequireUseRegister(instruction, 0), RequireUseRegister(instruction, 1), elemTypeId, Aux.Instruction(InstructionFlags.MayThrow)));
                        return;
                    case GenTreeKind.ArrayElementAddr:
                        _asm.Emit(InstrDesc.Array(Op.LdElemAddr, RequireResultRegister(instruction), RequireUseRegister(instruction, 0),
                            RequireUseRegister(instruction, 1), elemTypeId, Aux.Instruction(InstructionFlags.MayThrow)));
                        return;
                    case GenTreeKind.StoreArrayElement:
                        {
                            int arrayUseIndex = RequireCodegenUseIndexForOperand(instruction, source, 0, "array-element store array");
                            int indexUseIndex = RequireCodegenUseIndexForOperand(instruction, source, 1, "array-element store index");
                            int valueUseIndex = RequireCodegenUseIndexForOperand(instruction, source, 2, "array-element store value");
                            var valueType = StoreTargetType(instruction, source, 2) ?? elem;
                            var valueKind = StoreTargetKind(instruction, source, valueType, 2);
                            if (instruction.Uses.Length - valueUseIndex > 1)
                            {
                                var array = RequireUseRegister(instruction, arrayUseIndex);
                                var index = RequireUseRegister(instruction, indexUseIndex);
                                if (valueType is not null && (valueType.ContainsGcPointers || valueType.IsReferenceType))
                                {
                                    MachineRegister valueAddress = MaterializeMultiRegisterAggregateHome(
                                        instruction,
                                        valueUseIndex,
                                        valueType,
                                        valueKind,
                                        "array-element store value",
                                        array,
                                        index);
                                    _asm.Emit(InstrDesc.Array(Op.StElemObj, valueAddress, array, index, elemTypeId, ElementStoreAux(valueType)));
                                    return;
                                }

                                MachineRegister address = PickScratchRegister(instruction, RegisterClass.General, array, index);
                                _asm.Emit(InstrDesc.Array(Op.LdElemAddr, address, array, index, elemTypeId, Aux.Instruction(InstructionFlags.MayThrow)));
                                EmitMultiRegisterStoreToAddress(instruction, valueUseIndex, valueType, valueKind, address);
                                return;
                            }

                            var valueOperand = instruction.Uses[valueUseIndex];
                            if (IsAggregateStorage(valueOperand, valueType, valueKind))
                            {
                                EmitAddressOf(MachineRegisters.BackendScratch, valueOperand);
                                _asm.Emit(InstrDesc.Array(Op.StElemObj, MachineRegisters.BackendScratch,
                                    RequireUseRegister(instruction, arrayUseIndex), RequireUseRegister(instruction, indexUseIndex), elemTypeId, ElementStoreAux(valueType)));
                                return;
                            }

                            var op = SelectStoreElementOp(valueType, valueKind, valueOperand.RegisterClass, 0);
                            if (op == Op.StElemObj)
                            {
                                var array = RequireUseRegister(instruction, arrayUseIndex);
                                var index = RequireUseRegister(instruction, indexUseIndex);
                                MachineRegister valueAddress = MaterializeScalarAggregateHome(
                                    instruction,
                                    valueUseIndex,
                                    valueType,
                                    valueKind,
                                    "array-element store value",
                                    array,
                                    index);
                                _asm.Emit(InstrDesc.Array(Op.StElemObj, valueAddress,
                                    array, index, elemTypeId, ElementStoreAux(valueType)));
                                return;
                            }

                            _asm.Emit(InstrDesc.Array(op, RequireUseRegister(instruction, valueUseIndex),
                                RequireUseRegister(instruction, arrayUseIndex), RequireUseRegister(instruction, indexUseIndex), elemTypeId, ElementStoreAux(valueType)));
                            return;
                        }
                    case GenTreeKind.ArrayDataRef:
                        _asm.Emit(new InstrDesc(Op.LdArrayDataAddr, RegisterVmIsa.EncodeRegister(RequireResultRegister(instruction)),
                            RegisterVmIsa.EncodeRegister(RequireUseRegister(instruction, 0)), aux: Aux.Instruction(InstructionFlags.MayThrow)));
                        return;
                }
            }

            private void EmitPointerElementAddress(GenTree instruction, GenTree source)
            {
                if (instruction.Uses.Length != 2)
                    throw Unsupported(instruction, "pointer element address requires base and index");

                var indexKind = OperandStackKind(instruction, source, 1);
                var indexType = OperandType(instruction, source, 1);
                bool index64 = indexKind == GenStackKind.I8 || Is64BitInteger(indexType, indexKind);
                bool byRefBase = source.StackKind == GenStackKind.ByRef || OperandStackKind(instruction, source, 0) == GenStackKind.ByRef;
                Op op = byRefBase
                    ? (index64 ? Op.ByRefAddI64 : Op.ByRefAddI32)
                    : (index64 ? Op.PtrAddI64 : Op.PtrAddI32);

                _asm.Emit(new InstrDesc(op, RegisterVmIsa.EncodeRegister(RequireResultRegister(instruction)),
                    RegisterVmIsa.EncodeRegister(RequireUseRegister(instruction, 0)), RegisterVmIsa.EncodeRegister(RequireUseRegister(instruction, 1)), imm: source.Int32));
            }

            private void EmitRegisterMove(MachineRegister dst, MachineRegister src, RuntimeType? type, GenStackKind kind, RegisterClass dstClass)
            {
                if (dst == src)
                    return;
                var dstRegisterClass = MachineRegisters.GetClass(dst);
                var srcRegisterClass = MachineRegisters.GetClass(src);

                if (dstRegisterClass == RegisterClass.Float)
                {
                    if (srcRegisterClass == RegisterClass.Float)
                    {
                        _asm.MovF(dst, src);
                        return;
                    }

                    if (srcRegisterClass == RegisterClass.General)
                    {
                        _asm.Emit(InstrDesc.R(SelectIntegerToFloatBitcastOp(type, kind), dst, src, MachineRegister.Invalid));
                        return;
                    }
                }

                if (dstRegisterClass == RegisterClass.General)
                {
                    if (srcRegisterClass == RegisterClass.Float)
                    {
                        _asm.Emit(InstrDesc.R(SelectFloatToIntegerBitcastOp(type, kind), dst, src, MachineRegister.Invalid));
                        return;
                    }
                    if (srcRegisterClass == RegisterClass.General)
                    {
                        if (kind is GenStackKind.Ref or GenStackKind.Null)
                            _asm.MovRef(dst, src);
                        else if (kind is GenStackKind.Ptr or GenStackKind.ByRef or GenStackKind.NativeInt or GenStackKind.NativeUInt || IsPointerType(type))
                            _asm.MovPtr(dst, src);
                        else
                            _asm.MovI(dst, src);
                        return;
                    }
                }
                throw new InvalidOperationException("Cannot emit register move between incompatible register classes: " +
                    MachineRegisters.Format(src) + " -> " + MachineRegisters.Format(dst) +
                    $", source class {srcRegisterClass}, destination class {dstRegisterClass}" +
                    $", value type {(type?.ToString() ?? "<unknown>")}, stack kind {kind}.");
            }
            private static Op SelectIntegerToFloatBitcastOp(RuntimeType? type, GenStackKind kind)
                => RegisterMoveBitcastSize(type, kind) <= 4 ? Op.BitcastI32F32 : Op.BitcastI64F64;
            private static Op SelectFloatToIntegerBitcastOp(RuntimeType? type, GenStackKind kind)
                => RegisterMoveBitcastSize(type, kind) <= 4 ? Op.BitcastF32I32 : Op.BitcastF64I64;
            private static int RegisterMoveBitcastSize(RuntimeType? type, GenStackKind kind)
            {
                if (type is not null && type.SizeOf > 0)
                    return type.SizeOf;
                return kind == GenStackKind.R8 || kind == GenStackKind.I8 ? 8 : 4;
            }
            private void EmitLoad(MachineRegister dst, RegisterOperand src, RuntimeType? type, GenStackKind kind)
            {
                if (!src.IsFrameSlot)
                    throw new InvalidOperationException("All post-LSRA memory operands must be finalized frame slots. Operand: " + src);
                if (src.IsAddress)
                {
                    EmitLoadAddress(dst, src);
                    return;
                }
                _asm.Emit(InstrDesc.Mem(SelectLoadOp(type, kind, src.RegisterClass, src.FrameSlotSize), dst, FrameBaseRegister(src), src.FrameOffset, aux: FrameMemoryAux(src)));
            }

            private void EmitStore(RegisterOperand dst, MachineRegister src, RuntimeType? type, GenStackKind kind)
            {
                if (!dst.IsFrameSlot)
                    throw new InvalidOperationException("All post-LSRA memory operands must be finalized frame slots. Operand: " + dst);
                _asm.Emit(InstrDesc.Mem(SelectStoreOp(type, kind, dst.RegisterClass, dst.FrameSlotSize), src, FrameBaseRegister(dst), dst.FrameOffset, aux: FrameMemoryAux(dst)));
            }

            private void EmitMemoryToMemory(RegisterOperand dst, RegisterOperand src, RuntimeType? type, GenStackKind kind)
            {
                int size = StorageSizeOf(type, kind, dst, src);
                if (IsAggregateStorage(dst, type, kind) || IsAggregateStorage(src, type, kind) || size > TargetArchitecture.GeneralRegisterSize)
                {
                    EmitAddressOf(MachineRegisters.BackendScratch, dst);
                    EmitAddressOf(MachineRegisters.ParallelCopyScratch1, src);
                    EmitCopyAddressToAddress(type, kind, MachineRegisters.BackendScratch, MachineRegisters.ParallelCopyScratch1, size, InstructionFlags.MayThrow);
                    return;
                }

                var scratch = (dst.RegisterClass == RegisterClass.Float || src.RegisterClass == RegisterClass.Float)
                    ? MachineRegisters.FloatBackendScratch
                    : MachineRegisters.BackendScratch;
                EmitLoad(scratch, src, type, kind);
                EmitStore(dst, scratch, type, kind);
            }

            private void EmitAddressOf(MachineRegister dst, RegisterOperand operand)
            {
                if (operand.IsRegister)
                {
                    _asm.MovPtr(dst, operand.Register);
                    return;
                }

                if (operand.IsFrameSlot)
                {
                    EmitLoadAddress(dst, operand);
                    return;
                }

                throw new InvalidOperationException("Cannot take address of operand: " + operand.ToString());
            }

            private void EmitInitAggregate(RegisterOperand destination, RuntimeType? type, GenStackKind kind)
            {
                int size = StorageSizeOf(type, kind, destination);
                EmitAddressOf(MachineRegisters.BackendScratch, destination);
                if (type is not null)
                {
                    _asm.Emit(new InstrDesc(Op.InitObj, RegisterVmIsa.EncodeRegister(MachineRegisters.BackendScratch),
                        aux: Aux.Instruction(AggregateWriteFlags(type, InstructionFlags.MayThrow)), imm: type.TypeId));
                    return;
                }

                _asm.Emit(new InstrDesc(Op.InitBlk, RegisterVmIsa.EncodeRegister(MachineRegisters.BackendScratch),
                    aux: Aux.Instruction(InstructionFlags.MayThrow), imm: size));
            }

            private void EmitCopyAddressToAddress(RuntimeType? type, GenStackKind kind, MachineRegister dstAddress, MachineRegister srcAddress, int size, InstructionFlags flags)
            {
                int copySize = size > 0 ? size : StorageSizeOf(type, kind);
                if (type is not null)
                {
                    _asm.Emit(new InstrDesc(Op.CpObj, RegisterVmIsa.EncodeRegister(dstAddress), RegisterVmIsa.EncodeRegister(srcAddress),
                        aux: Aux.Instruction(AggregateWriteFlags(type, flags)), imm: type.TypeId));
                    return;
                }

                _asm.Emit(new InstrDesc(Op.CpBlk, RegisterVmIsa.EncodeRegister(dstAddress), RegisterVmIsa.EncodeRegister(srcAddress),
                    aux: Aux.Instruction(flags), imm: copySize));
            }

            private void EmitStackPointerSubImm(int value)
            {
#pragma warning disable CS0162
                if (TargetArchitecture.PointerSize == 4)
                    _asm.I32SubImm(MachineRegisters.StackPointer, MachineRegisters.StackPointer, value);
                else
                    _asm.I64SubImm(MachineRegisters.StackPointer, MachineRegisters.StackPointer, value);
#pragma warning restore CS0162
            }

            private void EmitStackPointerAddImm(int value)
            {
#pragma warning disable CS0162
                if (TargetArchitecture.PointerSize == 4)
                    _asm.I32AddImm(MachineRegisters.StackPointer, MachineRegisters.StackPointer, value);
                else
                    _asm.I64AddImm(MachineRegisters.StackPointer, MachineRegisters.StackPointer, value);
#pragma warning restore CS0162
            }

            private void EmitLoadAddress(MachineRegister dst, RegisterOperand src)
            {
                if (!src.IsFrameSlot)
                    throw new InvalidOperationException("Address source must be a finalized frame slot. Operand: " + src);
#pragma warning disable CS0162
                if (TargetArchitecture.PointerSize == 4)
                    _asm.I32AddImm(dst, FrameBaseRegister(src), src.FrameOffset);
                else
                    _asm.I64AddImm(dst, FrameBaseRegister(src), src.FrameOffset);
#pragma warning restore CS0162
            }

            private MachineRegister PickScratchRegister(GenTree instruction, RegisterClass registerClass, params MachineRegister[] extraAvoid)
            {
                var internalRegisters = instruction.InternalRegisters;
                for (int i = 0; i < internalRegisters.Length; i++)
                {
                    var candidate = internalRegisters[i];
                    if (candidate.RegisterClass != registerClass)
                        continue;
                    if (IsScratchCandidateFree(instruction, candidate.Register, extraAvoid))
                        return candidate.Register;
                }

                throw Unsupported(instruction, "missing allocated internal codegen scratch register for " + registerClass.ToString());
            }

            private static bool IsScratchCandidateFree(GenTree instruction, MachineRegister candidate, MachineRegister[] extraAvoid)
            {
                if (ContainsRegister(instruction.Results, candidate) || ContainsRegister(instruction.Uses, candidate))
                    return false;

                for (int i = 0; i < extraAvoid.Length; i++)
                {
                    if (extraAvoid[i] == candidate)
                        return false;
                }

                return true;
            }

            private static bool ContainsRegister(ImmutableArray<RegisterOperand> operands, MachineRegister register)
            {
                if (operands.IsDefaultOrEmpty)
                    return false;

                for (int i = 0; i < operands.Length; i++)
                {
                    if (operands[i].IsRegister && operands[i].Register == register)
                        return true;
                }

                return false;
            }

            private void EmitLoadFromAddress(MachineRegister dst, MachineRegister address, AbiRegisterSegment segment)
            {
                var kind = StackKindForSegment(segment);
                var op = SelectLoadOp(null, kind, segment.RegisterClass, segment.Size);
                _asm.Emit(InstrDesc.Mem(op, dst, address, segment.Offset,
                    aux: Aux.Memory(0, AlignLog2(segment.Size), MemoryBase.Register)));
            }

            private void EmitStoreToAddress(MachineRegister address, AbiRegisterSegment segment, MachineRegister src)
            {
                var kind = StackKindForSegment(segment);
                var op = SelectStoreOp(null, kind, segment.RegisterClass, segment.Size);
                _asm.Emit(InstrDesc.Mem(op, src, address, segment.Offset,
                    aux: Aux.Memory(0, AlignLog2(segment.Size), MemoryBase.Register)));
            }

            private void EmitMultiRegisterLoadFromAddress(GenTree instruction, RuntimeType? type, GenStackKind kind, MachineRegister address)
            {
                var segments = RegisterSegmentsForStorage(type, kind);
                if (segments.Length <= 1 || instruction.Results.Length != segments.Length)
                    throw Unsupported(instruction, "multi-register load result count does not match ABI storage segments");

                for (int i = 0; i < segments.Length; i++)
                {
                    var dst = instruction.Results[i];
                    var segment = segments[i];
                    if (dst.IsRegister)
                    {
                        EmitLoadFromAddress(dst.Register, address, segment);
                        continue;
                    }

                    if (dst.IsFrameSlot)
                    {
                        MachineRegister scratch = PickScratchRegister(instruction, segment.RegisterClass, address);
                        EmitLoadFromAddress(scratch, address, segment);
                        EmitStore(dst, scratch, null, StackKindForSegment(segment));
                        continue;
                    }

                    throw Unsupported(instruction, "multi-register load fragment has no destination");
                }
            }

            private MachineRegister MaterializeAggregateAddress(GenTree instruction, int valueUseIndex, RuntimeType? type, GenStackKind kind, string context, params MachineRegister[] extraAvoid)
            {
                if ((uint)valueUseIndex >= (uint)instruction.Uses.Length)
                    throw Unsupported(instruction, context + " has no source operand");

                var sourceLocation = instruction.Uses[valueUseIndex];
                if (!sourceLocation.IsFrameSlot)
                    throw Unsupported(instruction, context + " requires an addressable aggregate source");

                MachineRegister address = PickScratchRegister(instruction, RegisterClass.General, extraAvoid);
                EmitAddressOf(address, sourceLocation);
                return address;
            }

            private MachineRegister MaterializeScalarAggregateHome(GenTree instruction, int valueUseIndex, RuntimeType? type, GenStackKind kind, string context, params MachineRegister[] extraAvoid)
            {
                if ((uint)valueUseIndex >= (uint)instruction.Uses.Length)
                    throw Unsupported(instruction, context + " has no source operand");

                var source = instruction.Uses[valueUseIndex];
                int size = StorageSizeOf(type, kind, source);
                if (size <= 0 || size > 8)
                    throw Unsupported(instruction, context + " cannot be materialized from one register; size=" + size.ToString());

                if (source.IsFrameSlot)
                {
                    MachineRegister address = PickScratchRegister(instruction, RegisterClass.General, extraAvoid);
                    EmitAddressOf(address, source);
                    return address;
                }

                if (!source.IsRegister)
                    throw Unsupported(instruction, context + " source is neither register nor frame slot");

                if ((uint)valueUseIndex >= (uint)instruction.RegisterUses.Length)
                    throw Unsupported(instruction, context + " has no GenTree value home");

                RegisterOperand home = _method.GetHome(instruction.RegisterUses[valueUseIndex]);
                if (!home.IsFrameSlot)
                    throw Unsupported(instruction, context + " register source has no addressable home");

                EmitRawRegisterStoreToFrame(home, source.Register, size);

                MachineRegister homeAddress = PickScratchRegister(instruction, RegisterClass.General, AppendAvoid(extraAvoid, source.Register));
                EmitAddressOf(homeAddress, home);
                return homeAddress;
            }

            private void EmitRawRegisterStoreToFrame(RegisterOperand destination, MachineRegister source, int size)
            {
                if (!destination.IsFrameSlot)
                    throw new InvalidOperationException("Raw aggregate materialization destination is not a frame slot: " + destination.ToString());

                Op op = size switch
                {
                    1 => Op.StI1,
                    2 => Op.StI2,
                    4 => Op.StI4,
                    8 => Op.StI8,
                    _ => throw new InvalidOperationException("Unsupported raw aggregate materialization size: " + size.ToString()),
                };

                _asm.Emit(InstrDesc.Mem(op, source, FrameBaseRegister(destination), destination.FrameOffset, aux: FrameMemoryAux(destination)));
            }

            private static MachineRegister[] AppendAvoid(MachineRegister[] registers, MachineRegister extra)
            {
                if (registers is null || registers.Length == 0)
                    return new[] { extra };

                var result = new MachineRegister[registers.Length + 1];
                for (int i = 0; i < registers.Length; i++)
                    result[i] = registers[i];
                result[registers.Length] = extra;
                return result;
            }

            private MachineRegister MaterializeMultiRegisterAggregateHome(GenTree instruction, int firstValueUseIndex, RuntimeType? type, GenStackKind kind, string context, params MachineRegister[] extraAvoid)
            {
                var segments = RegisterSegmentsForStorage(type, kind);
                if (segments.Length <= 1 || instruction.Uses.Length - firstValueUseIndex != segments.Length)
                    throw Unsupported(instruction, context + " source count does not match ABI storage segments");

                RegisterOperand home = AggregateHomeForFragmentedUse(instruction, firstValueUseIndex, segments.Length, context);
                if (!home.IsFrameSlot)
                    throw Unsupported(instruction, context + " requires an addressable aggregate home");

                MachineRegister address = PickScratchRegister(instruction, RegisterClass.General, extraAvoid);
                EmitAddressOf(address, home);

                EmitMultiRegisterStoreToAddress(instruction, firstValueUseIndex, type, kind, address);
                return address;
            }

            private RegisterOperand AggregateHomeForFragmentedUse(GenTree instruction, int firstValueUseIndex, int fragmentCount, string context)
            {
                if ((uint)firstValueUseIndex >= (uint)instruction.RegisterUses.Length || firstValueUseIndex + fragmentCount > instruction.RegisterUses.Length)
                    throw Unsupported(instruction, context + " has no complete GenTree value home");

                var value = instruction.RegisterUses[firstValueUseIndex];
                for (int i = 1; i < fragmentCount; i++)
                {
                    if (!instruction.RegisterUses[firstValueUseIndex + i].Equals(value))
                        throw Unsupported(instruction, context + " fragments belong to different GenTree values");
                }

                return _method.GetHome(value);
            }

            private void EmitMultiRegisterStoreToAddress(GenTree instruction, int firstValueUseIndex, RuntimeType? type, GenStackKind kind, MachineRegister address)
            {
                var segments = RegisterSegmentsForStorage(type, kind);
                if (segments.Length <= 1 || instruction.Uses.Length - firstValueUseIndex != segments.Length)
                    throw Unsupported(instruction, "multi-register store source count does not match ABI storage segments");

                for (int i = 0; i < segments.Length; i++)
                {
                    var src = instruction.Uses[firstValueUseIndex + i];
                    var segment = segments[i];
                    MachineRegister value;

                    if (src.IsRegister)
                    {
                        value = src.Register;
                    }
                    else if (src.IsFrameSlot)
                    {
                        value = PickScratchRegister(instruction, segment.RegisterClass, address);
                        EmitLoad(value, src, null, StackKindForSegment(segment));
                    }
                    else
                    {
                        throw Unsupported(instruction, "multi-register store fragment has no source");
                    }

                    EmitStoreToAddress(address, segment, value);
                }
            }

            private void EmitR(GenTree source, Op op, MachineRegister rd, MachineRegister a, MachineRegister b)
            {
                ushort aux = InstructionAux(source, op);
                if (b == MachineRegister.Invalid)
                    _asm.Emit(new InstrDesc(op, RegisterVmIsa.EncodeRegister(rd), RegisterVmIsa.EncodeRegister(a), aux: aux));
                else
                    _asm.Emit(InstrDesc.R(op, rd, a, b, aux));
            }

            private ushort InstructionAux(GenTree source, Op op)
            {
                InstructionFlags flags = InstructionFlags.None;
                if (OperationMayThrow(source, op))
                    flags |= InstructionFlags.MayThrow;
                if ((source.Flags & GenTreeFlags.Allocation) != 0 || source.ContainsCall)
                    flags |= InstructionFlags.GcSafePoint;
                return Aux.Instruction(flags);
            }

            private static bool OperationMayThrow(GenTree source, Op op)
            {
                if (op is Op.I32Div or Op.I32Rem or Op.U32Div or Op.U32Rem or
                    Op.I64Div or Op.I64Rem or Op.U64Div or Op.U64Rem)
                    return true;

                if (op is Op.I32AddOvf or Op.I32SubOvf or Op.I32MulOvf or
                    Op.U32AddOvf or Op.U32SubOvf or Op.U32MulOvf or
                    Op.I64AddOvf or Op.I64SubOvf or Op.I64MulOvf or
                    Op.U64AddOvf or Op.U64SubOvf or Op.U64MulOvf or
                    Op.I64ToI32Ovf or Op.U64ToI32Ovf or
                    Op.I64ToU32Ovf or Op.U64ToU32Ovf or
                    Op.I32ToI8Ovf or Op.U32ToI8Ovf or Op.I32ToU8Ovf or Op.U32ToU8Ovf or
                    Op.I32ToI16Ovf or Op.U32ToI16Ovf or Op.I32ToU16Ovf or Op.U32ToU16Ovf or
                    Op.F32ToI32Ovf or Op.F32ToI64Ovf or
                    Op.F64ToI32Ovf or Op.F64ToI64Ovf)
                    return true;

                return source.Kind is GenTreeKind.Conv or GenTreeKind.CastClass or GenTreeKind.UnboxAny
                    && source.CanThrow;
            }

            private Label LabelForTarget(GenTree source)
            {
                if ((uint)source.TargetBlockId >= (uint)_blockLabels.Length)
                    throw new InvalidOperationException($"Invalid branch target block B{source.TargetBlockId}.");
                return _blockLabels[source.TargetBlockId];
            }

            private RegisterOperand RequireSingleResult(GenTree instruction)
            {
                if (instruction.Results.Length != 1)
                    throw Unsupported(instruction, $"instruction must have exactly one result, actual count: {instruction.Results.Length}");
                return instruction.Results[0];
            }

            private static GenTree? SingleRegisterResultOrNull(GenTree instruction)
            {
                return instruction.RegisterResults.Length == 1 ? instruction.RegisterResults[0] : null;
            }

            private MachineRegister RequireResultRegister(GenTree instruction)
            {
                var result = RequireSingleResult(instruction);
                if (!result.IsRegister)
                    throw Unsupported(instruction, $"instruction result is not a register: {result}");
                return result.Register;
            }

            private MachineRegister RequireUseRegister(GenTree instruction, int index)
            {
                if ((uint)index >= (uint)instruction.Uses.Length)
                    throw Unsupported(instruction, "missing use operand " + index);
                if (!instruction.Uses[index].IsRegister)
                    throw Unsupported(instruction, $"use operand is not a register: {instruction.Uses[index]}");
                return instruction.Uses[index].Register;
            }

            private RuntimeType RequireRuntimeType(GenTree source)
                => source.RuntimeType ?? source.Type ?? throw new InvalidOperationException($"GenTree has no runtime type: {source}");

            private Exception Unsupported(GenTree instruction, string message)
                => new NotSupportedException($"Codegen: {message} at method {_method.RuntimeMethod.MethodId}, " +
                    $"B{instruction.BlockId}:{instruction.Ordinal}, node {ExecutionOrderNodeId(instruction)}.");

            private RuntimeType? TypeForMove(GenTree instruction)
            {
                RuntimeType? type = null;
                GenTree? linearResult = SingleRegisterResultOrNull(instruction);
                if (linearResult is not null)
                    type = _method.GetValueInfo(linearResult).Type;
                else if (instruction.RegisterUses.Length != 0)
                    type = _method.GetValueInfo(instruction.RegisterUses[0]).Type;

                if (type is not null && type.IsValueType && !IsScalarValueType(type) && CanScalarizeAggregateMove(instruction, type))
                    return null;

                return type;
            }

            private static RuntimeType? ScalarizedMoveType(RuntimeType? type, RegisterOperand destination, RegisterOperand source)
            {
                if (type is not null && type.IsValueType && !IsScalarValueType(type))
                {
                    int size = MaxKnownOperandSize(destination, source);
                    if (CanRepresentAsSingleRegister(size, type))
                        return null;
                }
                return type;
            }

            private static bool CanScalarizeAggregateMove(GenTree instruction, RuntimeType type)
            {
                if (instruction.MoveKind == MoveKind.None)
                    return false;

                int size = MaxKnownOperandSize(instruction.Results[0], instruction.Uses[0]);
                return CanRepresentAsSingleRegister(size, type);
            }

            private static bool CanRepresentAsSingleRegister(int knownOperandSize, RuntimeType type)
            {
                int size = knownOperandSize > 0 ? knownOperandSize : type.SizeOf;
                return size > 0 && size <= TargetArchitecture.GeneralRegisterSize;
            }

            private static bool IsAggregateValue(RuntimeType? type, GenStackKind kind)
                => kind == GenStackKind.Value || (type is not null && type.IsValueType && !IsScalarValueType(type));

            private static bool IsAggregateStorage(RegisterOperand operand, RuntimeType? type, GenStackKind kind)
            {
                if (IsAggregateValue(type, kind))
                    return operand.IsFrameSlot || operand.FrameSlotSize > TargetArchitecture.GeneralRegisterSize;
                return operand.FrameSlotSize > TargetArchitecture.GeneralRegisterSize;
            }

            private static int StorageSizeOf(RuntimeType? type, GenStackKind kind)
            {
                if (type is not null && type.SizeOf > 0)
                    return type.SizeOf;
                return kind switch
                {
                    GenStackKind.I8 or GenStackKind.R8 => 8,
                    GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ref or GenStackKind.Null or GenStackKind.Ptr or GenStackKind.ByRef => TargetArchitecture.PointerSize,
                    GenStackKind.Void => 0,
                    _ => 4,
                };
            }

            private static int StorageSizeOf(RuntimeType? type, GenStackKind kind, RegisterOperand operand)
            {
                int typeSize = StorageSizeOf(type, kind);
                return Math.Max(typeSize, operand.FrameSlotSize);
            }

            private static int StorageSizeOf(RuntimeType? type, GenStackKind kind, RegisterOperand a, RegisterOperand b)
            {
                int typeSize = StorageSizeOf(type, kind);
                return Math.Max(typeSize, Math.Max(a.FrameSlotSize, b.FrameSlotSize));
            }

            private static InstructionFlags AggregateWriteFlags(RuntimeType type, InstructionFlags flags)
            {
                if (type.IsReferenceType || type.ContainsGcPointers)
                    flags |= InstructionFlags.WriteBarrier;
                return flags;
            }

            private static int MaxKnownOperandSize(RegisterOperand a, RegisterOperand b)
            {
                int size = 0;
                if (a.FrameSlotSize > size) size = a.FrameSlotSize;
                if (b.FrameSlotSize > size) size = b.FrameSlotSize;
                return size;
            }

            private GenStackKind StackKindForMove(GenTree instruction)
            {
                GenTree? linearResult = SingleRegisterResultOrNull(instruction);
                if (linearResult is not null)
                    return _method.GetValueInfo(linearResult).StackKind;
                if (instruction.RegisterUses.Length != 0)
                    return _method.GetValueInfo(instruction.RegisterUses[0]).StackKind;
                if ((instruction.Results.Length == 1 && instruction.Results[0].RegisterClass == RegisterClass.Float) ||
                    (instruction.Uses.Length != 0 && instruction.Uses[0].RegisterClass == RegisterClass.Float))
                    return GenStackKind.R8;
                return GenStackKind.I8;
            }

            private RuntimeType? OperandType(GenTree instruction, GenTree source, int operandIndex)
            {
                if (TryGetCodegenUseIndexForOperand(instruction, source, operandIndex, out int useIndex) &&
                    (uint)useIndex < (uint)instruction.RegisterUses.Length)
                {
                    return _method.GetValueInfo(instruction.RegisterUses[useIndex]).Type;
                }

                if ((uint)operandIndex < (uint)source.Operands.Length)
                    return source.Operands[operandIndex].Type;
                return source.Type;
            }

            private RuntimeType? LocalLikeStorageType(GenTree instruction, GenTree source, int operandIndex)
            {
                var descriptorType = source.LocalDescriptor?.Type;
                if (descriptorType is not null)
                    return descriptorType;

                return OperandType(instruction, source, operandIndex) ?? source.RuntimeType ?? source.Type;
            }

            private GenStackKind LocalLikeStorageKind(GenTree instruction, GenTree source, RuntimeType? storageType, int operandIndex)
            {
                if (source.LocalDescriptor is not null)
                    return source.LocalDescriptor.StackKind;

                return storageType is not null ? StackKindOf(storageType) : OperandStackKind(instruction, source, operandIndex);
            }

            private RuntimeType? StoreTargetType(GenTree instruction, GenTree source, int operandIndex)
            {
                var fieldType = source.Field?.FieldType;
                if (fieldType is not null)
                    return fieldType;

                return source.RuntimeType ?? source.Type ?? OperandType(instruction, source, operandIndex);
            }

            private GenStackKind StoreTargetKind(GenTree instruction, GenTree source, RuntimeType? storageType, int operandIndex)
            {
                return storageType is not null ? StackKindOf(storageType) : OperandStackKind(instruction, source, operandIndex);
            }

            private int RequireCodegenUseIndexForOperand(GenTree instruction, GenTree source, int operandIndex, string context)
            {
                if (TryGetCodegenUseIndexForOperand(instruction, source, operandIndex, out int useIndex) &&
                    (uint)useIndex < (uint)instruction.Uses.Length)
                {
                    return useIndex;
                }

                throw Unsupported(instruction, context + " has no register use for operand " + operandIndex.ToString());
            }

            private bool TryGetCodegenUseIndexForOperand(GenTree instruction, GenTree source, int operandIndex, out int useIndex)
            {
                useIndex = -1;
                if (operandIndex < 0)
                    return false;

                if (source.Operands.IsDefaultOrEmpty)
                {
                    if ((uint)operandIndex < (uint)instruction.RegisterUses.Length &&
                        (uint)operandIndex < (uint)instruction.Uses.Length)
                    {
                        useIndex = operandIndex;
                        return true;
                    }

                    return false;
                }

                if ((uint)operandIndex >= (uint)source.Operands.Length)
                    return false;

                int flagCount = source.OperandFlags.IsDefaultOrEmpty ? 0 : source.OperandFlags.Length;
                int codegenUseCursor = 0;

                for (int i = 0; i < source.Operands.Length; i++)
                {
                    var flags = i < flagCount ? source.OperandFlags[i] : LirOperandFlags.None;
                    if ((flags & LirOperandFlags.Contained) != 0)
                        continue;

                    var operand = source.Operands[i];
                    if (!TryGetOperandValueKey(operand, out var operandValueKey))
                        continue;

                    int slot = FindCodegenUseSlot(instruction, operandValueKey, codegenUseCursor);
                    if (slot < 0)
                    {
                        if (i == operandIndex)
                            return false;

                        continue;
                    }

                    if (i == operandIndex)
                    {
                        useIndex = slot;
                        return true;
                    }

                    int operandSlotCount = CodegenUseSlotCountForOperand(operand);
                    codegenUseCursor = slot + operandSlotCount;
                }

                return false;
            }

            private bool TryGetOperandValueKey(GenTree operand, out GenTreeValueKey key)
            {
                GenTree value = operand.RegisterResult ?? operand;
                key = ValueKeyForNode(value);

                if (_method.ValueInfoByNode.ContainsKey(key))
                    return true;

                return false;
            }

            private static GenTreeValueKey ValueKeyForNode(GenTree value)
                => value.LinearValueKey;

            private static int FindCodegenUseSlot(GenTree instruction, GenTreeValueKey operandValueKey, int startIndex)
            {
                var useValues = instruction.LsraInfo.CodegenUseValues;
                for (int i = Math.Max(0, startIndex); i < useValues.Length && i < instruction.Uses.Length; i++)
                {
                    if (useValues[i].Equals(operandValueKey))
                        return i;
                }

                return -1;
            }

            private int CodegenUseSlotCountForOperand(GenTree operand)
            {
                GenTree value = operand.RegisterResult ?? operand;
                var key = ValueKeyForNode(value);
                if (_method.ValueInfoByNode.TryGetValue(key, out var info))
                {
                    var abi = MachineAbi.ClassifyStorageValue(info.Type, info.StackKind);
                    return abi.PassingKind == AbiValuePassingKind.MultiRegister
                        ? MachineAbi.GetRegisterSegments(abi).Length
                        : 1;
                }

                return 1;
            }

            private GenStackKind OperandStackKind(GenTree instruction, GenTree source, int operandIndex)
            {
                if (TryGetCodegenUseIndexForOperand(instruction, source, operandIndex, out int useIndex) &&
                    (uint)useIndex < (uint)instruction.RegisterUses.Length)
                {
                    return _method.GetValueInfo(instruction.RegisterUses[useIndex]).StackKind;
                }

                if ((uint)operandIndex < (uint)source.Operands.Length)
                    return source.Operands[operandIndex].StackKind;
                return source.StackKind;
            }

            private GenStackKind RegisterClassKind(MachineRegister register)
                => MachineRegisters.GetClass(register) == RegisterClass.Float ? GenStackKind.R8 : GenStackKind.I8;

            private static MemoryBase FrameMemoryBase(RegisterOperand operand)
                => operand.FrameBase switch
                {
                    RegisterFrameBase.FramePointer => MemoryBase.FramePointer,
                    RegisterFrameBase.StackPointer => MemoryBase.StackPointer,
                    RegisterFrameBase.IncomingArgumentBase => MemoryBase.ThreadPointer,
                    _ => throw new InvalidOperationException("Invalid frame base: " + operand.FrameBase.ToString()),
                };

            private static MachineRegister FrameBaseRegister(RegisterOperand operand)
                => operand.FrameBase switch
                {
                    RegisterFrameBase.FramePointer => MachineRegisters.FramePointer,
                    RegisterFrameBase.StackPointer => MachineRegisters.StackPointer,
                    RegisterFrameBase.IncomingArgumentBase => MachineRegisters.ThreadPointer,
                    _ => throw new InvalidOperationException("Invalid frame base: " + operand.FrameBase.ToString()),
                };

            private static ushort FrameMemoryAux(RegisterOperand operand)
                => Aux.Memory(0, AlignLog2(operand.FrameSlotSize), FrameMemoryBase(operand));

            private static ushort MemoryAuxForRegisterBase(RuntimeType? type)
                => Aux.Memory(0, AlignLog2(type?.AlignOf ?? 1), MemoryBase.Register);

            private static byte AlignLog2(int sizeOrAlign)
            {
                int value = sizeOrAlign <= 0 ? 1 : sizeOrAlign;
                int log = 0;
                while ((1 << log) < value && log < 15)
                    log++;
                return (byte)log;
            }

            private static bool IsPointerType(RuntimeType? type)
                => type?.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef;

            private static bool IsFloat32Value(RuntimeType? type, GenStackKind kind)
                => kind == GenStackKind.R4 || type?.Name == "Single";

            private static bool IsFloat64Value(RuntimeType? type, GenStackKind kind)
                => kind == GenStackKind.R8 || type?.Name == "Double";

            private static bool IsFloat32(RuntimeType? type, GenStackKind kind, RegisterClass registerClass)
                => registerClass == RegisterClass.Float && IsFloat32Value(type, kind);

            private static bool IsFloat64(RuntimeType? type, GenStackKind kind, RegisterClass registerClass)
                => registerClass == RegisterClass.Float && IsFloat64Value(type, kind);

            private static bool Is64BitInteger(RuntimeType? type, GenStackKind kind)
                => kind == GenStackKind.I8 || type?.Name is "Int64" or "UInt64";

            private static bool IsUnsignedInteger(RuntimeType? type, GenStackKind kind)
                => kind == GenStackKind.NativeUInt || type?.Name is "Byte" or "UInt16" or "UInt32" or "UInt64" or "UIntPtr" or "Char" or "Boolean";

            private static RegisterClass RegisterClassForStorage(RuntimeType? type, GenStackKind kind)
            {
                var abi = MachineAbi.ClassifyStorageValue(type, kind);
                if (abi.RegisterClass != RegisterClass.Invalid)
                    return abi.RegisterClass;
                return kind is GenStackKind.R4 or GenStackKind.R8 ? RegisterClass.Float : RegisterClass.General;
            }

            private static ImmutableArray<AbiRegisterSegment> RegisterSegmentsForStorage(RuntimeType? type, GenStackKind kind)
            {
                var abi = MachineAbi.ClassifyStorageValue(type, kind);
                return MachineAbi.GetRegisterSegments(abi);
            }

            private static GenStackKind StackKindForSegment(AbiRegisterSegment segment)
            {
                if (segment.ContainsGcPointers)
                    return GenStackKind.Ref;
                if (segment.RegisterClass == RegisterClass.Float)
                    return segment.Size <= 4 ? GenStackKind.R4 : GenStackKind.R8;
                return segment.Size > 4 ? GenStackKind.I8 : GenStackKind.I4;
            }

            private static RegisterOperand FrameSlotFragment(RegisterOperand slot, AbiRegisterSegment segment)
            {
                if (!slot.IsFrameSlot)
                    throw new InvalidOperationException("ABI fragment source is not a finalized frame slot: " + slot);

                return RegisterOperand.ForFrameSlot(
                    segment.RegisterClass,
                    slot.FrameSlotKind,
                    slot.FrameBase,
                    slot.FrameSlotIndex,
                    checked(slot.FrameOffset + segment.Offset),
                    segment.Size,
                    slot.IsAddress);
            }

            private static Op SelectLoadOpForStorage(RuntimeType? type, GenStackKind kind, RegisterClass registerClass = RegisterClass.Invalid, int storageSize = 0)
            {
                NormalizeScalarizedStorageAccess(type, kind, ref registerClass, ref storageSize, out RuntimeType? accessType, out GenStackKind accessKind);
                return SelectLoadOp(accessType, accessKind, registerClass, storageSize);
            }

            private static Op SelectStoreOpForStorage(RuntimeType? type, GenStackKind kind, RegisterClass registerClass = RegisterClass.Invalid, int storageSize = 0)
            {
                NormalizeScalarizedStorageAccess(type, kind, ref registerClass, ref storageSize, out RuntimeType? accessType, out GenStackKind accessKind);
                return SelectStoreOp(accessType, accessKind, registerClass, storageSize);
            }

            private static void NormalizeScalarizedStorageAccess(
                RuntimeType? type,
                GenStackKind kind,
                ref RegisterClass registerClass,
                ref int storageSize,
                out RuntimeType? accessType,
                out GenStackKind accessKind)
            {
                accessType = type;
                accessKind = kind;

                if (type is null || !type.IsValueType)
                    return;

                if (IsScalarValueType(type))
                {
                    if (storageSize <= 0)
                        storageSize = Math.Max(1, type.SizeOf);

                    if (registerClass == RegisterClass.Invalid)
                        registerClass = RegisterClassForStorage(type, kind);

                    return;
                }

                var abi = MachineAbi.ClassifyStorageValue(type, kind);
                if (abi.PassingKind != AbiValuePassingKind.ScalarRegister)
                    return;

                accessType = null;
                registerClass = abi.RegisterClass == RegisterClass.Invalid ? registerClass : abi.RegisterClass;
                storageSize = abi.Size > 0 ? abi.Size : storageSize;

                if (abi.ContainsGcPointers)
                    accessKind = GenStackKind.Ref;
                else if (registerClass == RegisterClass.Float)
                    accessKind = storageSize <= 4 ? GenStackKind.R4 : GenStackKind.R8;
                else
                    accessKind = storageSize > 4 ? GenStackKind.I8 : GenStackKind.I4;
            }

            private static Op SelectLoadOp(RuntimeType? type, GenStackKind kind, RegisterClass registerClass = RegisterClass.Invalid, int storageSize = 0)
            {
                if (type is not null && type.IsValueType && !IsScalarValueType(type))
                    return Op.LdObj;
                if (registerClass == RegisterClass.Float || kind is GenStackKind.R4 or GenStackKind.R8)
                    return kind == GenStackKind.R4 || type?.Name == "Single" || storageSize == 4 ? Op.LdF32 : Op.LdF64;
                if (kind is GenStackKind.Ref or GenStackKind.Null || (type?.IsReferenceType ?? false))
                    return Op.LdRef;
                if (kind is GenStackKind.Ptr or GenStackKind.ByRef || IsPointerType(type))
                    return Op.LdPtr;
                if (kind is GenStackKind.NativeInt or GenStackKind.NativeUInt)
                    return Op.LdN;

                return type?.Name switch
                {
                    "SByte" => Op.LdI1,
                    "Byte" or "Boolean" => Op.LdU1,
                    "Int16" => Op.LdI2,
                    "UInt16" or "Char" => Op.LdU2,
                    "UInt32" => Op.LdU4,
                    "Int64" or "UInt64" => Op.LdI8,
                    _ => storageSize switch
                    {
                        1 => Op.LdU1,
                        2 => Op.LdU2,
                        8 => Op.LdI8,
                        _ => Op.LdI4,
                    },
                };
            }

            private static Op SelectStoreOp(RuntimeType? type, GenStackKind kind, RegisterClass registerClass = RegisterClass.Invalid, int storageSize = 0)
            {
                if (type is not null && type.IsValueType && !IsScalarValueType(type))
                    return Op.StObj;
                if (registerClass == RegisterClass.Float || kind is GenStackKind.R4 or GenStackKind.R8)
                    return kind == GenStackKind.R4 || type?.Name == "Single" || storageSize == 4 ? Op.StF32 : Op.StF64;
                if (kind is GenStackKind.Ref or GenStackKind.Null || (type?.IsReferenceType ?? false))
                    return Op.StRef;
                if (kind is GenStackKind.Ptr or GenStackKind.ByRef || IsPointerType(type))
                    return Op.StPtr;
                if (kind is GenStackKind.NativeInt or GenStackKind.NativeUInt)
                    return Op.StN;
                return type?.Name switch
                {
                    "SByte" or "Byte" or "Boolean" => Op.StI1,
                    "Int16" or "UInt16" or "Char" => Op.StI2,
                    "Int64" or "UInt64" => Op.StI8,
                    _ => storageSize switch
                    {
                        1 => Op.StI1,
                        2 => Op.StI2,
                        8 => Op.StI8,
                        _ => Op.StI4,
                    },
                };
            }

            private static Op SelectLoadFieldOp(RuntimeType type)
                => SelectLoadFieldOp(type, StackKindOf(type), RegisterClassForStorage(type, StackKindOf(type)), type.SizeOf);

            private static Op SelectLoadFieldOp(RuntimeType type, GenStackKind kind, RegisterClass registerClass, int storageSize) =>
                SelectLoadOpForStorage(type, kind, registerClass, storageSize) switch
                {
                    Op.LdI1 => Op.LdFldI1,
                    Op.LdU1 => Op.LdFldU1,
                    Op.LdI2 => Op.LdFldI2,
                    Op.LdU2 => Op.LdFldU2,
                    Op.LdI4 => Op.LdFldI4,
                    Op.LdU4 => Op.LdFldU4,
                    Op.LdI8 => Op.LdFldI8,
                    Op.LdN => Op.LdFldN,
                    Op.LdF32 => Op.LdFldF32,
                    Op.LdF64 => Op.LdFldF64,
                    Op.LdRef => Op.LdFldRef,
                    Op.LdPtr => Op.LdFldPtr,
                    _ => Op.LdFldObj,
                };

            private static Op SelectStoreFieldOp(RuntimeType type)
                => SelectStoreFieldOp(type, StackKindOf(type), RegisterClassForStorage(type, StackKindOf(type)), type.SizeOf);

            private static Op SelectStoreFieldOp(RuntimeType type, GenStackKind kind, RegisterClass registerClass, int storageSize) =>
                SelectStoreOpForStorage(type, kind, registerClass, storageSize) switch
                {
                    Op.StI1 => Op.StFldI1,
                    Op.StI2 => Op.StFldI2,
                    Op.StI4 => Op.StFldI4,
                    Op.StI8 => Op.StFldI8,
                    Op.StN => Op.StFldN,
                    Op.StF32 => Op.StFldF32,
                    Op.StF64 => Op.StFldF64,
                    Op.StRef => Op.StFldRef,
                    Op.StPtr => Op.StFldPtr,
                    _ => Op.StFldObj,
                };

            private static Op SelectLoadStaticFieldOp(RuntimeType type)
                => SelectLoadStaticFieldOp(type, StackKindOf(type), RegisterClassForStorage(type, StackKindOf(type)), type.SizeOf);

            private static Op SelectLoadStaticFieldOp(RuntimeType type, GenStackKind kind, RegisterClass registerClass, int storageSize) =>
                SelectLoadFieldOp(type, kind, registerClass, storageSize) switch
                {
                    Op.LdFldI1 => Op.LdSFldI1,
                    Op.LdFldU1 => Op.LdSFldU1,
                    Op.LdFldI2 => Op.LdSFldI2,
                    Op.LdFldU2 => Op.LdSFldU2,
                    Op.LdFldI4 => Op.LdSFldI4,
                    Op.LdFldU4 => Op.LdSFldU4,
                    Op.LdFldI8 => Op.LdSFldI8,
                    Op.LdFldN => Op.LdSFldN,
                    Op.LdFldF32 => Op.LdSFldF32,
                    Op.LdFldF64 => Op.LdSFldF64,
                    Op.LdFldRef => Op.LdSFldRef,
                    Op.LdFldPtr => Op.LdSFldPtr,
                    _ => Op.LdSFldObj,
                };

            private static Op SelectStoreStaticFieldOp(RuntimeType type)
                => SelectStoreStaticFieldOp(type, StackKindOf(type), RegisterClassForStorage(type, StackKindOf(type)), type.SizeOf);

            private static Op SelectStoreStaticFieldOp(RuntimeType type, GenStackKind kind, RegisterClass registerClass, int storageSize) =>
                SelectStoreFieldOp(type, kind, registerClass, storageSize) switch
                {
                    Op.StFldI1 => Op.StSFldI1,
                    Op.StFldI2 => Op.StSFldI2,
                    Op.StFldI4 => Op.StSFldI4,
                    Op.StFldI8 => Op.StSFldI8,
                    Op.StFldN => Op.StSFldN,
                    Op.StFldF32 => Op.StSFldF32,
                    Op.StFldF64 => Op.StSFldF64,
                    Op.StFldRef => Op.StSFldRef,
                    Op.StFldPtr => Op.StSFldPtr,
                    _ => Op.StSFldObj,
                };

            private static Op SelectLoadElementOp(RuntimeType? type, GenStackKind kind)
                => SelectLoadElementOp(type, kind, RegisterClassForStorage(type, kind), type?.SizeOf ?? 0);

            private static Op SelectLoadElementOp(RuntimeType? type, GenStackKind kind, RegisterClass registerClass, int storageSize) =>
                SelectLoadOpForStorage(type, kind, registerClass, storageSize) switch
                {
                    Op.LdI1 => Op.LdElemI1,
                    Op.LdU1 => Op.LdElemU1,
                    Op.LdI2 => Op.LdElemI2,
                    Op.LdU2 => Op.LdElemU2,
                    Op.LdI4 => Op.LdElemI4,
                    Op.LdU4 => Op.LdElemU4,
                    Op.LdI8 => Op.LdElemI8,
                    Op.LdN => Op.LdElemN,
                    Op.LdF32 => Op.LdElemF32,
                    Op.LdF64 => Op.LdElemF64,
                    Op.LdRef => Op.LdElemRef,
                    Op.LdPtr => Op.LdElemPtr,
                    _ => Op.LdElemObj,
                };

            private static Op SelectStoreElementOp(RuntimeType? type, GenStackKind kind)
                => SelectStoreElementOp(type, kind, RegisterClassForStorage(type, kind), type?.SizeOf ?? 0);

            private static Op SelectStoreElementOp(RuntimeType? type, GenStackKind kind, RegisterClass registerClass, int storageSize) =>
                SelectStoreOpForStorage(type, kind, registerClass, storageSize) switch
                {
                    Op.StI1 => Op.StElemI1,
                    Op.StI2 => Op.StElemI2,
                    Op.StI4 => Op.StElemI4,
                    Op.StI8 => Op.StElemI8,
                    Op.StN => Op.StElemN,
                    Op.StF32 => Op.StElemF32,
                    Op.StF64 => Op.StElemF64,
                    Op.StRef => Op.StElemRef,
                    Op.StPtr => Op.StElemPtr,
                    _ => Op.StElemObj,
                };

            private static ushort ElementStoreAux(RuntimeType? type)
                => type is not null && (type.IsReferenceType || type.ContainsGcPointers)
                    ? Aux.Instruction(InstructionFlags.MayThrow | InstructionFlags.WriteBarrier)
                    : Aux.Instruction(InstructionFlags.MayThrow);

            private static ushort FieldStoreAux(RuntimeType type)
                => type.ContainsGcPointers || type.IsReferenceType
                    ? Aux.Instruction(InstructionFlags.WriteBarrier | InstructionFlags.MayThrow)
                    : Aux.Instruction(InstructionFlags.MayThrow);

            private static ushort StaticFieldStoreAux(RuntimeType type)
                => type.ContainsGcPointers || type.IsReferenceType
                    ? Aux.Instruction(InstructionFlags.WriteBarrier)
                    : (ushort)0;

            private static GenStackKind StackKindOf(RuntimeType type)
            {
                if (type.IsReferenceType) return GenStackKind.Ref;
                if (type.Kind == RuntimeTypeKind.Pointer) return GenStackKind.Ptr;
                if (type.Kind == RuntimeTypeKind.ByRef) return GenStackKind.ByRef;
                return type.Name switch
                {
                    "Single" => GenStackKind.R4,
                    "Double" => GenStackKind.R8,
                    "Int64" or "UInt64" => GenStackKind.I8,
                    "IntPtr" => GenStackKind.NativeInt,
                    "UIntPtr" => GenStackKind.NativeUInt,
                    _ => GenStackKind.I4,
                };
            }

            private static Op SelectCallOp(GenTreeKind treeKind, RuntimeMethod method, RuntimeType returnType, RegisterClass resultClass)
            {
                var returnKind = MachineAbi.StackKindForType(returnType);
                var returnAbi = MachineAbi.ClassifyValue(returnType, returnKind, isReturn: true);
                bool isVoid = returnAbi.PassingKind == AbiValuePassingKind.Void;
                bool isValue = returnAbi.PassingKind is AbiValuePassingKind.MultiRegister or AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect;
                bool isFloat = !isValue && (returnAbi.RegisterClass == RegisterClass.Float || resultClass == RegisterClass.Float || returnType.Name is "Single" or "Double");
                bool isRef = !isValue && (returnAbi.ContainsGcPointers || returnType.IsReferenceType);
                bool internalCall = method.HasInternalCall;
                bool virt = treeKind == GenTreeKind.VirtualCall;
                bool iface = virt && method.DeclaringType.Kind == RuntimeTypeKind.Interface;

                if (iface)
                {
                    if (isVoid) return Op.CallIfaceVoid;
                    if (isValue) return Op.CallIfaceValue;
                    if (isFloat) return Op.CallIfaceF;
                    if (isRef) return Op.CallIfaceRef;
                    return Op.CallIfaceI;
                }

                if (virt)
                {
                    if (isVoid) return Op.CallVirtVoid;
                    if (isValue) return Op.CallVirtValue;
                    if (isFloat) return Op.CallVirtF;
                    if (isRef) return Op.CallVirtRef;
                    return Op.CallVirtI;
                }

                if (internalCall)
                {
                    if (isVoid) return Op.CallInternalVoid;
                    if (isValue) return Op.CallInternalValue;
                    if (isFloat) return Op.CallInternalF;
                    if (isRef) return Op.CallInternalRef;
                    return Op.CallInternalI;
                }

                if (isVoid) return Op.CallVoid;
                if (isValue) return Op.CallValue;
                if (isFloat) return Op.CallF;
                if (isRef) return Op.CallRef;
                return Op.CallI;
            }

            private static bool IsScalarValueType(RuntimeType type)
            {
                if (!type.IsValueType)
                    return false;

                if (type.Kind == RuntimeTypeKind.Enum)
                    return true;

                if (!string.Equals(type.Namespace, "System", StringComparison.Ordinal))
                    return false;

                return type.Name is
                    "Boolean" or "Char" or
                    "SByte" or "Byte" or
                    "Int16" or "UInt16" or
                    "Int32" or "UInt32" or
                    "Int64" or "UInt64" or
                    "IntPtr" or "UIntPtr" or
                    "Single" or "Double" or "Half";
            }

            private static CallFlags BuildCallFlags(RuntimeMethod method, Op op)
            {
                var flags = CallFlags.GcSafePoint | CallFlags.MayThrow;
                if (method.HasInternalCall || op is Op.CallInternalVoid or Op.CallInternalI or Op.CallInternalF or Op.CallInternalRef or Op.CallInternalValue)
                    flags |= CallFlags.InternalCall;
                if ((op is Op.CallValue or Op.CallVirtValue or Op.CallIfaceValue or Op.CallInternalValue or Op.CallIndirectValue or Op.DelegateInvokeValue) &&
                    MachineAbi.RequiresHiddenReturnBuffer(method))
                    flags |= CallFlags.HiddenReturnBuffer;
                if (op is Op.CallVirtVoid or Op.CallVirtI or Op.CallVirtF or Op.CallVirtRef or Op.CallVirtValue)
                    flags |= CallFlags.VirtualDispatch;
                if (op is Op.CallIfaceVoid or Op.CallIfaceI or Op.CallIfaceF or Op.CallIfaceRef or Op.CallIfaceValue)
                    flags |= CallFlags.InterfaceDispatch;
                return flags;
            }

            private bool ShouldEmitGcReportPoint(GenTree instruction)
            {
                if (IsGcSafePoint(instruction))
                    return true;

                if (!_nodePositions.TryGetValue(instruction.GenTreeLinearId, out int position))
                    return false;

                for (int i = 0; i < _method.GcInterruptibleRanges.Length; i++)
                {
                    var range = _method.GcInterruptibleRanges[i];
                    if (range.StartPosition <= position && position < range.EndPosition)
                        return true;
                }

                return false;
            }

            private bool IsGcSafePoint(GenTree instruction)
            {
                if (instruction.TreeKind == GenTreeKind.GcPoll)
                    return true;
                if (!GenTreeLirKinds.IsRealTree(instruction))
                    return false;
                var source = instruction;
                if ((source.Flags & (GenTreeFlags.ContainsCall | GenTreeFlags.Allocation)) != 0)
                    return true;
                return instruction.TreeKind is
                    GenTreeKind.Call or
                    GenTreeKind.VirtualCall or
                    GenTreeKind.NewObject or
                    GenTreeKind.NewArray or
                    GenTreeKind.Box or
                    GenTreeKind.UnboxAny or
                    GenTreeKind.CastClass or
                    GenTreeKind.IsInst or
                    GenTreeKind.ConstString or
                    GenTreeKind.Throw or
                    GenTreeKind.Rethrow;
            }

            private void EmitGcSafePoint(int pc, GenTree instruction)
            {
                if (!_emittedGcSafePointPcs.Add(pc))
                    return;

                if (!_nodePositions.TryGetValue(instruction.GenTreeLinearId, out int position))
                {
                    if (!_blockStartPositions.TryGetValue(instruction.BlockId, out position))
                        position = 0;
                }

                int rootStart = _state.GcRootCount;
                var roots = new List<GcRootRecord>();
                for (int i = 0; i < _method.GcLiveRanges.Length; i++)
                {
                    var range = _method.GcLiveRanges[i];
                    if (range.StartPosition <= position && position < range.EndPosition)
                    {
                        var record = ToGcRootRecord(range);
                        if (!ContainsSameGcRootCell(roots, record))
                            roots.Add(record);
                    }
                }


                for (int i = 0; i < roots.Count; i++)
                {
                    _asm.AddGcRoot(roots[i]);
                    _state.GcRootCount++;
                }

                _asm.AddGcSafePoint(new GcSafePointRecord(pc, rootStart, roots.Count));
            }

            private static bool ContainsSameGcRootCell(List<GcRootRecord> roots, GcRootRecord candidate)
            {
                for (int i = 0; i < roots.Count; i++)
                {
                    if (SameGcRootCell(roots[i], candidate))
                        return true;
                }

                return false;
            }

            private static bool SameGcRootCell(GcRootRecord left, GcRootRecord right)
            {
                return left.Kind == right.Kind &&
                       left.Register == right.Register &&
                       left.FrameBase == right.FrameBase &&
                       left.FrameOffset == right.FrameOffset &&
                       left.Size == right.Size &&
                       left.CellOffset == right.CellOffset;
            }

            private static GcRootRecord ToGcRootRecord(RegisterGcLiveRange range)
                => ToGcRootRecord(range.Root, range.Flags);

            private static GcRootRecord ToGcRootRecord(RegisterGcLiveRoot root, RegisterGcLiveRangeFlags rangeFlags)
            {
                var loc = root.Location;
                var flags = GcRootFlags.None;
                if ((rangeFlags & RegisterGcLiveRangeFlags.Pinned) != 0) flags |= GcRootFlags.Pinned;
                if ((rangeFlags & RegisterGcLiveRangeFlags.ReportOnlyInLeafFunclet) != 0) flags |= GcRootFlags.ReportOnlyInLeafFunclet;
                if ((rangeFlags & RegisterGcLiveRangeFlags.SharedWithParentFrame) != 0) flags |= GcRootFlags.SharedWithParentFrame;

                GcRootKind kind;
                MachineRegister reg = MachineRegister.Invalid;
                RegisterFrameBase frameBase = RegisterFrameBase.None;
                int frameOffset = -1;
                if (loc.IsRegister)
                {
                    reg = loc.Register;
                    kind = root.RootKind switch
                    {
                        RegisterGcRootKind.ByRef => GcRootKind.RegisterByRef,
                        RegisterGcRootKind.InteriorPointer => GcRootKind.InteriorRegister,
                        _ => GcRootKind.RegisterRef,
                    };
                }
                else if (loc.IsFrameSlot)
                {
                    frameBase = loc.FrameBase;
                    frameOffset = loc.FrameOffset;
                    kind = root.RootKind switch
                    {
                        RegisterGcRootKind.ByRef => GcRootKind.FrameByRef,
                        RegisterGcRootKind.InteriorPointer => GcRootKind.InteriorFrame,
                        _ => GcRootKind.FrameRef,
                    };
                }
                else
                {
                    throw new InvalidOperationException("GC live root location is not final: " + loc);
                }

                return new GcRootRecord(
                    kind,
                    reg,
                    frameOffset,
                    TargetArchitecture.PointerSize,
                    root.Type?.TypeId ?? -1,
                    root.Offset,
                    flags,
                    frameBase);
            }

            private void EmitExceptionRegions()
            {
                var regions = _method.Cfg.ExceptionRegions;
                for (int i = 0; i < regions.Length; i++)
                {
                    var region = regions[i];
                    int tryStart = BlockStartPc(region.TryStartBlockId);
                    int tryEnd = BlockRangeEndPc(region.TryEndBlockIdExclusive);
                    int handlerStart = BlockStartPc(region.HandlerStartBlockId);
                    int handlerEnd = BlockRangeEndPc(region.HandlerEndBlockIdExclusive);
                    int filterStart = region.Kind == CfgExceptionRegionKind.Filter ? handlerStart : -1;
                    int catchTypeId = region.Kind == CfgExceptionRegionKind.Catch && region.CatchTypeToken != 0 ? region.CatchTypeToken : -1;
                    var kind = region.Kind switch
                    {
                        CfgExceptionRegionKind.Catch => catchTypeId < 0 ? EhRegionKind.CatchAll : EhRegionKind.Catch,
                        CfgExceptionRegionKind.Finally => EhRegionKind.Finally,
                        CfgExceptionRegionKind.Fault => EhRegionKind.Fault,
                        CfgExceptionRegionKind.Filter => EhRegionKind.Filter,
                        _ => EhRegionKind.CatchAll,
                    };
                    _asm.AddExceptionRegion(new EhRegionRecord(kind, tryStart, tryEnd, handlerStart, handlerEnd, filterStart, catchTypeId, region.ParentIndex));
                }
            }

            private int BlockStartPc(int blockId)
            {
                if ((uint)blockId >= (uint)_blockStartPc.Length || _blockStartPc[blockId] < 0)
                    throw new InvalidOperationException("Block B" + blockId.ToString() + " was not emitted.");
                return _blockStartPc[blockId];
            }

            private int BlockRangeEndPc(int endBlockIdExclusive)
            {
                int blockId = endBlockIdExclusive - 1;
                while (blockId >= 0)
                {
                    if ((uint)blockId < (uint)_blockEndPc.Length && _blockEndPc[blockId] >= 0)
                        return _blockEndPc[blockId];
                    blockId--;
                }
                return _asm.Pc;
            }

            private void EmitUnwindInfo()
            {
                for (int i = 0; i < _method.UnwindCodes.Length; i++)
                {
                    var code = _method.UnwindCodes[i];
                    if (!_nodePc.TryGetValue(code.NodeId, out int pc))
                        continue;
                    _asm.AddUnwind(new UnwindRecord(pc, ToUnwindKind(code.Kind), code.Register, code.StackOffset, code.Size));
                }
            }

            private static UnwindCodeKind ToUnwindKind(RegisterUnwindCodeKind kind)
                => kind switch
                {
                    RegisterUnwindCodeKind.AllocateStack => UnwindCodeKind.AllocateStack,
                    RegisterUnwindCodeKind.SaveReturnAddress => UnwindCodeKind.SaveReturnAddress,
                    RegisterUnwindCodeKind.SaveCalleeSavedRegister => UnwindCodeKind.SaveCalleeSavedRegister,
                    RegisterUnwindCodeKind.SetFramePointer => UnwindCodeKind.SetFramePointer,
                    _ => throw new ArgumentOutOfRangeException(nameof(kind)),
                };
        }
    }
}
