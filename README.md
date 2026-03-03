# Cnidaria
![build](https://img.shields.io/badge/build-passing-brightgreen) ![dotnet](https://img.shields.io/badge/.NET-10.0-blue)

Easy to set up, light and sandboxed C# interpreter with minimal startup time and isolation by design.
While it strives to cover almost all of C# syntax and be very close in semantics, it is primarily designed for small, fast and reasonably simple embedded scripts. 
As such, is does not follow CoreCLR behaviour one to one.

---

## BCL

Basic Class Library is being written from the ground up and has no access for host resources.
Any vm-host interations must be explicitly declared by the host.

You can get acquainted with the standart library here.
[Standart library](./Cs/BCL/CoreBCL.cs)
[Extended library](./Cs/BCL/ExtendedBCL.cs)

---

## Hello World
```cs
using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
{
   var (output, diagnostics, context) = Cnidaria.Cs.CSharp.Interpret("""
Console.WriteLine("Hello World!");
""", cts, heapSize: 32 * 1024, stackSize: 4 * 1024, outputLimit: 4 * 1024);
   Console.WriteLine(output);
}
```