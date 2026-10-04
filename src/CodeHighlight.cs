using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;

namespace TaskPad
{
    /// Syntax highlighting for code files and for ``` fenced blocks inside notes.
    /// Every language is generated from one compact spec, so all of them share the selected code theme.
    public static class Code
    {
        // ---------------- themes ----------------

        public sealed class CodeTheme
        {
            public string Name;
            public bool Light;
            public Dictionary<string, string> C;   // token class -> #RRGGBB
            public string Bg;                      // code block background
        }

        static CodeTheme T(string name, bool light, string bg, string kw, string type, string fn, string str, string num, string cmt, string cst, string pre, string tag, string attr, string var, string plain) =>
            new CodeTheme
            {
                Name = name, Light = light, Bg = bg,
                C = new Dictionary<string, string>
                {
                    ["Keyword"] = kw, ["Type"] = type, ["Function"] = fn, ["String"] = str, ["Number"] = num, ["Comment"] = cmt,
                    ["Constant"] = cst, ["Preproc"] = pre, ["Tag"] = tag, ["Attribute"] = attr, ["Variable"] = var, ["Plain"] = plain,
                    ["Inserted"] = light ? "#1A7F37" : "#7EE787", ["Deleted"] = light ? "#CF222E" : "#FF7B72",
                },
            };

        public static readonly CodeTheme[] Themes =
        {
            T("One Dark", false, "#21252B", "#C678DD", "#E5C07B", "#61AFEF", "#98C379", "#D19A66", "#7F848E", "#D19A66", "#C678DD", "#E06C75", "#D19A66", "#E06C75", "#ABB2BF"),
            T("Dracula", false, "#21222C", "#FF79C6", "#8BE9FD", "#50FA7B", "#F1FA8C", "#BD93F9", "#6272A4", "#BD93F9", "#FF79C6", "#FF79C6", "#50FA7B", "#FFB86C", "#F8F8F2"),
            T("Monokai", false, "#1E1F1C", "#F92672", "#66D9EF", "#A6E22E", "#E6DB74", "#AE81FF", "#75715E", "#AE81FF", "#F92672", "#F92672", "#A6E22E", "#FD971F", "#F8F8F2"),
            T("Nord", false, "#272C36", "#81A1C1", "#8FBCBB", "#88C0D0", "#A3BE8C", "#B48EAD", "#616E88", "#B48EAD", "#5E81AC", "#81A1C1", "#8FBCBB", "#D8DEE9", "#D8DEE9"),
            T("GitHub Dark", false, "#161B22", "#FF7B72", "#FFA657", "#D2A8FF", "#A5D6FF", "#79C0FF", "#8B949E", "#79C0FF", "#FF7B72", "#7EE787", "#79C0FF", "#FFA657", "#C9D1D9"),
            T("GitHub Light", true, "#F6F8FA", "#CF222E", "#953800", "#8250DF", "#0A3069", "#0550AE", "#6E7781", "#0550AE", "#CF222E", "#116329", "#0550AE", "#953800", "#24292F"),
            T("One Light", true, "#F4F4F6", "#A626A4", "#C18401", "#4078F2", "#50A14F", "#986801", "#A0A1A7", "#986801", "#A626A4", "#E45649", "#986801", "#E45649", "#383A42"),
            T("Solarized Light", true, "#F7F0DC", "#859900", "#B58900", "#268BD2", "#2AA198", "#D33682", "#93A1A1", "#CB4B16", "#CB4B16", "#268BD2", "#B58900", "#268BD2", "#586E75"),
        };

        /// "auto" follows the app theme.
        public static CodeTheme Current
        {
            get
            {
                var name = Workspace.Settings?.CodeTheme ?? "auto";
                var t = Themes.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                return t ?? (Theme.IsLight ? Themes.First(x => x.Name == "GitHub Light") : Themes.First(x => x.Name == "One Dark"));
            }
        }

        public static void SetTheme(string name)
        {
            Workspace.Settings.CodeTheme = name;
            Workspace.Settings.Save();
            Refresh();
        }

        /// Re-generates definitions with the current colours and redraws every editor.
        public static void Refresh()
        {
            Definitions.Clear();
            BlockCache.Clear();
            _blockBrush = null;
            foreach (var v in Workspace.AllViews) v.ApplyCodeMode();
        }

        // ---------------- languages ----------------

        sealed class Lang
        {
            public string Name, Line, Line2, BlockStart, BlockEnd, Keywords = "", Types = "", Constants = "";
            public string[] Aliases = new string[0], Exts = new string[0];
            public bool DoubleQuote = true, SingleQuote = true, Backtick, Triple, HashPreproc, Decorators, Markup, IgnoreCase, DollarVars, Json, Yaml, Css, Diff, KeysBeforeEquals;
        }

        const string CKw = "if else for while do switch case default break continue return goto";
        static readonly Lang[] Langs =
        {
            new Lang { Name = "Python", Aliases = new[] { "python", "py", "python3" }, Exts = new[] { ".py", ".pyw" }, Line = "#", Triple = true, Decorators = true,
                Keywords = "and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield match case",
                Types = "int float str bool list dict set tuple bytes object type range print len open super self cls Exception ValueError TypeError KeyError",
                Constants = "True False None" },
            new Lang { Name = "JavaScript", Aliases = new[] { "javascript", "js", "jsx", "mjs", "node" }, Exts = new[] { ".js", ".jsx", ".mjs", ".cjs" }, Line = "//", BlockStart = "/*", BlockEnd = "*/", Backtick = true,
                Keywords = CKw + " var let const function class extends new delete typeof instanceof in of try catch finally throw async await yield import export from as static get set this super with",
                Types = "Array Object String Number Boolean Promise Map Set Date Math JSON console window document Error RegExp Symbol",
                Constants = "true false null undefined NaN Infinity" },
            new Lang { Name = "TypeScript", Aliases = new[] { "typescript", "ts", "tsx" }, Exts = new[] { ".ts", ".tsx" }, Line = "//", BlockStart = "/*", BlockEnd = "*/", Backtick = true, Decorators = true,
                Keywords = CKw + " var let const function class extends implements interface type enum namespace module declare abstract public private protected readonly new delete typeof keyof instanceof in of try catch finally throw async await yield import export from as static get set this super satisfies infer is",
                Types = "string number boolean any unknown never void object Array Promise Record Partial Readonly Map Set Date Error",
                Constants = "true false null undefined" },
            new Lang { Name = "C#", Aliases = new[] { "csharp", "cs", "c#" }, Exts = new[] { ".cs", ".csx" }, Line = "//", BlockStart = "/*", BlockEnd = "*/", HashPreproc = true,
                Keywords = CKw + " abstract as base checked class const delegate enum event explicit extern finally fixed foreach implicit in interface internal is lock namespace new operator out override params private protected public readonly ref sealed sizeof stackalloc static struct this throw try catch typeof unchecked unsafe using virtual volatile async await var get set init record when where yield nameof",
                Types = "bool byte char decimal double float int long object sbyte short string uint ulong ushort void dynamic Task List Dictionary IEnumerable Console Math DateTime Exception",
                Constants = "true false null" },
            new Lang { Name = "Java", Aliases = new[] { "java" }, Exts = new[] { ".java" }, Line = "//", BlockStart = "/*", BlockEnd = "*/", Decorators = true,
                Keywords = CKw + " abstract assert class extends final finally implements import instanceof interface native new package private protected public static strictfp super synchronized this throw throws transient try catch volatile var record sealed permits",
                Types = "boolean byte char double float int long short void String Object Integer List Map ArrayList HashMap System Exception",
                Constants = "true false null" },
            new Lang { Name = "Kotlin", Aliases = new[] { "kotlin", "kt" }, Exts = new[] { ".kt", ".kts" }, Line = "//", BlockStart = "/*", BlockEnd = "*/", Triple = true, Decorators = true,
                Keywords = "as break class continue do else for fun if in interface is object package return super this throw try catch finally typealias val var when while by constructor data enum sealed open override private protected public internal suspend import companion lateinit",
                Types = "Int Long Double Float Boolean String Char Unit Any List Map Set Array",
                Constants = "true false null" },
            new Lang { Name = "Swift", Aliases = new[] { "swift" }, Exts = new[] { ".swift" }, Line = "//", BlockStart = "/*", BlockEnd = "*/", Triple = true, Decorators = true,
                Keywords = "class struct enum protocol extension func var let if else guard switch case default for while repeat return break continue import in where throw throws try catch do defer self Self init deinit public private fileprivate internal open static override mutating async await some any",
                Types = "Int Double Float Bool String Character Array Dictionary Set Optional Void Any",
                Constants = "true false nil" },
            new Lang { Name = "Go", Aliases = new[] { "go", "golang" }, Exts = new[] { ".go" }, Line = "//", BlockStart = "/*", BlockEnd = "*/", Backtick = true,
                Keywords = "break case chan const continue default defer else fallthrough for func go goto if import interface map package range return select struct switch type var",
                Types = "bool byte complex64 complex128 error float32 float64 int int8 int16 int32 int64 rune string uint uint8 uint16 uint32 uint64 uintptr any make new len cap append panic recover fmt",
                Constants = "true false nil iota" },
            new Lang { Name = "Rust", Aliases = new[] { "rust", "rs" }, Exts = new[] { ".rs" }, Line = "//", BlockStart = "/*", BlockEnd = "*/", SingleQuote = false, HashPreproc = true,
                Keywords = "as async await break const continue crate dyn else enum extern fn for if impl in let loop match mod move mut pub ref return self Self static struct super trait type unsafe use where while",
                Types = "i8 i16 i32 i64 i128 isize u8 u16 u32 u64 u128 usize f32 f64 bool char str String Vec Option Result Box Rc Arc HashMap println format vec",
                Constants = "true false None Some Ok Err" },
            new Lang { Name = "C/C++", Aliases = new[] { "c", "cpp", "c++", "h", "hpp", "cc", "objc" }, Exts = new[] { ".c", ".h", ".cpp", ".cc", ".cxx", ".hpp", ".hh", ".ino" }, Line = "//", BlockStart = "/*", BlockEnd = "*/", HashPreproc = true,
                Keywords = CKw + " auto class const constexpr delete enum explicit extern friend inline mutable namespace new noexcept operator private protected public register sizeof static struct template this throw try catch typedef typename union using virtual volatile override final",
                Types = "bool char double float int long short signed unsigned void size_t std string vector map unique_ptr shared_ptr printf cout cin endl",
                Constants = "true false NULL nullptr" },
            new Lang { Name = "PHP", Aliases = new[] { "php" }, Exts = new[] { ".php" }, Line = "//", Line2 = "#", BlockStart = "/*", BlockEnd = "*/", DollarVars = true, IgnoreCase = true,
                Keywords = CKw + " abstract and as catch class clone const declare echo elseif empty enddeclare endfor endforeach endif endswitch endwhile extends final finally fn foreach function global if implements include include_once instanceof insteadof interface isset list match namespace new or print private protected public readonly require require_once static throw trait try unset use var xor yield",
                Types = "array bool callable float int iterable mixed object string void self parent",
                Constants = "true false null" },
            new Lang { Name = "Ruby", Aliases = new[] { "ruby", "rb" }, Exts = new[] { ".rb", ".rake", ".gemspec" }, Line = "#",
                Keywords = "alias and begin break case class def defined? do else elsif end ensure for if in module next not or redo rescue retry return self super then undef unless until when while yield require attr_accessor puts",
                Types = "Array Hash String Integer Float Symbol Proc Kernel",
                Constants = "true false nil" },
            new Lang { Name = "Shell", Aliases = new[] { "bash", "sh", "shell", "zsh", "console" }, Exts = new[] { ".sh", ".bash", ".zsh" }, Line = "#", DollarVars = true, Backtick = true,
                Keywords = "if then else elif fi case esac for while until do done in function return break continue export local readonly declare unset source alias",
                Types = "echo cd ls cat grep sed awk find rm cp mv mkdir chmod curl git npm sudo printf exit test",
                Constants = "true false" },
            new Lang { Name = "PowerShell", Aliases = new[] { "powershell", "ps1", "pwsh", "ps" }, Exts = new[] { ".ps1", ".psm1", ".psd1" }, Line = "#", BlockStart = "<#", BlockEnd = "#>", DollarVars = true, IgnoreCase = true,
                Keywords = "begin break catch class continue data do dynamicparam else elseif end enum exit filter finally for foreach function if in param process return switch throw trap try until using while",
                Types = "Write-Host Write-Output Get-ChildItem Get-Content Set-Content Get-Process Start-Process Where-Object ForEach-Object Select-Object New-Object Invoke-WebRequest",
                Constants = "$true $false $null" },
            new Lang { Name = "Batch", Aliases = new[] { "bat", "cmd", "batch" }, Exts = new[] { ".bat", ".cmd" }, Line = "REM ", Line2 = "::", IgnoreCase = true, SingleQuote = false,
                Keywords = "if else for in do goto call exit set echo setlocal endlocal not exist defined errorlevel pause start shift",
                Constants = "on off nul" },
            new Lang { Name = "SQL", Aliases = new[] { "sql", "mysql", "postgres", "postgresql", "sqlite", "tsql" }, Exts = new[] { ".sql" }, Line = "--", BlockStart = "/*", BlockEnd = "*/", IgnoreCase = true, DoubleQuote = false,
                Keywords = "select from where and or not insert into values update set delete create table alter drop index view join inner left right outer full on as group by order having limit offset distinct union all case when then else end exists in is like between primary key foreign references default constraint unique begin commit rollback transaction with returning",
                Types = "int integer bigint smallint varchar char text boolean bool date datetime timestamp decimal numeric float real serial uuid json jsonb count sum avg min max coalesce",
                Constants = "null true false" },
            new Lang { Name = "JSON", Aliases = new[] { "json", "jsonc", "json5" }, Exts = new[] { ".json", ".jsonc", ".geojson" }, Line = "//", BlockStart = "/*", BlockEnd = "*/", Json = true, SingleQuote = false,
                Constants = "true false null" },
            new Lang { Name = "YAML", Aliases = new[] { "yaml", "yml" }, Exts = new[] { ".yaml", ".yml" }, Line = "#", Yaml = true,
                Constants = "true false null yes no on off ~" },
            new Lang { Name = "TOML / INI", Aliases = new[] { "toml", "ini", "cfg", "conf", "properties", "env" }, Exts = new[] { ".toml", ".ini", ".cfg", ".conf", ".properties", ".env", ".editorconfig" }, Line = "#", Line2 = ";", KeysBeforeEquals = true,
                Constants = "true false" },
            new Lang { Name = "HTML", Aliases = new[] { "html", "htm", "xhtml", "vue", "svelte" }, Exts = new[] { ".html", ".htm", ".vue", ".svelte" }, Markup = true },
            new Lang { Name = "XML", Aliases = new[] { "xml", "xaml", "svg", "csproj", "plist", "rss" }, Exts = new[] { ".xml", ".xaml", ".svg", ".csproj", ".props", ".targets", ".config", ".plist", ".resx", ".xsd" }, Markup = true },
            new Lang { Name = "CSS", Aliases = new[] { "css", "scss", "sass", "less" }, Exts = new[] { ".css", ".scss", ".sass", ".less" }, Line = "//", BlockStart = "/*", BlockEnd = "*/", Css = true,
                Keywords = "@media @import @keyframes @font-face @supports @mixin @include @extend !important",
                Constants = "none auto inherit initial unset block inline flex grid absolute relative fixed sticky solid transparent" },
            new Lang { Name = "Lua", Aliases = new[] { "lua" }, Exts = new[] { ".lua" }, Line = "--", BlockStart = "--[[", BlockEnd = "]]",
                Keywords = "and break do else elseif end for function goto if in local not or repeat return then until while",
                Types = "print pairs ipairs table string math require type tostring tonumber",
                Constants = "true false nil" },
            new Lang { Name = "Dart", Aliases = new[] { "dart", "flutter" }, Exts = new[] { ".dart" }, Line = "//", BlockStart = "/*", BlockEnd = "*/", Triple = true, Decorators = true,
                Keywords = CKw + " abstract as async await class const extends factory final finally get implements import in is late library mixin new on operator part required rethrow set static super sync this throw try catch typedef var with yield",
                Types = "int double num bool String List Map Set Future Stream void dynamic Widget",
                Constants = "true false null" },
            new Lang { Name = "R", Aliases = new[] { "r" }, Exts = new[] { ".r", ".R" }, Line = "#",
                Keywords = "if else repeat while function for in next break return library require",
                Constants = "TRUE FALSE NULL NA Inf NaN" },
            new Lang { Name = "Scala", Aliases = new[] { "scala" }, Exts = new[] { ".scala", ".sc" }, Line = "//", BlockStart = "/*", BlockEnd = "*/", Triple = true,
                Keywords = "abstract case catch class def do else extends final finally for forSome if implicit import lazy match new object override package private protected return sealed super this throw trait try type val var while with yield given using enum then",
                Types = "Int Long Double Float Boolean String Unit Any List Map Option Some Seq",
                Constants = "true false null None Nil" },
            new Lang { Name = "Visual Basic", Aliases = new[] { "vb", "vbnet", "vba", "vbs" }, Exts = new[] { ".vb", ".vbs", ".bas" }, Line = "'", IgnoreCase = true, SingleQuote = false,
                Keywords = "and as byval byref call case catch class const dim do each else elseif end enum exit for function get handles if imports in inherits interface is loop me module mybase new next not nothing of or private property protected public return select set shared static step sub then throw to try until using while with",
                Types = "boolean byte char date decimal double integer long object short single string",
                Constants = "true false nothing" },
            new Lang { Name = "Dockerfile", Aliases = new[] { "dockerfile", "docker" }, Exts = new[] { ".dockerfile" }, Line = "#", IgnoreCase = true, DollarVars = true,
                Keywords = "from run cmd label expose env add copy entrypoint volume user workdir arg onbuild stopsignal healthcheck shell as" },
            new Lang { Name = "Makefile", Aliases = new[] { "make", "makefile", "mk" }, Exts = new[] { ".mk", ".make" }, Line = "#", DollarVars = true,
                Keywords = "ifeq ifneq ifdef ifndef else endif include define endef export .PHONY" },
            new Lang { Name = "Diff", Aliases = new[] { "diff", "patch" }, Exts = new[] { ".diff", ".patch" }, Diff = true, DoubleQuote = false, SingleQuote = false },
        };

        static readonly Dictionary<string, Lang> ByAlias = Langs.SelectMany(l => l.Aliases.Select(a => (a, l))).GroupBy(x => x.a).ToDictionary(g => g.Key, g => g.First().l, StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Lang> ByExt = Langs.SelectMany(l => l.Exts.Select(e => (e, l))).GroupBy(x => x.e, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().l, StringComparer.OrdinalIgnoreCase);

        public static IEnumerable<string> LanguageNames => Langs.Select(l => l.Name);

        /// Is this file a code file (opened with language highlighting instead of note rendering)?
        public static bool IsCodeFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var name = Path.GetFileName(path);
            if (name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) || name.Equals("Makefile", StringComparison.OrdinalIgnoreCase)) return true;
            return ByExt.ContainsKey(Path.GetExtension(path));
        }

        public static IHighlightingDefinition ForFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var name = Path.GetFileName(path);
            if (name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)) return Get(ByAlias["dockerfile"]);
            if (name.Equals("Makefile", StringComparison.OrdinalIgnoreCase)) return Get(ByAlias["makefile"]);
            return ByExt.TryGetValue(Path.GetExtension(path), out var l) ? Get(l) : null;
        }

        public static IHighlightingDefinition ForFence(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return null;
            var key = lang.Trim().Split(' ', '{', ',')[0].TrimStart('.');
            return ByAlias.TryGetValue(key, out var l) ? Get(l) : null;
        }

        static readonly Dictionary<Lang, IHighlightingDefinition> Definitions = new Dictionary<Lang, IHighlightingDefinition>();

        static IHighlightingDefinition Get(Lang l)
        {
            if (Definitions.TryGetValue(l, out var d)) return d;
            try
            {
                using (var r = XmlReader.Create(new StringReader(Xshd(l, Current))))
                    d = HighlightingLoader.Load(r, HighlightingManager.Instance);
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "TaskPad-error.log"), $"{DateTime.Now} highlighting {l.Name}{Environment.NewLine}{ex}{Environment.NewLine}"); } catch { }
                d = null;
            }
            Definitions[l] = d;
            return d;
        }

        // ---------------- XSHD generation ----------------

        static string X(string s) => SecurityElement.Escape(s);
        static string Rx(string s) => X(System.Text.RegularExpressions.Regex.Escape(s));

        static string Xshd(Lang l, CodeTheme t)
        {
            var sb = new StringBuilder();
            sb.Append($"<SyntaxDefinition name='{X(l.Name)}' xmlns='http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008'>");
            foreach (var kv in t.C)
            {
                string extra = kv.Key == "Comment" ? " fontStyle='italic'" : kv.Key == "Keyword" || kv.Key == "Tag" ? "" : "";
                sb.Append($"<Color name='{kv.Key}' foreground='{kv.Value}'{extra}/>");
            }
            sb.Append($"<RuleSet ignoreCase='{(l.IgnoreCase ? "true" : "false")}'>");

            if (l.Markup)
            {
                sb.Append("<Span color='Comment' multiline='true' begin='&lt;!--' end='--&gt;'/>");
                sb.Append("<Span color='Preproc' multiline='true' begin='&lt;!\\[CDATA\\[' end='\\]\\]&gt;'/>");
                sb.Append("<Span color='Preproc' begin='&lt;[?!]' end='&gt;'/>");
                sb.Append("<Span color='Tag' multiline='true' begin='&lt;/?[\\w:.-]*' end='/?&gt;'><RuleSet>");
                sb.Append("<Span color='String' multiline='true' begin='&quot;' end='&quot;'/><Span color='String' multiline='true' begin=\"'\" end=\"'\"/>");
                sb.Append("<Rule color='Attribute'>[\\w:@.-]+(?=\\s*=)</Rule>");
                sb.Append("</RuleSet></Span>");
                sb.Append("<Rule color='Constant'>&amp;[#\\w]+;</Rule>");
                sb.Append("</RuleSet></SyntaxDefinition>");
                return sb.ToString();
            }
            if (l.Diff)
            {
                sb.Append("<Rule color='Preproc'>^(@@.*|diff .*|index .*|\\+\\+\\+.*|---.*)$</Rule>");
                sb.Append("<Rule color='Inserted'>^\\+.*$</Rule><Rule color='Deleted'>^-.*$</Rule>");
                sb.Append("</RuleSet></SyntaxDefinition>");
                return sb.ToString();
            }

            if (l.BlockStart != null) sb.Append($"<Span color='Comment' multiline='true' begin='{Rx(l.BlockStart)}' end='{Rx(l.BlockEnd)}'/>");
            if (l.Line != null) sb.Append($"<Span color='Comment' begin='{Rx(l.Line)}'/>");
            if (l.Line2 != null) sb.Append($"<Span color='Comment' begin='{Rx(l.Line2)}'/>");
            if (l.HashPreproc) sb.Append("<Rule color='Preproc'>^\\s*\\#\\s*\\w+.*$|\\#!?\\[[^\\]]*\\]</Rule>");
            if (l.Json) sb.Append("<Rule color='Attribute'>&quot;(?:[^&quot;\\\\]|\\\\.)*&quot;(?=\\s*:)</Rule>");
            if (l.Yaml) sb.Append("<Rule color='Attribute'>^\\s*-?\\s*[\\w.\\-\"']+(?=\\s*:(\\s|$))</Rule><Rule color='Preproc'>^---$|^\\.\\.\\.$</Rule><Rule color='Variable'>[&amp;*][\\w-]+</Rule>");
            if (l.KeysBeforeEquals) sb.Append("<Rule color='Tag'>^\\s*\\[[^\\]]*\\]</Rule><Rule color='Attribute'>^\\s*[\\w.\\-]+(?=\\s*[=:])</Rule>");
            if (l.Css) sb.Append("<Rule color='Attribute'>[\\w-]+(?=\\s*:[^:{]*[;}]?$)|[\\w-]+(?=\\s*:\\s)</Rule><Rule color='Tag'>[.#][\\w-]+(?=[^;{]*\\{)</Rule><Rule color='Variable'>[$@]?--[\\w-]+|\\$[\\w-]+</Rule>");
            if (l.Triple)
            {
                sb.Append("<Span color='String' multiline='true' begin='[rRbBfFuU]{0,2}&quot;&quot;&quot;' end='&quot;&quot;&quot;'/>");
                sb.Append("<Span color='String' multiline='true' begin=\"[rRbBfFuU]{0,2}'''\" end=\"'''\"/>");
            }
            if (l.DoubleQuote) sb.Append("<Span color='String' begin='&quot;' end='&quot;'><RuleSet><Span begin='\\\\' end='.'/></RuleSet></Span>");
            if (l.SingleQuote) sb.Append("<Span color='String' begin=\"'\" end=\"'\"><RuleSet><Span begin='\\\\' end='.'/></RuleSet></Span>");
            if (l.Backtick) sb.Append("<Span color='String' multiline='true' begin='`' end='`'/>");
            if (l.Decorators) sb.Append("<Rule color='Preproc'>@[A-Za-z_][\\w.]*</Rule>");
            if (l.DollarVars) sb.Append("<Rule color='Variable'>\\$\\{[^}]*\\}|\\$[A-Za-z_][\\w]*|\\$[0-9@#?*!$-]|\\$\\(</Rule>");

            void Words(string color, string words)
            {
                var list = words.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
                var plain = list.Where(w => System.Text.RegularExpressions.Regex.IsMatch(w, @"^[A-Za-z_][\w]*$")).ToList();
                var odd = list.Except(plain).ToList();
                if (plain.Count > 0) sb.Append($"<Keywords color='{color}'>" + string.Concat(plain.Select(w => $"<Word>{X(w)}</Word>")) + "</Keywords>");
                foreach (var w in odd) sb.Append($"<Rule color='{color}'>(?&lt;![\\w$@-]){Rx(w)}(?![\\w-])</Rule>");
            }
            Words("Keyword", l.Keywords);
            Words("Type", l.Types);
            Words("Constant", l.Constants);
            sb.Append("<Rule color='Type'>\\b[A-Z][a-z0-9]+[A-Z]\\w*\\b</Rule>");     // PascalCase identifiers
            sb.Append("<Rule color='Function'>\\b[A-Za-z_][\\w]*(?=\\s*\\()</Rule>");
            sb.Append("<Rule color='Number'>\\b0[xX][0-9a-fA-F_]+\\b|\\b0[bB][01_]+\\b|\\b\\d[\\d_]*(\\.\\d+)?([eE][+-]?\\d+)?[fFdDlLuUmM]?\\b</Rule>");
            sb.Append("</RuleSet></SyntaxDefinition>");
            return sb.ToString();
        }

        // ---------------- fenced blocks in notes ----------------

        public sealed class Fence
        {
            public int Open, Close;      // line numbers of the ``` lines (Close = -1 when unclosed)
            public string Lang;
            public IHighlightingDefinition Def;
        }

        sealed class FenceMap
        {
            public int[] LineToFence;    // index into Fences, -1 = none
            public List<Fence> Fences;
            public bool Dirty = true;
        }

        static readonly ConditionalWeakTable<TextDocument, FenceMap> Maps = new ConditionalWeakTable<TextDocument, FenceMap>();

        static FenceMap MapOf(TextDocument doc)
        {
            var m = Maps.GetValue(doc, d =>
            {
                var fm = new FenceMap();
                d.Changed += (s, e) => fm.Dirty = true;
                return fm;
            });
            if (!m.Dirty && m.LineToFence != null && m.LineToFence.Length == doc.LineCount + 1) return m;
            var map = new int[doc.LineCount + 1];
            for (int i = 0; i < map.Length; i++) map[i] = -1;
            var fences = new List<Fence>();
            Fence open = null;
            string marker = null;
            foreach (var line in doc.Lines)
            {
                if (line.Length >= 3)
                {
                    var t = doc.GetText(line).Trim();
                    if (open == null && (t.StartsWith("```") || t.StartsWith("~~~")))
                    {
                        marker = t.Substring(0, 3);
                        var lang = t.TrimStart('`', '~').Trim();
                        open = new Fence { Open = line.LineNumber, Close = -1, Lang = lang, Def = ForFence(lang) };
                        fences.Add(open);
                        map[line.LineNumber] = fences.Count - 1;
                        continue;
                    }
                    if (open != null && t.StartsWith(marker) && t.TrimStart(marker[0]).Length == 0)
                    {
                        open.Close = line.LineNumber;
                        map[line.LineNumber] = fences.Count - 1;
                        open = null;
                        continue;
                    }
                }
                if (open != null) map[line.LineNumber] = fences.Count - 1;
            }
            m.LineToFence = map;
            m.Fences = fences;
            m.Dirty = false;
            return m;
        }

        /// The fence this line belongs to (including its ``` lines), or null.
        public static Fence FenceAt(TextDocument doc, int lineNumber)
        {
            var m = MapOf(doc);
            if (lineNumber < 1 || lineNumber >= m.LineToFence.Length) return null;
            int i = m.LineToFence[lineNumber];
            return i < 0 ? null : m.Fences[i];
        }

        public static IEnumerable<Fence> Fences(TextDocument doc) => MapOf(doc).Fences;

        /// True when note features (task markers, images, comments) must not touch this line:
        /// the document is a code file, or the line is inside a ``` block.
        public static bool IsCodeLine(TextDocument doc, int lineNumber) =>
            IsCodeFile(doc.FileName) || FenceAt(doc, lineNumber) != null;

        // per-block highlighting cache: (lang + block text) -> per line sections
        sealed class Sect { public int Offset, Length; public HighlightingColor Color; }
        static readonly Dictionary<string, List<Sect>[]> BlockCache = new Dictionary<string, List<Sect>[]>();

        static List<Sect>[] Highlight(TextDocument doc, Fence f)
        {
            int first = f.Open + 1, last = (f.Close > 0 ? f.Close : doc.LineCount + 1) - 1;
            if (last < first || f.Def == null) return null;
            var start = doc.GetLineByNumber(first).Offset;
            var end = doc.GetLineByNumber(last).EndOffset;
            var text = doc.GetText(start, end - start);
            var key = f.Lang + "\u0001" + text;
            if (BlockCache.TryGetValue(key, out var cached)) return cached;
            if (BlockCache.Count > 300) BlockCache.Clear();

            var tmp = new TextDocument(text);
            var hl = new DocumentHighlighter(tmp, f.Def);
            var result = new List<Sect>[tmp.LineCount];
            for (int n = 1; n <= tmp.LineCount; n++)
            {
                var line = tmp.GetLineByNumber(n);
                var list = new List<Sect>();
                try
                {
                    foreach (var s in hl.HighlightLine(n).Sections)
                        list.Add(new Sect { Offset = s.Offset - line.Offset, Length = s.Length, Color = s.Color });
                }
                catch { }
                result[n - 1] = list;
            }
            BlockCache[key] = result;
            return result;
        }

        static readonly Dictionary<string, Brush> BrushCache = new Dictionary<string, Brush>();
        public static Brush B(string hex)
        {
            if (!BrushCache.TryGetValue(hex, out var b)) { b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); BrushCache[hex] = b; }
            return b;
        }

        static Brush _blockBrush;
        public static Brush BlockBrush
        {
            get
            {
                if (_blockBrush == null)
                {
                    var c = (Color)ColorConverter.ConvertFromString(Current.Bg);
                    var b = new SolidColorBrush(c);
                    b.Freeze();
                    _blockBrush = b;
                }
                return _blockBrush;
            }
        }

        /// Colours code inside ``` fences of notes.
        public sealed class FenceColorizer : DocumentColorizingTransformer
        {
            protected override void ColorizeLine(DocumentLine line)
            {
                var doc = CurrentContext.Document;
                if (IsCodeFile(doc.FileName)) return;
                var f = FenceAt(doc, line.LineNumber);
                if (f == null) return;
                var plain = B(Current.C["Plain"]);
                if (line.LineNumber == f.Open || line.LineNumber == f.Close)
                {
                    if (line.Length > 0)
                        ChangeLinePart(line.Offset, line.EndOffset, el => el.TextRunProperties.SetForegroundBrush(Theme.F(Theme.FgDim)));
                    return;
                }
                if (line.Length > 0) ChangeLinePart(line.Offset, line.EndOffset, el => el.TextRunProperties.SetForegroundBrush(plain));
                var sections = Highlight(doc, f);
                if (sections == null) return;
                int idx = line.LineNumber - f.Open - 1;
                if (idx < 0 || idx >= sections.Length) return;
                foreach (var s in sections[idx])
                {
                    int a = line.Offset + Math.Max(0, s.Offset), b = Math.Min(line.EndOffset, line.Offset + s.Offset + s.Length);
                    if (b <= a) continue;
                    var color = s.Color;
                    var brush = color.Foreground?.GetBrush(null);
                    ChangeLinePart(a, b, el =>
                    {
                        if (brush != null) el.TextRunProperties.SetForegroundBrush(brush);
                        if (color.FontStyle.HasValue || color.FontWeight.HasValue)
                        {
                            var tf = el.TextRunProperties.Typeface;
                            el.TextRunProperties.SetTypeface(new Typeface(tf.FontFamily, color.FontStyle ?? tf.Style, color.FontWeight ?? tf.Weight, tf.Stretch));
                        }
                    });
                }
            }
        }

        /// Rounded background behind ``` blocks.
        public sealed class FenceBackground : IBackgroundRenderer
        {
            public KnownLayer Layer => KnownLayer.Background;

            public void Draw(TextView tv, DrawingContext dc)
            {
                var doc = tv.Document;
                if (doc == null || IsCodeFile(doc.FileName) || !tv.VisualLinesValid || tv.VisualLines.Count == 0) return;
                int firstVisible = tv.VisualLines.First().FirstDocumentLine.LineNumber;
                int lastVisible = tv.VisualLines.Last().LastDocumentLine.LineNumber;
                var border = new Pen(Theme.F(Theme.ChromeBorder), 1);
                foreach (var f in Fences(doc))
                {
                    int close = f.Close > 0 ? f.Close : doc.LineCount;
                    if (close < firstVisible || f.Open > lastVisible) continue;
                    var top = tv.GetVisualTopByDocumentLine(Math.Max(f.Open, firstVisible)) - tv.VerticalOffset;
                    var lastLine = tv.GetVisualLine(Math.Min(close, lastVisible));
                    double bottom = lastLine != null ? lastLine.VisualTop + lastLine.Height - tv.VerticalOffset
                                                     : tv.GetVisualTopByDocumentLine(Math.Min(close, lastVisible)) - tv.VerticalOffset + tv.DefaultLineHeight;
                    var rect = new Rect(2, top + 1, Math.Max(0, tv.ActualWidth - 16), Math.Max(0, bottom - top - 2));
                    dc.DrawRoundedRectangle(BlockBrush, border, rect, 6, 6);
                    // language label in the top-right corner
                    if (f.Lang.Length > 0 && f.Open >= firstVisible)
                    {
                        var ft = new FormattedText(f.Lang.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                            new Typeface("Segoe UI"), 11, Theme.F(Theme.FgDim), VisualTreeHelper.GetDpi(tv).PixelsPerDip);
                        dc.DrawText(ft, new Point(rect.Right - ft.Width - 10, top + 4));
                    }
                }
            }
        }
    }
}
