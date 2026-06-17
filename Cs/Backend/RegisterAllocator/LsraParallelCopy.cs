using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;

namespace Cnidaria.Cs
{
    internal readonly struct RegisterResolvedMove
    {
        public readonly RegisterOperand Source;
        public readonly RegisterOperand Destination;
        public readonly GenTree? SourceValue;
        public readonly GenTree? DestinationValue;
        public readonly MoveFlags MoveFlags;

        public RegisterResolvedMove(
            RegisterOperand source,
            RegisterOperand destination,
            GenTree? sourceValue,
            GenTree? destinationValue,
            MoveFlags moveFlags = MoveFlags.None)
        {
            if (!source.IsNone && !destination.IsNone && source.RegisterClass != destination.RegisterClass)
                throw new InvalidOperationException($"Cannot move between different register classes: {source} -> {destination}.");

            Source = source;
            Destination = destination;
            SourceValue = sourceValue;
            DestinationValue = destinationValue;
            MoveFlags = moveFlags;
        }

        public RegisterResolvedMove WithSource(RegisterOperand source, GenTree? sourceValue)
            => new RegisterResolvedMove(source, Destination, sourceValue, DestinationValue, MoveFlags);
    }
    internal static class RegisterParallelCopyResolver
    {
        public static ImmutableArray<GenTree> Resolve(
            int fromBlockId,
            int toBlockId,
            List<RegisterResolvedMove> moves,
            Func<int> getScratchSpillSlot,
            MachineRegister generalScratchRegister,
            MachineRegister floatScratchRegister,
            ref int nextNodeId,
            Func<RegisterOperand, RegisterOperand, GenTree?, GenTree?, bool>? canEmitDirectMemoryMove = null,
            int phiCopyFromBlockId = -1,
            int phiCopyToBlockId = -1,
            bool preserveIdentityMoves = false)
        {
            if (moves is null)
                throw new ArgumentNullException(nameof(moves));
            if (getScratchSpillSlot is null)
                throw new ArgumentNullException(nameof(getScratchSpillSlot));
            ValidateScratch(generalScratchRegister, RegisterClass.General);
            ValidateScratch(floatScratchRegister, RegisterClass.Float);

            var result = ImmutableArray.CreateBuilder<GenTree>();

            for (int i = moves.Count - 1; i >= 0; i--)
            {
                var move = moves[i];
                if (move.Source.IsNone || move.Destination.IsNone)
                {
                    moves.RemoveAt(i);
                    continue;
                }

                if (move.Source.Equals(move.Destination))
                {
                    if (preserveIdentityMoves)
                    {
                        EmitMove(
                            result,
                            ref nextNodeId,
                            fromBlockId,
                            move,
                            generalScratchRegister,
                            floatScratchRegister,
                            canEmitDirectMemoryMove,
                            "edge B" + fromBlockId + "->B" + toBlockId + " identity",
                            phiCopyFromBlockId,
                            phiCopyToBlockId);
                    }
                    moves.RemoveAt(i);
                }
            }

            int scratchSlot = -1;

            while (moves.Count != 0)
            {
                int acyclicIndex = FindAcyclicMove(moves);
                if (acyclicIndex >= 0)
                {
                    EmitMove(result, ref nextNodeId, fromBlockId, moves[acyclicIndex], generalScratchRegister,
                        floatScratchRegister, canEmitDirectMemoryMove, "edge B" + fromBlockId + "->B" + toBlockId,
                        phiCopyFromBlockId, phiCopyToBlockId);
                    moves.RemoveAt(acyclicIndex);
                    continue;
                }

                if (scratchSlot < 0)
                    scratchSlot = getScratchSpillSlot();

                var cycleBreak = moves[0];
                EmitMove(
                    result,
                    ref nextNodeId,
                    fromBlockId,
                    new RegisterResolvedMove(
                        cycleBreak.Source,
                        RegisterOperand.ForSpillSlot(cycleBreak.Source.RegisterClass, scratchSlot),
                        cycleBreak.SourceValue,
                        cycleBreak.SourceValue,
                        cycleBreak.MoveFlags | MoveFlags.ParallelCopy | MoveFlags.Spill | MoveFlags.Internal),
                    generalScratchRegister,
                    floatScratchRegister,
                    canEmitDirectMemoryMove,
                    "parallel-copy cycle spill",
                    phiCopyFromBlockId,
                    phiCopyToBlockId);

                for (int i = 0; i < moves.Count; i++)
                {
                    if (moves[i].Source.Equals(cycleBreak.Source))
                    {
                        moves[i] = moves[i].WithSource(
                            RegisterOperand.ForSpillSlot(cycleBreak.Source.RegisterClass, scratchSlot),
                            cycleBreak.SourceValue);
                    }
                }
            }

            return result.ToImmutable();
        }

        private static int FindAcyclicMove(List<RegisterResolvedMove> moves)
        {
            for (int i = 0; i < moves.Count; i++)
            {
                var destination = moves[i].Destination;
                bool destinationIsStillSource = false;
                for (int j = 0; j < moves.Count; j++)
                {
                    if (i == j)
                        continue;
                    if (moves[j].Source.Equals(destination))
                    {
                        destinationIsStillSource = true;
                        break;
                    }
                }

                if (!destinationIsStillSource)
                    return i;
            }

            return -1;
        }

        private static void EmitMove(
            ImmutableArray<GenTree>.Builder result,
            ref int nextNodeId,
            int blockId,
            RegisterResolvedMove move,
            MachineRegister generalScratchRegister,
            MachineRegister floatScratchRegister,
            Func<RegisterOperand, RegisterOperand, GenTree?, GenTree?, bool>? canEmitDirectMemoryMove,
            string comment,
            int phiCopyFromBlockId = -1,
            int phiCopyToBlockId = -1)
        {
            if (!RequiresScratch(move.Source, move.Destination) ||
                (canEmitDirectMemoryMove?.Invoke(move.Destination, move.Source, move.DestinationValue, move.SourceValue) == true))
            {
                result.Add(GenTreeLirFactory.Move(
                    nextNodeId++,
                    blockId,
                    result.Count,
                    move.Destination,
                    move.Source,
                    move.DestinationValue,
                    move.SourceValue,
                    comment,
                    move.MoveFlags | MoveFlags.ParallelCopy,
                    phiCopyFromBlockId,
                    phiCopyToBlockId));
                return;
            }

            var moveClass = move.Destination.RegisterClass is RegisterClass.General or RegisterClass.Float
                ? move.Destination.RegisterClass
                : move.Source.RegisterClass;
            if (moveClass is not (RegisterClass.General or RegisterClass.Float))
                throw new InvalidOperationException($"Invalid parallel-copy move without a concrete register class: {move.Source} -> {move.Destination}.");

            var scratch = RegisterOperand.ForRegister(
                moveClass == RegisterClass.Float ? floatScratchRegister : generalScratchRegister);

            result.Add(GenTreeLirFactory.Move(
                nextNodeId++,
                blockId,
                result.Count,
                scratch,
                move.Source,
                destinationValue: null,
                sourceValue: move.SourceValue,
                comment: comment + " reload",
                moveFlags: move.MoveFlags | MoveFlags.ParallelCopy | MoveFlags.Reload,
                phiCopyFromBlockId: phiCopyFromBlockId,
                phiCopyToBlockId: phiCopyToBlockId));

            result.Add(GenTreeLirFactory.Move(
                nextNodeId++,
                blockId,
                result.Count,
                move.Destination,
                scratch,
                move.DestinationValue,
                sourceValue: null,
                comment: comment + " store",
                moveFlags: move.MoveFlags | MoveFlags.ParallelCopy | MoveFlags.Spill,
                phiCopyFromBlockId: phiCopyFromBlockId,
                phiCopyToBlockId: phiCopyToBlockId));
        }

        private static bool RequiresScratch(RegisterOperand source, RegisterOperand destination)
            => !source.IsNone && !destination.IsNone && !source.IsRegister && !destination.IsRegister;

        private static void ValidateScratch(MachineRegister register, RegisterClass registerClass)
        {
            if (!MachineRegisters.IsRegisterInClass(register, registerClass))
                throw new InvalidOperationException($"Invalid {registerClass} parallel-copy scratch register {register}.");
            if (!MachineRegisters.IsReserved(register))
                throw new InvalidOperationException($"Parallel-copy scratch register {MachineRegisters.Format(register)} must be reserved.");
        }
    }
}
