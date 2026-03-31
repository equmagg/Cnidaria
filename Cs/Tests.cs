using System;
using System.Collections.Generic;
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
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
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
using System.Numerics;
var vec = new Vector3(2f, 3, 1f);
vec = vec * 2;
Console.WriteLine(vec.ToString());
", "<4, 6, 2>");
            // 56 List<T> Add / indexer / Count
            RunTest(@"
using System.Collections.Generic;
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
        int n = 100_000;
        int s = 0;
        for (int i = 0; i < n; i++)
        {
            s += (i * i - i) / (i + 1);
        }
        Console.WriteLine(s);
    }
}
", "752296015");

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
            

            Console.WriteLine($"Tests ran: {TestsRan}, tests failed {TestsFailed}");
            foreach (var msg in FailedMessages)
            {
                Console.WriteLine(msg);
            }
        }
    }
}