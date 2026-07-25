using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;

namespace Cnidaria.RiscV
{
    public enum RVPrivilegeMode : byte { User = 0, Supervisor = 1, Machine = 3 }

    public enum RVStopReason : byte { None, InstructionLimit, WaitingForInterrupt, Stopped, MachineFatalTrap }
    public enum RVMemoryAccess : byte { Execute, Load, Store }

    public readonly struct RVKeyboardEvent
    {
        public readonly ushort Usage;
        public readonly ushort Flags;

        public uint Encoded => (uint)Usage | ((uint)Flags << 16);

        public RVKeyboardEvent(ushort usage, ushort flags)
        {
            Usage = usage;
            Flags = flags;
        }

        public static RVKeyboardEvent KeyDown(ushort usage, bool repeat = false)
            => new RVKeyboardEvent(usage, (ushort)(repeat ? 3 : 1));

        public static RVKeyboardEvent KeyUp(ushort usage)
            => new RVKeyboardEvent(usage, 0);

        public static RVKeyboardEvent FromEncoded(uint encoded)
            => new RVKeyboardEvent((ushort)encoded, (ushort)(encoded >> 16));
    }

    public readonly struct RVTrapInfo
    {
        public readonly ulong Cause;
        public readonly ulong Value;
        public readonly ulong ProgramCounter;
        public readonly RVPrivilegeMode SourceMode;

        public bool IsInterrupt => (Cause & (1UL << 63)) != 0;

        public RVTrapInfo(ulong cause, ulong value, ulong programCounter, RVPrivilegeMode sourceMode)
        {
            Cause = cause;
            Value = value;
            ProgramCounter = programCounter;
            SourceMode = sourceMode;
        }
    }

    public readonly struct RVRunResult
    {
        public readonly ulong InstructionsRetired;
        public readonly ulong Steps;
        public readonly RVStopReason StopReason;
        public readonly RVTrapInfo LastTrap;
        public readonly bool HasTrap;

        public RVRunResult(ulong instructionsRetired, ulong steps, RVStopReason stopReason, RVTrapInfo lastTrap, bool hasTrap)
        {
            InstructionsRetired = instructionsRetired;
            Steps = steps;
            StopReason = stopReason;
            LastTrap = lastTrap;
            HasTrap = hasTrap;
        }
    }

    public enum RVTrapCause : ulong
    {
        InstructionAddressMisaligned = 0,
        InstructionAccessFault = 1,
        IllegalInstruction = 2,
        Breakpoint = 3,
        LoadAddressMisaligned = 4,
        LoadAccessFault = 5,
        StoreAddressMisaligned = 6,
        StoreAccessFault = 7,
        EnvironmentCallFromUMode = 8,
        EnvironmentCallFromSMode = 9,
        EnvironmentCallFromMMode = 11,
        InstructionPageFault = 12,
        LoadPageFault = 13,
        StorePageFault = 15,
    }

    public sealed class RVMachineConfig
    {
        public ulong RamBase { get; set; } = 0x80000000UL;
        public int RamSize { get; set; } = 128 * 1024 * 1024;
        public ulong ResetVector { get; set; } = 0x80000000UL;
        public ulong InitialStackPointer { get; set; }
        public ulong UartBase { get; set; } = 0x10000000UL;
        public ulong ClintBase { get; set; } = 0x02000000UL;
        public ulong PlicBase { get; set; } = 0x0C000000UL;
        public ulong BlockDeviceBase { get; set; } = 0x10001000UL;
        public ulong BlockDeviceStride { get; set; } = 0x1000UL;
        public int BlockDeviceSize { get; set; }
        public int BlockDeviceCount { get; set; } = 1;
        public ulong KeyboardBase { get; set; } = 0x10009000UL;
        public int KeyboardQueueCapacity { get; set; } = 256;
        public ulong InitialHartId { get; set; }
        public ulong InitialDeviceTreePointer { get; set; }
    }
    [InlineArray(32)]
    internal struct RegisterArray
    {
        private ulong _element;
    }
    [InlineArray(32)]
    internal struct VectorRegisterArray
    {
        private Vector512<ulong> _element;
    }
    public sealed class RiscVEmulator
    {
        private const ulong InterruptBit = 1UL << 63;
        private const ulong PageSize = 4096;
        private const ulong PageMask = PageSize - 1;
        private const ulong PteV = 1UL << 0;
        private const ulong PteR = 1UL << 1;
        private const ulong PteW = 1UL << 2;
        private const ulong PteX = 1UL << 3;
        private const ulong PteU = 1UL << 4;
        private const ulong PteA = 1UL << 6;
        private const ulong PteD = 1UL << 7;
        private const ulong MstatusSie = 1UL << 1;
        private const ulong MstatusMie = 1UL << 3;
        private const ulong MstatusSpie = 1UL << 5;
        private const ulong MstatusMpie = 1UL << 7;
        private const ulong MstatusSpp = 1UL << 8;
        private const ulong MstatusVsMask = 3UL << 9;
        private const ulong MstatusMppMask = 3UL << 11;
        private const ulong MstatusFsMask = 3UL << 13;
        private const ulong MstatusXsMask = 3UL << 15;
        private const ulong MstatusMprv = 1UL << 17;
        private const ulong MstatusSum = 1UL << 18;
        private const ulong MstatusMxr = 1UL << 19;
        private const ulong MstatusTvm = 1UL << 20;
        private const ulong MstatusTw = 1UL << 21;
        private const ulong MstatusTsr = 1UL << 22;
        private const ulong MstatusUxlMask = 3UL << 32;
        private const ulong MstatusSxlMask = 3UL << 34;
        private const ulong SstatusMask = MstatusSie | MstatusSpie | MstatusSpp | MstatusVsMask | MstatusFsMask | MstatusXsMask | MstatusSum | MstatusMxr | MstatusUxlMask;
        private const int VectorRegisterCount = 32;
        private const int VectorRegisterBytes = 64;
        private const int VectorLengthBits = 512;
        private const int VectorLengthBytes = VectorLengthBits / 8;
        private const int VectorElementLengthBits = 64;
        private const ulong VectorTypeIllegal = 1UL << 63;
        private const ulong SupervisorInterruptMask = (1UL << 1) | (1UL << 5) | (1UL << 9);
        private const ulong WritableMipMask =
            (1UL << (int)SupervisorSoftwareInterrupt) | (1UL << (int)SupervisorTimerInterrupt) | (1UL << (int)SupervisorExternalInterrupt);
        private const ulong MachineTimerInterrupt = 7;
        private const ulong MachineSoftwareInterrupt = 3;
        private const ulong SupervisorTimerInterrupt = 5;
        private const ulong SupervisorSoftwareInterrupt = 1;
        private const ulong MachineExternalInterrupt = 11;
        private const ulong SupervisorExternalInterrupt = 9;
        private const uint OpcodeMask = 0b0000000_00000_00000_000_00000_1111111U;
        private const uint Funct3Mask = 0b0000000_00000_00000_111_00000_0000000U;
        private const uint Funct6Mask = 0b1111110_00000_00000_000_00000_0000000U;
        private const uint Funct7Mask = 0b1111111_00000_00000_000_00000_0000000U;

        private RegisterArray _x;
        private RegisterArray _f;
        private VectorRegisterArray _v;
        private readonly byte[] _ram;
        private readonly ulong _ramBase;
        private readonly ulong _resetVector;
        private readonly RVUart16550 _uart;
        private readonly RVClint _clint;
        private readonly RVPlic _plic;
        private readonly RVMmioKeyboard? _keyboard;
        private readonly RVMmioBlockDevice[] _blocks;
        private readonly ulong _blockDeviceStride;
        private readonly ulong _hartId;
        private readonly ulong _initialDeviceTreePointer;

        private ulong _pc;
        private RVPrivilegeMode _mode;
        private bool _stopped;
        private bool _machineFatalTrap;
        private bool _waiting;
        private bool _hasTrap;
        private RVTrapInfo _lastTrap;
        private ulong _cycle;
        private ulong _instret;
        private ulong _reservationAddress;
        private bool _hasReservation;

        private ulong _fflags;
        private ulong _frm;
        private ulong _vstart;
        private ulong _vxsat;
        private ulong _vxrm;
        private ulong _vl;
        private ulong _vtype;
        private int _vsewBytes;
        private int _vgroupBytes;
        private int _vgroupRegisters;
        private int _vvlmax;
        private ulong _mstatus;
        private ulong _medeleg;
        private ulong _mideleg;
        private ulong _mie;
        private ulong _stvec;
        private ulong _mtvec;
        private ulong _mcounteren;
        private ulong _mscratch;
        private ulong _mepc;
        private ulong _mcause;
        private ulong _mtval;
        private ulong _mip;
        private ulong _menvcfg;
        private ulong _mseccfg;
        private ulong _scounteren;
        private ulong _senvcfg;
        private ulong _sscratch;
        private ulong _sepc;
        private ulong _scause;
        private ulong _stval;
        private ulong _satp;

        public Span<byte> Ram => _ram;
        public ulong RamBase => _ramBase;
        public ulong ProgramCounter { get => _pc; set => _pc = value; }
        public RVPrivilegeMode PrivilegeMode { get => _mode; set => _mode = value; }
        public bool WaitingForInterrupt => _waiting;
        public bool Stopped => _stopped;
        public ulong Cycle => _cycle;
        public ulong InstRet => _instret;
        public RVTrapInfo LastTrap => _lastTrap;
        public bool HasTrap => _hasTrap;
        public RVUart16550 Uart => _uart;
        public RVClint Clint => _clint;
        public RVPlic Plic => _plic;
        public RVMmioKeyboard? Keyboard => _keyboard;
        public RVMmioBlockDevice? BlockDevice => _blocks.Length == 0 ? null : _blocks[0];
        public ReadOnlySpan<RVMmioBlockDevice> BlockDevices => _blocks;
        public ulong BlockDeviceStride => _blockDeviceStride;
        public ulong HartId => _hartId;
        public ReadOnlySpan<ulong> IntegerRegisters => _x;
        public ReadOnlySpan<ulong> FloatingPointRegisters => _f;
        public ReadOnlySpan<Vector512<ulong>> VectorRegisters => _v;
        public ulong VectorLength => _vl;
        public ulong VectorType => _vtype;

        public RiscVEmulator(RVMachineConfig? config = null)
        {
            config ??= new RVMachineConfig();
            if (config.RamSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(config));
            _ramBase = config.RamBase;
            _resetVector = config.ResetVector;
            _ram = new byte[config.RamSize];
            _uart = new RVUart16550(config.UartBase);
            _clint = new RVClint(config.ClintBase);
            _plic = new RVPlic(config.PlicBase);
            _keyboard = config.KeyboardQueueCapacity > 0
                ? new RVMmioKeyboard(config.KeyboardBase, config.KeyboardQueueCapacity, RVPlic.KeyboardSource)
                : null;
            _blockDeviceStride = config.BlockDeviceStride;
            _hartId = config.InitialHartId;
            _initialDeviceTreePointer = config.InitialDeviceTreePointer;
            if (config.BlockDeviceSize > 0)
            {
                if (config.BlockDeviceCount <= 0)
                    throw new ArgumentOutOfRangeException(nameof(config));
                if (config.BlockDeviceCount > RVPlic.BlockDeviceSourceCount)
                    throw new ArgumentOutOfRangeException(nameof(config));
                if (config.BlockDeviceStride < RVMmioBlockDevice.RegisterWindowSize)
                    throw new ArgumentOutOfRangeException(nameof(config));
                _blocks = new RVMmioBlockDevice[config.BlockDeviceCount];
                for (int i = 0; i < _blocks.Length; i++)
                {
                    ulong address = checked(config.BlockDeviceBase + config.BlockDeviceStride * (ulong)i);
                    _blocks[i] = new RVMmioBlockDevice(address, config.BlockDeviceSize, RVPlic.BlockDeviceFirstSource + i);
                }
            }
            else
            {
                _blocks = Array.Empty<RVMmioBlockDevice>();
            }
            Reset(config.InitialStackPointer);
        }

        public void Reset(ulong initialStackPointer = 0)
        {
            Span<ulong> spanX = _x;
            spanX.Clear();
            Span<ulong> spanF = _f;
            spanF.Clear();
            Span<Vector512<ulong>> spanV = _v;
            spanV.Clear();
            Array.Clear(_ram, 0, _ram.Length);
            _pc = _resetVector;
            _mode = RVPrivilegeMode.Machine;
            _stopped = false;
            _machineFatalTrap = false;
            _waiting = false;
            _hasTrap = false;
            _lastTrap = default;
            _cycle = 0;
            _instret = 0;
            _reservationAddress = 0;
            _hasReservation = false;
            _fflags = 0;
            _frm = 0;
            _vstart = 0;
            _vxsat = 0;
            _vxrm = 0;
            _vl = 0;
            _vtype = VectorTypeIllegal;
            _mstatus = MstatusMpie | (3UL << 11) | MstatusFsMask | (2UL << 32) | (2UL << 34);
            _medeleg = 0;
            _mideleg = 0;
            _mie = 0;
            _stvec = 0;
            _mtvec = 0;
            _mcounteren = ulong.MaxValue;
            _mscratch = 0;
            _mepc = 0;
            _mcause = 0;
            _mtval = 0;
            _mip = 0;
            _menvcfg = 0;
            _mseccfg = 0;
            _scounteren = ulong.MaxValue;
            _senvcfg = 0;
            _sscratch = 0;
            _sepc = 0;
            _scause = 0;
            _stval = 0;
            _satp = 0;
            _uart.Reset();
            _clint.Reset();
            _plic.Reset();
            _keyboard?.Reset();
            foreach (var block in _blocks)
                block.Reset();
            _x[10] = _hartId;
            _x[11] = _initialDeviceTreePointer;
            if (initialStackPointer != 0)
                _x[2] = initialStackPointer;
        }

        public void LoadImage(byte[] image, ulong physicalAddress, bool setProgramCounter = true)
        {
            if (image is null)
                throw new ArgumentNullException(nameof(image));
            if (image.Length > _ram.Length)
                throw new ArgumentOutOfRangeException(nameof(image));
            ulong offset = physicalAddress - _ramBase;
            if (physicalAddress < _ramBase || offset > (ulong)(_ram.Length - image.Length))
                throw new ArgumentOutOfRangeException(nameof(physicalAddress));
            Buffer.BlockCopy(image, 0, _ram, (int)offset, image.Length);
            if (setProgramCounter)
                _pc = physicalAddress;
        }

        public void LoadImage(RVLinkedImage image, bool setProgramCounter = true)
        {
            if (image is null)
                throw new ArgumentNullException(nameof(image));
            LoadImage(image.Bytes.ToArray(), image.ImageBase, false);
            if (setProgramCounter)
                _pc = image.EntryAddress;
        }

        public void Stop()
        {
            _machineFatalTrap = false;
            _stopped = true;
            _waiting = false;
        }

        public RVRunResult Run(ulong instructionLimit)
        {
            ulong retired = 0;
            ulong steps = 0;
            ulong trapCause;
            ulong trapValue;

            while (steps < instructionLimit && !_stopped)
            {
                steps++;
                _cycle++;
                _clint.Tick();
                UpdatePendingInterrupts();

                ulong interrupt = SelectInterrupt();
                if (_waiting)
                {
                    if (interrupt == ulong.MaxValue)
                        break;
                    _waiting = false;
                }

                if (interrupt != ulong.MaxValue)
                {
                    EnterTrap(interrupt | InterruptBit, 0, _pc);
                    continue;
                }

                ulong pc = _pc;
                if ((pc & 3) != 0)
                {
                    EnterTrap((ulong)RVTrapCause.InstructionAddressMisaligned, pc, pc);
                    continue;
                }

                if (!TryReadMemory(pc, 4, RVMemoryAccess.Execute, out ulong rawInstruction, out trapCause, out trapValue))
                {
                    EnterTrap(trapCause, trapValue, pc);
                    continue;
                }

                uint instruction = (uint)rawInstruction;
                if ((instruction & 3) != 3)
                {
                    EnterTrap((ulong)RVTrapCause.IllegalInstruction, instruction, pc);
                    continue;
                }

                ulong nextPc = pc + 4;
                int rd = (int)((instruction >> 7) & 31);
                bool trapped = false;
                trapCause = (ulong)RVTrapCause.IllegalInstruction;
                trapValue = instruction;

                switch (instruction & OpcodeMask)
                {
                    case 0x37: // LUI
                        if (rd != 0) _x[rd] = (ulong)(long)ImmU(instruction);
                        break;

                    case 0x17: // AUIPC
                        if (rd != 0) _x[rd] = pc + (ulong)(long)ImmU(instruction);
                        break;

                    case 0x6F: // JAL
                        {
                            ulong target = pc + (ulong)ImmJ(instruction);
                            if ((target & 3) != 0)
                            {
                                trapped = true;
                                trapCause = (ulong)RVTrapCause.InstructionAddressMisaligned;
                                trapValue = target;
                                break;
                            }
                            if (rd != 0) _x[rd] = pc + 4;
                            nextPc = target;
                            break;
                        }

                    case 0x67: // JALR
                        {
                            if ((int)((instruction >> 12) & 7) != 0)
                            {
                                trapped = true;
                                break;
                            }
                            ulong target = (_x[(int)((instruction >> 15) & 31)] + (ulong)ImmI(instruction)) & ~1UL;
                            if ((target & 3) != 0)
                            {
                                trapped = true;
                                trapCause = (ulong)RVTrapCause.InstructionAddressMisaligned;
                                trapValue = target;
                                break;
                            }
                            if (rd != 0) _x[rd] = pc + 4;
                            nextPc = target;
                            break;
                        }

                    case 0x63: // BRANCH
                        {
                            bool take;
                            ulong a = _x[(int)((instruction >> 15) & 31)];
                            ulong b = _x[(int)((instruction >> 20) & 31)];
                            switch (instruction & Funct3Mask)
                            {
                                case 0x0000U: take = a == b; break;
                                case 0x1000U: take = a != b; break;
                                case 0x4000U: take = (long)a < (long)b; break;
                                case 0x5000U: take = (long)a >= (long)b; break;
                                case 0x6000U: take = a < b; break;
                                case 0x7000U: take = a >= b; break;
                                default: trapped = true; take = false; break;
                            }
                            if (trapped || !take)
                                break;
                            ulong target = pc + (ulong)ImmB(instruction);
                            if ((target & 3) != 0)
                            {
                                trapped = true;
                                trapCause = (ulong)RVTrapCause.InstructionAddressMisaligned;
                                trapValue = target;
                                break;
                            }
                            nextPc = target;
                            break;
                        }

                    case 0x03: // LOAD
                        {
                            int size;
                            bool unsignedLoad = false;
                            switch (instruction & Funct3Mask)
                            {
                                case 0x0000U: size = 1; break;
                                case 0x1000U: size = 2; break;
                                case 0x2000U: size = 4; break;
                                case 0x3000U: size = 8; break;
                                case 0x4000U: size = 1; unsignedLoad = true; break;
                                case 0x5000U: size = 2; unsignedLoad = true; break;
                                case 0x6000U: size = 4; unsignedLoad = true; break;
                                default: trapped = true; size = 0; break;
                            }
                            if (trapped)
                                break;
                            ulong address = _x[(int)((instruction >> 15) & 31)] + (ulong)ImmI(instruction);
                            if ((address & (ulong)(size - 1)) != 0)
                            {
                                trapped = true;
                                trapCause = (ulong)RVTrapCause.LoadAddressMisaligned;
                                trapValue = address;
                                break;
                            }
                            if (!TryReadMemory(address, size, RVMemoryAccess.Load, out ulong value, out trapCause, out trapValue))
                            {
                                trapped = true;
                                break;
                            }
                            if (!unsignedLoad)
                            {
                                if (size == 1) value = (ulong)(long)(sbyte)value;
                                else if (size == 2) value = (ulong)(long)(short)value;
                                else if (size == 4) value = (ulong)(long)(int)value;
                            }
                            if (rd != 0) _x[rd] = value;
                            break;
                        }

                    case 0x23: // STORE
                        {
                            int size;
                            switch (instruction & Funct3Mask)
                            {
                                case 0x0000U: size = 1; break;
                                case 0x1000U: size = 2; break;
                                case 0x2000U: size = 4; break;
                                case 0x3000U: size = 8; break;
                                default: trapped = true; size = 0; break;
                            }
                            if (trapped)
                                break;
                            ulong address = _x[(int)((instruction >> 15) & 31)] + (ulong)ImmS(instruction);
                            if ((address & (ulong)(size - 1)) != 0)
                            {
                                trapped = true;
                                trapCause = (ulong)RVTrapCause.StoreAddressMisaligned;
                                trapValue = address;
                                break;
                            }
                            if (!TryWriteMemory(address, size, _x[(int)((instruction >> 20) & 31)], out trapCause, out trapValue))
                                trapped = true;
                            break;
                        }

                    case 0x13: // IMM
                        {
                            ulong a = _x[(int)((instruction >> 15) & 31)];
                            if (TryExecuteBitmanipImmediate(instruction, a, out ulong bitmanipValue))
                            {
                                _x[rd] = bitmanipValue;
                                break;
                            }

                            long imm = ImmI(instruction);
                            switch (instruction & Funct3Mask)
                            {
                                case 0x0000U: _x[rd] = a + (ulong)imm; break;
                                case 0x2000U: _x[rd] = (long)a < imm ? 1UL : 0UL; break;
                                case 0x3000U: _x[rd] = a < (ulong)imm ? 1UL : 0UL; break;
                                case 0x4000U: _x[rd] = a ^ (ulong)imm; break;
                                case 0x6000U: _x[rd] = a | (ulong)imm; break;
                                case 0x7000U: _x[rd] = a & (ulong)imm; break;
                                case 0x1000U:
                                    {
                                        int shamt = (int)((instruction >> 20) & 0x3F);
                                        if ((instruction & Funct6Mask) != 0)
                                        {
                                            trapped = true;
                                            break;
                                        }

                                        if (rd != 0)
                                            _x[rd] = a << shamt;
                                        break;
                                    }
                                case 0x5000U:
                                    {
                                        uint funct6 = instruction & Funct6Mask;
                                        int shamt = (int)((instruction >> 20) & 0x3F);

                                        if (funct6 == 0x00000000U)
                                        {
                                            if (rd != 0)
                                                _x[rd] = a >> shamt;
                                        }
                                        else if (funct6 == 0x40000000U)
                                        {
                                            if (rd != 0)
                                                _x[rd] = (ulong)((long)a >> shamt);
                                        }
                                        else
                                        {
                                            trapped = true;
                                        }

                                        break;
                                    }
                                default:
                                    trapped = true;
                                    break;
                            }
                            break;
                        }

                    case 0x1B: //IMM32
                        {
                            int rs1 = (int)((instruction >> 15) & 31);
                            uint funct3 = instruction & Funct3Mask;

                            if (funct3 == 0x1000U)
                            {
                                int immediate = (int)((instruction >> 20) & 0xFFFU);
                                if (immediate == 0x600)
                                {
                                    _x[rd] = (ulong)System.Numerics.BitOperations.LeadingZeroCount((uint)_x[rs1]); break;
                                }
                                else if (immediate == 0x601)
                                {
                                    _x[rd] = (ulong)System.Numerics.BitOperations.TrailingZeroCount((uint)_x[rs1]); break;
                                }
                                else if (immediate == 0x602)
                                {
                                    _x[rd] = (ulong)System.Numerics.BitOperations.PopCount((uint)_x[rs1]); break;
                                }
                                else if ((instruction & Funct6Mask) == 0x08000000U)
                                {
                                    _x[rd] = (ulong)(uint)_x[rs1] << (int)((instruction >> 20) & 63);
                                    break;
                                }
                            }
                            else if (funct3 == 0x5000U && (instruction & Funct7Mask) == 0x60000000U)
                            {
                                uint value = System.Numerics.BitOperations.RotateRight((uint)_x[rs1], (int)((instruction >> 20) & 31));
                                _x[rd] = SignExtend32(value);
                                break;
                            }

                            int shamt = (int)((instruction >> 20) & 31);
                            uint funct7 = instruction & Funct7Mask;
                            switch (instruction & Funct3Mask)
                            {
                                case 0x0000U: _x[rd] = SignExtend32((uint)((int)_x[rs1] + (int)ImmI(instruction))); break;
                                case 0x1000U:
                                    if (funct7 != 0) { trapped = true; break; }
                                    _x[rd] = SignExtend32((uint)_x[rs1] << shamt);
                                    break;
                                case 0x5000U:
                                    if (funct7 == 0) { _x[rd] = SignExtend32((uint)_x[rs1] >> shamt); }
                                    else if (funct7 == 0x40000000U) { _x[rd] = SignExtend32((uint)((int)_x[rs1] >> shamt)); }
                                    else trapped = true;
                                    break;
                                default:
                                    trapped = true;
                                    break;
                            }
                            break;
                        }

                    case 0x33: // OP
                        {
                            ulong a = _x[(int)((instruction >> 15) & 31)];
                            ulong b = _x[(int)((instruction >> 20) & 31)];
                            if (TryExecuteBitmanipRegister(instruction, a, b, out ulong bitmanipValue))
                            {
                                _x[rd] = bitmanipValue;
                                break;
                            }

                            ulong value;
                            uint funct7 = instruction & Funct7Mask;
                            if (funct7 == 0x00000000U)
                            {
                                switch (instruction & Funct3Mask)
                                {
                                    case 0x0000U: value = a + b; break;
                                    case 0x1000U: value = a << (int)(b & 63); break;
                                    case 0x2000U: value = (long)a < (long)b ? 1UL : 0UL; break;
                                    case 0x3000U: value = a < b ? 1UL : 0UL; break;
                                    case 0x4000U: value = a ^ b; break;
                                    case 0x5000U: value = a >> (int)(b & 63); break;
                                    case 0x6000U: value = a | b; break;
                                    case 0x7000U: value = a & b; break;
                                    default: trapped = true; value = 0; break;
                                }
                            }
                            else if (funct7 == 0x40000000U)
                            {
                                switch (instruction & Funct3Mask)
                                {
                                    case 0x0000U: value = a - b; break;
                                    case 0x5000U: value = (ulong)((long)a >> (int)(b & 63)); break;
                                    default: trapped = true; value = 0; break;
                                }
                            }
                            else if (funct7 == 0x02000000U)
                            {
                                switch (instruction & Funct3Mask)
                                {
                                    case 0x0000U: value = a * b; break;
                                    case 0x1000U: value = Mulh((long)a, (long)b); break;
                                    case 0x2000U: value = Mulhsu((long)a, b); break;
                                    case 0x3000U: value = Mulhu(a, b); break;
                                    case 0x4000U: value = Div(a, b); break;
                                    case 0x5000U: value = b == 0 ? ulong.MaxValue : a / b; break;
                                    case 0x6000U: value = Rem(a, b); break;
                                    case 0x7000U: value = b == 0 ? a : a % b; break;
                                    default: trapped = true; value = 0; break;
                                }
                            }
                            else
                            {
                                trapped = true;
                                value = 0;
                            }
                            if (!trapped) _x[rd] = value;
                            break;
                        }

                    case 0x3B: // OP32
                        {
                            ulong a = _x[(int)((instruction >> 15) & 31)];
                            ulong b = _x[(int)((instruction >> 20) & 31)];

                            uint funct7 = instruction & Funct7Mask;

                            if (funct7 == 0x08000000U)
                            {
                                uint funct3 = instruction & Funct3Mask;
                                if (funct3 == 0x0000U)
                                {
                                    _x[rd] = (uint)a + b;
                                    break;
                                }
                                if (funct3 == 0x4000U && ((instruction >> 20) & 31) == 0)
                                {
                                    _x[rd] = (ushort)a;
                                    break;
                                }
                            }
                            else if (funct7 == 0x20000000U)
                            {
                                uint funct3 = instruction & Funct3Mask;
                                if (funct3 == 0x2000U)
                                {
                                    _x[rd] = ((uint)a << 1) + b; break;
                                }
                                else if (funct3 == 0x4000U)
                                {
                                    _x[rd] = ((uint)a << 2) + b; break;
                                }
                                else if (funct3 == 0x6000U)
                                {
                                    _x[rd] = ((uint)a << 3) + b; break;
                                }
                            }
                            else if (funct7 == 0x60000000U)
                            {
                                uint funct3 = instruction & Funct3Mask;
                                if (funct3 == 0x1000U)
                                {
                                    _x[rd] = SignExtend32(System.Numerics.BitOperations.RotateLeft((uint)a, (int)(b & 31))); break;
                                }
                                else if (funct3 == 0x5000U)
                                {
                                    _x[rd] = SignExtend32(System.Numerics.BitOperations.RotateRight((uint)a, (int)(b & 31))); break;
                                }
                            }

                            ulong value;
                            if (funct7 == 0x00000000U)
                            {
                                switch (instruction & Funct3Mask)
                                {
                                    case 0x0000U: value = SignExtend32((uint)((int)a + (int)b)); break;
                                    case 0x1000U: value = SignExtend32((uint)a << (int)(b & 31)); break;
                                    case 0x5000U: value = SignExtend32((uint)a >> (int)(b & 31)); break;
                                    default: trapped = true; value = 0; break;
                                }
                            }
                            else if (funct7 == 0x40000000U)
                            {
                                switch (instruction & Funct3Mask)
                                {
                                    case 0x0000U: value = SignExtend32((uint)((int)a - (int)b)); break;
                                    case 0x5000U: value = SignExtend32((uint)((int)a >> (int)(b & 31))); break;
                                    default: trapped = true; value = 0; break;
                                }
                            }
                            else if (funct7 == 0x02000000U)
                            {
                                switch (instruction & Funct3Mask)
                                {
                                    case 0x0000U: value = SignExtend32((uint)((int)a * (int)b)); break;
                                    case 0x4000U: value = SignExtend32((uint)DivW((int)a, (int)b)); break;
                                    case 0x5000U: value = SignExtend32((uint)b == 0 ? uint.MaxValue : (uint)a / (uint)b); break;
                                    case 0x6000U: value = SignExtend32((uint)RemW((int)a, (int)b)); break;
                                    case 0x7000U: value = SignExtend32((uint)b == 0 ? (uint)a : (uint)a % (uint)b); break;
                                    default: trapped = true; value = 0; break;
                                }
                            }
                            else
                            {
                                trapped = true;
                                value = 0;
                            }
                            if (!trapped && rd != 0) _x[rd] = value;
                            break;
                        }

                    case 0x0F: // MISC
                        {
                            uint funct3 = instruction & Funct3Mask;
                            if (funct3 != 0x0000U && funct3 != 0x1000U)
                                trapped = true;
                        }
                        break;

                    case 0x73: // SYSTEM
                        {
                            uint funct3 = instruction & Funct3Mask;

                            if (funct3 == 0x0000U)
                            {
                                switch (instruction)
                                {
                                    case 0x00000073: // ECALL
                                        trapCause = _mode == RVPrivilegeMode.User
                                            ? (ulong)RVTrapCause.EnvironmentCallFromUMode
                                            : _mode == RVPrivilegeMode.Supervisor
                                                ? (ulong)RVTrapCause.EnvironmentCallFromSMode
                                                : (ulong)RVTrapCause.EnvironmentCallFromMMode;
                                        trapValue = 0;
                                        trapped = true;
                                        goto outer_break;
                                    case 0x00100073: // EBREAK
                                        trapCause = (ulong)RVTrapCause.Breakpoint;
                                        trapValue = pc;
                                        trapped = true;
                                        goto outer_break;
                                    case 0x10200073: // SRET
                                        if (_mode < RVPrivilegeMode.Supervisor || (_mode == RVPrivilegeMode.Supervisor && (_mstatus & MstatusTsr) != 0))
                                        {
                                            trapped = true;
                                            goto outer_break;
                                        }
                                        ReturnFromSupervisorTrap(ref nextPc);
                                        goto outer_break;
                                    case 0x30200073: // MRET
                                        if (_mode != RVPrivilegeMode.Machine)
                                        {
                                            trapped = true;
                                            goto outer_break;
                                        }
                                        ReturnFromMachineTrap(ref nextPc);
                                        goto outer_break;
                                    case 0x10500073: // WFI
                                        if (_mode < RVPrivilegeMode.Machine && (_mstatus & MstatusTw) != 0)
                                        {
                                            trapped = true;
                                            goto outer_break;
                                        }
                                        _waiting = true;
                                        goto outer_break;
                                    default:
                                        if (IsPrivilegedFenceInstruction(instruction))
                                        {
                                            if (_mode < RVPrivilegeMode.Supervisor || (_mode == RVPrivilegeMode.Supervisor && (_mstatus & MstatusTvm) != 0))
                                                trapped = true;
                                            goto outer_break;
                                        }
                                        trapped = true;
                                        goto outer_break;
                                }
                            }

                            int rs1 = (int)((instruction >> 15) & 31);
                            int csr = (int)(instruction >> 20);
                            bool write = funct3 == 0x1000U || funct3 == 0x5000U ||
                                ((funct3 == 0x2000U || funct3 == 0x3000U || funct3 == 0x6000U || funct3 == 0x7000U) && rs1 != 0);
                            ulong old;
                            if ((funct3 == 0x1000U || funct3 == 0x5000U) && rd == 0)
                            {
                                old = 0;
                                if (!CheckCsrAccess(csr, write))
                                {
                                    trapped = true;
                                    break;
                                }
                            }
                            else
                            {
                                if (!TryReadCsr(csr, true, out old))
                                {
                                    trapped = true;
                                    break;
                                }
                                if (write && !CheckCsrAccess(csr, true))
                                {
                                    trapped = true;
                                    break;
                                }
                            }

                            ulong src = funct3 >= 0x5000U ? (ulong)rs1 : _x[rs1];
                            switch (funct3)
                            {
                                case 0x1000U:
                                case 0x5000U:
                                    if (!TryWriteCsr(csr, src, false))
                                    {
                                        trapped = true;
                                        goto outer_break;
                                    }
                                    break;
                                case 0x2000U:
                                case 0x6000U:
                                    if (rs1 != 0 && !TryWriteCsr(csr, old | src, false))
                                    {
                                        trapped = true;
                                        goto outer_break;
                                    }
                                    break;
                                case 0x3000U:
                                case 0x7000U:
                                    if (rs1 != 0 && !TryWriteCsr(csr, old & ~src, false))
                                    {
                                        trapped = true;
                                        goto outer_break;
                                    }
                                    break;
                                default:
                                    trapped = true;
                                    goto outer_break;
                            }
                            _x[rd] = old;

                        outer_break:
                            break;
                        }

                    case 0x57: // VECTOR
                        {
                            int funct3 = (int)((instruction & Funct3Mask) >> 12);
                            if (funct3 == 7)
                            {
                                int rs1 = (int)((instruction >> 15) & 31);
                                ulong avl;
                                ulong type;

                                if ((instruction & 0xC0000000U) == 0xC0000000U)
                                {
                                    avl = (ulong)rs1;
                                    type = (instruction >> 20) & 0x3FFUL;
                                }
                                else if ((instruction & Funct6Mask) == 0x80000000U)
                                {
                                    int rs2 = (int)((instruction >> 20) & 31);
                                    avl = rs1 == 0 && rd == 0 ? _vl : rs1 == 0 ? ulong.MaxValue : _x[rs1];
                                    type = _x[rs2];
                                }
                                else if ((instruction & 0x80000000U) == 0)
                                {
                                    avl = rs1 == 0 && rd == 0 ? _vl : rs1 == 0 ? ulong.MaxValue : _x[rs1];
                                    type = (instruction >> 20) & 0x7FFUL;
                                }
                                else
                                {
                                    trapped = true;
                                    break;
                                }

                                if (!TryDecodeVectorType(type, out int vsetSewBytes, out int vsetGroupBytes, out int vsetGroupRegisters, out int vsetVlmax))
                                {
                                    _vtype = VectorTypeIllegal;
                                    _vsewBytes = 0;
                                    _vgroupBytes = 0;
                                    _vgroupRegisters = 0;
                                    _vvlmax = 0;

                                    _vl = 0;
                                    _vstart = 0;
                                    _x[rd] = _vl;
                                    _mstatus |= MstatusVsMask;
                                    break;
                                }
                                _vtype = type & 0xFFUL;
                                _vsewBytes = vsetSewBytes;
                                _vgroupBytes = vsetGroupBytes;
                                _vgroupRegisters = vsetGroupRegisters;
                                _vvlmax = vsetVlmax;
                                if (avl == 0)
                                    _vl = 0;
                                else if (avl <= (ulong)vsetVlmax)
                                    _vl = avl;
                                else
                                    _vl = (ulong)vsetVlmax;
                                _vstart = 0;
                                _x[rd] = _vl;
                                _mstatus |= MstatusVsMask;
                                break;
                            }

                            if ((_vtype & VectorTypeIllegal) != 0 || _vstart > _vl)
                            {
                                trapped = true;
                                break;
                            }

                            int sewBytes = _vsewBytes;
                            int groupBytes = _vgroupBytes;
                            int groupRegisters = _vgroupRegisters;
                            int vlmax = _vvlmax;

                            bool floatOperation = funct3 == 1 || funct3 == 5;
                            if (floatOperation && sewBytes != 4 && sewBytes != 8)
                            {
                                trapped = true;
                                break;
                            }

                            int funct6 = (int)((instruction >> 26) & 0x3F);
                            bool narrowingShift = (funct6 == 44 || funct6 == 45) && (funct3 == 0 || funct3 == 4 || funct3 == 3);

                            int source2Registers = groupRegisters;

                            if (narrowingShift)
                            {
                                if (sewBytes == 8 || !TryGetVectorGroupRegisters(groupBytes << 1, out source2Registers))
                                {
                                    trapped = true;
                                    break;
                                }
                            }

                            int vs2 = (int)((instruction >> 20) & 31);
                            if (!CheckVectorGroup(vs2, source2Registers))
                            {
                                trapped = true;
                                break;
                            }

                            int source1 = (int)((instruction >> 15) & 31);
                            bool vectorSource1 = funct3 == 0 || funct3 == 1 || funct3 == 2;

                            if (vectorSource1 && !CheckVectorGroup(source1, groupRegisters))
                            {
                                trapped = true;
                                break;
                            }

                            bool maskDestination = floatOperation
                                ? (funct3 switch { 1 => 0x1B00_0000UL, 5 => 0xBB00_0000UL, _ => 0 } & (1UL << funct6)) != 0
                                : (funct3 switch { 0 => 0x3F00_0000UL, 3 => 0xF300_0000UL, 4 => 0xFF00_0000UL, _ => 0 } & (1UL << funct6)) != 0;
                            int vd = (int)((instruction >> 7) & 31);

                            if (!CheckVectorGroup(vd, maskDestination ? 1 : groupRegisters))
                            {
                                trapped = true;
                                break;
                            }

                            bool unmasked = ((instruction >> 25) & 1) != 0;

                            int vl = (int)_vl;
                            int start = (int)_vstart;
                            int sewBits = sewBytes * 8;
                            ulong mask = ElementMask(sewBits);
                            ulong scalar = 0;
                            long signedImmediate = (source1 & 16) != 0 ? source1 - 32 : source1;

                            if (funct3 == 4 || funct3 == 5 || funct3 == 6)
                                scalar = floatOperation ? _f[source1] : _x[source1] & mask;
                            else if (funct3 == 3)
                                scalar = (ulong)((funct6 == 37 || funct6 == 40 || funct6 == 41 || funct6 == 44 || funct6 == 45 || funct6 == 48)
                                        ? source1 : signedImmediate) & mask;
                            else if (!vectorSource1)
                            {
                                trapped = true;
                                break;
                            }

                            int source2Bytes = narrowingShift ? sewBytes << 1 : sewBytes;
                            ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);
                            int source1Offset = source1 * VectorRegisterBytes + start * sewBytes;
                            int source2Offset = vs2 * VectorRegisterBytes + start * source2Bytes;
                            int destinationOffset = vd * VectorRegisterBytes + start * sewBytes;
                            int gatherBaseOffset = vs2 * VectorRegisterBytes;
                            bool vectorFailed = false;
                            for (int i = start; i < vl; i++)
                            {
                                int currentSource1Offset = source1Offset;
                                int currentSource2Offset = source2Offset;
                                int currentDestinationOffset = destinationOffset;
                                source1Offset += sewBytes;
                                source2Offset += source2Bytes;
                                destinationOffset += sewBytes;

                                if (!unmasked && !ReadVectorMaskBit(ref vectorBytes, i))
                                    continue;

                                ulong a = vectorSource1 ? ReadVectorElement(ref vectorBytes, currentSource1Offset, sewBytes) : scalar;
                                ulong b = ReadVectorElement(ref vectorBytes, currentSource2Offset, source2Bytes);

                                if (floatOperation)
                                {
                                    if (maskDestination)
                                    {
                                        if (!TryEvaluateVectorFloatMaskOperation(funct6, funct3, a, b, sewBytes, out bool condition))
                                        {
                                            vectorFailed = true;
                                            break;
                                        }
                                        WriteVectorMaskBit(ref vectorBytes, vd, i, condition);
                                        continue;
                                    }

                                    if (!TryEvaluateVectorFloatOperation(funct6, funct3, a, b, sewBytes, out ulong floatResult))
                                    {
                                        vectorFailed = true;
                                        break;
                                    }
                                    WriteVectorElement(ref vectorBytes, currentDestinationOffset, sewBytes, floatResult);
                                    continue;
                                }

                                if (maskDestination)
                                {
                                    if (!TryEvaluateVectorMaskOperation(funct6, funct3, a, b, sewBits, out bool condition))
                                    {
                                        vectorFailed = true;
                                        break;
                                    }
                                    WriteVectorMaskBit(ref vectorBytes, vd, i, condition);
                                    continue;
                                }

                                ulong result;
                                if (narrowingShift)
                                {
                                    int sourceBits = sewBits << 1;
                                    int shift = (int)(a & (ulong)(sourceBits - 1));
                                    result = funct6 == 44 ? b >> shift : (ulong)(SignExtendElement(b, sourceBits) >> shift);
                                }
                                else if (funct6 == 48)
                                {
                                    if (funct3 != 0 && funct3 != 4 && funct3 != 3)
                                    {
                                        vectorFailed = true;
                                        break;
                                    }
                                    result = a < (ulong)vlmax ? ReadVectorElement(ref vectorBytes, gatherBaseOffset + (int)a * sewBytes, sewBytes) : 0;
                                }
                                else if (funct6 == 41 || funct6 == 43 || funct6 == 45 || funct6 == 47)
                                {
                                    if (!TryEvaluateVectorMultiplyAddOperation(
                                        funct6, funct3, a, b, ReadVectorElement(ref vectorBytes, currentDestinationOffset, sewBytes), out result))
                                    {
                                        vectorFailed = true;
                                        break;
                                    }
                                }
                                else if (!TryEvaluateVectorIntegerOperation(funct6, funct3, a, b, sewBits, mask, out result))
                                {
                                    vectorFailed = true;
                                    break;
                                }

                                WriteVectorElement(ref vectorBytes, currentDestinationOffset, sewBytes, result & mask);
                            }

                            if (vectorFailed)
                            {
                                trapped = true;
                                break;
                            }

                            _vstart = 0;
                            _mstatus |= MstatusVsMask;
                        }
                        break;

                    case 0x2F: // AMO
                        {
                            uint funct3 = instruction & Funct3Mask;
                            int size = funct3 == 0x2000U ? 4 : funct3 == 0x3000U ? 8 : 0;
                            if (size == 0)
                            {
                                trapped = true;
                                break;
                            }
                            ulong address = _x[(int)((instruction >> 15) & 31)];
                            if ((address & (ulong)(size - 1)) != 0)
                            {
                                trapCause = (ulong)RVTrapCause.LoadAddressMisaligned;
                                trapValue = address;
                                trapped = true;
                                break;
                            }
                            if (!TryReadMemory(address, size, RVMemoryAccess.Load, out ulong old, out trapCause, out trapValue))
                            {
                                trapped = true;
                                break;
                            }

                            uint op = instruction & 0xF8000000U;
                            if (op == 0x10000000U)
                            {
                                if (rd != 0)
                                    _x[rd] = size == 4 ? SignExtend32((uint)old) : old;
                                _reservationAddress = address;
                                _hasReservation = true;
                                break;
                            }

                            int rs2 = (int)((instruction >> 20) & 31);
                            ulong result;
                            if (op == 0x18000000U)
                            {
                                result = _hasReservation && _reservationAddress == address ? 0UL : 1UL;
                                if (result == 0 && !TryWriteMemory(address, size, _x[rs2], out trapCause, out trapValue))
                                {
                                    trapped = true;
                                    break;
                                }
                                _hasReservation = false;
                                _x[rd] = result;
                                break;
                            }

                            ulong src = size == 4 ? (uint)_x[rs2] : _x[rs2];
                            ulong write;
                            switch (op)
                            {
                                case 0x08000000U: write = src; break;
                                case 0x00000000U: write = old + src; break;
                                case 0x20000000U: write = old ^ src; break;
                                case 0x40000000U: write = old | src; break;
                                case 0x60000000U: write = old & src; break;
                                case 0x80000000U: write = size == 4 ? (ulong)(uint)Math.Min((int)old, (int)src) : (ulong)Math.Min((long)old, (long)src); break;
                                case 0xA0000000U: write = size == 4 ? (ulong)(uint)Math.Max((int)old, (int)src) : (ulong)Math.Max((long)old, (long)src); break;
                                case 0xC0000000U: write = size == 4 ? Math.Min((uint)old, (uint)src) : Math.Min(old, src); break;
                                case 0xE0000000U: write = size == 4 ? Math.Max((uint)old, (uint)src) : Math.Max(old, src); break;
                                default: trapped = true; goto outer_break;
                            }
                            if (!TryWriteMemory(address, size, write, out trapCause, out trapValue))
                            {
                                trapped = true;
                                break;
                            }
                            _x[rd] = size == 4 ? SignExtend32((uint)old) : old;
                        outer_break:
                            break;
                        }

                    case 0x07: // LOAD FP
                        {
                            uint funct3 = instruction & Funct3Mask;
                            if (funct3 == 0x0000U || funct3 == 0x5000U || funct3 == 0x6000U || funct3 == 0x7000U)
                            {
                                if (!ExecuteVectorLoad(instruction, out trapCause, out trapValue))
                                    trapped = true;
                                break;
                            }
                            int size;
                            if (funct3 == 0x2000U) size = 4;
                            else if (funct3 == 0x3000U) size = 8;
                            else { trapped = true; break; }
                            ulong address = _x[(int)((instruction >> 15) & 31)] + (ulong)ImmI(instruction);
                            if ((address & (ulong)(size - 1)) != 0)
                            {
                                trapped = true;
                                trapCause = (ulong)RVTrapCause.LoadAddressMisaligned;
                                trapValue = address;
                                break;
                            }
                            if (!TryReadMemory(address, size, RVMemoryAccess.Load, out ulong value, out trapCause, out trapValue))
                            {
                                trapped = true;
                                break;
                            }
                            _f[rd] = size == 4 ? 0xFFFFFFFF00000000UL | (uint)value : value;
                            break;
                        }

                    case 0x27: // STORE FP
                        {
                            uint funct3 = instruction & Funct3Mask;
                            if (funct3 == 0x0000U || funct3 == 0x5000U || funct3 == 0x6000U || funct3 == 0x7000U)
                            {
                                if (!ExecuteVectorStore(instruction, out trapCause, out trapValue))
                                    trapped = true;
                                break;
                            }
                            int size;
                            if (funct3 == 0x2000U) size = 4;
                            else if (funct3 == 0x3000U) size = 8;
                            else { trapped = true; break; }
                            ulong address = _x[(int)((instruction >> 15) & 31)] + (ulong)ImmS(instruction);
                            if ((address & (ulong)(size - 1)) != 0)
                            {
                                trapped = true;
                                trapCause = (ulong)RVTrapCause.StoreAddressMisaligned;
                                trapValue = address;
                                break;
                            }
                            if (!TryWriteMemory(address, size, _f[(int)((instruction >> 20) & 31)], out trapCause, out trapValue))
                                trapped = true;
                            break;
                        }

                    case 0x43: // MADD
                    case 0x47: // MSUB
                    case 0x4B: // NMSUB
                    case 0x4F: // NMADD
                    case 0x53: // FP
                        {
                            uint opcode = instruction & OpcodeMask;
                            int rs1 = (int)((instruction >> 15) & 31);
                            int rs2 = (int)((instruction >> 20) & 31);

                            if (opcode != 0x53U)
                            {
                                int rs3 = (int)((instruction >> 27) & 31);
                                if ((instruction & (1U << 25)) == 0)
                                {
                                    float a = ReadFloat32(rs1);
                                    float b = ReadFloat32(rs2);
                                    float c = ReadFloat32(rs3);
                                    float r = opcode switch
                                    {
                                        0x43 => a * b + c,
                                        0x47 => a * b - c,
                                        0x4B => -(a * b) + c,
                                        _ => -(a * b) - c,
                                    };
                                    WriteFloat32(rd, r);
                                    break;
                                }
                                else
                                {
                                    double a = ReadFloat64(rs1);
                                    double b = ReadFloat64(rs2);
                                    double c = ReadFloat64(rs3);
                                    double r = opcode switch
                                    {
                                        0x43 => a * b + c,
                                        0x47 => a * b - c,
                                        0x4B => -(a * b) + c,
                                        0x4F => -(a * b) - c,
                                        _ => -(a * b) - c
                                    };
                                    WriteFloat64(rd, r);
                                    break;
                                }
                            }

                            switch ((int)((instruction >> 25) & 127))
                            {
                                case 0x00: WriteFloat32(rd, ReadFloat32(rs1) + ReadFloat32(rs2)); goto outer_break;
                                case 0x01: WriteFloat64(rd, ReadFloat64(rs1) + ReadFloat64(rs2)); goto outer_break;
                                case 0x04: WriteFloat32(rd, ReadFloat32(rs1) - ReadFloat32(rs2)); goto outer_break;
                                case 0x05: WriteFloat64(rd, ReadFloat64(rs1) - ReadFloat64(rs2)); goto outer_break;
                                case 0x08: WriteFloat32(rd, ReadFloat32(rs1) * ReadFloat32(rs2)); goto outer_break;
                                case 0x09: WriteFloat64(rd, ReadFloat64(rs1) * ReadFloat64(rs2)); goto outer_break;
                                case 0x0C: WriteFloat32(rd, ReadFloat32(rs1) / ReadFloat32(rs2)); goto outer_break;
                                case 0x0D: WriteFloat64(rd, ReadFloat64(rs1) / ReadFloat64(rs2)); goto outer_break;
                                case 0x10:
                                    if (!ExecuteFloatSign(false, (int)((instruction >> 12) & 7), rd, rs1, rs2))
                                        trapped = true;
                                    goto outer_break;
                                case 0x11:
                                    if (!ExecuteFloatSign(true, (int)((instruction >> 12) & 7), rd, rs1, rs2))
                                        trapped = true;
                                    goto outer_break;
                                case 0x14:
                                    if (!ExecuteFloatMinMax(false, (int)((instruction >> 12) & 7), rd, rs1, rs2))
                                        trapped = true;
                                    goto outer_break;
                                case 0x15:
                                    if (!ExecuteFloatMinMax(true, (int)((instruction >> 12) & 7), rd, rs1, rs2))
                                        trapped = true;
                                    goto outer_break;
                                case 0x20:
                                    if (rs2 != 1)
                                    {
                                        trapped = true;
                                        goto outer_break;
                                    }
                                    WriteFloat32(rd, (float)ReadFloat64(rs1));
                                    goto outer_break;
                                case 0x21:
                                    if (rs2 != 0)
                                    {
                                        trapped = true;
                                        goto outer_break;
                                    }
                                    WriteFloat64(rd, ReadFloat32(rs1));
                                    goto outer_break;
                                case 0x2C:
                                    if (rs2 != 0)
                                    {
                                        trapped = true;
                                        goto outer_break;
                                    }
                                    WriteFloat32(rd, MathF.Sqrt(ReadFloat32(rs1)));
                                    goto outer_break;
                                case 0x2D:
                                    if (rs2 != 0)
                                    {
                                        trapped = true;
                                        goto outer_break;
                                    }
                                    WriteFloat64(rd, Math.Sqrt(ReadFloat64(rs1)));
                                    goto outer_break;
                                case 0x50:
                                    if (!ExecuteFloatCompare(false, (int)((instruction >> 12) & 7), rd, rs1, rs2))
                                        trapped = true;
                                    goto outer_break;
                                case 0x51:
                                    if (!ExecuteFloatCompare(true, (int)((instruction >> 12) & 7), rd, rs1, rs2))
                                        trapped = true;
                                    goto outer_break;
                                case 0x60:
                                    if (!ExecuteFloatToInt(false, rs2, rd, rs1))
                                        trapped = true;
                                    goto outer_break;
                                case 0x61:
                                    if (!ExecuteFloatToInt(true, rs2, rd, rs1))
                                        trapped = true;
                                    goto outer_break;
                                case 0x68:
                                    if (!ExecuteIntToFloat(false, rs2, rd, rs1))
                                        trapped = true;
                                    goto outer_break;
                                case 0x69:
                                    if (!ExecuteIntToFloat(true, rs2, rd, rs1))
                                        trapped = true;
                                    goto outer_break;
                                case 0x70:
                                    if (rs2 != 0)
                                    {
                                        trapped = true;
                                        goto outer_break;
                                    }
                                    {
                                        int funct3 = (int)((instruction >> 12) & 7);
                                        if (funct3 == 0) { _x[rd] = SignExtend32((uint)_f[rs1]); goto outer_break; }
                                        if (funct3 == 1) { _x[rd] = ClassifyFloat32((uint)_f[rs1]); goto outer_break; }
                                    }
                                    trapped = true;
                                    goto outer_break;
                                case 0x71:
                                    if (rs2 != 0)
                                    {
                                        trapped = true;
                                        goto outer_break;
                                    }
                                    {
                                        int funct3 = (int)((instruction >> 12) & 7);
                                        if (funct3 == 0) { _x[rd] = _f[rs1]; goto outer_break; }
                                        if (funct3 == 1) { _x[rd] = ClassifyFloat64(_f[rs1]); goto outer_break; }
                                    }
                                    trapped = true;
                                    goto outer_break;
                                case 0x78:
                                    if (rs2 != 0 || (int)((instruction >> 12) & 7) != 0)
                                    {
                                        trapped = true;
                                        goto outer_break;
                                    }
                                    WriteFloat32(rd, BitConverter.Int32BitsToSingle((int)_x[rs1]));
                                    goto outer_break;
                                case 0x79:
                                    if (rs2 != 0 || (int)((instruction >> 12) & 7) != 0)
                                    {
                                        trapped = true;
                                        goto outer_break;
                                    }
                                    _f[rd] = _x[rs1];
                                    break;
                                default:
                                    trapped = true;
                                    goto outer_break;
                            }
                        outer_break:
                            break;
                        }


                    default:
                        trapped = true;
                        break;
                }

                if (trapped)
                {
                    EnterTrap(trapCause, trapValue, pc);
                    continue;
                }

                _pc = nextPc;
                _x[0] = 0;
                _instret++;
                retired++;
            }

            RVStopReason reason = _machineFatalTrap
                ? RVStopReason.MachineFatalTrap
                : _stopped
                    ? RVStopReason.Stopped
                : _waiting
                    ? RVStopReason.WaitingForInterrupt
                    : steps == instructionLimit
                        ? RVStopReason.InstructionLimit
                        : RVStopReason.None;

            return new RVRunResult(retired, steps, reason, _lastTrap, _hasTrap);
        }


        private void UpdatePendingInterrupts()
        {
            const ulong GeneratedInterruptMask = (1UL << (int)MachineTimerInterrupt)
                | (1UL << (int)MachineSoftwareInterrupt)
                | (1UL << (int)MachineExternalInterrupt)
                | (1UL << (int)SupervisorExternalInterrupt);
            ulong mip = _mip & ~GeneratedInterruptMask;

            if (_clint.MsIp != 0)
                mip |= 1UL << (int)MachineSoftwareInterrupt;
            if (_clint.MTime >= _clint.MTimeCmp)
                mip |= 1UL << (int)MachineTimerInterrupt;

            foreach (var block in _blocks)
                _plic.SetSourcePending(block.InterruptSource, block.InterruptPending);
            if (_keyboard != null)
                _plic.SetSourcePending(_keyboard.InterruptSource, _keyboard.InterruptPending);
            _plic.SetSourcePending(RVPlic.UartSource, _uart.InterruptPending);
            if (_plic.HasPendingInterrupt(RVPlic.MachineContext))
                mip |= 1UL << (int)MachineExternalInterrupt;
            if (_plic.HasPendingInterrupt(RVPlic.SupervisorContext))
                mip |= 1UL << (int)SupervisorExternalInterrupt;

            _mip = mip;
        }


        private ulong SelectInterrupt()
        {
            ulong pending = _mip & _mie;
            if (pending == 0)
                return ulong.MaxValue;

            ulong cause;
            if ((pending & (1UL << (int)MachineExternalInterrupt)) != 0) cause = MachineExternalInterrupt;
            else if ((pending & (1UL << (int)MachineSoftwareInterrupt)) != 0) cause = MachineSoftwareInterrupt;
            else if ((pending & (1UL << (int)MachineTimerInterrupt)) != 0) cause = MachineTimerInterrupt;
            else if ((pending & (1UL << (int)SupervisorExternalInterrupt)) != 0) cause = SupervisorExternalInterrupt;
            else if ((pending & (1UL << (int)SupervisorSoftwareInterrupt)) != 0) cause = SupervisorSoftwareInterrupt;
            else if ((pending & (1UL << (int)SupervisorTimerInterrupt)) != 0) cause = SupervisorTimerInterrupt;
            else return ulong.MaxValue;

            ulong bit = 1UL << (int)cause;
            bool delegated = (_mideleg & bit) != 0;
            if (delegated)
            {
                if (_mode < RVPrivilegeMode.Supervisor)
                    return cause;
                if (_mode == RVPrivilegeMode.Supervisor && (_mstatus & MstatusSie) != 0)
                    return cause;
                return ulong.MaxValue;
            }

            if (_mode < RVPrivilegeMode.Machine)
                return cause;
            if (_mode == RVPrivilegeMode.Machine && (_mstatus & MstatusMie) != 0)
                return cause;
            return ulong.MaxValue;
        }

        private void EnterTrap(ulong cause, ulong value, ulong pc)
        {
            bool interrupt = (cause & InterruptBit) != 0;
            ulong causeCode = cause & ~InterruptBit;
            bool delegated = _mode <= RVPrivilegeMode.Supervisor && ((interrupt ? _mideleg : _medeleg) & (1UL << (int)causeCode)) != 0;
            _hasTrap = true;
            _lastTrap = new RVTrapInfo(cause, value, pc, _mode);

            if (delegated)
            {
                ulong status = _mstatus;
                status = (status & ~MstatusSpie) | ((status & MstatusSie) != 0 ? MstatusSpie : 0);
                status &= ~MstatusSie;
                status = _mode == RVPrivilegeMode.Supervisor ? status | MstatusSpp : status & ~MstatusSpp;
                _mstatus = status;
                _sepc = pc;
                _scause = cause;
                _stval = value;
                _mode = RVPrivilegeMode.Supervisor;
                _pc = TrapVector(_stvec, causeCode, interrupt);
                return;
            }

            ulong mstatus = _mstatus;
            mstatus = (mstatus & ~MstatusMpie) | ((mstatus & MstatusMie) != 0 ? MstatusMpie : 0);
            mstatus &= ~MstatusMie;
            mstatus = (mstatus & ~MstatusMppMask) | ((ulong)_mode << 11);
            _mstatus = mstatus;
            _mepc = pc;
            _mcause = cause;
            _mtval = value;
            if (!interrupt && _mode == RVPrivilegeMode.Machine)
            {
                _machineFatalTrap = true;
                _stopped = true;
                _waiting = false;
                _pc = pc;
                return;
            }

            _mode = RVPrivilegeMode.Machine;
            _pc = TrapVector(_mtvec, causeCode, interrupt);
        }

        private static ulong TrapVector(ulong vector, ulong cause, bool interrupt)
        {
            ulong baseAddress = vector & ~3UL;
            return interrupt && (vector & 3) == 1 ? baseAddress + cause * 4 : baseAddress;
        }

        private static bool IsPrivilegedFenceInstruction(uint instruction)
        {
            if ((instruction & 0x0000707FU) != 0x00000073U)
                return false;

            int funct7 = (int)((instruction >> 25) & 0x7F);
            return funct7 == 0x09 || funct7 == 0x0B || funct7 == 0x11 || funct7 == 0x31;
        }

        private void ReturnFromMachineTrap(ref ulong nextPc)
        {
            ulong status = _mstatus;
            RVPrivilegeMode target = (RVPrivilegeMode)((status >> 11) & 3);
            status = (status & ~MstatusMie) | ((status & MstatusMpie) != 0 ? MstatusMie : 0);
            status |= MstatusMpie;
            status &= ~MstatusMppMask;
            status &= ~MstatusMprv;
            _mstatus = status;
            _mode = target;
            nextPc = _mepc;
        }

        private void ReturnFromSupervisorTrap(ref ulong nextPc)
        {
            ulong status = _mstatus;
            RVPrivilegeMode target = (status & MstatusSpp) != 0 ? RVPrivilegeMode.Supervisor : RVPrivilegeMode.User;
            status = (status & ~MstatusSie) | ((status & MstatusSpie) != 0 ? MstatusSie : 0);
            status |= MstatusSpie;
            status &= ~MstatusSpp;
            _mstatus = status;
            _mode = target;
            nextPc = _sepc;
        }

        private bool TryReadCsr(int csr, bool checkAccess, out ulong value)
        {
            value = 0;
            if (checkAccess && !CheckCsrAccess(csr, false))
                return false;

            switch ((RVCsr)csr)
            {
                case RVCsr.FFlags: value = _fflags; return true;
                case RVCsr.FRm: value = _frm; return true;
                case RVCsr.FCsr: value = (_frm << 5) | (_fflags & 31); return true;
                case RVCsr.VStart: value = _vstart; return true;
                case RVCsr.VxSat: value = _vxsat; return true;
                case RVCsr.VxRm: value = _vxrm; return true;
                case RVCsr.VCsr: value = ((_vxrm & 3) << 1) | (_vxsat & 1); return true;
                case RVCsr.Cycle: value = _cycle; return CheckCounterAccess(0);
                case RVCsr.Time: value = _clint.MTime; return CheckCounterAccess(1);
                case RVCsr.InstRet: value = _instret; return CheckCounterAccess(2);
                case RVCsr.VL: value = _vl; return true;
                case RVCsr.VType: value = _vtype; return true;
                case RVCsr.VLenB: value = VectorLengthBytes; return true;
                case RVCsr.SStatus: value = _mstatus & SstatusMask; return true;
                case RVCsr.SIe: value = _mie & SupervisorInterruptMask; return true;
                case RVCsr.STVec: value = _stvec; return true;
                case RVCsr.SCounterEn: value = _scounteren; return true;
                case RVCsr.SEnvCfg: value = _senvcfg; return true;
                case RVCsr.SScratch: value = _sscratch; return true;
                case RVCsr.SEpc: value = _sepc; return true;
                case RVCsr.SCause: value = _scause; return true;
                case RVCsr.STVal: value = _stval; return true;
                case RVCsr.SIp: value = _mip & SupervisorInterruptMask; return true;
                case RVCsr.SAtp:
                    if (_mode == RVPrivilegeMode.Supervisor && (_mstatus & MstatusTvm) != 0)
                        return false;
                    value = _satp;
                    return true;
                case RVCsr.MVendorId:
                case RVCsr.MArchId:
                case RVCsr.MImpId:
                case RVCsr.MConfigPtr: value = 0; return true;
                case RVCsr.MHartId: value = _hartId; return true;
                case RVCsr.MStatus: value = _mstatus; return true;
                case RVCsr.MIsa: value = BuildMisa(); return true;
                case RVCsr.MEDeleg: value = _medeleg; return true;
                case RVCsr.MIDeleg: value = _mideleg; return true;
                case RVCsr.MIe: value = _mie; return true;
                case RVCsr.MTVec: value = _mtvec; return true;
                case RVCsr.MCounterEn: value = _mcounteren; return true;
                case RVCsr.MScratch: value = _mscratch; return true;
                case RVCsr.MEpc: value = _mepc; return true;
                case RVCsr.MCause: value = _mcause; return true;
                case RVCsr.MTVal: value = _mtval; return true;
                case RVCsr.MIp: value = _mip; return true;
                case RVCsr.MEnvCfg: value = _menvcfg; return true;
                case RVCsr.MSecCfg: value = _mseccfg; return true;
                case RVCsr.MCycle: value = _cycle; return true;
                case RVCsr.MInstRet: value = _instret; return true;
                default: return false;
            }
        }

        private bool TryWriteCsr(int csr, ulong value, bool checkAccess)
        {
            if (checkAccess && !CheckCsrAccess(csr, true))
                return false;

            switch ((RVCsr)csr)
            {
                case RVCsr.FFlags: _fflags = value & 31; return true;
                case RVCsr.FRm: _frm = value & 7; return true;
                case RVCsr.FCsr: _fflags = value & 31; _frm = (value >> 5) & 7; return true;
                case RVCsr.VStart: _vstart = value; return true;
                case RVCsr.VxSat: _vxsat = value & 1; return true;
                case RVCsr.VxRm: _vxrm = value & 3; return true;
                case RVCsr.VCsr: _vxsat = value & 1; _vxrm = (value >> 1) & 3; return true;
                case RVCsr.SStatus: _mstatus = (_mstatus & ~SstatusMask) | (value & SstatusMask); return true;
                case RVCsr.SIe: _mie = (_mie & ~SupervisorInterruptMask) | (value & SupervisorInterruptMask); return true;
                case RVCsr.STVec: _stvec = value; return true;
                case RVCsr.SCounterEn: _scounteren = value; return true;
                case RVCsr.SEnvCfg: _senvcfg = value; return true;
                case RVCsr.SScratch: _sscratch = value; return true;
                case RVCsr.SEpc: _sepc = value & ~1UL; return true;
                case RVCsr.SCause: _scause = value; return true;
                case RVCsr.STVal: _stval = value; return true;
                case RVCsr.SIp: _mip = (_mip & ~SupervisorInterruptMask) | (value & SupervisorInterruptMask); return true;
                case RVCsr.SAtp:
                    if (_mode == RVPrivilegeMode.Supervisor && (_mstatus & MstatusTvm) != 0)
                        return false;
                    _satp = NormalizeSatp(value);
                    return true;
                case RVCsr.MStatus: _mstatus = NormalizeMstatus(value); return true;
                case RVCsr.MEDeleg: _medeleg = value; return true;
                case RVCsr.MIDeleg: _mideleg = value; return true;
                case RVCsr.MIe: _mie = value; return true;
                case RVCsr.MTVec: _mtvec = value; return true;
                case RVCsr.MCounterEn: _mcounteren = value; return true;
                case RVCsr.MScratch: _mscratch = value; return true;
                case RVCsr.MEpc: _mepc = value & ~1UL; return true;
                case RVCsr.MCause: _mcause = value; return true;
                case RVCsr.MTVal: _mtval = value; return true;
                case RVCsr.MIp:
                    {
                        ulong generated =
                            (1UL << (int)MachineSoftwareInterrupt)
                            | (1UL << (int)MachineTimerInterrupt)
                            | (1UL << (int)MachineExternalInterrupt);
                        _mip = (_mip & generated) | (value & WritableMipMask);
                    }
                    return true;
                case RVCsr.MEnvCfg: _menvcfg = value; return true;
                case RVCsr.MSecCfg: _mseccfg = value; return true;
                case RVCsr.MCycle: _cycle = value; return true;
                case RVCsr.MInstRet: _instret = value; return true;
                case RVCsr.MIsa:
                case RVCsr.MVendorId:
                case RVCsr.MArchId:
                case RVCsr.MImpId:
                case RVCsr.MHartId:
                case RVCsr.MConfigPtr:
                case RVCsr.VL:
                case RVCsr.VType:
                case RVCsr.VLenB:
                case RVCsr.Cycle:
                case RVCsr.Time:
                case RVCsr.InstRet:
                    return true;
                default:
                    return false;
            }
        }

        private static ulong NormalizeMstatus(ulong value)
        {
            if (((value & MstatusMppMask) >> 11) == 2)
                value &= ~MstatusMppMask;
            return value;
        }

        private static ulong NormalizeSatp(ulong value)
        {
            ulong mode = value >> 60;
            if (mode == 0)
                return 0;
            return value & ((0xFUL << 60) | (0xFFFFUL << 44) | ((1UL << 44) - 1));
        }


        private bool CheckCsrAccess(int csr, bool write)
        {
            if (!IsImplementedCsr(csr))
                return false;
            int minimumPrivilege = (csr >> 8) & 3;
            if (minimumPrivilege == 2 || (int)_mode < minimumPrivilege)
                return false;
            if (write && (csr & 0xC00) == 0xC00)
                return false;
            return true;
        }

        private bool CheckCounterAccess(int counter)
        {
            ulong mask = 1UL << counter;
            if (_mode < RVPrivilegeMode.Machine && (_mcounteren & mask) == 0)
                return false;
            if (_mode == RVPrivilegeMode.User && (_scounteren & mask) == 0)
                return false;
            return true;
        }

        private static bool IsImplementedCsr(int csr)
        {
            switch ((RVCsr)csr)
            {
                case RVCsr.FFlags:
                case RVCsr.FRm:
                case RVCsr.FCsr:
                case RVCsr.VStart:
                case RVCsr.VxSat:
                case RVCsr.VxRm:
                case RVCsr.VCsr:
                case RVCsr.Cycle:
                case RVCsr.Time:
                case RVCsr.InstRet:
                case RVCsr.VL:
                case RVCsr.VType:
                case RVCsr.VLenB:
                case RVCsr.SStatus:
                case RVCsr.SIe:
                case RVCsr.STVec:
                case RVCsr.SCounterEn:
                case RVCsr.SEnvCfg:
                case RVCsr.SScratch:
                case RVCsr.SEpc:
                case RVCsr.SCause:
                case RVCsr.STVal:
                case RVCsr.SIp:
                case RVCsr.SAtp:
                case RVCsr.MVendorId:
                case RVCsr.MArchId:
                case RVCsr.MImpId:
                case RVCsr.MHartId:
                case RVCsr.MConfigPtr:
                case RVCsr.MStatus:
                case RVCsr.MIsa:
                case RVCsr.MEDeleg:
                case RVCsr.MIDeleg:
                case RVCsr.MIe:
                case RVCsr.MTVec:
                case RVCsr.MCounterEn:
                case RVCsr.MScratch:
                case RVCsr.MEpc:
                case RVCsr.MCause:
                case RVCsr.MTVal:
                case RVCsr.MIp:
                case RVCsr.MEnvCfg:
                case RVCsr.MSecCfg:
                case RVCsr.MCycle:
                case RVCsr.MInstRet:
                    return true;
                default:
                    return false;
            }
        }

        private static ulong BuildMisa()
            => (2UL << 62) | (1UL << ('i' - 'a')) | (1UL << ('m' - 'a')) | (1UL << ('a' - 'a')) | (1UL << ('f' - 'a')) | (1UL << ('d' - 'a')) | (1UL << ('s' - 'a')) | (1UL << ('u' - 'a')) | (1UL << ('v' - 'a'));

        private bool TryReadMemory(ulong virtualAddress, int size, RVMemoryAccess access, out ulong value, out ulong trapCause, out ulong trapValue)
        {
            value = 0;
            if (!TryTranslate(virtualAddress, access, out ulong physicalAddress, out trapCause, out trapValue))
                return false;
            return TryReadPhysical(physicalAddress, size, access, out value, out trapCause, out trapValue);
        }

        private bool TryWriteMemory(ulong virtualAddress, int size, ulong value, out ulong trapCause, out ulong trapValue)
        {
            if (!TryTranslate(virtualAddress, RVMemoryAccess.Store, out ulong physicalAddress, out trapCause, out trapValue))
                return false;
            bool ok = TryWritePhysical(physicalAddress, size, value, out trapCause, out trapValue);
            if (ok && _hasReservation && physicalAddress == _reservationAddress)
                _hasReservation = false;
            return ok;
        }

        private bool TryTranslate(ulong virtualAddress, RVMemoryAccess access, out ulong physicalAddress, out ulong trapCause, out ulong trapValue)
        {
            trapCause = 0;
            trapValue = virtualAddress;
            physicalAddress = virtualAddress;

            RVPrivilegeMode effectiveMode = _mode;
            if (access != RVMemoryAccess.Execute && _mode == RVPrivilegeMode.Machine && (_mstatus & MstatusMprv) != 0)
                effectiveMode = (RVPrivilegeMode)((_mstatus >> 11) & 3);

            if (effectiveMode == RVPrivilegeMode.Machine)
                return true;

            ulong mode = _satp >> 60;
            if (mode == 0)
                return true;

            int levels;
            int vaBits;
            if (mode == 8) { levels = 3; vaBits = 39; }
            else if (mode == 9) { levels = 4; vaBits = 48; }
            else
            {
                trapCause = PageFaultCause(access);
                return false;
            }

            if ((ulong)(((long)virtualAddress << (64 - vaBits)) >> (64 - vaBits)) != virtualAddress)
            {
                trapCause = PageFaultCause(access);
                return false;
            }

            ulong root = (_satp & ((1UL << 44) - 1)) << 12;
            ulong pageTable = root;
            int level = levels - 1;

            for (; ; )
            {
                ulong vpn = (virtualAddress >> (12 + level * 9)) & 0x1FF;
                ulong pteAddress = pageTable + vpn * 8;
                if (!TryReadPhysical(pteAddress, 8, RVMemoryAccess.Load, out ulong pte, out _, out _))
                {
                    trapCause = PageFaultCause(access);
                    return false;
                }

                bool valid = (pte & PteV) != 0;
                bool readable = (pte & PteR) != 0;
                bool writable = (pte & PteW) != 0;
                bool executable = (pte & PteX) != 0;
                if (!valid || (!readable && writable))
                {
                    trapCause = PageFaultCause(access);
                    return false;
                }

                if (readable || executable)
                {
                    if (level > 0)
                    {
                        ulong lowPpnMask = (1UL << (level * 9)) - 1;
                        if (((pte >> 10) & lowPpnMask) != 0)
                        {
                            trapCause = PageFaultCause(access);
                            return false;
                        }
                    }

                    bool userPage = (pte & PteU) != 0;
                    if (effectiveMode == RVPrivilegeMode.User && !userPage)
                    {
                        trapCause = PageFaultCause(access);
                        return false;
                    }
                    if (effectiveMode == RVPrivilegeMode.Supervisor && userPage && (access == RVMemoryAccess.Execute || (_mstatus & MstatusSum) == 0))
                    {
                        trapCause = PageFaultCause(access);
                        return false;
                    }

                    bool allowed = access switch
                    {
                        RVMemoryAccess.Execute => executable,
                        RVMemoryAccess.Load => readable || (((_mstatus & MstatusMxr) != 0) && executable),
                        _ => writable,
                    };
                    if (!allowed)
                    {
                        trapCause = PageFaultCause(access);
                        return false;
                    }

                    ulong required = PteA | (access == RVMemoryAccess.Store ? PteD : 0);
                    if ((pte & required) != required)
                    {
                        ulong updated = pte | required;
                        if (!TryWritePhysical(pteAddress, 8, updated, out _, out _))
                        {
                            trapCause = PageFaultCause(access);
                            return false;
                        }
                    }

                    ulong ppn = pte >> 10;
                    ulong offsetMask = (1UL << (12 + level * 9)) - 1;
                    physicalAddress = ((ppn << 12) & ~offsetMask) | (virtualAddress & offsetMask);
                    return true;
                }

                if (level == 0)
                {
                    trapCause = PageFaultCause(access);
                    return false;
                }

                pageTable = (pte >> 10) << 12;
                level--;
            }
        }

        private static ulong PageFaultCause(RVMemoryAccess access)
        {
            return access == RVMemoryAccess.Execute
                ? (ulong)RVTrapCause.InstructionPageFault
                : access == RVMemoryAccess.Load
                    ? (ulong)RVTrapCause.LoadPageFault
                    : (ulong)RVTrapCause.StorePageFault;
        }

        private static ulong AccessFaultCause(RVMemoryAccess access)
        {
            return access == RVMemoryAccess.Execute
                ? (ulong)RVTrapCause.InstructionAccessFault
                : access == RVMemoryAccess.Load
                    ? (ulong)RVTrapCause.LoadAccessFault
                    : (ulong)RVTrapCause.StoreAccessFault;
        }

        private bool TryReadPhysical(ulong address, int size, RVMemoryAccess access, out ulong value, out ulong trapCause, out ulong trapValue)
        {
            trapCause = 0;
            trapValue = address;
            ulong offset = address - _ramBase;
            if (address >= _ramBase && offset <= (ulong)(_ram.Length - size))
            {
                value = size switch
                {
                    1 => _ram[(int)offset],
                    2 => (ulong)(_ram[(int)offset] | (_ram[(int)offset + 1] << 8)),
                    4 => (ulong)(_ram[(int)offset] | (_ram[(int)offset + 1] << 8) | (_ram[(int)offset + 2] << 16) | (_ram[(int)offset + 3] << 24)),
                    _ => (ulong)_ram[(int)offset] | ((ulong)_ram[(int)offset + 1] << 8) | ((ulong)_ram[(int)offset + 2] << 16) | ((ulong)_ram[(int)offset + 3] << 24) | ((ulong)_ram[(int)offset + 4] << 32) | ((ulong)_ram[(int)offset + 5] << 40) | ((ulong)_ram[(int)offset + 6] << 48) | ((ulong)_ram[(int)offset + 7] << 56),
                };
                return true;
            }

            if (_uart.Contains(address))
            {
                value = _uart.Read(address);
                return true;
            }
            if (_clint.Contains(address))
            {
                value = _clint.Read(address, size);
                return true;
            }
            if (_plic.Contains(address))
            {
                value = _plic.Read(address, size);
                return true;
            }
            if (_keyboard != null && _keyboard.Contains(address))
            {
                value = _keyboard.Read(address, size);
                return true;
            }
            foreach (var block in _blocks)
            {
                if (block.Contains(address))
                {
                    value = block.Read(address, size);
                    return true;
                }
            }

            value = 0;
            trapCause = AccessFaultCause(access);
            return false;
        }

        private bool TryWritePhysical(ulong address, int size, ulong value, out ulong trapCause, out ulong trapValue)
        {
            trapCause = 0;
            trapValue = address;
            ulong offset = address - _ramBase;
            if (address >= _ramBase && offset <= (ulong)(_ram.Length - size))
            {
                _ram[(int)offset] = (byte)value;
                if (size >= 2)
                    _ram[(int)offset + 1] = (byte)(value >> 8);
                if (size >= 4)
                {
                    _ram[(int)offset + 2] = (byte)(value >> 16);
                    _ram[(int)offset + 3] = (byte)(value >> 24);
                }
                if (size == 8)
                {
                    _ram[(int)offset + 4] = (byte)(value >> 32);
                    _ram[(int)offset + 5] = (byte)(value >> 40);
                    _ram[(int)offset + 6] = (byte)(value >> 48);
                    _ram[(int)offset + 7] = (byte)(value >> 56);
                }
                return true;
            }

            if (_uart.Contains(address))
            {
                _uart.Write(address, value);
                return true;
            }
            if (_clint.Contains(address))
            {
                _clint.Write(address, size, value);
                return true;
            }
            if (_plic.Contains(address))
            {
                _plic.Write(address, size, value);
                return true;
            }
            if (_keyboard != null && _keyboard.Contains(address))
            {
                _keyboard.Write(address, size, value);
                return true;
            }
            foreach (var block in _blocks)
            {
                if (block.Contains(address))
                {
                    block.Write(address, size, value, _ram, _ramBase);
                    return true;
                }
            }

            trapCause = (ulong)RVTrapCause.StoreAccessFault;
            return false;
        }

        private bool ExecuteVectorLoad(uint instruction, out ulong trapCause, out ulong trapValue)
        {
            trapCause = (ulong)RVTrapCause.IllegalInstruction;
            trapValue = instruction;

            if ((_vtype & VectorTypeIllegal) != 0 || _vstart > _vl)
                return false;
            if (((instruction >> 26) & 0x3F) != 0 || ((instruction >> 20) & 0x1F) != 0)
                return false;

            int elementBytes = VectorMemoryElementBytes((int)((instruction >> 12) & 7));
            if (elementBytes == 0 || !TryGetVectorMemoryShape(elementBytes, out _, out int groupRegisters))
                return false;

            int vd = (int)((instruction >> 7) & 31);
            if (!CheckVectorGroup(vd, groupRegisters))
                return false;

            ulong baseAddress = _x[(int)((instruction >> 15) & 31)];
            bool unmasked = ((instruction >> 25) & 1) != 0;
            if (unmasked && _vstart == 0)
            {
                if (TryVectorLoadStore(baseAddress, vd, elementBytes, true, out bool trapped, out trapCause, out trapValue))
                    return true;
                if (trapped)
                    return false;
            }

            int vl = (int)_vl;
            int start = (int)_vstart;
            ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);
            int destinationOffset = vd * VectorRegisterBytes + start * elementBytes;
            ulong address = baseAddress + (ulong)start * (ulong)elementBytes;
            for (int i = start; i < vl; i++)
            {
                int currentDestinationOffset = destinationOffset;
                ulong currentAddress = address;
                destinationOffset += elementBytes;
                address += (ulong)elementBytes;
                if (!unmasked && !ReadVectorMaskBit(ref vectorBytes, i))
                    continue;
                if ((currentAddress & (ulong)(elementBytes - 1)) != 0)
                {
                    _vstart = (ulong)i;
                    trapCause = (ulong)RVTrapCause.LoadAddressMisaligned;
                    trapValue = currentAddress;
                    return false;
                }
                if (!TryReadMemory(currentAddress, elementBytes, RVMemoryAccess.Load, out ulong value, out trapCause, out trapValue))
                {
                    _vstart = (ulong)i;
                    return false;
                }
                WriteVectorElement(ref vectorBytes, currentDestinationOffset, elementBytes, value);
            }

            _vstart = 0;
            _mstatus |= MstatusVsMask;
            return true;
        }

        private bool ExecuteVectorStore(uint instruction, out ulong trapCause, out ulong trapValue)
        {
            trapCause = (ulong)RVTrapCause.IllegalInstruction;
            trapValue = instruction;

            if ((_vtype & VectorTypeIllegal) != 0 || _vstart > _vl)
                return false;
            if (((instruction >> 26) & 0x3F) != 0 || ((instruction >> 20) & 0x1F) != 0)
                return false;

            int elementBytes = VectorMemoryElementBytes((int)((instruction >> 12) & 7));
            if (elementBytes == 0 || !TryGetVectorMemoryShape(elementBytes, out _, out int groupRegisters))
                return false;

            int vs3 = (int)((instruction >> 7) & 31);
            if (!CheckVectorGroup(vs3, groupRegisters))
                return false;

            ulong baseAddress = _x[(int)((instruction >> 15) & 31)];
            bool unmasked = ((instruction >> 25) & 1) != 0;
            if (unmasked && _vstart == 0)
            {
                if (TryVectorLoadStore(baseAddress, vs3, elementBytes, false, out bool trapped, out trapCause, out trapValue))
                    return true;
                if (trapped)
                    return false;
            }

            int vl = (int)_vl;
            int start = (int)_vstart;
            ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);
            int sourceOffset = vs3 * VectorRegisterBytes + start * elementBytes;
            ulong address = baseAddress + (ulong)start * (ulong)elementBytes;
            for (int i = start; i < vl; i++)
            {
                int currentSourceOffset = sourceOffset;
                ulong currentAddress = address;
                sourceOffset += elementBytes;
                address += (ulong)elementBytes;
                if (!unmasked && !ReadVectorMaskBit(ref vectorBytes, i))
                    continue;
                if ((currentAddress & (ulong)(elementBytes - 1)) != 0)
                {
                    _vstart = (ulong)i;
                    trapCause = (ulong)RVTrapCause.StoreAddressMisaligned;
                    trapValue = currentAddress;
                    return false;
                }
                ulong value = ReadVectorElement(ref vectorBytes, currentSourceOffset, elementBytes);
                if (!TryWriteMemory(currentAddress, elementBytes, value, out trapCause, out trapValue))
                {
                    _vstart = (ulong)i;
                    return false;
                }
            }

            _vstart = 0;
            _mstatus |= MstatusVsMask;
            return true;
        }

        private bool TryVectorLoadStore(ulong baseAddress, int register, int elementBytes, bool load, out bool trapped, out ulong trapCause, out ulong trapValue)
        {
            trapped = false;
            trapCause = 0;
            trapValue = 0;
            int elementCount = (int)_vl;
            if (elementCount == 0)
            {
                _vstart = 0;
                _mstatus |= MstatusVsMask;
                return true;
            }

            if ((baseAddress & (ulong)(elementBytes - 1)) != 0)
            {
                trapped = true;
                trapCause = load ? (ulong)RVTrapCause.LoadAddressMisaligned : (ulong)RVTrapCause.StoreAddressMisaligned;
                trapValue = baseAddress;
                return false;
            }

            int byteCount = elementCount * elementBytes;
            RVMemoryAccess access = load ? RVMemoryAccess.Load : RVMemoryAccess.Store;
            int firstLength = (int)Math.Min((ulong)byteCount, PageSize - (baseAddress & PageMask));
            if (!TryGetTranslatedRamOffset(baseAddress, firstLength, access, out int firstOffset, out bool firstTrap, out trapCause, out trapValue))
            {
                trapped = firstTrap;
                return false;
            }

            int secondLength = byteCount - firstLength;
            Span<byte> vector = GetVectorBytes().Slice(register * VectorRegisterBytes, byteCount);
            if (load)
                _ram.AsSpan(firstOffset, firstLength).CopyTo(vector);
            else
            {
                vector.Slice(0, firstLength).CopyTo(_ram.AsSpan(firstOffset, firstLength));
                _hasReservation = false;
            }

            if (secondLength != 0)
            {
                if (!TryGetTranslatedRamOffset(baseAddress + (ulong)firstLength, secondLength, access, out int secondOffset, out bool secondTrap, out trapCause, out trapValue))
                {
                    _vstart = (ulong)(firstLength / elementBytes);
                    trapped = secondTrap;
                    return false;
                }

                if (load)
                    _ram.AsSpan(secondOffset, secondLength).CopyTo(vector.Slice(firstLength));
                else
                    vector.Slice(firstLength, secondLength).CopyTo(_ram.AsSpan(secondOffset, secondLength));
            }

            _vstart = 0;
            _mstatus |= MstatusVsMask;
            return true;
        }

        private bool TryGetTranslatedRamOffset(ulong address, int length, RVMemoryAccess access, out int offset, out bool trapped, out ulong trapCause, out ulong trapValue)
        {
            offset = 0;
            trapped = false;
            if (!TryTranslate(address, access, out ulong physicalAddress, out trapCause, out trapValue))
            {
                trapped = true;
                return false;
            }

            ulong ramOffset = physicalAddress - _ramBase;
            if (physicalAddress < _ramBase || ramOffset > (ulong)(_ram.Length - length))
                return false;
            offset = (int)ramOffset;
            return true;
        }

        private static bool TryDecodeVectorType(ulong type, out int sewBytes, out int groupBytes, out int groupRegisters, out int vlmax)
        {
            sewBytes = 0;
            groupBytes = 0;
            groupRegisters = 0;
            vlmax = 0;

            if ((type & VectorTypeIllegal) != 0 || (type & ~(VectorTypeIllegal | 0xFFUL)) != 0)
                return false;

            int vlmul = (int)(type & 7);
            int vsew = (int)((type >> 3) & 7);
            if (vsew > 3 || vlmul == 4)
                return false;

            int numerator;
            int denominator;
            switch (vlmul)
            {
                case 0: numerator = 1; denominator = 1; break;
                case 1: numerator = 2; denominator = 1; break;
                case 2: numerator = 4; denominator = 1; break;
                case 3: numerator = 8; denominator = 1; break;
                case 5: numerator = 1; denominator = 8; break;
                case 6: numerator = 1; denominator = 4; break;
                case 7: numerator = 1; denominator = 2; break;
                default: return false;
            }

            sewBytes = 1 << vsew;
            groupBytes = VectorLengthBytes * numerator / denominator;
            if (groupBytes < sewBytes || groupBytes > VectorLengthBytes * 8 || groupBytes % sewBytes != 0)
                return false;
            if (!TryGetVectorGroupRegisters(groupBytes, out groupRegisters))
                return false;
            vlmax = groupBytes / sewBytes;
            return vlmax != 0;
        }

        private bool TryGetVectorMemoryShape(int elementBytes, out int groupBytes, out int groupRegisters)
        {
            groupBytes = 0;
            groupRegisters = 0;
            int sewBytes = _vsewBytes;
            int currentGroupBytes = _vgroupBytes;
            if (sewBytes == 0 || currentGroupBytes == 0)
                return false;
            long bytes = (long)currentGroupBytes * elementBytes;
            if (bytes % sewBytes != 0)
                return false;
            bytes /= sewBytes;
            if (bytes <= 0 || bytes > VectorLengthBytes * 8)
                return false;
            groupBytes = (int)bytes;
            return TryGetVectorGroupRegisters(groupBytes, out groupRegisters);
        }

        private static int VectorMemoryElementBytes(int width)
        {
            switch (width)
            {
                case 0: return 1;
                case 5: return 2;
                case 6: return 4;
                case 7: return 8;
                default: return 0;
            }
        }
        private static bool TryGetVectorGroupRegisters(int groupBytes, out int groupRegisters)
        {
            groupRegisters = 0;
            if (groupBytes <= 0 || groupBytes > VectorLengthBytes * 8)
                return false;
            groupRegisters = Math.Max(1, (groupBytes + VectorRegisterBytes - 1) / VectorRegisterBytes);
            return groupRegisters <= VectorRegisterCount;
        }


        private static bool CheckVectorGroup(int register, int groupRegisters)
        {
            if (register < 0 || register >= VectorRegisterCount || groupRegisters <= 0 || register + groupRegisters > VectorRegisterCount)
                return false;
            return groupRegisters == 1 || (register & (groupRegisters - 1)) == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Span<byte> GetVectorBytes() => MemoryMarshal.AsBytes((Span<Vector512<ulong>>)_v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ReadVectorElement(ref byte vectorBytes, int offset, int elementBytes)
        {
            ref byte address = ref Unsafe.Add(ref vectorBytes, offset);
            switch (elementBytes)
            {
                case 1:
                    return address;
                case 2:
                    {
                        ushort value = Unsafe.ReadUnaligned<ushort>(ref address);
                        return BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value);
                    }
                case 4:
                    {
                        uint value = Unsafe.ReadUnaligned<uint>(ref address);
                        return BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value);
                    }
                default:
                    {
                        ulong value = Unsafe.ReadUnaligned<ulong>(ref address);
                        return BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value);
                    }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteVectorElement(ref byte vectorBytes, int offset, int elementBytes, ulong value)
        {
            ref byte address = ref Unsafe.Add(ref vectorBytes, offset);
            switch (elementBytes)
            {
                case 1:
                    address = (byte)value;
                    break;
                case 2:
                    {
                        ushort element = (ushort)value;
                        if (!BitConverter.IsLittleEndian)
                            element = BinaryPrimitives.ReverseEndianness(element);
                        Unsafe.WriteUnaligned(ref address, element);
                        break;
                    }
                case 4:
                    {
                        uint element = (uint)value;
                        if (!BitConverter.IsLittleEndian)
                            element = BinaryPrimitives.ReverseEndianness(element);
                        Unsafe.WriteUnaligned(ref address, element);
                        break;
                    }
                default:
                    if (!BitConverter.IsLittleEndian)
                        value = BinaryPrimitives.ReverseEndianness(value);
                    Unsafe.WriteUnaligned(ref address, value);
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ReadVectorMaskBit(ref byte vectorBytes, int element)
            => (Unsafe.Add(ref vectorBytes, element >> 3) & (1 << (element & 7))) != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteVectorMaskBit(ref byte vectorBytes, int register, int element, bool value)
        {
            ref byte destination = ref Unsafe.Add(ref vectorBytes, register * VectorRegisterBytes + (element >> 3));
            byte mask = (byte)(1 << (element & 7));
            if (value)
                destination |= mask;
            else
                destination &= (byte)~mask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryEvaluateVectorMultiplyAddOperation(int funct6, int funct3, ulong a, ulong b, ulong vd, out ulong result)
        {
            result = 0;
            if (funct3 != 2 && funct3 != 6)
                return false;

            ulong product = a * b;
            switch (funct6)
            {
                case 41:
                    result = a * vd + b;
                    return true;
                case 43:
                    result = b - a * vd;
                    return true;
                case 45:
                    result = vd + product;
                    return true;
                case 47:
                    result = vd - product;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryEvaluateVectorFloatMaskOperation(int funct6, int funct3, ulong a, ulong b, int sewBytes, out bool result)
        {
            result = false;
            if (funct3 != 1 && funct3 != 5)
                return false;

            if (sewBytes == 4)
            {
                float left = BitConverter.UInt32BitsToSingle((uint)b);
                float right = BitConverter.UInt32BitsToSingle((uint)a);
                switch (funct6)
                {
                    case 24: result = left == right; return true;
                    case 25: result = left <= right; return true;
                    case 27: result = left < right; return true;
                    case 28: result = left != right; return true;
                    case 29: result = left > right; return funct3 == 5;
                    case 31: result = left >= right; return funct3 == 5;
                    default: return false;
                }
            }

            if (sewBytes == 8)
            {
                double left = BitConverter.Int64BitsToDouble((long)b);
                double right = BitConverter.Int64BitsToDouble((long)a);
                switch (funct6)
                {
                    case 24: result = left == right; return true;
                    case 25: result = left <= right; return true;
                    case 27: result = left < right; return true;
                    case 28: result = left != right; return true;
                    case 29: result = left > right; return funct3 == 5;
                    case 31: result = left >= right; return funct3 == 5;
                    default: return false;
                }
            }

            return false;
        }

        private static bool TryEvaluateVectorFloatOperation(int funct6, int funct3, ulong a, ulong b, int sewBytes, out ulong result)
        {
            result = 0;
            if (funct3 != 1 && funct3 != 5)
                return false;

            if (sewBytes == 4)
            {
                uint leftBits = (uint)b;
                uint rightBits = (uint)a;
                float left = BitConverter.UInt32BitsToSingle(leftBits);
                float right = BitConverter.UInt32BitsToSingle(rightBits);
                switch (funct6)
                {
                    case 0: result = BitConverter.SingleToUInt32Bits(left + right); return true;
                    case 2: result = BitConverter.SingleToUInt32Bits(left - right); return true;
                    case 4: result = BitConverter.SingleToUInt32Bits(MathF.Min(left, right)); return true;
                    case 6: result = BitConverter.SingleToUInt32Bits(MathF.Max(left, right)); return true;
                    case 8: result = VectorFloatSign32(leftBits, rightBits, 0); return true;
                    case 9: result = VectorFloatSign32(leftBits, rightBits, 1); return true;
                    case 10: result = VectorFloatSign32(leftBits, rightBits, 2); return true;
                    case 32: result = BitConverter.SingleToUInt32Bits(left / right); return true;
                    case 33:
                        if (funct3 != 5) return false;
                        result = BitConverter.SingleToUInt32Bits(right / left);
                        return true;
                    case 36: result = BitConverter.SingleToUInt32Bits(left * right); return true;
                    case 39:
                        if (funct3 != 5) return false;
                        result = BitConverter.SingleToUInt32Bits(right - left);
                        return true;
                    default: return false;
                }
            }

            if (sewBytes == 8)
            {
                double left = BitConverter.Int64BitsToDouble((long)b);
                double right = BitConverter.Int64BitsToDouble((long)a);
                switch (funct6)
                {
                    case 0: result = (ulong)BitConverter.DoubleToInt64Bits(left + right); return true;
                    case 2: result = (ulong)BitConverter.DoubleToInt64Bits(left - right); return true;
                    case 4: result = (ulong)BitConverter.DoubleToInt64Bits(Math.Min(left, right)); return true;
                    case 6: result = (ulong)BitConverter.DoubleToInt64Bits(Math.Max(left, right)); return true;
                    case 8: result = VectorFloatSign64(b, a, 0); return true;
                    case 9: result = VectorFloatSign64(b, a, 1); return true;
                    case 10: result = VectorFloatSign64(b, a, 2); return true;
                    case 32: result = (ulong)BitConverter.DoubleToInt64Bits(left / right); return true;
                    case 33:
                        if (funct3 != 5) return false;
                        result = (ulong)BitConverter.DoubleToInt64Bits(right / left);
                        return true;
                    case 36: result = (ulong)BitConverter.DoubleToInt64Bits(left * right); return true;
                    case 39:
                        if (funct3 != 5) return false;
                        result = (ulong)BitConverter.DoubleToInt64Bits(right - left);
                        return true;
                    default: return false;
                }
            }

            return false;
        }

        private static uint VectorFloatSign32(uint magnitudeSource, uint signSource, int mode)
        {
            uint sign = signSource & 0x80000000U;
            uint magnitude = magnitudeSource & 0x7FFFFFFFU;
            switch (mode)
            {
                case 0: return magnitude | sign;
                case 1: return magnitude | (~sign & 0x80000000U);
                default: return magnitude | ((magnitudeSource ^ signSource) & 0x80000000U);
            }
        }

        private static ulong VectorFloatSign64(ulong magnitudeSource, ulong signSource, int mode)
        {
            ulong sign = signSource & 0x8000000000000000UL;
            ulong magnitude = magnitudeSource & 0x7FFFFFFFFFFFFFFFUL;
            switch (mode)
            {
                case 0: return magnitude | sign;
                case 1: return magnitude | (~sign & 0x8000000000000000UL);
                default: return magnitude | ((magnitudeSource ^ signSource) & 0x8000000000000000UL);
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryEvaluateVectorMaskOperation(int funct6, int funct3, ulong a, ulong b, int sewBits, out bool result)
        {
            result = false;
            switch (funct6)
            {
                case 24: result = b == a; return true;
                case 25: result = b != a; return true;
                case 26: result = b < a; return funct3 != 3;
                case 27: result = SignExtendElement(b, sewBits) < SignExtendElement(a, sewBits); return funct3 != 3;
                case 28: result = b <= a; return true;
                case 29: result = SignExtendElement(b, sewBits) <= SignExtendElement(a, sewBits); return true;
                case 30: result = b > a; return funct3 == 4 || funct3 == 3;
                case 31: result = SignExtendElement(b, sewBits) > SignExtendElement(a, sewBits); return funct3 == 4 || funct3 == 3;
                default: return false;
            }
        }

        private static bool TryEvaluateVectorIntegerOperation(int funct6, int funct3, ulong a, ulong b, int sewBits, ulong mask, out ulong result)
        {
            a &= mask;
            b &= mask;
            result = 0;
            switch (funct6)
            {
                case 0:
                    if (funct3 != 0 && funct3 != 4 && funct3 != 3) return false;
                    result = b + a;
                    return true;
                case 2:
                    if (funct3 != 0 && funct3 != 4) return false;
                    result = b - a;
                    return true;
                case 3:
                    if (funct3 != 4 && funct3 != 3) return false;
                    result = a - b;
                    return true;
                case 4:
                    if (funct3 != 0 && funct3 != 4) return false;
                    result = Math.Min(b, a);
                    return true;
                case 5:
                    if (funct3 != 0 && funct3 != 4) return false;
                    result = (ulong)Math.Min(SignExtendElement(b, sewBits), SignExtendElement(a, sewBits));
                    return true;
                case 6:
                    if (funct3 != 0 && funct3 != 4) return false;
                    result = Math.Max(b, a);
                    return true;
                case 7:
                    if (funct3 != 0 && funct3 != 4) return false;
                    result = (ulong)Math.Max(SignExtendElement(b, sewBits), SignExtendElement(a, sewBits));
                    return true;
                case 9:
                    if (funct3 != 0 && funct3 != 4 && funct3 != 3) return false;
                    result = b & a;
                    return true;
                case 10:
                    if (funct3 != 0 && funct3 != 4 && funct3 != 3) return false;
                    result = b | a;
                    return true;
                case 11:
                    if (funct3 != 0 && funct3 != 4 && funct3 != 3) return false;
                    result = b ^ a;
                    return true;
                case 32:
                    if (funct3 != 2 && funct3 != 6) return false;
                    result = a == 0 ? mask : b / a;
                    return true;
                case 33:
                    if (funct3 != 2 && funct3 != 6) return false;
                    result = SignedElementDiv(b, a, sewBits);
                    return true;
                case 34:
                    if (funct3 != 2 && funct3 != 6) return false;
                    result = a == 0 ? b : b % a;
                    return true;
                case 35:
                    if (funct3 != 2 && funct3 != 6) return false;
                    result = SignedElementRem(b, a, sewBits);
                    return true;
                case 36:
                    if (funct3 != 2 && funct3 != 6) return false;
                    result = UnsignedElementMulHigh(b, a, sewBits);
                    return true;
                case 37:
                    if (funct3 == 0 || funct3 == 4 || funct3 == 3)
                    {
                        result = b << (int)(a & (ulong)(sewBits - 1));
                        return true;
                    }
                    if (funct3 == 2 || funct3 == 6)
                    {
                        result = b * a;
                        return true;
                    }
                    return false;
                case 38:
                    if (funct3 != 2 && funct3 != 6) return false;
                    result = sewBits == 64
                        ? Mulhsu(SignExtendElement(b, sewBits), a)
                        : (ulong)((SignExtendElement(b, sewBits) * (long)(a & ElementMask(sewBits))) >> sewBits) & ElementMask(sewBits);
                    return true;
                case 39:
                    if (funct3 != 2 && funct3 != 6) return false;
                    result = sewBits == 64
                        ? Mulh(SignExtendElement(b, sewBits), SignExtendElement(a, sewBits))
                        : (ulong)((SignExtendElement(b, sewBits) * SignExtendElement(a, sewBits)) >> sewBits) & ElementMask(sewBits);
                    return true;
                case 40:
                    if (funct3 != 0 && funct3 != 4 && funct3 != 3) return false;
                    result = b >> (int)(a & (ulong)(sewBits - 1));
                    return true;
                case 41:
                    if (funct3 != 0 && funct3 != 4 && funct3 != 3) return false;
                    result = (ulong)(SignExtendElement(b, sewBits) >> (int)(a & (ulong)(sewBits - 1)));
                    return true;
                default:
                    return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ElementMask(int bits)
            => bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long SignExtendElement(ulong value, int bits)
        {
            int shift = 64 - bits;
            return ((long)(value << shift)) >> shift;
        }

        private static ulong SignedElementDiv(ulong dividend, ulong divisor, int bits)
        {
            ulong mask = ElementMask(bits);
            if ((divisor & mask) == 0)
                return mask;
            long a = SignExtendElement(dividend, bits);
            long b = SignExtendElement(divisor, bits);
            long min = bits == 64 ? long.MinValue : -(1L << (bits - 1));
            if (a == min && b == -1)
                return (ulong)a & mask;
            return (ulong)(a / b) & mask;
        }

        private static ulong SignedElementRem(ulong dividend, ulong divisor, int bits)
        {
            ulong mask = ElementMask(bits);
            if ((divisor & mask) == 0)
                return dividend & mask;
            long a = SignExtendElement(dividend, bits);
            long b = SignExtendElement(divisor, bits);
            long min = bits == 64 ? long.MinValue : -(1L << (bits - 1));
            if (a == min && b == -1)
                return 0;
            return (ulong)(a % b) & mask;
        }

        private static ulong UnsignedElementMulHigh(ulong left, ulong right, int bits)
        {
            if (bits == 64)
                return Mulhu(left, right);
            ulong mask = ElementMask(bits);
            return ((left & mask) * (right & mask)) >> bits;
        }


        private bool ExecuteFloatSign(bool doublePrecision, int funct3, int rd, int rs1, int rs2)
        {
            if (doublePrecision)
            {
                ulong a = _f[rs1];
                ulong b = _f[rs2];
                ulong sign = b & 0x8000000000000000UL;
                ulong magnitude = a & 0x7FFFFFFFFFFFFFFFUL;
                switch (funct3)
                {
                    case 0: _f[rd] = magnitude | sign; return true;
                    case 1: _f[rd] = magnitude | (~sign & 0x8000000000000000UL); return true;
                    case 2: _f[rd] = magnitude | ((a ^ b) & 0x8000000000000000UL); return true;
                    default: return false;
                }
            }
            else
            {
                uint a = (uint)_f[rs1];
                uint b = (uint)_f[rs2];
                uint sign = b & 0x80000000U;
                uint magnitude = a & 0x7FFFFFFFU;
                switch (funct3)
                {
                    case 0: _f[rd] = 0xFFFFFFFF00000000UL | magnitude | sign; return true;
                    case 1: _f[rd] = 0xFFFFFFFF00000000UL | magnitude | (~sign & 0x80000000U); return true;
                    case 2: _f[rd] = 0xFFFFFFFF00000000UL | magnitude | ((a ^ b) & 0x80000000U); return true;
                    default: return false;
                }
            }
        }

        private bool ExecuteFloatMinMax(bool doublePrecision, int funct3, int rd, int rs1, int rs2)
        {
            if (funct3 != 0 && funct3 != 1)
                return false;
            if (doublePrecision)
            {
                double a = ReadFloat64(rs1);
                double b = ReadFloat64(rs2);
                WriteFloat64(rd, funct3 == 0 ? Math.Min(a, b) : Math.Max(a, b));
            }
            else
            {
                float a = ReadFloat32(rs1);
                float b = ReadFloat32(rs2);
                WriteFloat32(rd, funct3 == 0 ? MathF.Min(a, b) : MathF.Max(a, b));
            }
            return true;
        }

        private bool ExecuteFloatCompare(bool doublePrecision, int funct3, int rd, int rs1, int rs2)
        {
            bool result;
            if (doublePrecision)
            {
                double a = ReadFloat64(rs1);
                double b = ReadFloat64(rs2);
                switch (funct3)
                {
                    case 0: result = a <= b; break;
                    case 1: result = a < b; break;
                    case 2: result = a == b; break;
                    default: return false;
                }
            }
            else
            {
                float a = ReadFloat32(rs1);
                float b = ReadFloat32(rs2);
                switch (funct3)
                {
                    case 0: result = a <= b; break;
                    case 1: result = a < b; break;
                    case 2: result = a == b; break;
                    default: return false;
                }
            }
            if (rd != 0)
                _x[rd] = result ? 1UL : 0UL;
            return true;
        }

        private bool ExecuteFloatToInt(bool doublePrecision, int rs2, int rd, int rs1)
        {
            double value = doublePrecision ? ReadFloat64(rs1) : ReadFloat32(rs1);
            ulong result;
            switch (rs2)
            {
                case 0: result = SignExtend32((uint)(int)value); break;
                case 1: result = SignExtend32((uint)value); break;
                case 2: result = (ulong)(long)value; break;
                case 3: result = (ulong)value; break;
                default: return false;
            }
            if (rd != 0)
                _x[rd] = result;
            return true;
        }

        private bool ExecuteIntToFloat(bool doublePrecision, int rs2, int rd, int rs1)
        {
            ulong raw = _x[rs1];
            if (doublePrecision)
            {
                switch (rs2)
                {
                    case 0: WriteFloat64(rd, (int)raw); return true;
                    case 1: WriteFloat64(rd, (uint)raw); return true;
                    case 2: WriteFloat64(rd, (long)raw); return true;
                    case 3: WriteFloat64(rd, raw); return true;
                    default: return false;
                }
            }
            else
            {
                switch (rs2)
                {
                    case 0: WriteFloat32(rd, (int)raw); return true;
                    case 1: WriteFloat32(rd, (uint)raw); return true;
                    case 2: WriteFloat32(rd, (long)raw); return true;
                    case 3: WriteFloat32(rd, raw); return true;
                    default: return false;
                }
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float ReadFloat32(int register)
            => BitConverter.Int32BitsToSingle((int)_f[register]);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double ReadFloat64(int register)
            => BitConverter.Int64BitsToDouble((long)_f[register]);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteFloat32(int register, float value)
            => _f[register] = 0xFFFFFFFF00000000UL | BitConverter.SingleToUInt32Bits(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteFloat64(int register, double value)
            => _f[register] = (ulong)BitConverter.DoubleToInt64Bits(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ClassifyFloat32(uint bits)
        {
            bool sign = (bits & 0x80000000U) != 0;
            uint exponent = (bits >> 23) & 0xFF;
            uint fraction = bits & 0x7FFFFFU;
            if (exponent == 0xFF)
            {
                if (fraction == 0) return sign ? 1UL : 1UL << 7;
                return (fraction & 0x400000U) == 0 ? 1UL << 8 : 1UL << 9;
            }
            if (exponent == 0)
            {
                if (fraction == 0) return sign ? 1UL << 3 : 1UL << 4;
                return sign ? 1UL << 2 : 1UL << 5;
            }
            return sign ? 1UL << 1 : 1UL << 6;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ClassifyFloat64(ulong bits)
        {
            bool sign = (bits & 0x8000000000000000UL) != 0;
            ulong exponent = (bits >> 52) & 0x7FF;
            ulong fraction = bits & 0xFFFFFFFFFFFFFUL;
            if (exponent == 0x7FF)
            {
                if (fraction == 0) return sign ? 1UL : 1UL << 7;
                return (fraction & 0x8000000000000UL) == 0 ? 1UL << 8 : 1UL << 9;
            }
            if (exponent == 0)
            {
                if (fraction == 0) return sign ? 1UL << 3 : 1UL << 4;
                return sign ? 1UL << 2 : 1UL << 5;
            }
            return sign ? 1UL << 1 : 1UL << 6;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryExecuteBitmanipImmediate(uint instruction, ulong source, out ulong result)
        {
            uint funct3 = instruction & Funct3Mask;

            if (funct3 == 0x1000U)
            {
                int immediate = (int)((instruction >> 20) & 0xFFFU);
                uint funct6 = instruction & Funct6Mask;
                switch (immediate)
                {
                    case 0x600: result = (ulong)System.Numerics.BitOperations.LeadingZeroCount(source); return true;
                    case 0x601: result = (ulong)System.Numerics.BitOperations.TrailingZeroCount(source); return true;
                    case 0x602: result = (ulong)System.Numerics.BitOperations.PopCount(source); return true;
                    case 0x604: result = (ulong)(long)(sbyte)source; return true;
                    case 0x605: result = (ulong)(long)(short)source; return true;
                }

                switch (funct6)
                {
                    case 0x48000000U: result = source & ~(1UL << immediate & 63); return true;
                    case 0x68000000U: result = source ^ (1UL << immediate & 63); return true;
                    case 0x28000000U: result = source | (1UL << immediate & 63); return true;
                }
            }
            else if (funct3 == 0x5000U)
            {
                int immediate = (int)((instruction >> 20) & 0xFFFU);
                if (immediate == 0x287)
                {
                    result = OrCombineBytes(source); return true;
                }
                if (immediate == 0x6B8)
                {
                    result = BinaryPrimitives.ReverseEndianness(source); return true;
                }
                uint funct6 = instruction & Funct6Mask;
                if (funct6 == 0x60000000U)
                {
                    result = System.Numerics.BitOperations.RotateRight(source, immediate & 63); return true;
                }
                if(funct6 == 0x48000000U)
                {
                    result = (source >> immediate & 63) & 1UL; return true;
                }
            }

            result = 0;
            return false;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryExecuteBitmanipRegister(uint instruction, ulong left, ulong right, out ulong result)
        {
            uint funct3 = instruction & Funct3Mask;
            uint funct7 = instruction & Funct7Mask;

            switch (funct7)
            {
                case 0x40000000U:
                    switch (funct3)
                    {
                        case 0x4000U: result = ~(left ^ right); return true;
                        case 0x6000U: result = left | ~right; return true;
                        case 0x7000U: result = left & ~right; return true;
                    }
                    break;
                case 0x0A000000U:
                    switch (funct3)
                    {
                        case 0x1000U:
                            CarrylessMultiply(left, right, out result, out _);
                            return true;
                        case 0x2000U:
                            CarrylessMultiply(left, right, out ulong low, out ulong high);
                            result = (high << 1) | (low >> 63);
                            return true;
                        case 0x3000U:
                            CarrylessMultiply(left, right, out _, out result);
                            return true;
                        case 0x4000U: result = (long)left < (long)right ? left : right; return true;
                        case 0x5000U: result = left < right ? left : right; return true;
                        case 0x6000U: result = (long)left > (long)right ? left : right; return true;
                        case 0x7000U: result = left > right ? left : right; return true;
                    }
                    break;
                case 0x60000000U:
                    switch (funct3)
                    {
                        case 0x1000U: result = System.Numerics.BitOperations.RotateLeft(left, (int)(right & 63)); return true;
                        case 0x5000U: result = System.Numerics.BitOperations.RotateRight(left, (int)(right & 63)); return true;
                    }
                    break;
                case 0x20000000U:
                    switch (funct3)
                    {
                        case 0x2000U: result = (left << 1) + right; return true;
                        case 0x4000U: result = (left << 2) + right; return true;
                        case 0x6000U: result = (left << 3) + right; return true;
                    }
                    break;
                case 0x48000000U:
                    switch (funct3)
                    {
                        case 0x1000U: result = left & ~(1UL << (int)(right & 63)); return true;
                        case 0x5000U: result = (left >> (int)(right & 63)) & 1UL; return true;
                    }
                    break;
                case 0x68000000U:
                    if (funct3 == 0x1000U)
                    {
                        result = left ^ (1UL << (int)(right & 63));
                        return true;
                    }
                    break;
                case 0x28000000U:
                    if (funct3 == 0x1000U)
                    {
                        result = left | (1UL << (int)(right & 63));
                        return true;
                    }
                    break;
            }

            result = 0;
            return false;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong OrCombineBytes(ulong value)
        {
            ulong result = 0;
            for (int shift = 0; shift < 64; shift += 8)
            {
                if (((value >> shift) & 0xFFUL) != 0)
                    result |= 0xFFUL << shift;
            }
            return result;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CarrylessMultiply(ulong left, ulong right, out ulong low, out ulong high)
        {
            low = 0;
            high = 0;
            while (right != 0)
            {
                int shift = System.Numerics.BitOperations.TrailingZeroCount(right);
                low ^= left << shift;
                if (shift != 0)
                    high ^= left >> (64 - shift);
                right &= right - 1;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long ImmI(uint instruction)
            => ((long)(int)instruction) >> 20;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long ImmS(uint instruction)
        {
            uint value = ((instruction >> 7) & 0x1FU) | (((instruction >> 25) & 0x7FU) << 5);
            return ((long)(int)(value << 20)) >> 20;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long ImmB(uint instruction)
        {
            uint value = (((instruction >> 8) & 0x0FU) << 1) | (((instruction >> 25) & 0x3FU) << 5) | (((instruction >> 7) & 1U) << 11) | (((instruction >> 31) & 1U) << 12);
            return ((long)(int)(value << 19)) >> 19;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long ImmU(uint instruction)
            => (long)(int)(instruction & 0xFFFFF000U);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long ImmJ(uint instruction)
        {
            uint value = (((instruction >> 21) & 0x3FFU) << 1) | (((instruction >> 20) & 1U) << 11) | (((instruction >> 12) & 0xFFU) << 12) | (((instruction >> 31) & 1U) << 20);
            return ((long)(int)(value << 11)) >> 11;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong SignExtend32(uint value) => (ulong)(long)(int)value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Div(ulong a, ulong b)
        {
            if (b == 0) return ulong.MaxValue;
            if (a == 0x8000000000000000UL && b == ulong.MaxValue) return a;
            return (ulong)((long)a / (long)b);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Rem(ulong a, ulong b)
        {
            if (b == 0) return a;
            if (a == 0x8000000000000000UL && b == ulong.MaxValue) return 0;
            return (ulong)((long)a % (long)b);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int DivW(int a, int b)
        {
            if (b == 0) return -1;
            if (a == int.MinValue && b == -1) return a;
            return a / b;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int RemW(int a, int b)
        {
            if (b == 0) return a;
            if (a == int.MinValue && b == -1) return 0;
            return a % b;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Mulhu(ulong x, ulong y)
        {
            ulong x0 = (uint)x;
            ulong x1 = x >> 32;
            ulong y0 = (uint)y;
            ulong y1 = y >> 32;
            ulong p11 = x1 * y1;
            ulong p01 = x0 * y1;
            ulong p10 = x1 * y0;
            ulong p00 = x0 * y0;
            ulong middle = (p00 >> 32) + (uint)p10 + (uint)p01;
            return p11 + (p10 >> 32) + (p01 >> 32) + (middle >> 32);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Mulh(long x, long y)
        {
            ulong ux = (ulong)x;
            ulong uy = (ulong)y;
            ulong high = Mulhu(ux, uy);
            if (x < 0) high -= uy;
            if (y < 0) high -= ux;
            return high;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Mulhsu(long x, ulong y)
        {
            ulong ux = (ulong)x;
            ulong high = Mulhu(ux, y);
            if (x < 0) high -= y;
            return high;
        }
    }

    internal sealed class RVByteQueue
    {
        private readonly byte[] _buffer;
        private int _head;
        private int _count;

        public int Count => _count;

        public RVByteQueue(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new byte[capacity];
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }

        public bool Enqueue(byte value)
        {
            if (_count == _buffer.Length)
                return false;
            int index = _head + _count;
            if (index >= _buffer.Length)
                index -= _buffer.Length;
            _buffer[index] = value;
            _count++;
            return true;
        }

        public bool TryDequeue(out byte value)
        {
            if (_count == 0)
            {
                value = 0;
                return false;
            }
            value = _buffer[_head++];
            if (_head == _buffer.Length)
                _head = 0;
            _count--;
            return true;
        }
    }

    public sealed class RVUart16550
    {
        private const byte InterruptEnableReceivedDataAvailable = 1;
        private const byte InterruptEnableTransmitterEmpty = 2;
        private const byte InterruptIdNone = 1;
        private const byte InterruptIdTransmitterEmpty = 2;
        private const byte InterruptIdReceivedDataAvailable = 4;
        private const byte FifoStatusBits = 0xC0;

        private readonly RVByteQueue _rx = new RVByteQueue(4096);
        private readonly RVByteQueue _tx = new RVByteQueue(4096);
        private readonly ulong _base;
        private byte _ier;
        private byte _fcr;
        private byte _lcr;
        private byte _mcr;
        private byte _scr;
        private ushort _divisor;
        private bool _thrInterruptPending;

        public ulong BaseAddress => _base;

        public RVUart16550(ulong baseAddress)
        {
            _base = baseAddress;
        }

        public void Reset()
        {
            _rx.Clear();
            _tx.Clear();
            _ier = 0;
            _fcr = 0;
            _lcr = 0;
            _mcr = 0;
            _scr = 0;
            _divisor = 0;
            _thrInterruptPending = false;
        }

        public bool Contains(ulong address)
            => address - _base < 8;

        public bool EnqueueInput(byte value)
            => _rx.Enqueue(value);

        public bool TryReadOutput(out byte value)
            => _tx.TryDequeue(out value);

        public bool InterruptPending
            => GetInterruptId() != InterruptIdNone;

        public ulong Read(ulong address)
        {
            int offset = (int)(address - _base);
            bool dlab = (_lcr & 0x80) != 0;
            switch (offset)
            {
                case 0:
                    if (dlab) return (byte)_divisor;
                    return _rx.TryDequeue(out byte b) ? b : 0UL;
                case 1:
                    return dlab ? (byte)(_divisor >> 8) : _ier;
                case 2:
                    byte interruptId = GetInterruptId();
                    if (interruptId == InterruptIdTransmitterEmpty)
                        _thrInterruptPending = false;
                    return (ulong)(FifoStatusBits | interruptId);
                case 3:
                    return _lcr;
                case 4:
                    return _mcr;
                case 5:
                    return 0x60UL | (_rx.Count != 0 ? 1UL : 0UL);
                case 6:
                    return 0xB0;
                case 7:
                    return _scr;
                default:
                    return 0;
            }
        }

        public void Write(ulong address, ulong value)
        {
            int offset = (int)(address - _base);
            byte b = (byte)value;
            bool dlab = (_lcr & 0x80) != 0;
            switch (offset)
            {
                case 0:
                    if (dlab)
                        _divisor = (ushort)((_divisor & 0xFF00) | b);
                    else
                    {
                        _tx.Enqueue(b);
                        _thrInterruptPending = true;
                    }
                    break;
                case 1:
                    if (dlab)
                    {
                        _divisor = (ushort)((_divisor & 0x00FF) | (b << 8));
                    }
                    else
                    {
                        bool enableThr = (_ier & InterruptEnableTransmitterEmpty) == 0 && (b & InterruptEnableTransmitterEmpty) != 0;
                        _ier = b;
                        if (enableThr)
                            _thrInterruptPending = true;
                        if ((_ier & InterruptEnableTransmitterEmpty) == 0)
                            _thrInterruptPending = false;
                    }
                    break;
                case 2:
                    _fcr = b;
                    if ((b & 2) != 0)
                        _rx.Clear();
                    if ((b & 4) != 0)
                        _thrInterruptPending = false;
                    break;
                case 3:
                    _lcr = b;
                    break;
                case 4:
                    _mcr = b;
                    break;
                case 7:
                    _scr = b;
                    break;
            }
        }

        private byte GetInterruptId()
        {
            if ((_ier & InterruptEnableReceivedDataAvailable) != 0 && _rx.Count != 0)
                return InterruptIdReceivedDataAvailable;
            if ((_ier & InterruptEnableTransmitterEmpty) != 0 && _thrInterruptPending)
                return InterruptIdTransmitterEmpty;
            return InterruptIdNone;
        }
    }

    public sealed class RVClint
    {
        private readonly ulong _base;
        private ulong _mtime;
        private ulong _mtimecmp = ulong.MaxValue;
        private uint _msip;

        public ulong BaseAddress => _base;
        public ulong MTime => _mtime;
        public ulong MTimeCmp => _mtimecmp;
        public uint MsIp => _msip;

        public RVClint(ulong baseAddress)
        {
            _base = baseAddress;
        }

        public void Reset()
        {
            _mtime = 0;
            _mtimecmp = ulong.MaxValue;
            _msip = 0;
        }

        public void Tick()
            => _mtime++;

        public bool Contains(ulong address)
            => address - _base < 0x10000;

        public ulong Read(ulong address, int size)
        {
            ulong offset = address - _base;
            if (offset < 4)
                return _msip;
            if (offset >= 0x4000 && offset < 0x4008)
                return ReadWindow(_mtimecmp, (int)(offset - 0x4000), size);
            if (offset >= 0xBFF8 && offset < 0xC000)
                return ReadWindow(_mtime, (int)(offset - 0xBFF8), size);
            return 0;
        }

        public void Write(ulong address, int size, ulong value)
        {
            ulong offset = address - _base;
            if (offset < 4)
            {
                _msip = (uint)(value & 1);
                return;
            }
            if (offset >= 0x4000 && offset < 0x4008)
                _mtimecmp = WriteWindow(_mtimecmp, (int)(offset - 0x4000), size, value);
            else if (offset >= 0xBFF8 && offset < 0xC000)
                _mtime = WriteWindow(_mtime, (int)(offset - 0xBFF8), size, value);
        }

        private static ulong ReadWindow(ulong value, int offset, int size)
            => size == 8 ? value : (value >> (offset * 8)) & ((1UL << (size * 8)) - 1);

        private static ulong WriteWindow(ulong old, int offset, int size, ulong value)
        {
            ulong mask = size == 8 ? ulong.MaxValue : ((1UL << (size * 8)) - 1) << (offset * 8);
            return (old & ~mask) | ((value << (offset * 8)) & mask);
        }
    }

    public sealed class RVPlic
    {
        public const int MachineContext = 0;
        public const int SupervisorContext = 1;
        public const int BlockDeviceFirstSource = 1;
        public const int BlockDeviceSourceCount = 8;
        public const int BlockDeviceSource = BlockDeviceFirstSource;
        public const int UartSource = 10;
        public const int KeyboardSource = 11;

        private const int SourceCount = 32;
        private const int ContextCount = 2;

        private readonly ulong _base;
        private readonly uint[] _priority = new uint[SourceCount];
        private readonly uint[] _enable = new uint[ContextCount];
        private readonly uint[] _threshold = new uint[ContextCount];
        private uint _pending;
        private uint _claimed;

        public RVPlic(ulong baseAddress)
        {
            _base = baseAddress;
        }

        public void Reset()
        {
            Array.Clear(_priority, 0, _priority.Length);
            Array.Clear(_enable, 0, _enable.Length);
            Array.Clear(_threshold, 0, _threshold.Length);
            _pending = 0;
            _claimed = 0;
            for (int source = BlockDeviceFirstSource; source < BlockDeviceFirstSource + BlockDeviceSourceCount; source++)
                _priority[source] = 1;
            _priority[UartSource] = 1;
            _priority[KeyboardSource] = 1;
        }

        public ulong BaseAddress => _base;

        public bool Contains(ulong address)
            => address - _base < 0x4000000UL;

        public void SetSourcePending(int source, bool pending)
        {
            if ((uint)source >= SourceCount || source == 0)
                return;
            uint bit = 1U << source;
            if (pending)
            {
                if ((_claimed & bit) == 0)
                    _pending |= bit;
            }
            else
            {
                _pending &= ~bit;
            }
        }

        public bool HasPendingInterrupt(int context)
        {
            if ((uint)context >= ContextCount)
                return false;

            uint active = _pending & _enable[context];
            if (active == 0)
                return false;

            uint threshold = _threshold[context];

            while (active != 0)
            {
                int source = System.Numerics.BitOperations.TrailingZeroCount(active);
                if (_priority[source] > threshold)
                    return true;

                active &= active - 1;
            }

            return false;
        }

        public ulong Read(ulong address, int size)
        {
            ulong offset = address - _base;
            uint value;
            if (offset < 0x1000)
            {
                int source = checked((int)(offset >> 2));
                value = source < SourceCount ? _priority[source] : 0;
            }
            else if (offset >= 0x1000 && offset < 0x1080)
            {
                value = offset == 0x1000 ? _pending : 0;
            }
            else if (offset >= 0x2000 && offset < 0x2080)
            {
                int context = checked((int)((offset - 0x2000) >> 7));
                value = context < ContextCount ? _enable[context] : 0;
            }
            else if (offset >= 0x200000 && offset < 0x202000)
            {
                int context = checked((int)((offset - 0x200000) >> 12));
                ulong contextOffset = (offset - 0x200000) & 0xFFF;
                if (context >= ContextCount)
                    value = 0;
                else if (contextOffset == 0)
                    value = _threshold[context];
                else if (contextOffset == 4)
                {
                    if ((uint)context >= ContextCount)
                        Slice(0, (int)(offset & 3), size);
                    uint active = _pending & _enable[context];
                    if (active == 0)
                        Slice(0, (int)(offset & 3), size);
                    uint threshold = _threshold[context];
                    uint best = 0;
                    uint bestPriority = 0;

                    while (active != 0)
                    {
                        int source = System.Numerics.BitOperations.TrailingZeroCount(active);
                        uint priority = _priority[source];

                        if (priority > threshold && priority > bestPriority)
                        {
                            best = (uint)source;
                            bestPriority = priority;
                        }

                        active &= active - 1;
                    }
                    value = best;
                    if (value != 0)
                    {
                        uint bit = 1U << (int)value;
                        _pending &= ~bit;
                        _claimed |= bit;
                    }
                }
                else
                    value = 0;
            }
            else
            {
                value = 0;
            }

            return Slice(value, (int)(offset & 3), size);
        }

        public void Write(ulong address, int size, ulong value)
        {
            ulong offset = address - _base;
            if ((offset & 3) != 0 || size != 4)
                return;

            if (offset < 0x1000)
            {
                int source = checked((int)(offset >> 2));
                if (source > 0 && source < SourceCount)
                    _priority[source] = (uint)value & 7;
                return;
            }

            if (offset >= 0x2000 && offset < 0x2080)
            {
                int context = checked((int)((offset - 0x2000) >> 7));
                if (context < ContextCount)
                    _enable[context] = (uint)value & ~1U;
                return;
            }

            if (offset >= 0x200000 && offset < 0x202000)
            {
                int context = checked((int)((offset - 0x200000) >> 12));
                ulong contextOffset = (offset - 0x200000) & 0xFFF;
                if (context >= ContextCount)
                    return;
                if (contextOffset == 0)
                {
                    _threshold[context] = (uint)value & 7;
                }
                else if (contextOffset == 4)
                {
                    int source = (int)((uint)value);
                    if (source > 0 && source < SourceCount)
                        _claimed &= ~(1U << source);
                }
            }
        }


        private static ulong Slice(uint value, int byteOffset, int size)
        {
            if (size == 4 && byteOffset == 0)
                return value;
            int bits = size * 8;
            uint mask = bits >= 32 ? uint.MaxValue : (1U << bits) - 1;
            return (value >> (byteOffset * 8)) & mask;
        }
    }


    public sealed class RVMmioKeyboard
    {
        public const ulong RegisterWindowSize = 0x100UL;
        private const uint MagicValue = 0x44424b43U;
        private const uint Version = 1;
        private const uint StatusReady = 1;
        private const uint StatusOverflow = 2;
        private const uint ControlInterruptEnable = 1;
        private const uint ControlClearQueue = 2;
        private const uint ControlClearOverflow = 4;

        private readonly ulong _base;
        private readonly int _interruptSource;
        private readonly uint[] _events;
        private int _head;
        private int _count;
        private bool _interruptEnabled;
        private bool _overflow;

        public ulong BaseAddress => _base;
        public int InterruptSource => _interruptSource;
        public int Capacity => _events.Length;
        public int Count => _count;
        public bool Overflow => _overflow;
        public bool InterruptPending => _interruptEnabled && _count != 0;

        public RVMmioKeyboard(ulong baseAddress, int capacity, int interruptSource = RVPlic.KeyboardSource)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _base = baseAddress;
            _interruptSource = interruptSource;
            _events = new uint[capacity];
        }

        public void Reset()
        {
            _head = 0;
            _count = 0;
            _interruptEnabled = false;
            _overflow = false;
        }

        public bool Contains(ulong address)
            => address - _base < RegisterWindowSize;

        public bool Enqueue(RVKeyboardEvent value)
            => EnqueueEncoded(value.Encoded);

        public bool EnqueueKeyDown(ushort usage, bool repeat = false)
            => Enqueue(RVKeyboardEvent.KeyDown(usage, repeat));

        public bool EnqueueKeyUp(ushort usage)
            => Enqueue(RVKeyboardEvent.KeyUp(usage));

        public bool EnqueueEncoded(uint value)
        {
            if (_count == _events.Length)
            {
                _overflow = true;
                return false;
            }
            int index = _head + _count;
            if (index >= _events.Length)
                index -= _events.Length;
            _events[index] = value;
            _count++;
            return true;
        }

        public ulong Read(ulong address, int size)
        {
            ulong offset = address - _base;
            uint value = offset switch
            {
                0x00 => MagicValue,
                0x04 => Version,
                0x08 => (uint)_events.Length,
                0x0c => BuildStatus(),
                0x10 => _interruptEnabled ? 1U : 0U,
                0x14 => Dequeue(),
                _ => 0,
            };
            return Slice(value, (int)(offset & 3), size);
        }

        public void Write(ulong address, int size, ulong value)
        {
            ulong offset = address - _base;
            if ((offset & 3) != 0 || size != 4)
                return;
            if (offset != 0x10)
                return;

            uint control = (uint)value;
            _interruptEnabled = (control & ControlInterruptEnable) != 0;
            if ((control & ControlClearQueue) != 0)
            {
                _head = 0;
                _count = 0;
            }
            if ((control & ControlClearOverflow) != 0)
                _overflow = false;
        }

        private uint BuildStatus()
        {
            uint status = _count != 0 ? StatusReady : 0;
            if (_overflow)
                status |= StatusOverflow;
            return status;
        }

        private uint Dequeue()
        {
            if (_count == 0)
                return 0;
            uint value = _events[_head++];
            if (_head == _events.Length)
                _head = 0;
            _count--;
            return value;
        }

        private static ulong Slice(uint value, int byteOffset, int size)
        {
            if (size == 4 && byteOffset == 0)
                return value;
            int bits = size * 8;
            uint mask = bits >= 32 ? uint.MaxValue : (1U << bits) - 1;
            return (value >> (byteOffset * 8)) & mask;
        }
    }

    public sealed class RVMmioBlockDevice
    {
        private const uint MagicValue = 0x74726976;
        private const uint Version = 2;
        private const uint DeviceIdBlock = 2;
        private const uint VendorId = 0x434e4944;
        private const uint QueueSize = 8;
        private const ulong SupportedFeatures = (1UL << 6) | (1UL << 32);
        private const uint DeviceFeaturesLow = 1u << 6;
        private const uint DeviceFeaturesHigh = 1u;
        private const uint InterruptUsedBuffer = 1;
        private const uint StatusFeaturesOk = 8;
        private const ushort VirtqAvailFNoInterrupt = 1;
        private const uint VirtqDescFNext = 1;
        private const uint VirtqDescFWrite = 2;
        private const uint BlkTIn = 0;
        private const uint BlkTOut = 1;
        private const uint BlkTFlush = 4;
        private const byte BlkSOk = 0;
        private const byte BlkSIoErr = 1;
        private const byte BlkSUnsupported = 2;

        public const ulong RegisterWindowSize = 0x1000;
        public const ulong SectorSize = 512;

        private readonly ulong _base;
        private readonly byte[] _storage;
        private uint _deviceFeaturesSel;
        private uint _driverFeaturesSel;
        private ulong _driverFeatures;
        private uint _queueSel;
        private uint _queueNum;
        private uint _queueReady;
        private ulong _queueDesc;
        private ulong _queueDriver;
        private ulong _queueDevice;
        private ushort _lastAvailableIndex;
        private uint _interruptStatus;
        private uint _status;
        private uint _configGeneration;

        public byte[] Storage => _storage;
        public ulong BaseAddress => _base;
        public int InterruptSource { get; }
        public bool InterruptPending => _interruptStatus != 0;
        public ulong CapacityBytes => (ulong)_storage.Length;
        public ulong CapacitySectors => (ulong)_storage.Length / SectorSize;

        public RVMmioBlockDevice(ulong baseAddress, int storageSize, int interruptSource = RVPlic.BlockDeviceSource)
        {
            if (storageSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(storageSize));
            _base = baseAddress;
            _storage = new byte[storageSize];
            InterruptSource = interruptSource;
        }

        public void Reset()
        {
            _deviceFeaturesSel = 0;
            _driverFeaturesSel = 0;
            _driverFeatures = 0;
            _queueSel = 0;
            _queueNum = 0;
            _queueReady = 0;
            _queueDesc = 0;
            _queueDriver = 0;
            _queueDevice = 0;
            _lastAvailableIndex = 0;
            _interruptStatus = 0;
            _status = 0;
            _configGeneration++;
        }

        public bool Contains(ulong address)
            => address - _base < RegisterWindowSize;

        public void LoadImage(byte[] image, int offset = 0)
        {
            if (image is null)
                throw new ArgumentNullException(nameof(image));
            if (offset < 0 || offset > _storage.Length || image.Length > _storage.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(offset));
            Buffer.BlockCopy(image, 0, _storage, offset, image.Length);
        }

        public void ClearStorage(int offset = 0, int length = -1)
        {
            if (length < 0)
                length = _storage.Length - offset;
            if (offset < 0 || length < 0 || offset > _storage.Length || length > _storage.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(offset));
            Array.Clear(_storage, offset, length);
        }

        public ulong Read(ulong address, int size)
        {
            ulong offset = address - _base;
            if (offset >= 0x100)
                return ReadConfig(offset - 0x100, size);

            ulong value = (offset & ~3UL) switch
            {
                0x000 => MagicValue,
                0x004 => Version,
                0x008 => DeviceIdBlock,
                0x00c => VendorId,
                0x010 => ReadDeviceFeatures(),
                0x014 => _deviceFeaturesSel,
                0x020 => ReadDriverFeatures(),
                0x024 => _driverFeaturesSel,
                0x030 => _queueSel,
                0x034 => _queueSel == 0 ? QueueSize : 0,
                0x038 => _queueSel == 0 ? _queueNum : 0,
                0x044 => _queueSel == 0 ? _queueReady : 0,
                0x050 => 0,
                0x060 => _interruptStatus,
                0x064 => 0,
                0x070 => _status,
                0x080 => (uint)_queueDesc,
                0x084 => (uint)(_queueDesc >> 32),
                0x090 => (uint)_queueDriver,
                0x094 => (uint)(_queueDriver >> 32),
                0x0a0 => (uint)_queueDevice,
                0x0a4 => (uint)(_queueDevice >> 32),
                0x0fc => _configGeneration,
                _ => 0,
            };
            return ReadWindow(value, (int)(offset & 3), size);
        }

        public void Write(ulong address, int size, ulong value, byte[] ram, ulong ramBase)
        {
            ulong offset = address - _base;
            if (offset >= 0x100)
                return;

            switch (offset & ~3UL)
            {
                case 0x014:
                    _deviceFeaturesSel = (uint)WriteWindow(_deviceFeaturesSel, (int)(offset & 3), size, value);
                    break;
                case 0x020:
                    WriteDriverFeatures((uint)WriteWindow(ReadDriverFeatures(), (int)(offset & 3), size, value));
                    break;
                case 0x024:
                    _driverFeaturesSel = (uint)WriteWindow(_driverFeaturesSel, (int)(offset & 3), size, value);
                    break;
                case 0x030:
                    _queueSel = (uint)WriteWindow(_queueSel, (int)(offset & 3), size, value);
                    break;
                case 0x038:
                    if (_queueSel == 0)
                        _queueNum = Math.Min((uint)WriteWindow(_queueNum, (int)(offset & 3), size, value), QueueSize);
                    break;
                case 0x044:
                    if (_queueSel == 0)
                    {
                        _queueReady = (uint)WriteWindow(_queueReady, (int)(offset & 3), size, value) & 1;
                        if (_queueReady == 0)
                            _lastAvailableIndex = 0;
                    }
                    break;
                case 0x050:
                    if ((uint)WriteWindow(0, (int)(offset & 3), size, value) == 0)
                        ProcessQueue(ram, ramBase);
                    break;
                case 0x064:
                    _interruptStatus &= ~(uint)WriteWindow(0, (int)(offset & 3), size, value);
                    break;
                case 0x070:
                    uint newStatus = (uint)WriteWindow(_status, (int)(offset & 3), size, value);
                    if (newStatus == 0)
                        Reset();
                    else
                    {
                        _status = newStatus;
                        if ((_status & StatusFeaturesOk) != 0 && (_driverFeatures & ~SupportedFeatures) != 0)
                            _status &= ~StatusFeaturesOk;
                    }
                    break;
                case 0x080:
                    if (_queueSel == 0)
                        _queueDesc = WriteLow32(_queueDesc, (uint)WriteWindow((uint)_queueDesc, (int)(offset & 3), size, value));
                    break;
                case 0x084:
                    if (_queueSel == 0)
                        _queueDesc = WriteHigh32(_queueDesc, (uint)WriteWindow((uint)(_queueDesc >> 32), (int)(offset & 3), size, value));
                    break;
                case 0x090:
                    if (_queueSel == 0)
                        _queueDriver = WriteLow32(_queueDriver, (uint)WriteWindow((uint)_queueDriver, (int)(offset & 3), size, value));
                    break;
                case 0x094:
                    if (_queueSel == 0)
                        _queueDriver = WriteHigh32(_queueDriver, (uint)WriteWindow((uint)(_queueDriver >> 32), (int)(offset & 3), size, value));
                    break;
                case 0x0a0:
                    if (_queueSel == 0)
                        _queueDevice = WriteLow32(_queueDevice, (uint)WriteWindow((uint)_queueDevice, (int)(offset & 3), size, value));
                    break;
                case 0x0a4:
                    if (_queueSel == 0)
                        _queueDevice = WriteHigh32(_queueDevice, (uint)WriteWindow((uint)(_queueDevice >> 32), (int)(offset & 3), size, value));
                    break;
            }
        }

        private uint ReadDeviceFeatures()
            => _deviceFeaturesSel == 0 ? DeviceFeaturesLow : _deviceFeaturesSel == 1 ? DeviceFeaturesHigh : 0;

        private uint ReadDriverFeatures()
            => _driverFeaturesSel == 0 ? (uint)_driverFeatures : _driverFeaturesSel == 1 ? (uint)(_driverFeatures >> 32) : 0;

        private void WriteDriverFeatures(uint value)
        {
            if (_driverFeaturesSel == 0)
                _driverFeatures = (_driverFeatures & 0xffffffff00000000UL) | value;
            else if (_driverFeaturesSel == 1)
                _driverFeatures = (_driverFeatures & 0xffffffffUL) | ((ulong)value << 32);
        }

        private ulong ReadConfig(ulong offset, int size)
        {
            ulong value = (offset & ~7UL) switch
            {
                0x00 => CapacitySectors,
                0x08 => 0,
                0x10 => SectorSize << 32,
                _ => 0,
            };
            return ReadWindow(value, (int)(offset & 7), size);
        }

        private void ProcessQueue(byte[] ram, ulong ramBase)
        {
            if (_queueSel != 0 || _queueReady == 0 || _queueNum == 0 || _queueNum > QueueSize)
                return;
            if (!TryReadU16(ram, ramBase, _queueDriver + 2, out ushort availableIndex))
                return;

            while (_lastAvailableIndex != availableIndex)
            {
                ulong ringEntryAddress = _queueDriver + 4 + 2UL * ((ulong)_lastAvailableIndex % _queueNum);
                if (!TryReadU16(ram, ramBase, ringEntryAddress, out ushort head))
                    return;
                byte status = ExecuteRequest(ram, ramBase, head, out uint bytesTransferred);
                PublishUsedBuffer(ram, ramBase, head, bytesTransferred, status != BlkSOk);
                _lastAvailableIndex++;
            }
        }

        private byte ExecuteRequest(byte[] ram, ulong ramBase, ushort head, out uint bytesTransferred)
        {
            bytesTransferred = 0;
            if (head >= _queueNum)
                return BlkSIoErr;
            if (!ReadDescriptor(ram, ramBase, head, out Descriptor header))
                return BlkSIoErr;
            if ((header.Flags & VirtqDescFNext) == 0 || header.Length < 16)
                return BlkSIoErr;
            if (!TryReadU32(ram, ramBase, header.Address, out uint type))
                return BlkSIoErr;
            if (!TryReadU64(ram, ramBase, header.Address + 8, out ulong sector))
                return BlkSIoErr;

            if (type == BlkTFlush)
                return CompleteFlushRequest(ram, ramBase, header.Next);
            if (type != BlkTIn && type != BlkTOut)
                return CompleteUnsupportedRequest(ram, ramBase, header.Next);

            ulong storageOffset;
            try
            {
                storageOffset = checked(sector * SectorSize);
            }
            catch (OverflowException)
            {
                return CompleteDataRequest(ram, ramBase, header.Next, type, ulong.MaxValue, out bytesTransferred);
            }

            return CompleteDataRequest(ram, ramBase, header.Next, type, storageOffset, out bytesTransferred);
        }

        private byte CompleteDataRequest(byte[] ram, ulong ramBase, ushort firstDataDescriptor, uint type, ulong storageOffset, out uint bytesTransferred)
        {
            bytesTransferred = 0;
            ushort descriptorIndex = firstDataDescriptor;
            uint descriptorsLeft = _queueNum;
            while (descriptorsLeft-- != 0)
            {
                if (!ReadDescriptor(ram, ramBase, descriptorIndex, out Descriptor descriptor))
                    return BlkSIoErr;
                bool isLast = (descriptor.Flags & VirtqDescFNext) == 0;
                if (isLast)
                    return WriteStatus(ram, ramBase, descriptor, BlkSOk) ? BlkSOk : BlkSIoErr;

                if (descriptor.Length > int.MaxValue || storageOffset > (ulong)_storage.Length || descriptor.Length > (ulong)_storage.Length - storageOffset)
                {
                    if (!SkipToStatusAndWrite(ram, ramBase, descriptor, BlkSIoErr))
                        return BlkSIoErr;
                    return BlkSIoErr;
                }

                if (type == BlkTIn)
                {
                    if ((descriptor.Flags & VirtqDescFWrite) == 0)
                    {
                        if (!SkipToStatusAndWrite(ram, ramBase, descriptor, BlkSIoErr))
                            return BlkSIoErr;
                        return BlkSIoErr;
                    }
                    if (!TryWriteBytes(ram, ramBase, descriptor.Address, _storage, (int)storageOffset, (int)descriptor.Length))
                    {
                        if (!SkipToStatusAndWrite(ram, ramBase, descriptor, BlkSIoErr))
                            return BlkSIoErr;
                        return BlkSIoErr;
                    }
                }
                else
                {
                    if ((descriptor.Flags & VirtqDescFWrite) != 0)
                    {
                        if (!SkipToStatusAndWrite(ram, ramBase, descriptor, BlkSIoErr))
                            return BlkSIoErr;
                        return BlkSIoErr;
                    }
                    if (!TryReadBytes(ram, ramBase, descriptor.Address, _storage, (int)storageOffset, (int)descriptor.Length))
                    {
                        if (!SkipToStatusAndWrite(ram, ramBase, descriptor, BlkSIoErr))
                            return BlkSIoErr;
                        return BlkSIoErr;
                    }
                }

                bytesTransferred = checked(bytesTransferred + descriptor.Length);
                storageOffset = checked(storageOffset + descriptor.Length);
                descriptorIndex = descriptor.Next;
            }
            return BlkSIoErr;
        }

        private byte CompleteFlushRequest(byte[] ram, ulong ramBase, ushort statusDescriptor)
        {
            if (!ReadDescriptor(ram, ramBase, statusDescriptor, out Descriptor descriptor))
                return BlkSIoErr;
            return WriteStatus(ram, ramBase, descriptor, BlkSOk) ? BlkSOk : BlkSIoErr;
        }

        private byte CompleteUnsupportedRequest(byte[] ram, ulong ramBase, ushort firstDescriptor)
        {
            if (!ReadDescriptor(ram, ramBase, firstDescriptor, out Descriptor descriptor))
                return BlkSUnsupported;
            return SkipToStatusAndWrite(ram, ramBase, descriptor, BlkSUnsupported) ? BlkSUnsupported : BlkSIoErr;
        }

        private bool SkipToStatusAndWrite(byte[] ram, ulong ramBase, Descriptor descriptor, byte status)
        {
            uint descriptorsLeft = _queueNum;
            while ((descriptor.Flags & VirtqDescFNext) != 0 && descriptorsLeft-- != 0)
            {
                if (!ReadDescriptor(ram, ramBase, descriptor.Next, out descriptor))
                    return false;
            }
            return WriteStatus(ram, ramBase, descriptor, status);
        }

        private bool WriteStatus(byte[] ram, ulong ramBase, Descriptor descriptor, byte status)
        {
            if (descriptor.Length < 1 || (descriptor.Flags & VirtqDescFWrite) == 0)
                return false;
            if (!TryWriteU8(ram, ramBase, descriptor.Address, status))
                return false;
            return true;
        }

        private void PublishUsedBuffer(byte[] ram, ulong ramBase, ushort head, uint bytesTransferred, bool forceInterrupt)
        {
            if (!TryReadU16(ram, ramBase, _queueDevice + 2, out ushort usedIndex))
                return;
            ulong elementAddress = _queueDevice + 4 + 8UL * ((ulong)usedIndex % _queueNum);
            if (!TryWriteU32(ram, ramBase, elementAddress, head))
                return;
            if (!TryWriteU32(ram, ramBase, elementAddress + 4, bytesTransferred))
                return;
            if (!TryWriteU16(ram, ramBase, _queueDevice + 2, unchecked((ushort)(usedIndex + 1))))
                return;
            bool suppressInterrupt = false;
            if (TryReadU16(ram, ramBase, _queueDriver, out ushort availableFlags))
                suppressInterrupt = (availableFlags & VirtqAvailFNoInterrupt) != 0;
            if (!suppressInterrupt || forceInterrupt)
                _interruptStatus |= InterruptUsedBuffer;
        }

        private bool ReadDescriptor(byte[] ram, ulong ramBase, ushort index, out Descriptor descriptor)
        {
            descriptor = default;
            if (index >= _queueNum)
                return false;
            ulong address = _queueDesc + 16UL * index;
            if (!TryReadU64(ram, ramBase, address, out ulong guestAddress))
                return false;
            if (!TryReadU32(ram, ramBase, address + 8, out uint length))
                return false;
            if (!TryReadU16(ram, ramBase, address + 12, out ushort flags))
                return false;
            if (!TryReadU16(ram, ramBase, address + 14, out ushort next))
                return false;
            descriptor = new Descriptor(guestAddress, length, flags, next);
            return true;
        }

        private readonly struct Descriptor
        {
            public readonly ulong Address;
            public readonly uint Length;
            public readonly ushort Flags;
            public readonly ushort Next;

            public Descriptor(ulong address, uint length, ushort flags, ushort next)
            {
                Address = address;
                Length = length;
                Flags = flags;
                Next = next;
            }
        }

        private static bool TryReadU8(byte[] ram, ulong ramBase, ulong address, out byte value)
        {
            value = 0;
            if (!TryGetRamOffset(ram, ramBase, address, 1, out int offset))
                return false;
            value = ram[offset];
            return true;
        }

        private static bool TryWriteU8(byte[] ram, ulong ramBase, ulong address, byte value)
        {
            if (!TryGetRamOffset(ram, ramBase, address, 1, out int offset))
                return false;
            ram[offset] = value;
            return true;
        }

        private static bool TryReadU16(byte[] ram, ulong ramBase, ulong address, out ushort value)
        {
            value = 0;
            if (!TryGetRamOffset(ram, ramBase, address, 2, out int offset))
                return false;
            value = (ushort)(ram[offset] | (ram[offset + 1] << 8));
            return true;
        }

        private static bool TryWriteU16(byte[] ram, ulong ramBase, ulong address, ushort value)
        {
            if (!TryGetRamOffset(ram, ramBase, address, 2, out int offset))
                return false;
            ram[offset] = (byte)value;
            ram[offset + 1] = (byte)(value >> 8);
            return true;
        }

        private static bool TryReadU32(byte[] ram, ulong ramBase, ulong address, out uint value)
        {
            value = 0;
            if (!TryGetRamOffset(ram, ramBase, address, 4, out int offset))
                return false;
            value = (uint)(ram[offset] | (ram[offset + 1] << 8) | (ram[offset + 2] << 16) | (ram[offset + 3] << 24));
            return true;
        }

        private static bool TryWriteU32(byte[] ram, ulong ramBase, ulong address, uint value)
        {
            if (!TryGetRamOffset(ram, ramBase, address, 4, out int offset))
                return false;
            ram[offset] = (byte)value;
            ram[offset + 1] = (byte)(value >> 8);
            ram[offset + 2] = (byte)(value >> 16);
            ram[offset + 3] = (byte)(value >> 24);
            return true;
        }

        private static bool TryReadU64(byte[] ram, ulong ramBase, ulong address, out ulong value)
        {
            value = 0;
            if (!TryGetRamOffset(ram, ramBase, address, 8, out int offset))
                return false;
            value = (ulong)ram[offset]
                | ((ulong)ram[offset + 1] << 8)
                | ((ulong)ram[offset + 2] << 16)
                | ((ulong)ram[offset + 3] << 24)
                | ((ulong)ram[offset + 4] << 32)
                | ((ulong)ram[offset + 5] << 40)
                | ((ulong)ram[offset + 6] << 48)
                | ((ulong)ram[offset + 7] << 56);
            return true;
        }

        private static bool TryWriteBytes(byte[] ram, ulong ramBase, ulong address, byte[] source, int sourceOffset, int count)
        {
            if (!TryGetRamOffset(ram, ramBase, address, count, out int offset))
                return false;
            Buffer.BlockCopy(source, sourceOffset, ram, offset, count);
            return true;
        }

        private static bool TryReadBytes(byte[] ram, ulong ramBase, ulong address, byte[] target, int targetOffset, int count)
        {
            if (!TryGetRamOffset(ram, ramBase, address, count, out int offset))
                return false;
            Buffer.BlockCopy(ram, offset, target, targetOffset, count);
            return true;
        }

        private static bool TryGetRamOffset(byte[] ram, ulong ramBase, ulong address, int length, out int offset)
        {
            offset = 0;
            if (length < 0 || length > ram.Length)
                return false;
            ulong relative = address - ramBase;
            if (address < ramBase || relative > (ulong)(ram.Length - length))
                return false;
            offset = (int)relative;
            return true;
        }

        private static ulong ReadWindow(ulong value, int offset, int size)
        {
            if (size == 8 && offset == 0)
                return value;
            int bits = size * 8;
            ulong mask = bits >= 64 ? ulong.MaxValue : (1UL << bits) - 1;
            return (value >> (offset * 8)) & mask;
        }

        private static ulong WriteWindow(ulong old, int offset, int size, ulong value)
        {
            if (size == 8 && offset == 0)
                return value;
            int bits = size * 8;
            ulong mask = bits >= 64 ? ulong.MaxValue : ((1UL << bits) - 1) << (offset * 8);
            return (old & ~mask) | ((value << (offset * 8)) & mask);
        }

        private static ulong WriteLow32(ulong old, uint value)
            => (old & 0xffffffff00000000UL) | value;

        private static ulong WriteHigh32(ulong old, uint value)
            => (old & 0xffffffffUL) | ((ulong)value << 32);
    }


}
