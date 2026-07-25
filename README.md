# Cnidaria
![build](https://img.shields.io/badge/build-passing-brightgreen) ![dotnet](https://img.shields.io/badge/.NET-10.0-blue)

Cnidaria is the crossplatform compiler and interpreter for primarily **C#** and multiple other languages (currently C).

It is *THE solution to use modern C# as an embedded/scripting language*. Be it DSL, in-game scripting or remote code execution.
While it strives to cover almost all of C# syntax and be very close in semantics, it is primarily designed for small, fast and reasonably simple embedded scripts. 
As such, is does not follow CoreCLR behaviour one to one.
Cnidaria has no access to host resources by default, providing, along with strict execution limits, a level of safety by design. X86 target intentionally bypasses that for raw native execution speed.

---

## BCL

Basic Class Library is being ported from the ground up and has no access for host/OS resources.
Any vm-host interations must be explicitly declared by the host by attaching a library and declaring InternalCall implementations.

You can get acquainted with the standart library here.
[Standart library](./Cs/BCL/CoreBCL.cs)
[Extended library](./Cs/BCL/ExtendedBCL.cs)

---

## Hello World

Targeting internal VM

```cs
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

var (output, diagnostics, context) = Cnidaria.Cs.CSharp.Interpret("""
Console.WriteLine("Hello World!");
""", cts, heapSize: 32 * 1024, stackSize: 4 * 1024, outputLimit: 4 * 1024);
Console.WriteLine(output);

```

Targeting native x86

```cs
string source = """
Console.WriteLine("Hello World!");
"""
var (x64Exe, x64Diags) = Cnidaria.Cs.CSharp.CompileToX86(source, TargetInfo.X64Windows);
foreach(var diag in x64Diags) { Console.WriteLine(diag.GetMessage(source)); }
File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "CsExe.exe"), x64Exe?.ToExecutableBytes() ?? throw new NullReferenceException());
```

Targeting RISC-V Emulator

```cs
string source = """
Console.WriteLine("Hello World!");
System.Runtime.InteropServices.Marshal.LinuxRequestShutdown(); // stop the emulator
"""
var (program, diags) = Cnidaria.Cs.CSharp.CompileToRiscV(source, TargetInfo.RVA23Linux);
foreach(var diag in diags) { Console.WriteLine(diag.GetMessage(source)); }
var layout = Cnidaria.RiscV.RiscVZBootLayout.Default;
var machine = new Cnidaria.RiscV.RiscVEmulator(new Cnidaria.RiscV.RVMachineConfig
{
    RamBase = 0x80000000UL,
    RamSize = 128 * 1024 * 1024,
    ResetVector = layout.Zs2LoadAddress,
    BlockDeviceBase = layout.BlockDeviceBase,
    BlockDeviceSize = layout.RequiredBootChainStorageSize
});

Cnidaria.RiscV.RiscVZBoot.LoadDefaultBootChain(machine, layout, autorunSource: program?.ToLinuxExecutableBytes());
var result = machine.Run(instructionLimit: ulong.MaxValue);
while (machine.Uart.TryReadOutput(out byte b))
    Console.Write((char)b);
Console.WriteLine($"\nstop={result.StopReason} pc=0x{machine.ProgramCounter:x16} mode={machine.PrivilegeMode} steps={result.Steps} time={t.Elapsed}");
```

## Pipeline
We roughly follow Roslyn/RyuJiT(ILC) compilation phases.
Stack-based bytecode, being an IL analogue, can be directly interpreted for minimal startup time.
For more complex and performant scripts you can sacrifice some compilation time for all the serious optimizations and performance, targeting either low level register bytecode, which is better suited for VM execution, or a native target like x86 or RISC-V. x86 does not have an emulator and hence loses any security considerations.
 
Source code -> stack bytecode path mimics Roslyn pipeline
```
Lexer > Tokens
Parser > Ast
Binder > BoundTree
Rewriter > lowered BoundTree
Bytecode Emiter > stack-based bytecode > stack-based VM
```

stack bytecode -> register bytecode path mimics RyuJiT pipeline
```
stack-based bytecode > Import/Morph/Inline/Physical Promotion > GenTree HIR
CFG/SSA anotation > VN-based SSA optimization > rationalization > LIR
LSRA (register allocation) > target specific CodeGen > target > execution
```
SSA/VN-based optimizations we currently implement in order:

- Copy propagation
- Constant/fact propagation
- Constant folding
- Dead Code Elimination
- Redundant Branch Optimization
- Loop Invariant Code Motion
- Common Subexpression Elimination
- Assertion propagation
- Strength reduction

---

# С
For C we support x86, ARM and RISC-V targets, as well as a Bytecode VM. For internal VM we avoid stack-based VM entirely and map it to C# Register VM, which is low level enough to host C without issues, allowing for limited interop.
C compilation steps go as follows
```
Preprocessor+Lexer > Token stream
Parser > AST (Syntax only)
Binder > BoundTree (Semantics)
Declarator + Gimplifier > GIMPLE (Lowering)
> CFG (Control Flow Graph)
> SSA (Static Single Assignment form)
> LIR (Linear IR)
> LSRA (Register Allocator)
> target specific CodeGen > target
```

### C Hello World
Targeting internal vitual mashine

```cs
var code = """
#include <stdio.h>
int main()
{
    printf("Hello World!\n");
    return 0;
}
""";
var compilation = Cnidaria.C.Compilation.Create(code); 
foreach(var diag in compilation.GetDiagnostics())
{
    Console.WriteLine(diag.Message);
}
var cfg = Cnidaria.C.ControlFlowGraph.Build(compilation.GetSemanticModel(compilation.SyntaxTrees[0]));
var ssa = Cnidaria.C.SsaGraph.Build(cfg);
var lir = Cnidaria.C.LirModule.Lower(ssa);
var program = Cnidaria.C.RegisterBytecodeCodeGenerator.Generate(lir);
var cRuntime = program.CreateSyntheticRuntime();
byte[] cMem = GC.AllocateUninitializedArray<byte>(64 * 1024);
var cVm = new RegisterBasedVm(cMem, staticEnd: 4 * 1024, stackEnd: 32 * 1024, cRuntime.RuntimeTypes, cRuntime.Modules, program.Image, textWriter: Console.Out);
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
var limits = new Cnidaria.Cs.ExecutionLimits
{
    MaxCallDepth = 128,
    MaxInstructions = 1_000_000_000,
    TokenCheckPeriod = 256,
};
cVm.Execute(cRuntime.EntryPc, cts.Token, limits, ReadOnlySpan<VmValue>.Empty);
```

Targeting x86-64 Windows .exe

```cs
var code = """
#include <stdio.h>
int main()
{
    printf("Hello World!\n");
    return 0;
}
""";
var comp = Cnidaria.C.Compilation.Create(code, Cnidaria.C.TargetInfo.X64Windows);
foreach(var diag in comp.GetDiagnostics())
{
    Console.WriteLine(diag.Message);
}
var cfg = Cnidaria.C.ControlFlowGraph.Build(comp.GetSemanticModel(comp.SyntaxTrees[0]));
var ssa = Cnidaria.C.SsaGraph.Build(cfg);
var lir = Cnidaria.C.LirModule.Lower(ssa);
Cnidaria.X86.X86Program xProgram = Cnidaria.C.X86CodeGenerator.Generate(lir);
File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "CExecutable.exe"), xProgram.ToWindowsExecutableBytes());
```

Targeting RISC-V emulator

```cs
var code = """
#include <stdio.h>
int main()
{
    printf("Hello World!\n");
    shutdown(); // stop the emulator
    return 0;
}
""";
var comp = Cnidaria.C.Compilation.Create(code, Cnidaria.C.TargetInfo.RV64GLinux); 
foreach(var diag in comp.GetDiagnostics())
{
    Console.WriteLine(diag.Message);
}
var cfg = Cnidaria.C.ControlFlowGraph.Build(comp.GetSemanticModel(comp.SyntaxTrees[0]));
var ssa = Cnidaria.C.SsaGraph.Build(cfg);
var lir = Cnidaria.C.LirModule.Lower(ssa);
var program = Cnidaria.C.RiscVCodeGenerator.Generate(lir);

var layout = new Cnidaria.RiscV.RiscVZBootLayout();
var machine = new Cnidaria.RiscV.RiscVEmulator(new Cnidaria.RiscV.RVMachineConfig
{
    RamBase = 0x80000000UL,
    RamSize = 128 * 1024 * 1024,
    ResetVector = layout.Zs2LoadAddress,
    BlockDeviceBase = layout.BlockDeviceBase,
    BlockDeviceSize = layout.RequiredBootChainStorageSize
});

Cnidaria.RiscV.RiscVZBoot.LoadDefaultBootChain(machine, layout, autorunSource: program.ToLinuxExecutableBytes());
var result = machine.Run(instructionLimit: 10_000_000);
while (machine.Uart.TryReadOutput(out byte b))
    Console.Write((char)b);
Console.WriteLine($"\nstop={result.StopReason} pc=0x{machine.ProgramCounter:x16} mode={machine.PrivilegeMode} steps={result.Steps}");
```