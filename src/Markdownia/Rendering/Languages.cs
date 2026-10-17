using System.Collections.Frozen;

namespace Markdownia.Rendering;

public enum LanguageMode
{
    Code,
    Markup,
    Diff,
}

public sealed class LanguageDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string[] Aliases { get; init; } = [];
    public LanguageMode Mode { get; init; } = LanguageMode.Code;
    public FrozenSet<string> Keywords { get; init; } = FrozenSet<string>.Empty;
    public FrozenSet<string> Types { get; init; } = FrozenSet<string>.Empty;
    public FrozenSet<string> Constants { get; init; } = FrozenSet<string>.Empty;
    public string[] LineComments { get; init; } = [];
    public (string Open, string Close)[] BlockComments { get; init; } = [];
    public string Quotes { get; init; } = "\"'";
    public string[] StringPrefixes { get; init; } = [];
    public bool TripleQuotes { get; init; }
    public bool ShortCharLiterals { get; init; }
    public bool PreprocessorHash { get; init; }
    public bool Decorators { get; init; }
    public bool CapitalizedTypes { get; init; }
    public bool CaseInsensitive { get; init; }
    public bool KeyStrings { get; init; }
    public bool ShellVariables { get; init; }
    public bool DashInIdentifiers { get; init; }
}

public static class Languages
{
    private static FrozenSet<string> Set(string words) =>
        words.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).ToFrozenSet(StringComparer.Ordinal);

    private static readonly (string, string)[] CBlock = [("/*", "*/")];

    private static readonly FrozenSet<string> CLikeConstants = Set("true false null nullptr NULL");

    public static readonly IReadOnlyList<LanguageDefinition> All =
    [
        new()
        {
            Id = "csharp", DisplayName = "C#", Aliases = ["cs", "c#", "csx"],
            Keywords = Set("""
                abstract as async await base break case catch checked class const continue default delegate do else enum
                event explicit extern finally fixed for foreach goto if implicit in interface internal is lock namespace new
                operator out override params private protected public readonly ref return sealed sizeof stackalloc static
                struct switch this throw try typeof unchecked unsafe using virtual volatile while yield var when where get
                set init add remove partial record required file scoped with and or not nameof global value let from select
                orderby group into join on equals by ascending descending field
                """),
            Types = Set("bool byte sbyte char decimal double float int uint nint nuint long ulong short ushort object string void dynamic"),
            Constants = CLikeConstants,
            LineComments = ["//"], BlockComments = CBlock, StringPrefixes = ["$@", "@$", "$$", "@", "$", "u8"],
            ShortCharLiterals = true, PreprocessorHash = true, CapitalizedTypes = true,
        },
        new()
        {
            Id = "cpp", DisplayName = "C++", Aliases = ["c++", "cc", "cxx", "hpp", "hxx", "h++", "ino"],
            Keywords = Set("""
                alignas alignof and asm auto break case catch class concept const consteval constexpr constinit const_cast
                continue co_await co_return co_yield decltype default delete do dynamic_cast else enum explicit export extern
                final for friend goto if inline mutable namespace new noexcept not operator or override private protected
                public register reinterpret_cast requires return sizeof static static_assert static_cast struct switch
                template this thread_local throw try typedef typeid typename union using virtual volatile while xor
                """),
            Types = Set("""
                bool char char8_t char16_t char32_t double float int long short signed unsigned void wchar_t size_t ssize_t
                int8_t int16_t int32_t int64_t uint8_t uint16_t uint32_t uint64_t std string vector map unordered_map set
                unique_ptr shared_ptr optional span string_view array
                """),
            Constants = CLikeConstants,
            LineComments = ["//"], BlockComments = CBlock, StringPrefixes = ["u8", "u", "U", "L", "R"],
            ShortCharLiterals = true, PreprocessorHash = true,
        },
        new()
        {
            Id = "c", DisplayName = "C", Aliases = ["h"],
            Keywords = Set("""
                auto break case const continue default do else enum extern for goto if inline register restrict return
                sizeof static struct switch typedef union volatile while _Alignas _Alignof _Atomic _Bool _Generic _Noreturn
                _Static_assert _Thread_local
                """),
            Types = Set("char double float int long short signed unsigned void size_t bool int8_t int16_t int32_t int64_t uint8_t uint16_t uint32_t uint64_t FILE"),
            Constants = CLikeConstants,
            LineComments = ["//"], BlockComments = CBlock, ShortCharLiterals = true, PreprocessorHash = true,
        },
        new()
        {
            Id = "python", DisplayName = "Python", Aliases = ["py", "python3", "py3", "pyw", "gyp"],
            Keywords = Set("""
                and as assert async await break class continue def del elif else except finally for from global if import
                in is lambda nonlocal not or pass raise return try while with yield match case type
                """),
            Types = Set("int float str bool list dict set tuple bytes object complex frozenset range self cls print len super isinstance"),
            Constants = Set("True False None"),
            LineComments = ["#"], StringPrefixes = ["rb", "br", "fr", "rf", "r", "f", "b", "u", "R", "F", "B", "U"],
            TripleQuotes = true, Decorators = true,
        },
        new()
        {
            Id = "javascript", DisplayName = "JavaScript", Aliases = ["js", "jsx", "mjs", "cjs", "node"],
            Keywords = JsKeywords,
            Types = Set("Array Object String Number Boolean Promise Map Set Symbol Date RegExp Error JSON Math console window document"),
            Constants = Set("true false null undefined NaN Infinity this"),
            LineComments = ["//"], BlockComments = CBlock, Quotes = "\"'`", CapitalizedTypes = true, Decorators = true,
        },
        new()
        {
            Id = "typescript", DisplayName = "TypeScript", Aliases = ["ts", "tsx", "mts", "cts"],
            Keywords = Set(JsKeywordText + " type interface enum implements namespace declare abstract private protected public readonly keyof infer is asserts satisfies override module"),
            Types = Set("string number boolean any unknown never void object symbol bigint Array Promise Record Partial Readonly Map Set"),
            Constants = Set("true false null undefined NaN Infinity this"),
            LineComments = ["//"], BlockComments = CBlock, Quotes = "\"'`", CapitalizedTypes = true, Decorators = true,
        },
        new()
        {
            Id = "java", DisplayName = "Java", Aliases = ["jav"],
            Keywords = Set("""
                abstract assert break case catch class const continue default do else enum extends final finally for goto if
                implements import instanceof interface native new package private protected public return static strictfp
                super switch synchronized this throw throws transient try volatile while var record sealed permits yield non-sealed
                """),
            Types = Set("boolean byte char double float int long short void String Object Integer List Map"),
            Constants = CLikeConstants,
            LineComments = ["//"], BlockComments = CBlock, ShortCharLiterals = true, CapitalizedTypes = true, Decorators = true,
        },
        new()
        {
            Id = "kotlin", DisplayName = "Kotlin", Aliases = ["kt", "kts"],
            Keywords = Set("""
                as break class continue do else for fun if in interface is object package return super this throw try typealias
                typeof val var when while by catch constructor delegate dynamic field file finally get import init param
                property receiver set setparam where actual abstract annotation companion const crossinline data enum expect
                external final infix inline inner internal lateinit noinline open operator out override private protected
                public reified sealed suspend tailrec vararg
                """),
            Types = Set("Int Long Short Byte Double Float Boolean Char String Unit Any Nothing List Map Set Array"),
            Constants = Set("true false null"),
            LineComments = ["//"], BlockComments = CBlock, TripleQuotes = true, ShortCharLiterals = true, CapitalizedTypes = true, Decorators = true,
        },
        new()
        {
            Id = "rust", DisplayName = "Rust", Aliases = ["rs"],
            Keywords = Set("""
                as async await break const continue crate dyn else enum extern fn for if impl in let loop match mod move mut pub
                ref return self Self static struct super trait type unsafe use where while macro_rules union
                """),
            Types = Set("i8 i16 i32 i64 i128 isize u8 u16 u32 u64 u128 usize f32 f64 bool char str String Vec Option Result Box Rc Arc HashMap"),
            Constants = Set("true false None Some Ok Err"),
            LineComments = ["//"], BlockComments = CBlock, StringPrefixes = ["b", "r"], ShortCharLiterals = true, CapitalizedTypes = true,
        },
        new()
        {
            Id = "go", DisplayName = "Go", Aliases = ["golang"],
            Keywords = Set("""
                break case chan const continue default defer else fallthrough for func go goto if import interface map package
                range return select struct switch type var
                """),
            Types = Set("bool byte complex64 complex128 error float32 float64 int int8 int16 int32 int64 rune string uint uint8 uint16 uint32 uint64 uintptr any"),
            Constants = Set("true false nil iota"),
            LineComments = ["//"], BlockComments = CBlock, Quotes = "\"'`", ShortCharLiterals = true,
        },
        new()
        {
            Id = "swift", DisplayName = "Swift",
            Keywords = Set("""
                associatedtype class deinit enum extension fileprivate func import init inout internal let open operator private
                protocol public rethrows static struct subscript typealias var break case continue default defer do else
                fallthrough for guard if in repeat return switch where while as catch is super self Self throw throws try async
                await actor some any
                """),
            Types = Set("Int Double Float Bool String Character Array Dictionary Set Optional Void"),
            Constants = Set("true false nil"),
            LineComments = ["//"], BlockComments = CBlock, TripleQuotes = true, CapitalizedTypes = true, Decorators = true,
        },
        new()
        {
            Id = "fsharp", DisplayName = "F#", Aliases = ["fs", "f#", "fsx"],
            Keywords = Set("""
                abstract and as assert base begin class default delegate do done downcast downto elif else end exception extern
                for fun function global if in inherit inline interface internal lazy let match member module mutable namespace
                new not of open or override private public rec return static struct then to try type upcast use val void when
                while with yield
                """),
            Types = Set("int float string bool unit char decimal list array seq option int64 byte obj"),
            Constants = Set("true false null None Some"),
            LineComments = ["//"], BlockComments = [("(*", "*)")], Quotes = "\"", TripleQuotes = true, StringPrefixes = ["$", "@"],
            CapitalizedTypes = true,
        },
        new()
        {
            Id = "php", DisplayName = "PHP",
            Keywords = Set("""
                abstract and array as break callable case catch class clone const continue declare default do echo else elseif
                empty enddeclare endfor endforeach endif endswitch endwhile extends final finally fn for foreach function global
                goto if implements include include_once instanceof insteadof interface isset list match namespace new or print
                private protected public readonly require require_once return static switch throw trait try unset use var while
                xor yield
                """),
            Constants = Set("true false null TRUE FALSE NULL"),
            LineComments = ["//", "#"], BlockComments = CBlock, ShellVariables = true,
        },
        new()
        {
            Id = "ruby", DisplayName = "Ruby", Aliases = ["rb"],
            Keywords = Set("""
                alias and begin break case class def defined? do else elsif end ensure for if in module next not or redo rescue
                retry return self super then undef unless until when while yield require attr_accessor attr_reader puts
                """),
            Constants = Set("true false nil"),
            LineComments = ["#"], CapitalizedTypes = true,
        },
        new()
        {
            Id = "sql", DisplayName = "SQL", Aliases = ["mysql", "postgres", "postgresql", "sqlite", "tsql", "plsql", "psql"],
            Keywords = Set("""
                select from where and or not insert into values update set delete create table alter drop index view as join
                inner left right outer full cross on group by order having limit offset union all distinct case when then else
                end is in exists between like primary key foreign references default unique check constraint with returning
                begin commit rollback transaction asc desc top database schema grant revoke trigger procedure function declare
                """),
            Types = Set("int integer bigint smallint tinyint decimal numeric float real double varchar char text nvarchar date time timestamp datetime boolean bool blob uuid json jsonb serial"),
            Constants = Set("null true false"),
            LineComments = ["--"], BlockComments = CBlock, Quotes = "'\"", CaseInsensitive = true,
        },
        new()
        {
            Id = "bash", DisplayName = "Shell", Aliases = ["sh", "shell", "zsh", "console", "shellsession", "ksh", "fish"],
            Keywords = Set("if then else elif fi for while until do done case esac in function return exit export local readonly declare source alias set unset shift break continue echo cd sudo"),
            LineComments = ["#"], ShellVariables = true, DashInIdentifiers = true,
        },
        new()
        {
            Id = "powershell", DisplayName = "PowerShell", Aliases = ["ps1", "pwsh", "ps", "psm1"],
            Keywords = Set("""
                begin break catch class continue data define do dynamicparam else elseif end enum exit filter finally for
                foreach from function hidden if in param process return static switch throw trap try until using var while
                """),
            Constants = Set("$true $false $null"),
            LineComments = ["#"], BlockComments = [("<#", "#>")], ShellVariables = true, DashInIdentifiers = true, CaseInsensitive = true,
        },
        new()
        {
            Id = "json", DisplayName = "JSON", Aliases = ["jsonc", "json5", "geojson"],
            Constants = Set("true false null"),
            LineComments = ["//"], BlockComments = CBlock, Quotes = "\"", KeyStrings = true,
        },
        new()
        {
            Id = "yaml", DisplayName = "YAML", Aliases = ["yml"],
            Constants = Set("true false null yes no on off ~"),
            LineComments = ["#"], KeyStrings = true, DashInIdentifiers = true,
        },
        new()
        {
            Id = "toml", DisplayName = "TOML", Aliases = ["ini", "cfg", "conf", "editorconfig"],
            Constants = Set("true false"),
            LineComments = ["#", ";"], KeyStrings = true, TripleQuotes = true, DashInIdentifiers = true,
        },
        new()
        {
            Id = "css", DisplayName = "CSS", Aliases = ["scss", "less", "sass"],
            Keywords = Set("@media @import @font-face @keyframes @supports @layer important"),
            LineComments = ["//"], BlockComments = CBlock, DashInIdentifiers = true, KeyStrings = true,
        },
        new()
        {
            Id = "html", DisplayName = "HTML", Aliases = ["htm", "xhtml", "xml", "xaml", "axaml", "svg", "plist", "csproj", "vue", "razor", "cshtml"],
            Mode = LanguageMode.Markup,
        },
        new()
        {
            Id = "diff", DisplayName = "Diff", Aliases = ["patch", "udiff"], Mode = LanguageMode.Diff,
        },
        new()
        {
            Id = "dockerfile", DisplayName = "Dockerfile", Aliases = ["docker"],
            Keywords = Set("FROM RUN CMD LABEL EXPOSE ENV ADD COPY ENTRYPOINT VOLUME USER WORKDIR ARG ONBUILD STOPSIGNAL HEALTHCHECK SHELL AS"),
            LineComments = ["#"], ShellVariables = true,
        },
        new()
        {
            Id = "lua", DisplayName = "Lua",
            Keywords = Set("and break do else elseif end for function goto if in local not or repeat return then until while"),
            Constants = Set("true false nil"),
            LineComments = ["--"], BlockComments = [("--[[", "]]")],
        },
    ];

    private const string JsKeywordText = """
        async await break case catch class const continue debugger default delete do else export extends finally for
        function if import in instanceof let new of return static super switch throw try typeof var void while with
        yield get set from as
        """;

    private static FrozenSet<string> JsKeywords => Set(JsKeywordText);

    private static readonly FrozenDictionary<string, LanguageDefinition> ByName = BuildIndex();

    private static FrozenDictionary<string, LanguageDefinition> BuildIndex()
    {
        var map = new Dictionary<string, LanguageDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var lang in All)
        {
            map[lang.Id] = lang;
            foreach (var alias in lang.Aliases)
                map.TryAdd(alias, lang);
        }
        return map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public static LanguageDefinition? Find(string name) => ByName.GetValueOrDefault(name);

    /// <summary>Languages offered in the "Code Block" dropdown.</summary>
    public static readonly IReadOnlyList<(string Label, string Fence)> Common =
    [
        ("Plain text", ""),
        ("C#", "csharp"),
        ("C++", "cpp"),
        ("C", "c"),
        ("Python", "python"),
        ("JavaScript", "javascript"),
        ("TypeScript", "typescript"),
        ("Java", "java"),
        ("Rust", "rust"),
        ("Go", "go"),
        ("SQL", "sql"),
        ("Shell", "bash"),
        ("PowerShell", "powershell"),
        ("JSON", "json"),
        ("YAML", "yaml"),
        ("HTML / XML", "html"),
        ("CSS", "css"),
        ("Diff", "diff"),
    ];
}
