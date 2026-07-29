#if TOOLS
using GameConfig.Constant;
using GameFramework;
using GameFramework.Resource;
using Godot;
using GodotGameFramework.Json;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GodotGameFramework.Editor
{
    /// <summary>
    /// AB 包导出管理面板 —— 可视化编辑所有 AssetBundle 资源，
    /// 展开可查看每个包里的具体文件，一键切换启用/导出状态，
    /// 选择导出目标文件夹，快速打包。
    /// </summary>
    [Tool]
    public partial class ExportInspector : EditorPlugin
    {
        // ═══════════════════════════════════════════════════════════
        //  内部类型
        // ═══════════════════════════════════════════════════════════

        private sealed class BundleInfo
        {
            public string Path;               // res:// 资源路径
            public string FolderPath;         // 所在文件夹
            public string Name;
            public bool Enabled;
            public bool ExportEnabled;
            public bool PackExternalDeps;
            public bool ExportOnlyImported;
            public long TotalSize;
            public List<ResourceItem> Resources = new();
        }

        private sealed class ResourceItem
        {
            public string Name;
            public string FullPath;           // res:// 路径
            public string TypeLabel;          // 中文类型名
            public long FileSize;
            public bool HasImport;            // 是否有 .import 文件
            public ImportStatus ImportStatus; // 导入产物状态
        }

        private enum ImportStatus
        {
            NotApplicable,   // 无需导入（.tscn/.tres/.glb 等）
            Ready,           // 已导入，缓存存在
            Missing,         // 有 .import 但缓存文件缺失
        }

        private const string EditorSettingExportFolder = "godot_asset_bundle/export_folder";
        private const string EditorSettingVersion = "godot_asset_bundle/version";

        // ═══════════════════════════════════════════════════════════
        //  控件
        // ═══════════════════════════════════════════════════════════

        private Control _dockPanel;
        private Tree _bundleTree;
        private Button _btnRefresh;
        private Button _btnExportFolder;
        private Button _openExportFolder;
        private LineEdit _txtVersion;   // 版本号输入
        private Button _btnExportAll;
        private LineEdit _txtExportFolder;
        private Label _lblSummary;   // 底部的状态栏
        private TreeItem _treeRoot;

        private string _exportFolder = "";
        private string _version = "1.0.0";
        private readonly Dictionary<string, Godot.Resource> _bundles = new();

        // ═══════════════════════════════════════════════════════════
        //  生命周期
        // ═══════════════════════════════════════════════════════════

        public override void _EnterTree()
        {
            LoadExportFolderPreference();
            LoadVersionPreference();
            BuildUI();
#pragma warning disable CS0618
            AddControlToDock(DockSlot.RightUl, _dockPanel);
#pragma warning restore CS0618
            RefreshBundleList();
        }

        public override void _ExitTree()
        {
#pragma warning disable CS0618
            RemoveControlFromDocks(_dockPanel);
#pragma warning restore CS0618
            _dockPanel?.QueueFree();
        }

        // ═══════════════════════════════════════════════════════════
        //  UI 构建
        // ═══════════════════════════════════════════════════════════

        private void BuildUI()
        {
            _dockPanel = new MarginContainer
            {
                Name = "ExportInspectorDock",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            };

            var vbox = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            };
            _dockPanel.AddChild(vbox);

            // ── 标题行 ──
            var titleLabel = new Label
            {
                Text = "📦 AssetBundle 导出管理",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            titleLabel.AddThemeFontSizeOverride("font_size", 16);
            vbox.AddChild(titleLabel);
            vbox.AddChild(new HSeparator());

            // ── 导出目录选择 ──
            var folderRow = new HBoxContainer();
            folderRow.AddChild(new Label { Text = "导出目录:" });

            _txtExportFolder = new LineEdit
            {
                Text = _exportFolder,
                PlaceholderText = "选择导出目标文件夹...",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                Editable = false,
            };
            folderRow.AddChild(_txtExportFolder);

            _btnExportFolder = new Button { Text = "浏览..." };
            _btnExportFolder.Pressed += OnSelectExportFolderPressed;
            folderRow.AddChild(_btnExportFolder);

            _openExportFolder = new Button { Text = "打开" };
            _openExportFolder.Pressed += OnOpenExportFolderPressed;
            folderRow.AddChild(_openExportFolder);

            vbox.AddChild(folderRow);

            // ── 版本号 ──
            var versionRow = new HBoxContainer();
            versionRow.AddChild(new Label { Text = "版本号:" });

            _txtVersion = new LineEdit
            {
                Text = _version,
                PlaceholderText = "1.0.0",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            _txtVersion.TextSubmitted += (text) => OnVersionConfirmed(text);
            _txtVersion.FocusExited += () => OnVersionConfirmed(_txtVersion.Text);
            versionRow.AddChild(_txtVersion);

            vbox.AddChild(versionRow);

            // ── 操作按钮 ──
            var buttonRow = new HBoxContainer();

            _btnRefresh = new Button { Text = "🔄 刷新" };
            _btnRefresh.Pressed += OnRefreshPressed;
            buttonRow.AddChild(_btnRefresh);

            // 弹性空间
            buttonRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

            _btnExportAll = new Button { Text = "📤 导出全部" };
            _btnExportAll.Pressed += OnExportAllPressed;
            buttonRow.AddChild(_btnExportAll);

            vbox.AddChild(buttonRow);
            vbox.AddChild(new HSeparator());

            // ── 资源树 (Tree) ──
            _bundleTree = new Tree
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                Columns = 7,
                AllowReselect = true,
                HideRoot = true,
            };

            _bundleTree.SetColumnTitle(0, "资源名称");
            _bundleTree.SetColumnTitle(1, "类型");
            _bundleTree.SetColumnTitle(2, "大小");
            _bundleTree.SetColumnTitle(3, "状态");
            _bundleTree.SetColumnTitle(4, "启用");
            _bundleTree.SetColumnTitle(5, "导出");
            _bundleTree.SetColumnTitle(6, "仅产物");

            _bundleTree.SetColumnExpand(0, true);
            _bundleTree.SetColumnExpand(1, false);
            _bundleTree.SetColumnExpand(2, false);
            _bundleTree.SetColumnExpand(3, false);
            _bundleTree.SetColumnExpand(4, false);
            _bundleTree.SetColumnExpand(5, false);
            _bundleTree.SetColumnExpand(6, false);

            _bundleTree.SetColumnCustomMinimumWidth(0, 160);
            _bundleTree.SetColumnCustomMinimumWidth(1, 60);
            _bundleTree.SetColumnCustomMinimumWidth(2, 80);
            _bundleTree.SetColumnCustomMinimumWidth(3, 80);
            _bundleTree.SetColumnCustomMinimumWidth(4, 40);
            _bundleTree.SetColumnCustomMinimumWidth(5, 40);
            _bundleTree.SetColumnCustomMinimumWidth(6, 40);

            // 列标题点击排序
            _bundleTree.ColumnTitlesVisible = true;

            _bundleTree.ItemEdited += OnTreeItemEdited;
            _bundleTree.ButtonClicked += OnTreeButtonClicked;

            _treeRoot = _bundleTree.CreateItem();
            _treeRoot.SetText(0, "正在扫描...");

            vbox.AddChild(_bundleTree);

            // ── 底部状态栏 ──
            var statusBar = new HBoxContainer();
            _lblSummary = new Label
            {
                Text = "准备就绪",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            statusBar.AddChild(_lblSummary);
            vbox.AddChild(statusBar);
        }

        // ═══════════════════════════════════════════════════════════
        //  扫描 & 刷新
        // ═══════════════════════════════════════════════════════════

        private void RefreshBundleList()
        {
            _bundles.Clear();
            _bundleTree.Clear();
            _treeRoot = _bundleTree.CreateItem();

            ScanDirectoryForBundles("res://");

            if (_bundles.Count == 0)
            {
                _treeRoot.SetText(0, "未发现 AssetBundle 资源");
                _treeRoot.SetText(1, "提示");
                _treeRoot.SetTooltipText(0, "在文件夹上右键 → 新建 → Resource → AssetBundle 来创建");
                _lblSummary.Text = "0 个资源包";
                return;
            }

            _treeRoot.SetText(0, $"📦 共 {_bundles.Count} 个资源包");
            _treeRoot.SetText(1, "");
            _treeRoot.SetText(2, "");
            _treeRoot.SetText(3, "");

            int totalBundles = 0;
            int totalResources = 0;
            long totalBytes = 0;
            int totalReady = 0;
            int totalMissing = 0;

            foreach (var kvp in _bundles.OrderBy(k => k.Key))
            {
                var path = kvp.Key;
                var res = kvp.Value;

                var info = BuildBundleInfo(path, res);
                if (info.Enabled) totalBundles++;

                totalResources += info.Resources.Count;
                totalBytes += info.TotalSize;
                totalReady += info.Resources.Count(r => r.ImportStatus == ImportStatus.Ready);
                totalMissing += info.Resources.Count(r => r.ImportStatus == ImportStatus.Missing);

                var bundleItem = _bundleTree.CreateItem(_treeRoot);
                bundleItem.SetMetadata(0, path);

                // 包名行 — 使用 Selectable 方便整行点击展开
                bundleItem.SetText(0, info.Name);
                bundleItem.SetText(1, "📦 包");
                bundleItem.SetText(2, FormatFileSize(info.TotalSize));
                bundleItem.SetText(3, $"{info.Resources.Count} 个文件");
                bundleItem.SetTooltipText(0, info.FolderPath);

                // 行颜色：启用=正常色，禁用=灰色
                if (!info.Enabled)
                {
                    var gray = new Color(0.6f, 0.6f, 0.6f);
                    for (int c = 0; c < 4; c++)
                        bundleItem.SetCustomColor(c, gray);
                }

                // Checkbox: 启用
                bundleItem.SetEditable(4, true);
                bundleItem.SetCellMode(4, TreeItem.TreeCellMode.Check);
                bundleItem.SetChecked(4, info.Enabled);

                // Checkbox: 导出
                bundleItem.SetEditable(5, true);
                bundleItem.SetCellMode(5, TreeItem.TreeCellMode.Check);
                bundleItem.SetChecked(5, info.ExportEnabled);

                // Checkbox: 仅产物
                bundleItem.SetEditable(6, true);
                bundleItem.SetCellMode(6, TreeItem.TreeCellMode.Check);
                bundleItem.SetChecked(6, info.ExportOnlyImported);
                bundleItem.SetTooltipText(6, "勾选=仅导出 .ctex/.fontdata/.sample，不包含源文件，体积更小");

                // ── 展开后显示包内文件 ──
                foreach (var ri in info.Resources)
                {
                    var childItem = _bundleTree.CreateItem(bundleItem);

                    childItem.SetText(0, ri.Name);
                    childItem.SetText(1, ri.TypeLabel);
                    childItem.SetText(2, FormatFileSize(ri.FileSize));

                    // 状态列 — 带颜色
                    switch (ri.ImportStatus)
                    {
                        case ImportStatus.Ready:
                            childItem.SetText(3, "✅ 已导入");
                            childItem.SetCustomColor(3, new Color(0.3f, 0.8f, 0.3f));
                            break;
                        case ImportStatus.Missing:
                            childItem.SetText(3, "⚠️ 缓存缺失");
                            childItem.SetCustomColor(3, new Color(0.9f, 0.5f, 0.1f));
                            childItem.SetTooltipText(3, "在编辑器中打开项目以重新导入此资源");
                            break;
                        case ImportStatus.NotApplicable:
                            childItem.SetText(3, "—");
                            childItem.SetCustomColor(3, new Color(0.6f, 0.6f, 0.6f));
                            break;
                    }

                    childItem.SetTooltipText(0, ri.FullPath);
                }
            }

            // ── 更新底部状态栏 ──
            string statusText = $"📦 {totalBundles}/{_bundles.Count} 个包启用  ·  " +
                                $"📄 {totalResources} 个资源  ·  " +
                                $"💾 {FormatFileSize(totalBytes)}";
            if (totalMissing > 0)
                statusText += $"  ·  ⚠️ {totalMissing} 个缓存缺失";

            _lblSummary.Text = statusText;
        }

        /// <summary>递归搜索目录中所有 AssetBundle</summary>
        private void ScanDirectoryForBundles(string dirPath)
        {
            var dir = DirAccess.Open(dirPath);
            if (dir == null) return;

            dir.ListDirBegin();
            var fileName = dir.GetNext();

            while (!string.IsNullOrEmpty(fileName))
            {
                if (fileName.StartsWith('.'))
                {
                    fileName = dir.GetNext();
                    continue;
                }

                var fullPath = dirPath.PathJoin(fileName);

                if (dir.CurrentIsDir())
                {
                    ScanDirectoryForBundles(fullPath);
                }
                else
                {
                    TryAddBundle(fullPath);
                }

                fileName = dir.GetNext();
            }

            dir.ListDirEnd();
        }

        /// <summary>尝试将文件作为 AssetBundle 加载</summary>
        private void TryAddBundle(string path)
        {
            var ext = Path.GetExtension(path)?.ToLower();
            if (ext != ".tres" && ext != ".res") return;

            var resource = ResourceLoader.Load(path);
            if (resource == null) return;

            var script = resource.GetScript().As<Script>();
            if (script == null) return;

            var globalName = script.GetGlobalName();
            if (string.IsNullOrEmpty(globalName) || !globalName.ToString().Contains("AssetBundle")) return;

            if (!_bundles.ContainsKey(path))
            {
                _bundles[path] = resource;
            }
        }

        /// <summary>从 Resource 读取 Bundle 信息，并扫描文件夹内的具体文件</summary>
        private BundleInfo BuildBundleInfo(string path, Godot.Resource res)
        {
            string folderPath = path.GetBaseDir();

            var info = new BundleInfo
            {
                Path = path,
                FolderPath = folderPath,
                Name = Path.GetFileNameWithoutExtension(path),
                Enabled = res.Get("enabled").AsBool(),
                ExportEnabled = res.Get("export_enabled").AsBool(),
                PackExternalDeps = res.Get("pack_external_dependencies").AsBool(),
                ExportOnlyImported = res.Get("export_only_imported").AsBool(),
            };

            ScanFolderResources(info);
            return info;
        }

        /// <summary>扫描文件夹，获取每个资源的详细信息</summary>
        private void ScanFolderResources(BundleInfo info)
        {
            info.Resources.Clear();
            info.TotalSize = 0;

            var dir = DirAccess.Open(info.FolderPath);
            if (dir == null) return;

            dir.ListDirBegin();
            var fileName = dir.GetNext();

            while (!string.IsNullOrEmpty(fileName))
            {
                if (fileName.StartsWith('.'))
                {
                    fileName = dir.GetNext();
                    continue;
                }

                var fullPath = info.FolderPath.PathJoin(fileName);

                if (dir.CurrentIsDir())
                {
                    // 递归子目录
                    ScanFolderResourcesRecursive(info, fullPath);
                }
                else
                {
                    AddResourceItem(info, fullPath, fileName);
                }

                fileName = dir.GetNext();
            }

            dir.ListDirEnd();
        }

        private void ScanFolderResourcesRecursive(BundleInfo info, string dirPath)
        {
            var dir = DirAccess.Open(dirPath);
            if (dir == null) return;

            dir.ListDirBegin();
            var fileName = dir.GetNext();

            while (!string.IsNullOrEmpty(fileName))
            {
                if (fileName.StartsWith('.'))
                {
                    fileName = dir.GetNext();
                    continue;
                }

                var fullPath = dirPath.PathJoin(fileName);

                if (dir.CurrentIsDir())
                {
                    ScanFolderResourcesRecursive(info, fullPath);
                }
                else
                {
                    AddResourceItem(info, fullPath, fileName);
                }

                fileName = dir.GetNext();
            }

            dir.ListDirEnd();
        }

        /// <summary>添加单个资源条目到 BundleInfo</summary>
        private void AddResourceItem(BundleInfo info, string fullPath, string fileName)
        {
            // 跳过 AssetBundle 元数据文件自身
            var ext = Path.GetExtension(fileName)?.ToLower();
            if (ext is ".tres" or ".res")
            {
                var resCheck = ResourceLoader.Load(fullPath);
                if (resCheck != null)
                {
                    var script = resCheck.GetScript().As<Script>();
                    if (script != null && script.GetGlobalName().ToString().Contains("AssetBundle"))
                        return;
                }
            }

            var typeLabel = GetTypeLabel(ext, fileName);
            long fileSize = GetFileSize(fullPath);

            var ri = new ResourceItem
            {
                Name = fileName,
                FullPath = fullPath,
                TypeLabel = typeLabel,
                FileSize = fileSize,
                HasImport = HasImportFile(fullPath),
                ImportStatus = CheckImportStatus(fullPath),
            };

            info.Resources.Add(ri);
            info.TotalSize += fileSize;
        }

        // ═══════════════════════════════════════════════════════════
        //  文件信息辅助
        // ═══════════════════════════════════════════════════════════

        private static string GetTypeLabel(string ext, string fileName)
        {
            return ext switch
            {
                ".png" or ".jpg" or ".jpeg" or ".svg" or ".webp" => "🖼 图片",
                ".wav" or ".ogg" or ".mp3" => "🎵 音频",
                ".ttf" or ".otf" or ".font" => "📄 字体",
                ".tscn" or ".scn" => "🎬 场景",
                ".glb" or ".gltf" => "🧊 模型",
                ".gdshader" or ".shader" => "✨ 着色器",
                ".tres" or ".res" => "📋 资源",
                ".gd" => "🐍 脚本",
                ".cs" => "🔧 C#",
                _ => "📄 文件",
            };
        }

        private static long GetFileSize(string resPath)
        {
            var globalPath = ProjectSettings.GlobalizePath(resPath);
            if (File.Exists(globalPath))
                return new FileInfo(globalPath).Length;
            return 0;
        }

        private static bool HasImportFile(string resPath)
        {
            var importPath = resPath + ".import";
            var globalPath = ProjectSettings.GlobalizePath(importPath);
            return File.Exists(globalPath);
        }

        /// <summary>检查导入状态：读取 .import 里的 dest_files，看缓存文件是否存在</summary>
        private static ImportStatus CheckImportStatus(string resPath)
        {
            var importPath = resPath + ".import";
            var globalImportPath = ProjectSettings.GlobalizePath(importPath);
            if (!File.Exists(globalImportPath))
                return ImportStatus.NotApplicable;

            try
            {
                var lines = File.ReadAllLines(globalImportPath);
                string destFilesLine = null;
                string pathLine = null;

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("dest_files="))
                        destFilesLine = trimmed;
                    else if (trimmed.StartsWith("path=") && pathLine == null)
                        pathLine = trimmed;
                }

                string[] destPaths = null;

                if (destFilesLine != null)
                {
                    var json = destFilesLine.Substring("dest_files=".Length);
                    destPaths = System.Text.Json.JsonSerializer.Deserialize<string[]>(json);
                }

                if ((destPaths == null || destPaths.Length == 0) && pathLine != null)
                {
                    var firstQuote = pathLine.IndexOf('"');
                    var lastQuote = pathLine.LastIndexOf('"');
                    if (firstQuote > 0 && lastQuote > firstQuote)
                    {
                        var singlePath = pathLine.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                        destPaths = new[] { singlePath };
                    }
                }

                if (destPaths == null || destPaths.Length == 0)
                    return ImportStatus.NotApplicable;

                // 只要有一个缓存文件存在就算 Ready
                foreach (var destPath in destPaths)
                {
                    var globalDest = ProjectSettings.GlobalizePath(destPath);
                    if (File.Exists(globalDest))
                        return ImportStatus.Ready;
                }

                return ImportStatus.Missing;
            }
            catch
            {
                return ImportStatus.NotApplicable;
            }
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        // ═══════════════════════════════════════════════════════════
        //  事件处理
        // ═══════════════════════════════════════════════════════════

        private void OnTreeItemEdited()
        {
            var editedItem = _bundleTree.GetEdited();
            if (editedItem == null) return;

            var path = editedItem.GetMetadata(0).AsString();
            if (string.IsNullOrEmpty(path) || !_bundles.TryGetValue(path, out var res)) return;

            var column = _bundleTree.GetEditedColumn();

            switch (column)
            {
                case 4: // 启用
                    res.Set("enabled", editedItem.IsChecked(4));
                    break;
                case 5: // 导出
                    res.Set("export_enabled", editedItem.IsChecked(5));
                    break;
                case 6: // 仅产物
                    res.Set("export_only_imported", editedItem.IsChecked(6));
                    break;
                default:
                    return; // 其他列不可编辑
            }

            var saveErr = ResourceSaver.Save(res, path);
            if (saveErr != Error.Ok)
            {
                GD.PrintErr($"保存失败: {path} (Error: {saveErr})");
            }

            // 延迟刷新（不能在鼠标事件中操作 Tree）
            Callable.From(RefreshBundleList).CallDeferred();
        }

        private void OnTreeButtonClicked(TreeItem item, long column, long id, long mouseButton)
        {
        }

        private void OnVersionConfirmed(string text)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+\.\d+\.\d+$"))
            {
                _version = text;
                SaveVersionPreference();
            }
            else
            {
                // 非法版本号 → 回退到上一个合法值
                _txtVersion.Text = _version;
            }
        }

        private void OnRefreshPressed()
        {
            GD.Print("正在扫描 AssetBundle...");
            RefreshBundleList();
        }

        private void OnSelectExportFolderPressed()
        {
            var dialog = new EditorFileDialog
            {
                Title = "选择 AB 包导出目录",
                FileMode = EditorFileDialog.FileModeEnum.OpenDir,
                Access = EditorFileDialog.AccessEnum.Filesystem,
            };

            dialog.DirSelected += (dir) =>
            {
                _exportFolder = dir;
                _txtExportFolder.Text = dir;
                SaveExportFolderPreference();
                GD.Print($"导出目录已设为: {dir}");
            };

            EditorInterface.Singleton.GetBaseControl().AddChild(dialog);
            dialog.PopupCenteredRatio(0.6f);
        }
        private void OnOpenExportFolderPressed()
        {
            if (string.IsNullOrEmpty(_exportFolder))
            {
                var dialog = new ConfirmationDialog();
                dialog.Title = "警告";
                dialog.DialogText = "导出文件夹为空";
                dialog.Confirmed += () => dialog.QueueFree();
                dialog.Canceled += () => dialog.QueueFree();
                return;
            }
            OS.ShellOpen(_exportFolder);
        }

        // ═══════════════════════════════════════════════════════════
        //  导出逻辑
        // ═══════════════════════════════════════════════════════════

        private void OnExportAllPressed()
        {
            var toExport = new List<BundleInfo>();

            foreach (var kvp in _bundles)
            {
                var info = BuildBundleInfo(kvp.Key, kvp.Value);
                if (info.Enabled && info.ExportEnabled)
                {
                    toExport.Add(info);
                }
            }

            if (toExport.Count == 0)
            {
                GD.Print("没有需要导出的 AB 包（请确保「启用」和「导出」都已勾选）。");
                return;
            }

            var exportDir = _exportFolder;
            if (string.IsNullOrEmpty(exportDir))
            {
                exportDir = ProjectSettings.GlobalizePath("res://").PathJoin("exported_bundles");
            }

            var absDir = Path.Combine(ProjectSettings.GlobalizePath(exportDir), DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            if (!absDir.StartsWith("res://") && !Directory.Exists(absDir))
            {
                Directory.CreateDirectory(absDir);
            }

            int success = 0;
            try
            {
                foreach (var bundle in toExport)
                {
                    ExportSingleBundle(bundle, absDir);
                    success++;
                }
                CreatePackVersionFile(absDir, success);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ExportInspector] 导出异常: {ex}");
            }
        }

        private bool ExportSingleBundle(BundleInfo bundle, string exportDir)
        {
            var packageName = bundle.Name + ".pck";
            var packagePath = Path.Combine(exportDir, packageName);

            try
            {
                var packer = new PckPacker();
                var err = packer.PckStart(packagePath);
                if (err != Error.Ok)
                {
                    GD.PrintErr($"[ExportInspector] 无法创建 .pck: {packagePath} (Error: {err})");
                    return false;
                }

                PackDirectoryContents(packer, bundle.FolderPath, bundle);

                err = packer.Flush();
                if (err != Error.Ok)
                {
                    GD.PrintErr($"[ExportInspector] 写入 .pck 失败: {packagePath} (Error: {err})");
                    return false;
                }

                GD.Print($"[ExportInspector] ✅ 导出成功: {packagePath} ({FormatFileSize(new FileInfo(packagePath).Length)})");
                return true;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ExportInspector] 导出异常 ({bundle.Name}): {ex.Message}");
                return false;
            }
        }

        private static void PackDirectoryContents(PckPacker packer, string dirPath, BundleInfo bundle)
        {
            var dir = DirAccess.Open(dirPath);
            if (dir == null) return;

            dir.ListDirBegin();
            var fileName = dir.GetNext();

            while (!string.IsNullOrEmpty(fileName))
            {
                if (fileName.StartsWith('.') || fileName.EndsWith(".gd.uid"))
                {
                    fileName = dir.GetNext();
                    continue;
                }

                var fullPath = dirPath.PathJoin(fileName);

                if (dir.CurrentIsDir())
                {
                    PackDirectoryContents(packer, fullPath, bundle);
                }
                else
                {
                    // 跳过 AssetBundle 元数据文件
                    var ext = Path.GetExtension(fileName)?.ToLower();
                    if (ext is ".tres" or ".res")
                    {
                        var resCheck = ResourceLoader.Load(fullPath);
                        if (resCheck != null)
                        {
                            var script = resCheck.GetScript().As<Script>();
                            if (script != null && script.GetGlobalName().ToString().Contains("AssetBundle"))
                            {
                                fileName = dir.GetNext();
                                continue;
                            }
                        }
                    }

                    PackFileWithImport(packer, fullPath, bundle.ExportOnlyImported);
                }

                fileName = dir.GetNext();
            }

            dir.ListDirEnd();
        }

        private static void PackFileWithImport(PckPacker packer, string filePath, bool exportOnlyImported = false)
        {
            var globalPath = ProjectSettings.GlobalizePath(filePath);
            if (!File.Exists(globalPath)) return;

            var importPath = filePath + ".import";
            var globalImportPath = ProjectSettings.GlobalizePath(importPath);
            bool hasImport = File.Exists(globalImportPath);

            if (hasImport)
            {
                if (!exportOnlyImported)
                {
                    var err = packer.AddFile(filePath, globalPath);
                    if (err != Error.Ok)
                        GD.PushWarning($"[ExportInspector] 无法添加文件到包: {filePath}");
                }

                var errImport = packer.AddFile(importPath, globalImportPath);
                if (errImport != Error.Ok)
                    GD.PushWarning($"[ExportInspector] 无法添加 .import 文件到包: {importPath}");

                PackImportedResource(packer, globalImportPath);
            }
            else
            {
                var err = packer.AddFile(filePath, globalPath);
                if (err != Error.Ok)
                    GD.PushWarning($"[ExportInspector] 无法添加文件到包: {filePath}");
            }
        }

        private static void PackImportedResource(PckPacker packer, string importFilePath)
        {
            try
            {
                var lines = File.ReadAllLines(importFilePath);
                string destFilesLine = null;
                string pathLine = null;

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("dest_files="))
                        destFilesLine = trimmed;
                    else if (trimmed.StartsWith("path=") && pathLine == null)
                        pathLine = trimmed;
                }

                string[] destPaths = null;
                if (destFilesLine != null)
                {
                    var json = destFilesLine.Substring("dest_files=".Length);
                    destPaths = System.Text.Json.JsonSerializer.Deserialize<string[]>(json);
                }

                if ((destPaths == null || destPaths.Length == 0) && pathLine != null)
                {
                    var firstQuote = pathLine.IndexOf('"');
                    var lastQuote = pathLine.LastIndexOf('"');
                    if (firstQuote > 0 && lastQuote > firstQuote)
                    {
                        var singlePath = pathLine.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                        destPaths = new[] { singlePath };
                    }
                }

                if (destPaths == null || destPaths.Length == 0) return;

                foreach (var resPath in destPaths)
                {
                    var globalDestPath = ProjectSettings.GlobalizePath(resPath);
                    if (File.Exists(globalDestPath))
                    {
                        var addErr = packer.AddFile(resPath, globalDestPath);
                        if (addErr != Error.Ok)
                            GD.PushWarning($"[ExportInspector] 无法添加导入产物到包: {resPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PushWarning($"[ExportInspector] 解析 .import 文件失败: {importFilePath}, {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  偏好设置
        // ═══════════════════════════════════════════════════════════

        private void LoadExportFolderPreference()
        {
            if (EditorInterface.Singleton.GetEditorSettings()
                    .HasSetting(EditorSettingExportFolder))
            {
                _exportFolder = (string)EditorInterface.Singleton
                    .GetEditorSettings()
                    .GetSetting(EditorSettingExportFolder);
            }
        }

        private void SaveExportFolderPreference()
        {
            EditorInterface.Singleton.GetEditorSettings()
                .SetSetting(EditorSettingExportFolder, _exportFolder);
        }

        private void LoadVersionPreference()
        {
            var settings = EditorInterface.Singleton.GetEditorSettings();
            if (settings.HasSetting(EditorSettingVersion))
            {
                _version = (string)settings.GetSetting(EditorSettingVersion);
            }
        }

        private void SaveVersionPreference()
        {
            EditorInterface.Singleton.GetEditorSettings()
                .SetSetting(EditorSettingVersion, _version);
        }

        // ═══════════════════════════════════════════════════════════
        //  版本文件生成
        // ═══════════════════════════════════════════════════════════

        private void CreatePackVersionFile(string exportDir, int num)
        {
            if (num == 0)
            {
                GD.Print("[ExportInspector] 没有成功导出的包，跳过版本文件生成。");
                return;
            }
            var res = ResourceLoader.Load(string.Format(GameFolderConstant.Resources, "UpdateSettingRes"));
            var blist = _bundles.ToList();
            var packs = new Pack[num];
            int idx = 0;

            for (int i = 0; i < blist.Count; i++)
            {
                var bundle = blist[i];
                var info = BuildBundleInfo(bundle.Key, bundle.Value);
                if (!info.Enabled || !info.ExportEnabled) continue;

                var pckPath = Path.Combine(exportDir, info.Name + ".pck");
                var fileInfo = new FileInfo(pckPath);

                packs[idx] = new Pack
                {
                    Name = info.Name,
                    Size = fileInfo.Exists ? fileInfo.Length : 0,
                    Hash = fileInfo.Exists ? NodeUtility.ComputeSHA256(pckPath) : string.Empty,
                    Url = res.Get(UpdateSettingRes.Parameters.RemoteUrl) + "/" + info.Name + ".pck",
                    Type = PackType.Resource,
                };
                idx++;
            }

            var versionStr = string.IsNullOrWhiteSpace(_version) ? "1.0.0" : _version.Trim();
            var versionList = new PackVersionList(versionStr, packs);
            string json = JsonConvert.SerializeObject(versionList, Formatting.Indented);
            string filePath = Path.Combine(exportDir, ResourceManager.GameFrameworkVersionData);
            File.WriteAllText(filePath, json);
            GD.Print($"[ExportInspector] 版本文件已生成: {filePath}");
        }

    }
}
#endif
