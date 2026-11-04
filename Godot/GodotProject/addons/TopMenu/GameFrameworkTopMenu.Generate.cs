#if TOOLS
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

/// <summary>
/// GameFrameworkTopMenu 的分部类：承载 "Generate File" 子菜单的数据与三套生成逻辑。
/// 1. Localization File —— 原 LocalizationEditorPlugin：Configs/Localization/*.xlsx → DataTables/Localizations/*.txt，
///    同时把全部本地化键汇总生成 Framework/GodotGameFrameworkCore/Localization/LocalizationKeyConstants.cs
/// 2. GameConfig File —— Luban：启动 gen_code_bin_to_project.bat/.sh 生成配置代码
/// 3. Collection Res —— 原 ResourcesCollectionEditor：扫描 TheGame 资源 → ResourcesCollectionConstant.cs
/// </summary>
public partial class GameFrameworkTopMenu
{
    // ---- Generate 子菜单项（Label 供 _EnterTree 填充菜单；Define 为关联路径，供各处理器使用） ----
    private static readonly (string Label, string Define)[] Generate = new[]
    {
        ("Localization File", LocalizationSourceDir),
        ("GameConfig File", GameConfigScriptDir),
        ("Collection Res", CollectionResSource),
    };

    // ---- 路径常量 ----
    private static readonly string LocalizationSourceDir =
        Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../../Configs/Localization/"));
    private const string LocalizationOutputPath = "res://TheGame/DataTables/Localizations/";

    /// <summary>本地化键常量文件：随 Localization File 一并生成，纯生成物不要手改。</summary>
    private const string LocalizationKeyConstantsOutput =
        "res://Framework/GodotGameFrameworkCore/Localization/LocalizationKeyConstants.cs";
    private const string LocalizationKeyConstantsNamespace = "GodotGameFramework.Localization";
    private const string LocalizationKeyConstantsClassName = "LocalizationKeyConstants";

    private static readonly string GameConfigScriptDir =
        Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../../Configs/GameConfig/"));

    private const string CollectionResSource = "res://TheGame/";
    private const string CollectionResOutput = "res://TheGame/GameScripts/GameProto/ResourcesCollectionConstant.cs";

    // ---- Excel 命名空间 ----
    private static readonly XNamespace Ns =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private void OnGeneratePopupIdPressed(long id)
    {
        int index = (int)id;
        if (index < 0 || index >= Generate.Length)
        {
            return;
        }

        try
        {
            switch (index)
            {
                case 0: GenerateLocalizationFiles(); break;
                case 1: RunGameConfigGeneration(); break;
                case 2: CollectionRes(); break;
                default: break;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameFramework] 生成失败: {ex.Message}");
        }
    }

    // ============================ Generate: Localization ============================

    /// <summary>
    /// 将 Configs/Localization/*.xlsx 的每个 sheet 导出为 DataTables/Localizations/{sheet}.txt，
    /// 并把所有 sheet 中的本地化键汇总，生成 Framework/GodotGameFrameworkCore/Localization/LocalizationKeyConstants.cs。
    /// 原 LocalizationEditorPlugin 功能 + 本地化键常量生成。
    /// </summary>
    private void GenerateLocalizationFiles()
    {
        if (!Directory.Exists(LocalizationSourceDir))
        {
            GD.PrintErr($"[GameFramework] 未找到源目录: {LocalizationSourceDir}");
            return;
        }

        string outputDir = ProjectSettings.GlobalizePath(LocalizationOutputPath);
        Directory.CreateDirectory(outputDir);

        // 排序：Directory.GetFiles 的返回顺序由文件系统决定，排序后键常量的生成结果才稳定（diff 不抖动）
        string[] excelFiles = Directory.GetFiles(LocalizationSourceDir, "*.xlsx");
        Array.Sort(excelFiles, StringComparer.OrdinalIgnoreCase);

        // 汇总所有 sheet 的键：同一个键会在每种语言的 sheet 里各出现一次，最后统一去重
        var collectedKeys = new List<string>();

        int fileCount = 0;
        foreach (string excelFile in excelFiles)
        {
            GD.Print($"[GameFramework] 处理: {Path.GetFileName(excelFile)}");
            ProcessExcel(excelFile, outputDir, collectedKeys);
            fileCount++;
        }

        int keyCount = GenerateLocalizationKeyConstants(collectedKeys);

        EditorInterface.Singleton.GetResourceFilesystem().Scan();
        GD.Print($"[GameFramework] 本地化生成完成，共处理 {fileCount} 个文件、{keyCount} 个键。");
    }

    private void ProcessExcel(string excelPath, string outputDir, List<string> keyCollector)
    {
        try
        {
            using (ZipArchive archive = ZipFile.OpenRead(excelPath))
            {
                List<string> sharedStrings = ParseSharedStrings(archive);
                Dictionary<string, string> sheets = ParseSheets(archive);

                foreach (var kv in sheets)
                {
                    string sheetName = SanitizeName(kv.Key);
                    string sheetFile = kv.Value;

                    string tsv = ParseSheetToTsv(archive, sheetFile, sharedStrings);
                    if (string.IsNullOrEmpty(tsv))
                        continue;

                    // 键（C 列）在此顺手收集，避免二次解析 xlsx
                    CollectLocalizationKeys(tsv, sheetName, keyCollector);

                    if (File.Exists(Path.Combine(outputDir, $"{sheetName}.txt")))
                        File.Delete(Path.Combine(outputDir, $"{sheetName}.txt"));
                    File.WriteAllText(Path.Combine(outputDir, $"{sheetName}.txt"), tsv, Encoding.UTF8);
                    GD.Print($"  → {sheetName}.txt");
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"  失败: {ex.Message}");
        }
    }

    // ============================ Generate: Localization Key Constants ============================

    /// <summary>
    /// 从导出的 TSV 中收集键（C 列）：跳过空行与 '#' 开头的表头行，列数不足 3 的行忽略。
    /// 同一个 sheet 内键重复会让运行时字典解析失败，这里提前报到编辑器控制台。
    /// </summary>
    private static void CollectLocalizationKeys(string tsv, string sheetName, List<string> keys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string rawLine in tsv.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] columns = line.Split('\t');
            if (columns.Length < 3)
            {
                continue;
            }

            string key = columns[2].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            if (!seen.Add(key))
            {
                GD.PrintErr($"[GameFramework]   Sheet '{sheetName}' 有重复键: {key}");
            }

            keys.Add(key);
        }
    }

    /// <summary>
    /// 把收集到的全部本地化键去重排序后，生成 LocalizationKeyConstants.cs（静态类 + 嵌套静态类 + const），
    /// 让业务代码用常量代替硬编码键字符串：
    ///   · "StartGame"      →  public const string StartGame = "StartGame";
    ///   · "MainForm.Score" →  public static class MainForm { public const string Score = "MainForm.Score"; }
    /// 另外生成 All 数组，便于运行时校验 / 遍历全部键。
    /// 文件已存在时整体覆盖（纯生成物，不要手改）。
    /// </summary>
    /// <returns>生成的键数量（0 表示未生成）。</returns>
    private int GenerateLocalizationKeyConstants(List<string> collectedKeys)
    {
        // 去重 + 升序：同一个键在每种语言的 sheet 里都会出现一次；升序保证每次生成结果一致
        List<string> keys = collectedKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        if (keys.Count == 0)
        {
            GD.PrintErr("[GameFramework] 未解析到任何本地化键，跳过 LocalizationKeyConstants 生成。");
            return 0;
        }

        var root = new LocalizationKeyNode();
        foreach (string key in keys)
        {
            root.AddKey(key);
        }

        var sb = new StringBuilder();
        WriteLine(sb, "//------------------------------------------------------------");
        WriteLine(sb, "// Localization Key Constants");
        WriteLine(sb, "// Auto-generated by GameFrameworkTopMenu plugin.");
        WriteLine(sb, "// Source: Configs/Localization/*.xlsx");
        WriteLine(sb, "// Do not modify manually.");
        WriteLine(sb, $"// Generation time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        WriteLine(sb, "//------------------------------------------------------------");
        sb.Append('\n');
        WriteLine(sb, $"namespace {LocalizationKeyConstantsNamespace}");
        WriteLine(sb, "{");
        WriteLine(sb, "\t/// <summary>");
        WriteLine(sb, $"\t/// 本地化键常量（由 Excel 表自动生成，覆盖全部 {keys.Count} 个键，不要手改）。");
        WriteLine(sb, "\t/// 用法：GF.Localization.GetString(LocalizationKeyConstants.StartGame)");
        WriteLine(sb, "\t/// </summary>");
        WriteLine(sb, $"\tpublic static class {LocalizationKeyConstantsClassName}");
        WriteLine(sb, "\t{");
        root.Emit(sb, "\t\t");
        sb.Append('\n');
        WriteLine(sb, "\t\t/// <summary>全部本地化键（去重、升序）。</summary>");
        WriteLine(sb, "\t\tpublic static readonly string[] All =");
        WriteLine(sb, "\t\t{");
        foreach (string key in keys)
        {
            WriteLine(sb, $"\t\t\t\"{EscapeCSharpString(key)}\",");
        }
        WriteLine(sb, "\t\t};");
        WriteLine(sb, "\t}");
        WriteLine(sb, "}");

        string outputPath = ProjectSettings.GlobalizePath(LocalizationKeyConstantsOutput);
        string outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // 与仓库内既有源码保持一致：UTF-8 无 BOM
        File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(false));

        GD.Print($"[GameFramework] 已生成 {LocalizationKeyConstantsOutput}（{keys.Count} 个键）。");
        return keys.Count;
    }

    /// <summary>按 '\n' 追加一行：统一 LF，与仓库内源码的换行风格一致。</summary>
    private static void WriteLine(StringBuilder sb, string line)
    {
        sb.Append(line).Append('\n');
    }

    /// <summary>转义 C# 字符串字面量中的反斜杠与双引号。</summary>
    private static string EscapeCSharpString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>C# 保留关键字（生成成员名时需回避）。</summary>
    private static readonly HashSet<string> CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit",
        "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
        "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out",
        "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
        "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile",
        "while",
    };

    /// <summary>
    /// 把键分段净化成合法的 C# 标识符：非法字符转 '_'、数字开头补 '_'、C# 关键字加后缀 '_'。
    /// 注意 char.IsLetter 包含中文，所以中文键可以直接作为成员名。
    /// </summary>
    private static string ToIdentifier(string segment)
    {
        var sb = new StringBuilder(segment.Length + 1);
        foreach (char c in segment)
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        string name = sb.Length > 0 ? sb.ToString() : "_";
        if (char.IsDigit(name[0]))
        {
            name = "_" + name;
        }

        if (CSharpKeywords.Contains(name))
        {
            name += "_";
        }

        return name;
    }

    /// <summary>
    /// 本地化键树节点：按键名的 '.' 分段建树，生成时映射为「嵌套静态类 + const」，
    /// 例如键 MainForm.Score → LocalizationKeyConstants.MainForm.Score。
    /// </summary>
    private sealed class LocalizationKeyNode
    {
        /// <summary>本节点在生成代码中的成员名（已净化成合法且同级唯一的标识符）。</summary>
        public string Name;

        /// <summary>若本节点本身就是一个完整键，则为该键；否则为 null。</summary>
        public string Value;

        /// <summary>子节点，键为原始键分段。</summary>
        public readonly Dictionary<string, LocalizationKeyNode> Children =
            new Dictionary<string, LocalizationKeyNode>(StringComparer.Ordinal);

        /// <summary>把一条完整键插入树中（形如 "A..B" 的空分段忽略）。</summary>
        public void AddKey(string key)
        {
            LocalizationKeyNode node = this;

            foreach (string rawSegment in key.Split('.'))
            {
                string segment = rawSegment.Trim();
                if (segment.Length == 0)
                {
                    continue;
                }

                if (!node.Children.TryGetValue(segment, out LocalizationKeyNode child))
                {
                    child = new LocalizationKeyNode
                    {
                        Name = node.MakeUniqueName(ToIdentifier(segment)),
                    };
                    node.Children.Add(segment, child);
                }

                node = child;
            }

            if (!ReferenceEquals(node, this))
            {
                node.Value = key;
            }
        }

        /// <summary>同级成员名去重：净化后撞名时追加 _2、_3…</summary>
        private string MakeUniqueName(string baseName)
        {
            string name = baseName;
            int suffix = 2;
            while (Children.Values.Any(child => string.Equals(child.Name, name, StringComparison.Ordinal)))
            {
                name = $"{baseName}_{suffix++}";
            }

            return name;
        }

        /// <summary>
        /// 递归生成类体：先输出常量（本层就是完整键的部分），再输出嵌套类（还有更深层的键）。
        /// 若同一节点既是完整键又含有子键，类名占用原成员名，常量加后缀 '_' 避让。
        /// </summary>
        public void Emit(StringBuilder sb, string indent)
        {
            var children = Children.Values
                .OrderBy(child => child.Name, StringComparer.Ordinal)
                .ToList();

            // 同名成员（const 与 class）在 C# 里不合法，生成时统一在同一作用域内查重
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (LocalizationKeyNode child in children)
            {
                if (child.Children.Count > 0)
                {
                    usedNames.Add(child.Name);
                }
            }

            bool wroteAny = false;

            foreach (LocalizationKeyNode child in children)
            {
                if (child.Value == null)
                {
                    continue;
                }

                string constName = child.Children.Count > 0 ? child.Name + "_" : child.Name;
                while (!usedNames.Add(constName))
                {
                    constName += "_";
                }

                WriteLine(sb, $"{indent}public const string {constName} = \"{EscapeCSharpString(child.Value)}\";");
                wroteAny = true;
            }

            foreach (LocalizationKeyNode child in children)
            {
                if (child.Children.Count == 0)
                {
                    continue;
                }

                if (wroteAny)
                {
                    sb.Append('\n');
                }

                WriteLine(sb, $"{indent}public static class {child.Name}");
                WriteLine(sb, $"{indent}{{");
                child.Emit(sb, indent + "\t");
                WriteLine(sb, $"{indent}}}");
                wroteAny = true;
            }
        }
    }

    private List<string> ParseSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry == null) return new List<string>();

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        return doc.Root.Elements(Ns + "si").Select(si =>
        {
            var t = si.Element(Ns + "t");
            if (t != null) return t.Value;
            return string.Concat(si.Elements(Ns + "r").Elements(Ns + "t").Select(e => e.Value));
        }).ToList();
    }

    private Dictionary<string, string> ParseSheets(ZipArchive archive)
    {
        var result = new Dictionary<string, string>();
        var rels = new Dictionary<string, string>();

        var relEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (relEntry != null)
        {
            using var stream = relEntry.Open();
            var doc = XDocument.Load(stream);
            foreach (var rel in doc.Root.Elements())
            {
                string id = rel.Attribute("Id")?.Value;
                string target = rel.Attribute("Target")?.Value;
                if (id != null && target != null)
                    rels[id] = "xl/" + target.Replace('\\', '/');
            }
        }

        var wbEntry = archive.GetEntry("xl/workbook.xml");
        if (wbEntry == null) return result;

        using (var stream = wbEntry.Open())
        {
            var doc = XDocument.Load(stream);
            var sheetsEl = doc.Root?.Element(Ns + "sheets");
            if (sheetsEl == null) return result;

            foreach (var sheet in sheetsEl.Elements(Ns + "sheet"))
            {
                string name = sheet.Attribute("name")?.Value;
                string rId = sheet.Attribute(RelNs + "id")?.Value;
                if (name != null && rId != null && rels.TryGetValue(rId, out string path))
                    result[name] = path;
            }
        }

        return result;
    }

    private string ParseSheetToTsv(ZipArchive archive, string sheetFile, List<string> sharedStrings)
    {
        var entry = archive.GetEntry(sheetFile);
        if (entry == null) return null;

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        var sheetData = doc.Root?.Element(Ns + "sheetData");
        if (sheetData == null) return null;

        var sb = new StringBuilder();

        foreach (var row in sheetData.Elements(Ns + "row"))
        {
            int lastCol = -1;
            foreach (var cell in row.Elements(Ns + "c"))
            {
                string refStr = cell.Attribute("r")?.Value;
                if (string.IsNullOrEmpty(refStr)) continue;

                int colIndex = CellRefToColIndex(refStr);
                if (colIndex > 3) continue; // 只输出 A-D

                // 首个单元格输出 colIndex 个 tab；后续输出 (colIndex - lastCol) 个
                int tabs = lastCol < 0 ? colIndex : colIndex - lastCol;
                for (int i = 0; i < tabs; i++)
                    sb.Append('\t');

                sb.Append(GetCellValue(cell, sharedStrings));
                lastCol = colIndex;
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static int CellRefToColIndex(string cellRef)
    {
        // "B6" -> "B", "AA1" -> "AA"
        int i = 0;
        while (i < cellRef.Length && char.IsLetter(cellRef[i]))
            i++;
        string col = cellRef.Substring(0, i);

        // A=0, B=1, ..., Z=25, AA=26, ...
        int result = 0;
        foreach (char c in col)
            result = result * 26 + (c - 'A' + 1);
        return result - 1;
    }

    private string GetCellValue(XElement cell, List<string> sharedStrings)
    {
        string type = cell.Attribute("t")?.Value ?? "";
        string v = cell.Element(Ns + "v")?.Value ?? "";

        if (type == "s" && int.TryParse(v, out int si) && si >= 0 && si < sharedStrings.Count)
            return sharedStrings[si];

        if (type == "inlineStr")
        {
            var isEl = cell.Element(Ns + "is");
            return isEl?.Element(Ns + "t")?.Value ?? "";
        }

        return v;
    }

    private string SanitizeName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    // ============================ Generate: GameConfig (Luban) ============================

    /// <summary>
    /// 启动 Luban 配置生成脚本（gen_code_bin_to_project.bat/.sh），生成 GameConfig C# 代码与二进制数据。
    /// 脚本内部自带 cd /d %~dp0，工作目录无需额外指定。
    /// </summary>
    private void RunGameConfigGeneration()
    {
        string script;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            script = Path.Combine(GameConfigScriptDir, "gen_code_bin_to_project.bat");
        }
        else
        {
            script = Path.Combine(GameConfigScriptDir, "gen_code_bin_to_project.sh");
        }

        if (!File.Exists(script))
        {
            GD.PrintErr($"[GameFramework] 未找到 Luban 生成脚本: {script}");
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                WorkingDirectory = GameConfigScriptDir,
                UseShellExecute = true,
            };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi.FileName = script;
            }
            else
            {
                psi.FileName = "/bin/bash";
                psi.Arguments = $"\"{script}\"";
                psi.UseShellExecute = false;
            }
            Process.Start(psi);
            GD.Print($"[GameFramework] 已启动 GameConfig 生成脚本: {Path.GetFileName(script)}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameFramework] 启动生成脚本失败: {ex.Message}");
        }
    }

    // ============================ Generate: Collection Res ============================

    /// <summary>
    /// 收集 TheGame 文件夹下所有非脚本资源文件路径，生成到 ResourcesCollectionConstant.cs。
    /// 使用 DirAccess 遍历以保证路径分隔符统一为 '/'。
    /// 发现同名文件（不同路径）时会报错并中止生成。
    /// 原 ResourcesCollectionEditor 功能。
    /// </summary>
    private void CollectionRes()
    {
        // 使用 DirAccess 遍历 Godot 资源目录，保证路径统一为 "res://" 格式
        var allFiles = new List<string>();
        var dir = DirAccess.Open(CollectionResSource);
        if (dir == null)
        {
            GD.PrintErr($"[GameFramework] Can not open directory: {CollectionResSource}");
            return;
        }

        CollectFilesRecursive(dir, CollectionResSource, allFiles);

        // 过滤规则：
        //   1. 排除 .cs 脚本文件
        //   2. 排除 GameScripts/ 目录下的所有文件（包含 .cs 和可能的元数据）
        //   3. 排除 .import 文件（Godot 资源导入元数据，非实际资源）
        var resourceFiles = allFiles
            .Where(f => !f.EndsWith(".cs"))
            .Where(f => !f.StartsWith("res://TheGame/GameScripts/"))
            .Where(f => !f.EndsWith(".import"))
            .Where(f => !f.EndsWith(".uid"))
            .OrderBy(f => f)
            .ToList();

        if (resourceFiles.Count == 0)
        {
            GD.Print("[GameFramework] No resource files found.");
            return;
        }

        // 检查同名文件（不同路径）：如果有相同文件名（不含扩展名）直接报错
        var duplicateNames = resourceFiles
            .GroupBy(f => Path.GetFileNameWithoutExtension(f))
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicateNames.Count > 0)
        {
            GD.PrintErr("[GameFramework] 有相同文件，请修改");
            foreach (var group in duplicateNames)
            {
                GD.PrintErr($"  Duplicate name: \"{group.Key}\"");
                foreach (var f in group)
                {
                    GD.PrintErr($"    - {f}");
                }
            }
            return;
        }

        var lines = new List<string>();

        foreach (var file in resourceFiles)
        {
            string constName = Path.GetFileNameWithoutExtension(file);

            //获得文件所在文件夹名
            string folderName = Path.GetFileName(Path.GetDirectoryName(file));

            // 常量名不能以数字开头
            if (constName.Length > 0 && char.IsDigit(constName[0]))
                constName = $"_{constName}";

            lines.Add($"\t\tpublic const string {folderName}_{constName} = \"{file}\";");
        }

        // 生成完整 C# 文件内容
        string fileContent = $"//------------------------------------------------------------\n" +
            $"// Resources Collection Constant\n" +
            $"// Auto-generated by GameFrameworkTopMenu plugin.\n" +
            $"// Do not modify manually.\n" +
            $"// Generation time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
            $"//------------------------------------------------------------\n" +
            $"\n" +
            $"namespace GameConfig.Constant\n" +
            $"{{\n" +
            $"\tpublic static class ResourcesCollectionConstant\n" +
            $"{{\n" +
            string.Join("\n", lines) + "\n" +
            $"\t}}\n" +
            $"}}\n";

        string outputPath = ProjectSettings.GlobalizePath(CollectionResOutput);
        File.WriteAllText(outputPath, fileContent);

        GD.Print($"[GameFramework] Collection complete. {resourceFiles.Count} resources saved to: {CollectionResOutput}");

        // 刷新文件系统让编辑器立即看到新文件
        EditorInterface.Singleton.GetResourceFilesystem().Scan();
    }

    private static void CollectFilesRecursive(DirAccess dir, string currentPath, List<string> results)
    {
        dir.ListDirBegin();

        while (true)
        {
            string fileName = dir.GetNext();
            if (string.IsNullOrEmpty(fileName))
                break;

            if (fileName == "." || fileName == "..")
                continue;

            string fullPath = currentPath.TrimEnd('/') + "/" + fileName;

            if (dir.CurrentIsDir())
            {
                var subDir = DirAccess.Open(fullPath);
                if (subDir != null)
                {
                    CollectFilesRecursive(subDir, fullPath, results);
                }
            }
            else
            {
                results.Add(fullPath);
            }
        }

        dir.ListDirEnd();
    }
}
#endif
