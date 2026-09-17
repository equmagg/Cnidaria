using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Cnidaria.RiscV;

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
    EnvironmentCallFromVsMode = 10,
    EnvironmentCallFromMMode = 11,
    InstructionPageFault = 12,
    LoadPageFault = 13,
    StorePageFault = 15,
    InstructionGuestPageFault = 20,
    LoadGuestPageFault = 21,
    VirtualInstruction = 22,
    StoreGuestPageFault = 23,
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
    public ulong HostBridgeBase { get; set; } = 0x1000A000UL;
    public bool HostBridgeEnabled { get; set; } = true;
    public ulong FramebufferBase { get; set; } = 0x50000000UL;
    public int FramebufferWidth { get; set; } = 640;
    public int FramebufferHeight { get; set; } = 480;
    public ulong InitialHartId { get; set; }
    public ulong InitialDeviceTreePointer { get; set; }
}
[InlineArray(32)]
internal struct RegisterArray
{
    private ulong _element;
}
/// <summary>The vector file as one flat run of words, which is how every access reaches it</summary>
[InlineArray(32 * 8)]
internal struct VectorRegisterArray
{
    private ulong _element;
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
    private const int CacheBlockSize = 64;
    private const ulong VectorTypeTailAgnostic = 1UL << 6;
    private const ulong VectorTypeMaskAgnostic = 1UL << 7;
    private const int VectorLengthBits = RVVector.LengthBits;
    private const int VectorLengthBytes = VectorLengthBits / 8;
    private const int VectorElementLengthBits = 64;
    private const ulong VectorTypeIllegal = 1UL << 63;
    private const ulong SupervisorInterruptMask = (1UL << 1) | (1UL << 5) | (1UL << 9);
    private const ulong WritableMipMask =
        (1UL << (int)SupervisorSoftwareInterrupt) | (1UL << (int)SupervisorTimerInterrupt) | (1UL << (int)SupervisorExternalInterrupt);
    private const ulong MachineTimerInterrupt = 7;
    private const ulong MachineSoftwareInterrupt = 3;
    private const ulong SupervisorTimerInterrupt = 5;
    private const ulong VirtualSupervisorInterruptMask = (1UL << 2) | (1UL << 6) | (1UL << 10);
    private const ulong MstatusMpv = 1UL << 39;
    private const ulong HstatusSpv = 1UL << 7;
    private const ulong HstatusSpvp = 1UL << 8;
    private const ulong HstatusMask = (1UL << 5) | (1UL << 6) | (1UL << 7) | (1UL << 8) | (1UL << 9)
        | (3UL << 12) | (0x3FUL << 32) | (1UL << 21) | (1UL << 22);
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
    private readonly RVMmioHostBridge? _hostBridge;
    private readonly RVFramebuffer? _framebuffer;
    private readonly byte[] _framebufferPixels;
    private readonly ulong _framebufferPixelBase;
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
    private ulong _stimecmp = ulong.MaxValue;

    // The hypervisor extension: the guest runs with _virtual set, which turns
    // supervisor into VS and user into VU
    private bool _virtual;
    private ulong _hstatus;
    private ulong _hedeleg;
    private ulong _hideleg;
    private ulong _hie;
    private ulong _hvip;
    private ulong _hgeie;
    private ulong _hcounteren;
    private ulong _henvcfg;
    private ulong _hgatp;
    private ulong _htval;
    private ulong _htinst;
    private ulong _htimedelta;
    private ulong _vsstatus;
    private ulong _vstvec;
    private ulong _vsscratch;
    private ulong _vsepc;
    private ulong _vscause;
    private ulong _vstval;
    private ulong _vsatp;
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
    public RVMmioHostBridge? HostBridge => _hostBridge;
    public RVFramebuffer? Framebuffer => _framebuffer;
    public RVMmioBlockDevice? BlockDevice => _blocks.Length == 0 ? null : _blocks[0];
    public ReadOnlySpan<RVMmioBlockDevice> BlockDevices => _blocks;
    public ulong BlockDeviceStride => _blockDeviceStride;
    public ulong HartId => _hartId;
    public ReadOnlySpan<ulong> IntegerRegisters => _x;
    public ReadOnlySpan<ulong> FloatingPointRegisters => _f;
    public ReadOnlySpan<ulong> VectorRegisters => _v;
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
        _hostBridge = config.HostBridgeEnabled
            ? new RVMmioHostBridge(config.HostBridgeBase)
            : null;
        _framebuffer = config.FramebufferWidth > 0 && config.FramebufferHeight > 0
            ? new RVFramebuffer(config.FramebufferBase, config.FramebufferWidth, config.FramebufferHeight)
            : null;
        _framebufferPixels = _framebuffer?.Pixels ?? Array.Empty<byte>();
        _framebufferPixelBase = _framebuffer?.PixelBaseAddress ?? 0;
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
        RequireDisjointDevices();
        Reset(config.InitialStackPointer);
    }

    /// <summary>A window that overlaps another one would be shadowed by whichever is probed first.</summary>
    private void RequireDisjointDevices()
    {
        var windows = new List<(string Name, ulong Base, ulong Size)>
        {
            ("uart", _uart.BaseAddress, RVUart16550.RegisterWindowSize),
            ("clint", _clint.BaseAddress, RVClint.RegisterWindowSize),
            ("plic", _plic.BaseAddress, RVPlic.RegisterWindowSize),
        };
        if (_keyboard != null)
            windows.Add(("keyboard", _keyboard.BaseAddress, RVMmioKeyboard.RegisterWindowSize));
        if (_hostBridge != null)
            windows.Add(("host bridge", _hostBridge.BaseAddress, RVMmioHostBridge.RegisterWindowSize));
        if (_framebuffer != null)
            windows.Add(("framebuffer", _framebuffer.BaseAddress, _framebuffer.WindowSize));
        for (int i = 0; i < _blocks.Length; i++)
            windows.Add(("block device " + i, _blocks[i].BaseAddress, RVMmioBlockDevice.RegisterWindowSize));

        for (int i = 0; i < windows.Count; i++)
        {
            for (int j = i + 1; j < windows.Count; j++)
            {
                var left = windows[i];
                var right = windows[j];
                if (left.Base < right.Base + right.Size && right.Base < left.Base + left.Size)
                    throw new ArgumentException($"The {left.Name} and {right.Name} register windows overlap.");
            }
        }
    }

    public void Reset(ulong initialStackPointer = 0)
    {
        Span<ulong> spanX = _x;
        spanX.Clear();
        Span<ulong> spanF = _f;
        spanF.Clear();
        Span<ulong> spanV = _v;
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
        _stimecmp = ulong.MaxValue;
        _virtual = false;
        _hstatus = 0;
        _hedeleg = 0;
        _hideleg = 0;
        _hie = 0;
        _hvip = 0;
        _hgeie = 0;
        _hcounteren = 0;
        _henvcfg = 0;
        _hgatp = 0;
        _htval = 0;
        _htinst = 0;
        _htimedelta = 0;
        _vsstatus = 0;
        _vstvec = 0;
        _vsscratch = 0;
        _vsepc = 0;
        _vscause = 0;
        _vstval = 0;
        _vsatp = 0;
        _sscratch = 0;
        _sepc = 0;
        _scause = 0;
        _stval = 0;
        _satp = 0;
        _uart.Reset();
        _clint.Reset();
        _plic.Reset();
        _keyboard?.Reset();
        _hostBridge?.Reset();
        _framebuffer?.Reset();
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
            if ((pc & 1) != 0)
            {
                EnterTrap((ulong)RVTrapCause.InstructionAddressMisaligned, pc, pc);
                continue;
            }

            uint instruction;
            if ((pc & 4095) != 4094)
            {
                // Four bytes from here cannot leave the page, so one read covers either length
                if (!TryReadMemory(pc, 4, RVMemoryAccess.Execute, out ulong rawInstruction, out trapCause, out trapValue))
                {
                    EnterTrap(trapCause, trapValue, pc);
                    continue;
                }
                instruction = (uint)rawInstruction;
            }
            else
            {
                // The last halfword of a page: only fetch the second half if the first says to
                if (!TryReadMemory(pc, 2, RVMemoryAccess.Execute, out ulong lowHalf, out trapCause, out trapValue))
                {
                    EnterTrap(trapCause, trapValue, pc);
                    continue;
                }
                instruction = (uint)lowHalf;
                if ((instruction & 3) == 3)
                {
                    if (!TryReadMemory(pc + 2, 2, RVMemoryAccess.Execute, out ulong highHalf, out trapCause, out trapValue))
                    {
                        EnterTrap(trapCause, trapValue, pc);
                        continue;
                    }
                    instruction |= (uint)highHalf << 16;
                }
            }

            ulong nextPc;
            if ((instruction & 3) == 3)
            {
                nextPc = pc + 4;
            }
            else
            {
                uint expanded = ExpandCompressed((ushort)instruction);
                if (expanded == 0)
                {
                    EnterTrap((ulong)RVTrapCause.IllegalInstruction, instruction, pc);
                    continue;
                }
                instruction = expanded;
                nextPc = pc + 2;
            }
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
                        if ((target & 1) != 0)
                        {
                            trapped = true;
                            trapCause = (ulong)RVTrapCause.InstructionAddressMisaligned;
                            trapValue = target;
                            break;
                        }
                        if (rd != 0) _x[rd] = nextPc;
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
                        if ((target & 1) != 0)
                        {
                            trapped = true;
                            trapCause = (ulong)RVTrapCause.InstructionAddressMisaligned;
                            trapValue = target;
                            break;
                        }
                        if (rd != 0) _x[rd] = nextPc;
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
                        if ((target & 1) != 0)
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
                        else if (funct7 == 0x0E000000U)
                        {
                            switch (instruction & Funct3Mask)
                            {
                                case 0x5000U: value = b == 0 ? 0UL : a; break;
                                case 0x7000U: value = b != 0 ? 0UL : a; break;
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
                        if (funct3 == 0x0000U || funct3 == 0x1000U)
                            break;
                        if (funct3 != 0x2000U)
                        {
                            trapped = true;
                            break;
                        }

                        // The cache block operations name their block by any address inside it
                        ulong blockBase = _x[(int)((instruction >> 15) & 31)] & ~(ulong)(CacheBlockSize - 1);
                        uint blockOperation = instruction >> 20;
                        if (blockOperation == 4)
                        {
                            for (int offset = 0; offset < CacheBlockSize; offset += 8)
                            {
                                if (!TryWriteMemory(blockBase + (ulong)offset, 8, 0, out trapCause, out trapValue))
                                {
                                    trapped = true;
                                    break;
                                }
                            }
                        }
                        else if (blockOperation <= 2)
                        {
                            // Nothing is cached here, so the operation only has to fault where a store would
                            if (!TryTranslate(blockBase, RVMemoryAccess.Store, out _, out trapCause, out trapValue))
                                trapped = true;
                        }
                        else
                        {
                            trapped = true;
                        }
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
                                            ? (_virtual ? (ulong)RVTrapCause.EnvironmentCallFromVsMode : (ulong)RVTrapCause.EnvironmentCallFromSMode)
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
                                case 0x00D00073: // WRS.NTO
                                case 0x01D00073: // WRS.STO
                                    // A reservation set may lose its reservation at once, so waiting is optional
                                    goto outer_break;
                                case 0x18000073: // SFENCE.W.INVAL
                                case 0x18100073: // SFENCE.INVAL.IR
                                    if (_mode < RVPrivilegeMode.Supervisor)
                                        trapped = true;
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

                        if (funct3 == 0x4000U)
                        {
                            if ((instruction & 0x80000000U) != 0)
                            {
                                // A may-be-operation reads as zero until some extension gives it a meaning
                                if (((instruction >> 28) & 3) == 0 &&
                                    (((instruction >> 25) & 1) != 0 || ((instruction >> 22) & 0xF) == 7))
                                    _x[rd] = 0;
                                else
                                    trapped = true;
                                break;
                            }

                            if (!ExecuteHypervisorLoadStore(instruction, rd, out trapCause, out trapValue))
                                trapped = true;
                            break;
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

                        // The forms whose shape the element loop cannot describe
                        if (IsVectorSpecialForm(funct6, funct3))
                        {
                            if (!ExecuteVectorSpecial(instruction, funct6, funct3, sewBytes, groupBytes, groupRegisters, vlmax))
                                trapped = true;
                            break;
                        }

                        bool narrowingShift = funct6 >= 44 && funct6 <= 47 && (funct3 == 0 || funct3 == 4 || funct3 == 3);

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

                        // A reduction reads and writes single registers whatever the group holds
                        if (!floatOperation && funct3 == 2 && funct6 <= 7)
                        {
                            if (!ExecuteVectorReduction(instruction, funct6, vs2, source1, sewBytes, groupRegisters))
                                trapped = true;
                            break;
                        }

                        if (funct6 == 16 && (funct3 == 2 || funct3 == 6))
                        {
                            if (!ExecuteVectorScalarMove(instruction, funct3, vs2, sewBytes, vlmax))
                                trapped = true;
                            break;
                        }

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
                            scalar = (ulong)(funct6 == 20 || funct6 == 21
                                    ? ((funct6 & 1) << 5) | source1
                                    : (funct6 == 37 || funct6 == 40 || funct6 == 41 || (funct6 >= 42 && funct6 <= 47) || funct6 == 12)
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

                            bool selected = unmasked || ReadVectorMaskBit(ref vectorBytes, i);
                            if (!selected && funct6 != 23)
                            {
                                if (!maskDestination && (_vtype & VectorTypeMaskAgnostic) != 0)
                                    WriteVectorElement(ref vectorBytes, currentDestinationOffset, sewBytes, mask);
                                continue;
                            }

                            ulong a = vectorSource1 ? ReadVectorElement(ref vectorBytes, currentSource1Offset, sewBytes) : scalar;
                            ulong b = ReadVectorElement(ref vectorBytes, currentSource2Offset, source2Bytes);
                            if (funct6 == 23 && !selected)
                            {
                                WriteVectorElement(ref vectorBytes, currentDestinationOffset, sewBytes, b & mask);
                                continue;
                            }

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
                                if (funct6 == 44)
                                    result = b >> shift;
                                else if (funct6 == 45)
                                    result = (ulong)(SignExtendElement(b, sourceBits) >> shift);
                                else if (funct6 == 46)
                                {
                                    result = (b >> shift) + VectorRoundingIncrement(b, shift);
                                    if (result > mask) { result = mask; _vxsat = 1; }
                                }
                                else
                                {
                                    long wide = SignExtendElement(b, sourceBits);
                                    long clipped = (wide >> shift) + (long)VectorRoundingIncrement((ulong)wide, shift);
                                    long ceiling = (long)(mask >> 1);
                                    if (clipped > ceiling) { clipped = ceiling; _vxsat = 1; }
                                    else if (clipped < -ceiling - 1) { clipped = -ceiling - 1; _vxsat = 1; }
                                    result = (ulong)clipped;
                                }
                            }
                            else if (funct6 == 12)
                            {
                                if (funct3 != 0 && funct3 != 4 && funct3 != 3)
                                {
                                    vectorFailed = true;
                                    break;
                                }
                                result = a < (ulong)vlmax ? ReadVectorElement(ref vectorBytes, gatherBaseOffset + (int)a * sewBytes, sewBytes) : 0;
                            }
                            // The multiply-add group shares its funct6 with the shifts, and only the
                            // integer-source forms belong to it
                            else if ((funct3 == 2 || funct3 == 6) && (funct6 == 41 || funct6 == 43 || funct6 == 45 || funct6 == 47))
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

                        if (!maskDestination)
                            FillVectorAgnosticTail(ref vectorBytes, vd, sewBytes, vl, vlmax);

                        _vstart = 0;
                        _mstatus |= MstatusVsMask;
                    }
                    break;

                case 0x2F: // AMO
                    {
                        uint funct3 = instruction & Funct3Mask;
                        int size = funct3 == 0x2000U ? 4 : funct3 == 0x3000U ? 8 : funct3 == 0x4000U ? 16 : 0;
                        if (size == 0)
                        {
                            trapped = true;
                            break;
                        }
                        ulong address = _x[(int)((instruction >> 15) & 31)];
                        uint op = instruction & 0xF8000000U;
                        if ((address & (ulong)(size - 1)) != 0)
                        {
                            trapCause = op == 0x10000000U
                                ? (ulong)RVTrapCause.LoadAddressMisaligned
                                : (ulong)RVTrapCause.StoreAddressMisaligned;
                            trapValue = address;
                            trapped = true;
                            break;
                        }

                        int rs2 = (int)((instruction >> 20) & 31);

                        if (op == 0x28000000U)
                        {
                            if (size == 16)
                            {
                                if ((rd & 1) != 0 || (rs2 & 1) != 0)
                                {
                                    trapped = true;
                                    break;
                                }
                                if (!TryTranslate(address, RVMemoryAccess.Store, out _, out trapCause, out trapValue) ||
                                    !TryTranslate(address + 8, RVMemoryAccess.Store, out _, out trapCause, out trapValue) ||
                                    !TryReadMemory(address, 8, RVMemoryAccess.Load, out ulong oldLo, out trapCause, out trapValue) ||
                                    !TryReadMemory(address + 8, 8, RVMemoryAccess.Load, out ulong oldHi, out trapCause, out trapValue))
                                {
                                    trapped = true;
                                    break;
                                }

                                ulong expectedLo = rd == 0 ? 0UL : _x[rd];
                                ulong expectedHi = rd == 0 ? 0UL : _x[rd + 1];
                                ulong desiredLo = rs2 == 0 ? 0UL : _x[rs2];
                                ulong desiredHi = rs2 == 0 ? 0UL : _x[rs2 + 1];
                                if (oldLo == expectedLo && oldHi == expectedHi)
                                {
                                    if (!TryWriteMemory(address, 8, desiredLo, out trapCause, out trapValue) ||
                                        !TryWriteMemory(address + 8, 8, desiredHi, out trapCause, out trapValue))
                                    {
                                        trapped = true;
                                        break;
                                    }
                                }

                                if (rd != 0)
                                {
                                    _x[rd] = oldLo;
                                    _x[rd + 1] = oldHi;
                                }
                                break;
                            }

                            if (!TryTranslate(address, RVMemoryAccess.Store, out _, out trapCause, out trapValue) ||
                                !TryReadMemory(address, size, RVMemoryAccess.Load, out ulong casOld, out trapCause, out trapValue))
                            {
                                trapped = true;
                                break;
                            }

                            ulong expected = size == 4 ? (uint)_x[rd] : _x[rd];
                            ulong desired = size == 4 ? (uint)_x[rs2] : _x[rs2];
                            ulong observed = size == 4 ? (uint)casOld : casOld;
                            if (observed == expected && !TryWriteMemory(address, size, desired, out trapCause, out trapValue))
                            {
                                trapped = true;
                                break;
                            }
                            if (rd != 0)
                                _x[rd] = size == 4 ? SignExtend32((uint)casOld) : casOld;
                            break;
                        }

                        if (size == 16)
                        {
                            trapped = true;
                            break;
                        }
                        if (!TryReadMemory(address, size, RVMemoryAccess.Load, out ulong old, out trapCause, out trapValue))
                        {
                            trapped = true;
                            break;
                        }

                        if (op == 0x10000000U)
                        {
                            if (rd != 0)
                                _x[rd] = size == 4 ? SignExtend32((uint)old) : old;
                            _reservationAddress = address;
                            _hasReservation = true;
                            break;
                        }

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
                        else if (funct3 == 0x1000U) size = 2;
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
                        // A narrow value sits in the low bits with the rest set, so a wider read sees a NaN
                        _f[rd] = size == 8 ? value
                            : size == 4 ? 0xFFFFFFFF00000000UL | (uint)value
                            : 0xFFFFFFFFFFFF0000UL | (ushort)value;
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
                        else if (funct3 == 0x1000U) size = 2;
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
                        int funct3 = (int)((instruction >> 12) & 7);
                        // A rounding mode of seven means the one frm currently holds
                        int rm = funct3 == 7 ? (int)_frm : funct3;

                        if (opcode != 0x53U)
                        {
                            if (rm > 4)
                            {
                                trapped = true;
                                break;
                            }

                            int rs3 = (int)((instruction >> 27) & 31);
                            if ((instruction & (1U << 25)) == 0)
                            {
                                float a = ReadFloat32(rs1);
                                float b = ReadFloat32(rs2);
                                float c = ReadFloat32(rs3);
                                if (opcode == 0x4B || opcode == 0x4F)
                                    a = -a;
                                if (opcode == 0x47 || opcode == 0x4F)
                                    c = -c;
                                // The product of two floats is exact in a double, so the sum rounds only once
                                double product = (double)a * b;
                                double sum = product + c;
                                float r = (float)sum;
                                if (rm != 0 && !float.IsNaN(r) && !float.IsInfinity(r))
                                {
                                    double high = sum - c;
                                    double error = (product - high) + (c - (sum - high));
                                    double residual = (sum - r) + error;
                                    if (residual != 0.0)
                                        r = RoundSingleDirected(r, residual, rm);
                                }
                                WriteFloat32(rd, r);
                                break;
                            }
                            else
                            {
                                double a = ReadFloat64(rs1);
                                double b = ReadFloat64(rs2);
                                double c = ReadFloat64(rs3);
                                if (opcode == 0x4B || opcode == 0x4F)
                                    a = -a;
                                if (opcode == 0x47 || opcode == 0x4F)
                                    c = -c;
                                double r = Math.FusedMultiplyAdd(a, b, c);
                                if (rm != 0 && !double.IsNaN(r) && !double.IsInfinity(r))
                                {
                                    double product = a * b;
                                    double productError = Math.FusedMultiplyAdd(a, b, -product);
                                    double sum = product + c;
                                    double high = sum - c;
                                    double sumError = (product - high) + (c - (sum - high));
                                    double residual = (sum - r) + (sumError + productError);
                                    if (residual != 0.0)
                                        r = RoundDoubleDirected(r, residual, rm);
                                }
                                WriteFloat64(rd, r);
                                break;
                            }
                        }

                        switch ((int)((instruction >> 25) & 127))
                        {
                            case 0x00:
                            case 0x04:
                            case 0x08:
                            case 0x0C:
                            {
                                if (rm > 4)
                                {
                                    trapped = true;
                                    goto outer_break;
                                }

                                float a = ReadFloat32(rs1);
                                float b = ReadFloat32(rs2);
                                int kind = (int)((instruction >> 25) & 127);
                                // Every one of these is exact in a double, or close enough that the
                                // second rounding cannot cross a float boundary
                                double wide = kind switch
                                {
                                    0x00 => (double)a + b,
                                    0x04 => (double)a - b,
                                    0x08 => (double)a * b,
                                    _ => (double)a / b,
                                };
                                float r = (float)wide;
                                if (rm != 0 && !float.IsNaN(r) && !float.IsInfinity(r))
                                {
                                    double residual = wide - r;
                                    if (residual != 0.0)
                                        r = RoundSingleDirected(r, residual, rm);
                                }
                                WriteFloat32(rd, r);
                                goto outer_break;
                            }
                            case 0x01:
                            case 0x05:
                            case 0x09:
                            case 0x0D:
                            {
                                if (rm > 4)
                                {
                                    trapped = true;
                                    goto outer_break;
                                }

                                double a = ReadFloat64(rs1);
                                double b = ReadFloat64(rs2);
                                int kind = (int)((instruction >> 25) & 127);
                                if (kind == 0x05)
                                    b = -b;
                                double r = kind switch
                                {
                                    0x00 or 0x01 or 0x04 or 0x05 => a + b,
                                    0x09 => a * b,
                                    _ => a / b,
                                };
                                if (rm != 0 && !double.IsNaN(r) && !double.IsInfinity(r))
                                {
                                    double residual;
                                    if (kind == 0x09)
                                    {
                                        residual = Math.FusedMultiplyAdd(a, b, -r);
                                    }
                                    else if (kind == 0x0D)
                                    {
                                        // a - r*b is the part of the quotient that did not fit
                                        residual = Math.FusedMultiplyAdd(-r, b, a) / b;
                                    }
                                    else
                                    {
                                        double high = r - a;
                                        residual = (a - (r - high)) + (b - high);
                                    }
                                    if (residual != 0.0)
                                        r = RoundDoubleDirected(r, residual, rm);
                                }
                                WriteFloat64(rd, r);
                                goto outer_break;
                            }
                            case 0x10:
                                if (!ExecuteFloatSign(false, funct3, rd, rs1, rs2))
                                    trapped = true;
                                goto outer_break;
                            case 0x11:
                                if (!ExecuteFloatSign(true, funct3, rd, rs1, rs2))
                                    trapped = true;
                                goto outer_break;
                            case 0x14:
                                if (!ExecuteFloatMinMax(false, funct3, rd, rs1, rs2))
                                    trapped = true;
                                goto outer_break;
                            case 0x15:
                                if (!ExecuteFloatMinMax(true, funct3, rd, rs1, rs2))
                                    trapped = true;
                                goto outer_break;
                            case 0x20:
                                if (rm > 4)
                                {
                                    trapped = true;
                                    goto outer_break;
                                }
                                if (rs2 == 1)
                                {
                                    double wide = ReadFloat64(rs1);
                                    float narrow = (float)wide;
                                    if (rm != 0 && !float.IsNaN(narrow) && !float.IsInfinity(narrow))
                                    {
                                        double residual = wide - narrow;
                                        if (residual != 0.0)
                                            narrow = RoundSingleDirected(narrow, residual, rm);
                                    }
                                    WriteFloat32(rd, narrow);
                                    goto outer_break;
                                }
                                if (rs2 == 2)
                                {
                                    WriteFloat32(rd, ReadFloat16(rs1));
                                    goto outer_break;
                                }
                                if (rs2 == 4 || rs2 == 5)
                                {
                                    float value = ReadFloat32(rs1);
                                    float rounded = (float)RoundToIntegral(value, rm);
                                    if (rs2 == 5 && rounded != value && !float.IsNaN(value) && !float.IsInfinity(value))
                                        _fflags |= 1;
                                    WriteFloat32(rd, float.IsNaN(value) || float.IsInfinity(value) ? value : rounded);
                                    goto outer_break;
                                }
                                trapped = true;
                                goto outer_break;
                            case 0x21:
                                if (rm > 4)
                                {
                                    trapped = true;
                                    goto outer_break;
                                }
                                if (rs2 == 0)
                                {
                                    WriteFloat64(rd, ReadFloat32(rs1));
                                    goto outer_break;
                                }
                                if (rs2 == 2)
                                {
                                    WriteFloat64(rd, ReadFloat16(rs1));
                                    goto outer_break;
                                }
                                if (rs2 == 4 || rs2 == 5)
                                {
                                    double value = ReadFloat64(rs1);
                                    double rounded = RoundToIntegral(value, rm);
                                    if (rs2 == 5 && rounded != value && !double.IsNaN(value) && !double.IsInfinity(value))
                                        _fflags |= 1;
                                    WriteFloat64(rd, double.IsNaN(value) || double.IsInfinity(value) ? value : rounded);
                                    goto outer_break;
                                }
                                trapped = true;
                                goto outer_break;
                            case 0x22:
                            {
                                if (rm > 4 || (rs2 != 0 && rs2 != 1))
                                {
                                    trapped = true;
                                    goto outer_break;
                                }

                                double wide = rs2 == 0 ? ReadFloat32(rs1) : ReadFloat64(rs1);
                                Half narrow = (Half)wide;
                                if (rm != 0 && !Half.IsNaN(narrow) && !Half.IsInfinity(narrow))
                                {
                                    double residual = wide - (double)narrow;
                                    if (residual != 0.0)
                                    {
                                        if (rm == 2)
                                            narrow = residual < 0.0 ? Half.BitDecrement(narrow) : narrow;
                                        else if (rm == 3)
                                            narrow = residual > 0.0 ? Half.BitIncrement(narrow) : narrow;
                                        else if (rm == 1)
                                            narrow = (double)narrow > 0.0
                                                ? (residual < 0.0 ? Half.BitDecrement(narrow) : narrow)
                                                : (residual > 0.0 ? Half.BitIncrement(narrow) : narrow);
                                        else
                                        {
                                            double step = residual > 0.0
                                                ? (double)Half.BitIncrement(narrow) - (double)narrow
                                                : (double)Half.BitDecrement(narrow) - (double)narrow;
                                            if (residual + residual == step)
                                                narrow = (Half)((double)narrow + step);
                                        }
                                    }
                                }
                                _f[rd] = 0xFFFFFFFFFFFF0000UL | BitConverter.HalfToUInt16Bits(narrow);
                                goto outer_break;
                            }
                            case 0x72:
                                if (rs2 != 0 || funct3 != 0)
                                {
                                    trapped = true;
                                    goto outer_break;
                                }
                                _x[rd] = (ulong)(long)(short)(ushort)_f[rs1];
                                goto outer_break;
                            case 0x7A:
                                if (rs2 != 0 || funct3 != 0)
                                {
                                    trapped = true;
                                    goto outer_break;
                                }
                                _f[rd] = 0xFFFFFFFFFFFF0000UL | (ushort)_x[rs1];
                                goto outer_break;
                            case 0x2C:
                            {
                                if (rs2 != 0 || rm > 4)
                                {
                                    trapped = true;
                                    goto outer_break;
                                }

                                double wide = Math.Sqrt(ReadFloat32(rs1));
                                float r = (float)wide;
                                if (rm != 0 && !float.IsNaN(r) && !float.IsInfinity(r))
                                {
                                    double residual = wide - r;
                                    if (residual != 0.0)
                                        r = RoundSingleDirected(r, residual, rm);
                                }
                                WriteFloat32(rd, r);
                                goto outer_break;
                            }
                            case 0x2D:
                            {
                                if (rs2 != 0 || rm > 4)
                                {
                                    trapped = true;
                                    goto outer_break;
                                }

                                double a = ReadFloat64(rs1);
                                double r = Math.Sqrt(a);
                                if (rm != 0 && !double.IsNaN(r) && !double.IsInfinity(r) && r != 0.0)
                                {
                                    // a - r*r says which side of the root the rounded value sits on
                                    double residual = Math.FusedMultiplyAdd(-r, r, a) / (r + r);
                                    if (residual != 0.0)
                                        r = RoundDoubleDirected(r, residual, rm);
                                }
                                WriteFloat64(rd, r);
                                goto outer_break;
                            }
                            case 0x50:
                                if (!ExecuteFloatCompare(false, funct3, rd, rs1, rs2))
                                    trapped = true;
                                goto outer_break;
                            case 0x51:
                                if (!ExecuteFloatCompare(true, funct3, rd, rs1, rs2))
                                    trapped = true;
                                goto outer_break;
                            case 0x60:
                                if (rm > 4 || !ExecuteFloatToInt(false, rs2, rd, rs1, rm))
                                    trapped = true;
                                goto outer_break;
                            case 0x61:
                                if (rs2 == 8)
                                {
                                    // fcvtmod.w.d truncates and then wraps, where the plain conversion saturates
                                    if (funct3 != 1)
                                    {
                                        trapped = true;
                                        goto outer_break;
                                    }
                                    double value = ReadFloat64(rs1);
                                    long wrapped = double.IsNaN(value) || double.IsInfinity(value)
                                        ? 0L
                                        : unchecked((long)Math.Truncate(Math.IEEERemainder(Math.Truncate(value), 4294967296.0)));
                                    if (rd != 0)
                                        _x[rd] = SignExtend32((uint)wrapped);
                                    goto outer_break;
                                }
                                if (rm > 4 || !ExecuteFloatToInt(true, rs2, rd, rs1, rm))
                                    trapped = true;
                                goto outer_break;
                            case 0x68:
                                if (rm > 4 || !ExecuteIntToFloat(false, rs2, rd, rs1, rm))
                                    trapped = true;
                                goto outer_break;
                            case 0x69:
                                if (rm > 4 || !ExecuteIntToFloat(true, rs2, rd, rs1, rm))
                                    trapped = true;
                                goto outer_break;
                            case 0x70:
                                if (rs2 != 0)
                                {
                                    trapped = true;
                                    goto outer_break;
                                }
                                if (funct3 == 0) { _x[rd] = SignExtend32((uint)_f[rs1]); goto outer_break; }
                                if (funct3 == 1) { _x[rd] = ClassifyFloat32((uint)_f[rs1]); goto outer_break; }
                                trapped = true;
                                goto outer_break;
                            case 0x71:
                                if (rs2 != 0)
                                {
                                    trapped = true;
                                    goto outer_break;
                                }
                                if (funct3 == 0) { _x[rd] = _f[rs1]; goto outer_break; }
                                if (funct3 == 1) { _x[rd] = ClassifyFloat64(_f[rs1]); goto outer_break; }
                                trapped = true;
                                goto outer_break;
                            case 0x78:
                                if (funct3 != 0)
                                {
                                    trapped = true;
                                    goto outer_break;
                                }
                                if (rs2 == 1)
                                {
                                    WriteFloat32(rd, (float)LoadFloatConstant(rs1, false));
                                    goto outer_break;
                                }
                                if (rs2 != 0)
                                {
                                    trapped = true;
                                    goto outer_break;
                                }
                                WriteFloat32(rd, BitConverter.Int32BitsToSingle((int)_x[rs1]));
                                goto outer_break;
                            case 0x79:
                                if (funct3 != 0)
                                {
                                    trapped = true;
                                    goto outer_break;
                                }
                                if (rs2 == 1)
                                {
                                    WriteFloat64(rd, LoadFloatConstant(rs1, true));
                                    goto outer_break;
                                }
                                if (rs2 != 0)
                                {
                                    trapped = true;
                                    goto outer_break;
                                }
                                _f[rd] = _x[rs1];
                                goto outer_break;
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


    /// <summary>Sstc hands the supervisor its own timer, but only where menvcfg turns it on</summary>
    private bool SupervisorTimerEnabled => (_menvcfg & (1UL << 63)) != 0;

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
        if (SupervisorTimerEnabled)
        {
            if (_clint.MTime >= _stimecmp)
                mip |= 1UL << (int)SupervisorTimerInterrupt;
            else
                mip &= ~(1UL << (int)SupervisorTimerInterrupt);
        }

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
        bool wasVirtual = _virtual;

        // A guest trap the hypervisor delegated on lands in the guest's own supervisor
        if (delegated && wasVirtual && causeCode < 64 &&
            ((interrupt ? _hideleg : _hedeleg) & (1UL << (int)causeCode)) != 0)
        {
            ulong guest = _vsstatus;
            guest = (guest & ~MstatusSpie) | ((guest & MstatusSie) != 0 ? MstatusSpie : 0);
            guest &= ~MstatusSie;
            guest = _mode == RVPrivilegeMode.Supervisor ? guest | MstatusSpp : guest & ~MstatusSpp;
            _vsstatus = guest;
            _vsepc = pc;
            _vscause = cause;
            _vstval = value;
            _mode = RVPrivilegeMode.Supervisor;
            _pc = TrapVector(_vstvec, causeCode, interrupt);
            return;
        }

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
            _hstatus = wasVirtual ? _hstatus | HstatusSpv : _hstatus & ~HstatusSpv;
            if (wasVirtual)
                _hstatus = _mode == RVPrivilegeMode.Supervisor ? _hstatus | HstatusSpvp : _hstatus & ~HstatusSpvp;
            _virtual = false;
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
        _mstatus = wasVirtual ? _mstatus | MstatusMpv : _mstatus & ~MstatusMpv;
        _virtual = false;
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

    /// <summary>The guest load and store instructions, which reach guest memory from the host</summary>
    private bool ExecuteHypervisorLoadStore(uint instruction, int rd, out ulong trapCause, out ulong trapValue)
    {
        trapCause = (ulong)RVTrapCause.IllegalInstruction;
        trapValue = instruction;

        uint funct7 = instruction >> 25;
        if (funct7 < 0x30 || funct7 > 0x37)
            return false;
        if (_mode < RVPrivilegeMode.Supervisor || _virtual)
            return false;

        bool store = (funct7 & 1) != 0;
        int rs2 = (int)((instruction >> 20) & 31);
        int size = 1 << (int)((funct7 >> 1) & 3);
        bool unsignedLoad;
        bool asExecute;
        if (store)
        {
            if (rs2 > 31 || ((instruction >> 7) & 31) != 0 && false)
                return false;
            unsignedLoad = false;
            asExecute = false;
        }
        else
        {
            if (rs2 == 3)
            {
                if (size == 1 || size == 8)
                    return false;
                unsignedLoad = true;
                asExecute = true;
            }
            else if (rs2 == 1)
            {
                if (size == 8)
                    return false;
                unsignedLoad = true;
                asExecute = false;
            }
            else if (rs2 == 0)
            {
                unsignedLoad = false;
                asExecute = false;
            }
            else
            {
                return false;
            }
        }

        // The guest privilege is the one hstatus remembers for it
        RVPrivilegeMode guestMode = (_hstatus & (1UL << 8)) != 0 ? RVPrivilegeMode.Supervisor : RVPrivilegeMode.User;
        ulong address = _x[(int)((instruction >> 15) & 31)];
        if ((address & (ulong)(size - 1)) != 0)
        {
            trapCause = store ? (ulong)RVTrapCause.StoreAddressMisaligned : (ulong)RVTrapCause.LoadAddressMisaligned;
            trapValue = address;
            return false;
        }

        var access = store ? RVMemoryAccess.Store : RVMemoryAccess.Load;
        if (!TryTranslateGuest(address, access, guestMode, asExecute, out ulong physical, out trapCause, out trapValue))
            return false;

        if (store)
            return TryWritePhysical(physical, size, _x[rs2], out trapCause, out trapValue);

        if (!TryReadPhysical(physical, size, RVMemoryAccess.Load, out ulong value, out trapCause, out trapValue))
            return false;
        if (rd != 0)
            _x[rd] = unsignedLoad || size == 8 ? value : (ulong)SignExtendBySize(value, size);
        return true;
    }

    private static long SignExtendBySize(ulong value, int size) => size switch
    {
        1 => (sbyte)value,
        2 => (short)value,
        4 => (int)value,
        _ => (long)value,
    };

    private static bool IsPrivilegedFenceInstruction(uint instruction)
    {
        if ((instruction & 0x0000707FU) != 0x00000073U)
            return false;

        int funct7 = (int)((instruction >> 25) & 0x7F);
        // sfence.vma, sinval.vma, hfence and hinval in both their guest forms
        return funct7 == 0x09 || funct7 == 0x0B || funct7 == 0x11 || funct7 == 0x13
            || funct7 == 0x31 || funct7 == 0x33;
    }

    private void ReturnFromMachineTrap(ref ulong nextPc)
    {
        ulong status = _mstatus;
        RVPrivilegeMode target = (RVPrivilegeMode)((status >> 11) & 3);
        status = (status & ~MstatusMie) | ((status & MstatusMpie) != 0 ? MstatusMie : 0);
        status |= MstatusMpie;
        status &= ~MstatusMppMask;
        status &= ~MstatusMprv;
        // mstatus remembers whether the mode it is returning to was a guest
        bool returnVirtual = target != RVPrivilegeMode.Machine && (status & MstatusMpv) != 0;
        status &= ~MstatusMpv;
        _mstatus = status;
        _virtual = returnVirtual;
        _mode = target;
        nextPc = _mepc;
    }

    private void ReturnFromSupervisorTrap(ref ulong nextPc)
    {
        if (_virtual)
        {
            ulong guest = _vsstatus;
            RVPrivilegeMode guestTarget = (guest & MstatusSpp) != 0 ? RVPrivilegeMode.Supervisor : RVPrivilegeMode.User;
            guest = (guest & ~MstatusSie) | ((guest & MstatusSpie) != 0 ? MstatusSie : 0);
            guest |= MstatusSpie;
            guest &= ~MstatusSpp;
            _vsstatus = guest;
            _mode = guestTarget;
            nextPc = _vsepc;
            return;
        }

        ulong status = _mstatus;
        RVPrivilegeMode target = (status & MstatusSpp) != 0 ? RVPrivilegeMode.Supervisor : RVPrivilegeMode.User;
        status = (status & ~MstatusSie) | ((status & MstatusSpie) != 0 ? MstatusSie : 0);
        status |= MstatusSpie;
        status &= ~MstatusSpp;
        _mstatus = status;
        // hstatus remembers whether the supervisor was interrupted out of a guest
        bool returnVirtual = (_hstatus & HstatusSpv) != 0;
        _hstatus &= ~HstatusSpv;
        _virtual = returnVirtual;
        _mode = target;
        nextPc = _sepc;
    }

    private bool TryReadCsr(int csr, bool checkAccess, out ulong value)
    {
        value = 0;
        if (checkAccess && !CheckCsrAccess(csr, false))
            return false;
        if (_virtual)
            csr = VirtualSupervisorCsr(csr);

        if (IsPerformanceCounter(csr))
            return csr < 0xC03 || csr > 0xC1F || CheckCounterAccess(csr - 0xC00);

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
            case RVCsr.STimeCmp: value = _stimecmp; return SupervisorTimerEnabled;
            case RVCsr.HStatus: value = _hstatus; return true;
            case RVCsr.HEDeleg: value = _hedeleg; return true;
            case RVCsr.HIDeleg: value = _hideleg; return true;
            case RVCsr.HIe: value = _hie; return true;
            case RVCsr.HIp: value = (_mip & VirtualSupervisorInterruptMask) | _hvip; return true;
            case RVCsr.HVIp: value = _hvip; return true;
            case RVCsr.HGEIe: value = _hgeie; return true;
            case RVCsr.HGEIp: value = 0; return true;
            case RVCsr.HCounterEn: value = _hcounteren; return true;
            case RVCsr.HEnvCfg: value = _henvcfg; return true;
            case RVCsr.HGEAtp: value = _hgatp; return true;
            case RVCsr.HTVal: value = _htval; return true;
            case RVCsr.HTInst: value = _htinst; return true;
            case RVCsr.HTimeDelta: value = _htimedelta; return true;
            case RVCsr.VsStatus: value = _vsstatus & SstatusMask; return true;
            case RVCsr.VsIe: value = (_hie & VirtualSupervisorInterruptMask) >> 1; return true;
            case RVCsr.VsIp: value = ((_mip | _hvip) & VirtualSupervisorInterruptMask) >> 1; return true;
            case RVCsr.VsTVec: value = _vstvec; return true;
            case RVCsr.VsScratch: value = _vsscratch; return true;
            case RVCsr.VsEpc: value = _vsepc; return true;
            case RVCsr.VsCause: value = _vscause; return true;
            case RVCsr.VsTVal: value = _vstval; return true;
            case RVCsr.VsAtp: value = _vsatp; return true;
            case RVCsr.SCountOvf: value = 0; return true;
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
        if (_virtual)
            csr = VirtualSupervisorCsr(csr);

        if (IsPerformanceCounter(csr))
            return true;

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
            case RVCsr.HStatus: _hstatus = value & HstatusMask; return true;
            case RVCsr.HEDeleg: _hedeleg = value; return true;
            case RVCsr.HIDeleg: _hideleg = value & VirtualSupervisorInterruptMask; return true;
            case RVCsr.HIe: _hie = value & VirtualSupervisorInterruptMask; return true;
            case RVCsr.HVIp: _hvip = value & VirtualSupervisorInterruptMask; return true;
            case RVCsr.HIp: _hvip = value & VirtualSupervisorInterruptMask; return true;
            case RVCsr.HGEIe: _hgeie = value; return true;
            case RVCsr.HCounterEn: _hcounteren = value; return true;
            case RVCsr.HEnvCfg: _henvcfg = value; return true;
            case RVCsr.HGEAtp: _hgatp = NormalizeHgatp(value); return true;
            case RVCsr.HTVal: _htval = value; return true;
            case RVCsr.HTInst: _htinst = value; return true;
            case RVCsr.HTimeDelta: _htimedelta = value; return true;
            case RVCsr.VsStatus: _vsstatus = value & SstatusMask; return true;
            case RVCsr.VsIe: _hie = (_hie & ~VirtualSupervisorInterruptMask) | ((value << 1) & VirtualSupervisorInterruptMask); return true;
            case RVCsr.VsIp: _hvip = (_hvip & ~(1UL << 2)) | ((value << 1) & (1UL << 2)); return true;
            case RVCsr.VsTVec: _vstvec = value; return true;
            case RVCsr.VsScratch: _vsscratch = value; return true;
            case RVCsr.VsEpc: _vsepc = value & ~1UL; return true;
            case RVCsr.VsCause: _vscause = value; return true;
            case RVCsr.VsTVal: _vstval = value; return true;
            case RVCsr.VsAtp: _vsatp = NormalizeSatp(value); return true;
            case RVCsr.STimeCmp:
                if (!SupervisorTimerEnabled)
                    return false;
                _stimecmp = value;
                return true;
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

    private static ulong NormalizeHgatp(ulong value)
    {
        ulong mode = value >> 60;
        if (mode == 0)
            return 0;
        return value & ((0xFUL << 60) | (0x3FFFUL << 44) | ((1UL << 44) - 1));
    }

    private static ulong NormalizeSatp(ulong value)
    {
        ulong mode = value >> 60;
        if (mode == 0)
            return 0;
        return value & ((0xFUL << 60) | (0xFFFFUL << 44) | ((1UL << 44) - 1));
    }


    /// <summary>A guest naming a supervisor register means its own shadow of it</summary>
    private static int VirtualSupervisorCsr(int csr) => csr switch
    {
        (int)RVCsr.SStatus => (int)RVCsr.VsStatus,
        (int)RVCsr.SIe => (int)RVCsr.VsIe,
        (int)RVCsr.STVec => (int)RVCsr.VsTVec,
        (int)RVCsr.SScratch => (int)RVCsr.VsScratch,
        (int)RVCsr.SEpc => (int)RVCsr.VsEpc,
        (int)RVCsr.SCause => (int)RVCsr.VsCause,
        (int)RVCsr.STVal => (int)RVCsr.VsTVal,
        (int)RVCsr.SIp => (int)RVCsr.VsIp,
        (int)RVCsr.SAtp => (int)RVCsr.VsAtp,
        _ => csr,
    };

    private bool CheckCsrAccess(int csr, bool write)
    {
        if (!IsImplementedCsr(csr))
            return false;
        // A guest may not reach the registers that describe it
        if (_virtual && ((csr >= 0x600 && csr <= 0x6FF) || (csr >= 0x200 && csr <= 0x2FF) || (csr >= 0xA00 && csr <= 0xAFF)))
            return false;
        int minimumPrivilege = (csr >> 8) & 3;
        // The hypervisor registers belong to a supervisor that is not itself a guest
        if (minimumPrivilege == 2)
            return _mode >= RVPrivilegeMode.Supervisor && !_virtual;
        if ((int)_mode < minimumPrivilege)
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

    /// <summary>Zihpm asks for the counters to exist; none of them count anything here</summary>
    private static bool IsPerformanceCounter(int csr)
        => (csr >= 0xC03 && csr <= 0xC1F) || (csr >= 0xB03 && csr <= 0xB1F) || (csr >= 0x323 && csr <= 0x33F);

    private static bool IsImplementedCsr(int csr)
    {
        if (IsPerformanceCounter(csr))
            return true;
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
            case RVCsr.STimeCmp:
            case RVCsr.SCountOvf:
            case RVCsr.HStatus:
            case RVCsr.HEDeleg:
            case RVCsr.HIDeleg:
            case RVCsr.HIe:
            case RVCsr.HIp:
            case RVCsr.HVIp:
            case RVCsr.HGEIe:
            case RVCsr.HGEIp:
            case RVCsr.HCounterEn:
            case RVCsr.HEnvCfg:
            case RVCsr.HGEAtp:
            case RVCsr.HTVal:
            case RVCsr.HTInst:
            case RVCsr.HTimeDelta:
            case RVCsr.VsStatus:
            case RVCsr.VsIe:
            case RVCsr.VsIp:
            case RVCsr.VsTVec:
            case RVCsr.VsScratch:
            case RVCsr.VsEpc:
            case RVCsr.VsCause:
            case RVCsr.VsTVal:
            case RVCsr.VsAtp:
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
        => (2UL << 62) | (1UL << ('i' - 'a')) | (1UL << ('m' - 'a')) | (1UL << ('a' - 'a')) | (1UL << ('c' - 'a')) | (1UL << ('f' - 'a')) | (1UL << ('d' - 'a')) | (1UL << ('h' - 'a')) | (1UL << ('s' - 'a')) | (1UL << ('u' - 'a')) | (1UL << ('v' - 'a'));

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

        if (_virtual)
            return TryTranslateGuest(virtualAddress, access, effectiveMode, false, out physicalAddress, out trapCause, out trapValue);

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

    /// <summary>Translates for a guest: the address crosses the VS tables and then the G tables</summary>
    private bool TryTranslateGuest(ulong virtualAddress, RVMemoryAccess access, RVPrivilegeMode guestMode, bool executableAsReadable,
        out ulong physicalAddress, out ulong trapCause, out ulong trapValue)
    {
        trapCause = 0;
        trapValue = virtualAddress;
        physicalAddress = virtualAddress;

        ulong guestPhysical;
        ulong mode = _vsatp >> 60;
        if (mode == 0)
        {
            guestPhysical = virtualAddress;
        }
        else
        {
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

            ulong table = (_vsatp & ((1UL << 44) - 1)) << 12;
            int level = levels - 1;
            for (; ; )
            {
                ulong vpn = (virtualAddress >> (12 + level * 9)) & 0x1FF;
                // A guest page table lives in guest memory, so reaching an entry needs the G stage too
                if (!TryTranslateGuestPhysical(table + vpn * 8, RVMemoryAccess.Load, out ulong pteAddress, out trapCause, out trapValue) ||
                    !TryReadPhysical(pteAddress, 8, RVMemoryAccess.Load, out ulong pte, out _, out _))
                {
                    if (trapCause == 0)
                        trapCause = PageFaultCause(access);
                    return false;
                }

                bool readable = (pte & PteR) != 0;
                bool writable = (pte & PteW) != 0;
                bool executable = (pte & PteX) != 0;
                if ((pte & PteV) == 0 || (!readable && writable))
                {
                    trapCause = PageFaultCause(access);
                    return false;
                }

                if (readable || executable)
                {
                    if (level > 0 && ((pte >> 10) & ((1UL << (level * 9)) - 1)) != 0)
                    {
                        trapCause = PageFaultCause(access);
                        return false;
                    }

                    bool userPage = (pte & PteU) != 0;
                    if (guestMode == RVPrivilegeMode.User && !userPage)
                    {
                        trapCause = PageFaultCause(access);
                        return false;
                    }
                    if (guestMode == RVPrivilegeMode.Supervisor && userPage &&
                        (access == RVMemoryAccess.Execute || (_vsstatus & MstatusSum) == 0))
                    {
                        trapCause = PageFaultCause(access);
                        return false;
                    }

                    bool allowed = access switch
                    {
                        RVMemoryAccess.Execute => executable,
                        RVMemoryAccess.Load => readable || (executableAsReadable && executable) || (((_vsstatus & MstatusMxr) != 0) && executable),
                        _ => writable,
                    };
                    if (!allowed)
                    {
                        trapCause = PageFaultCause(access);
                        return false;
                    }

                    ulong offsetMask = (1UL << (12 + level * 9)) - 1;
                    guestPhysical = (((pte >> 10) << 12) & ~offsetMask) | (virtualAddress & offsetMask);
                    break;
                }

                if (level == 0)
                {
                    trapCause = PageFaultCause(access);
                    return false;
                }

                table = (pte >> 10) << 12;
                level--;
            }
        }

        return TryTranslateGuestPhysical(guestPhysical, access, out physicalAddress, out trapCause, out trapValue);
    }

    /// <summary>The second stage, where a guest physical address becomes a real one</summary>
    private bool TryTranslateGuestPhysical(ulong guestPhysical, RVMemoryAccess access, out ulong physicalAddress, out ulong trapCause, out ulong trapValue)
    {
        trapCause = 0;
        trapValue = 0;
        physicalAddress = guestPhysical;

        ulong mode = _hgatp >> 60;
        if (mode == 0)
            return true;

        int levels;
        int gpaBits;
        if (mode == 8) { levels = 3; gpaBits = 41; }
        else if (mode == 9) { levels = 4; gpaBits = 50; }
        else
        {
            trapCause = GuestPageFaultCause(access);
            _htval = guestPhysical >> 2;
            return false;
        }

        if ((guestPhysical >> gpaBits) != 0)
        {
            trapCause = GuestPageFaultCause(access);
            _htval = guestPhysical >> 2;
            return false;
        }

        ulong table = (_hgatp & ((1UL << 44) - 1)) << 12;
        int level = levels - 1;
        for (; ; )
        {
            // The root of the G stage is four times as wide, which is what the x4 in Sv39x4 names
            int indexBits = level == levels - 1 ? 11 : 9;
            ulong index = (guestPhysical >> (12 + level * 9)) & ((1UL << indexBits) - 1);
            if (!TryReadPhysical(table + index * 8, 8, RVMemoryAccess.Load, out ulong pte, out _, out _))
            {
                trapCause = GuestPageFaultCause(access);
                _htval = guestPhysical >> 2;
                return false;
            }

            bool readable = (pte & PteR) != 0;
            bool writable = (pte & PteW) != 0;
            bool executable = (pte & PteX) != 0;
            if ((pte & PteV) == 0 || (!readable && writable))
            {
                trapCause = GuestPageFaultCause(access);
                _htval = guestPhysical >> 2;
                return false;
            }

            if (readable || executable)
            {
                bool allowed = access switch
                {
                    RVMemoryAccess.Execute => executable,
                    RVMemoryAccess.Load => readable,
                    _ => writable,
                };
                // Everything a guest reaches is user memory as far as this stage is concerned
                if (!allowed || (pte & PteU) == 0)
                {
                    trapCause = GuestPageFaultCause(access);
                    _htval = guestPhysical >> 2;
                    return false;
                }

                ulong offsetMask = (1UL << (12 + level * 9)) - 1;
                physicalAddress = (((pte >> 10) << 12) & ~offsetMask) | (guestPhysical & offsetMask);
                return true;
            }

            if (level == 0)
            {
                trapCause = GuestPageFaultCause(access);
                _htval = guestPhysical >> 2;
                return false;
            }

            table = (pte >> 10) << 12;
            level--;
        }
    }

    private static ulong GuestPageFaultCause(RVMemoryAccess access)
        => access == RVMemoryAccess.Execute
            ? (ulong)RVTrapCause.InstructionGuestPageFault
            : access == RVMemoryAccess.Load
                ? (ulong)RVTrapCause.LoadGuestPageFault
                : (ulong)RVTrapCause.StoreGuestPageFault;

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
                // The byte shifts promote to int, so the top byte lands on the sign bit: truncate
                // through uint before widening, otherwise an unsigned word load comes back sign-extended
                4 => (ulong)(uint)(_ram[(int)offset] | (_ram[(int)offset + 1] << 8) | (_ram[(int)offset + 2] << 16) | (_ram[(int)offset + 3] << 24)),
                _ => (ulong)_ram[(int)offset] | ((ulong)_ram[(int)offset + 1] << 8) | ((ulong)_ram[(int)offset + 2] << 16) | ((ulong)_ram[(int)offset + 3] << 24) | ((ulong)_ram[(int)offset + 4] << 32) | ((ulong)_ram[(int)offset + 5] << 40) | ((ulong)_ram[(int)offset + 6] << 48) | ((ulong)_ram[(int)offset + 7] << 56),
            };
            return true;
        }

        offset = address - _framebufferPixelBase;
        if (_framebufferPixels.Length != 0 && address >= _framebufferPixelBase && offset <= (ulong)(_framebufferPixels.Length - size))
        {
            var pixels = _framebufferPixels;
            value = size switch
            {
                1 => pixels[(int)offset],
                2 => (ulong)(pixels[(int)offset] | (pixels[(int)offset + 1] << 8)),
                4 => (ulong)(uint)(pixels[(int)offset] | (pixels[(int)offset + 1] << 8) | (pixels[(int)offset + 2] << 16) | (pixels[(int)offset + 3] << 24)),
                _ => (ulong)pixels[(int)offset] | ((ulong)pixels[(int)offset + 1] << 8) | ((ulong)pixels[(int)offset + 2] << 16) | ((ulong)pixels[(int)offset + 3] << 24) | ((ulong)pixels[(int)offset + 4] << 32) | ((ulong)pixels[(int)offset + 5] << 40) | ((ulong)pixels[(int)offset + 6] << 48) | ((ulong)pixels[(int)offset + 7] << 56),
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
        if (_hostBridge != null && _hostBridge.Contains(address))
        {
            value = _hostBridge.Read(address, size);
            return true;
        }
        if (_framebuffer != null && _framebuffer.ContainsRegister(address))
        {
            value = _framebuffer.Read(address, size);
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

        offset = address - _framebufferPixelBase;
        if (_framebufferPixels.Length != 0 && address >= _framebufferPixelBase && offset <= (ulong)(_framebufferPixels.Length - size))
        {
            var pixels = _framebufferPixels;
            pixels[(int)offset] = (byte)value;
            if (size >= 2)
                pixels[(int)offset + 1] = (byte)(value >> 8);
            if (size >= 4)
            {
                pixels[(int)offset + 2] = (byte)(value >> 16);
                pixels[(int)offset + 3] = (byte)(value >> 24);
            }
            if (size == 8)
            {
                pixels[(int)offset + 4] = (byte)(value >> 32);
                pixels[(int)offset + 5] = (byte)(value >> 40);
                pixels[(int)offset + 6] = (byte)(value >> 48);
                pixels[(int)offset + 7] = (byte)(value >> 56);
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
        if (_hostBridge != null && _hostBridge.Contains(address))
        {
            _hostBridge.Write(address, size, value);
            return true;
        }
        if (_framebuffer != null && _framebuffer.ContainsRegister(address))
        {
            _framebuffer.Write(address, size, value);
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
        => ExecuteVectorMemory(instruction, true, out trapCause, out trapValue);

    private bool ExecuteVectorStore(uint instruction, out ulong trapCause, out ulong trapValue)
        => ExecuteVectorMemory(instruction, false, out trapCause, out trapValue);

    /// <summary>Sorts a vector access into the shape it asks for and hands it on</summary>
    private bool ExecuteVectorMemory(uint instruction, bool load, out ulong trapCause, out ulong trapValue)
    {
        trapCause = (ulong)RVTrapCause.IllegalInstruction;
        trapValue = instruction;

        // An element wider than the register file holds is not one this machine offers
        if (((instruction >> 28) & 1) != 0)
            return false;

        int fields = (int)(instruction >> 29) + 1;
        uint addressing = (instruction >> 26) & 3;
        uint unitStride = (instruction >> 20) & 0x1F;

        // A whole register access reads no vtype at all
        if (addressing == 0 && unitStride == 0x08)
            return ExecuteVectorWholeRegister(instruction, fields, load, out trapCause, out trapValue);

        if ((_vtype & VectorTypeIllegal) != 0 || _vstart > _vl)
            return false;

        if (addressing == 0)
        {
            if (unitStride == 0x0B)
                return fields == 1 && ExecuteVectorMaskMemory(instruction, load, out trapCause, out trapValue);
            if (unitStride != 0 && (unitStride != 0x10 || !load))
                return false;
        }

        return ExecuteVectorElementMemory(instruction, load, fields, addressing, addressing == 0 && unitStride == 0x10,
            out trapCause, out trapValue);
    }

    /// <summary>Every element addressed access: unit stride, strided or indexed, one field or eight</summary>
    private bool ExecuteVectorElementMemory(uint instruction, bool load, int fields, uint addressing, bool faultOnlyFirst,
        out ulong trapCause, out ulong trapValue)
    {
        trapCause = (ulong)RVTrapCause.IllegalInstruction;
        trapValue = instruction;

        bool indexed = addressing == 1 || addressing == 3;
        int width = VectorMemoryElementBytes((int)((instruction >> 12) & 7));
        if (width == 0)
            return false;

        int elementBytes;
        int groupBytes;
        int groupRegisters;
        int indexBytes = 0;
        int indexRegisters = 0;
        if (indexed)
        {
            indexBytes = width;
            if (!TryGetVectorMemoryShape(indexBytes, out _, out indexRegisters))
                return false;
            elementBytes = _vsewBytes;
            groupBytes = _vgroupBytes;
            groupRegisters = _vgroupRegisters;
        }
        else
        {
            elementBytes = width;
            if (!TryGetVectorMemoryShape(elementBytes, out groupBytes, out groupRegisters))
                return false;
        }

        // A segment holds one group per field, and the whole run must fit the register file
        if (fields * groupRegisters > 8)
            return false;

        int register = (int)((instruction >> 7) & 31);
        if (!CheckVectorGroup(register, groupRegisters) || register + fields * groupRegisters > VectorRegisterCount)
            return false;

        int indexRegister = (int)((instruction >> 20) & 31);
        if (indexed && !CheckVectorGroup(indexRegister, indexRegisters))
            return false;

        ulong baseAddress = _x[(int)((instruction >> 15) & 31)];
        long stride = addressing == 2 ? (long)_x[indexRegister] : fields * elementBytes;
        bool unmasked = ((instruction >> 25) & 1) != 0;
        int vl = (int)_vl;
        int start = (int)_vstart;
        int elementVlmax = groupBytes / elementBytes;
        ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);

        // The plain case still goes through one memory copy
        if (fields == 1 && addressing == 0 && !faultOnlyFirst && unmasked && start == 0)
        {
            if (TryVectorLoadStore(baseAddress, register, elementBytes, vl * elementBytes, load, out bool trapped, out trapCause, out trapValue))
            {
                if (load)
                    FillVectorAgnosticTail(ref vectorBytes, register, elementBytes, vl, elementVlmax);
                return true;
            }
            if (trapped)
                return false;
        }

        for (int i = start; i < vl; i++)
        {
            if (!unmasked && !ReadVectorMaskBit(ref vectorBytes, i))
            {
                if (load && (_vtype & VectorTypeMaskAgnostic) != 0)
                {
                    for (int f = 0; f < fields; f++)
                        WriteVectorElement(ref vectorBytes, (register + f * groupRegisters) * VectorRegisterBytes + i * elementBytes,
                            elementBytes, ElementMask(elementBytes * 8));
                }
                continue;
            }

            ulong elementAddress = indexed
                ? baseAddress + ReadVectorElement(ref vectorBytes, indexRegister * VectorRegisterBytes + i * indexBytes, indexBytes)
                : baseAddress + (ulong)(stride * i);

            for (int f = 0; f < fields; f++)
            {
                int offset = (register + f * groupRegisters) * VectorRegisterBytes + i * elementBytes;
                if (TryVectorElementAccess(elementAddress + (ulong)(f * elementBytes), ref vectorBytes, offset, elementBytes, load, i,
                        out trapCause, out trapValue))
                {
                    continue;
                }

                // A fault only first load shortens vl instead of trapping, once the first element is through
                if (!faultOnlyFirst || i == 0)
                    return false;
                _vl = (ulong)i;
                _vstart = 0;
                _mstatus |= MstatusVsMask;
                return true;
            }
        }

        if (load)
        {
            for (int f = 0; f < fields; f++)
                FillVectorAgnosticTail(ref vectorBytes, register + f * groupRegisters, elementBytes, vl, elementVlmax);
        }

        _vstart = 0;
        _mstatus |= MstatusVsMask;
        return true;
    }

    /// <summary>A whole register access moves a fixed count of registers, whatever vtype says</summary>
    private bool ExecuteVectorWholeRegister(uint instruction, int fields, bool load, out ulong trapCause, out ulong trapValue)
    {
        trapCause = (ulong)RVTrapCause.IllegalInstruction;
        trapValue = instruction;

        if (fields is not (1 or 2 or 4 or 8) || ((instruction >> 25) & 1) == 0)
            return false;
        int width = (int)((instruction >> 12) & 7);
        if (load ? VectorMemoryElementBytes(width) == 0 : width != 0)
            return false;

        int register = (int)((instruction >> 7) & 31);
        if (!CheckVectorGroup(register, fields))
            return false;

        ulong address = _x[(int)((instruction >> 15) & 31)];
        if (!TryVectorLoadStore(address, register, 1, fields * VectorRegisterBytes, load, out _, out trapCause, out trapValue))
            return false;
        return true;
    }

    private bool ExecuteVectorMaskMemory(uint instruction, bool load, out ulong trapCause, out ulong trapValue)
    {
        trapCause = (ulong)RVTrapCause.IllegalInstruction;
        trapValue = instruction;

        if (((instruction >> 12) & 7) != 0 || ((instruction >> 25) & 1) == 0)
            return false;

        int register = (int)((instruction >> 7) & 31);
        ulong baseAddress = _x[(int)((instruction >> 15) & 31)];
        int bytes = (int)((_vl + 7) >> 3);
        int start = Math.Min((int)_vstart, bytes);
        ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);

        for (int i = start; i < bytes; i++)
        {
            ref byte element = ref Unsafe.Add(ref vectorBytes, register * VectorRegisterBytes + i);
            if (load)
            {
                if (!TryReadMemory(baseAddress + (ulong)i, 1, RVMemoryAccess.Load, out ulong value, out trapCause, out trapValue))
                {
                    _vstart = (ulong)i;
                    return false;
                }
                element = (byte)value;
            }
            else if (!TryWriteMemory(baseAddress + (ulong)i, 1, element, out trapCause, out trapValue))
            {
                _vstart = (ulong)i;
                return false;
            }
        }

        _vstart = 0;
        _mstatus |= MstatusVsMask;
        return true;
    }

    private bool TryVectorElementAccess(ulong address, ref byte vectorBytes, int offset, int elementBytes, bool load, int element,
        out ulong trapCause, out ulong trapValue)
    {
        if ((address & (ulong)(elementBytes - 1)) != 0)
        {
            _vstart = (ulong)element;
            trapCause = load ? (ulong)RVTrapCause.LoadAddressMisaligned : (ulong)RVTrapCause.StoreAddressMisaligned;
            trapValue = address;
            return false;
        }

        if (load)
        {
            if (!TryReadMemory(address, elementBytes, RVMemoryAccess.Load, out ulong value, out trapCause, out trapValue))
            {
                _vstart = (ulong)element;
                return false;
            }
            WriteVectorElement(ref vectorBytes, offset, elementBytes, value);
            return true;
        }

        if (!TryWriteMemory(address, elementBytes, ReadVectorElement(ref vectorBytes, offset, elementBytes), out trapCause, out trapValue))
        {
            _vstart = (ulong)element;
            return false;
        }
        return true;
    }

    private bool TryVectorLoadStore(ulong baseAddress, int register, int elementBytes, int byteCount, bool load, out bool trapped, out ulong trapCause, out ulong trapValue)
    {
        trapped = false;
        trapCause = 0;
        trapValue = 0;
        if (byteCount == 0)
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
    private Span<byte> GetVectorBytes() => MemoryMarshal.AsBytes((Span<ulong>)_v);

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
                case 23:
                    if (funct3 != 5) return false;
                    result = rightBits;
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
                case 23:
                    if (funct3 != 5) return false;
                    result = a;
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

    /// <summary>A tail element is agnostic under vta, and this machine leaves ones there</summary>
    private void FillVectorAgnosticTail(ref byte vectorBytes, int register, int elementBytes, int vl, int vlmax)
    {
        if ((_vtype & VectorTypeTailAgnostic) == 0)
            return;

        ulong ones = ElementMask(elementBytes * 8);
        int offset = register * VectorRegisterBytes + vl * elementBytes;
        for (int i = vl; i < vlmax; i++)
        {
            WriteVectorElement(ref vectorBytes, offset, elementBytes, ones);
            offset += elementBytes;
        }
    }

    private bool ExecuteVectorScalarMove(uint instruction, int funct3, int vs2, int sewBytes, int vlmax)
    {
        int rd = (int)((instruction >> 7) & 31);
        int rs1 = (int)((instruction >> 15) & 31);
        if (((instruction >> 25) & 1) == 0)
            return false;
        ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);

        if (funct3 == 2)
        {
            if (rs1 != 0)
                return false;
            ulong element = ReadVectorElement(ref vectorBytes, vs2 * VectorRegisterBytes, sewBytes);
            _x[rd] = (ulong)SignExtendElement(element, sewBytes * 8);
            _mstatus |= MstatusVsMask;
            return true;
        }

        if (vs2 != 0 || !CheckVectorGroup(rd, 1))
            return false;
        if (_vl != 0)
            WriteVectorElement(ref vectorBytes, rd * VectorRegisterBytes, sewBytes, _x[rs1] & ElementMask(sewBytes * 8));
        FillVectorAgnosticTail(ref vectorBytes, rd, sewBytes, 1, vlmax);
        _vstart = 0;
        _mstatus |= MstatusVsMask;
        return true;
    }

    private bool ExecuteVectorReduction(uint instruction, int funct6, int vs2, int vs1, int sewBytes, int groupRegisters)
    {
        int vd = (int)((instruction >> 7) & 31);
        if (!CheckVectorGroup(vs2, groupRegisters) || !CheckVectorGroup(vs1, 1) || !CheckVectorGroup(vd, 1))
            return false;

        bool unmasked = ((instruction >> 25) & 1) != 0;
        int sewBits = sewBytes * 8;
        ulong mask = ElementMask(sewBits);
        ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);
        ulong accumulator = ReadVectorElement(ref vectorBytes, vs1 * VectorRegisterBytes, sewBytes) & mask;

        for (int i = (int)_vstart; i < (int)_vl; i++)
        {
            if (!unmasked && !ReadVectorMaskBit(ref vectorBytes, i))
                continue;
            ulong element = ReadVectorElement(ref vectorBytes, vs2 * VectorRegisterBytes + i * sewBytes, sewBytes) & mask;
            accumulator = funct6 switch
            {
                0 => (accumulator + element) & mask,
                1 => accumulator & element,
                2 => accumulator | element,
                3 => accumulator ^ element,
                4 => Math.Min(accumulator, element),
                5 => (ulong)Math.Min(SignExtendElement(accumulator, sewBits), SignExtendElement(element, sewBits)) & mask,
                6 => Math.Max(accumulator, element),
                _ => (ulong)Math.Max(SignExtendElement(accumulator, sewBits), SignExtendElement(element, sewBits)) & mask,
            };
        }

        WriteVectorElement(ref vectorBytes, vd * VectorRegisterBytes, sewBytes, accumulator);
        FillVectorAgnosticTail(ref vectorBytes, vd, sewBytes, 1, VectorRegisterBytes / sewBytes);
        _vstart = 0;
        _mstatus |= MstatusVsMask;
        return true;
    }

    /// <summary>Whether a vector instruction needs a shape the element loop cannot walk</summary>
    private static bool IsVectorSpecialForm(int funct6, int funct3)
    {
        switch (funct3)
        {
            case 1:
                return funct6 == 0x01 || funct6 == 0x03 || funct6 == 0x05 || funct6 == 0x07
                    || funct6 == 0x10 || funct6 == 0x12 || funct6 == 0x13
                    || funct6 >= 0x28;
            case 2:
                return funct6 == 0x10 || funct6 == 0x12 || funct6 == 0x14 || funct6 == 0x17
                    || (funct6 >= 0x18 && funct6 <= 0x1F) || funct6 >= 0x30;
            case 0:
                return funct6 == 0x0E || (funct6 >= 0x10 && funct6 <= 0x13) || funct6 == 0x30 || funct6 == 0x31 || funct6 == 0x35;
            case 3:
                return funct6 == 0x0E || funct6 == 0x0F || funct6 == 0x27 || funct6 == 0x10 || funct6 == 0x11 || funct6 == 0x35;
            case 4:
                return funct6 == 0x0E || funct6 == 0x0F || (funct6 >= 0x10 && funct6 <= 0x13) || funct6 == 0x35;
            case 5:
                return funct6 == 0x0E || funct6 == 0x0F || funct6 == 0x10 || funct6 >= 0x28;
            case 6:
                return funct6 == 0x0E || funct6 == 0x0F || funct6 >= 0x30;
            default:
                return false;
        }
    }

    /// <summary>The mask, unary, slide, compress, whole register and widening forms</summary>
    private bool ExecuteVectorSpecial(uint instruction, int funct6, int funct3, int sewBytes, int groupBytes, int groupRegisters, int vlmax)
    {
        int vd = (int)((instruction >> 7) & 31);
        int vs1 = (int)((instruction >> 15) & 31);
        int vs2 = (int)((instruction >> 20) & 31);
        bool unmasked = ((instruction >> 25) & 1) != 0;
        int vl = (int)_vl;
        int start = (int)_vstart;
        int sewBits = sewBytes * 8;
        ulong mask = ElementMask(sewBits);
        ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);

        if (funct3 == 1 || funct3 == 5)
        {
            if (funct3 == 1 && funct6 == 0x12 && vs1 >= 8)
                return ExecuteVectorFloatConvertWidth(instruction, vs1, sewBytes, groupBytes, groupRegisters);
            if (sewBytes != 4 && sewBytes != 8)
                return false;
            if (funct3 == 1 && (funct6 == 0x31 || funct6 == 0x33))
                return ExecuteVectorFloatWideningReduction(instruction, funct6, vs2, vs1, sewBytes, groupRegisters);
            if (funct6 >= 0x30)
                return ExecuteVectorFloatWidening(instruction, funct6, funct3, sewBytes, groupBytes, groupRegisters);
            if (funct6 >= 0x28)
                return ExecuteVectorFloatMultiplyAdd(instruction, funct6, funct3, sewBytes, groupRegisters, vlmax);
            if (funct6 == 0x10)
                return ExecuteVectorFloatScalarMove(instruction, funct3, sewBytes, vlmax);
            if (funct3 == 1 && (funct6 == 0x12 || funct6 == 0x13))
                return ExecuteVectorFloatUnary(instruction, funct6, sewBytes, groupRegisters, vlmax);
            if (funct3 == 1)
                return ExecuteVectorFloatReduction(instruction, funct6, vs2, vs1, sewBytes, groupRegisters);
            return ExecuteVectorSlide(instruction, funct6, funct3, sewBytes, groupRegisters, vlmax);
        }

        if (funct3 == 0 && (funct6 == 0x30 || funct6 == 0x31))
            return ExecuteVectorWideningReduction(instruction, funct6, vs2, vs1, sewBytes, groupRegisters);

        if (funct3 == 0 && funct6 == 0x0E)
            return ExecuteVectorGatherHalfword(instruction, sewBytes, groupBytes, groupRegisters, vlmax);

        if (funct6 >= 0x30)
            return ExecuteVectorWidening(instruction, funct6, funct3, sewBytes, groupBytes, groupRegisters);

        if (funct3 == 3 && funct6 == 0x27)
        {
            // A whole register move ignores vtype and carries one, two, four or eight registers
            if (!unmasked)
                return false;
            int count = vs1 + 1;
            if (count != 1 && count != 2 && count != 4 && count != 8)
                return false;
            if (!CheckVectorGroup(vd, count) || !CheckVectorGroup(vs2, count))
                return false;
            Span<byte> registers = GetVectorBytes();
            registers.Slice(vs2 * VectorRegisterBytes, count * VectorRegisterBytes)
                .CopyTo(registers.Slice(vd * VectorRegisterBytes, count * VectorRegisterBytes));
            _vstart = 0;
            _mstatus |= MstatusVsMask;
            return true;
        }

        if (funct6 == 0x0E || funct6 == 0x0F)
            return ExecuteVectorSlide(instruction, funct6, funct3, sewBytes, groupRegisters, vlmax);

        if (funct3 != 2)
            return ExecuteVectorCarry(instruction, funct6, funct3, sewBytes, groupRegisters, vlmax);

        if (funct6 >= 0x18)
        {
            // The mask logic works a bit at a time over one register
            if (!unmasked)
                return false;
            for (int i = start; i < vl; i++)
            {
                bool left = ReadVectorMaskBitOf(ref vectorBytes, vs2, i);
                bool right = ReadVectorMaskBitOf(ref vectorBytes, vs1, i);
                bool value = funct6 switch
                {
                    0x18 => left && !right,
                    0x19 => left && right,
                    0x1A => left || right,
                    0x1B => left ^ right,
                    0x1C => left || !right,
                    0x1D => !(left && right),
                    0x1E => !(left || right),
                    _ => !(left ^ right),
                };
                WriteVectorMaskBit(ref vectorBytes, vd, i, value);
            }
            _vstart = 0;
            _mstatus |= MstatusVsMask;
            return true;
        }

        if (funct6 == 0x17)
        {
            // vcompress packs the elements its mask names into the low end of the destination
            if (!unmasked || start != 0)
                return false;
            if (!CheckVectorGroup(vd, groupRegisters) || !CheckVectorGroup(vs2, groupRegisters))
                return false;
            int packed = 0;
            for (int i = 0; i < vl; i++)
            {
                if (!ReadVectorMaskBitOf(ref vectorBytes, vs1, i))
                    continue;
                ulong element = ReadVectorElement(ref vectorBytes, vs2 * VectorRegisterBytes + i * sewBytes, sewBytes);
                WriteVectorElement(ref vectorBytes, vd * VectorRegisterBytes + packed * sewBytes, sewBytes, element);
                packed++;
            }
            FillVectorAgnosticTail(ref vectorBytes, vd, sewBytes, packed, vlmax);
            _vstart = 0;
            _mstatus |= MstatusVsMask;
            return true;
        }

        if (funct6 == 0x10)
        {
            if (vs1 < 0x10)
                return ExecuteVectorScalarMove(instruction, funct3, vs2, sewBytes, vlmax);
            if (vs1 > 0x11)
                return false;

            // vcpop counts the mask bits it finds and vfirst names the lowest
            int rd = vd;
            long count = 0;
            long first = -1;
            for (int i = start; i < vl; i++)
            {
                if (!unmasked && !ReadVectorMaskBit(ref vectorBytes, i))
                    continue;
                if (!ReadVectorMaskBitOf(ref vectorBytes, vs2, i))
                    continue;
                count++;
                if (first < 0)
                    first = i;
            }
            _x[rd] = vs1 == 0x10 ? (ulong)count : (ulong)first;
            if (rd == 0)
                _x[0] = 0;
            _vstart = 0;
            _mstatus |= MstatusVsMask;
            return true;
        }

        if (funct6 == 0x14)
            return ExecuteVectorMaskUnary(instruction, vs1, vd, vs2, unmasked, sewBytes, groupRegisters, vlmax);

        return ExecuteVectorExtendUnary(instruction, vs1, vd, vs2, unmasked, sewBytes, groupBytes, groupRegisters, vlmax);
    }

    /// <summary>The carry and borrow forms, which read the mask register as a carry in</summary>
    private bool ExecuteVectorCarry(uint instruction, int funct6, int funct3, int sewBytes, int groupRegisters, int vlmax)
    {
        int vd = (int)((instruction >> 7) & 31);
        int source1 = (int)((instruction >> 15) & 31);
        int vs2 = (int)((instruction >> 20) & 31);
        bool withoutCarry = ((instruction >> 25) & 1) != 0;
        bool maskResult = (funct6 & 1) != 0;
        bool subtract = funct6 >= 0x12;
        if (!maskResult && withoutCarry)
            return false;
        if (subtract && funct3 == 3)
            return false;
        if (!CheckVectorGroup(vs2, groupRegisters) || !CheckVectorGroup(vd, maskResult ? 1 : groupRegisters))
            return false;
        if (funct3 == 0 && !CheckVectorGroup(source1, groupRegisters))
            return false;

        int vl = (int)_vl;
        int start = (int)_vstart;
        int sewBits = sewBytes * 8;
        ulong mask = ElementMask(sewBits);
        ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);
        ulong scalar = funct3 == 4
            ? _x[source1] & mask
            : (ulong)(long)((source1 & 16) != 0 ? source1 - 32 : source1) & mask;

        int destinationOffset = vd * VectorRegisterBytes + start * sewBytes;
        int source1Offset = source1 * VectorRegisterBytes + start * sewBytes;
        int source2Offset = vs2 * VectorRegisterBytes + start * sewBytes;
        for (int i = start; i < vl; i++, destinationOffset += sewBytes, source1Offset += sewBytes, source2Offset += sewBytes)
        {
            ulong a = funct3 == 0 ? ReadVectorElement(ref vectorBytes, source1Offset, sewBytes) & mask : scalar;
            ulong b = ReadVectorElement(ref vectorBytes, source2Offset, sewBytes) & mask;
            ulong carry = !withoutCarry && ReadVectorMaskBit(ref vectorBytes, i) ? 1UL : 0UL;

            if (subtract)
            {
                if (maskResult)
                    WriteVectorMaskBit(ref vectorBytes, vd, i, b < a || (b == a && carry != 0));
                else
                    WriteVectorElement(ref vectorBytes, destinationOffset, sewBytes, (b - a - carry) & mask);
                continue;
            }

            ulong sum;
            bool carried;
            if (sewBits == 64)
            {
                sum = b + a;
                carried = sum < b;
                sum += carry;
                carried |= carry != 0 && sum == 0;
            }
            else
            {
                sum = b + a + carry;
                carried = sum > mask;
            }

            if (maskResult)
                WriteVectorMaskBit(ref vectorBytes, vd, i, carried);
            else
                WriteVectorElement(ref vectorBytes, destinationOffset, sewBytes, sum & mask);
        }

        if (!maskResult)
            FillVectorAgnosticTail(ref vectorBytes, vd, sewBytes, vl, vlmax);
        _vstart = 0;
        _mstatus |= MstatusVsMask;
        return true;
    }

    /// <summary>The set-before, set-only, set-including, iota and identity forms</summary>
    private bool ExecuteVectorMaskUnary(uint instruction, int vs1, int vd, int vs2, bool unmasked, int sewBytes, int groupRegisters, int vlmax)
    {
        int vl = (int)_vl;
        int start = (int)_vstart;
        ulong mask = ElementMask(sewBytes * 8);
        ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);

        if (vs1 == 0x11)
        {
            // vid.v hands each element its own index
            if (vs2 != 0 || !CheckVectorGroup(vd, groupRegisters))
                return false;
            int offset = vd * VectorRegisterBytes + start * sewBytes;
            for (int i = start; i < vl; i++, offset += sewBytes)
            {
                if (!unmasked && !ReadVectorMaskBit(ref vectorBytes, i))
                {
                    if ((_vtype & VectorTypeMaskAgnostic) != 0)
                        WriteVectorElement(ref vectorBytes, offset, sewBytes, mask);
                    continue;
                }
                WriteVectorElement(ref vectorBytes, offset, sewBytes, (ulong)i & mask);
            }
            FillVectorAgnosticTail(ref vectorBytes, vd, sewBytes, vl, vlmax);
            _vstart = 0;
            _mstatus |= MstatusVsMask;
            return true;
        }

        if (vs1 == 0x10)
        {
            // viota.m gives each element the count of mask bits below it
            if (start != 0 || !CheckVectorGroup(vd, groupRegisters))
                return false;
            ulong running = 0;
            int offset = vd * VectorRegisterBytes;
            for (int i = 0; i < vl; i++, offset += sewBytes)
            {
                bool active = unmasked || ReadVectorMaskBit(ref vectorBytes, i);
                if (!active)
                {
                    if ((_vtype & VectorTypeMaskAgnostic) != 0)
                        WriteVectorElement(ref vectorBytes, offset, sewBytes, mask);
                    continue;
                }
                WriteVectorElement(ref vectorBytes, offset, sewBytes, running & mask);
                if (ReadVectorMaskBitOf(ref vectorBytes, vs2, i))
                    running++;
            }
            FillVectorAgnosticTail(ref vectorBytes, vd, sewBytes, vl, vlmax);
            _vstart = 0;
            _mstatus |= MstatusVsMask;
            return true;
        }

        if (vs1 < 1 || vs1 > 3 || start != 0)
            return false;

        bool seen = false;
        for (int i = 0; i < vl; i++)
        {
            if (!unmasked && !ReadVectorMaskBit(ref vectorBytes, i))
                continue;
            bool set = ReadVectorMaskBitOf(ref vectorBytes, vs2, i);
            bool value = vs1 switch
            {
                1 => !seen && !set,
                2 => !seen && set,
                _ => !seen,
            };
            if (set)
                seen = true;
            WriteVectorMaskBit(ref vectorBytes, vd, i, value);
        }
        _vstart = 0;
        _mstatus |= MstatusVsMask;
        return true;
    }

    /// <summary>The widening extensions and the bit manipulation the vector profile adds</summary>
    private bool ExecuteVectorExtendUnary(uint instruction, int vs1, int vd, int vs2, bool unmasked, int sewBytes, int groupBytes, int groupRegisters, int vlmax)
    {
        int vl = (int)_vl;
        int start = (int)_vstart;
        int sewBits = sewBytes * 8;
        ulong mask = ElementMask(sewBits);
        ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);

        int sourceBytes = sewBytes;
        int sourceRegisters = groupRegisters;
        bool extending = vs1 >= 2 && vs1 <= 7;
        bool signedExtend = false;
        if (extending)
        {
            int factor = 1 << (4 - (vs1 >> 1));
            signedExtend = (vs1 & 1) != 0;
            if (sewBytes % factor != 0)
                return false;
            sourceBytes = sewBytes / factor;
            if (!TryGetVectorGroupRegisters(Math.Max(1, groupBytes / factor), out sourceRegisters))
                return false;
        }
        else if (vs1 != 8 && vs1 != 9 && vs1 != 0xA && vs1 != 0xC && vs1 != 0xD && vs1 != 0xE)
        {
            return false;
        }

        if (!CheckVectorGroup(vd, groupRegisters) || !CheckVectorGroup(vs2, sourceRegisters))
            return false;

        int sourceBits = sourceBytes * 8;
        int destinationOffset = vd * VectorRegisterBytes + start * sewBytes;
        int sourceOffset = vs2 * VectorRegisterBytes + start * sourceBytes;
        for (int i = start; i < vl; i++, destinationOffset += sewBytes, sourceOffset += sourceBytes)
        {
            if (!unmasked && !ReadVectorMaskBit(ref vectorBytes, i))
            {
                if ((_vtype & VectorTypeMaskAgnostic) != 0)
                    WriteVectorElement(ref vectorBytes, destinationOffset, sewBytes, mask);
                continue;
            }
            ulong element = ReadVectorElement(ref vectorBytes, sourceOffset, sourceBytes);
            ulong result = vs1 switch
            {
                8 => ReverseBits(element),
                9 => BinaryPrimitives.ReverseEndianness(element) >> (64 - sewBits),
                0xA => BinaryPrimitives.ReverseEndianness(ReverseBits(element)) >> (64 - sewBits),
                0xC => element == 0 ? (ulong)sewBits : (ulong)(BitOperations.LeadingZeroCount(element) - (64 - sewBits)),
                0xD => element == 0 ? (ulong)sewBits : (ulong)BitOperations.TrailingZeroCount(element),
                0xE => (ulong)BitOperations.PopCount(element),
                _ => signedExtend ? (ulong)SignExtendElement(element, sourceBits) : element,
            };
            WriteVectorElement(ref vectorBytes, destinationOffset, sewBytes, result & mask);
        }

        FillVectorAgnosticTail(ref vectorBytes, vd, sewBytes, vl, vlmax);
        _vstart = 0;
        _mstatus |= MstatusVsMask;
        return true;
    }

    private static ulong ReverseBits(ulong value)
    {
        value = ((value & 0x5555555555555555UL) << 1) | ((value >> 1) & 0x5555555555555555UL);
        value = ((value & 0x3333333333333333UL) << 2) | ((value >> 2) & 0x3333333333333333UL);
        value = ((value & 0x0F0F0F0F0F0F0F0FUL) << 4) | ((value >> 4) & 0x0F0F0F0F0F0F0F0FUL);
        return value;
    }

    /// <summary>The slide family, which moves elements past one another by a whole offset</summary>
    private bool ExecuteVectorSlide(uint instruction, int funct6, int funct3, int sewBytes, int groupRegisters, int vlmax)
    {
        int vd = (int)((instruction >> 7) & 31);
        int source1 = (int)((instruction >> 15) & 31);
        int vs2 = (int)((instruction >> 20) & 31);
        bool unmasked = ((instruction >> 25) & 1) != 0;
        bool down = funct6 == 0x0F;
        bool byOne = funct3 == 5 || funct3 == 6;
        if (funct3 != 3 && funct3 != 4 && !byOne)
            return false;
        if (!CheckVectorGroup(vd, groupRegisters) || !CheckVectorGroup(vs2, groupRegisters))
            return false;

        int vl = (int)_vl;
        int start = (int)_vstart;
        int sewBits = sewBytes * 8;
        ulong mask = ElementMask(sewBits);
        ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);

        ulong offset = byOne ? 1UL : funct3 == 3 ? (ulong)source1 : _x[source1];
        ulong inserted = byOne ? (funct3 == 5 ? _f[source1] : _x[source1]) & mask : 0;
        int first = !down && !byOne && offset < (ulong)vl ? Math.Max(start, (int)offset) : start;
        if (!down && !byOne && offset >= (ulong)vl)
            first = vl;

        int destinationOffset = vd * VectorRegisterBytes + first * sewBytes;
        for (int i = first; i < vl; i++, destinationOffset += sewBytes)
        {
            if (!unmasked && !ReadVectorMaskBit(ref vectorBytes, i))
            {
                if ((_vtype & VectorTypeMaskAgnostic) != 0)
                    WriteVectorElement(ref vectorBytes, destinationOffset, sewBytes, mask);
                continue;
            }

            ulong element;
            if (down)
            {
                ulong source = (ulong)i + offset;
                element = byOne
                    ? (i + 1 < vl ? ReadVectorElement(ref vectorBytes, vs2 * VectorRegisterBytes + (i + 1) * sewBytes, sewBytes) : inserted)
                    : source < (ulong)vlmax ? ReadVectorElement(ref vectorBytes, vs2 * VectorRegisterBytes + (int)source * sewBytes, sewBytes) : 0;
            }
            else
            {
                element = byOne && i == 0
                    ? inserted
                    : ReadVectorElement(ref vectorBytes, vs2 * VectorRegisterBytes + (i - (int)offset) * sewBytes, sewBytes);
            }
            WriteVectorElement(ref vectorBytes, destinationOffset, sewBytes, element & mask);
        }

        FillVectorAgnosticTail(ref vectorBytes, vd, sewBytes, vl, vlmax);
        _vstart = 0;
        _mstatus |= MstatusVsMask;
        return true;
    }

    /// <summary>The widening integer arithmetic, whose destination holds twice the element</summary>
    private bool ExecuteVectorWidening(uint instruction, int funct6, int funct3, int sewBytes, int groupBytes, int groupRegisters)
    {
        // The widening shift left is the one member of the group the integer forms name
        bool shifting = funct6 == 0x35 && (funct3 == 0 || funct3 == 3 || funct3 == 4);
        if (!shifting && funct3 != 2 && funct3 != 6)
            return false;
        if (sewBytes == 8 || !TryGetVectorGroupRegisters(groupBytes << 1, out int wideRegisters))
            return false;

        int vd = (int)((instruction >> 7) & 31);
        int source1 = (int)((instruction >> 15) & 31);
        int vs2 = (int)((instruction >> 20) & 31);
        bool unmasked = ((instruction >> 25) & 1) != 0;
        bool wideSource2 = funct6 >= 0x34 && funct6 <= 0x37;
        int sewBits = sewBytes * 8;
        int wideBits = sewBits << 1;
        int wideBytes = sewBytes << 1;
        ulong narrowMask = ElementMask(sewBits);
        ulong wideMask = ElementMask(wideBits);

        if (!CheckVectorGroup(vd, wideRegisters) || !CheckVectorGroup(vs2, wideSource2 && !shifting ? wideRegisters : groupRegisters))
            return false;
        if ((funct3 == 2 || (shifting && funct3 == 0)) && !CheckVectorGroup(source1, groupRegisters))
            return false;

        int vl = (int)_vl;
        int start = (int)_vstart;
        ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);
        ulong scalar = funct3 == 6 || (shifting && funct3 == 4)
            ? _x[source1] & narrowMask
            : shifting && funct3 == 3 ? (ulong)source1 : 0;
        int source2Bytes = wideSource2 && !shifting ? wideBytes : sewBytes;
        int destinationOffset = vd * VectorRegisterBytes + start * wideBytes;
        int source1Offset = source1 * VectorRegisterBytes + start * sewBytes;
        int source2Offset = vs2 * VectorRegisterBytes + start * source2Bytes;

        for (int i = start; i < vl; i++, destinationOffset += wideBytes, source1Offset += sewBytes, source2Offset += source2Bytes)
        {
            if (!unmasked && !ReadVectorMaskBit(ref vectorBytes, i))
            {
                if ((_vtype & VectorTypeMaskAgnostic) != 0)
                    WriteVectorElement(ref vectorBytes, destinationOffset, wideBytes, wideMask);
                continue;
            }

            ulong a = funct3 == 2 || (shifting && funct3 == 0)
                ? ReadVectorElement(ref vectorBytes, source1Offset, sewBytes) & narrowMask
                : scalar;
            ulong b = ReadVectorElement(ref vectorBytes, source2Offset, source2Bytes);
            if (shifting)
            {
                WriteVectorElement(ref vectorBytes, destinationOffset, wideBytes, (b << (int)(a & (ulong)(wideBits - 1))) & wideMask);
                continue;
            }
            long signedA = SignExtendElement(a, sewBits);
            long signedB = wideSource2 ? SignExtendElement(b, wideBits) : SignExtendElement(b, sewBits);
            ulong wide;
            switch (funct6)
            {
                case 0x30: wide = b + a; break;
                case 0x31: wide = (ulong)(signedB + signedA); break;
                case 0x32: wide = b - a; break;
                case 0x33: wide = (ulong)(signedB - signedA); break;
                case 0x34: wide = b + a; break;
                case 0x35: wide = (ulong)(signedB + signedA); break;
                case 0x36: wide = b - a; break;
                case 0x37: wide = (ulong)(signedB - signedA); break;
                case 0x38: wide = b * a; break;
                case 0x3A: wide = (ulong)(signedB * (long)a); break;
                case 0x3B: wide = (ulong)(signedB * signedA); break;
                case 0x3C: wide = ReadVectorElement(ref vectorBytes, destinationOffset, wideBytes) + a * b; break;
                case 0x3D: wide = ReadVectorElement(ref vectorBytes, destinationOffset, wideBytes) + (ulong)(signedA * signedB); break;
                case 0x3E:
                    if (funct3 != 6) return false;
                    wide = ReadVectorElement(ref vectorBytes, destinationOffset, wideBytes) + (ulong)(signedA * (long)b);
                    break;
                case 0x3F: wide = ReadVectorElement(ref vectorBytes, destinationOffset, wideBytes) + (ulong)((long)a * signedB); break;
                default: return false;
            }
            WriteVectorElement(ref vectorBytes, destinationOffset, wideBytes, wide & wideMask);
        }

        FillVectorAgnosticTail(ref vectorBytes, vd, wideBytes, vl, groupBytes * 2 / wideBytes);
        _vstart = 0;
        _mstatus |= MstatusVsMask;
        return true;
    }

    /// <summary>A reduction whose accumulator holds twice the element its group does</summary>
    private bool ExecuteVectorWideningReduction(uint instruction, int funct6, int vs2, int vs1, int sewBytes, int groupRegisters)
    {
        int vd = (int)((instruction >> 7) & 31);
        if (sewBytes == 8 || !CheckVectorGroup(vs2, groupRegisters) || !CheckVectorGroup(vs1, 1) || !CheckVectorGroup(vd, 1))
            return false;

        bool unmasked = ((instruction >> 25) & 1) != 0;
        int sewBits = sewBytes * 8;
        int wideBytes = sewBytes << 1;
        ulong wideMask = ElementMask(wideBytes * 8);
        ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);
        ulong accumulator = ReadVectorElement(ref vectorBytes, vs1 * VectorRegisterBytes, wideBytes) & wideMask;

        int vl = (int)_vl;
        for (int i = (int)_vstart; i < vl; i++)
        {
            if (!unmasked && !ReadVectorMaskBit(ref vectorBytes, i))
                continue;
            ulong element = ReadVectorElement(ref vectorBytes, vs2 * VectorRegisterBytes + i * sewBytes, sewBytes) & ElementMask(sewBits);
            accumulator = (accumulator + (funct6 == 0x30 ? element : (ulong)SignExtendElement(element, sewBits))) & wideMask;
        }

        WriteVectorElement(ref vectorBytes, vd * VectorRegisterBytes, wideBytes, accumulator);
        FillVectorAgnosticTail(ref vectorBytes, vd, wideBytes, 1, VectorRegisterBytes / wideBytes);
        _vstart = 0;
        _mstatus |= MstatusVsMask;
        return true;
    }

    /// <summary>A gather whose indices are sixteen bits wide whatever the element is</summary>
    private bool ExecuteVectorGatherHalfword(uint instruction, int sewBytes, int groupBytes, int groupRegisters, int vlmax)
    {
        if (!TryGetVectorMemoryShape(2, out _, out int indexRegisters))
            return false;

        int vd = (int)((instruction >> 7) & 31);
        int vs1 = (int)((instruction >> 15) & 31);
        int vs2 = (int)((instruction >> 20) & 31);
        if (!CheckVectorGroup(vd, groupRegisters) || !CheckVectorGroup(vs2, groupRegisters) || !CheckVectorGroup(vs1, indexRegisters))
            return false;

        bool unmasked = ((instruction >> 25) & 1) != 0;
        ulong mask = ElementMask(sewBytes * 8);
        ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);
        int vl = (int)_vl;
        int start = (int)_vstart;
        int destinationOffset = vd * VectorRegisterBytes + start * sewBytes;

        for (int i = start; i < vl; i++, destinationOffset += sewBytes)
        {
            if (!unmasked && !ReadVectorMaskBit(ref vectorBytes, i))
            {
                if ((_vtype & VectorTypeMaskAgnostic) != 0)
                    WriteVectorElement(ref vectorBytes, destinationOffset, sewBytes, mask);
                continue;
            }
            ulong index = ReadVectorElement(ref vectorBytes, vs1 * VectorRegisterBytes + i * 2, 2);
            WriteVectorElement(ref vectorBytes, destinationOffset, sewBytes,
                index < (ulong)vlmax ? ReadVectorElement(ref vectorBytes, vs2 * VectorRegisterBytes + (int)index * sewBytes, sewBytes) : 0);
        }

        FillVectorAgnosticTail(ref vectorBytes, vd, sewBytes, vl, vlmax);
        _vstart = 0;
        _mstatus |= MstatusVsMask;
        return true;
    }

    /// <summary>The fused multiply add family, which reads the destination as a third operand</summary>
    private bool ExecuteVectorFloatMultiplyAdd(uint instruction, int funct6, int funct3, int sewBytes, int groupRegisters, int vlmax)
    {
        int vd = (int)((instruction >> 7) & 31);
        int source1 = (int)((instruction >> 15) & 31);
        int vs2 = (int)((instruction >> 20) & 31);
        bool unmasked = ((instruction >> 25) & 1) != 0;
        if (!CheckVectorGroup(vd, groupRegisters) || !CheckVectorGroup(vs2, groupRegisters))
            return false;
        if (funct3 == 1 && !CheckVectorGroup(source1, groupRegisters))
            return false;

        int vl = (int)_vl;
        int start = (int)_vstart;
        ulong mask = ElementMask(sewBytes * 8);
        ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);
        ulong scalar = funct3 == 5 ? _f[source1] : 0;
        bool accumulating = funct6 >= 0x2C;
        bool negated = (funct6 & 1) != 0;
        bool subtracting = (funct6 & 2) != 0;

        int destinationOffset = vd * VectorRegisterBytes + start * sewBytes;
        int source1Offset = source1 * VectorRegisterBytes + start * sewBytes;
        int source2Offset = vs2 * VectorRegisterBytes + start * sewBytes;
        for (int i = start; i < vl; i++, destinationOffset += sewBytes, source1Offset += sewBytes, source2Offset += sewBytes)
        {
            if (!unmasked && !ReadVectorMaskBit(ref vectorBytes, i))
            {
                if ((_vtype & VectorTypeMaskAgnostic) != 0)
                    WriteVectorElement(ref vectorBytes, destinationOffset, sewBytes, mask);
                continue;
            }

            ulong a = funct3 == 1 ? ReadVectorElement(ref vectorBytes, source1Offset, sewBytes) : scalar;
            ulong b = ReadVectorElement(ref vectorBytes, source2Offset, sewBytes);
            ulong d = ReadVectorElement(ref vectorBytes, destinationOffset, sewBytes);
            ulong result;
            if (sewBytes == 4)
            {
                // The accumulating forms multiply the two sources, the rest multiply the destination
                float left = BitConverter.UInt32BitsToSingle((uint)a);
                float right = BitConverter.UInt32BitsToSingle((uint)(accumulating ? b : d));
                float addend = BitConverter.UInt32BitsToSingle((uint)(accumulating ? d : b));
                if (negated) { left = -left; addend = -addend; }
                if (subtracting) addend = -addend;
                result = BitConverter.SingleToUInt32Bits(MathF.FusedMultiplyAdd(left, right, addend));
            }
            else
            {
                double left = BitConverter.Int64BitsToDouble((long)a);
                double right = BitConverter.Int64BitsToDouble((long)(accumulating ? b : d));
                double addend = BitConverter.Int64BitsToDouble((long)(accumulating ? d : b));
                if (negated) { left = -left; addend = -addend; }
                if (subtracting) addend = -addend;
                result = (ulong)BitConverter.DoubleToInt64Bits(Math.FusedMultiplyAdd(left, right, addend));
            }
            WriteVectorElement(ref vectorBytes, destinationOffset, sewBytes, result);
        }

        FillVectorAgnosticTail(ref vectorBytes, vd, sewBytes, vl, vlmax);
        _vstart = 0;
        _mstatus |= MstatusVsMask;
        return true;
    }

    /// <summary>The seven bit significands the reciprocal estimate is defined by</summary>
    private static ReadOnlySpan<byte> ReciprocalSignificands => new byte[]
    {
        127, 125, 123, 121, 119, 117, 116, 114, 112, 110, 109, 107, 105, 104, 102, 100,
        99, 97, 96, 94, 93, 91, 90, 88, 87, 85, 84, 83, 81, 80, 79, 77,
        76, 75, 74, 72, 71, 70, 69, 68, 66, 65, 64, 63, 62, 61, 60, 59,
        58, 57, 56, 55, 54, 53, 52, 51, 50, 49, 48, 47, 46, 45, 44, 43,
        42, 41, 40, 40, 39, 38, 37, 36, 35, 35, 34, 33, 32, 31, 31, 30,
        29, 28, 28, 27, 26, 25, 25, 24, 23, 23, 22, 21, 21, 20, 19, 19,
        18, 17, 17, 16, 15, 15, 14, 14, 13, 12, 12, 11, 11, 10, 9, 9,
        8, 8, 7, 7, 6, 5, 5, 4, 4, 3, 3, 2, 2, 1, 1, 0,
    };

    /// <summary>The seven bit significands the reciprocal square root estimate is defined by</summary>
    private static ReadOnlySpan<byte> ReciprocalRootSignificands => new byte[]
    {
        52, 51, 50, 48, 47, 46, 44, 43, 42, 41, 40, 39, 38, 36, 35, 34,
        33, 32, 31, 30, 30, 29, 28, 27, 26, 25, 24, 23, 23, 22, 21, 20,
        19, 19, 18, 17, 16, 16, 15, 14, 14, 13, 12, 12, 11, 10, 10, 9,
        9, 8, 7, 7, 6, 6, 5, 4, 4, 3, 3, 2, 2, 1, 1, 0,
        127, 125, 123, 121, 119, 118, 116, 114, 113, 111, 109, 108, 106, 105, 103, 102,
        100, 99, 97, 96, 95, 93, 92, 91, 90, 88, 87, 86, 85, 84, 83, 82,
        80, 79, 78, 77, 76, 75, 74, 73, 72, 71, 70, 70, 69, 68, 67, 66,
        65, 64, 63, 63, 62, 61, 60, 59, 59, 58, 57, 56, 56, 55, 54, 53,
    };

    /// <summary>The reciprocal estimate, which reads its significand out of a table of a hundred and twenty eight</summary>
    private static ulong ReciprocalEstimate(ulong bits, int expBits, int fracBits, int rm)
    {
        ulong expMask = (1UL << expBits) - 1;
        ulong fracMask = (1UL << fracBits) - 1;
        int bias = (int)(expMask >> 1);
        ulong signBit = (bits >> (expBits + fracBits)) << (expBits + fracBits);
        int exponent = (int)((bits >> fracBits) & expMask);
        ulong significand = bits & fracMask;

        if (exponent == (int)expMask)
            return significand == 0 ? signBit : (expMask << fracBits) | (1UL << (fracBits - 1));
        if (exponent == 0 && significand == 0)
            return signBit | (expMask << fracBits);

        if (exponent == 0)
        {
            // A subnormal normalizes first, and one too small to reciprocate saturates
            if ((significand >> (fracBits - 2)) == 0)
            {
                bool towardZero = rm == 1 || (rm == 2 && signBit == 0) || (rm == 3 && signBit != 0);
                return towardZero
                    ? signBit | ((expMask - 1) << fracBits) | fracMask
                    : signBit | (expMask << fracBits);
            }
            int shift = (significand >> (fracBits - 1)) != 0 ? 1 : 2;
            exponent = 1 - shift;
            significand = (significand << shift) & fracMask;
        }

        int estimate = 2 * bias - 1 - exponent;
        ulong result = (ulong)ReciprocalSignificands[(int)(significand >> (fracBits - 7))] << (fracBits - 7);
        if (estimate <= 0)
        {
            // The estimate lands among the subnormals, where the hidden bit comes back out
            result = ((1UL << fracBits) | result) >> (1 - estimate);
            estimate = 0;
        }
        return signBit | ((ulong)estimate << fracBits) | (result & fracMask);
    }

    /// <summary>The reciprocal square root estimate, whose table the low exponent bit selects a half of</summary>
    private static ulong ReciprocalRootEstimate(ulong bits, int expBits, int fracBits)
    {
        ulong expMask = (1UL << expBits) - 1;
        ulong fracMask = (1UL << fracBits) - 1;
        int bias = (int)(expMask >> 1);
        ulong sign = bits >> (expBits + fracBits);
        int exponent = (int)((bits >> fracBits) & expMask);
        ulong significand = bits & fracMask;
        ulong quiet = (expMask << fracBits) | (1UL << (fracBits - 1));

        if (exponent == (int)expMask && significand != 0)
            return quiet;
        if (exponent == 0 && significand == 0)
            return (sign << (expBits + fracBits)) | (expMask << fracBits);
        if (sign != 0)
            return quiet;
        if (exponent == (int)expMask)
            return 0;

        if (exponent == 0)
        {
            int shift = 0;
            while ((significand & (1UL << (fracBits - 1))) == 0)
            {
                significand <<= 1;
                shift++;
            }
            significand = (significand << 1) & fracMask;
            exponent = -shift;
        }

        int index = (int)(((ulong)(exponent & 1) << 6) | (significand >> (fracBits - 6)));
        int estimate = (3 * bias - 1 - exponent) / 2;
        return ((ulong)estimate << fracBits) | ((ulong)ReciprocalRootSignificands[index] << (fracBits - 7));
    }

    /// <summary>The widening float arithmetic, whose destination holds twice the element</summary>
    private bool ExecuteVectorFloatWidening(uint instruction, int funct6, int funct3, int sewBytes, int groupBytes, int groupRegisters)
    {
        // Only a single reaches a double: this machine holds no half precision element
        if (sewBytes != 4 || !TryGetVectorGroupRegisters(groupBytes << 1, out int wideRegisters))
            return false;

        int vd = (int)((instruction >> 7) & 31);
        int source1 = (int)((instruction >> 15) & 31);
        int vs2 = (int)((instruction >> 20) & 31);
        bool unmasked = ((instruction >> 25) & 1) != 0;
        bool wideSource2 = funct6 == 0x34 || funct6 == 0x36;
        bool accumulating = funct6 >= 0x3C;

        if (funct6 is not (0x30 or 0x32 or 0x34 or 0x36 or 0x38 or 0x3C or 0x3D or 0x3E or 0x3F))
            return false;
        if (!CheckVectorGroup(vd, wideRegisters) || !CheckVectorGroup(vs2, wideSource2 ? wideRegisters : groupRegisters))
            return false;
        if (funct3 == 1 && !CheckVectorGroup(source1, groupRegisters))
            return false;

        int vl = (int)_vl;
        int start = (int)_vstart;
        ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);
        double scalar = funct3 == 5 ? BitConverter.UInt32BitsToSingle((uint)_f[source1]) : 0;
        int source2Bytes = wideSource2 ? 8 : 4;
        int destinationOffset = vd * VectorRegisterBytes + start * 8;
        int source1Offset = source1 * VectorRegisterBytes + start * 4;
        int source2Offset = vs2 * VectorRegisterBytes + start * source2Bytes;

        for (int i = start; i < vl; i++, destinationOffset += 8, source1Offset += 4, source2Offset += source2Bytes)
        {
            if (!unmasked && !ReadVectorMaskBit(ref vectorBytes, i))
            {
                if ((_vtype & VectorTypeMaskAgnostic) != 0)
                    WriteVectorElement(ref vectorBytes, destinationOffset, 8, ulong.MaxValue);
                continue;
            }

            double a = funct3 == 1
                ? BitConverter.UInt32BitsToSingle((uint)ReadVectorElement(ref vectorBytes, source1Offset, 4))
                : scalar;
            double b = wideSource2
                ? BitConverter.Int64BitsToDouble((long)ReadVectorElement(ref vectorBytes, source2Offset, 8))
                : BitConverter.UInt32BitsToSingle((uint)ReadVectorElement(ref vectorBytes, source2Offset, 4));
            double result;
            if (accumulating)
            {
                double addend = BitConverter.Int64BitsToDouble((long)ReadVectorElement(ref vectorBytes, destinationOffset, 8));
                bool negated = (funct6 & 1) != 0;
                bool subtracting = (funct6 & 2) != 0;
                if (negated) { a = -a; addend = -addend; }
                if (subtracting) addend = -addend;
                result = Math.FusedMultiplyAdd(a, b, addend);
            }
            else
            {
                result = funct6 switch
                {
                    0x30 or 0x34 => b + a,
                    0x32 or 0x36 => b - a,
                    _ => b * a,
                };
            }
            WriteVectorElement(ref vectorBytes, destinationOffset, 8, (ulong)BitConverter.DoubleToInt64Bits(result));
        }

        FillVectorAgnosticTail(ref vectorBytes, vd, 8, vl, groupBytes >> 2);
        _vstart = 0;
        _mstatus |= MstatusVsMask;
        return true;
    }

    /// <summary>A float reduction whose accumulator holds twice the element its group does</summary>
    private bool ExecuteVectorFloatWideningReduction(uint instruction, int funct6, int vs2, int vs1, int sewBytes, int groupRegisters)
    {
        int vd = (int)((instruction >> 7) & 31);
        if (sewBytes != 4 || !CheckVectorGroup(vs2, groupRegisters) || !CheckVectorGroup(vs1, 1) || !CheckVectorGroup(vd, 1))
            return false;

        bool unmasked = ((instruction >> 25) & 1) != 0;
        ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);
        double accumulator = BitConverter.Int64BitsToDouble((long)ReadVectorElement(ref vectorBytes, vs1 * VectorRegisterBytes, 8));

        int vl = (int)_vl;
        for (int i = (int)_vstart; i < vl; i++)
        {
            if (!unmasked && !ReadVectorMaskBit(ref vectorBytes, i))
                continue;
            accumulator += BitConverter.UInt32BitsToSingle((uint)ReadVectorElement(ref vectorBytes, vs2 * VectorRegisterBytes + i * 4, 4));
        }

        WriteVectorElement(ref vectorBytes, vd * VectorRegisterBytes, 8, (ulong)BitConverter.DoubleToInt64Bits(accumulator));
        FillVectorAgnosticTail(ref vectorBytes, vd, 8, 1, VectorRegisterBytes / 8);
        _vstart = 0;
        _mstatus |= MstatusVsMask;
        return true;
    }

    /// <summary>The conversions that change the element width as well as the kind</summary>
    private bool ExecuteVectorFloatConvertWidth(uint instruction, int select, int sewBytes, int groupBytes, int groupRegisters)
    {
        bool widening = select < 0x10;
        if (select > 0x17 || (widening && (select == 0x0D)) || (!widening && select > 0x17))
            return false;

        int sourceBytes = widening ? sewBytes : sewBytes << 1;
        int destinationBytes = widening ? sewBytes << 1 : sewBytes;
        if (sourceBytes > 8 || destinationBytes > 8)
            return false;

        int wideGroupBytes = widening ? groupBytes << 1 : groupBytes;
        if (!TryGetVectorGroupRegisters(groupBytes << 1, out int wideRegisters))
            return false;
        int sourceRegisters = widening ? groupRegisters : wideRegisters;
        int destinationRegisters = widening ? wideRegisters : groupRegisters;

        int operation = select & 7;
        bool floatSource = widening ? operation is 0 or 1 or 4 or 6 or 7 : operation is 0 or 1 or 4 or 5 or 6 or 7;
        bool floatResult = widening ? operation is 2 or 3 or 4 : operation is 2 or 3 or 4 or 5;
        if (floatSource && sourceBytes != 4 && sourceBytes != 8)
            return false;
        if (floatResult && destinationBytes != 4 && destinationBytes != 8)
            return false;
        if (!floatSource && sourceBytes < 2)
            return false;
        if (!floatResult && destinationBytes < 2)
            return false;

        int vd = (int)((instruction >> 7) & 31);
        int vs2 = (int)((instruction >> 20) & 31);
        bool unmasked = ((instruction >> 25) & 1) != 0;
        if (!CheckVectorGroup(vd, destinationRegisters) || !CheckVectorGroup(vs2, sourceRegisters))
            return false;

        int rm = (int)_frm;
        if (rm > 4)
            return false;
        // The truncating and odd rounding forms name the mode they want
        if (operation is 6 or 7)
            rm = 1;

        int vl = (int)_vl;
        int start = (int)_vstart;
        ulong destinationMask = ElementMask(destinationBytes * 8);
        ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);
        int destinationOffset = vd * VectorRegisterBytes + start * destinationBytes;
        int sourceOffset = vs2 * VectorRegisterBytes + start * sourceBytes;

        for (int i = start; i < vl; i++, destinationOffset += destinationBytes, sourceOffset += sourceBytes)
        {
            if (!unmasked && !ReadVectorMaskBit(ref vectorBytes, i))
            {
                if ((_vtype & VectorTypeMaskAgnostic) != 0)
                    WriteVectorElement(ref vectorBytes, destinationOffset, destinationBytes, destinationMask);
                continue;
            }

            ulong element = ReadVectorElement(ref vectorBytes, sourceOffset, sourceBytes);
            ulong result;
            if (floatSource && floatResult)
            {
                // The one rounding mode a plain cast cannot reach is the odd one the narrowing form offers
                result = widening
                    ? (ulong)BitConverter.DoubleToInt64Bits(BitConverter.UInt32BitsToSingle((uint)element))
                    : BitConverter.SingleToUInt32Bits(NarrowDouble(BitConverter.Int64BitsToDouble((long)element), !widening && (select & 7) == 5));
            }
            else if (floatSource)
            {
                double value = sourceBytes == 4
                    ? BitConverter.UInt32BitsToSingle((uint)element)
                    : BitConverter.Int64BitsToDouble((long)element);
                result = VectorFloatToInteger(value, (operation & 1) == 0, destinationBytes * 8, rm);
            }
            else
            {
                result = VectorIntegerToFloat(element, (operation & 1) != 0, destinationBytes * 8, rm, sourceBytes * 8);
            }
            WriteVectorElement(ref vectorBytes, destinationOffset, destinationBytes, result & destinationMask);
        }

        FillVectorAgnosticTail(ref vectorBytes, vd, destinationBytes, vl, wideGroupBytes / destinationBytes);
        _vstart = 0;
        _mstatus |= MstatusVsMask;
        return true;
    }

    /// <summary>Rounds a double onto a single, with the odd mode the narrowing conversion offers</summary>
    private static float NarrowDouble(double value, bool towardOdd)
    {
        float nearest = (float)value;
        if (!towardOdd || double.IsNaN(value) || (double)nearest == value)
            return nearest;

        // Round to odd keeps the low bit set unless the result was already exact
        uint bits = BitConverter.SingleToUInt32Bits(nearest);
        if ((bits & 1) != 0)
            return nearest;
        bool tooLarge = Math.Abs((double)nearest) > Math.Abs(value);
        return BitConverter.UInt32BitsToSingle(tooLarge ? bits - 1 : bits + 1);
    }

    /// <summary>The square root, class and conversion forms</summary>
    private bool ExecuteVectorFloatUnary(uint instruction, int funct6, int sewBytes, int groupRegisters, int vlmax)
    {
        int vd = (int)((instruction >> 7) & 31);
        int select = (int)((instruction >> 15) & 31);
        int vs2 = (int)((instruction >> 20) & 31);
        bool unmasked = ((instruction >> 25) & 1) != 0;
        if (!CheckVectorGroup(vd, groupRegisters) || !CheckVectorGroup(vs2, groupRegisters))
            return false;

        bool converting = funct6 == 0x12;
        if (converting)
        {
            if (select > 7 || select == 4 || select == 5)
                return false;
        }
        else if (select is not (0 or 4 or 5 or 0x10))
        {
            return false;
        }

        int vl = (int)_vl;
        int start = (int)_vstart;
        int sewBits = sewBytes * 8;
        ulong mask = ElementMask(sewBits);
        int rm = (int)_frm;
        if (rm > 4)
            return false;
        ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);

        int destinationOffset = vd * VectorRegisterBytes + start * sewBytes;
        int sourceOffset = vs2 * VectorRegisterBytes + start * sewBytes;
        for (int i = start; i < vl; i++, destinationOffset += sewBytes, sourceOffset += sewBytes)
        {
            if (!unmasked && !ReadVectorMaskBit(ref vectorBytes, i))
            {
                if ((_vtype & VectorTypeMaskAgnostic) != 0)
                    WriteVectorElement(ref vectorBytes, destinationOffset, sewBytes, mask);
                continue;
            }

            ulong element = ReadVectorElement(ref vectorBytes, sourceOffset, sewBytes);
            ulong result;
            if (!converting)
            {
                if (select == 0x10)
                    result = sewBytes == 4 ? ClassifyFloat32((uint)element) : ClassifyFloat64(element);
                else if (select == 4)
                    result = sewBytes == 4 ? ReciprocalRootEstimate(element, 8, 23) : ReciprocalRootEstimate(element, 11, 52);
                else if (select == 5)
                    result = sewBytes == 4 ? ReciprocalEstimate(element, 8, 23, rm) : ReciprocalEstimate(element, 11, 52, rm);
                else if (sewBytes == 4)
                    result = BitConverter.SingleToUInt32Bits(MathF.Sqrt(BitConverter.UInt32BitsToSingle((uint)element)));
                else
                    result = (ulong)BitConverter.DoubleToInt64Bits(Math.Sqrt(BitConverter.Int64BitsToDouble((long)element)));
            }
            else if (select <= 1 || select >= 6)
            {
                double value = sewBytes == 4 ? BitConverter.UInt32BitsToSingle((uint)element) : BitConverter.Int64BitsToDouble((long)element);
                result = VectorFloatToInteger(value, (select & 1) == 0, sewBits, select >= 6 ? 1 : rm);
            }
            else
            {
                result = VectorIntegerToFloat(element, select == 3, sewBits, rm);
            }
            WriteVectorElement(ref vectorBytes, destinationOffset, sewBytes, result);
        }

        FillVectorAgnosticTail(ref vectorBytes, vd, sewBytes, vl, vlmax);
        _vstart = 0;
        _mstatus |= MstatusVsMask;
        return true;
    }

    private static ulong VectorFloatToInteger(double value, bool unsignedResult, int sewBits, int rm)
    {
        ulong limit = ElementMask(sewBits);
        if (double.IsNaN(value))
            return unsignedResult ? limit : limit >> 1;

        double rounded = RoundToIntegral(value, rm);
        if (unsignedResult)
        {
            double ceiling = sewBits == 64 ? 18446744073709551615.0 : limit;
            return rounded <= 0.0 ? 0UL : rounded >= ceiling ? limit : (ulong)rounded;
        }

        double high = sewBits == 64 ? 9223372036854775807.0 : limit >> 1;
        double low = -high - 1.0;
        return rounded <= low ? (limit >> 1) + 1 : rounded >= high ? limit >> 1 : (ulong)(long)rounded & limit;
    }

    private static ulong VectorIntegerToFloat(ulong element, bool signedSource, int sewBits, int rm, int sourceBits = 0)
    {
        if (sourceBits != 0 && sourceBits < 64)
            element = signedSource ? (ulong)SignExtendElement(element, sourceBits) : element & ElementMask(sourceBits);
        if (sewBits == 32)
        {
            int narrow = (int)element;
            float nearest = signedSource ? narrow : (uint)element;
            if (rm != 0)
            {
                double residual = (signedSource ? narrow : (double)(uint)element) - nearest;
                nearest = RoundSingleDirected(nearest, residual, rm);
            }
            return BitConverter.SingleToUInt32Bits(nearest);
        }

        double wide = signedSource ? (long)element : element;
        if (rm != 0)
        {
            // The exact difference says which neighbour a directed mode wants
            Int128 exact = signedSource ? (Int128)(long)element : (Int128)element;
            wide = RoundDoubleDirected(wide, (double)(exact - (Int128)wide), rm);
        }
        return (ulong)BitConverter.DoubleToInt64Bits(wide);
    }

    /// <summary>The float scalar moves, which carry element zero to and from a float register</summary>
    private bool ExecuteVectorFloatScalarMove(uint instruction, int funct3, int sewBytes, int vlmax)
    {
        int rd = (int)((instruction >> 7) & 31);
        int rs1 = (int)((instruction >> 15) & 31);
        int vs2 = (int)((instruction >> 20) & 31);
        if (((instruction >> 25) & 1) == 0)
            return false;
        ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);

        if (funct3 == 1)
        {
            if (rs1 != 0)
                return false;
            ulong element = ReadVectorElement(ref vectorBytes, vs2 * VectorRegisterBytes, sewBytes);
            _f[rd] = sewBytes == 4 ? 0xFFFFFFFF00000000UL | element : element;
            _mstatus |= MstatusFsMask;
            _vstart = 0;
            return true;
        }

        if (vs2 != 0)
            return false;
        if (_vl != 0 && _vstart == 0)
            WriteVectorElement(ref vectorBytes, rd * VectorRegisterBytes, sewBytes, _f[rs1] & ElementMask(sewBytes * 8));
        FillVectorAgnosticTail(ref vectorBytes, rd, sewBytes, 1, vlmax);
        _vstart = 0;
        _mstatus |= MstatusVsMask;
        return true;
    }

    /// <summary>The float reductions, which fold a group down onto element zero</summary>
    private bool ExecuteVectorFloatReduction(uint instruction, int funct6, int vs2, int vs1, int sewBytes, int groupRegisters)
    {
        int vd = (int)((instruction >> 7) & 31);
        if (!CheckVectorGroup(vs2, groupRegisters) || !CheckVectorGroup(vs1, 1) || !CheckVectorGroup(vd, 1))
            return false;

        bool unmasked = ((instruction >> 25) & 1) != 0;
        ref byte vectorBytes = ref Unsafe.As<VectorRegisterArray, byte>(ref _v);
        ulong accumulator = ReadVectorElement(ref vectorBytes, vs1 * VectorRegisterBytes, sewBytes);
        int vl = (int)_vl;
        for (int i = (int)_vstart; i < vl; i++)
        {
            if (!unmasked && !ReadVectorMaskBit(ref vectorBytes, i))
                continue;
            ulong element = ReadVectorElement(ref vectorBytes, vs2 * VectorRegisterBytes + i * sewBytes, sewBytes);
            if (sewBytes == 4)
            {
                float left = BitConverter.UInt32BitsToSingle((uint)accumulator);
                float right = BitConverter.UInt32BitsToSingle((uint)element);
                accumulator = BitConverter.SingleToUInt32Bits(funct6 switch
                {
                    1 or 3 => left + right,
                    5 => MathF.Min(left, right),
                    _ => MathF.Max(left, right),
                });
            }
            else
            {
                double left = BitConverter.Int64BitsToDouble((long)accumulator);
                double right = BitConverter.Int64BitsToDouble((long)element);
                accumulator = (ulong)BitConverter.DoubleToInt64Bits(funct6 switch
                {
                    1 or 3 => left + right,
                    5 => Math.Min(left, right),
                    _ => Math.Max(left, right),
                });
            }
        }

        WriteVectorElement(ref vectorBytes, vd * VectorRegisterBytes, sewBytes, accumulator);
        FillVectorAgnosticTail(ref vectorBytes, vd, sewBytes, 1, VectorRegisterBytes / sewBytes);
        _vstart = 0;
        _mstatus |= MstatusVsMask;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ReadVectorMaskBitOf(ref byte vectorBytes, int register, int element)
        => (Unsafe.Add(ref vectorBytes, register * VectorRegisterBytes + (element >> 3)) & (1 << (element & 7))) != 0;

    private bool TryEvaluateVectorIntegerOperation(int funct6, int funct3, ulong a, ulong b, int sewBits, ulong mask, out ulong result)
    {
        a &= mask;
        b &= mask;
        result = 0;
        switch (funct6)
        {
            case 1:
                if (funct3 != 0 && funct3 != 4) return false;
                result = b & ~a;
                return true;
            case 8:
                if (funct3 != 2 && funct3 != 6) return false;
                result = VectorAverage(b, a, sewBits, false, false);
                return true;
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
                if (funct3 == 0 || funct3 == 4 || funct3 == 3) { result = b & a; return true; }
                if (funct3 != 2 && funct3 != 6) return false;
                result = VectorAverage(b, a, sewBits, true, false);
                return true;
            case 10:
                if (funct3 == 0 || funct3 == 4 || funct3 == 3) { result = b | a; return true; }
                if (funct3 != 2 && funct3 != 6) return false;
                result = VectorAverage(b, a, sewBits, false, true);
                return true;
            case 11:
                if (funct3 == 0 || funct3 == 4 || funct3 == 3) { result = b ^ a; return true; }
                if (funct3 != 2 && funct3 != 6) return false;
                result = VectorAverage(b, a, sewBits, true, true);
                return true;
            case 20:
                if (funct3 != 0 && funct3 != 4 && funct3 != 3) return false;
                result = VectorRotate(b, (int)(a & (ulong)(sewBits - 1)), sewBits, false);
                return true;
            case 21:
                if (funct3 == 3) { result = VectorRotate(b, (int)(a & (ulong)(sewBits - 1)), sewBits, false); return true; }
                if (funct3 != 0 && funct3 != 4) return false;
                result = VectorRotate(b, (int)(a & (ulong)(sewBits - 1)), sewBits, true);
                return true;
            case 23:
                if (funct3 != 0 && funct3 != 4 && funct3 != 3) return false;
                result = a;
                return true;
            case 32:
                if (funct3 == 0 || funct3 == 4 || funct3 == 3)
                {
                    ulong sum = b + a;
                    if (sum > mask || sum < b) { sum = mask; _vxsat = 1; }
                    result = sum;
                    return true;
                }
                if (funct3 != 2 && funct3 != 6) return false;
                result = a == 0 ? mask : b / a;
                return true;
            case 33:
                if (funct3 == 0 || funct3 == 4 || funct3 == 3)
                {
                    result = VectorSaturateAdd(SignExtendElement(b, sewBits), SignExtendElement(a, sewBits), sewBits, false);
                    return true;
                }
                if (funct3 != 2 && funct3 != 6) return false;
                result = SignedElementDiv(b, a, sewBits);
                return true;
            case 34:
                if (funct3 == 0 || funct3 == 4)
                {
                    if (b < a) { _vxsat = 1; result = 0; return true; }
                    result = b - a;
                    return true;
                }
                if (funct3 != 2 && funct3 != 6) return false;
                result = a == 0 ? b : b % a;
                return true;
            case 35:
                if (funct3 == 0 || funct3 == 4)
                {
                    result = VectorSaturateAdd(SignExtendElement(b, sewBits), SignExtendElement(a, sewBits), sewBits, true);
                    return true;
                }
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
                if (funct3 == 0 || funct3 == 4)
                {
                    Int128 product = (Int128)SignExtendElement(b, sewBits) * SignExtendElement(a, sewBits);
                    int narrow = sewBits - 1;
                    Int128 scaled = (product >> narrow) + VectorRoundingIncrement((ulong)product, narrow);
                    long ceiling = (long)(mask >> 1);
                    if (scaled > ceiling) { _vxsat = 1; scaled = ceiling; }
                    else if (scaled < -ceiling - 1) { _vxsat = 1; scaled = -ceiling - 1; }
                    result = (ulong)(long)scaled;
                    return true;
                }
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
            case 42:
                if (funct3 != 0 && funct3 != 4 && funct3 != 3) return false;
                {
                    int narrow = (int)(a & (ulong)(sewBits - 1));
                    result = (b >> narrow) + VectorRoundingIncrement(b, narrow);
                }
                return true;
            case 43:
                if (funct3 != 0 && funct3 != 4 && funct3 != 3) return false;
                {
                    int narrow = (int)(a & (ulong)(sewBits - 1));
                    long wide = SignExtendElement(b, sewBits);
                    result = (ulong)(wide >> narrow) + VectorRoundingIncrement((ulong)wide, narrow);
                }
                return true;
            default:
                return false;
        }
    }

    /// <summary>The increment a shift right takes on, which is what vxrm names</summary>
    private ulong VectorRoundingIncrement(ulong value, int shift)
    {
        if (shift == 0)
            return 0;
        ulong dropped = (value >> (shift - 1)) & 1;
        switch (_vxrm)
        {
            case 0:
                return dropped;
            case 1:
                return dropped & (((value >> shift) | (shift > 1 && (value & ((1UL << (shift - 1)) - 1)) != 0 ? 1UL : 0UL)) & 1);
            case 2:
                return 0;
            default:
                return ~(value >> shift) & ((value & ((1UL << shift) - 1)) != 0 ? 1UL : 0UL) & 1;
        }
    }

    /// <summary>The averaging forms, which keep the bit their sum needs and then round it away</summary>
    private ulong VectorAverage(ulong b, ulong a, int sewBits, bool signedOperands, bool subtract)
    {
        ulong half;
        ulong dropped;
        if (signedOperands)
        {
            long left = SignExtendElement(b, sewBits);
            long right = SignExtendElement(a, sewBits);
            half = subtract
                ? (ulong)((left >> 1) - (right >> 1) - (~left & right & 1))
                : (ulong)((left >> 1) + (right >> 1) + (left & right & 1));
            dropped = (ulong)(left ^ right) & 1;
        }
        else
        {
            half = subtract
                ? (b >> 1) - (a >> 1) - (~b & a & 1)
                : (b >> 1) + (a >> 1) + (b & a & 1);
            dropped = (b ^ a) & 1;
        }

        switch (_vxrm)
        {
            case 0:
                return half + dropped;
            case 1:
                return half + (dropped & half & 1);
            case 2:
                return half;
            default:
                return half + (~half & dropped & 1);
        }
    }

    /// <summary>The saturating signed add and subtract, which clamp instead of wrapping</summary>
    private ulong VectorSaturateAdd(long left, long right, int sewBits, bool subtract)
    {
        long sum = subtract ? unchecked(left - right) : unchecked(left + right);
        long ceiling = (long)(ElementMask(sewBits) >> 1);
        bool overflowed = subtract
            ? ((left ^ right) & (left ^ sum)) < 0
            : ((left ^ sum) & (right ^ sum)) < 0;
        if (overflowed)
        {
            _vxsat = 1;
            return (ulong)(left < 0 ? -ceiling - 1 : ceiling);
        }
        if (sum > ceiling) { _vxsat = 1; return (ulong)ceiling; }
        if (sum < -ceiling - 1) { _vxsat = 1; return (ulong)(-ceiling - 1); }
        return (ulong)sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong VectorRotate(ulong value, int amount, int sewBits, bool left)
    {
        if (amount == 0)
            return value;
        return left
            ? (value << amount) | (value >> (sewBits - amount))
            : (value >> amount) | (value << (sewBits - amount));
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
        if (funct3 > 3)
            return false;
        bool keepNaN = funct3 >= 2;
        bool wantMax = (funct3 & 1) != 0;
        if (doublePrecision)
        {
            double a = ReadFloat64(rs1);
            double b = ReadFloat64(rs2);
            double r;
            if (double.IsNaN(a) || double.IsNaN(b))
            {
                // fmin and fmax hand back the operand that is not NaN; only the Zfa forms keep it
                r = keepNaN || (double.IsNaN(a) && double.IsNaN(b)) ? double.NaN : double.IsNaN(a) ? b : a;
            }
            else
            {
                r = wantMax ? Math.Max(a, b) : Math.Min(a, b);
            }
            WriteFloat64(rd, r);
        }
        else
        {
            float a = ReadFloat32(rs1);
            float b = ReadFloat32(rs2);
            float r;
            if (float.IsNaN(a) || float.IsNaN(b))
            {
                r = keepNaN || (float.IsNaN(a) && float.IsNaN(b)) ? float.NaN : float.IsNaN(a) ? b : a;
            }
            else
            {
                r = wantMax ? MathF.Max(a, b) : MathF.Min(a, b);
            }
            WriteFloat32(rd, r);
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
                case 0: case 4: result = a <= b; break;
                case 1: case 5: result = a < b; break;
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
                case 0: case 4: result = a <= b; break;
                case 1: case 5: result = a < b; break;
                case 2: result = a == b; break;
                default: return false;
            }
        }
        if (rd != 0)
            _x[rd] = result ? 1UL : 0UL;
        return true;
    }

    /// <summary>Rewrites a compressed instruction as the wide one it stands for, or zero when there is none</summary>
    /// <remarks>
    /// Only the sixteen bit path reaches this, so a wide instruction never pays for the extension.
    /// </remarks>
    private static uint ExpandCompressed(ushort c)
    {
        uint rdFull = (uint)(c >> 7) & 31u;
        uint rs2Full = (uint)(c >> 2) & 31u;
        uint rdShort = 8u + ((uint)(c >> 2) & 7u);
        uint rs1Short = 8u + ((uint)(c >> 7) & 7u);
        uint funct3 = (uint)(c >> 13) & 7u;

        switch (c & 3)
        {
            case 0:
                switch (funct3)
                {
                    case 0:
                    {
                        uint nzuimm = (((uint)c >> 5) & 1u) << 3 | (((uint)c >> 6) & 1u) << 2
                            | (((uint)c >> 7) & 15u) << 6 | (((uint)c >> 11) & 3u) << 4;
                        return nzuimm == 0 ? 0u : nzuimm << 20 | 2u << 15 | rdShort << 7 | 0x13u;
                    }
                    case 1:
                        return ScaledOffset8(c) << 20 | rs1Short << 15 | 3u << 12 | rdShort << 7 | 0x07u;
                    case 2:
                        return ScaledOffset4(c) << 20 | rs1Short << 15 | 2u << 12 | rdShort << 7 | 0x03u;
                    case 3:
                        return ScaledOffset8(c) << 20 | rs1Short << 15 | 3u << 12 | rdShort << 7 | 0x03u;
                    case 4:
                    {
                        // Zcb: the narrow loads and stores
                        uint kind = ((uint)c >> 10) & 3u;
                        uint wide = (((uint)c >> 5) & 1u) << 1;
                        uint narrow = (((uint)c >> 6) & 1u) | wide;
                        if (kind == 0)
                            return narrow << 20 | rs1Short << 15 | 4u << 12 | rdShort << 7 | 0x03u;
                        if (kind == 1)
                            return (((uint)c >> 6) & 1u) != 0
                                ? wide << 20 | rs1Short << 15 | 1u << 12 | rdShort << 7 | 0x03u
                                : wide << 20 | rs1Short << 15 | 5u << 12 | rdShort << 7 | 0x03u;
                        if (kind == 2)
                            return Store(narrow, rdShort, rs1Short, 0u, 0x23u);
                        return Store(wide, rdShort, rs1Short, 1u, 0x23u);
                    }
                    case 5:
                        return Store(ScaledOffset8(c), rdShort, rs1Short, 3u, 0x27u);
                    case 6:
                        return Store(ScaledOffset4(c), rdShort, rs1Short, 2u, 0x23u);
                    default:
                        return Store(ScaledOffset8(c), rdShort, rs1Short, 3u, 0x23u);
                }

            case 1:
                switch (funct3)
                {
                    case 0:
                        return Immediate6(c) << 20 | rdFull << 15 | rdFull << 7 | 0x13u;
                    case 1:
                        return rdFull == 0 ? 0u : Immediate6(c) << 20 | rdFull << 15 | rdFull << 7 | 0x1Bu;
                    case 2:
                        return rdFull == 0 ? 0u : Immediate6(c) << 20 | rdFull << 7 | 0x13u;
                    case 3:
                    {
                        if (rdFull == 2)
                        {
                            int scaled = (((c >> 12) & 1) << 9) | (((c >> 3) & 3) << 7) | (((c >> 5) & 1) << 6)
                                | (((c >> 2) & 1) << 5) | (((c >> 6) & 1) << 4);
                            if (scaled == 0)
                                return 0u;
                            return ((uint)((scaled << 22) >> 22) & 0xFFFu) << 20 | 2u << 15 | 2u << 7 | 0x13u;
                        }
                        if (((c >> 2) & 31) == 0 && (rdFull & 1) != 0)
                            return 0x13u; // Zcmop, which changes nothing
                        if (rdFull == 0)
                            return 0u;
                        uint upper = (((uint)c >> 12) & 1u) << 5 | ((uint)c >> 2) & 31u;
                        if (upper == 0)
                            return 0u;
                        return ((upper ^ 32u) - 32u) << 12 | rdFull << 7 | 0x37u;
                    }
                    case 4:
                    {
                        uint group = ((uint)c >> 10) & 3u;
                        uint shamt = (((uint)c >> 12) & 1u) << 5 | ((uint)c >> 2) & 31u;
                        if (group == 0)
                            return shamt << 20 | rs1Short << 15 | 5u << 12 | rs1Short << 7 | 0x13u;
                        if (group == 1)
                            return 0x400u << 20 | shamt << 20 | rs1Short << 15 | 5u << 12 | rs1Short << 7 | 0x13u;
                        if (group == 2)
                            return Immediate6(c) << 20 | rs1Short << 15 | 7u << 12 | rs1Short << 7 | 0x13u;

                        uint pick = ((uint)c >> 5) & 3u;
                        if ((c & 0x1000) == 0)
                        {
                            uint funct7 = pick == 0 ? 0x20u : 0u;
                            uint operation = pick == 0 ? 0u : pick == 1 ? 4u : pick == 2 ? 6u : 7u;
                            return funct7 << 25 | rdShort << 20 | rs1Short << 15 | operation << 12 | rs1Short << 7 | 0x33u;
                        }
                        if (pick == 0)
                            return 0x20u << 25 | rdShort << 20 | rs1Short << 15 | rs1Short << 7 | 0x3Bu;
                        if (pick == 1)
                            return rdShort << 20 | rs1Short << 15 | rs1Short << 7 | 0x3Bu;
                        if (pick == 2)
                            return 1u << 25 | rdShort << 20 | rs1Short << 15 | rs1Short << 7 | 0x33u; // Zcb c.mul
                        switch (((uint)c >> 2) & 7u)
                        {
                            case 0: return 255u << 20 | rs1Short << 15 | 7u << 12 | rs1Short << 7 | 0x13u;
                            case 1: return 0x604u << 20 | rs1Short << 15 | 1u << 12 | rs1Short << 7 | 0x13u;
                            case 2: return 0x04u << 25 | rs1Short << 15 | 4u << 12 | rs1Short << 7 | 0x3Bu;
                            case 3: return 0x605u << 20 | rs1Short << 15 | 1u << 12 | rs1Short << 7 | 0x13u;
                            case 4: return 0x04u << 25 | rs1Short << 15 | rs1Short << 7 | 0x3Bu;
                            case 5: return 0xFFFu << 20 | rs1Short << 15 | 4u << 12 | rs1Short << 7 | 0x13u;
                            default: return 0u;
                        }
                    }
                    case 5:
                        return JumpImmediate(c) | 0x6Fu;
                    case 6:
                        return BranchImmediate(c) | rs1Short << 15 | 0x63u;
                    default:
                        return BranchImmediate(c) | rs1Short << 15 | 1u << 12 | 0x63u;
                }

            case 2:
                switch (funct3)
                {
                    case 0:
                    {
                        uint shamt = (((uint)c >> 12) & 1u) << 5 | ((uint)c >> 2) & 31u;
                        return rdFull == 0 ? 0u : shamt << 20 | rdFull << 15 | 1u << 12 | rdFull << 7 | 0x13u;
                    }
                    case 1:
                        return StackOffset8(c) << 20 | 2u << 15 | 3u << 12 | rdFull << 7 | 0x07u;
                    case 2:
                        return rdFull == 0 ? 0u : StackOffset4(c) << 20 | 2u << 15 | 2u << 12 | rdFull << 7 | 0x03u;
                    case 3:
                        return rdFull == 0 ? 0u : StackOffset8(c) << 20 | 2u << 15 | 3u << 12 | rdFull << 7 | 0x03u;
                    case 4:
                        if ((c & 0x1000) == 0)
                        {
                            if (rs2Full == 0)
                                return rdFull == 0 ? 0u : rdFull << 15 | 0x67u;
                            return rdFull == 0 ? 0u : rs2Full << 20 | rdFull << 7 | 0x33u;
                        }
                        if (rs2Full == 0)
                            return rdFull == 0 ? 0x00100073u : rdFull << 15 | 1u << 7 | 0x67u;
                        return rs2Full << 20 | rdFull << 15 | rdFull << 7 | 0x33u;
                    case 5:
                        return Store(StackStoreOffset8(c), rs2Full, 2u, 3u, 0x27u);
                    case 6:
                        return Store(StackStoreOffset4(c), rs2Full, 2u, 2u, 0x23u);
                    default:
                        return Store(StackStoreOffset8(c), rs2Full, 2u, 3u, 0x23u);
                }

            default:
                return 0u;
        }
    }

    private static uint Store(uint offset, uint rs2, uint rs1, uint funct3, uint opcode)
        => (offset >> 5) << 25 | rs2 << 20 | rs1 << 15 | funct3 << 12 | (offset & 31u) << 7 | opcode;

    private static uint ScaledOffset4(ushort c)
        => (((uint)c >> 10) & 7u) << 3 | (((uint)c >> 6) & 1u) << 2 | (((uint)c >> 5) & 1u) << 6;

    private static uint ScaledOffset8(ushort c)
        => (((uint)c >> 10) & 7u) << 3 | (((uint)c >> 5) & 3u) << 6;

    private static uint StackOffset4(ushort c)
        => (((uint)c >> 12) & 1u) << 5 | (((uint)c >> 4) & 7u) << 2 | (((uint)c >> 2) & 3u) << 6;

    private static uint StackOffset8(ushort c)
        => (((uint)c >> 12) & 1u) << 5 | (((uint)c >> 5) & 3u) << 3 | (((uint)c >> 2) & 7u) << 6;

    private static uint StackStoreOffset4(ushort c)
        => (((uint)c >> 9) & 15u) << 2 | (((uint)c >> 7) & 3u) << 6;

    private static uint StackStoreOffset8(ushort c)
        => (((uint)c >> 10) & 7u) << 3 | (((uint)c >> 7) & 7u) << 6;

    private static uint Immediate6(ushort c)
    {
        uint value = (((uint)c >> 12) & 1u) << 5 | ((uint)c >> 2) & 31u;
        return ((value ^ 32u) - 32u) & 0xFFFu;
    }

    private static uint JumpImmediate(ushort c)
    {
        int offset = (((c >> 3) & 7) << 1) | (((c >> 11) & 1) << 4) | (((c >> 2) & 1) << 5)
            | (((c >> 7) & 1) << 6) | (((c >> 6) & 1) << 7) | (((c >> 9) & 3) << 8)
            | (((c >> 8) & 1) << 10) | (((c >> 12) & 1) << 11);
        offset = (offset << 20) >> 20;
        uint bits = (uint)offset;
        return ((bits >> 20) & 1u) << 31 | ((bits >> 1) & 0x3FFu) << 21 | ((bits >> 11) & 1u) << 20 | ((bits >> 12) & 0xFFu) << 12;
    }

    private static uint BranchImmediate(ushort c)
    {
        int offset = (((c >> 3) & 3) << 1) | (((c >> 10) & 3) << 3) | (((c >> 2) & 1) << 5)
            | (((c >> 5) & 3) << 6) | (((c >> 12) & 1) << 8);
        offset = (offset << 23) >> 23;
        uint bits = (uint)offset;
        return ((bits >> 12) & 1u) << 31 | ((bits >> 5) & 0x3Fu) << 25 | ((bits >> 1) & 15u) << 8 | ((bits >> 11) & 1u) << 7;
    }

    private bool ExecuteFloatToInt(bool doublePrecision, int rs2, int rd, int rs1, int rm)
    {
        double value = doublePrecision ? ReadFloat64(rs1) : ReadFloat32(rs1);
        if (rs2 > 3)
            return false;

        bool wide = rs2 >= 2;
        bool unsignedResult = (rs2 & 1) != 0;
        ulong result;
        if (double.IsNaN(value))
        {
            // A conversion of NaN delivers the largest result the destination holds
            result = unsignedResult
                ? (wide ? ulong.MaxValue : SignExtend32(uint.MaxValue))
                : (wide ? (ulong)long.MaxValue : SignExtend32(int.MaxValue));
        }
        else
        {
            double rounded = RoundToIntegral(value, rm);
            if (unsignedResult)
            {
                double limit = wide ? 18446744073709551615.0 : 4294967295.0;
                result = rounded <= 0.0 ? 0UL
                    : rounded >= limit ? (wide ? ulong.MaxValue : SignExtend32(uint.MaxValue))
                    : wide ? (ulong)rounded : SignExtend32((uint)rounded);
            }
            else
            {
                double low = wide ? -9223372036854775808.0 : -2147483648.0;
                double high = wide ? 9223372036854775807.0 : 2147483647.0;
                result = rounded <= low ? (wide ? unchecked((ulong)long.MinValue) : SignExtend32(unchecked((uint)int.MinValue)))
                    : rounded >= high ? (wide ? (ulong)long.MaxValue : SignExtend32(int.MaxValue))
                    : wide ? (ulong)(long)rounded : SignExtend32(unchecked((uint)(int)rounded));
            }
        }

        if (rd != 0)
            _x[rd] = result;
        return true;
    }

    /// <summary>Rounds to an integral value the way the named mode does</summary>
    private static double RoundToIntegral(double value, int rm) => rm switch
    {
        0 => Math.Round(value, MidpointRounding.ToEven),
        1 => Math.Truncate(value),
        2 => Math.Floor(value),
        3 => Math.Ceiling(value),
        _ => Math.Round(value, MidpointRounding.AwayFromZero),
    };

    /// <summary>Nudges a nearest-even result onto the neighbour the named mode wants</summary>
    /// <remarks>Reached only when the mode is not nearest-even, which keeps the interpreter loop free of it</remarks>
    private static double RoundDoubleDirected(double nearest, double residual, int rm)
    {
        if (rm == 4)
        {
            // Nearest with ties away from zero differs only where the value sits exactly halfway
            double step = residual > 0.0 ? Math.BitIncrement(nearest) - nearest : Math.BitDecrement(nearest) - nearest;
            return residual + residual == step ? nearest + step : nearest;
        }

        if (rm == 2)
            return residual < 0.0 ? Math.BitDecrement(nearest) : nearest;
        if (rm == 3)
            return residual > 0.0 ? Math.BitIncrement(nearest) : nearest;
        if (nearest > 0.0)
            return residual < 0.0 ? Math.BitDecrement(nearest) : nearest;
        return residual > 0.0 ? Math.BitIncrement(nearest) : nearest;
    }

    private static float RoundSingleDirected(float nearest, double residual, int rm)
    {
        if (rm == 4)
        {
            double step = residual > 0.0 ? (double)MathF.BitIncrement(nearest) - nearest : (double)MathF.BitDecrement(nearest) - nearest;
            return residual + residual == step ? (float)(nearest + step) : nearest;
        }

        if (rm == 2)
            return residual < 0.0 ? MathF.BitDecrement(nearest) : nearest;
        if (rm == 3)
            return residual > 0.0 ? MathF.BitIncrement(nearest) : nearest;
        if (nearest > 0.0f)
            return residual < 0.0 ? MathF.BitDecrement(nearest) : nearest;
        return residual > 0.0 ? MathF.BitIncrement(nearest) : nearest;
    }

    /// <summary>The thirty-two constants fli loads, in the order Zfa numbers them</summary>
    private static double LoadFloatConstant(int index, bool doublePrecision) => index switch
    {
        0 => -1.0,
        1 => doublePrecision ? 2.2250738585072014E-308 : 1.17549435E-38,
        2 => 1.52587890625E-05,
        3 => 3.0517578125E-05,
        4 => 0.00390625,
        5 => 0.0078125,
        6 => 0.0625,
        7 => 0.125,
        8 => 0.25,
        9 => 0.3125,
        10 => 0.375,
        11 => 0.4375,
        12 => 0.5,
        13 => 0.625,
        14 => 0.75,
        15 => 0.875,
        16 => 1.0,
        17 => 1.25,
        18 => 1.5,
        19 => 1.75,
        20 => 2.0,
        21 => 2.5,
        22 => 3.0,
        23 => 4.0,
        24 => 8.0,
        25 => 16.0,
        26 => 128.0,
        27 => 256.0,
        28 => 32768.0,
        29 => 65536.0,
        30 => double.PositiveInfinity,
        _ => double.NaN,
    };

    private bool ExecuteIntToFloat(bool doublePrecision, int rs2, int rd, int rs1, int rm)
    {
        ulong raw = _x[rs1];
        if (doublePrecision)
        {
            if (rm != 0 && rs2 >= 2)
            {
                // Only the sixty-four bit sources can lose bits on the way into a double
                double exact = rs2 == 2 ? (long)raw : raw;
                double back = rs2 == 2 ? (double)(long)raw : raw;
                long reconstructed = rs2 == 2 ? (long)back : unchecked((long)(ulong)back);
                double residual = rs2 == 2 ? (long)raw - (double)reconstructed : (double)(raw - (ulong)reconstructed);
                if (residual != 0.0)
                    exact = RoundDoubleDirected(back, residual, rm);
                WriteFloat64(rd, exact);
                return true;
            }

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
            if (rm != 0)
            {
                double exact = rs2 switch
                {
                    0 => (int)raw,
                    1 => (uint)raw,
                    2 => (long)raw,
                    3 => raw,
                    _ => double.NaN,
                };
                if (double.IsNaN(exact) && rs2 > 3)
                    return false;
                float nearest = (float)exact;
                double residual = exact - nearest;
                if (residual != 0.0)
                    nearest = RoundSingleDirected(nearest, residual, rm);
                WriteFloat32(rd, nearest);
                return true;
            }

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
    private float ReadFloat16(int register)
    {
        ulong raw = _f[register];
        return (raw & 0xFFFFFFFFFFFF0000UL) == 0xFFFFFFFFFFFF0000UL
            ? (float)BitConverter.UInt16BitsToHalf((ushort)raw)
            : float.NaN;
    }
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
            if (funct6 == 0x48000000U)
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

    public const ulong RegisterWindowSize = 8UL;

    public bool Contains(ulong address)
        => address - _base < RegisterWindowSize;

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

    public const ulong RegisterWindowSize = 0x10000UL;

    public bool Contains(ulong address)
        => address - _base < RegisterWindowSize;

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

    public const ulong RegisterWindowSize = 0x4000000UL;

    public bool Contains(ulong address)
        => address - _base < RegisterWindowSize;

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
