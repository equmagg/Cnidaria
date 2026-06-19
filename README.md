# Cnidaria
![build](https://img.shields.io/badge/build-passing-brightgreen) ![dotnet](https://img.shields.io/badge/.NET-10.0-blue)

Cnidaria the **interpreter** and compiler for primarily **C#** and multiple other languages (currently C).

It is *THE solution to use modern C# as an embedded/scripting language*. Be it DSL, in-game scripting or remote code execution.
While it strives to cover almost all of C# syntax and be very close in semantics, it is primarily designed for small, fast and reasonably simple embedded scripts. 
As such, is does not follow CoreCLR behaviour one to one.
Cnidaria has no access to host resources by default, providing, along with strict execution limits, a level of safety by design.

---

## BCL

Basic Class Library is being ported from the ground up and has no access for host/OS resources.
Any vm-host interations must be explicitly declared by the host by attaching a library and declaring InternalCall implementations.

You can get acquainted with the standart library here.
[Standart library](./Cs/BCL/CoreBCL.cs)
[Extended library](./Cs/BCL/ExtendedBCL.cs)

---

## Hello World
```cs
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

var (output, diagnostics, context) = Cnidaria.Cs.CSharp.Interpret("""
Console.WriteLine("Hello World!");
""", cts, heapSize: 32 * 1024, stackSize: 4 * 1024, outputLimit: 4 * 1024);
Console.WriteLine(output);

```

## Pipeline
We roughly follow Roslyn/RyuJiT compilation phases.
Stack-based bytecode, being an IL analogue, can be directly interpreted for near zero startup time.
For more complex and performant scripts you can compile it into low level register bytecode, sacrificing some compilation time for all the serious optimizations and performance.

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
LSRA (register allocation) > target specific CodeGen > target > register VM (if target is register bytecode)
```
SSA/VN-based optimizations we currenly implement in order:
Constant folding
Сonstant/fact propagation
Copy propagation
Redundant Branch Optimization
Common Subexpression Elimination
Dead Code Elimination
Strength reduction
---

# С
For C we avoid stack-based VM entirely and map it to C# Register VM, which is low level enough to host C without issues, allowing for future interop.
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
