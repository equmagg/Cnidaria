using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace Cnidaria.Cs
{
    
    public static class BytecodeSerializer
    {
        // CNBC little endian
        private const uint BytecodeMagic = 0x43424E43;
        private const ushort BytecodeVersion = 1;

        // CNCF little endian
        private const uint ModuleMagic = 0x46434E43;
        private const ushort ModuleVersion = 1;

        public static byte[] SerializeFunctions(IReadOnlyDictionary<int, BytecodeFunction> functions)
        {
            if (functions is null)
                throw new ArgumentNullException(nameof(functions));
            
            using var ms = new MemoryStream(capacity: EstimateFunctionsSize(functions));
            using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

            bw.Write(BytecodeMagic);
            bw.Write(BytecodeVersion);
            bw.Write((ushort)0); // reserved
            bw.Write(functions.Count);

            foreach (var pair in functions.OrderBy(x => x.Key))
            {
                var key = pair.Key;
                var fn = pair.Value ?? throw new InvalidDataException("Function entry is null.");

                if (key != fn.MethodToken)
                    throw new InvalidDataException(
                        $"Dictionary key 0x{key:X8} does not match function.MethodToken 0x{fn.MethodToken:X8}.");

                WriteFunction(bw, fn);
            }

            bw.Flush();
            return ms.ToArray();
        }

        public static Dictionary<int, BytecodeFunction> DeserializeFunctions(ReadOnlySpan<byte> data)
        {
            using var ms = new MemoryStream(data.ToArray(), writable: false);
            using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            uint magic = br.ReadUInt32();
            if (magic != BytecodeMagic)
                throw new InvalidDataException("Invalid bytecode image magic.");

            ushort version = br.ReadUInt16();
            if (version != BytecodeVersion)
                throw new InvalidDataException($"Unsupported bytecode image version: {version}.");

            _ = br.ReadUInt16(); // reserved

            int functionCount = br.ReadInt32();
            if (functionCount < 0)
                throw new InvalidDataException("Negative function count.");

            var result = new Dictionary<int, BytecodeFunction>(functionCount);

            for (int i = 0; i < functionCount; i++)
            {
                var fn = ReadFunction(br);
                if (!result.TryAdd(fn.MethodToken, fn))
                    throw new InvalidDataException($"Duplicate method token 0x{fn.MethodToken:X8}.");
            }

            if (ms.Position != ms.Length)
                throw new InvalidDataException("Trailing bytes found in bytecode image.");

            return result;
        }

        public static byte[] SerializeCompiledModule(byte[] flatMetadata, IReadOnlyDictionary<int, BytecodeFunction> functions)
        {
            if (flatMetadata is null)
                throw new ArgumentNullException(nameof(flatMetadata));
            if (functions is null)
                throw new ArgumentNullException(nameof(functions));

            byte[] bytecode = SerializeFunctions(functions);

            using var ms = new MemoryStream(capacity: 16 + flatMetadata.Length + bytecode.Length);
            using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

            bw.Write(ModuleMagic);
            bw.Write(ModuleVersion);
            bw.Write((ushort)0); // reserved
            bw.Write(flatMetadata.Length);
            bw.Write(bytecode.Length);
            bw.Write(flatMetadata);
            bw.Write(bytecode);

            bw.Flush();
            return ms.ToArray();
        }

        public static (byte[] flatMetadata, IMetadataView metadata, Dictionary<int, BytecodeFunction> functions)
            DeserializeCompiledModule(ReadOnlySpan<byte> data)
        {
            using var ms = new MemoryStream(data.ToArray(), writable: false);
            using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            uint magic = br.ReadUInt32();
            if (magic != ModuleMagic)
                throw new InvalidDataException("Invalid compiled module magic.");

            ushort version = br.ReadUInt16();
            if (version != ModuleVersion)
                throw new InvalidDataException($"Unsupported compiled module version: {version}.");

            _ = br.ReadUInt16(); // reserved

            int metadataSize = br.ReadInt32();
            int bytecodeSize = br.ReadInt32();

            if (metadataSize < 0 || bytecodeSize < 0)
                throw new InvalidDataException("Negative section size.");

            byte[] flatMetadata = ReadExactly(br, metadataSize);
            byte[] bytecode = ReadExactly(br, bytecodeSize);

            if (ms.Position != ms.Length)
                throw new InvalidDataException("Trailing bytes found in compiled module image.");

            var metadata = new FlatMetadataView(flatMetadata);
            var functions = DeserializeFunctions(bytecode);

            return (flatMetadata, metadata, functions);
        }

        private static void WriteFunction(BinaryWriter bw, BytecodeFunction fn)
        {
            bw.Write(fn.MethodToken);
            bw.Write(fn.MaxStack);

            bw.Write(fn.LocalTypeTokens.Length);
            for (int i = 0; i < fn.LocalTypeTokens.Length; i++)
                bw.Write(fn.LocalTypeTokens[i]);

            bw.Write(fn.Instructions.Length);
            for (int i = 0; i < fn.Instructions.Length; i++)
            {
                var ins = fn.Instructions[i];
                bw.Write((byte)ins.Op);
                bw.Write(ins.Operand0);
                bw.Write(ins.Operand1);
                bw.Write(ins.Operand2);
                bw.Write(ins.Pop);
                bw.Write(ins.Push);
            }

            bw.Write(fn.ExceptionHandlers.Length);
            for (int i = 0; i < fn.ExceptionHandlers.Length; i++)
            {
                var eh = fn.ExceptionHandlers[i];
                bw.Write(eh.TryStartPc);
                bw.Write(eh.TryEndPc);
                bw.Write(eh.HandlerStartPc);
                bw.Write(eh.HandlerEndPc);
                bw.Write(eh.CatchTypeToken);
            }
        }

        private static BytecodeFunction ReadFunction(BinaryReader br)
        {
            int methodToken = br.ReadInt32();
            int maxStack = br.ReadInt32();
            if (maxStack < 0)
                throw new InvalidDataException("Negative MaxStack.");

            int localCount = br.ReadInt32();
            if (localCount < 0)
                throw new InvalidDataException("Negative local count.");

            var localBuilder = ImmutableArray.CreateBuilder<int>(localCount);
            for (int i = 0; i < localCount; i++)
                localBuilder.Add(br.ReadInt32());

            int instructionCount = br.ReadInt32();
            if (instructionCount < 0)
                throw new InvalidDataException("Negative instruction count.");

            var instructionBuilder = ImmutableArray.CreateBuilder<Instruction>(instructionCount);
            for (int i = 0; i < instructionCount; i++)
            {
                var op = (BytecodeOp)br.ReadByte();
                int operand0 = br.ReadInt32();
                int operand1 = br.ReadInt32();
                long operand2 = br.ReadInt64();
                short pop = br.ReadInt16();
                short push = br.ReadInt16();

                instructionBuilder.Add(new Instruction(op, operand0, operand1, operand2, pop, push));
            }

            int ehCount = br.ReadInt32();
            if (ehCount < 0)
                throw new InvalidDataException("Negative exception handler count.");

            var ehBuilder = ImmutableArray.CreateBuilder<ExceptionHandler>(ehCount);
            for (int i = 0; i < ehCount; i++)
            {
                int tryStartPc = br.ReadInt32();
                int tryEndPc = br.ReadInt32();
                int handlerStartPc = br.ReadInt32();
                int handlerEndPc = br.ReadInt32();
                int catchTypeToken = br.ReadInt32();

                ehBuilder.Add(new ExceptionHandler(
                    tryStartPc,
                    tryEndPc,
                    handlerStartPc,
                    handlerEndPc,
                    catchTypeToken));
            }

            return new BytecodeFunction(
                methodToken,
                localBuilder.ToImmutable(),
                instructionBuilder.ToImmutable(),
                maxStack,
                ehBuilder.ToImmutable());
        }

        private static byte[] ReadExactly(BinaryReader br, int count)
        {
            byte[] bytes = br.ReadBytes(count);
            if (bytes.Length != count)
                throw new EndOfStreamException($"Expected {count} bytes, got {bytes.Length}.");
            return bytes;
        }

        private static int EstimateFunctionsSize(IReadOnlyDictionary<int, BytecodeFunction> functions)
        {
            // Header
            int size = 12;

            foreach (var fn in functions.Values)
            {
                int locals = fn.LocalTypeTokens.IsDefault ? 0 : fn.LocalTypeTokens.Length;
                int insns = fn.Instructions.IsDefault ? 0 : fn.Instructions.Length;
                int ehs = fn.ExceptionHandlers.IsDefault ? 0 : fn.ExceptionHandlers.Length;

                // Function header
                // methodToken + maxStack + localCount + instructionCount + ehCount
                size += 4 + 4 + 4 + 4 + 4;

                // Locals
                size += locals * 4;

                // Instruction
                // op(byte) + operand0(int) + operand1(int) + operand2(long) + pop(short) + push(short)
                size += insns * (1 + 4 + 4 + 8 + 2 + 2);

                // ExceptionHandler
                size += ehs * (5 * 4);
            }

            return size;
        }
    }
}
