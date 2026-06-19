using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Cnidaria.Cs
{
    internal static class Tests
    {
        internal static int TestsRan;
        internal static int TestsFailed;
        internal static List<string> FailedMessages = new();
        internal static List<long> InstructionsExecuted = new();
        internal static List<TimeSpan> BuildTime = new();
        internal static List<TimeSpan> CompilationTime = new();

        internal static bool Assert(string result, string target)
        {
            TestsRan++;
            if (string.IsNullOrWhiteSpace(result))
            {
                TestsFailed++;
                FailedMessages.Add($"Test {TestsRan} is empty, expected '{target}'");
                return false;
            }

            result = result.TrimStart().TrimEnd();

            if (result.Contains("Error"))
            {
                TestsFailed++;
                FailedMessages.Add($"Test {TestsRan} failed with error: {result}, expected '{target}'");
                return false;
            }

            if (result != target)
            {
                TestsFailed++;
                FailedMessages.Add($"Test {TestsRan} failed: expected '{target}', got '{result}'");
                return false;
            }

            return true;
        }
        internal static void RunTest(string source, string target)
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            {
                var (output, diagnostics, context) = Cnidaria.Cs.CSharp.Interpret(source, cts);
                foreach (var diagnostic in diagnostics)
                {
                    if(diagnostic.GetSeverity() == DiagnosticSeverity.Error)
                        output += $"{diagnostic.GetMessage()}\n";
                }
                if(diagnostics.All(x => x.GetSeverity() != DiagnosticSeverity.Error))
                {
                    InstructionsExecuted.Add(context.InstructionsCount);
                    BuildTime.Add(context.BuildTime);
                    CompilationTime.Add(context.ComlilationTime);
                }
                else
                {
                    InstructionsExecuted.Add(-1);
                }
                Assert(output, target);
            }
        }
        internal static void RunAll()
        {
            Console.WriteLine("Running tests");
            // 1 arithmetic precedence
            RunTest(@"
int A()
{
    return 2 + 3 * 4 - (10 / 2);
}
Console.WriteLine(A());
", "9");

            // 2 unary minus
            RunTest(@"
int a = -5;
Console.WriteLine((-a) * 10 + (a + 5));
", "50");

            // 3 preincrement / postincrement
            RunTest(@"
int x = 1;
int y = ++x + x++;
Console.WriteLine(y * 10 + x);
", "43");

            // 4 bitwise operators
            RunTest(@"
Console.WriteLine((5 & 3) * 100 + (5 | 2) * 10 + (5 ^ 1));
", "174");

            // 5 division / modulo with negatives
            RunTest(@"
Console.WriteLine((-7 / 3) * 10 + (-7 % 3));
", "-21");

            // 6 boolean precedence
            RunTest(@"
bool b1 = false || true && false;
bool b2 = !(false || false) && true;
Console.WriteLine((b1 ? 1 : 0) * 10 + (b2 ? 1 : 0));
", "1");

            // 7 short circuit
            RunTest(@"
int calls = 0;
bool Side()
{
    calls++;
    return true;
}
bool a = false && Side();
bool b = true || Side();
Console.WriteLine(calls);
", "0");

            // 8 ternary operator
            RunTest(@"
int F(int x) => x > 0 ? 10 : 20;
Console.WriteLine(F(1) + F(0));
", "30");

            // 9 ternary operator associativity
            RunTest(@"
int x = false ? 1 : true ? 2 : 3;
Console.WriteLine(x);
", "2");

            // 10 ternary operator precedence
            RunTest(@"
int x = false || true ? 1 : 2;
Console.WriteLine(x);
", "1");

            // 11 compound assignments
            RunTest(@"
int x = 1;
x += 2 * 3;
x *= 2;
Console.WriteLine(x);
", "14");

            // 12 relational operators
            RunTest(@"
int x =
    (5 > 3 ? 1 : 0) * 100 +
    (5 >= 5 ? 1 : 0) * 10 +
    (5 != 5 ? 1 : 0);
Console.WriteLine(x);
", "110");

            // 13 if/else / comparison
            RunTest(@"
int F(int x)
{
    if (x < 0) return 1;
    if (x == 0) return 2;
    return 3;
}
Console.WriteLine(F(-5) + F(0) + F(7));
", "6");

            // 14 while loop
            RunTest(@"
int i = 0;
int s = 0;
while (i < 10)
{
    s += i;
    i++;
}
Console.WriteLine(s);
", "45");

            // 15 for loop / break / continue
            RunTest(@"
int s = 0;
for (int i = 0; i < 10; i++)
{
    if (i == 7) break;
    if ((i % 2) == 0) continue;
    s += i;
}
Console.WriteLine(s);
", "9");

            // 16 continue in nested loops
            RunTest(@"
int s = 0;
for (int i = 0; i < 3; i++)
{
    for (int j = 0; j < 3; j++)
    {
        if (i == 1 && j == 1) continue;
        s += i * 10 + j;
    }
}
Console.WriteLine(s);
", "88");

            // 17 nested local function
            RunTest(@"
int Outer(int a)
{
    int Inner(int x) { return x + 1; }
    return Inner(a) + Inner(a + 10);
}
Console.WriteLine(Outer(5));
", "22");

            // 18 local function recursion
            RunTest(@"
int Fact(int n)
{
    int F(int x)
    {
        if (x <= 1) return 1;
        return x * F(x - 1);
    }
    return F(n);
}
Console.WriteLine(Fact(6));
", "720");

            // 19 params
            RunTest(@"
int Sum(params int[] items)
{
    int s = 0;
    for (int i = 0; i < items.Length; i++) s += items[i];
    return s;
}
Console.WriteLine(Sum(5, 6, 7));
", "18");

            // 20 explicit array argument in params
            RunTest(@"
int Sum(params int[] items)
{
    int s = 0;
    for (int i = 0; i < items.Length; i++) s += items[i];
    return s;
}
int[] x = new int[3];
x[0] = 5; x[1] = 6; x[2] = 7;
Console.WriteLine(Sum(x));
", "18");

            // 21 ref parameter
            RunTest(@"
void Swap(ref int a, ref int b)
{
    int t = a; a = b; b = t;
}
int x = 10, y = 20;
Swap(ref x, ref y);
Console.WriteLine(x * 100 + y);
", "2010");

            // 22 out parameter
            RunTest(@"
bool TryGet(out int v)
{
    v = 123;
    return true;
}
int x;
bool ok = TryGet(out x);
Console.WriteLine((ok ? 1 : 0) * 1000 + x);
", "1123");

            // 23 arrays
            RunTest(@"
int[] a = new int[4];
a[0] = 3; a[1] = 1; a[2] = 4; a[3] = 2;
int s = 0;
for (int i = 0; i < a.Length; i++) s += a[i];
Console.WriteLine(s);
", "10");

            // 24 array initializer
            RunTest(@"
int[] a = new int[] { 1, 2, 3 };
Console.WriteLine(a.Length * 100 + a[0] * 10 + a[2]);
", "313");

            // 25 index from end
            RunTest(@"
int[] arr = new int[5] { 1, 2, 3, 4, 5 };
int k = 3;
Console.WriteLine(arr[^k]);
", "3");

            // 26 index from end on constants
            RunTest(@"
int[] arr = new int[5] { 1, 2, 3, 4, 5 };
Console.WriteLine(arr[^1] * 10 + arr[^2]);
", "54");

            // 27 array slices ..x / x.. / ..
            RunTest(@"
int[] arr = new int[5];
Console.Write(arr[..2].Length);
Console.Write(arr[2..].Length);
Console.Write(arr[..].Length);
", "235");

            // 28 array slice with from end
            RunTest(@"
int[] arr = new int[5];
Console.Write(arr[..^2].Length);
Console.Write(arr[^2..].Length);
", "32");

            // 29 array slice empty / full boundary
            RunTest(@"
int[] a = new int[5];
Console.Write(a[0..0].Length);
Console.Write(a[0..5].Length);
Console.Write(a[5..5].Length);
", "050");

            // 30 array / string slice
            RunTest(@"
int[] arr = new int[5];
var sliced = arr[1..];
string str = ""hello"";
Console.Write(sliced.Length);
Console.Write(str[1..3].Length);
", "42");

            // 31 string slices ..x / x..
            RunTest(@"
string s = ""abcdef"";
Console.Write(s[..3].Length);
Console.Write(s[2..].Length);
", "34");

            // 32 string range from end
            RunTest(@"
string s = ""abcdef"";
Console.WriteLine(s[1..^1].Length);
", "4");

            // 33 string slice empty / full
            RunTest(@"
string s = ""abc"";
Console.Write(s[..0].Length);
Console.Write(s[0..].Length);
", "03");

            // 34 string Substring / Replace(char,char)
            RunTest(@"
string s = ""hello"";
Console.Write(s.Substring(1, 3));
Console.Write(""a-b-c"".Replace('-', '+'));
", "ella+b+c");

            // 35 string Substring(startIndex)
            RunTest(@"
Console.Write(""hello"".Substring(2));
", "llo");

            // 36 Replace(string,string)
            RunTest(@"
Console.Write(""one two one"".Replace(""one"", ""1""));
", "1 two 1");

            // 37 string equality / inequality operators
            RunTest(@"
string a = ""ab"";
string b = ""ab"";
Console.WriteLine((a == b ? 1 : 0) * 10 + (a != b ? 1 : 0));
", "10");

            // 38 string Contains
            RunTest(@"
string s = ""hello world"";
int x =
    (s.Contains('w') ? 1 : 0) * 100 +
    (s.Contains(""world"") ? 1 : 0) * 10 +
    (s.Contains("""") ? 1 : 0);
Console.WriteLine(x);
", "111");

            // 39 Contains negative cases
            RunTest(@"
string s = ""abc"";
Console.WriteLine((s.Contains('x') ? 1 : 0) * 10 + (s.Contains(""d"") ? 1 : 0));
", "0");

            // 40 TrimStart / TrimEnd / Trim
            RunTest(@"
string s = ""  abc  "";
Console.Write(s.TrimStart().Length);
Console.Write(s.TrimEnd().Length);
Console.Write(s.Trim().Length);
", "553");

            // 41 string.IsNullOrWhiteSpace
            RunTest(@"
string a = null;
string b = """";
string c = "" \t\n"";
string d = "" a "";
int x =
    (string.IsNullOrWhiteSpace(a) ? 1 : 0) * 1000 +
    (string.IsNullOrWhiteSpace(b) ? 1 : 0) * 100 +
    (string.IsNullOrWhiteSpace(c) ? 1 : 0) * 10 +
    (string.IsNullOrWhiteSpace(d) ? 1 : 0);
Console.WriteLine(x);
", "1110");

            // 42 Join(string, string[]) with null element
            RunTest(@"
string[] a = new string[] { ""a"", null, ""c"" };
Console.Write(string.Join(""-"", a));
", "a--c");

            // 43 Join with null separator
            RunTest(@"
string sep = null;
string[] a = new string[] { ""a"", ""b"", ""c"" };
Console.Write(string.Join(sep, a));
", "abc");

            // 44 Join(string, object[]) with null element
            RunTest(@"
object[] a = new object[] { 1, null, ""c"" };
Console.Write(string.Join(""-"", a));
", "1--c");

            // 45 StartsWith / EndsWith
            RunTest(@"
string s = ""hello"";
Console.WriteLine((s.StartsWith(""he"") ? 1 : 0) * 10 + (s.EndsWith(""lo"") ? 1 : 0));
", "11");

            // 46 IndexOf(char)
            RunTest(@"
int x = ""abc"".IndexOf('b') * 100 + (""aaaa"".IndexOf('z') + 10);
Console.WriteLine(x);
", "109");

            // 47 string.Concat(object, object, object) with null object
            RunTest(@"
Console.WriteLine(string.Concat(""a"", (object)null, ""b""));
", "ab");

            // 48 Int32.Parse
            RunTest(@"
Console.WriteLine(Int32.Parse(""  +42 "") * 10 + Int32.Parse("" -7 ""));
", "413");

            // 49 Int32.TryParse
            RunTest(@"
int x;
bool ok = Int32.TryParse("" 12 "", out x);
Console.WriteLine((ok ? 1 : 0) * 100 + x);
", "112");

            // 50 Byte.TryParse
            RunTest(@"
byte b;
bool ok = byte.TryParse("" 255 "", out b);
Console.WriteLine((ok ? 1 : 0) * 1000 + b);
", "1255");

            // 51 Nullable<T> basic
            RunTest(@"
Nullable<int> n = new Nullable<int>(5);
Console.WriteLine((n.HasValue ? 1 : 0) * 10 + n.Value);
", "15");

            // 52 classes / fields / ctor
            RunTest(@"
namespace Ns;

class C
{
    public int X;
    public C(int x) { X = x; }
    public int Add(int y) { return X + y; }
}

internal class Program
{
    public static void Main(string[] args)
    {
        var c = new C(7);
        Console.WriteLine(c.Add(5));
    }
}
", "12");

            // 53 method mutating field
            RunTest(@"
namespace Ns;

class C
{
    public int X;
    public int Inc() { X++; return X; }
}

internal class Program
{
    public static void Main(string[] args)
    {
        var c = new C();
        Console.WriteLine(c.Inc() * 100 + c.Inc());
    }
}
", "102");

            // 54 class default field initialization
            RunTest(@"
namespace Ns;
class C
{
    public int X;
}
internal class Program
{
    public static void Main(string[] args)
    {
        var c = new C();
        Console.WriteLine(c.X);
    }
}
", "0");
            // 55 Vector3
            RunTest(@"
var vec = new Vector3(2f, 3, 1f);
vec = vec * 2;
Console.WriteLine(vec.ToString());
", "<4, 6, 2>");
            // 56 List<T> Add / indexer / Count
            RunTest(@"
var l = new List<int>();
l.Add(7);
l.Add(8);
l.Add(9);
l.Add(10);
Console.WriteLine(l.Count * 1000 + l[0] * 100 + l[3]);
", "4710");

            // 57 StringBuilder basic
            RunTest(@"
var sb = new System.Text.StringBuilder();
sb.Append('a');
sb.Append('b');
sb.Append(""cd"");
Console.WriteLine(sb.ToString());
", "abcd");

            // 58 tuples basic
            RunTest(@"
(int a, byte b) x = (3, 2);
Console.Write(x.Item1);
Console.Write(x.b);
", "32");

            // 59 tuples as return / parameter
            RunTest(@"
(int x, int y) Make() => (10, 20);
int Sum((int x, int y) p) => p.x + p.Item2;
Console.WriteLine(Sum(Make()));
", "30");

            // 60 tuples with Rest
            RunTest(@"
var t = (1, 2, 3, 4, 5, 6, 7, 8);
Console.Write(t.Item7);
Console.Write(t.Item8);
", "78");

            // 61 heavy arithmetic
            RunTest(@"
namespace Ns;
internal class Program
{
    public unsafe static void Main(string[] args)
    {
        int n = 1_000;
        int s = 0;
        for (int i = 0; i < n; i++)
        {
            s += (i * i - i) / (i + 1);
        }
        Console.WriteLine(s);
    }
}
", "497503");

            // 62 stackalloc byte
            RunTest(@"
unsafe
{
    byte* p = stackalloc byte[4];
    p[0] = 1;
    p[1] = 2;
    p[2] = 3;
    p[3] = 4;
    Console.Write(p[0]);
    Console.Write(p[3]);
    Console.Write(p[1] + p[2]);
}
", "145");
            // 63 generic byref parameter assignment
            RunTest(@"
namespace Ns;
public struct Pair
{
    public int A;
    public int B;
    public Pair(int a, int b) { A = a; B = b; }
}
internal class Program
{
    static void Set<T>(ref T dst, T v)
    {
        dst = v;
    }
    public static void Main(string[] args)
    {
        Pair p = new Pair(1,2);
        Set<Pair>(ref p, new Pair(9, 4));
        Console.Write(p.A);
        Console.Write(p.B);
    }
}
", "94");
            // 64 bit cast double to long
            RunTest(@"
Console.Write(BitConverter.DoubleToInt64Bits(0.0) == 0x0000000000000000);
Console.Write(BitConverter.DoubleToInt64Bits(-0.0) == unchecked((long)0x8000000000000000));
Console.Write(BitConverter.DoubleToInt64Bits(1.0) == unchecked((long)0x3FF0000000000000));
Console.Write(BitConverter.DoubleToInt64Bits(-1.0) == unchecked((long)0xBFF0000000000000));
Console.Write(BitConverter.DoubleToInt64Bits(double.PositiveInfinity) == unchecked((long)0x7FF0000000000000));
Console.Write(BitConverter.DoubleToInt64Bits(double.NegativeInfinity) == unchecked((long)0xFFF0000000000000));
", "truetruetruetruetruetrue");
            // 65 Math.Pow
            RunTest(@"
Console.Write(Math.Pow(2, 3) == 8.0);
Console.Write(Math.Pow(2, -3) == 0.125);
Console.Write(Math.Pow(0.25, 0.5) == 0.5);
Console.Write(Math.Pow(16, 0.25) == 2.0);
", "truetruetruetrue");
            // 66 Split(char separator)
            RunTest(@"
string str = ""aa,b,,c"";
string[] strs = str.Split(',');
Console.WriteLine(strs[0]);
", "aa");
            // 67 foreach
            RunTest(@"
int[] arr = { 1, 2, 3 };
foreach(var item in arr)
{
    Console.Write(item);
}
var list = new List<int>(); list.Add(1); list.Add(2);
foreach(var item in list) 
{
    Console.Write(item);
}
string str = ""1234"";
foreach(var item in str) 
{
     Console.Write(item);
}
", "123121234");
            //68 tuple deconstruction
            RunTest(@"
(int a, int b) t = (1, 2);

int x, y;
(x, y) = t;

var (p, q) = (3, 4);
((x, y), p) = ((10, 20), 30);

Console.Write(x);
Console.Write(y);
Console.Write(p);
Console.Write(q);
", "1020304");
            // 69 tuple swap parallel assignment
            RunTest(@"
int x = 1, y = 2;
(x, y) = (y, x);
Console.WriteLine(x * 10 + y);
", "21");
            // 70 local function captures outer variable
            RunTest(@"
int sum = 0;
void Add(int x) { sum += x; }
Add(1);
Add(20);
Console.WriteLine(sum);
", "21");
            // 71 argument evaluation order
            RunTest(@"
int x = 1;
int Next() { return x++; }
int Pack(int a, int b, int c) { return a * 100 + b * 10 + c; }
Console.WriteLine(Pack(Next(), Next(), Next()) * 10 + x);
", "1234");
            // 72 ternary evaluates only selected branch
            RunTest(@"
int calls = 0;
int A() { calls++; return 1; }
int B() { calls += 10; return 2; }
int x = true ? A() : B();
Console.WriteLine(x * 100 + calls);
", "101");
            // 73 switch basic dispatch
            RunTest(@"
int F(int x)
{
    switch (x)
    {
        case 0: return 1;
        case 1: return 2;
        default: return 3;
    }
}
Console.WriteLine(F(0) * 100 + F(1) * 10 + F(9));
", "123");
            // 74 params with zero arguments
            RunTest(@"
int Sum(params int[] items)
{
    int s = 0;
    for (int i = 0; i < items.Length; i++) s += items[i];
    return s;
}
Console.WriteLine(Sum());
", "0");
            // 75 Postfix unary order
            RunTest(@"
int x = 1;
x = x++;
Console.WriteLine(x);
", "1");
            // 76 Prefix unary order
            RunTest(@"
int x = 1;
x = ++x;
Console.WriteLine(x);
", "2");
            // 77 assignment with side effect
            RunTest(@"
int i = 0;
int[] a = new int[] { 10, 20 };
a[i] = i++;
Console.WriteLine(a[0] * 100 + a[1] * 10 + i);
", "201");
            // 78 struct value copy isolation
            RunTest(@"
namespace Ns;
struct S
{
    public int A;
    public int B;
    public S(int a, int b) { A = a; B = b; }
}
internal class Program
{
    public static void Main(string[] args)
    {
        S a = new S(1, 2);
        S b = a;
        b.A = 9;
        b.B = 8;
        Console.Write(a.A);
        Console.Write(a.B);
        Console.Write(b.A);
        Console.Write(b.B);
    }
}
", "1298");
            // 79 struct return copy isolation
            RunTest(@"
namespace Ns;
struct S
{
    public int A;
    public int B;
    public S(int a, int b) { A = a; B = b; }
}
internal class Program
{
    static S Make() => new S(3, 4);
    public static void Main(string[] args)
    {
        S x = Make();
        S y = Make();
        x.A = 9;
        Console.Write(x.A);
        Console.Write(x.B);
        Console.Write(y.A);
        Console.Write(y.B);
    }
}
", "9434");
            // 80 struct field promotion
            RunTest(@"
namespace Ns;
struct Pair
{
    public int X;
    public int Y;
    public Pair(int x, int y) { X = x; Y = y; }
}
internal class Program
{
    static Pair Make(int a, int b) => new Pair(a + 1, b + 2);
    static int Sum(Pair p) => p.X * 10 + p.Y;
    public static void Main(string[] args)
    {
        Console.WriteLine(Sum(Make(4, 5)));
    }
}
", "57");
            // 81 struct argument mutation does not affect caller without ref
            RunTest(@"
namespace Ns;
struct Pair
{
    public int X;
    public int Y;
}
internal class Program
{
    static void Mutate(Pair p)
    {
        p.X = 100;
        p.Y = 200;
    }
    public static void Main(string[] args)
    {
        Pair p;
        p.X = 1;
        p.Y = 2;
        Mutate(p);
        Console.Write(p.X);
        Console.Write(p.Y);
    }
}
", "12");
            // 82 struct ref argument mutation affects caller
            RunTest(@"
namespace Ns;
struct Pair
{
    public int X;
    public int Y;
}
internal class Program
{
    static void Mutate(ref Pair p)
    {
        p.X = 100;
        p.Y = 200;
    }
    public static void Main(string[] args)
    {
        Pair p;
        p.X = 1;
        p.Y = 2;
        Mutate(ref p);
        Console.Write(p.X);
        Console.Write(p.Y);
    }
}
", "100200");
            // 83 struct out parameter writes all fields
            RunTest(@"
namespace Ns;
struct Pair
{
    public int X;
    public int Y;
}
internal class Program
{
    static void Make(out Pair p)
    {
        p.X = 7;
        p.Y = 8;
    }
    public static void Main(string[] args)
    {
        Pair p;
        Make(out p);
        Console.Write(p.X);
        Console.Write(p.Y);
    }
}
", "78");
            // 84 nested struct field load
            RunTest(@"
namespace Ns;
struct Inner
{
    public int A;
    public int B;
}
struct Outer
{
    public Inner I;
    public int C;
}
internal class Program
{
    public static void Main(string[] args)
    {
        Outer o;
        o.I.A = 1;
        o.I.B = 2;
        o.C = 3;
        Console.WriteLine(o.I.A * 100 + o.I.B * 10 + o.C);
    }
}
", "123");
            // 85 nested struct field mutation
            RunTest(@"
namespace Ns;
struct Inner
{
    public int A;
    public int B;
}
struct Outer
{
    public Inner I;
}
internal class Program
{
    static void Inc(ref Outer o)
    {
        o.I.A++;
        o.I.B += 10;
    }
    public static void Main(string[] args)
    {
        Outer o;
        o.I.A = 4;
        o.I.B = 5;
        Inc(ref o);
        Console.Write(o.I.A);
        Console.Write(o.I.B);
    }
}
", "515");
            // 86 array of structs stores independent copies
            RunTest(@"
namespace Ns;
struct Pair
{
    public int X;
    public int Y;
    public Pair(int x, int y) { X = x; Y = y; }
}
internal class Program
{
    public static void Main(string[] args)
    {
        Pair p = new Pair(1, 2);
        Pair[] a = new Pair[2];
        a[0] = p;
        p.X = 9;
        a[1] = p;
        Console.Write(a[0].X);
        Console.Write(a[0].Y);
        Console.Write(a[1].X);
        Console.Write(a[1].Y);
    }
}
", "1292");
            // 87 array struct element field assignment
            RunTest(@"
namespace Ns;
struct Pair
{
    public int X;
    public int Y;
}
internal class Program
{
    public static void Main(string[] args)
    {
        Pair[] a = new Pair[2];
        a[0].X = 3;
        a[0].Y = 4;
        a[1].X = a[0].X + 10;
        a[1].Y = a[0].Y + 20;
        Console.Write(a[0].X);
        Console.Write(a[0].Y);
        Console.Write(a[1].X);
        Console.Write(a[1].Y);
    }
}
", "341324");
            // 88 tuple field promotion with mixed sizes
            RunTest(@"
(byte a, int b, short c) Make() => ((byte)2, 300, (short)4);
int Sum((byte a, int b, short c) p) => p.a * 10000 + p.b * 10 + p.c;
Console.WriteLine(Sum(Make()));
", "23004");
            // 89 tuple field promotion with biger sizes
            RunTest(@"
(long a, int b) Make() => (10000000000L, 7);
long Sum((long a, int b) p) => p.a + p.b;
Console.WriteLine(Sum(Make()));
", "10000000007");
            // 90 tuple mutation local
            RunTest(@"
(int a, int b) t = (1, 2);
t.a += 10;
t.Item2 += 20;
Console.Write(t.a);
Console.Write(t.b);
", "1122");
            // 91 tuple passed by ref
            RunTest(@"
void Mutate(ref (int a, int b) t)
{
    t.a += 3;
    t.b += 4;
}
var t = (10, 20);
Mutate(ref t);
Console.Write(t.Item1);
Console.Write(t.Item2);
", "1324");
            // 92 tuple out parameter
            RunTest(@"
void Make(out (int a, int b) t)
{
    t = (5, 6);
}
(int a, int b) x;
Make(out x);
Console.Write(x.a);
Console.Write(x.b);
", "56");
            // 93 tuple nested fields
            RunTest(@"
var t = ((1, 2), (3, 4));
Console.Write(t.Item1.Item1);
Console.Write(t.Item1.Item2);
Console.Write(t.Item2.Item1);
Console.Write(t.Item2.Item2);
", "1234");
            // 94 tuple deconstruction from method return
            RunTest(@"
(int a, int b) Make() => (7, 8);
var (x, y) = Make();
Console.Write(x);
Console.Write(y);
", "78");
            // 95 tuple swap with side effecting indexers
            RunTest(@"
int[] a = new int[] { 1, 2 };
int i = 0;
(a[i++], a[i++]) = (a[1], a[0]);
Console.Write(a[0]);
Console.Write(a[1]);
Console.Write(i);
", "212");
            // 96 nested tuple assignment order
            RunTest(@"
int a = 1, b = 2, c = 3;
(a, (b, c)) = (c, (a, b));
Console.Write(a);
Console.Write(b);
Console.Write(c);
", "312");
            // 97 local function capture after mutation
            RunTest(@"
int x = 1;
int F() => x;
x = 9;
Console.WriteLine(F());
", "9");
            // 98 nested local function captures two levels
            RunTest(@"
int x = 1;
int A()
{
    int y = 10;
    int B()
    {
        int z = 100;
        return x + y + z;
    }
    return B();
}
x = 2;
Console.WriteLine(A());
", "112");
            // 99 capture mutated in loop
            RunTest(@"
int x = 0;
void Add(int v) { x += v; }
for (int i = 1; i <= 5; i++)
{
    Add(i);
}
Console.WriteLine(x);
", "15");
            // 100 recursive local function with captured accumulator
            RunTest(@"
int acc = 0;
void Walk(int n)
{
    if (n == 0) return;
    acc += n;
    Walk(n - 1);
}
Walk(5);
Console.WriteLine(acc);
", "15");
            // 101 argument evaluation order with mixed ref/out
            RunTest(@"
int x = 1;
int Next() { return x++; }
void M(int a, ref int b, int c)
{
    b = a * 100 + b * 10 + c;
}
int y = 2;
M(Next(), ref y, Next());
Console.WriteLine(y * 10 + x);
", "1223");

            // 102 assignment target evaluated before rhs
            RunTest(@"
int[] a = new int[] { 10, 20, 30 };
int i = 0;
a[i] = (i = 2);
Console.Write(a[0]);
Console.Write(a[1]);
Console.Write(a[2]);
Console.Write(i);
", "220302");
            // 103 compound assignment evaluates once
            RunTest(@"
int[] a = new int[] { 10, 20, 30 };
int i = 0;
a[i++] += 5;
Console.Write(a[0]);
Console.Write(a[1]);
Console.Write(a[2]);
Console.Write(i);
", "1520301");
            // 104 null coalescing lazy rhs
            RunTest(@"
int calls = 0;
string F()
{
    calls++;
    return ""x"";
}
string a = ""a"";
string b = null;
Console.Write(a ?? F());
Console.Write(b ?? F());
Console.Write(calls);
", "ax1");
            // 105 null coalescing assignment
            RunTest(@"
string a = null;
string b = ""b"";
a ??= ""a"";
b ??= ""x"";
Console.Write(a);
Console.Write(b);
", "ab");
            // 106 unchecked overflow wraps
            RunTest(@"
int x = unchecked(2147483647 + 1);
Console.WriteLine(x == -2147483648);
", "true");
            // 107 unchecked conversion truncates
            RunTest(@"
long x = 300;
byte b = unchecked((byte)x);
Console.WriteLine(b);
", "44");
            // 108 checked overflow throws
            RunTest(@"
try
{
    int x = checked(2147483647 + 1);
    Console.Write(x);
}
catch (OverflowException)
{
    Console.Write(""overflow"");
}
", "overflow");
            // 109 checked conversion overflow
            RunTest(@"
try
{
    long x = 300;
    byte b = checked((byte)x);
    Console.Write(b);
}
catch (OverflowException)
{
    Console.Write(""overflow"");
}
", "overflow");
            // 110 switch fallthrough
            RunTest(@"
int F(int x)
{
    int r = 0;
    switch (x)
    {
        case 1:
            r += 10;
            break;
        case 2:
        case 3:
            r += 20;
            break;
        default:
            r += 30;
            break;
    }
    return r + x;
}
Console.WriteLine(F(1) + F(2) + F(4));
", "67");
            // 111 switch in loop with continue and break
            RunTest(@"
int s = 0;
for (int i = 0; i < 6; i++)
{
    switch (i)
    {
        case 1:
            continue;
        case 4:
            break;
        default:
            s += i;
            break;
    }
    s += 10;
}
Console.WriteLine(s);
", "60");
            // 112 do while executes once
            RunTest(@"
int i = 10;
int s = 0;
do
{
    s += i;
    i++;
}
while (i < 10);
Console.WriteLine(s);
", "10");
            // 113 for without initializer and iterator
            RunTest(@"
int i = 0;
int s = 0;
for (; i < 5;)
{
    s += i;
    i++;
}
Console.WriteLine(s);
", "10");
            // 114 nested break exits only inner loop
            RunTest(@"
int s = 0;
for (int i = 0; i < 3; i++)
{
    for (int j = 0; j < 5; j++)
    {
        if (j == 2) break;
        s += i * 10 + j;
    }
}
Console.WriteLine(s);
", "63");
            // 115 foreach over struct array
            RunTest(@"
namespace Ns;
struct Pair
{
    public int X;
    public int Y;
    public Pair(int x, int y) { X = x; Y = y; }
}
internal class Program
{
    public static void Main(string[] args)
    {
        Pair[] a = new Pair[] { new Pair(1, 2), new Pair(3, 4) };
        int s = 0;
        foreach (Pair p in a)
        {
            s += p.X * 10 + p.Y;
        }
        Console.WriteLine(s);
    }
}
", "46");
            // 116 double to string shortest round trip
            RunTest(@"
Console.WriteLine(Math.PI);
", "3.141592653589793");
            // 117 generic struct identity
            RunTest(@"
namespace Ns;
struct Pair
{
    public int X;
    public int Y;
    public Pair(int x, int y) { X = x; Y = y; }
}
internal class Program
{
    static T Id<T>(T value) => value;
    public static void Main(string[] args)
    {
        Pair p = Id<Pair>(new Pair(5, 6));
        Console.Write(p.X);
        Console.Write(p.Y);
    }
}
", "56");
            // 118 generic struct swap
            RunTest(@"
namespace Ns;
struct Pair
{
    public int X;
    public int Y;
    public Pair(int x, int y) { X = x; Y = y; }
}
internal class Program
{
    static void Swap<T>(ref T a, ref T b)
    {
        T t = a;
        a = b;
        b = t;
    }
    public static void Main(string[] args)
    {
        Pair a = new Pair(1, 2);
        Pair b = new Pair(3, 4);
        Swap<Pair>(ref a, ref b);
        Console.Write(a.X);
        Console.Write(a.Y);
        Console.Write(b.X);
        Console.Write(b.Y);
    }
}
", "3412");
            // 119 generic array write/read
            RunTest(@"
T First<T>(T[] a)
{
    return a[0];
}
int[] xs = new int[] { 7, 8 };
string[] ss = new string[] { ""a"", ""b"" };
Console.Write(First<int>(xs));
Console.Write(First<string>(ss));
", "7a");
            // 120 virtual dispatch
            RunTest(@"
namespace Ns;
class Base
{
    public virtual int F() { return 1; }
}
class Derived : Base
{
    public override int F() { return 2; }
}
internal class Program
{
    public static void Main(string[] args)
    {
        Base b = new Derived();
        Console.WriteLine(b.F());
    }
}
", "2");
            // 121 virtual dispatch through base method
            RunTest(@"
namespace Ns;
class Base
{
    public virtual int F() { return 1; }
    public int G() { return F() + 10; }
}
class Derived : Base
{
    public override int F() { return 5; }
}
internal class Program
{
    public static void Main(string[] args)
    {
        Base b = new Derived();
        Console.WriteLine(b.G());
    }
}
", "15");
            // 122 inheritance field layout
            RunTest(@"
namespace Ns;
class A
{
    public int X;
}
class B : A
{
    public int Y;
}
internal class Program
{
    public static void Main(string[] args)
    {
        B b = new B();
        b.X = 3;
        b.Y = 4;
        Console.Write(b.X);
        Console.Write(b.Y);
    }
}
", "34");
            // 123 basic is operator
            RunTest(@"
namespace Ns;
class A { }
class B : A { }
internal class Program
{
    public static void Main(string[] args)
    {
        A a = new B();
        object n = null;
        Console.Write(a is B);
        Console.Write(a is A);
        Console.Write(n is A);
    }
}
", "truetruefalse");
            // 124 boxing and virtual call
            RunTest(@"
object x = 123;
Console.WriteLine(x.ToString());
", "123");
            // 125 boxing struct field value
            RunTest(@"
namespace Ns;
struct S
{
    public int X;
    public override string ToString() { return X.ToString(); }
}
internal class Program
{
    public static void Main(string[] args)
    {
        S s;
        s.X = 42;
        object o = s;
        s.X = 100;
        Console.WriteLine(o.ToString());
    }
}
", "42");
            // 126 IndexOf generic inference
            RunTest(@"
int[] xs = [ 10, 20, 30 ];
string[] ss = [ ""a"", ""b"", ""c"" ];

Console.Write(Array.IndexOf(xs, 20));
Console.Write(Array.IndexOf(ss, ""c""));
", "12");
            // 127 interpolated string format specifier
            RunTest(@"
int val = 65;
Console.Write($""0x{val:X8}"");
", "0x00000041");
            // 128 delegate closure capture
            RunTest(@"
int x = 65;
Action a = () => Console.Write(x);
a();
", "65");
            // 129 multicast delegate invocation
            RunTest(@"
int x = 65;
int y = 56;
Action a = () => Console.Write(x);
a += () => Console.Write(y);
a();
", "6556");
            // 130 predicate invocation
            RunTest(@"
List<int> list = [ 1, 2, 3, 4, 5 ];
Console.Write(list.FindLastIndex(x => x % 2 == 0));
", "3");
            // 131 goto case
            RunTest(@"
int x = 0;
switch (x)
{
    case 0:
        Console.Write(0);
        goto case 2;

    case 1:
        Console.Write(1);
        break;

    case 2:
        Console.Write(2);
        goto default;

    default:
        Console.Write(3);
        break;
}
", "023");
            // 132 using var
            RunTest(@"
namespace Ns;
class C : IDisposable
{
    public int X;
    public void Dispose()
    {
        Console.Write(""Disposed"");
    }
}
internal class Program
{
    public static void Main(string[] args)
    {
        using (C c = new C()) { }
        using var c = new C();
    }
}
", "DisposedDisposed");
            // 133 yield return
            RunTest(@"
foreach (int i in ProduceEvenNumbers(9))
{
    Console.Write(i);
}
IEnumerable<int> ProduceEvenNumbers(int upto)
{
    for (int i = 0; i <= upto; i += 2)
    {
        yield return i;
    }
}
", "02468");
            // 134 preprocessor skips inactive syntax garbage
            RunTest(@"
#if false
""unterminated string
#endif
Console.WriteLine(134);
", "134");
            // 135 preprocessor #if true keeps active branch
            RunTest(@"
#if true
Console.Write(1);
#else
Console.Write(2);
#endif
Console.Write(3);
", "13");
            // 136 int shift count masking
            RunTest(@"
int a = 1 << 31;
int b = 1 << 32;
int c = 8 >> 35;
Console.Write(a == -2147483648);
Console.Write(b);
Console.Write(c);
", "true11");

            // 137 long shift count masking
            RunTest(@"
long a = 1L << 63;
long b = 1L << 64;
long c = 16L >> 68;
Console.Write(a == unchecked((long)0x8000000000000000));
Console.Write(b);
Console.Write(c);
", "true11");

            // 138 signed integer division truncates toward zero
            RunTest(@"
Console.Write((-7 / 2));
Console.Write(',');
Console.Write((7 / -2));
Console.Write(',');
Console.Write((-7 / -2));
", "-3,-3,3");

            // 139 signed integer remainder sign follows dividend
            RunTest(@"
Console.Write((-7 % 2));
Console.Write(',');
Console.Write((7 % -2));
Console.Write(',');
Console.Write((-7 % -2));
", "-1,1,-1");

            // 140 compound assignment cast back byte
            RunTest(@"
byte b = 250;
b += 10;
Console.WriteLine(b);
", "4");
            // 141 nested compound assignment value result
            RunTest(@"
int x = 1;
int y = 2;
int z = (x += 10) + (y *= 20);
Console.Write(x);
Console.Write(',');
Console.Write(y);
Console.Write(',');
Console.Write(z);
", "11,40,51");
            // 142 right associative assignment
            RunTest(@"
int a = 1;
int b = 2;
int c = 3;
a = b = c = 9;
Console.Write(a);
Console.Write(b);
Console.Write(c);
", "999");
            // 143 null coalescing right associativity
            RunTest(@"
string a = null;
string b = null;
string c = ""c"";
Console.Write(a ?? b ?? c);
", "c");
            // 144 jagged array nested side effects
            RunTest(@"
int[][] a = new int[2][];
a[0] = new int[] { 10, 20 };
a[1] = new int[] { 30, 40 };
int i = 0;
int j = 0;
a[i++][j++] += a[i][j];
Console.Write(a[0][0]);
Console.Write(',');
Console.Write(i);
Console.Write(',');
Console.Write(j);
", "50,1,1");
            // 145 foreach iteration variable copy for struct
            RunTest(@"
namespace Ns;
struct S
{
    public int X;
}
internal class Program
{
    public static void Main(string[] args)
    {
        S[] a = new S[1];
        a[0].X = 5;
        foreach (S s in a)
        {
            S t = s;
            t.X = 9;
        }
        Console.WriteLine(a[0].X);
    }
}
", "5");
            // 146 default struct array zero initialization
            RunTest(@"
namespace Ns;
struct S
{
    public int A;
    public int B;
}
internal class Program
{
    public static void Main(string[] args)
    {
        S[] a = new S[2];
        Console.Write(a[0].A);
        Console.Write(a[0].B);
        Console.Write(a[1].A);
        Console.Write(a[1].B);
    }
}
", "0000");
            // 147 nested struct array field mutation
            RunTest(@"
namespace Ns;
struct Inner
{
    public int X;
}
struct Outer
{
    public Inner I;
}
internal class Program
{
    public static void Main(string[] args)
    {
        Outer[] a = new Outer[1];
        a[0].I.X = 42;
        Console.WriteLine(a[0].I.X);
    }
}
", "42");
            // 148 class field default null
            RunTest(@"
namespace Ns;
class Node
{
    public Node Next;
}
internal class Program
{
    public static void Main(string[] args)
    {
        Node n = new Node();
        Console.WriteLine(n.Next == null);
    }
}
", "true");
            // 149 class array reference aliases
            RunTest(@"
namespace Ns;
class Box
{
    public int X;
}
internal class Program
{
    public static void Main(string[] args)
    {
        Box b = new Box();
        b.X = 3;
        Box[] a = new Box[] { b, b };
        a[0].X = 9;
        Console.WriteLine(a[1].X);
    }
}
", "9");
            // 150 virtual call from base method
            RunTest(@"
namespace Ns;
class A
{
    public virtual int F() { return 1; }
    public int G() { return F() * 10; }
}
class B : A
{
    public override int F() { return 7; }
}
internal class Program
{
    public static void Main(string[] args)
    {
        A a = new B();
        Console.WriteLine(a.G());
    }
}
", "70");
            // 151 override calls base method
            RunTest(@"
namespace Ns;
class A
{
    public virtual int F() { return 10; }
}
class B : A
{
    public override int F() { return base.F() + 5; }
}
internal class Program
{
    public static void Main(string[] args)
    {
        A a = new B();
        Console.WriteLine(a.F());
    }
}
", "15");
            // 152 is operator with sealed type
            RunTest(@"
namespace Ns;
class A { }
class B : A { }
class C : B { }
internal class Program
{
    public static void Main(string[] args)
    {
        A x = new C();
        Console.Write(x is A);
        Console.Write(x is B);
        Console.Write(x is C);
        Console.Write(x is string);
    }
}
", "truetruetruefalse");

            // 153 explicit downcast
            RunTest(@"
namespace Ns;
class A { public int X; }
class B : A { public int Y; }
internal class Program
{
    public static void Main(string[] args)
    {
        A a = new B();
        a.X = 3;
        B b = (B)a;
        b.Y = 4;
        Console.Write(b.X);
        Console.Write(b.Y);
    }
}
", "34");
            // 154 boxing keeps struct copy
            RunTest(@"
namespace Ns;
struct S
{
    public int X;
    public override string ToString() { return X.ToString(); }
}
internal class Program
{
    public static void Main(string[] args)
    {
        S s;
        s.X = 1;
        object o1 = s;
        s.X = 2;
        object o2 = s;
        Console.Write(o1.ToString());
        Console.Write(o2.ToString());
    }
}
", "12");

            // 155 unbox explicit cast copy
            RunTest(@"
namespace Ns;
struct S
{
    public int X;
}
internal class Program
{
    public static void Main(string[] args)
    {
        S s;
        s.X = 5;
        object o = s;
        S t = (S)o;
        t.X = 9;
        Console.Write(s.X);
        Console.Write(t.X);
    }
}
", "59");
            // 156 basic generic method type inference
            RunTest(@"
T Id<T>(T x) => x;
Console.Write(Id(7));
Console.Write(Id(""x""));
", "7x");
            // 157 different generic type parameters
            RunTest(@"
string Pair<TA, TB>(TA a, TB b)
{
    return a.ToString() + "":"" + b.ToString();
}
Console.Write(Pair(12, ""ab""));
", "12:ab");
            // 158 params array is fresh per call
            RunTest(@"
int Mutate(params int[] xs)
{
    if (xs.Length > 0)
        xs[0] = 99;
    return xs.Length;
}
int a = Mutate(1, 2);
int b = Mutate(3, 4, 5);
Console.Write(a);
Console.Write(b);
", "23");
            // 159 out argument target evaluated before call
            RunTest(@"
void M(out int x)
{
    x = 42;
}
int[] a = new int[] { 1, 2 };
int i = 0;
M(out a[i++]);
Console.Write(a[0]);
Console.Write(',');
Console.Write(a[1]);
Console.Write(',');
Console.Write(i);
", "42,2,1");
            // 160 ref argument aliasing same local
            RunTest(@"
void M(ref int a, ref int b)
{
    a += 1;
    b += 10;
}
int x = 0;
M(ref x, ref x);
Console.WriteLine(x);
", "11");
            // 161 local function captures by reference
            RunTest(@"
int x = 1;
int F() => x;
int a = F();
x = 9;
int b = F();
Console.Write(a);
Console.Write(b);
", "19");
            // 162 lambda captures mutated local
            RunTest(@"
int x = 1;
Func<int> f = () => x;
Console.Write(f());
x = 7;
Console.Write(f());
", "17");
            // 163 lambda captures independent invocation frames
            RunTest(@"
Func<int> Make(int x)
{
    return () => x;
}
var a = Make(1);
var b = Make(2);
Console.Write(a());
Console.Write(b());
", "12");
            // 164 delegate combines invocation order
            RunTest(@"
int x = 0;
Action a = () => x = x * 10 + 1;
a += () => x = x * 10 + 2;
a += () => x = x * 10 + 3;
a();
Console.WriteLine(x);
", "123");
            // 165 delegate remove last matching handler
            RunTest(@"
int x = 0;
Action h1 = () => x = x * 10 + 1;
Action h2 = () => x = x * 10 + 2;
Action a = h1;
a += h2;
a -= h2;
a();
Console.WriteLine(x);
", "1");
            // 166 List<T> remove and index shift
            RunTest(@"
var l = new List<int>();
l.Add(1);
l.Add(2);
l.Add(3);
l.RemoveAt(1);
Console.Write(l.Count);
Console.Write(l[0]);
Console.Write(l[1]);
", "213");
            // 167 List<T> foreach after mutation before enumeration
            RunTest(@"
var l = new List<int>();
l.Add(4);
l.Add(5);
l.Add(6);
int s = 0;
foreach (int x in l)
    s = s * 10 + x;
Console.WriteLine(s);
", "456");
            // 168 string concatenation mixed primitive
            RunTest(@"
int x = 12;
bool b = true;
Console.Write(""x="" + x + "",b="" + b);
", "x=12,b=True");
            // 169 string interpolation evaluates left to right
            RunTest(@"
int x = 0;
int Next() { x++; return x; }
Console.Write($""{Next()}-{Next()}-{x}"");
", "1-2-2");
            // 170 string interpolation with escaped braces
            RunTest(@"
int x = 7;
Console.Write($""{{{x}}}"");
", "{7}");
            // 171 verbatim string escape quotes
            RunTest(@"
string s = @""a""""b"";
Console.Write(s);
Console.Write(s.Length);
", "a\"b3");
            // 172 char escape values
            RunTest(@"
Console.Write((int)'\n');
Console.Write(',');
Console.Write((int)'\t');
Console.Write(',');
Console.Write((int)'\\');
", "10,9,92");
            // 173 switch default before cases
            RunTest(@"
int F(int x)
{
    switch (x)
    {
        default:
            return 9;
        case 1:
            return 1;
        case 2:
            return 2;
    }
}
Console.Write(F(1));
Console.Write(F(2));
Console.Write(F(3));
", "129");
            // 174 switch goto default from case
            RunTest(@"
int x = 0;
switch (1)
{
    case 1:
        x += 1;
        goto default;
    default:
        x += 10;
        break;
}
Console.WriteLine(x);
", "11");
            // 175 nested switch break only switch
            RunTest(@"
int s = 0;
for (int i = 0; i < 3; i++)
{
    switch (i)
    {
        case 1:
            break;
        default:
            s += 10;
            break;
    }
    s += i;
}
Console.WriteLine(s);
", "23");
            // 176 continue inside switch inside loop
            RunTest(@"
int s = 0;
for (int i = 0; i < 5; i++)
{
    switch (i)
    {
        case 1:
        case 3:
            continue;
    }
    s = s * 10 + i;
}
Console.WriteLine(s);
", "24");
            // 177 do while with continue still evaluates condition
            RunTest(@"
int i = 0;
int s = 0;
do
{
    i++;
    if (i < 3)
        continue;
    s += i;
}
while (i < 5);
Console.WriteLine(s);
", "12");
            // 178 try finally with return from method
            RunTest(@"
int F()
{
    try
    {
        return 1;
    }
    finally
    {
        Console.Write(2);
    }
}
Console.Write(F());
", "21");
            // 179 catch exact exception type
            RunTest(@"
try
{
    throw new InvalidOperationException();
}
catch (ArgumentException)
{
    Console.Write(1);
}
catch (InvalidOperationException)
{
    Console.Write(2);
}
", "2");
            // 180 catch base exception type
            RunTest(@"
try
{
    throw new InvalidOperationException();
}
catch (Exception)
{
    Console.Write(1);
}
", "1");
            // 181 finally runs before catch in outer frame
            RunTest(@"
try
{
    try
    {
        Console.Write(1);
        throw new InvalidOperationException();
    }
    finally
    {
        Console.Write(2);
    }
}
catch (Exception)
{
    Console.Write(3);
}
", "123");
            // 182 using disposes on exception
            RunTest(@"
namespace Ns;
class C : IDisposable
{
    public void Dispose()
    {
        Console.Write(2);
    }
}
internal class Program
{
    public static void Main(string[] args)
    {
        try
        {
            using (var c = new C())
            {
                Console.Write(1);
                throw new InvalidOperationException();
            }
        }
        catch (Exception)
        {
            Console.Write(3);
        }
    }
}
", "123");
            // 183 checked nested expression throws before assignment
            RunTest(@"
int x = 1;
try
{
    x = checked(2147483647 + 1);
}
catch (OverflowException)
{
    Console.Write(x);
}
", "1");
            // 184 unchecked nested expression wraps inside checked context
            RunTest(@"
try
{
    int x = checked(unchecked(2147483647 + 1));
    Console.Write(x == -2147483648);
}
catch (OverflowException)
{
    Console.Write(""bad"");
}
", "true");
            // 185 nullable default has no value
            RunTest(@"
Nullable<int> n = default(Nullable<int>);
Console.Write(n.HasValue);
", "false");
            // 186 nullable null assignment
            RunTest(@"
int? n = 5;
n = null;
Console.Write(n.HasValue);
", "false");
            // 187 tuple names preserved
            RunTest(@"
(int left, int right) a = (3, 4);
(int x, int y) b = a;
Console.Write(b.x);
Console.Write(b.y);
", "34");
            // 188 tuple assignment target evaluated before rhs
            RunTest(@"
int[] a = new int[] { 1, 2 };
int i = 0;
(a[i++], a[i++]) = (10, 20);
Console.Write(a[0]);
Console.Write(',');
Console.Write(a[1]);
Console.Write(',');
Console.Write(i);
", "10,20,2");
            // 189 tuple nested deconstruction with discard
            RunTest(@"
var t = ((1, 2), (3, 4));
int a;
int d;
((a, _), (_, d)) = t;
Console.Write(a);
Console.Write(d);
", "14");
            // 190 yield break
            RunTest(@"
IEnumerable<int> Gen()
{
    yield return 1;
    yield break;
    yield return 2;
}
foreach (int x in Gen())
    Console.Write(x);
", "1");
            // 191 iterator local state survives MoveNext
            RunTest(@"
IEnumerable<int> Gen()
{
    int x = 1;
    yield return x;
    x += 10;
    yield return x;
}
foreach (int x in Gen())
    Console.Write(x);
", "111");
            // 192 yield return stops after consumer break
            RunTest(@"
int produced = 0;
IEnumerable<int> Gen()
{
    for (int i = 0; i < 10; i++)
    {
        produced++;
        yield return i;
    }
}
foreach (int x in Gen())
{
    Console.Write(x);
    if (x == 2)
        break;
}
Console.Write("":"" + produced);
", "012:3");
            // 193 stackalloc indexing
            RunTest(@"
unsafe
{
    int* p = stackalloc int[3];
    p[0] = 10;
    p[1] = 20;
    p[2] = p[0] + p[1];
    Console.WriteLine(p[2]);
}
", "30");
            // 194 pointer arithmetic
            RunTest(@"
unsafe
{
    int* p = stackalloc int[3];
    *(p + 0) = 1;
    *(p + 1) = 2;
    *(p + 2) = 3;
    Console.Write(*(p + 0));
    Console.Write(*(p + 1));
    Console.Write(*(p + 2));
}
", "123");
            // 195 numeric casts sign extension
            RunTest(@"
sbyte a = -1;
short b = a;
int c = b;
long d = c;
Console.WriteLine(d);
", "-1");
            // 196 explicit narrowing signed
            RunTest(@"
int x = -1;
byte b = unchecked((byte)x);
Console.WriteLine(b);
", "255");
            // 197 float arithmetic
            RunTest(@"
float x = 1.5f;
float y = 2.25f;
Console.WriteLine(x + y == 3.75f);
", "true");
            // 198 double comparison with NaN
            RunTest(@"
double n = double.NaN;
Console.Write(n == n);
Console.Write(n != n);
", "falsetrue");
            // 199 Math.Min Max Abs
            RunTest(@"
Console.Write(Math.Min(3, 7));
Console.Write(Math.Max(3, 7));
Console.Write(Math.Abs(-5));
", "375");
            // 200 enum underlying values
            RunTest(@"
namespace Ns;
enum E
{
    A = 1,
    B = 5,
    C = 6
}
internal class Program
{
    public static void Main(string[] args)
    {
        E e = E.B;
        Console.WriteLine((int)e);
    }
}
", "5");
            // 201 enum default zero
            RunTest(@"
namespace Ns;
enum E
{
    A = 1
}
internal class Program
{
    public static void Main(string[] args)
    {
        E e = default(E);
        Console.WriteLine((int)e);
    }
}
", "0");
            // 202 enum bitwise operations
            RunTest(@"
namespace Ns;
enum E
{
    A = 1,
    B = 2,
    C = 4
}
internal class Program
{
    public static void Main(string[] args)
    {
        E e = E.A | E.C;
        Console.WriteLine((int)e);
    }
}
", "5");
            // 203 overload resolution params vs normal
            RunTest(@"
internal class Program
{
    static string F(int x) => ""normal"";
    static string F(params int[] x) => ""params"";
    public static void Main(string[] args)
    {
        Console.Write(F(1));
        Console.Write(',');
        Console.Write(F(1, 2));
    }
}
", "normal,params");
            // 204 overload resolution generic vs non-generic
            RunTest(@"
internal class Program
{
    static string F<T>(T x) => ""generic"";
    static string F(int x) => ""int"";
    public static void Main(string[] args)
    {
        Console.Write(F(1));
        Console.Write(',');
        Console.Write(F(""x""));
    }
}
", "int,generic");
            // 205 optional parameter default
            RunTest(@"
int F(int x, int y = 10)
{
    return x + y;
}
Console.Write(F(1));
Console.Write(',');
Console.Write(F(1, 2));
", "11,3");
            // 206 named arguments order
            RunTest(@"
int F(int a, int b, int c)
{
    return a * 100 + b * 10 + c;
}
Console.WriteLine(F(c: 3, a: 1, b: 2));
", "123");
            // 207 named and positional arguments
            RunTest(@"
int F(int a, int b, int c)
{
    return a * 100 + b * 10 + c;
}
Console.WriteLine(F(1, c: 3, b: 2));
", "123");
            // 208 inline array
            RunTest(@"
var buffer = new InlineArray10<int>();
buffer[3] = 3;
Console.Write(buffer[3]);
", "3");
            // 209 jagged array indexing
            RunTest(@"
class Program
{
    public static void Main(string[] args)
    {
        byte[][] slots = new byte[8][];

        for (int i = 0; i < 200; i++)
        {
            int index = i & 7;
            slots[index] = new byte[512];
            slots[index][0] = (byte)i;
        }

        int sum = 0;
        for (int i = 0; i < slots.Length; i++)
            sum += slots[i][0];

        Console.WriteLine(sum);
    }
}
", "1564");
            // 210 multiregister struct return ABI
            RunTest(@"
namespace Ns;

struct S
{
    public ulong A;
    public ushort B;
    public ushort C;
    public ushort D;

    public S(ulong a, ushort b, ushort c, ushort d)
    {
        A = a;
        B = b;
        C = c;
        D = d;
    }
}

internal class Program
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static S Pick(S[] data, int index)
    {
        S temp = data[index];
        return temp;
    }

    public static void Main(string[] args)
    {
        S marker = new S(100, 200, 300, 400);
        S[] data = new S[] { new S(1, 2, 3, 4), new S(5, 6, 7, 8) };

        S result = Pick(data, 1);

        Console.Write(result.A);
        Console.Write(',');
        Console.Write(result.B);
        Console.Write(',');
        Console.Write(result.C);
        Console.Write(',');
        Console.Write(result.D);
        Console.Write(',');
        Console.Write(marker.B);
    }
}
", "5,6,7,8,200");
            // 211 multiregister struct generic promotion
            RunTest(@"
namespace Ns;
struct User
{
    public ulong Id;
    public ushort Score1;
    public ushort Score2;
    public ushort Score3;
    public User(ulong id, ushort score1, ushort score2, ushort score3)
    {
        Id = id;
        Score1 = score1;
        Score2 = score2;
        Score3 = score3;
    }
}
internal class Program
{
    static T RemoveAtInPlace<T>(T[] data, ref int size, int idx)
    {
        T temp = data[idx];
        for (int i = idx; i < size - 1; i++)
            data[i] = data[i + 1];
        size--;
        return temp;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void InsertAtInPlace<T>(T[] data, ref int size, int idx, T value)
    {
        for (int i = size; i > idx; i--)
            data[i] = data[i - 1];
        data[idx] = value;
        size++;
    }

    public static void Main(string[] args)
    {
        User[] data = new User[] { new User(1, 1, 2, 3), new User(2, 4, 5, 6), new User(3, 7, 8, 9) };
        User newUser = new User(2, 10, 20, 30);
        int size = data.Length;
        User temp = RemoveAtInPlace<User>(data, ref size, 1);
        newUser.Score1 += temp.Score1;
        newUser.Score2 += temp.Score2;
        newUser.Score3 += temp.Score3;
        InsertAtInPlace<User>(data, ref size, 1, newUser);
        Console.Write(data[1].Id);
        Console.Write(',');
        Console.Write(data[1].Score1);
        Console.Write(',');
        Console.Write(data[1].Score2);
        Console.Write(',');
        Console.Write(data[1].Score3);
    }
}
", "2,14,25,36");
            // 212 multiregister struct return from array after loop
            RunTest(@"
namespace Ns;

struct Ret16
{
    public ulong A;
    public ushort B;
    public ushort C;
    public ushort D;

    public Ret16(ulong a, ushort b, ushort c, ushort d)
    {
        A = a;
        B = b;
        C = c;
        D = d;
    }
}

internal class Program
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int MakeSeed()
    {
        int[] data = new int[1];
        data[0] = 1;
        return data[0];
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static Ret16 PickAndAdjust(Ret16[] data, int seed)
    {
        int index = 0;

        for (int i = 0; i < data.Length; i++)
        {
            if (((int)data[i].B + seed) % 3 == 0)
                index = i;

            data[i].C = (ushort)(data[i].C + seed + i);
        }

        Ret16 result = data[index];
        result.D = (ushort)(result.D + data[0].C);
        return result;
    }

    public static void Main(string[] args)
    {
        int seed = MakeSeed();
        Ret16 guard = new Ret16(100, 99, 98, 97);
        Ret16[] data = new Ret16[]
        {
            new Ret16(10, 2, 3, 4),
            new Ret16(20, 5, 6, 7),
            new Ret16(30, 8, 9, 10),
            new Ret16(40, 11, 12, 13)
        };

        Ret16 r = PickAndAdjust(data, seed);

        Console.Write(r.A);
        Console.Write(',');
        Console.Write(r.B);
        Console.Write(',');
        Console.Write(r.C);
        Console.Write(',');
        Console.Write(r.D);
        Console.Write(',');
        Console.Write(guard.B);
    }
}
", "40,11,16,17,99");
            // 213 hidden buffer large struct return after field accumulation
            RunTest(@"
namespace Ns;

struct Large213
{
    public long A;
    public long B;
    public int C;
    public int D;
    public ushort E;
    public ushort F;

    public Large213(long a, long b, int c, int d, ushort e, ushort f)
    {
        A = a;
        B = b;
        C = c;
        D = d;
        E = e;
        F = f;
    }
}

internal class Program
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int MakeSeed()
    {
        int[] data = new int[1];
        data[0] = 1;
        return data[0];
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static Large213 FoldLarge(Large213[] data, int seed)
    {
        Large213 result = data[0];

        for (int i = 0; i < data.Length; i++)
        {
            if (((i + seed) & 1) == 0)
                result.A += data[i].B;
            else
                result.B += data[i].A;

            result.C += data[i].C + seed;
            result.D ^= data[i].D;
            result.E = (ushort)(result.E + data[i].E);
            result.F = (ushort)(result.F + i);
        }

        return result;
    }

    public static void Main(string[] args)
    {
        int seed = MakeSeed();
        Large213[] data = new Large213[]
        {
            new Large213(1, 2, 3, 4, 5, 6),
            new Large213(10, 20, 30, 40, 50, 60),
            new Large213(100, 200, 300, 400, 500, 600)
        };

        Large213 r = FoldLarge(data, seed);

        Console.Write(r.A);
        Console.Write(',');
        Console.Write(r.B);
        Console.Write(',');
        Console.Write(r.C);
        Console.Write(',');
        Console.Write(r.D);
        Console.Write(',');
        Console.Write(r.E);
        Console.Write(',');
        Console.Write(r.F);
    }
}
", "21,103,339,440,560,9");
            // 214 generic struct array rotation returns
            RunTest(@"
namespace Ns;

struct Ret214
{
    public ulong Id;
    public ushort A;
    public ushort B;
    public ushort C;

    public Ret214(ulong id, ushort a, ushort b, ushort c)
    {
        Id = id;
        A = a;
        B = b;
        C = c;
    }
}

internal class Program
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int MakeCount()
    {
        int[] data = new int[1];
        data[0] = 2;
        return data[0];
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static T RotateLeft<T>(T[] data, ref int size, int count)
    {
        for (int c = 0; c < count; c++)
        {
            T first = data[0];

            for (int i = 0; i < size - 1; i++)
                data[i] = data[i + 1];

            data[size - 1] = first;
        }

        return data[count % size];
    }

    public static void Main(string[] args)
    {
        int size = 4;
        int count = MakeCount();
        Ret214[] data = new Ret214[]
        {
            new Ret214(1, 2, 3, 4),
            new Ret214(2, 5, 6, 7),
            new Ret214(3, 8, 9, 10),
            new Ret214(4, 11, 12, 13)
        };

        Ret214 r = RotateLeft<Ret214>(data, ref size, count);

        Console.Write(data[0].Id);
        Console.Write(',');
        Console.Write(data[1].Id);
        Console.Write(',');
        Console.Write(r.Id);
        Console.Write(',');
        Console.Write(r.A);
        Console.Write(',');
        Console.Write(r.B);
        Console.Write(',');
        Console.Write(r.C);
        Console.Write(',');
        Console.Write(size);
    }
}
", "3,4,1,2,3,4,4");
            // 215 promoted struct materialized through ref call and safepoints
            RunTest(@"
namespace Ns;

struct Acc215
{
    public int A;
    public int B;
    public int C;
    public int D;

    public Acc215(int a, int b, int c, int d)
    {
        A = a;
        B = b;
        C = c;
        D = d;
    }
}

internal class Program
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int MakeSeed()
    {
        int[] data = new int[1];
        data[0] = 1;
        return data[0];
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Mutate(ref Acc215 p, int[] noise)
    {
        for (int i = 0; i < noise.Length; i++)
        {
            p.A += noise[i];
            p.B += p.A;
            p.C ^= noise[i];
            p.D += i;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static Acc215 Work(int seed)
    {
        Acc215 p = new Acc215(seed, seed + 1, seed + 2, seed + 3);
        int[] noise = new int[5];

        for (int i = 0; i < noise.Length; i++)
            noise[i] = i + seed;

        Mutate(ref p, noise);

        int[] forceSafepoint = new int[200];
        for (int i = 0; i < forceSafepoint.Length; i++)
            forceSafepoint[i] = i;

        p.D += forceSafepoint[199] - 199;
        return p;
    }

    public static void Main(string[] args)
    {
        Acc215 r = Work(MakeSeed());

        Console.Write(r.A);
        Console.Write(',');
        Console.Write(r.B);
        Console.Write(',');
        Console.Write(r.C);
        Console.Write(',');
        Console.Write(r.D);
    }
}
", "16,42,2,14");
            // 216 nested struct hiddenbuffer return copy
            RunTest(@"
namespace Ns;

struct Inner216
{
    public long A;
    public int B;

    public Inner216(long a, int b)
    {
        A = a;
        B = b;
    }
}

struct Outer216
{
    public Inner216 L;
    public Inner216 R;
    public int Tag;

    public Outer216(Inner216 l, Inner216 r, int tag)
    {
        L = l;
        R = r;
        Tag = tag;
    }
}

internal class Program
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static Outer216 Transform(Outer216[] data, int seed)
    {
        Outer216 o = data[seed % data.Length];

        for (int i = 0; i < data.Length; i++)
        {
            o.L.A += data[i].R.A;
            o.R.B += data[i].L.B;
            o.Tag += data[i].Tag;
        }

        data[0].L.A = 999;
        return o;
    }

    public static void Main(string[] args)
    {
        Outer216[] data = new Outer216[]
        {
            new Outer216(new Inner216(1, 2), new Inner216(3, 4), 5),
            new Outer216(new Inner216(10, 20), new Inner216(30, 40), 50)
        };

        Outer216 r = Transform(data, 1);

        Console.Write(r.L.A);
        Console.Write(',');
        Console.Write(r.R.B);
        Console.Write(',');
        Console.Write(r.Tag);
        Console.Write(',');
        Console.Write(data[0].L.A);
    }
}
", "43,62,105,999");


            Console.WriteLine($"Tests ran: {TestsRan}, tests failed {TestsFailed}");
            foreach (var msg in FailedMessages)
            {
                Console.WriteLine(msg);
            }
            Console.WriteLine($"Average instructions executed count: " +
                $"{(InstructionsExecuted.Count > 0 ? (long)InstructionsExecuted.Where(x => x > 0L).Average() : 0L)}" +
                $"\nPeak instructions executed count: {InstructionsExecuted.Max()} at i {InstructionsExecuted.IndexOf(InstructionsExecuted.Max())}");
            Console.WriteLine($"Average build time: " +
                $"{new TimeSpan((BuildTime.Count > 0 ? Convert.ToInt64(BuildTime.Average(x => x.Ticks)) : 0L))}");
            Console.WriteLine($"Average compilation time: " +
                $"{new TimeSpan((CompilationTime.Count > 0 ? Convert.ToInt64(CompilationTime.Average(x => x.Ticks)) : 0L))}");
        }
    }
}