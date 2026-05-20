using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Cnidaria.C
{
    public enum DiagnosticSeverity : byte { Error, Warning, Message };
    public interface IDiagnostic
    {
        DiagnosticSeverity Severity { get; }
        string Message { get; }
        TextSpan Position { get; }
    }
    public readonly struct SyntaxDiagnostic : IDiagnostic
    {
        public DiagnosticSeverity Severity { get; }
        public string Message { get; }
        public TextSpan Position { get; }

        public SyntaxDiagnostic(DiagnosticSeverity severity, string? message, TextSpan position)
        {
            Severity = severity;
            Message = message ?? string.Empty;
            Position = position;
        }

        public SyntaxDiagnostic(string? message, TextSpan position)
            : this(DiagnosticSeverity.Error, message, position)
        {
        }

        public static SyntaxDiagnostic Error(string message, TextSpan position)
            => new SyntaxDiagnostic(DiagnosticSeverity.Error, message, position);

        public static SyntaxDiagnostic Warning(string message, TextSpan position)
            => new SyntaxDiagnostic(DiagnosticSeverity.Warning, message, position);

        public static SyntaxDiagnostic MessageInfo(string message, TextSpan position)
            => new SyntaxDiagnostic(DiagnosticSeverity.Message, message, position);
    }
    public readonly struct TextSpan
    {
        public readonly int Start;
        public readonly int Length;

        public int End => Start + Length;

        public TextSpan(int start, int length)
        {
            Start = start;
            Length = length;
        }

        public static TextSpan FromBounds(int start, int end)
            => new TextSpan(start, end - start);
    }

    public readonly struct SyntaxTrivia
    {
        public readonly SyntaxKind Kind;
        public readonly int Position;
        public readonly string Text;

        public SyntaxTrivia(SyntaxKind kind, int position, string text)
        {
            Kind = kind;
            Position = position;
            Text = text;
        }
    }

    public struct SyntaxToken
    {
        public readonly SyntaxKind Kind;
        public readonly int Position;
        public readonly string Text;
        public readonly object? Value;
        public readonly ImmutableArray<SyntaxTrivia> LeadingTrivia;
        public ImmutableArray<SyntaxTrivia> TrailingTrivia;

        public TextSpan Span => new TextSpan(Position, Text.Length);

        public SyntaxToken(
            SyntaxKind kind,
            int position,
            string text,
            object? value,
            ImmutableArray<SyntaxTrivia> leadingTrivia,
            ImmutableArray<SyntaxTrivia> trailingTrivia)
        {
            Kind = kind;
            Position = position;
            Text = text;
            Value = value;
            LeadingTrivia = leadingTrivia;
            TrailingTrivia = trailingTrivia;
        }
    }

    public sealed class TypeNameTable
    {
        private readonly Stack<Dictionary<string, bool>> _scopes = new();

        public TypeNameTable()
        {
            _scopes.Push(new Dictionary<string, bool>(StringComparer.Ordinal));
        }

        public void BeginScope()
        {
            _scopes.Push(new Dictionary<string, bool>(StringComparer.Ordinal));
        }

        public void EndScope()
        {
            if (_scopes.Count == 1)
                throw new InvalidOperationException("Cannot pop global typedef scope.");

            _scopes.Pop();
        }

        public void DeclareTypedef(string name)
        {
            Declare(name, isTypedefName: true);
        }

        public void DeclareOrdinaryIdentifier(string name)
        {
            Declare(name, isTypedefName: false);
        }

        private void Declare(string name, bool isTypedefName)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name cannot be empty.", nameof(name));

            _scopes.Peek()[name] = isTypedefName;
        }

        public bool IsTypeName(string name)
        {
            foreach (var scope in _scopes)
            {
                if (scope.TryGetValue(name, out var isTypedefName))
                    return isTypedefName;
            }

            return false;
        }
    }

    public sealed class PreprocessorEnvironment
    {
        public string OperatingSystem { get; }
        public string Architecture { get; }
        public IReadOnlyDictionary<string, string> ExtraPredefinedMacros { get; }
        public static PreprocessorEnvironment Default => new(null!, null!, null);
        public PreprocessorEnvironment(
            string operatingSystem,
            string architecture,
            IReadOnlyDictionary<string, string>? extraPredefinedMacros = null)
        {
            OperatingSystem = string.IsNullOrWhiteSpace(operatingSystem)
                ? "unknown"
                : operatingSystem.Trim().ToLowerInvariant();

            Architecture = string.IsNullOrWhiteSpace(architecture)
                ? "unknown"
                : architecture.Trim().ToLowerInvariant();

            ExtraPredefinedMacros = new ReadOnlyDictionary<string, string>(
                extraPredefinedMacros is null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(extraPredefinedMacros, StringComparer.Ordinal));
        }


        internal IReadOnlyDictionary<string, string> CreatePredefinedMacros()
        {
            var macros = new Dictionary<string, string>(StringComparer.Ordinal);

            switch (OperatingSystem)
            {
                case "windows":
                    macros["_WIN32"] = "1";
                    macros["__WIN32__"] = "1";
                    if (Architecture is "x64" or "arm64")
                        macros["_WIN64"] = "1";
                    break;

                case "linux":
                    macros["__linux__"] = "1";
                    macros["__linux"] = "1";
                    macros["linux"] = "1";
                    macros["__unix__"] = "1";
                    break;

                case "macos":
                case "darwin":
                    macros["__APPLE__"] = "1";
                    macros["__MACH__"] = "1";
                    break;

                case "freebsd":
                    macros["__FreeBSD__"] = "1";
                    macros["__unix__"] = "1";
                    break;

                case "unix":
                    macros["__unix__"] = "1";
                    break;
            }

            switch (Architecture)
            {
                case "x86":
                case "i386":
                    macros["__i386__"] = "1";
                    macros["_M_IX86"] = "600";
                    break;

                case "x64":
                case "x86_64":
                case "amd64":
                    macros["__x86_64__"] = "1";
                    macros["__amd64__"] = "1";
                    macros["_M_X64"] = "100";
                    macros["_M_AMD64"] = "100";
                    break;

                case "arm":
                    macros["__arm__"] = "1";
                    macros["_M_ARM"] = "7";
                    break;

                case "arm64":
                case "aarch64":
                    macros["__aarch64__"] = "1";
                    macros["_M_ARM64"] = "1";
                    break;

                case "wasm":
                case "wasm32":
                    macros["__wasm__"] = "1";
                    macros["__wasm32__"] = "1";
                    break;
            }

            foreach (var pair in ExtraPredefinedMacros)
                macros[pair.Key] = pair.Value;

            return new ReadOnlyDictionary<string, string>(macros);
        }
    }

    public sealed class IncludeDirective
    {
        public string Name { get; }
        public bool IsAngled { get; }
        public string? CurrentFilePath { get; }
        public IReadOnlyList<string> IncludeSearchPaths { get; }

        public IncludeDirective(
            string name,
            bool isAngled,
            string? currentFilePath,
            IReadOnlyList<string> includeSearchPaths)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            IsAngled = isAngled;
            CurrentFilePath = currentFilePath;
            IncludeSearchPaths = includeSearchPaths ?? ImmutableArray<string>.Empty;
        }
    }

    public readonly struct IncludeFile
    {
        public readonly string Path { get; }
        public readonly string Text { get; }

        public IncludeFile(string path, string text)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }
        public bool Exists => !string.IsNullOrEmpty(Path);
    }

    public interface IIncludeResolver
    {
        bool TryResolveInclude(IncludeDirective directive, out IncludeFile file);
    }

    public sealed class NullIncludeResolver : IIncludeResolver
    {
        public static readonly NullIncludeResolver Instance = new();

        private NullIncludeResolver()
        {
        }

        public bool TryResolveInclude(IncludeDirective directive, out IncludeFile file)
        {
            file = default;
            return false;
        }
    }

    public sealed class CompositeIncludeResolver : IIncludeResolver
    {
        private readonly ImmutableArray<IIncludeResolver> _resolvers;

        public CompositeIncludeResolver(IEnumerable<IIncludeResolver> resolvers)
        {
            if (resolvers is null)
                throw new ArgumentNullException(nameof(resolvers));

            _resolvers = resolvers
                .Where(static resolver => resolver is not null && resolver is not NullIncludeResolver)
                .Distinct()
                .ToImmutableArray();
        }

        public CompositeIncludeResolver(params IIncludeResolver[] resolvers)
            : this((IEnumerable<IIncludeResolver>)resolvers)
        {
        }

        public bool TryResolveInclude(IncludeDirective directive, out IncludeFile file)
        {
            foreach (var resolver in _resolvers)
            {
                if (resolver.TryResolveInclude(directive, out file))
                    return true;
            }

            file = default;
            return false;
        }
    }

    public sealed class FileSystemIncludeResolver : IIncludeResolver
    {
        private readonly int _maxBytes;

        public FileSystemIncludeResolver(int maxBytes = 4 * 1024 * 1024)
        {
            _maxBytes = maxBytes <= 0 ? 4 * 1024 * 1024 : maxBytes;
        }

        public bool TryResolveInclude(IncludeDirective directive, out IncludeFile file)
        {
            file = default;

            if (!IsSafeRelativeIncludeName(directive.Name))
                return false;

            foreach (var root in EnumerateRoots(directive))
            {
                if (TryReadUnderRoot(root, directive.Name, out file))
                    return true;
            }

            return false;
        }

        private static IEnumerable<string> EnumerateRoots(IncludeDirective directive)
        {
            if (!directive.IsAngled && directive.CurrentFilePath is not null)
            {
                var currentDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(directive.CurrentFilePath));
                if (!string.IsNullOrWhiteSpace(currentDirectory))
                    yield return currentDirectory;
            }

            foreach (var includeDirectory in directive.IncludeSearchPaths)
            {
                if (!string.IsNullOrWhiteSpace(includeDirectory))
                    yield return System.IO.Path.GetFullPath(includeDirectory);
            }
        }

        private bool TryReadUnderRoot(string root, string relativeName, out IncludeFile file)
        {
            file = default;

            try
            {
                var fullRoot = EnsureTrailingSeparator(System.IO.Path.GetFullPath(root));
                var candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(fullRoot, relativeName));

                if (!IsUnderRoot(candidate, fullRoot))
                    return false;

                var info = new FileInfo(candidate);
                if (!info.Exists || info.Length > _maxBytes)
                    return false;

                file = new IncludeFile(candidate, File.ReadAllText(candidate));
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        private static bool IsSafeRelativeIncludeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            if (name.IndexOf('\0') >= 0)
                return false;

            if (System.IO.Path.IsPathRooted(name))
                return false;

            return true;
        }

        private static bool IsUnderRoot(string candidate, string root)
        {
            var comparison = IsFileSystemCaseInsensitive
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            var fullRoot = EnsureTrailingSeparator(System.IO.Path.GetFullPath(root));
            var fullCandidate = System.IO.Path.GetFullPath(candidate);

            return fullCandidate.StartsWith(fullRoot, comparison);
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (path.EndsWith(System.IO.Path.DirectorySeparatorChar) ||
                path.EndsWith(System.IO.Path.AltDirectorySeparatorChar))
                return path;

            return path + System.IO.Path.DirectorySeparatorChar;
        }

        private static bool IsFileSystemCaseInsensitive
            => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
               RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    }

    public sealed class InMemoryIncludeResolver : IIncludeResolver
    {
        private readonly Dictionary<string, IncludeFile> _files = new(StringComparer.Ordinal);

        public InMemoryIncludeResolver()
        {
        }

        public InMemoryIncludeResolver(IEnumerable<IncludeFile> files)
        {
            if (files is null)
                throw new ArgumentNullException(nameof(files));

            foreach (var file in files)
                Add(file);
        }

        public InMemoryIncludeResolver(IReadOnlyDictionary<string, string> files)
        {
            if (files is null)
                throw new ArgumentNullException(nameof(files));

            foreach (var pair in files)
                Add(pair.Key, pair.Value);
        }

        public void Add(string path, string text)
            => Add(new IncludeFile(path, text));

        public void Add(IncludeFile file)
        {
            if (string.IsNullOrWhiteSpace(file.Path))
                throw new ArgumentException("An in-memory include file must have a non-empty path.", nameof(file));

            var normalizedPath = NormalizeVirtualPath(file.Path);
            _files[normalizedPath] = new IncludeFile(normalizedPath, file.Text ?? string.Empty);
        }

        public bool TryResolveInclude(IncludeDirective directive, out IncludeFile file)
        {
            if (directive is null)
                throw new ArgumentNullException(nameof(directive));

            if (TryGet(directive.Name, out file))
                return true;

            if (!directive.IsAngled && directive.CurrentFilePath is not null)
            {
                var currentDirectory = GetVirtualDirectoryName(directive.CurrentFilePath);
                if (!string.IsNullOrEmpty(currentDirectory) &&
                    TryGet(CombineVirtualPath(currentDirectory, directive.Name), out file))
                {
                    return true;
                }
            }

            foreach (var includeDirectory in directive.IncludeSearchPaths)
            {
                if (string.IsNullOrWhiteSpace(includeDirectory))
                    continue;

                if (TryGet(CombineVirtualPath(includeDirectory, directive.Name), out file))
                    return true;
            }

            file = default;
            return false;
        }

        private bool TryGet(string path, out IncludeFile file)
            => _files.TryGetValue(NormalizeVirtualPath(path), out file);

        private static string CombineVirtualPath(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left))
                return NormalizeVirtualPath(right);
            if (string.IsNullOrWhiteSpace(right))
                return NormalizeVirtualPath(left);

            return NormalizeVirtualPath(left.TrimEnd('/', '\\') + "/" + right.TrimStart('/', '\\'));
        }

        private static string GetVirtualDirectoryName(string path)
        {
            var normalized = NormalizeVirtualPath(path);
            var index = normalized.LastIndexOf('/');
            return index <= 0 ? string.Empty : normalized.Substring(0, index);
        }

        private static string NormalizeVirtualPath(string path)
        {
            if (path is null)
                throw new ArgumentNullException(nameof(path));

            var normalized = path.Replace('\\', '/').Trim();
            while (normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized.Substring(2);

            while (normalized.Contains("//", StringComparison.Ordinal))
                normalized = normalized.Replace("//", "/", StringComparison.Ordinal);

            return normalized;
        }
    }

    public sealed class PreprocessorOptions
    {
        public string? FilePath { get; }
        public PreprocessorEnvironment Environment { get; }
        public IReadOnlyDictionary<string, string> PredefinedMacros { get; }
        public IReadOnlyList<string> IncludeSearchPaths { get; }
        public IIncludeResolver IncludeResolver { get; }
        public int MaxIncludeDepth { get; }
        public int MaxInputLength { get; }
        public int MaxTokenLength { get; }
        public int MaxIncludeBytes { get; }
        public int MaxMacroExpansionDepth { get; }
        public int MaxMacroExpansionTokens { get; }

        public PreprocessorOptions(
            string? filePath = null,
            PreprocessorEnvironment? environment = null,
            IReadOnlyDictionary<string, string>? predefinedMacros = null,
            IEnumerable<string>? includeSearchPaths = null,
            IIncludeResolver? includeResolver = null,
            IEnumerable<IncludeFile>? includeFiles = null,
            int maxIncludeDepth = 200,
            int maxInputLength = 4 * 1024 * 1024,
            int maxTokenLength = 246 * 1024,
            int maxIncludeBytes = 1 * 1024 * 1024,
            int maxMacroExpansionDepth = 200,
            int maxMacroExpansionTokens = 1_000_000)
        {
            FilePath = filePath;
            Environment = environment ?? PreprocessorEnvironment.Default;
            IncludeSearchPaths = includeSearchPaths?.ToImmutableArray() ?? ImmutableArray<string>.Empty;
            IncludeResolver = CreateIncludeResolver(includeFiles, includeResolver);
            MaxIncludeDepth = maxIncludeDepth <= 0 ? 200 : maxIncludeDepth;
            MaxInputLength = maxInputLength <= 0 ? 16 * 1024 * 1024 : maxInputLength;
            MaxTokenLength = maxTokenLength <= 0 ? 1024 * 1024 : maxTokenLength;
            MaxIncludeBytes = maxIncludeBytes <= 0 ? 4 * 1024 * 1024 : maxIncludeBytes;
            MaxMacroExpansionDepth = maxMacroExpansionDepth <= 0 ? 200 : maxMacroExpansionDepth;
            MaxMacroExpansionTokens = maxMacroExpansionTokens <= 0 ? 1_000_000 : maxMacroExpansionTokens;

            var macros = new Dictionary<string, string>(Environment.CreatePredefinedMacros(), StringComparer.Ordinal);
            if (predefinedMacros is not null)
            {
                foreach (var pair in predefinedMacros)
                    macros[pair.Key] = pair.Value;
            }

            PredefinedMacros = new ReadOnlyDictionary<string, string>(macros);
        }

        public static PreprocessorOptions CreateDefault(string? filePath = null)
            => new PreprocessorOptions(filePath: filePath);

        public static PreprocessorOptions CreateForInMemoryFiles(
            string? filePath = null,
            IEnumerable<IncludeFile>? includeFiles = null,
            IEnumerable<string>? includeSearchPaths = null,
            IIncludeResolver? includeResolver = null,
            IReadOnlyDictionary<string, string>? predefinedMacros = null,
            PreprocessorEnvironment? environment = null)
        {
            return new PreprocessorOptions(
                filePath: filePath,
                environment: environment,
                predefinedMacros: predefinedMacros,
                includeSearchPaths: includeSearchPaths,
                includeFiles: includeFiles,
                includeResolver: includeResolver);
        }

        private static IIncludeResolver CreateIncludeResolver(
            IEnumerable<IncludeFile>? includeFiles,
            IIncludeResolver? includeResolver)
        {
            var memoryResolver = includeFiles is null
                ? null
                : new InMemoryIncludeResolver(includeFiles);

            if (memoryResolver is null)
                return includeResolver ?? NullIncludeResolver.Instance;

            if (includeResolver is null || includeResolver is NullIncludeResolver)
                return memoryResolver;

            return new CompositeIncludeResolver(memoryResolver, includeResolver);
        }
    }

    public static class SyntaxFacts
    {
        private static readonly Dictionary<string, SyntaxKind> s_punctuatorKinds = new(StringComparer.Ordinal)
        {
            ["("] = SyntaxKind.OpenParenToken,
            [")"] = SyntaxKind.CloseParenToken,

            ["{"] = SyntaxKind.OpenBraceToken,
            ["}"] = SyntaxKind.CloseBraceToken,

            ["["] = SyntaxKind.OpenBracketToken,
            ["]"] = SyntaxKind.CloseBracketToken,

            [";"] = SyntaxKind.SemicolonToken,
            [":"] = SyntaxKind.ColonToken,
            [","] = SyntaxKind.CommaToken,
            ["."] = SyntaxKind.DotToken,
            ["->"] = SyntaxKind.ArrowToken,
            ["?"] = SyntaxKind.QuestionToken,
            ["..."] = SyntaxKind.EllipsisToken,

            ["+"] = SyntaxKind.PlusToken,
            ["++"] = SyntaxKind.PlusPlusToken,
            ["+="] = SyntaxKind.PlusEqualsToken,

            ["-"] = SyntaxKind.MinusToken,
            ["--"] = SyntaxKind.MinusMinusToken,
            ["-="] = SyntaxKind.MinusEqualsToken,

            ["*"] = SyntaxKind.StarToken,
            ["*="] = SyntaxKind.StarEqualsToken,

            ["/"] = SyntaxKind.SlashToken,
            ["/="] = SyntaxKind.SlashEqualsToken,

            ["%"] = SyntaxKind.PercentToken,
            ["%="] = SyntaxKind.PercentEqualsToken,

            ["&"] = SyntaxKind.AmpersandToken,
            ["&&"] = SyntaxKind.AmpersandAmpersandToken,
            ["&="] = SyntaxKind.AmpersandEqualsToken,

            ["|"] = SyntaxKind.PipeToken,
            ["||"] = SyntaxKind.PipePipeToken,
            ["|="] = SyntaxKind.PipeEqualsToken,

            ["^"] = SyntaxKind.HatToken,
            ["^="] = SyntaxKind.HatEqualsToken,

            ["~"] = SyntaxKind.TildeToken,

            ["!"] = SyntaxKind.BangToken,
            ["!="] = SyntaxKind.BangEqualsToken,

            ["="] = SyntaxKind.EqualsToken,
            ["=="] = SyntaxKind.EqualsEqualsToken,

            ["<"] = SyntaxKind.LessThanToken,
            ["<="] = SyntaxKind.LessThanEqualsToken,
            ["<<"] = SyntaxKind.LessThanLessThanToken,
            ["<<="] = SyntaxKind.LessThanLessThanEqualsToken,

            [">"] = SyntaxKind.GreaterThanToken,
            [">="] = SyntaxKind.GreaterThanEqualsToken,
            [">>"] = SyntaxKind.GreaterThanGreaterThanToken,
            [">>="] = SyntaxKind.GreaterThanGreaterThanEqualsToken,

            ["#"] = SyntaxKind.HashToken,
            ["##"] = SyntaxKind.HashHashToken,

            ["\\"] = SyntaxKind.BackslashToken,

            ["<:"] = SyntaxKind.OpenBracketDigraphToken,
            [":>"] = SyntaxKind.CloseBracketDigraphToken,
            ["<%"] = SyntaxKind.OpenBraceDigraphToken,
            ["%>"] = SyntaxKind.CloseBraceDigraphToken,
            ["%:"] = SyntaxKind.HashDigraphToken,
            ["%:%:"] = SyntaxKind.HashHashDigraphToken,
        };

        private static readonly Dictionary<string, SyntaxKind> s_keywordKinds = new(StringComparer.Ordinal)
        {
            ["auto"] = SyntaxKind.AutoKeyword,
            ["break"] = SyntaxKind.BreakKeyword,
            ["case"] = SyntaxKind.CaseKeyword,
            ["char"] = SyntaxKind.CharKeyword,
            ["const"] = SyntaxKind.ConstKeyword,
            ["continue"] = SyntaxKind.ContinueKeyword,
            ["default"] = SyntaxKind.DefaultKeyword,
            ["do"] = SyntaxKind.DoKeyword,
            ["double"] = SyntaxKind.DoubleKeyword,
            ["else"] = SyntaxKind.ElseKeyword,
            ["enum"] = SyntaxKind.EnumKeyword,
            ["extern"] = SyntaxKind.ExternKeyword,
            ["float"] = SyntaxKind.FloatKeyword,
            ["for"] = SyntaxKind.ForKeyword,
            ["goto"] = SyntaxKind.GotoKeyword,
            ["if"] = SyntaxKind.IfKeyword,
            ["int"] = SyntaxKind.IntKeyword,
            ["long"] = SyntaxKind.LongKeyword,
            ["register"] = SyntaxKind.RegisterKeyword,
            ["return"] = SyntaxKind.ReturnKeyword,
            ["short"] = SyntaxKind.ShortKeyword,
            ["signed"] = SyntaxKind.SignedKeyword,
            ["sizeof"] = SyntaxKind.SizeofKeyword,
            ["static"] = SyntaxKind.StaticKeyword,
            ["struct"] = SyntaxKind.StructKeyword,
            ["switch"] = SyntaxKind.SwitchKeyword,
            ["typedef"] = SyntaxKind.TypedefKeyword,
            ["union"] = SyntaxKind.UnionKeyword,
            ["unsigned"] = SyntaxKind.UnsignedKeyword,
            ["void"] = SyntaxKind.VoidKeyword,
            ["volatile"] = SyntaxKind.VolatileKeyword,
            ["while"] = SyntaxKind.WhileKeyword,

            ["inline"] = SyntaxKind.InlineKeyword,
            ["restrict"] = SyntaxKind.RestrictKeyword,

            ["_Bool"] = SyntaxKind.UnderscoreBoolKeyword,
            ["_Complex"] = SyntaxKind.UnderscoreComplexKeyword,
            ["_Imaginary"] = SyntaxKind.UnderscoreImaginaryKeyword,
            ["_Pragma"] = SyntaxKind.UnderscorePragmaKeyword,

            ["_Alignas"] = SyntaxKind.UnderscoreAlignasKeyword,
            ["_Alignof"] = SyntaxKind.UnderscoreAlignofKeyword,
            ["_Atomic"] = SyntaxKind.UnderscoreAtomicKeyword,
            ["_Generic"] = SyntaxKind.UnderscoreGenericKeyword,
            ["_Noreturn"] = SyntaxKind.UnderscoreNoreturnKeyword,
            ["_Static_assert"] = SyntaxKind.UnderscoreStaticAssertKeyword,
            ["_Thread_local"] = SyntaxKind.UnderscoreThreadLocalKeyword,

            ["alignas"] = SyntaxKind.AlignasKeyword,
            ["alignof"] = SyntaxKind.AlignofKeyword,
            ["bool"] = SyntaxKind.BoolKeyword,
            ["constexpr"] = SyntaxKind.ConstexprKeyword,
            ["false"] = SyntaxKind.FalseKeyword,
            ["nullptr"] = SyntaxKind.NullptrKeyword,
            ["static_assert"] = SyntaxKind.StaticAssertKeyword,
            ["thread_local"] = SyntaxKind.ThreadLocalKeyword,
            ["true"] = SyntaxKind.TrueKeyword,
            ["typeof"] = SyntaxKind.TypeofKeyword,
            ["typeof_unqual"] = SyntaxKind.TypeofUnqualKeyword,

            ["_BitInt"] = SyntaxKind.UnderscoreBitIntKeyword,

            ["asm"] = SyntaxKind.AsmKeyword,
            ["fortran"] = SyntaxKind.FortranKeyword,

            ["_Decimal32"] = SyntaxKind.UnderscoreDecimal32Keyword,
            ["_Decimal64"] = SyntaxKind.UnderscoreDecimal64Keyword,
            ["_Decimal128"] = SyntaxKind.UnderscoreDecimal128Keyword,

            ["__extension__"] = SyntaxKind.ExtensionKeyword,
            ["__attribute__"] = SyntaxKind.AttributeKeyword,
            ["__declspec"] = SyntaxKind.DeclspecKeyword,

            ["__builtin_va_arg"] = SyntaxKind.BuiltinVaArgKeyword,
            ["__builtin_offsetof"] = SyntaxKind.BuiltinOffsetofKeyword,
            ["__builtin_types_compatible_p"] = SyntaxKind.BuiltinTypesCompatiblePKeyword,
            ["__builtin_choose_expr"] = SyntaxKind.BuiltinChooseExprKeyword,

            ["__asm"] = SyntaxKind.AsmExtensionKeyword,
            ["__asm__"] = SyntaxKind.AsmExtensionKeyword,

            ["__inline"] = SyntaxKind.InlineExtensionKeyword,
            ["__inline__"] = SyntaxKind.InlineExtensionKeyword,

            ["__restrict"] = SyntaxKind.RestrictExtensionKeyword,
            ["__restrict__"] = SyntaxKind.RestrictExtensionKeyword,

            ["__typeof"] = SyntaxKind.TypeofExtensionKeyword,
            ["__typeof__"] = SyntaxKind.TypeofExtensionKeyword,

            ["__volatile"] = SyntaxKind.VolatileExtensionKeyword,
            ["__volatile__"] = SyntaxKind.VolatileExtensionKeyword,

            ["__const"] = SyntaxKind.ConstExtensionKeyword,
            ["__const__"] = SyntaxKind.ConstExtensionKeyword,
        };

        public static readonly IReadOnlyDictionary<string, SyntaxKind> PunctuatorKinds = s_punctuatorKinds;
        public static readonly IReadOnlyDictionary<string, SyntaxKind> KeywordKinds = s_keywordKinds;

        public static SyntaxKind GetKeywordKind(string text)
        {
            if (s_keywordKinds.TryGetValue(text, out var kind))
                return kind;

            return SyntaxKind.IdentifierToken;
        }

        public static bool TryGetPunctuatorKind(string text, out SyntaxKind kind)
            => s_punctuatorKinds.TryGetValue(text, out kind);

        public static int MaxPunctuatorTextLength { get; } =
            s_punctuatorKinds.Keys.Max(static text => text.Length);
    }

    public sealed class Lexer
    {
        private sealed class MacroInfo
        {
            public readonly string Name;
            public readonly bool IsFunctionLike;
            public readonly ImmutableArray<string> Parameters;
            public readonly bool IsVariadic;
            public readonly string? VariadicParameterName;
            public readonly string ReplacementText;

            public MacroInfo(
                string name,
                bool isFunctionLike,
                ImmutableArray<string> parameters,
                bool isVariadic,
                string? variadicParameterName,
                string replacementText)
            {
                Name = name;
                IsFunctionLike = isFunctionLike;
                Parameters = parameters;
                IsVariadic = isVariadic;
                VariadicParameterName = variadicParameterName;
                ReplacementText = replacementText;
            }
        }

        private struct ConditionalFrame
        {
            public bool ParentActive;
            public bool IsActive;
            public bool AnyBranchTaken;
            public bool ElseSeen;
        }

        private readonly struct InputFrame
        {
            public readonly string Text;
            public readonly string? FilePath;
            public readonly int Position;
            public readonly bool AtStartOfLine;
            public readonly bool SeenBom;
            public readonly int ConditionalBaseDepth;

            public InputFrame(
                string text,
                string? filePath,
                int position,
                bool atStartOfLine,
                bool seenBom,
                int conditionalBaseDepth)
            {
                Text = text;
                FilePath = filePath;
                Position = position;
                AtStartOfLine = atStartOfLine;
                SeenBom = seenBom;
                ConditionalBaseDepth = conditionalBaseDepth;
            }
        }

        private enum LexerMode
        {
            Normal,
            Directive,
            IncludeDirective,
            DisabledText,
        }

        private string _text;
        private string? _filePath;
        private readonly TypeNameTable _typeNames;
        private readonly Dictionary<string, MacroInfo> _macros;
        private readonly Stack<ConditionalFrame> _conditionalStack = new();
        private readonly Stack<InputFrame> _inputStack = new();
        private readonly Queue<SyntaxToken> _pendingTokens = new();
        private readonly HashSet<string> _disabledMacroNames = new(StringComparer.Ordinal);
        private readonly List<SyntaxDiagnostic> _diagnostics = new();
        private readonly PreprocessorOptions _options;
        private int _macroExpansionDepth;
        private int _macroExpansionTokenCount;

        private int _position;
        private bool _atStartOfLine = true;
        private bool _seenBom;
        private int _conditionalBaseDepth;
        private LexerMode _mode = LexerMode.Normal;

        public Lexer(string text, TypeNameTable typeNames)
            : this(text, typeNames, PreprocessorOptions.CreateDefault())
        {
        }

        public Lexer(string text, TypeNameTable typeNames, PreprocessorOptions? options)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _typeNames = typeNames ?? throw new ArgumentNullException(nameof(typeNames));
            _options = options ?? PreprocessorOptions.CreateDefault();

            if (_text.Length > _options.MaxInputLength)
                throw new ArgumentException($"Input length {_text.Length} exceeds the configured lexer limit {_options.MaxInputLength}.", nameof(text));
            _filePath = _options.FilePath;
            _macros = new Dictionary<string, MacroInfo>(StringComparer.Ordinal);

            foreach (var pair in _options.PredefinedMacros)
                DefineObjectMacro(pair.Key, pair.Value);
        }

        public IReadOnlyList<SyntaxDiagnostic> Diagnostics => _diagnostics;

        public PreprocessorOptions Options => _options;

        public string? CurrentFilePath => _filePath;

        public void DefineObjectMacro(string name, string replacementText = "1")
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Macro name cannot be empty.", nameof(name));

            if (!IsIdentifierStart(name[0]) || name.Any(static ch => !IsIdentifierPart(ch)))
                throw new ArgumentException("Macro name must be a valid C identifier.", nameof(name));

            replacementText ??= string.Empty;
            if (replacementText.Length > _options.MaxInputLength)
                throw new ArgumentException($"Macro replacement text exceeds the configured lexer limit {_options.MaxInputLength}.", nameof(replacementText));

            _macros[name] = new MacroInfo(
                name,
                isFunctionLike: false,
                parameters: ImmutableArray<string>.Empty,
                isVariadic: false,
                variadicParameterName: null,
                replacementText: replacementText);
        }

        public bool UndefineMacro(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            return _macros.Remove(name);
        }

        public bool IsMacroDefined(string name)
            => name is not null && _macros.ContainsKey(name);

        private bool PushInput(string text, string? filePath)
        {
            if (_inputStack.Count >= _options.MaxIncludeDepth)
            {
                Error(_position, 0, $"Maximum include depth {_options.MaxIncludeDepth} exceeded while including '{filePath ?? "<memory>"}'.");
                return false;
            }

            text ??= string.Empty;
            if (text.Length > _options.MaxInputLength)
            {
                Error(_position, 0, $"Included input length {text.Length} exceeds the configured lexer limit {_options.MaxInputLength}.");
                return false;
            }

            _inputStack.Push(new InputFrame(
                _text,
                _filePath,
                _position,
                _atStartOfLine,
                _seenBom,
                _conditionalBaseDepth));

            _text = text;
            _filePath = filePath;
            _position = 0;
            _atStartOfLine = true;
            _seenBom = false;
            _conditionalBaseDepth = _conditionalStack.Count;
            _mode = LexerMode.Normal;
            return true;
        }

        private bool TryPopInput()
        {
            if (_conditionalStack.Count > _conditionalBaseDepth)
            {
                Error(_position, 0, $"Unterminated preprocessor conditional block in '{_filePath ?? "<input>"}'.");

                while (_conditionalStack.Count > _conditionalBaseDepth)
                    _conditionalStack.Pop();
            }

            if (_inputStack.Count == 0)
                return false;

            var frame = _inputStack.Pop();
            _text = frame.Text;
            _filePath = frame.FilePath;
            _position = frame.Position;
            _atStartOfLine = frame.AtStartOfLine;
            _seenBom = frame.SeenBom;
            _conditionalBaseDepth = frame.ConditionalBaseDepth;
            _mode = LexerMode.Normal;
            return true;
        }

        public SyntaxToken NextToken()
        {
            while (true)
            {
                if (_pendingTokens.Count != 0)
                    return _pendingTokens.Dequeue();

                var leadingTrivia = ReadTrivia(leading: true);

                if (Current == '\0')
                {
                    if (TryPopInput())
                        continue;

                    if (_conditionalStack.Count != 0)
                        Error(_position, 0, "Unterminated preprocessor conditional block.");

                    return new SyntaxToken(
                        SyntaxKind.EndOfFileToken,
                        _position,
                        string.Empty,
                        null,
                        leadingTrivia,
                        ImmutableArray<SyntaxTrivia>.Empty);
                }

                if (!IsCurrentlyActive)
                    continue;

                var token = LexToken(leadingTrivia);
                token.TrailingTrivia = ReadTrivia(leading: false);

                if (TryExpandMacro(token))
                    continue;

                return token;
            }
        }

        private bool IsCurrentlyActive
            => _conditionalStack.Count == 0 || _conditionalStack.Peek().IsActive;

        private bool TryExpandMacro(SyntaxToken token)
        {
            if (!IsMacroExpansionCandidate(token))
                return false;

            if (!_macros.TryGetValue(token.Text, out var macro))
                return false;

            if (_disabledMacroNames.Contains(macro.Name))
                return false;

            if (_macroExpansionDepth >= _options.MaxMacroExpansionDepth)
            {
                Error(token.Position, token.Text.Length, "Macro expansion depth limit exceeded.");
                return false;
            }

            if (macro.IsFunctionLike)
                return TryExpandFunctionLikeMacro(token, macro);

            ExpandObjectLikeMacro(token, macro);
            return true;
        }

        private bool IsMacroExpansionCandidate(SyntaxToken token)
        {
            if (token.Text.Length == 0)
                return false;

            if (!IsIdentifierStart(token.Text[0]))
                return false;

            for (var i = 1; i < token.Text.Length; i++)
            {
                if (!IsIdentifierPart(token.Text[i]))
                    return false;
            }

            return true;
        }

        private void ExpandObjectLikeMacro(SyntaxToken sourceToken, MacroInfo macro)
        {
            EnqueueMacroExpansionText(sourceToken, macro, macro.ReplacementText);
        }

        private bool TryExpandFunctionLikeMacro(SyntaxToken sourceToken, MacroInfo macro)
        {
            if (Current != '(')
                return false;

            var arguments = ReadMacroInvocationArguments(sourceToken, macro, out var argumentsAreWellFormed);
            if (!argumentsAreWellFormed)
                return true;

            if (!TryBuildMacroArgumentMap(sourceToken, macro, arguments, out var rawArguments, out var expandedArguments))
                return true;

            var replacementText = BuildFunctionLikeMacroReplacement(macro, rawArguments, expandedArguments);
            EnqueueMacroExpansionText(sourceToken, macro, replacementText);
            return true;
        }

        private ImmutableArray<string> ReadMacroInvocationArguments(
            SyntaxToken sourceToken,
            MacroInfo macro,
            out bool isWellFormed)
        {
            isWellFormed = false;

            if (Current != '(')
                return ImmutableArray<string>.Empty;

            Advance();

            var arguments = ImmutableArray.CreateBuilder<string>();
            var currentArgument = new System.Text.StringBuilder();
            var depth = 0;
            var couldBeEmptyArgumentList = MacroHasEmptyArgumentList(macro);

            while (Current != '\0')
            {
                if (depth == 0 && Current == ')' && couldBeEmptyArgumentList && IsNullOrWhiteSpace(currentArgument))
                {
                    Advance();
                    isWellFormed = true;
                    return arguments.ToImmutable();
                }

                if (couldBeEmptyArgumentList && char.IsWhiteSpace(Current))
                {
                    currentArgument.Append(Current);
                    Advance();
                    continue;
                }

                couldBeEmptyArgumentList = false;

                if (Current == ',' && depth == 0)
                {
                    arguments.Add(currentArgument.ToString());
                    currentArgument.Clear();
                    Advance();
                    continue;
                }

                if (Current == '(')
                {
                    depth++;
                    currentArgument.Append(Current);
                    Advance();
                    continue;
                }

                if (Current == ')')
                {
                    if (depth == 0)
                    {
                        arguments.Add(currentArgument.ToString());
                        Advance();
                        isWellFormed = true;
                        return arguments.ToImmutable();
                    }

                    depth--;
                    currentArgument.Append(Current);
                    Advance();
                    continue;
                }

                if (IsLineContinuationStart())
                {
                    var start = _position;
                    ReadLineContinuation();
                    currentArgument.Append(_text, start, _position - start);
                    continue;
                }

                if (Current == '/' && Peek(1) == '/')
                {
                    ReadRawSingleLineCommentAsSpace(currentArgument);
                    continue;
                }

                if (Current == '/' && Peek(1) == '*')
                {
                    ReadRawBlockCommentAsSpace(currentArgument, sourceToken.Position);
                    continue;
                }

                if (IsAtQuotedLiteralStart(out _, out _))
                {
                    ReadRawQuotedLiteral(currentArgument);
                    continue;
                }

                currentArgument.Append(Current);
                Advance();
            }

            Error(sourceToken.Position, sourceToken.Text.Length, $"Unterminated invocation of macro '{macro.Name}'.");
            return arguments.ToImmutable();
        }

        private static bool MacroHasEmptyArgumentList(MacroInfo macro)
        {
            if (!macro.IsFunctionLike)
                return false;

            if (!macro.IsVariadic)
                return macro.Parameters.Length == 0;

            return GetFixedParameterCount(macro) == 0;
        }

        private static int GetFixedParameterCount(MacroInfo macro)
        {
            if (!macro.IsVariadic)
                return macro.Parameters.Length;

            if (macro.VariadicParameterName is not null && macro.Parameters.Contains(macro.VariadicParameterName))
                return macro.Parameters.Length - 1;

            return macro.Parameters.Length;
        }

        private bool TryBuildMacroArgumentMap(
            SyntaxToken sourceToken,
            MacroInfo macro,
            ImmutableArray<string> arguments,
            out Dictionary<string, string> rawArguments,
            out Dictionary<string, string> expandedArguments)
        {
            rawArguments = new Dictionary<string, string>(StringComparer.Ordinal);
            expandedArguments = new Dictionary<string, string>(StringComparer.Ordinal);

            var fixedParameterCount = GetFixedParameterCount(macro);

            if (!macro.IsVariadic && arguments.Length != fixedParameterCount)
            {
                Error(sourceToken.Position, sourceToken.Text.Length, $"Macro '{macro.Name}' expects {fixedParameterCount} argument(s), got {arguments.Length}.");
                return false;
            }

            if (macro.IsVariadic && arguments.Length < fixedParameterCount)
            {
                Error(sourceToken.Position, sourceToken.Text.Length, $"Macro '{macro.Name}' expects at least {fixedParameterCount} argument(s), got {arguments.Length}.");
                return false;
            }

            for (var i = 0; i < fixedParameterCount; i++)
            {
                var parameter = macro.Parameters[i];
                var raw = i < arguments.Length ? arguments[i] : string.Empty;
                rawArguments[parameter] = raw;
                expandedArguments[parameter] = ExpandMacroArgumentToText(raw, macro.Name, sourceToken.Position);
            }

            if (macro.IsVariadic)
            {
                var variadicRaw = JoinMacroArguments(arguments, fixedParameterCount);
                var variadicExpanded = ExpandMacroArgumentToText(variadicRaw, macro.Name, sourceToken.Position);

                rawArguments["__VA_ARGS__"] = variadicRaw;
                expandedArguments["__VA_ARGS__"] = variadicExpanded;

                if (macro.VariadicParameterName is not null)
                {
                    rawArguments[macro.VariadicParameterName] = variadicRaw;
                    expandedArguments[macro.VariadicParameterName] = variadicExpanded;
                }
            }

            return true;
        }

        private string BuildFunctionLikeMacroReplacement(
            MacroInfo macro,
            Dictionary<string, string> rawArguments,
            Dictionary<string, string> expandedArguments)
        {
            const char pasteMarker = '\uE000';

            var builder = new System.Text.StringBuilder(macro.ReplacementText.Length);
            var text = macro.ReplacementText;
            var index = 0;
            var skipHorizontalWhitespace = false;

            while (index < text.Length)
            {
                if (skipHorizontalWhitespace && (text[index] is ' ' or '\t' or '\v' or '\f'))
                {
                    index++;
                    continue;
                }

                skipHorizontalWhitespace = false;

                if (StartsWith(text, index, "##") || StartsWith(text, index, "%:%:"))
                {
                    TrimTrailingHorizontalWhitespace(builder);
                    builder.Append(pasteMarker);
                    index += text[index] == '#' ? 2 : 4;
                    skipHorizontalWhitespace = true;
                    continue;
                }

                if ((text[index] == '#' || StartsWith(text, index, "%:")) && !StartsWith(text, index, "##") && !StartsWith(text, index, "%:%:"))
                {
                    var hashLength = text[index] == '#' ? 1 : 2;
                    var afterHash = SkipHorizontalWhitespace(text, index + hashLength);

                    if (TryReadIdentifier(text, afterHash, out var parameterName, out var afterIdentifier) &&
                        rawArguments.TryGetValue(parameterName, out var rawArgument))
                    {
                        builder.Append(StringifyMacroArgument(rawArgument));
                        index = afterIdentifier;
                        continue;
                    }
                }

                if (IsAtQuotedLiteralStart(text, index, out _, out _))
                {
                    AppendRawQuotedLiteral(text, ref index, builder);
                    continue;
                }

                if (IsIdentifierStart(text[index]))
                {
                    var identifierStart = index;
                    index++;

                    while (index < text.Length && IsIdentifierPart(text[index]))
                        index++;

                    var identifier = text[identifierStart..index];
                    if (expandedArguments.TryGetValue(identifier, out var replacement))
                    {
                        builder.Append(replacement);
                        continue;
                    }

                    builder.Append(identifier);
                    continue;
                }

                builder.Append(text[index]);
                index++;
            }

            return builder.ToString().Replace(pasteMarker.ToString(), string.Empty, StringComparison.Ordinal);
        }

        private void EnqueueMacroExpansionText(SyntaxToken sourceToken, MacroInfo macro, string replacementText)
        {
            if (replacementText.Length == 0)
                return;

            if (replacementText.Length > _options.MaxInputLength)
            {
                Error(sourceToken.Position, sourceToken.Text.Length, "Macro replacement text exceeds the configured input limit.");
                return;
            }

            var expansionOptions = CreateMacroExpansionOptions();
            var nested = new Lexer(replacementText, _typeNames, expansionOptions);
            nested._macros.Clear();

            foreach (var pair in _macros)
                nested._macros[pair.Key] = pair.Value;

            foreach (var name in _disabledMacroNames)
                nested._disabledMacroNames.Add(name);

            nested._disabledMacroNames.Add(macro.Name);
            nested._macroExpansionDepth = _macroExpansionDepth + 1;
            nested._macroExpansionTokenCount = _macroExpansionTokenCount;

            _disabledMacroNames.Add(macro.Name);
            _macroExpansionDepth++;

            try
            {
                while (true)
                {
                    if (++_macroExpansionTokenCount > _options.MaxMacroExpansionTokens)
                    {
                        Error(sourceToken.Position, sourceToken.Text.Length, "Macro expansion token limit exceeded.");
                        break;
                    }

                    var token = nested.NextToken();
                    if (token.Kind == SyntaxKind.EndOfFileToken)
                        break;

                    _pendingTokens.Enqueue(token);
                }

                foreach (var diagnostic in nested.Diagnostics)
                    _diagnostics.Add(diagnostic);
            }
            finally
            {
                _macroExpansionDepth--;
                _disabledMacroNames.Remove(macro.Name);
            }
        }

        private string ExpandMacroArgumentToText(string text, string disabledMacroName, int diagnosticPosition)
            => MacroExpandTextToString(text, disabledMacroName, diagnosticPosition);

        private string MacroExpandTextToString(string text, string? disabledMacroName, int diagnosticPosition)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var expansionOptions = CreateMacroExpansionOptions();
            var nested = new Lexer(text, _typeNames, expansionOptions);
            nested._macros.Clear();

            foreach (var pair in _macros)
                nested._macros[pair.Key] = pair.Value;

            foreach (var name in _disabledMacroNames)
                nested._disabledMacroNames.Add(name);

            if (disabledMacroName is not null)
                nested._disabledMacroNames.Add(disabledMacroName);

            nested._macroExpansionDepth = _macroExpansionDepth + 1;
            nested._macroExpansionTokenCount = _macroExpansionTokenCount;

            var builder = new System.Text.StringBuilder(text.Length);

            while (true)
            {
                if (++_macroExpansionTokenCount > _options.MaxMacroExpansionTokens)
                {
                    Error(diagnosticPosition, 0, "Macro expansion token limit exceeded.");
                    break;
                }

                var token = nested.NextToken();
                if (token.Kind == SyntaxKind.EndOfFileToken)
                    break;

                AppendTriviaText(builder, token.LeadingTrivia);
                builder.Append(token.Text);
                AppendTriviaText(builder, token.TrailingTrivia);
            }

            foreach (var diagnostic in nested.Diagnostics)
                _diagnostics.Add(diagnostic);

            return builder.ToString();
        }

        private static void AppendTriviaText(System.Text.StringBuilder builder, ImmutableArray<SyntaxTrivia> trivia)
        {
            foreach (var item in trivia)
                builder.Append(item.Text);
        }

        private static string JoinMacroArguments(ImmutableArray<string> arguments, int start)
        {
            if (start >= arguments.Length)
                return string.Empty;

            var builder = new System.Text.StringBuilder();
            for (var i = start; i < arguments.Length; i++)
            {
                if (i > start)
                    builder.Append(", ");

                builder.Append(arguments[i]);
            }

            return builder.ToString();
        }

        private static bool IsNullOrWhiteSpace(System.Text.StringBuilder builder)
        {
            for (var i = 0; i < builder.Length; i++)
            {
                if (!char.IsWhiteSpace(builder[i]))
                    return false;
            }

            return true;
        }

        private static void TrimTrailingHorizontalWhitespace(System.Text.StringBuilder builder)
        {
            while (builder.Length > 0 && (builder[^1] is ' ' or '\t' or '\v' or '\f'))
                builder.Length--;
        }

        private static string StringifyMacroArgument(string text)
        {
            var normalized = CollapseMacroArgumentWhitespace(text.Trim());
            var builder = new System.Text.StringBuilder(normalized.Length + 2);

            builder.Append('"');
            foreach (var ch in normalized)
            {
                if (ch is '\\' or '"')
                    builder.Append('\\');

                builder.Append(ch);
            }

            builder.Append('"');
            return builder.ToString();
        }

        private static string CollapseMacroArgumentWhitespace(string text)
        {
            var builder = new System.Text.StringBuilder(text.Length);
            var inWhitespace = false;

            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch))
                {
                    inWhitespace = true;
                    continue;
                }

                if (inWhitespace && builder.Length > 0)
                    builder.Append(' ');

                builder.Append(ch);
                inWhitespace = false;
            }

            return builder.ToString();
        }

        private void ReadRawSingleLineCommentAsSpace(System.Text.StringBuilder builder)
        {
            builder.Append(' ');
            Advance();
            Advance();

            while (Current != '\0')
            {
                if (IsLineContinuationStart())
                {
                    ReadLineContinuation();
                    continue;
                }

                if (IsLineBreak(Current))
                    break;

                Advance();
            }
        }

        private void ReadRawBlockCommentAsSpace(System.Text.StringBuilder builder, int diagnosticPosition)
        {
            builder.Append(' ');
            Advance();
            Advance();

            var terminated = false;

            while (Current != '\0')
            {
                if (Current == '*' && Peek(1) == '/')
                {
                    Advance();
                    Advance();
                    terminated = true;
                    break;
                }

                Advance();
            }

            if (!terminated)
                Error(diagnosticPosition, 0, "Unterminated block comment in macro invocation.");
        }

        private bool IsAtQuotedLiteralStart(out int prefixLength, out char quote)
            => IsAtQuotedLiteralStart(_text, _position, out prefixLength, out quote);

        private static bool IsAtQuotedLiteralStart(string text, int index, out int prefixLength, out char quote)
        {
            prefixLength = 0;
            quote = '\0';

            if (index >= text.Length)
                return false;

            if (text[index] is '"' or '\'')
            {
                quote = text[index];
                return true;
            }

            if (index + 1 < text.Length && (text[index] is 'L' or 'u' or 'U') && (text[index + 1] is '"' or '\''))
            {
                prefixLength = 1;
                quote = text[index + 1];
                return true;
            }

            if (index + 2 < text.Length && text[index] == 'u' && text[index + 1] == '8' && (text[index + 2] is '"' or '\''))
            {
                prefixLength = 2;
                quote = text[index + 2];
                return true;
            }

            return false;
        }

        private void ReadRawQuotedLiteral(System.Text.StringBuilder builder)
        {
            var index = _position;
            AppendRawQuotedLiteral(_text, ref index, builder);
            _position = index;
        }

        private static void AppendRawQuotedLiteral(string text, ref int index, System.Text.StringBuilder builder)
        {
            if (!IsAtQuotedLiteralStart(text, index, out var prefixLength, out var quote))
                return;

            for (var i = 0; i < prefixLength; i++)
                builder.Append(text[index++]);

            builder.Append(text[index++]);

            while (index < text.Length)
            {
                var ch = text[index++];
                builder.Append(ch);

                if (ch == '\\' && index < text.Length)
                {
                    builder.Append(text[index++]);
                    continue;
                }

                if (ch == quote)
                    break;
            }
        }

        private static bool TryReadIdentifier(string text, int index, out string identifier, out int end)
        {
            identifier = string.Empty;
            end = index;

            if (index >= text.Length || !IsIdentifierStart(text[index]))
                return false;

            var start = index;
            index++;

            while (index < text.Length && IsIdentifierPart(text[index]))
                index++;

            identifier = text[start..index];
            end = index;
            return true;
        }

        private PreprocessorOptions CreateMacroExpansionOptions()
            => new PreprocessorOptions(
                filePath: _filePath,
                environment: new PreprocessorEnvironment("unknown", "unknown"),
                predefinedMacros: ImmutableDictionary<string, string>.Empty,
                includeSearchPaths: ImmutableArray<string>.Empty,
                includeResolver: NullIncludeResolver.Instance,
                maxIncludeDepth: _options.MaxIncludeDepth,
                maxInputLength: _options.MaxInputLength,
                maxTokenLength: _options.MaxTokenLength,
                maxIncludeBytes: _options.MaxIncludeBytes,
                maxMacroExpansionDepth: _options.MaxMacroExpansionDepth,
                maxMacroExpansionTokens: _options.MaxMacroExpansionTokens);

        private SyntaxToken LexToken(ImmutableArray<SyntaxTrivia> leadingTrivia)
        {
            var prefixedLiteral = TryLexPrefixedLiteral(leadingTrivia);
            if (prefixedLiteral.HasValue)
                return prefixedLiteral.Value;

            if (IsIdentifierStart(Current) || IsUniversalCharacterNameStart())
                return LexIdentifierOrKeyword(leadingTrivia);

            if (char.IsDigit(Current) || (Current == '.' && char.IsDigit(Peek(1))))
                return LexNumber(leadingTrivia);

            if (Current == '"')
                return LexStringLiteral(leadingTrivia, SyntaxKind.StringLiteralToken, prefixLength: 0);

            if (Current == '\'')
                return LexCharacterLiteral(leadingTrivia, SyntaxKind.CharacterLiteralToken, prefixLength: 0);

            return LexPunctuatorOrBadToken(leadingTrivia);
        }

        private SyntaxToken? TryLexPrefixedLiteral(ImmutableArray<SyntaxTrivia> leadingTrivia)
        {
            if (Current == 'L')
            {
                if (Peek(1) == '"')
                    return LexStringLiteral(leadingTrivia, SyntaxKind.WideStringLiteralToken, prefixLength: 1);

                if (Peek(1) == '\'')
                    return LexCharacterLiteral(leadingTrivia, SyntaxKind.WideCharacterLiteralToken, prefixLength: 1);
            }

            if (Current == 'u' && Peek(1) == '8')
            {
                if (Peek(2) == '"')
                    return LexStringLiteral(leadingTrivia, SyntaxKind.Utf8StringLiteralToken, prefixLength: 2);

                if (Peek(2) == '\'')
                    return LexCharacterLiteral(leadingTrivia, SyntaxKind.Utf8CharacterLiteralToken, prefixLength: 2);
            }

            if (Current == 'u')
            {
                if (Peek(1) == '"')
                    return LexStringLiteral(leadingTrivia, SyntaxKind.Utf16StringLiteralToken, prefixLength: 1);

                if (Peek(1) == '\'')
                    return LexCharacterLiteral(leadingTrivia, SyntaxKind.Utf16CharacterLiteralToken, prefixLength: 1);
            }

            if (Current == 'U')
            {
                if (Peek(1) == '"')
                    return LexStringLiteral(leadingTrivia, SyntaxKind.Utf32StringLiteralToken, prefixLength: 1);

                if (Peek(1) == '\'')
                    return LexCharacterLiteral(leadingTrivia, SyntaxKind.Utf32CharacterLiteralToken, prefixLength: 1);
            }

            return null;
        }

        private SyntaxToken LexIdentifierOrKeyword(ImmutableArray<SyntaxTrivia> leadingTrivia)
        {
            var start = _position;

            while (IsIdentifierPart(Current) || IsUniversalCharacterNameStart())
            {
                if (IsUniversalCharacterNameStart())
                    ReadUniversalCharacterName();
                else
                    Advance();
            }

            var text = _text[start.._position];

            var keywordKind = SyntaxFacts.GetKeywordKind(text);
            if (keywordKind != SyntaxKind.IdentifierToken)
                return MakeToken(keywordKind, start, text, null, leadingTrivia);

            var kind = _typeNames.IsTypeName(text)
                ? SyntaxKind.TypedefNameToken
                : SyntaxKind.IdentifierToken;

            return MakeToken(kind, start, text, text, leadingTrivia);
        }

        private SyntaxToken LexNumber(ImmutableArray<SyntaxTrivia> leadingTrivia)
        {
            var start = _position;

            ScanPreprocessingNumber();

            var text = _text[start.._position];
            var kind = LooksLikeFloatingLiteral(text)
                ? SyntaxKind.FloatingLiteralToken
                : SyntaxKind.IntegerLiteralToken;

            ValidateNumericLiteral(text, start, kind);

            return MakeToken(kind, start, text, text, leadingTrivia);
        }

        private void ScanPreprocessingNumber()
        {
            if (Current == '.')
                Advance();

            while (Current != '\0')
            {
                if (IsIdentifierPart(Current) || Current == '.')
                {
                    Advance();
                    continue;
                }

                if ((Current is '+' or '-') && (Peek(-1) is 'e' or 'E' or 'p' or 'P'))
                {
                    Advance();
                    continue;
                }

                if (Current == '\'' && IsIdentifierPart(Peek(1)))
                {
                    Advance();
                    continue;
                }

                if (IsUniversalCharacterNameStart())
                {
                    ReadUniversalCharacterName();
                    continue;
                }

                break;
            }
        }

        private void ValidateNumericLiteral(string text, int start, SyntaxKind kind)
        {
            if (text.Length > _options.MaxTokenLength)
                Error(start, text.Length, $"Numeric literal length exceeds the configured limit {_options.MaxTokenLength}.");

            if (text.Length == 0)
                return;

            if (kind == SyntaxKind.FloatingLiteralToken)
            {
                ValidateFloatingLiteral(text, start);
                return;
            }

            ValidateIntegerLiteral(text, start);
        }

        private void ValidateIntegerLiteral(string text, int start)
        {
            var body = StripIntegerSuffix(text, out _);

            if (body.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                var digits = body[2..].Replace("'", string.Empty);
                if (digits.Length == 0 || digits.Any(static ch => !IsHexDigit(ch)))
                    Error(start, text.Length, "Invalid hexadecimal integer literal.");

                return;
            }

            if (body.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                var digits = body[2..].Replace("'", string.Empty);
                if (digits.Length == 0 || digits.Any(static ch => ch is not ('0' or '1')))
                    Error(start, text.Length, "Invalid binary integer literal.");

                return;
            }

            if (body.Length > 1 && body[0] == '0')
            {
                var digits = body.Replace("'", string.Empty);
                foreach (var ch in digits)
                {
                    if (ch < '0' || ch > '7')
                    {
                        Error(start, text.Length, "Invalid octal integer literal.");
                        return;
                    }
                }
            }
        }

        private void ValidateFloatingLiteral(string text, int start)
        {
            var lower = text.ToLowerInvariant();

            if (lower.StartsWith("0x", StringComparison.Ordinal))
            {
                var p = lower.IndexOf('p');
                if (p < 0)
                {
                    Error(start, text.Length, "Hexadecimal floating literal requires a binary exponent.");
                    return;
                }

                var exponent = StripFloatingSuffix(lower[(p + 1)..].TrimStart('+', '-'));
                if (exponent.Length == 0 || exponent.Any(static ch => !char.IsDigit(ch)))
                    Error(start, text.Length, "Expected exponent digits in hexadecimal floating literal.");

                return;
            }

            var e = lower.IndexOf('e');
            if (e >= 0)
            {
                var exponent = StripFloatingSuffix(lower[(e + 1)..].TrimStart('+', '-'));
                if (exponent.Length == 0 || exponent.Any(static ch => !char.IsDigit(ch)))
                    Error(start, text.Length, "Expected exponent digits in floating literal.");
            }
        }

        private static bool LooksLikeFloatingLiteral(string text)
        {
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return text.IndexOf('.') >= 0 || text.IndexOf('p') >= 0 || text.IndexOf('P') >= 0;

            return text.IndexOf('.') >= 0 ||
                   text.IndexOf('e') >= 0 ||
                   text.IndexOf('E') >= 0;
        }

        private static string StripIntegerSuffix(string text, out string suffix)
        {
            var index = text.Length;

            while (index > 0 && text[index - 1] is 'u' or 'U' or 'l' or 'L' or 'w' or 'W' or 'b' or 'B')
                index--;

            suffix = text[index..];
            return text[..index];
        }

        private static string StripFloatingSuffix(string text)
        {
            if (text.Length == 0)
                return text;

            return text[^1] is 'f' or 'F' or 'l' or 'L' or 'd' or 'D'
                ? text[..^1]
                : text;
        }

        private void ReadDigits(Func<char, bool> isDigit)
        {
            while (true)
            {
                if (isDigit(Current))
                {
                    Advance();
                    continue;
                }

                if (Current == '\'' && isDigit(Peek(1)))
                {
                    Advance();
                    Advance();
                    continue;
                }

                break;
            }
        }
        private SyntaxToken LexStringLiteral(
            ImmutableArray<SyntaxTrivia> leadingTrivia,
            SyntaxKind kind,
            int prefixLength)
        {
            return LexQuotedLiteral(leadingTrivia, kind, prefixLength, quote: '"');
        }

        private SyntaxToken LexCharacterLiteral(
            ImmutableArray<SyntaxTrivia> leadingTrivia,
            SyntaxKind kind,
            int prefixLength)
        {
            return LexQuotedLiteral(leadingTrivia, kind, prefixLength, quote: '\'');
        }

        private SyntaxToken LexQuotedLiteral(
            ImmutableArray<SyntaxTrivia> leadingTrivia,
            SyntaxKind kind,
            int prefixLength,
            char quote)
        {
            var start = _position;

            for (var i = 0; i < prefixLength; i++)
                Advance();

            Advance();

            var terminated = false;

            while (Current != '\0')
            {
                if (IsLineContinuationStart())
                {
                    ReadLineContinuation();
                    continue;
                }

                if (Current == '\\')
                {
                    ReadEscapeSequence();
                    continue;
                }

                if (Current == quote)
                {
                    Advance();
                    terminated = true;
                    break;
                }

                if (IsLineBreak(Current))
                    break;

                Advance();
            }

            var text = _text[start.._position];

            if (!terminated)
                Error(start, _position - start, $"Unterminated {(quote == '\'' ? "character" : "string")} literal.");

            var value = terminated
                ? DecodeQuotedLiteralValue(text, prefixLength, quote, start)
                : text;

            return MakeToken(
                terminated ? kind : SyntaxKind.BadToken,
                start,
                text,
                value,
                leadingTrivia);
        }

        private object DecodeQuotedLiteralValue(string text, int prefixLength, char quote, int diagnosticStart)
        {
            var start = prefixLength + 1;
            var end = Math.Max(start, text.Length - 1);
            var builder = new System.Text.StringBuilder(Math.Max(0, end - start));

            for (var index = start; index < end;)
            {
                var ch = text[index++];
                if (ch != '\\')
                {
                    builder.Append(ch);
                    continue;
                }

                if (index >= end)
                {
                    builder.Append('\\');
                    break;
                }

                if (IsEscapedNewLine(text, index, out var newlineLength))
                {
                    index += newlineLength;
                    continue;
                }

                var escapePosition = diagnosticStart + index - 1;
                var escape = text[index++];
                switch (escape)
                {
                    case '\\': builder.Append('\\'); break;
                    case '\'': builder.Append('\''); break;
                    case '"': builder.Append('"'); break;
                    case '?': builder.Append('?'); break;
                    case 'a': builder.Append('\a'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'v': builder.Append('\v'); break;

                    case >= '0' and <= '7':
                        {
                            var value = escape - '0';
                            var digits = 1;
                            while (digits < 3 && index < end && text[index] is >= '0' and <= '7')
                            {
                                value = (value << 3) + (text[index] - '0');
                                index++;
                                digits++;
                            }
                            builder.Append(unchecked((char)value));
                            break;
                        }

                    case 'x':
                        {
                            if (index >= end || !IsHexDigit(text[index]))
                            {
                                Error(escapePosition, Math.Min(2, text.Length - (index - 1)), "Expected hexadecimal digits after \\x escape.");
                                builder.Append('x');
                                break;
                            }

                            var value = 0;
                            while (index < end && IsHexDigit(text[index]))
                            {
                                value = unchecked((value << 4) + (int)HexValue(text[index]));
                                index++;
                            }
                            builder.Append(unchecked((char)value));
                            break;
                        }

                    case 'u':
                    case 'U':
                        {
                            var digitCount = escape == 'u' ? 4 : 8;
                            if (TryReadFixedHexScalar(text, ref index, end, digitCount, out var scalar))
                                AppendUnicodeScalar(builder, scalar, escapePosition, digitCount + 2);
                            else
                            {
                                Error(escapePosition, Math.Min(digitCount + 2, text.Length - (index - 1)), $"Invalid universal character name \\{escape} escape.");
                                builder.Append(escape);
                            }
                            break;
                        }

                    default:
                        Warning(escapePosition, Math.Min(2, text.Length - (index - 1)), "Unknown escape sequence.");
                        builder.Append(escape);
                        break;
                }
            }

            if (quote == '\'')
            {
                if (builder.Length == 0)
                {
                    Error(diagnosticStart, text.Length, "Empty character literal.");
                    return '\0';
                }

                if (builder.Length == 1)
                    return builder[0];

                var value = 0;
                foreach (var ch in builder.ToString())
                    value = unchecked((value << 8) | (byte)ch);
                return value;
            }

            return builder.ToString();
        }

        private static bool IsEscapedNewLine(string text, int index, out int length)
        {
            length = 0;
            if (index >= text.Length)
                return false;

            if (text[index] == '\r')
            {
                length = index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
                return true;
            }

            if (text[index] == '\n')
            {
                length = 1;
                return true;
            }

            return false;
        }

        private static bool TryReadFixedHexScalar(string text, ref int index, int end, int digitCount, out int scalar)
        {
            scalar = 0;
            if (index + digitCount > end)
                return false;

            for (var i = 0; i < digitCount; i++)
            {
                var ch = text[index + i];
                if (!IsHexDigit(ch))
                    return false;
                scalar = unchecked((scalar << 4) + (int)HexValue(ch));
            }

            index += digitCount;
            return true;
        }

        private void AppendUnicodeScalar(System.Text.StringBuilder builder, int scalar, int diagnosticStart, int diagnosticLength)
        {
            if (scalar < 0 || scalar > 0x10FFFF || scalar is >= 0xD800 and <= 0xDFFF)
            {
                Error(diagnosticStart, diagnosticLength, "Universal character name is not a valid Unicode scalar value.");
                builder.Append('?');
                return;
            }

            if (scalar <= 0xFFFF)
                builder.Append((char)scalar);
            else
                builder.Append(char.ConvertFromUtf32(scalar));
        }

        private void ReadEscapeSequence()
        {
            var start = _position;

            if (Current != '\\')
                return;

            Advance();

            if (Current == '\0')
            {
                Error(start, _position - start, "Incomplete escape sequence.");
                return;
            }

            switch (Current)
            {
                case '\\':
                case '\'':
                case '"':
                case '?':
                case 'a':
                case 'b':
                case 'f':
                case 'n':
                case 'r':
                case 't':
                case 'v':
                    Advance();
                    return;

                case >= '0' and <= '7':
                    for (var i = 0; i < 3 && Current is >= '0' and <= '7'; i++)
                        Advance();
                    return;

                case 'x':
                    Advance();
                    if (!IsHexDigit(Current))
                    {
                        Error(start, _position - start, "Expected hexadecimal digits after \\x escape.");
                        return;
                    }

                    while (IsHexDigit(Current))
                        Advance();
                    return;

                case 'u':
                case 'U':
                    _position = start;
                    ReadUniversalCharacterName();
                    return;

                default:
                    Warning(start, Math.Min(2, _text.Length - start), "Unknown escape sequence.");
                    Advance();
                    return;
            }
        }

        private SyntaxToken LexPunctuatorOrBadToken(ImmutableArray<SyntaxTrivia> leadingTrivia)
        {
            var remaining = _text.Length - _position;
            var maxLength = Math.Min(SyntaxFacts.MaxPunctuatorTextLength, remaining);

            for (var length = maxLength; length >= 1; length--)
            {
                var text = _text.Substring(_position, length);

                if (SyntaxFacts.TryGetPunctuatorKind(text, out var kind))
                    return MakeFixedToken(kind, leadingTrivia, length);
            }

            return MakeBadToken(leadingTrivia);
        }

        private ImmutableArray<SyntaxTrivia> ReadTrivia(bool leading)
        {
            var trivia = ImmutableArray.CreateBuilder<SyntaxTrivia>();

            while (true)
            {
                var start = _position;

                if (Current == '\0')
                    break;

                if (leading && !_seenBom && _position == 0 && Current == '\uFEFF')
                {
                    AdvancePreservingLineStart();
                    _seenBom = true;

                    trivia.Add(new SyntaxTrivia(
                        SyntaxKind.BomTrivia,
                        start,
                        _text[start.._position]));

                    continue;
                }

                if (leading && _atStartOfLine && IsDirectiveIntroducer())
                {
                    ReadDirectiveTrivia(trivia);
                    continue;
                }

                if (Current is ' ' or '\t' or '\v' or '\f')
                {
                    while (Current is ' ' or '\t' or '\v' or '\f')
                        AdvancePreservingLineStart();

                    trivia.Add(new SyntaxTrivia(
                        SyntaxKind.WhitespaceTrivia,
                        start,
                        _text[start.._position]));

                    continue;
                }

                if (leading && !IsCurrentlyActive)
                {
                    if (ReadDisabledTextTrivia(trivia))
                        continue;
                }

                if (IsLineContinuationStart())
                {
                    ReadLineContinuation();

                    trivia.Add(new SyntaxTrivia(
                        SyntaxKind.LineContinuationTrivia,
                        start,
                        _text[start.._position]));

                    continue;
                }

                if (IsLineBreak(Current))
                {
                    ReadLineBreak();

                    trivia.Add(new SyntaxTrivia(
                        SyntaxKind.EndOfLineTrivia,
                        start,
                        _text[start.._position]));

                    _atStartOfLine = true;
                    continue;
                }

                if (Current == '/' && Peek(1) == '/')
                {
                    Advance();
                    Advance();

                    while (Current != '\0')
                    {
                        if (IsLineContinuationStart())
                        {
                            ReadLineContinuation();
                            continue;
                        }

                        if (IsLineBreak(Current))
                            break;

                        Advance();
                    }

                    trivia.Add(new SyntaxTrivia(
                        SyntaxKind.SingleLineCommentTrivia,
                        start,
                        _text[start.._position]));

                    continue;
                }

                if (Current == '/' && Peek(1) == '*')
                {
                    Advance();
                    Advance();

                    var terminated = false;

                    while (Current != '\0')
                    {
                        if (Current == '*' && Peek(1) == '/')
                        {
                            Advance();
                            Advance();
                            terminated = true;
                            break;
                        }

                        if (IsLineContinuationStart())
                        {
                            ReadLineContinuation();
                            continue;
                        }

                        if (IsLineBreak(Current))
                        {
                            ReadLineBreak();
                            _atStartOfLine = true;
                            continue;
                        }

                        Advance();
                    }

                    if (!terminated)
                        Error(start, _position - start, "Unterminated block comment.");

                    trivia.Add(new SyntaxTrivia(
                        SyntaxKind.MultiLineCommentTrivia,
                        start,
                        _text[start.._position]));

                    continue;
                }

                break;
            }

            return trivia.ToImmutable();
        }

        private bool ReadDisabledTextTrivia(ImmutableArray<SyntaxTrivia>.Builder trivia)
        {
            var previousMode = _mode;
            _mode = LexerMode.DisabledText;
            var start = _position;

            while (Current != '\0')
            {
                if (_atStartOfLine && IsDirectiveIntroducer())
                    break;

                if (IsLineContinuationStart())
                {
                    ReadLineContinuation();
                    continue;
                }

                if (IsLineBreak(Current))
                {
                    ReadLineBreak();
                    _atStartOfLine = true;
                    continue;
                }

                if (Current is ' ' or '\t' or '\v' or '\f')
                {
                    AdvancePreservingLineStart();
                    continue;
                }

                Advance();
                _atStartOfLine = false;
            }

            if (_position == start)
            {
                _mode = previousMode;
                return false;
            }

            trivia.Add(new SyntaxTrivia(
                SyntaxKind.DisabledTextTrivia,
                start,
                _text[start.._position]));

            _mode = previousMode;
            return true;
        }

        private void ReadDirectiveTrivia(ImmutableArray<SyntaxTrivia>.Builder trivia)
        {
            var previousMode = _mode;
            _mode = LexerMode.Directive;
            var start = _position;

            if (Current == '#' && Peek(1) == '#')
            {
                _mode = previousMode;
                return;
            }

            if (Current == '%' && Peek(1) == ':' && Peek(2) == '%' && Peek(3) == ':')
            {
                _mode = previousMode;
                return;
            }

            while (Current != '\0')
            {
                if (IsLineContinuationStart())
                {
                    ReadLineContinuation();
                    continue;
                }

                if (IsLineBreak(Current))
                    break;

                Advance();
            }

            var text = _text[start.._position];
            var pushedInput = ProcessDirectiveText(text, start);

            trivia.Add(new SyntaxTrivia(
                SyntaxKind.DirectiveTrivia,
                start,
                text));

            if (!pushedInput)
            {
                _atStartOfLine = false;
                _mode = previousMode;
            }
            else
            {
                _mode = LexerMode.Normal;
            }
        }

        private bool ProcessDirectiveText(string rawText, int position)
        {
            var text = RemoveCommentsPreservingLiterals(RemoveLineContinuations(rawText));
            var index = 0;

            if (index < text.Length && text[index] == '#')
                index++;
            else if (index + 1 < text.Length && text[index] == '%' && text[index + 1] == ':')
                index += 2;
            else
                return false;

            index = SkipHorizontalWhitespace(text, index);

            var directiveStart = index;
            if (index >= text.Length || !IsIdentifierStart(text[index]))
                return false;

            index++;
            while (index < text.Length && IsIdentifierPart(text[index]))
                index++;

            var directiveText = text[directiveStart..index];
            var rest = text[index..];

            switch (directiveText)
            {
                case "define":
                    ProcessDefineDirective(rest, position);
                    return false;

                case "undef":
                    ProcessUndefDirective(rest);
                    return false;

                case "include":
                case "include_next":
                case "import":
                    return ProcessIncludeDirective(rest, position, directiveText);

                case "if":
                    PushConditionalFrame(EvaluateIfExpression(rest) != 0);
                    return false;

                case "ifdef":
                    PushConditionalFrame(IsMacroDefined(ReadDirectiveIdentifier(rest)));
                    return false;

                case "ifndef":
                    PushConditionalFrame(!IsMacroDefined(ReadDirectiveIdentifier(rest)));
                    return false;

                case "elif":
                    UpdateElifFrame(EvaluateIfExpression(rest) != 0, position);
                    return false;

                case "elifdef":
                    UpdateElifFrame(IsMacroDefined(ReadDirectiveIdentifier(rest)), position);
                    return false;

                case "elifndef":
                    UpdateElifFrame(!IsMacroDefined(ReadDirectiveIdentifier(rest)), position);
                    return false;

                case "else":
                    UpdateElseFrame(position);
                    return false;

                case "endif":
                    PopConditionalFrame(position);
                    return false;

                case "error":
                    if (IsCurrentlyActive)
                        Error(position, rest.Length, $"#error: {rest.Trim()}");
                    return false;

                case "warning":
                    if (IsCurrentlyActive)
                        Warning(position, rest.Length, $"#warning: {rest.Trim()}");
                    return false;

                case "line":
                case "pragma":
                    return false;

                case "embed":
                    if (IsCurrentlyActive)
                        Error(position, rest.Length, "#embed is not implemented.");
                    return false;

                default:
                    return false;
            }
        }

        private bool ProcessIncludeDirective(string rest, int position, string directiveName)
        {
            if (!IsCurrentlyActive)
                return false;

            var previousMode = _mode;
            _mode = LexerMode.IncludeDirective;

            try
            {
                if (!TryParseIncludeName(rest, out var includeName, out var isAngled))
                {
                    var expandedRest = MacroExpandTextToString(rest, disabledMacroName: null, position);
                    if (!TryParseIncludeName(expandedRest, out includeName, out isAngled))
                    {
                        Error(position, rest.Length, $"Unsupported or malformed #{directiveName} directive.");
                        return false;
                    }
                }

                var directive = new IncludeDirective(
                    includeName,
                    isAngled,
                    _filePath,
                    _options.IncludeSearchPaths);

                if (!_options.IncludeResolver.TryResolveInclude(directive, out var includeFile))
                {
                    Error(position, rest.Length, $"Cannot resolve include '{includeName}'.");
                    return false;
                }

                if (includeFile.Text.Length > _options.MaxIncludeBytes)
                {
                    Error(position, rest.Length, $"Include '{includeName}' exceeds the configured include size limit {_options.MaxIncludeBytes}.");
                    return false;
                }

                return PushInput(includeFile.Text, includeFile.Path);
            }
            finally
            {
                if (_mode == LexerMode.IncludeDirective)
                    _mode = previousMode;
            }
        }

        private bool HasInclude(string includeName, bool isAngled)
        {
            var directive = new IncludeDirective(
                includeName,
                isAngled,
                _filePath,
                _options.IncludeSearchPaths);

            return _options.IncludeResolver.TryResolveInclude(directive, out _);
        }

        private static bool TryParseIncludeName(string text, out string includeName, out bool isAngled)
        {
            includeName = string.Empty;
            isAngled = false;

            var index = SkipHorizontalWhitespace(text, 0);
            if (index >= text.Length)
                return false;

            if (text[index] == '"')
            {
                var start = ++index;
                while (index < text.Length && text[index] != '"')
                    index++;

                if (index >= text.Length)
                    return false;

                includeName = text[start..index];
                isAngled = false;
                return includeName.Length != 0;
            }

            if (text[index] == '<')
            {
                var start = ++index;
                while (index < text.Length && text[index] != '>')
                    index++;

                if (index >= text.Length)
                    return false;

                includeName = text[start..index];
                isAngled = true;
                return includeName.Length != 0;
            }

            return false;
        }

        private void ProcessDefineDirective(string rest, int position)
        {
            if (!IsCurrentlyActive)
                return;

            var index = SkipHorizontalWhitespace(rest, 0);
            if (index >= rest.Length || !IsIdentifierStart(rest[index]))
            {
                Error(position, rest.Length, "Malformed #define directive.");
                return;
            }

            var nameStart = index;
            index++;
            while (index < rest.Length && IsIdentifierPart(rest[index]))
                index++;

            var name = rest[nameStart..index];
            var isFunctionLike = false;
            var parameters = ImmutableArray<string>.Empty;
            var isVariadic = false;
            string? variadicParameterName = null;

            if (index < rest.Length && rest[index] == '(')
            {
                isFunctionLike = true;
                index++;

                var parsedParameters = ImmutableArray.CreateBuilder<string>();

                while (true)
                {
                    index = SkipHorizontalWhitespace(rest, index);

                    if (index >= rest.Length)
                    {
                        Error(position, rest.Length, $"Unterminated macro parameter list for {name}.");
                        return;
                    }

                    if (rest[index] == ')')
                    {
                        index++;
                        break;
                    }

                    if (StartsWith(rest, index, "..."))
                    {
                        isVariadic = true;
                        variadicParameterName = "__VA_ARGS__";
                        index += 3;
                        index = SkipHorizontalWhitespace(rest, index);

                        if (index < rest.Length && rest[index] == ')')
                        {
                            index++;
                            break;
                        }

                        Error(position, rest.Length, $"Variadic marker must be the last parameter of macro {name}.");
                        return;
                    }

                    if (!IsIdentifierStart(rest[index]))
                    {
                        Error(position, rest.Length, $"Invalid macro parameter in {name}.");
                        return;
                    }

                    var parameterStart = index;
                    index++;
                    while (index < rest.Length && IsIdentifierPart(rest[index]))
                        index++;

                    var parameter = rest[parameterStart..index];
                    if (parsedParameters.Contains(parameter))
                    {
                        Error(position, rest.Length, $"Duplicate macro parameter '{parameter}' in {name}.");
                        return;
                    }

                    parsedParameters.Add(parameter);

                    index = SkipHorizontalWhitespace(rest, index);

                    if (StartsWith(rest, index, "..."))
                    {
                        isVariadic = true;
                        variadicParameterName = parameter;
                        index += 3;
                        index = SkipHorizontalWhitespace(rest, index);

                        if (index < rest.Length && rest[index] == ')')
                        {
                            index++;
                            break;
                        }

                        Error(position, rest.Length, $"Variadic marker must be the last parameter of macro {name}.");
                        return;
                    }

                    if (index < rest.Length && rest[index] == ',')
                    {
                        index++;
                        continue;
                    }

                    if (index < rest.Length && rest[index] == ')')
                    {
                        index++;
                        break;
                    }

                    Error(position, rest.Length, $"Expected ',' or ')' in macro parameter list for {name}.");
                    return;
                }

                parameters = parsedParameters.ToImmutable();
            }

            var replacementText = rest[index..].TrimStart(' ', '\t', '\v', '\f');

            _macros[name] = new MacroInfo(
                name,
                isFunctionLike,
                parameters,
                isVariadic,
                variadicParameterName,
                replacementText);
        }

        private void ProcessUndefDirective(string rest)
        {
            if (!IsCurrentlyActive)
                return;

            var name = ReadDirectiveIdentifier(rest);
            if (name.Length != 0)
                _macros.Remove(name);
        }

        private long EvaluateIfExpression(string text)
        {
            var parser = new IfExpressionParser(text, _macros, HasInclude);
            return parser.Parse();
        }

        private void PushConditionalFrame(bool condition)
        {
            var parentActive = IsCurrentlyActive;
            var isActive = parentActive && condition;

            _conditionalStack.Push(new ConditionalFrame
            {
                ParentActive = parentActive,
                IsActive = isActive,
                AnyBranchTaken = isActive,
                ElseSeen = false,
            });
        }

        private void UpdateElifFrame(bool condition, int position)
        {
            if (_conditionalStack.Count == 0)
            {
                Error(position, 0, "#elif without matching #if.");
                return;
            }

            var frame = _conditionalStack.Pop();

            if (frame.ElseSeen)
            {
                Error(position, 0, "#elif after #else.");
                frame.IsActive = false;
                _conditionalStack.Push(frame);
                return;
            }

            var isActive = frame.ParentActive && !frame.AnyBranchTaken && condition;
            frame.IsActive = isActive;
            frame.AnyBranchTaken |= isActive;

            _conditionalStack.Push(frame);
        }

        private void UpdateElseFrame(int position)
        {
            if (_conditionalStack.Count == 0)
            {
                Error(position, 0, "#else without matching #if.");
                return;
            }

            var frame = _conditionalStack.Pop();

            if (frame.ElseSeen)
            {
                Error(position, 0, "Duplicate #else.");
                frame.IsActive = false;
                _conditionalStack.Push(frame);
                return;
            }

            frame.ElseSeen = true;
            frame.IsActive = frame.ParentActive && !frame.AnyBranchTaken;
            frame.AnyBranchTaken |= frame.IsActive;

            _conditionalStack.Push(frame);
        }

        private void PopConditionalFrame(int position)
        {
            if (_conditionalStack.Count == 0)
            {
                Error(position, 0, "#endif without matching #if.");
                return;
            }

            _conditionalStack.Pop();
        }

        private void Report(DiagnosticSeverity severity, int start, int length, string message)
        {
            if (start < 0)
                start = 0;

            if (length < 0)
                length = 0;

            _diagnostics.Add(new SyntaxDiagnostic(
                severity,
                message,
                new TextSpan(start, length)));
        }

        private void Error(int start, int length, string message)
            => Report(DiagnosticSeverity.Error, start, length, message);

        private void Warning(int start, int length, string message)
            => Report(DiagnosticSeverity.Warning, start, length, message);

        private SyntaxToken MakeFixedToken(
            SyntaxKind kind,
            ImmutableArray<SyntaxTrivia> leadingTrivia,
            int length)
        {
            var start = _position;
            var text = _text.Substring(start, length);

            for (var i = 0; i < length; i++)
                Advance();

            return MakeToken(kind, start, text, null, leadingTrivia);
        }

        private SyntaxToken MakeBadToken(ImmutableArray<SyntaxTrivia> leadingTrivia)
        {
            var start = _position;
            var text = _text.Substring(start, 1);

            Advance();
            Error(start, text.Length, $"Unexpected character '{text}'.");

            return MakeToken(SyntaxKind.BadToken, start, text, null, leadingTrivia);
        }

        private SyntaxToken MakeToken(
            SyntaxKind kind,
            int position,
            string text,
            object? value,
            ImmutableArray<SyntaxTrivia> leadingTrivia)
        {
            if (text.Length > _options.MaxTokenLength)
                Error(position, text.Length, $"Token length exceeds the configured limit {_options.MaxTokenLength}.");

            _atStartOfLine = false;

            return new SyntaxToken(
                kind,
                position,
                text,
                value,
                leadingTrivia,
                ImmutableArray<SyntaxTrivia>.Empty);
        }

        private char Current => Peek(0);

        private char Peek(int offset)
        {
            var index = _position + offset;

            if (index < 0 || index >= _text.Length)
                return '\0';

            return _text[index];
        }

        private void Advance()
        {
            if (_position < _text.Length)
                _position++;
        }

        private void AdvancePreservingLineStart()
        {
            Advance();
        }

        private void ReadLineContinuation()
        {
            Advance();
            ReadLineBreak();
        }

        private void ReadLineBreak()
        {
            if (Current == '\r' && Peek(1) == '\n')
            {
                Advance();
                Advance();
                return;
            }

            Advance();
        }

        private bool IsDirectiveIntroducer()
        {
            if (Current == '#')
                return Peek(1) != '#';

            if (Current == '%' && Peek(1) == ':')
                return !(Peek(2) == '%' && Peek(3) == ':');

            return false;
        }

        private bool IsLineContinuationStart()
            => Current == '\\' && IsLineBreak(Peek(1));

        private bool IsUniversalCharacterNameStart()
            => Current == '\\' && (Peek(1) is 'u' or 'U');

        private void ReadUniversalCharacterName()
        {
            TryReadUniversalCharacterName(out _);
        }

        private bool TryReadUniversalCharacterName(out uint value)
        {
            value = 0;

            if (Current != '\\' || Peek(1) is not ('u' or 'U'))
                return false;

            var start = _position;
            var shortForm = Peek(1) == 'u';
            var digitCount = shortForm ? 4 : 8;

            Advance();
            Advance();

            for (var i = 0; i < digitCount; i++)
            {
                if (!IsHexDigit(Current))
                {
                    Error(start, _position - start, "Invalid universal character name.");
                    return true;
                }

                value = (value << 4) | HexValue(Current);
                Advance();
            }

            if (!IsAllowedUniversalCharacterName(value))
                Error(start, _position - start, "Universal character name is not allowed here.");

            return true;
        }

        private static uint HexValue(char ch)
        {
            if (ch is >= '0' and <= '9')
                return (uint)(ch - '0');

            if (ch is >= 'a' and <= 'f')
                return (uint)(ch - 'a' + 10);

            return (uint)(ch - 'A' + 10);
        }

        private static bool IsAllowedUniversalCharacterName(uint value)
        {
            if (value > 0x10FFFF)
                return false;

            if (value is >= 0xD800 and <= 0xDFFF)
                return false;

            return true;
        }

        private static bool IsLineBreak(char ch)
            => ch is '\r' or '\n';

        private static bool IsIdentifierStart(char ch)
            => ch == '_' || char.IsLetter(ch);

        private static bool IsIdentifierPart(char ch)
            => ch == '_' || char.IsLetterOrDigit(ch);

        private static bool IsHexDigit(char ch)
            => ch is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

        private static bool StartsWith(string text, int index, string value)
        {
            if (index < 0 || index + value.Length > text.Length)
                return false;

            for (var i = 0; i < value.Length; i++)
            {
                if (text[index + i] != value[i])
                    return false;
            }

            return true;
        }

        private static int SkipHorizontalWhitespace(string text, int index)
        {
            while (index < text.Length && text[index] is ' ' or '\t' or '\v' or '\f')
                index++;

            return index;
        }

        private static string ReadDirectiveIdentifier(string text)
        {
            var index = SkipHorizontalWhitespace(text, 0);

            if (index >= text.Length || !IsIdentifierStart(text[index]))
                return string.Empty;

            var start = index;
            index++;

            while (index < text.Length && IsIdentifierPart(text[index]))
                index++;

            return text[start..index];
        }

        private static string RemoveLineContinuations(string text)
        {
            var builder = new System.Text.StringBuilder(text.Length);

            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length && IsLineBreak(text[i + 1]))
                {
                    i++;

                    if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        i++;

                    continue;
                }

                builder.Append(text[i]);
            }

            return builder.ToString();
        }

        private static string RemoveCommentsPreservingLiterals(string text)
        {
            var builder = new System.Text.StringBuilder(text.Length);

            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] is '"' or '\'')
                {
                    var quote = text[i];
                    builder.Append(text[i]);
                    i++;

                    while (i < text.Length)
                    {
                        builder.Append(text[i]);

                        if (text[i] == '\\' && i + 1 < text.Length)
                        {
                            i++;
                            builder.Append(text[i]);
                            i++;
                            continue;
                        }

                        if (text[i] == quote)
                            break;

                        i++;
                    }

                    continue;
                }

                if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '/')
                {
                    builder.Append(' ');
                    break;
                }

                if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*')
                {
                    builder.Append(' ');
                    i += 2;

                    while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                    {
                        builder.Append(IsLineBreak(text[i]) ? text[i] : ' ');
                        i++;
                    }

                    if (i + 1 < text.Length)
                        i++;

                    continue;
                }

                builder.Append(text[i]);
            }

            return builder.ToString();
        }

        private sealed class IfExpressionParser
        {
            private readonly string _text;
            private readonly Dictionary<string, MacroInfo> _macros;
            private readonly Func<string, bool, bool> _hasInclude;
            private readonly HashSet<string> _expansionStack;
            private int _position;

            public IfExpressionParser(
                string text,
                Dictionary<string, MacroInfo> macros,
                Func<string, bool, bool> hasInclude,
                HashSet<string>? expansionStack = null)
            {
                _text = text;
                _macros = macros;
                _hasInclude = hasInclude;
                _expansionStack = expansionStack ?? new HashSet<string>(StringComparer.Ordinal);
            }

            public long Parse()
            {
                var value = ParseConditional();
                return value;
            }

            private long ParseConditional()
            {
                var condition = ParseLogicalOr();

                SkipWhitespace();
                if (!TryMatch("?"))
                    return condition;

                var whenTrue = ParseConditional();
                TryMatch(":");
                var whenFalse = ParseConditional();

                return condition != 0 ? whenTrue : whenFalse;
            }

            private long ParseLogicalOr()
            {
                var left = ParseLogicalAnd();

                while (TryMatch("||"))
                {
                    var right = ParseLogicalAnd();
                    left = left != 0 || right != 0 ? 1 : 0;
                }

                return left;
            }

            private long ParseLogicalAnd()
            {
                var left = ParseBitwiseOr();

                while (TryMatch("&&"))
                {
                    var right = ParseBitwiseOr();
                    left = left != 0 && right != 0 ? 1 : 0;
                }

                return left;
            }

            private long ParseBitwiseOr()
            {
                var left = ParseBitwiseXor();

                while (!PeekMatch("||") && TryMatch("|"))
                    left |= ParseBitwiseXor();

                return left;
            }

            private long ParseBitwiseXor()
            {
                var left = ParseBitwiseAnd();

                while (TryMatch("^"))
                    left ^= ParseBitwiseAnd();

                return left;
            }

            private long ParseBitwiseAnd()
            {
                var left = ParseEquality();

                while (!PeekMatch("&&") && TryMatch("&"))
                    left &= ParseEquality();

                return left;
            }

            private long ParseEquality()
            {
                var left = ParseRelational();

                while (true)
                {
                    if (TryMatch("=="))
                    {
                        left = left == ParseRelational() ? 1 : 0;
                        continue;
                    }

                    if (TryMatch("!="))
                    {
                        left = left != ParseRelational() ? 1 : 0;
                        continue;
                    }

                    return left;
                }
            }

            private long ParseRelational()
            {
                var left = ParseShift();

                while (true)
                {
                    if (TryMatch("<="))
                    {
                        left = left <= ParseShift() ? 1 : 0;
                        continue;
                    }

                    if (TryMatch(">="))
                    {
                        left = left >= ParseShift() ? 1 : 0;
                        continue;
                    }

                    if (TryMatch("<"))
                    {
                        left = left < ParseShift() ? 1 : 0;
                        continue;
                    }

                    if (TryMatch(">"))
                    {
                        left = left > ParseShift() ? 1 : 0;
                        continue;
                    }

                    return left;
                }
            }

            private long ParseShift()
            {
                var left = ParseAdditive();

                while (true)
                {
                    if (TryMatch("<<"))
                    {
                        left <<= (int)(ParseAdditive() & 63);
                        continue;
                    }

                    if (TryMatch(">>"))
                    {
                        left >>= (int)(ParseAdditive() & 63);
                        continue;
                    }

                    return left;
                }
            }

            private long ParseAdditive()
            {
                var left = ParseMultiplicative();

                while (true)
                {
                    if (TryMatch("+"))
                    {
                        left += ParseMultiplicative();
                        continue;
                    }

                    if (TryMatch("-"))
                    {
                        left -= ParseMultiplicative();
                        continue;
                    }

                    return left;
                }
            }

            private long ParseMultiplicative()
            {
                var left = ParseUnary();

                while (true)
                {
                    if (TryMatch("*"))
                    {
                        left *= ParseUnary();
                        continue;
                    }

                    if (TryMatch("/"))
                    {
                        var right = ParseUnary();
                        left = right == 0 ? 0 : left / right;
                        continue;
                    }

                    if (TryMatch("%"))
                    {
                        var right = ParseUnary();
                        left = right == 0 ? 0 : left % right;
                        continue;
                    }

                    return left;
                }
            }

            private long ParseUnary()
            {
                if (TryMatch("!"))
                    return ParseUnary() == 0 ? 1 : 0;

                if (TryMatch("~"))
                    return ~ParseUnary();

                if (TryMatch("+"))
                    return ParseUnary();

                if (TryMatch("-"))
                    return -ParseUnary();

                if (TryMatchIdentifier("defined"))
                    return ParseDefinedOperator();

                if (TryMatchIdentifier("__has_include"))
                    return ParseHasIncludeOperator();

                return ParsePrimary();
            }

            private long ParsePrimary()
            {
                SkipWhitespace();

                if (TryMatch("("))
                {
                    var value = ParseConditional();
                    TryMatch(")");
                    return value;
                }

                if (Current == '\'')
                    return ReadCharacterConstant();

                if (char.IsDigit(Current))
                    return ReadIntegerConstant();

                if (IsIdentifierStart(Current))
                    return EvaluateIdentifier(ReadIdentifier());

                if (Current != '\0')
                    _position++;

                return 0;
            }

            private long EvaluateIdentifier(string name)
            {
                if (!_macros.TryGetValue(name, out var macro) || macro.IsFunctionLike)
                    return 0;

                if (!_expansionStack.Add(name))
                    return 0;

                try
                {
                    var parser = new IfExpressionParser(macro.ReplacementText, _macros, _hasInclude, _expansionStack);
                    return parser.Parse();
                }
                finally
                {
                    _expansionStack.Remove(name);
                }
            }

            private long ParseDefinedOperator()
            {
                SkipWhitespace();

                if (TryMatch("("))
                {
                    var name = ReadIdentifier();
                    TryMatch(")");
                    return _macros.ContainsKey(name) ? 1 : 0;
                }

                var bareName = ReadIdentifier();
                return _macros.ContainsKey(bareName) ? 1 : 0;
            }

            private long ParseHasIncludeOperator()
            {
                SkipWhitespace();
                var hasParentheses = TryMatch("(");

                if (!TryReadHeaderName(out var includeName, out var isAngled))
                    return 0;

                if (hasParentheses)
                    TryMatch(")");

                return _hasInclude(includeName, isAngled) ? 1 : 0;
            }

            private bool TryReadHeaderName(out string includeName, out bool isAngled)
            {
                includeName = string.Empty;
                isAngled = false;
                SkipWhitespace();

                if (Current == '"')
                {
                    var start = ++_position;
                    while (Current != '\0' && Current != '"')
                        _position++;

                    if (Current != '"')
                        return false;

                    includeName = _text[start.._position];
                    _position++;
                    return includeName.Length != 0;
                }

                if (Current == '<')
                {
                    var start = ++_position;
                    while (Current != '\0' && Current != '>')
                        _position++;

                    if (Current != '>')
                        return false;

                    includeName = _text[start.._position];
                    _position++;
                    isAngled = true;
                    return includeName.Length != 0;
                }

                return false;
            }

            private long ReadIntegerConstant()
            {
                SkipWhitespace();

                var start = _position;
                var numberBase = 10;

                if (Current == '0' && (Peek(1) is 'x' or 'X'))
                {
                    numberBase = 16;
                    _position += 2;
                }
                else if (Current == '0' && (Peek(1) is 'b' or 'B'))
                {
                    numberBase = 2;
                    _position += 2;
                }
                else if (Current == '0')
                {
                    numberBase = 8;
                    _position++;
                }

                var digitsStart = _position;
                while (IsValidDigitForBase(Current, numberBase) || Current == '\'')
                    _position++;

                var digits = _text[digitsStart.._position].Replace("'", string.Empty);

                while (IsIdentifierPart(Current))
                    _position++;

                if (digits.Length == 0)
                    return start < _text.Length && _text[start] == '0' ? 0 : 0;

                try
                {
                    return Convert.ToInt64(digits, numberBase);
                }
                catch
                {
                    return 0;
                }
            }

            private long ReadCharacterConstant()
            {
                if (Current != '\'')
                    return 0;

                _position++;

                long value = 0;
                if (Current == '\\')
                {
                    _position++;
                    value = Current switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '0' => 0,
                        _ => Current,
                    };

                    if (Current != '\0')
                        _position++;
                }
                else if (Current != '\0')
                {
                    value = Current;
                    _position++;
                }

                while (Current != '\0' && Current != '\'')
                    _position++;

                if (Current == '\'')
                    _position++;

                return value;
            }

            private string ReadIdentifier()
            {
                SkipWhitespace();

                if (!IsIdentifierStart(Current))
                    return string.Empty;

                var start = _position;
                _position++;

                while (IsIdentifierPart(Current))
                    _position++;

                return _text[start.._position];
            }

            private bool TryMatchIdentifier(string identifier)
            {
                SkipWhitespace();

                if (!StartsWith(_text, _position, identifier))
                    return false;

                var end = _position + identifier.Length;
                if (end < _text.Length && IsIdentifierPart(_text[end]))
                    return false;

                _position = end;
                return true;
            }

            private bool TryMatch(string text)
            {
                SkipWhitespace();

                if (!StartsWith(_text, _position, text))
                    return false;

                _position += text.Length;
                return true;
            }

            private bool PeekMatch(string text)
            {
                SkipWhitespace();
                return StartsWith(_text, _position, text);
            }

            private void SkipWhitespace()
            {
                while (Current is ' ' or '\t' or '\v' or '\f' or '\r' or '\n')
                    _position++;
            }

            private char Current => Peek(0);

            private char Peek(int offset)
            {
                var index = _position + offset;
                return index >= 0 && index < _text.Length ? _text[index] : '\0';
            }

            private static bool IsValidDigitForBase(char ch, int numberBase)
            {
                return numberBase switch
                {
                    2 => ch is '0' or '1',
                    8 => ch is >= '0' and <= '7',
                    10 => char.IsDigit(ch),
                    16 => IsHexDigit(ch),
                    _ => false,
                };
            }
        }
    }
}
