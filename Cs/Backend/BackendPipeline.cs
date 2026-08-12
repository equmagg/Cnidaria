using Cnidaria.C;
using Cnidaria.RiscV;
using Cnidaria.X86;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace Cnidaria.Cs
{
    public sealed class CodeGeneratorOptions
    {
        public static CodeGeneratorOptions Default { get; } = new CodeGeneratorOptions();

        public bool EmitExceptionRegions { get; set; } = true;
        public bool EmitGcInfo { get; set; } = true;
        public bool EmitUnwindInfo { get; set; } = true;
        public bool VerifyImage { get; set; } = true;
    }
    public sealed class BackendOptions
    {
        public static BackendOptions Default { get; } = new BackendOptions();

        public bool SplitCriticalEdgesBeforeSsa { get; set; } = true;
        public bool BuildSsa { get; set; } = true;
        public bool OptimizeSsa { get; set; } = true;
        public bool ValidateHir { get; set; } = true;
        public bool ValidateSsa { get; set; } = true;
        public bool EnablePhysicalPromotion { get; set; } = true;
        public bool EnableLoopInversion { get; set; } = true;
        public bool EnableLoopUnrolling { get; set; } = true;

        public PhysicalPromotionOptions PhysicalPromotionOptions { get; set; } = PhysicalPromotionOptions.Default;
        public LoopInversionOptions LoopInversionOptions { get; set; } = LoopInversionOptions.Default;
        public LoopUnrollingOptions LoopUnrollingOptions { get; set; } = LoopUnrollingOptions.Default;
        public SsaOptimizationOptions SsaOptimizationOptions { get; set; } = SsaOptimizationOptions.DefaultWithoutValidation;
        public LinearRationalizationOptions RationalizationOptions { get; set; } = LinearRationalizationOptions.Default;
        public RegisterAllocatorOptions? RegisterAllocatorOptions { get; set; }
        public CodeGeneratorOptions CodeGeneratorOptions { get; set; } = CodeGeneratorOptions.Default;
        public TargetInfo? Target { get; set; }
    }
    public sealed class BackendResult
    {
        internal GenTreeProgram HirProgram { get; }
        internal SsaProgram? SsaProgram { get; }
        internal GenTreeProgram RationalizedProgram { get; }
        internal GenTreeProgram LoweredProgram { get; }
        internal GenTreeProgram RegisterAllocatedProgram { get; }
        public CodeImage Image { get; }

        internal BackendResult(
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
    public static class BackendPipeline
    {
        public static BackendResult CompileProgram(GenTreeProgram program, BackendOptions? options = null)
        {
            if (program is null)
                throw new ArgumentNullException(nameof(program));

            options ??= BackendOptions.Default;
            var swCompile = Stopwatch.StartNew();
            var lowered = GenTreeBackendPipeline.RunProgram(
                program,
                options,
                nonCallOperationsClobberCallerSavedRegisters: false);
            var image = RegisterBytecodeGenerator.Build(lowered.RegisterAllocatedProgram, options.CodeGeneratorOptions, program.Target);
            swCompile.Stop();
            return new BackendResult(
                lowered.HirProgram,
                lowered.SsaProgram,
                lowered.RationalizedProgram,
                lowered.LoweredProgram,
                lowered.RegisterAllocatedProgram,
                image);
        }

        internal static BackendResult CompileMethod(GenTreeMethod method, BackendOptions? options = null)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            options ??= BackendOptions.Default;
            var lowered = GenTreeBackendPipeline.RunMethod(
                method,
                options,
                nonCallOperationsClobberCallerSavedRegisters: false);
            var image = RegisterBytecodeGenerator.Build(lowered.RegisterAllocatedProgram, options.CodeGeneratorOptions, method.Target);
            return new BackendResult(
                lowered.HirProgram,
                lowered.SsaProgram,
                lowered.RationalizedProgram,
                lowered.LoweredProgram,
                lowered.RegisterAllocatedProgram,
                image);
        }

        public static X86Program CompileX86Program(
            GenTreeProgram program,
            BackendOptions? options = null,
            X86CodeGeneratorOptions? codeGeneratorOptions = null)
        {
            if (program is null)
                throw new ArgumentNullException(nameof(program));

            options ??= BackendOptions.Default;
            var lowered = GenTreeBackendPipeline.RunProgram(
                program, options, nonCallOperationsClobberCallerSavedRegisters: false);
            return X86CodeGenerator.Build(
                lowered.RegisterAllocatedProgram, codeGeneratorOptions, lowered.RegisterAllocatedProgram.Target);
        }

        internal static X86Program CompileX86Method(
            GenTreeMethod method,
            BackendOptions? options = null,
            X86CodeGeneratorOptions? codeGeneratorOptions = null)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            options ??= BackendOptions.Default;
            var lowered = GenTreeBackendPipeline.RunMethod(
                method, options, nonCallOperationsClobberCallerSavedRegisters: false);
            return X86CodeGenerator.Build(
                lowered.RegisterAllocatedProgram, codeGeneratorOptions, lowered.RegisterAllocatedProgram.Target);
        }

        public static RiscVProgram CompileRiscVProgram(
            GenTreeProgram program,
            BackendOptions? options = null,
            RiscVCodeGeneratorOptions? codeGeneratorOptions = null)
        {
            if (program is null)
                throw new ArgumentNullException(nameof(program));

            options ??= BackendOptions.Default;
            var lowered = GenTreeBackendPipeline.RunProgram(
                program, options, nonCallOperationsClobberCallerSavedRegisters: true);
            return RiscVCodeGenerator.Build(
                lowered.RegisterAllocatedProgram,
                codeGeneratorOptions,
                lowered.RegisterAllocatedProgram.Target);
        }

        internal static RiscVProgram CompileRiscVMethod(
            GenTreeMethod method,
            BackendOptions? options = null,
            RiscVCodeGeneratorOptions? codeGeneratorOptions = null)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            options ??= BackendOptions.Default;
            var lowered = GenTreeBackendPipeline.RunMethod(
                method, options, nonCallOperationsClobberCallerSavedRegisters: true);
            return RiscVCodeGenerator.Build(
                lowered.RegisterAllocatedProgram,
                codeGeneratorOptions,
                lowered.RegisterAllocatedProgram.Target);
        }
    }
    internal static class GenTreeBackendPipeline
    {
        public static GenTreeBackendPipelineResult RunProgram(
            GenTreeProgram program,
            BackendOptions options,
            bool nonCallOperationsClobberCallerSavedRegisters)
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
                var method = CompileMethodThroughLsra(
                    program.Methods[i],
                    options,
                    nonCallOperationsClobberCallerSavedRegisters,
                    out var hir,
                    out var ssa,
                    out var rationalized,
                    out var lowered);
                hirMethods.Add(hir);
                if (ssa is not null)
                    ssaMethods!.Add(ssa);
                rationalizedMethods.Add(rationalized);
                loweredMethods.Add(lowered);
                allocatedMethods.Add(method);
            }

            return new GenTreeBackendPipelineResult(
                new GenTreeProgram(program.TypeSystem, hirMethods.ToImmutable()),
                ssaMethods is null ? null : new SsaProgram(ssaMethods.ToImmutable()),
                new GenTreeProgram(program.TypeSystem, rationalizedMethods.ToImmutable()),
                new GenTreeProgram(program.TypeSystem, loweredMethods.ToImmutable()),
                new GenTreeProgram(program.TypeSystem, allocatedMethods.ToImmutable()));
        }

        public static GenTreeBackendPipelineResult RunMethod(
            GenTreeMethod method,
            BackendOptions options,
            bool nonCallOperationsClobberCallerSavedRegisters)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (options is null)
                throw new ArgumentNullException(nameof(options));

            var allocated = CompileMethodThroughLsra(
                method,
                options,
                nonCallOperationsClobberCallerSavedRegisters,
                out var hir,
                out var ssa,
                out var rationalized,
                out var lowered);
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
            bool nonCallOperationsClobberCallerSavedRegisters,
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

                ssaMethod = SsaEarlyPropagator.OptimizeMethod(ssaMethod, validate: options.ValidateSsa);
                ssaMethod = SsaValueNumbering.BuildMethod(ssaMethod, validate: options.ValidateSsa);

                if (options.OptimizeSsa)
                {
                    ssaMethod = SsaOptimizer.OptimizeMethod(ssaMethod, options.SsaOptimizationOptions);

                    if (options.SsaOptimizationOptions.EnableInductionVariableOptimization)
                    {
                        var inductionVariables = SsaInductionVariableOptimizer.OptimizeMethod(
                            ssaMethod,
                            options.SsaOptimizationOptions,
                            options.ValidateSsa);
                        ssaMethod = inductionVariables.Method;
                    }

                    if (options.SsaOptimizationOptions.EnableDeadStoreRemoval)
                    {
                        var deadStores = SsaDeadStoreRemoval.Run(ssaMethod, validate: options.ValidateSsa);
                        if (deadStores.Changed)
                            ssaMethod = RebuildSsaAfterVnBasedDeadStoreRemoval(ssaMethod, deadStores.Method, options.ValidateSsa);
                    }

                    hirMethod = ssaMethod.GenTreeMethod;
                    hirMethod.AttachSsa(ssaMethod, optimized: true);
                }
                else
                {
                    hirMethod = ssaMethod.GenTreeMethod;
                    hirMethod.AttachSsa(ssaMethod, optimized: false);
                }

                if (options.ValidateSsa)
                    SsaVerifier.Verify(ssaMethod);
            }

            var lirOptions = CreateLirOptions(options, nonCallOperationsClobberCallerSavedRegisters);
            rationalizedMethod = GenTreeLinearIrRationalizer.RationalizeMethod(hirMethod, ssaMethod, lirOptions);
            loweredMethod = GenTreeLinearLowerer.LowerMethod(rationalizedMethod, lirOptions);
            return LinearScanRegisterAllocator.AllocateMethod(loweredMethod, options.RegisterAllocatorOptions);
        }

        private static SsaMethod RebuildSsaAfterVnBasedDeadStoreRemoval(
            SsaMethod previous,
            GenTreeMethod rewritten,
            bool validate)
        {
            bool includeExceptionEdges = HasExceptionEdges(previous.Cfg);
            var cfg = ControlFlowGraph.Build(rewritten, includeExceptionEdges);
            rewritten.AttachFlowGraph(cfg);

            var liveness = GenTreeLocalLiveness.Build(rewritten, cfg);
            rewritten.AttachHirLiveness(liveness);

            var rebuilt = GenTreeSsaBuilder.BuildMethod(rewritten, cfg, liveness, validate);
            return SsaValueNumbering.BuildMethod(rebuilt, validate);
        }

        private static bool HasExceptionEdges(ControlFlowGraph cfg)
        {
            for (int b = 0; b < cfg.Blocks.Length; b++)
            {
                var successors = cfg.Blocks[b].Successors;
                for (int s = 0; s < successors.Length; s++)
                {
                    if (successors[s].Kind == CfgEdgeKind.Exception)
                        return true;
                }
            }

            return false;
        }

        private static GenTreeMethod PrepareHir(GenTreeMethod method, BackendOptions options)
        {
            method = GenTreeClassInitializationEntryInserter.Insert(method);

            method = GenTreeMorpher.MorphMethod(method);
            method = GenTreeLocalRewriter.RewriteMethod(method);

            if (options.EnablePhysicalPromotion)
            {
                var promotion = GenTreePhysicalPromoter.PromoteMethod(method, options.PhysicalPromotionOptions);
                method = promotion.Method;
                if (promotion.Changed)
                    method = GenTreeMorpher.MorphMethod(method, GenTreeMethodPhase.GlobalMorphedHir);
            }

            method = GenTreeClassInitializationOptimizer.OptimizeMethod(method);

            if (options.EnableLoopInversion)
                method = GenTreeLoopInverter.InvertLoops(method, options.LoopInversionOptions);

            if (options.EnableLoopUnrolling)
                method = GenTreeLoopUnroller.UnrollLoops(method, options.LoopUnrollingOptions);

            if (options.SplitCriticalEdgesBeforeSsa)
            {
                GenTreeMethod split;
                if (method.Function.ExceptionHandlers.Length == 0)
                {
                    split = GenTreeCriticalEdgeSplitter.SplitCriticalEdges(method);
                }
                else
                {
                    var preSplitCfg = ControlFlowGraph.Build(method);
                    split = GenTreeCriticalEdgeSplitter.SplitCriticalEdges(
                        method,
                        edge => CanSplitCriticalEdgeWithEh(preSplitCfg, edge));
                }

                if (!ReferenceEquals(split, method))
                {
                    method = GenTreeMorpher.MorphMethod(split);
                    method = GenTreeLocalRewriter.RewriteMethod(method);
                }
            }

            var cfg = ControlFlowGraph.Build(method);
            method.AttachFlowGraph(cfg);

            var liveness = GenTreeLocalLiveness.Build(method, cfg);
            method.AttachHirLiveness(liveness);

            if (options.ValidateHir)
                GenTreeHirVerifier.Verify(method, cfg, liveness);

            return method;
        }

        private static bool CanSplitCriticalEdgeWithEh(ControlFlowGraph cfg, CfgEdge edge)
        {
            if (edge.Kind == CfgEdgeKind.Exception)
                return false;

            if ((uint)edge.FromBlockId >= (uint)cfg.Blocks.Length ||
                (uint)edge.ToBlockId >= (uint)cfg.Blocks.Length)
                return false;

            var from = cfg.Blocks[edge.FromBlockId];
            var to = cfg.Blocks[edge.ToBlockId];

            if (from.IsInHandlerRegion || to.IsInHandlerRegion || to.IsHandlerEntry)
                return false;

            if (!to.IsInTryRegion)
                return true;

            if (GenTreeCriticalEdgeSplitter.IsTryRegionEntry(cfg, edge.ToBlockId))
                return false;

            return GenTreeCriticalEdgeSplitter.SameEhRegion(from, to);
        }

        private static LinearRationalizationOptions CreateLirOptions(
            BackendOptions options,
            bool nonCallOperationsClobberCallerSavedRegisters)
        {
            return new LinearRationalizationOptions
            {
                Validate = options.RationalizationOptions.Validate,
                NonCallOperationsClobberCallerSavedRegisters = nonCallOperationsClobberCallerSavedRegisters,
            };
        }
    }

    internal static class GenTreeClassInitializationEntryInserter
    {
        public static GenTreeMethod Insert(GenTreeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (!method.RuntimeMethod.RequiresClassInitializationEntryCheck || method.Blocks.IsDefaultOrEmpty)
                return method;

            RuntimeType type = method.RuntimeMethod.DeclaringType;
            if (type.IsBeforeFieldInit || StringComparer.Ordinal.Equals(method.RuntimeMethod.Name, ".cctor"))
                return method;

            GenTreeBlock entry = method.Blocks[0];
            if (!entry.Statements.IsDefaultOrEmpty &&
                entry.Statements[0].Kind == GenTreeKind.ClassInit &&
                ReferenceEquals(entry.Statements[0].RuntimeType, type))
            {
                return method;
            }

            int nextId = 0;
            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var nodes = method.Blocks[b].LinearNodes;
                for (int n = 0; n < nodes.Length; n++)
                    nextId = Math.Max(nextId, checked(nodes[n].Id + 1));
            }

            var classInit = new GenTree(
                nextId,
                GenTreeKind.ClassInit,
                entry.StartPc,
                BytecodeOp.Nop,
                type: null,
                stackKind: GenStackKind.Void,
                flags: GenTreeFlags.ContainsCall | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow | GenTreeFlags.GlobalRef | GenTreeFlags.Ordered,
                operands: ImmutableArray<GenTree>.Empty,
                runtimeType: type);

            var statements = ImmutableArray.CreateBuilder<GenTree>(entry.Statements.Length + 1);
            statements.Add(classInit);
            statements.AddRange(entry.Statements);

            var blocks = method.Blocks.ToArray();
            blocks[0] = new GenTreeBlock(
                entry.Id,
                entry.StartPc,
                entry.EndPcExclusive,
                entry.EntryStackDepth,
                entry.ExitStackDepth,
                entry.JumpKind,
                entry.Flags,
                statements.ToImmutable(),
                entry.SuccessorBlockIds,
                entry.SuccessorPcs,
                entry.RegionPc);
            return method.CloneWithBlocks(blocks.ToImmutableArray());
        }
    }

    internal static class GenTreeClassInitializationOptimizer
    {
        public static GenTreeMethod OptimizeMethod(GenTreeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (method.Function.ExceptionHandlers.Length != 0)
                return method;

            bool hasClassInit = false;
            for (int b = 0; b < method.Blocks.Length && !hasClassInit; b++)
            {
                var statements = method.Blocks[b].Statements;
                for (int s = 0; s < statements.Length; s++)
                {
                    if (statements[s].Kind == GenTreeKind.ClassInit)
                    {
                        hasClassInit = true;
                        break;
                    }
                }
            }
            if (!hasClassInit)
                return method;

            ControlFlowGraph cfg = ControlFlowGraph.Build(method);
            var blocks = method.Blocks.ToArray();
            bool changed = false;
            var initial = new HashSet<int>();
            RuntimeMethod runtimeMethod = method.RuntimeMethod;
            RuntimeType declaringType = runtimeMethod.DeclaringType;
            if (StringComparer.Ordinal.Equals(runtimeMethod.Name, ".cctor") ||
                (!runtimeMethod.RequiresClassInitializationEntryCheck &&
                 !declaringType.IsBeforeFieldInit &&
                 (runtimeMethod.IsStatic || declaringType.IsValueType || StringComparer.Ordinal.Equals(runtimeMethod.Name, ".ctor"))))
            {
                initial.Add(declaringType.TypeId);
            }

            Visit(0, initial);
            if (!changed)
                return method;
            return method.CloneWithBlocks(blocks.ToImmutableArray());

            void Visit(int blockId, HashSet<int> inherited)
            {
                var current = new HashSet<int>(inherited);
                GenTreeBlock block = method.Blocks[blockId];
                var statements = block.Statements;
                var rewritten = ImmutableArray.CreateBuilder<GenTree>(statements.Length);
                bool blockChanged = false;

                for (int i = 0; i < statements.Length; i++)
                {
                    GenTree statement = statements[i];
                    if (statement.Kind == GenTreeKind.ClassInit && statement.RuntimeType is RuntimeType type)
                    {
                        if (!current.Add(type.TypeId))
                        {
                            blockChanged = true;
                            continue;
                        }
                    }
                    rewritten.Add(statement);
                }

                if (blockChanged)
                {
                    changed = true;
                    blocks[blockId] = new GenTreeBlock(
                        block.Id,
                        block.StartPc,
                        block.EndPcExclusive,
                        block.EntryStackDepth,
                        block.ExitStackDepth,
                        block.JumpKind,
                        block.Flags,
                        rewritten.ToImmutable(),
                        block.SuccessorBlockIds,
                        block.SuccessorPcs,
                        block.RegionPc);
                }

                var children = cfg.DominatorTreeChildren[blockId];
                for (int i = 0; i < children.Length; i++)
                    Visit(children[i], current);
            }
        }
    }

    internal static class GenTreeMorpher
    {
        private const GenTreeFlags EffectMask =
            GenTreeFlags.ContainsCall |
            GenTreeFlags.CanThrow |
            GenTreeFlags.SideEffect |
            GenTreeFlags.MemoryRead |
            GenTreeFlags.MemoryWrite |
            GenTreeFlags.GlobalRef |
            GenTreeFlags.Ordered;

        public static GenTreeMethod MorphMethod(GenTreeMethod method, GenTreeMethodPhase phase = GenTreeMethodPhase.MorphedHir)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            var target = method.Target;
            bool methodChanged = false;
            var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(method.Blocks.Length);

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var block = method.Blocks[b];
                bool blockChanged = false;
                var statements = ImmutableArray.CreateBuilder<GenTree>(block.Statements.Length);

                for (int s = 0; s < block.Statements.Length; s++)
                {
                    GenTree statement = block.Statements[s];
                    GenTree morphed = MorphTree(statement, target, ref blockChanged);
                    morphed.SetParent(null);
                    statements.Add(morphed);
                }

                if (blockChanged)
                {
                    methodChanged = true;
                    blocks.Add(new GenTreeBlock(
                        block.Id,
                        block.StartPc,
                        block.EndPcExclusive,
                        block.EntryStackDepth,
                        block.ExitStackDepth,
                        block.JumpKind,
                        block.Flags,
                        statements.ToImmutable(),
                        block.SuccessorBlockIds,
                        block.SuccessorPcs,
                        block.RegionPc));
                }
                else
                {
                    blocks.Add(block);
                }
            }

            if (methodChanged)
                method = method.CloneWithBlocks(blocks.ToImmutable());

            method.SetPhase(phase);
            return method;
        }

        private static GenTree MorphTree(GenTree tree, TargetInfo target, ref bool changed)
        {
            if (!tree.Operands.IsDefaultOrEmpty)
            {
                bool operandsChanged = false;
                var operands = ImmutableArray.CreateBuilder<GenTree>(tree.Operands.Length);
                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    GenTree original = tree.Operands[i];
                    GenTree morphed = MorphTree(original, target, ref changed);
                    operandsChanged |= !ReferenceEquals(original, morphed);
                    operands.Add(morphed);
                }

                if (operandsChanged)
                {
                    tree.SetOperands(operands.ToImmutable());
                    changed = true;
                }
            }

            GenTree folded = MorphNode(tree, target);
            if (!ReferenceEquals(folded, tree))
            {
                changed = true;
                return folded;
            }

            return tree;
        }

        internal static GenTree MorphNode(GenTree node, TargetInfo target)
        {
            NormalizeNodeFlags(node, target);
            return GenTreeFolder.Fold(node, target);
        }

        internal static GenTreeFlags NormalizeTreeFlags(GenTree node, TargetInfo target)
        {
            for (int i = 0; i < node.Operands.Length; i++)
                NormalizeTreeFlags(node.Operands[i], target);
            return NormalizeNodeFlags(node, target);
        }

        private static GenTreeFlags NormalizeNodeFlags(GenTree node, TargetInfo target)
        {
            var flags = node.Flags & ~EffectMask;
            for (int i = 0; i < node.Operands.Length; i++)
                flags |= node.Operands[i].Flags & EffectMask;

            if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var localFieldAccess))
                return NormalizeLocalFieldFlags(node, localFieldAccess);

            switch (node.Kind)
            {
                case GenTreeKind.ClassInit:
                case GenTreeKind.Call:
                case GenTreeKind.IndirectCall:
                case GenTreeKind.VirtualCall:
                case GenTreeKind.DelegateInvoke:
                    flags |= GenTreeFlags.ContainsCall | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow | GenTreeFlags.GlobalRef | GenTreeFlags.Ordered;
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

                case GenTreeKind.StaticData:
                    flags |= GenTreeFlags.Allocation | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow | GenTreeFlags.GlobalRef | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.StackAlloc:
                    flags |= GenTreeFlags.Allocation | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.Field:
                case GenTreeKind.FieldAddr:
                    flags |= GenTreeFlags.MemoryRead;
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        flags |= GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.StaticField:
                case GenTreeKind.StaticFieldAddr:
                    flags |= GenTreeFlags.MemoryRead | GenTreeFlags.GlobalRef;
                    break;

                case GenTreeKind.LoadIndirect:
                    flags |= GenTreeFlags.MemoryRead;
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        flags |= GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.NullCheck:
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        flags |= GenTreeFlags.CanThrow;
                    flags |= GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.ArrayLength:
                    flags |= GenTreeFlags.MemoryRead;
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        flags |= GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.ArrayElement:
                case GenTreeKind.ArrayElementAddr:
                case GenTreeKind.ArrayDataRef:
                    flags |= GenTreeFlags.MemoryRead | GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.StoreLocal:
                case GenTreeKind.StoreArg:
                case GenTreeKind.StoreTemp:
                    flags |= GenTreeFlags.LocalDef | GenTreeFlags.SideEffect | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.Local:
                case GenTreeKind.Arg:
                case GenTreeKind.Temp:
                case GenTreeKind.LocalAddr:
                case GenTreeKind.ArgAddr:
                case GenTreeKind.TempAddr:
                    flags |= GenTreeFlags.LocalUse;
                    break;

                case GenTreeKind.StoreField:
                    flags |= GenTreeFlags.MemoryWrite | GenTreeFlags.SideEffect | GenTreeFlags.GlobalRef | GenTreeFlags.Ordered;
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        flags |= GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.StoreStaticField:
                    flags |= GenTreeFlags.MemoryWrite | GenTreeFlags.SideEffect | GenTreeFlags.GlobalRef | GenTreeFlags.CanThrow | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.StoreIndirect:
                    flags |= GenTreeFlags.MemoryWrite | GenTreeFlags.SideEffect | GenTreeFlags.Ordered;
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        flags |= GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.StoreArrayElement:
                    flags |= GenTreeFlags.MemoryWrite | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow | GenTreeFlags.Ordered;
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

                case GenTreeKind.Binary:
                    if (GenTreeArithmeticSemantics.BinaryOperationCanThrow(node.SourceOp, node.Type, node.StackKind, node.Operands, target))
                        flags |= GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.Conv:
                    if ((node.ConvFlags & NumericConvFlags.Checked) != 0)
                        flags |= GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.CastClass:
                case GenTreeKind.UnboxAny:
                    flags |= GenTreeFlags.CanThrow;
                    break;
            }

            if (node.Kind == GenTreeKind.Binary &&
                (node.SourceOp is BytecodeOp.Div or BytecodeOp.Div_Un or BytecodeOp.Rem or BytecodeOp.Rem_Un) &&
                !GenTreeArithmeticSemantics.DivRemCanThrow(node, target))
                flags = ClearNodeOwnedCanThrow(flags, node.Operands);

            node.Flags = flags;
            return flags;
        }

        internal static GenTreeFlags NormalizeLocalFieldFlags(GenTree node, SsaLocalAccess localFieldAccess)
        {
            GenTreeFlags flags =
                (node.Flags & GenTreeFlags.ExplicitInit) |
                GenTreeFlags.NullCheckEliminated;
            for (int i = 0; i < node.Operands.Length; i++)
            {
                flags |= node.Operands[i].Flags
                    & ~(GenTreeFlags.AssertionProperties | GenTreeFlags.ExplicitInit);
            }

            flags |= GenTreeFlags.Indirect;

            if (localFieldAccess.IsUse || localFieldAccess.IsPartialDefinition)
                flags |= GenTreeFlags.LocalUse;

            if (localFieldAccess.IsDefinition)
            {
                flags |= GenTreeFlags.LocalDef |
                         GenTreeFlags.VarDef |
                         GenTreeFlags.SideEffect |
                         GenTreeFlags.Ordered;

                if (localFieldAccess.IsPartialDefinition)
                    flags |= GenTreeFlags.VarUseAsg;
            }

            node.Flags = flags;
            return flags;
        }

        private static GenTreeFlags ClearNodeOwnedCanThrow(GenTreeFlags flags, ImmutableArray<GenTree> operands)
        {
            flags &= ~GenTreeFlags.CanThrow;
            for (int i = 0; i < operands.Length; i++)
            {
                if (operands[i].CanThrow)
                    flags |= GenTreeFlags.CanThrow;
            }
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
                    MarkAddressExposed(statements[s], parent: null, operandIndex: -1);
            }

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

                GenTreeMorpher.NormalizeLocalFieldFlags(node, localFieldAccess);
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

            if (node.Kind == GenTreeKind.NullCheck)
            {
                if (node.Operands.Length != 1 ||
                    node.StackKind != GenStackKind.Void ||
                    node.Operands[0].StackKind is not (GenStackKind.Ref or GenStackKind.Null))
                {
                    throw new InvalidOperationException($"Malformed HIR null check in B{blockId}: {node}.");
                }
            }
            else if (node.Kind == GenTreeKind.ArrayLength)
            {
                if (node.Operands.Length != 1 ||
                    node.StackKind != GenStackKind.I4 ||
                    node.Operands[0].StackKind is not (GenStackKind.Ref or GenStackKind.Null))
                {
                    throw new InvalidOperationException($"Malformed HIR array length in B{blockId}: {node}.");
                }
            }

            for (int i = 0; i < node.Operands.Length; i++)
                VerifyTree(node.Operands[i], node, blockId, i);
        }
    }
}
