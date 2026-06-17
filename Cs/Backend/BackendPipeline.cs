using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

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
            var swCompile = Stopwatch.StartNew();
            var lowered = GenTreeBackendPipeline.RunProgram(program, options);
            var image = CodeGenerator.Build(lowered.RegisterAllocatedProgram, options.CodeGeneratorOptions);
            swCompile.Stop();
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
                new GenTreeProgram(program.TypeSystem, hirMethods.ToImmutable()),
                ssaMethods is null ? null : new SsaProgram(ssaMethods.ToImmutable()),
                new GenTreeProgram(program.TypeSystem, rationalizedMethods.ToImmutable()),
                new GenTreeProgram(program.TypeSystem, loweredMethods.ToImmutable()),
                new GenTreeProgram(program.TypeSystem, allocatedMethods.ToImmutable()));
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

                if (options.OptimizeSsa)
                {
                    ssaMethod = SsaOptimizer.OptimizeMethod(ssaMethod, options.SsaOptimizationOptions);
                    hirMethod = ssaMethod.GenTreeMethod;
                    hirMethod.AttachSsa(ssaMethod, optimized: true);
                }
                else
                {
                    ssaMethod = SsaValueNumbering.BuildMethod(ssaMethod, validate: options.ValidateSsa);
                    hirMethod = ssaMethod.GenTreeMethod;
                    hirMethod.AttachSsa(ssaMethod, optimized: false);
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

            if (to.IsInTryRegion)
                return false;

            return true;
        }

        private static LinearRationalizationOptions CreateLirOptions(BackendOptions options)
        {
            return new LinearRationalizationOptions
            {
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
                case GenTreeKind.DelegateInvoke:
                case GenTreeKind.NewObject:
                    flags |= GenTreeFlags.ContainsCall | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.NewDelegate:
                case GenTreeKind.DelegateCombine:
                case GenTreeKind.DelegateRemove:
                case GenTreeKind.NewArray:
                case GenTreeKind.Box:
                    flags |= GenTreeFlags.Allocation | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow;
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

                case GenTreeKind.LocalAddr:
                case GenTreeKind.ArgAddr:
                case GenTreeKind.TempAddr:
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
                    if (node.Kind is GenTreeKind.Throw or GenTreeKind.Rethrow)
                        flags |= GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.Binary:
                    if (GenTreeArithmeticSemantics.BinaryOperationCanThrow(node.SourceOp, node.Type, node.StackKind, node.Operands))
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
                !GenTreeArithmeticSemantics.DivRemCanThrow(node))
                flags = ClearNodeOwnedCanThrow(flags, node.Operands);

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
}
