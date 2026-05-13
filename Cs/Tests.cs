using System;
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

        internal static bool Assert(string result, string target)
        {
            TestsRan++;
            if (string.IsNullOrWhiteSpace(result))
            {
                TestsFailed++;
                FailedMessages.Add($"Test {TestsRan} is empty");
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
                    output += $"{diagnostic.GetMessage()}\n";
                }
                Assert(output, target);
            }
        }
        internal static void RunAll()
        {
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
        int n = 10_000;
        int s = 0;
        for (int i = 0; i < n; i++)
        {
            s += (i * i - i) / (i + 1);
        }
        Console.WriteLine(s);
    }
}
", "49975003");

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

            Console.WriteLine($"Tests ran: {TestsRan}, tests failed {TestsFailed}");
            foreach (var msg in FailedMessages)
            {
                Console.WriteLine(msg);
            }
        }
    }
}