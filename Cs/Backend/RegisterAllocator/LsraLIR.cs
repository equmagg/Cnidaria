using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;

namespace Cnidaria.Cs
{
    internal static class GenTreeLirFactory
    {
        private static ImmutableArray<RegisterOperand> OneResult(RegisterOperand result)
            => result.IsNone ? ImmutableArray<RegisterOperand>.Empty : ImmutableArray.Create(result);

        private static ImmutableArray<OperandRole> NormalizeUseRoles(
            ImmutableArray<RegisterOperand> uses,
            ImmutableArray<OperandRole> useRoles)
        {
            uses = uses.IsDefault ? ImmutableArray<RegisterOperand>.Empty : uses;
            if (uses.Length == 0)
                return ImmutableArray<OperandRole>.Empty;
            if (useRoles.IsDefaultOrEmpty)
                return ImmutableArray.CreateRange(new OperandRole[uses.Length]);
            if (useRoles.Length != uses.Length)
                throw new ArgumentException("Use role count must match register use count.", nameof(useRoles));
            return useRoles;
        }

        private static MoveFlags InferMoveFlags(RegisterOperand destination, RegisterOperand source)
        {
            if (source.IsAddress)
                return MoveFlags.None;

            MoveFlags flags = MoveFlags.None;
            if (source.IsMemoryOperand && destination.IsRegister)
                flags |= MoveFlags.Reload;
            if (source.IsRegister && destination.IsMemoryOperand)
                flags |= MoveFlags.Spill;
            if (source.IsMemoryOperand && destination.IsMemoryOperand)
                flags |= MoveFlags.Reload | MoveFlags.Spill;
            return flags;
        }

        private static GenTree Attach(
            GenTree node,
            int id,
            int blockId,
            int ordinal,
            ImmutableArray<RegisterOperand> results,
            ImmutableArray<RegisterOperand> uses,
            ImmutableArray<GenTree> linearResults,
            ImmutableArray<GenTree> linearUses,
            ImmutableArray<LirOperandFlags> linearOperands,
            int linearId,
            FrameOperation frameOperation,
            int immediate,
            string? comment,
            ImmutableArray<OperandRole> useRoles = default,
            MoveFlags moveFlags = MoveFlags.None,
            int phiCopyFromBlockId = -1,
            int phiCopyToBlockId = -1)
        {
            results = results.IsDefault ? ImmutableArray<RegisterOperand>.Empty : results;
            uses = uses.IsDefault ? ImmutableArray<RegisterOperand>.Empty : uses;
            linearResults = linearResults.IsDefault ? ImmutableArray<GenTree>.Empty : linearResults;
            linearUses = linearUses.IsDefault ? ImmutableArray<GenTree>.Empty : linearUses;

            node.RegisterResults = linearResults;
            node.RegisterResult = linearResults.Length == 1 ? linearResults[0] : null;
            node.RegisterUses = linearUses;
            node.OperandFlags = linearOperands.IsDefault ? ImmutableArray<LirOperandFlags>.Empty : linearOperands;
            node.LinearId = linearId >= 0 ? linearId : id;
            node.LinearBlockId = blockId;
            node.LinearOrdinal = ordinal;
            bool isPhiEdgeMove = phiCopyFromBlockId >= 0 || phiCopyToBlockId >= 0;
            if (isPhiEdgeMove && (phiCopyFromBlockId < 0 || phiCopyToBlockId < 0))
                throw new InvalidOperationException("Phi edge moves must carry both source and destination block ids.");

            node.LinearKind = node.Kind switch
            {
                GenTreeKind.Copy or GenTreeKind.Reload or GenTreeKind.Spill => (isPhiEdgeMove ? GenTreeLinearKind.PhiCopy : GenTreeLinearKind.Copy),
                GenTreeKind.GcPoll => GenTreeLinearKind.GcPoll,
                _ => GenTreeLinearKind.Tree,
            };
            node.LinearPhiCopyFromBlockId = phiCopyFromBlockId;
            node.LinearPhiCopyToBlockId = phiCopyToBlockId;

            node.AttachLsraInfo(new GenTreeLsraInfo
            {
                GtRegNum = results.Length == 1 && results[0].IsRegister ? results[0].Register : MachineRegister.Invalid,
                Home = results.Length == 1 ? results[0] : RegisterOperand.None,
                CodegenResults = results,
                CodegenUses = uses,
                CodegenUseRoles = NormalizeUseRoles(uses, useRoles),
                CodegenResultValues = BuildValueKeys(linearResults),
                CodegenUseValues = BuildValueKeys(linearUses),
                MoveFlags = moveFlags,
                FrameOperation = frameOperation,
                Immediate = immediate,
                Comment = comment,
            });
            return node;
        }

        private static ImmutableArray<GenTreeValueKey> BuildValueKeys(ImmutableArray<GenTree> values)
        {
            if (values.IsDefaultOrEmpty)
                return ImmutableArray<GenTreeValueKey>.Empty;
            var result = ImmutableArray.CreateBuilder<GenTreeValueKey>(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                result.Add(value.LinearValueKey);
            }
            return result.ToImmutable();
        }

        private static GenTree SyntheticNode(int id, GenTreeKind kind, GenStackKind stackKind = GenStackKind.Void, RuntimeType? type = null)
        {
            return new GenTree(
                id,
                kind,
                pc: -1,
                BytecodeOp.Nop,
                type: type,
                stackKind: stackKind,
                flags: GenTreeFlags.SideEffect | GenTreeFlags.Ordered,
                operands: ImmutableArray<GenTree>.Empty);
        }

        public static GenTree Tree(
            int id,
            int blockId,
            int ordinal,
            GenTree source,
            RegisterOperand result,
            ImmutableArray<RegisterOperand> uses,
            GenTree? linearResult,
            ImmutableArray<GenTree> linearUses,
            int linearId = -1,
            ImmutableArray<OperandRole> useRoles = default,
            ImmutableArray<LirOperandFlags> linearOperands = default)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            var linearResults = result.IsNone
                ? ImmutableArray<GenTree>.Empty
                : linearResult is not null
                    ? ImmutableArray.Create(linearResult)
                    : ImmutableArray<GenTree>.Empty;

            return Attach(
                source,
                id,
                blockId,
                ordinal,
                OneResult(result),
                uses,
                linearResults,
                linearUses,
                linearOperands,
                linearId,
                FrameOperation.None,
                immediate: 0,
                comment: null,
                useRoles: useRoles);
        }

        public static GenTree TreeMulti(
            int id,
            int blockId,
            int ordinal,
            GenTree source,
            ImmutableArray<RegisterOperand> results,
            ImmutableArray<RegisterOperand> uses,
            ImmutableArray<GenTree> linearResults,
            ImmutableArray<GenTree> linearUses,
            int linearId = -1,
            ImmutableArray<OperandRole> useRoles = default,
            ImmutableArray<LirOperandFlags> linearOperands = default)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            results = results.IsDefault ? ImmutableArray<RegisterOperand>.Empty : results;
            linearResults = linearResults.IsDefault ? ImmutableArray<GenTree>.Empty : linearResults;
            if (linearResults.Length != 0 && linearResults.Length != results.Length)
                throw new ArgumentException("Multi-register GenTree LIR node must carry one GenTree result per result fragment.", nameof(linearResults));

            return Attach(
                source,
                id,
                blockId,
                ordinal,
                results,
                uses,
                linearResults,
                linearUses,
                linearOperands,
                linearId,
                FrameOperation.None,
                immediate: 0,
                comment: null,
                useRoles: useRoles);
        }

        public static GenTree Move(
            int id,
            int blockId,
            int ordinal,
            RegisterOperand destination,
            RegisterOperand source,
            GenTree? destinationValue,
            GenTree? sourceValue,
            string? comment = null,
            MoveFlags moveFlags = MoveFlags.None,
            int phiCopyFromBlockId = -1,
            int phiCopyToBlockId = -1)
        {
            moveFlags |= InferMoveFlags(destination, source);
            var kind = (moveFlags & MoveFlags.Spill) != 0
                ? GenTreeKind.Spill
                : (moveFlags & MoveFlags.Reload) != 0
                    ? GenTreeKind.Reload
                    : GenTreeKind.Copy;
            var node = SyntheticNode(id, kind, destinationValue?.StackKind ?? sourceValue?.StackKind ?? GenStackKind.Void, destinationValue?.Type ?? sourceValue?.Type);
            return Attach(
                node,
                id,
                blockId,
                ordinal,
                OneResult(destination),
                ImmutableArray.Create(source),
                destinationValue is not null ? ImmutableArray.Create(destinationValue) : ImmutableArray<GenTree>.Empty,
                sourceValue is not null ? ImmutableArray.Create(sourceValue) : ImmutableArray<GenTree>.Empty,
                ImmutableArray<LirOperandFlags>.Empty,
                linearId: id,
                FrameOperation.None,
                immediate: 0,
                comment: comment,
                moveFlags: moveFlags,
                phiCopyFromBlockId: phiCopyFromBlockId,
                phiCopyToBlockId: phiCopyToBlockId);
        }

        public static GenTree DefaultValue(
            int id,
            int blockId,
            int ordinal,
            RegisterOperand destination,
            RuntimeType? type,
            GenStackKind stackKind,
            string? comment = null)
        {
            if (destination.IsNone)
                throw new ArgumentException("Default value emission requires a concrete destination.", nameof(destination));

            var node = SyntheticNode(id, GenTreeKind.DefaultValue, stackKind, type);
            return Attach(
                node,
                id,
                blockId,
                ordinal,
                OneResult(destination),
                ImmutableArray<RegisterOperand>.Empty,
                ImmutableArray<GenTree>.Empty,
                ImmutableArray<GenTree>.Empty,
                ImmutableArray<LirOperandFlags>.Empty,
                linearId: id,
                FrameOperation.None,
                immediate: 0,
                comment: comment);
        }

        public static GenTree Frame(
            int id,
            int blockId,
            int ordinal,
            FrameOperation operation,
            RegisterOperand result,
            ImmutableArray<RegisterOperand> uses,
            int immediate,
            string? comment = null)
        {
            if (operation == FrameOperation.None)
                throw new ArgumentOutOfRangeException(nameof(operation));
            if (immediate < 0)
                throw new ArgumentOutOfRangeException(nameof(immediate));

            var node = SyntheticNode(id, GenTreeKind.StackFrameOp);
            return Attach(
                node,
                id,
                blockId,
                ordinal,
                OneResult(result),
                uses,
                ImmutableArray<GenTree>.Empty,
                ImmutableArray<GenTree>.Empty,
                ImmutableArray<LirOperandFlags>.Empty,
                linearId: id,
                operation,
                immediate,
                comment);
        }

        public static GenTree GcPoll(
            int id,
            int blockId,
            int ordinal,
            int linearId,
            GenTree? source = null,
            string? comment = null)
        {
            if (linearId < 0)
                throw new ArgumentOutOfRangeException(nameof(linearId));

            var node = SyntheticNode(id, GenTreeKind.GcPoll);
            return Attach(
                node,
                id,
                blockId,
                ordinal,
                ImmutableArray<RegisterOperand>.Empty,
                ImmutableArray<RegisterOperand>.Empty,
                ImmutableArray<GenTree>.Empty,
                ImmutableArray<GenTree>.Empty,
                ImmutableArray<LirOperandFlags>.Empty,
                linearId,
                FrameOperation.None,
                immediate: 0,
                comment: comment);
        }
    }
    internal static class GenTreeLirNodeExtensions
    {
        public static GenTree WithOperands(
            this GenTree node,
            ImmutableArray<RegisterOperand> results,
            ImmutableArray<RegisterOperand> uses,
            ImmutableArray<GenTree> linearResults,
            ImmutableArray<GenTree> linearUses)
        {
            var roles = node.Uses.Length == (uses.IsDefault ? 0 : uses.Length)
                ? node.UseRoles
                : default;
            return node.WithOperands(results, uses, linearResults, linearUses, roles);
        }

        public static GenTree WithOperands(
            this GenTree node,
            ImmutableArray<RegisterOperand> results,
            ImmutableArray<RegisterOperand> uses,
            ImmutableArray<GenTree> linearResults,
            ImmutableArray<GenTree> linearUses,
            ImmutableArray<OperandRole> useRoles)
        {
            results = results.IsDefault ? ImmutableArray<RegisterOperand>.Empty : results;
            uses = uses.IsDefault ? ImmutableArray<RegisterOperand>.Empty : uses;
            linearResults = linearResults.IsDefault ? ImmutableArray<GenTree>.Empty : linearResults;
            linearUses = linearUses.IsDefault ? ImmutableArray<GenTree>.Empty : linearUses;

            node.RegisterResults = linearResults;
            node.RegisterResult = linearResults.Length == 1 ? linearResults[0] : null;
            node.RegisterUses = linearUses;
            node.AttachLsraInfo(new GenTreeLsraInfo
            {
                GtRegNum = results.Length == 1 && results[0].IsRegister ? results[0].Register : MachineRegister.Invalid,
                Home = results.Length == 1 ? results[0] : RegisterOperand.None,
                CodegenResults = results,
                CodegenUses = uses,
                CodegenUseRoles = NormalizeUseRoles(uses, useRoles),
                CodegenResultValues = BuildValueKeys(linearResults),
                CodegenUseValues = BuildValueKeys(linearUses),
                InternalRegisters = node.LsraInfo.InternalRegisters,
                MoveFlags = node.MoveFlags,
                FrameOperation = node.FrameOperation,
                Immediate = node.Immediate,
                Comment = node.Comment,
                Flags = node.LsraFlags,
                LocationAtDefinition = node.LsraInfo.LocationAtDefinition,
            });
            return node;
        }

        public static GenTree WithOrdinal(this GenTree node, int ordinal)
        {
            node.LinearOrdinal = ordinal;
            return node;
        }

        public static GenTree WithPlacement(this GenTree node, int blockId, int ordinal)
        {
            node.LinearBlockId = blockId;
            node.LinearOrdinal = ordinal;
            return node;
        }

        private static ImmutableArray<OperandRole> NormalizeUseRoles(ImmutableArray<RegisterOperand> uses, ImmutableArray<OperandRole> useRoles)
        {
            if (uses.Length == 0)
                return ImmutableArray<OperandRole>.Empty;
            if (useRoles.IsDefaultOrEmpty)
                return ImmutableArray.CreateRange(new OperandRole[uses.Length]);
            if (useRoles.Length != uses.Length)
                throw new InvalidOperationException("Codegen use role count does not match use operand count.");
            return useRoles;
        }

        private static ImmutableArray<GenTreeValueKey> BuildValueKeys(ImmutableArray<GenTree> values)
        {
            if (values.IsDefaultOrEmpty)
                return ImmutableArray<GenTreeValueKey>.Empty;
            var result = ImmutableArray.CreateBuilder<GenTreeValueKey>(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                result.Add(value.LinearValueKey);
            }
            return result.ToImmutable();
        }
    }
}
