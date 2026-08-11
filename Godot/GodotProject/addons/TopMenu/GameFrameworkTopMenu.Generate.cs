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
/// 1. Localization File —— 原 LocalizationEditorPlugin：Configs/Localization/*.xlsx → DataTables/Localizations/*.txt
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
    /// 将 Configs/Localization/*.xlsx 的每个 sheet 导出为 DataTables/Localizations/{sheet}.txt。
    /// 原 LocalizationEditorPlugin 功能。
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

        int count = 0;
        foreach (string excelFile in Directory.GetFiles(LocalizationSourceDir, "*.xlsx"))
        {
            GD.Print($"[GameFramework] 处理: {Path.GetFileName(excelFile)}");
            ProcessExcel(excelFile, outputDir);
            count++;
        }

        EditorInterface.Singleton.GetResourceFilesystem().Scan();
        GD.Print($"[GameFramework] 本地化生成完成，共处理 {count} 个文件。");
    }

    private void ProcessExcel(string excelPath, string outputDir)
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
