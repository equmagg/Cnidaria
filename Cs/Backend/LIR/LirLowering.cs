using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Cs
{
    internal static class GenTreeLinearLowerer
    {
        public static GenTreeMethod LowerMethod(GenTreeMethod rationalizedMethod, LinearRationalizationOptions? options = null)
        {
            if (rationalizedMethod is null)
                throw new ArgumentNullException(nameof(rationalizedMethod));

            options ??= LinearRationalizationOptions.Default;
            if (rationalizedMethod.Phase < GenTreeMethodPhase.RationalizedLir)
                throw new InvalidOperationException("Lowering requires a rationalized LIR method. Run GenTreeLinearIrRationalizer.RationalizeMethod first.");
            if (rationalizedMethod.LinearNodes.IsDefaultOrEmpty && rationalizedMethod.Blocks.Length != 0)
                throw new InvalidOperationException("Lowering requires rationalized LIR nodes. Call GenTreeLinearIrRationalizer.RationalizeMethod first.");

            LinearLoweringRefiner.Refine(rationalizedMethod);
            var result = LinearLiveness.Attach(rationalizedMethod);

            if (options.Validate)
                LinearVerifier.Verify(result);

            return result;
        }
    }
    internal static class LinearLoweringRefiner
    {
        public static void Refine(GenTreeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            for (int i = 0; i < method.LinearNodes.Length; i++)
            {
                var node = method.LinearNodes[i];
                if (node.LinearKind == GenTreeLinearKind.Tree)
                    RefineTreeNode(node);
            }
        }

        private static void RefineTreeNode(GenTree node)
        {
            var memoryAccess = GenTreeLinearLoweringClassifier.ClassifyMemoryAccess(node);
            var operands = RefineOperandFlags(node, memoryAccess);
            var uses = BuildUses(node, operands);
            var lowering = GenTreeLinearLoweringClassifier.Classify(node, node.RegisterResult, uses, node.LinearBlockId, memoryAccess);

            node.SetLinearState(
                node.LinearId,
                node.LinearBlockId,
                node.LinearOrdinal,
                node.LinearKind,
                node.RegisterResults,
                operands,
                uses,
                lowering,
                memoryAccess,
                node.LinearPhiCopyFromBlockId,
                node.LinearPhiCopyToBlockId);
        }

        private static ImmutableArray<LirOperandFlags> RefineOperandFlags(GenTree node, LinearMemoryAccess memoryAccess)
        {
            if (node.Operands.IsDefaultOrEmpty)
                return ImmutableArray<LirOperandFlags>.Empty;

            var builder = ImmutableArray.CreateBuilder<LirOperandFlags>(node.Operands.Length);
            int existingCount = node.OperandFlags.IsDefaultOrEmpty ? 0 : node.OperandFlags.Length;
            for (int i = 0; i < node.Operands.Length; i++)
            {
                var flags = i < existingCount ? node.OperandFlags[i] : LirOperandFlags.None;

                if ((flags & LirOperandFlags.Contained) == 0 &&
                    memoryAccess.HasValueOperand(i) &&
                    CanUseValueOperandFromHome(node, i, memoryAccess))
                {
                    flags |= LirOperandFlags.RegOptional;
                }

                builder.Add(flags);
            }

            return builder.ToImmutable();
        }

        private static bool CanUseValueOperandFromHome(GenTree node, int operandIndex, LinearMemoryAccess memoryAccess)
        {
            if (node.HasLoweringFlag(GenTreeLinearFlags.RequiresRegisterOperands))
                return false;

            if (memoryAccess.HasAddressOperand(operandIndex))
                return false;

            if (memoryAccess.IsBlockCopy)
                return true;

            var operand = node.Operands[operandIndex];
            var abi = MachineAbi.ClassifyStorageValue(operand.Type, operand.StackKind);
            return abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect or AbiValuePassingKind.MultiRegister;
        }

        private static ImmutableArray<GenTree> BuildUses(GenTree tree, ImmutableArray<LirOperandFlags> operandFlags)
        {
            if (tree.Operands.IsDefaultOrEmpty)
                return ImmutableArray<GenTree>.Empty;

            var builder = ImmutableArray.CreateBuilder<GenTree>(tree.Operands.Length);
            int flagCount = operandFlags.IsDefaultOrEmpty ? 0 : operandFlags.Length;
            for (int i = 0; i < tree.Operands.Length; i++)
            {
                var flags = i < flagCount ? operandFlags[i] : LirOperandFlags.None;
                if ((flags & LirOperandFlags.Contained) != 0)
                    continue;

                var operandTree = tree.Operands[i];
                if (operandTree.RegisterResult is not null)
                    builder.Add(operandTree.RegisterResult);
            }

            return builder.ToImmutable();
        }
    }
}
