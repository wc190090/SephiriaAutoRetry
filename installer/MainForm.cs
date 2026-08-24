using System.Diagnostics;
using System.Reflection;

namespace SephiriaAutoRetry.Installer;

internal sealed class MainForm : Form
{
    private readonly TextBox gamePathBox = new();
    private readonly Label statusLabel = new();
    private readonly RichTextBox logBox = new();
    private readonly Button installButton = new();
    private readonly Button uninstallButton = new();
    private bool busy;

    internal MainForm()
    {
        Text = $"Sephiria Auto Retry {InstallerEngine.ModVersion} 离线安装器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 560);
        Size = new Size(820, 610);
        Font = new Font("Microsoft YaHei UI", 9F);

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            RowCount = 7,
            ColumnCount = 1,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        Label title = new()
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = $"Sephiria Auto Retry {InstallerEngine.ModVersion}",
            Margin = new Padding(0, 0, 0, 7),
        };
        root.Controls.Add(title, 0, 0);

        Label description = new()
        {
            AutoSize = true,
            MaximumSize = new Size(750, 0),
            Text = "死亡后从本层入口检查点自动重试。联机时仅房主安装，但房主必须使用启动参数 -allow_rejoin。安装、更新或卸载前请完全退出游戏。",
            Margin = new Padding(0, 0, 0, 12),
        };
        root.Controls.Add(description, 0, 1);

        TableLayoutPanel pathPanel = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(0, 0, 0, 8),
        };
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        gamePathBox.Dock = DockStyle.Fill;
        gamePathBox.PlaceholderText = "请选择包含 Sephiria.exe 的游戏根目录";
        gamePathBox.TextChanged += (_, _) => RefreshStatus();
        Button browseButton = new() { Text = "浏览…", AutoSize = true };
        browseButton.Click += (_, _) => Browse();
        Button detectButton = new() { Text = "自动查找", AutoSize = true };
        detectButton.Click += (_, _) => DetectGame();
        pathPanel.Controls.Add(gamePathBox, 0, 0);
        pathPanel.Controls.Add(browseButton, 1, 0);
        pathPanel.Controls.Add(detectButton, 2, 0);
        root.Controls.Add(pathPanel, 0, 2);

        statusLabel.AutoSize = true;
        statusLabel.Margin = new Padding(0, 0, 0, 8);
        root.Controls.Add(statusLabel, 0, 3);

        FlowLayoutPanel actions = new()
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 10),
        };
        installButton.Text = "一键安装";
        installButton.AutoSize = true;
        installButton.Padding = new Padding(9, 5, 9, 5);
        installButton.Click += (_, _) => Install();
        uninstallButton.Text = "卸载 Mod";
        uninstallButton.AutoSize = true;
        uninstallButton.Padding = new Padding(9, 5, 9, 5);
        uninstallButton.Click += (_, _) => Uninstall();
        Button copyLaunchOption = new() { Text = "复制 -allow_rejoin", AutoSize = true, Padding = new Padding(9, 5, 9, 5) };
        copyLaunchOption.Click += (_, _) =>
        {
            Clipboard.SetText("-allow_rejoin");
            AppendLog("已复制房主启动参数：-allow_rejoin");
        };
        Button openFolder = new() { Text = "打开游戏目录", AutoSize = true, Padding = new Padding(9, 5, 9, 5) };
        openFolder.Click += (_, _) => OpenPath(gamePathBox.Text);
        Button openLog = new() { Text = "打开日志", AutoSize = true, Padding = new Padding(9, 5, 9, 5) };
        openLog.Click += (_, _) =>
        {
            string path = InstallerEngine.GetLogPath(gamePathBox.Text);
            if (File.Exists(path)) OpenPath(path);
            else MessageBox.Show(this, "尚未生成 BepInEx\\LogOutput.log。", "日志不存在", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        actions.Controls.Add(installButton);
        actions.Controls.Add(uninstallButton);
        actions.Controls.Add(copyLaunchOption);
        actions.Controls.Add(openFolder);
        actions.Controls.Add(openLog);
        root.Controls.Add(actions, 0, 4);

        logBox.Dock = DockStyle.Fill;
        logBox.ReadOnly = true;
        logBox.BackColor = Color.FromArgb(248, 248, 248);
        logBox.Font = new Font("Consolas", 9F);
        logBox.Text = "等待操作……\n";
        root.Controls.Add(logBox, 0, 5);

        FlowLayoutPanel footer = new() { AutoSize = true, Dock = DockStyle.Top };
        Label safety = new()
        {
            AutoSize = true,
            Text = "安装会备份存档和旧插件；卸载只删除本 Mod，保留 BepInEx、配置、存档和其他 Mod。",
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 7, 12, 0),
        };
        Button noticeButton = new() { Text = "第三方软件说明", AutoSize = true };
        noticeButton.Click += (_, _) => ShowThirdPartyNotice();
        footer.Controls.Add(safety);
        footer.Controls.Add(noticeButton);
        root.Controls.Add(footer, 0, 6);

        AcceptButton = installButton;
        Shown += (_, _) => DetectGame();
    }

    private void DetectGame()
    {
        try
        {
            IReadOnlyList<string> results = InstallerEngine.FindGameDirectories();
            if (results.Count > 0)
            {
                gamePathBox.Text = results[0];
                AppendLog($"自动找到游戏目录：{results[0]}");
            }
            else
            {
                AppendLog("未自动找到游戏，请手动选择包含 Sephiria.exe 的目录。");
            }
        }
        catch (Exception ex)
        {
            AppendLog("自动查找失败：" + ex.Message);
        }

        RefreshStatus();
    }

    private void Browse()
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "选择包含 Sephiria.exe 的游戏根目录",
            ShowNewFolderButton = false,
            UseDescriptionForTitle = true,
        };
        if (Directory.Exists(gamePathBox.Text))
        {
            dialog.InitialDirectory = gamePathBox.Text;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            gamePathBox.Text = dialog.SelectedPath;
        }
    }

    private async void Install()
    {
        SetBusy(true);
        AppendLog("开始安装/更新；窗口会保持响应，请等待备份与校验完成……");
        try
        {
            string gameRoot = gamePathBox.Text;
            InstallResult result = await Task.Run(() => InstallerEngine.Install(gameRoot, AppendLog));
            string bepinex = result.InstalledBepInEx ? $"已安装 BepInEx {InstallerEngine.BepInExVersion}。" : "保留了现有 BepInEx。";
            string backup = result.BackupPath == null ? "本次没有需要备份的文件。" : $"备份：\n{result.BackupPath}";
            MessageBox.Show(
                this,
                $"Sephiria Auto Retry {InstallerEngine.ModVersion} 安装成功。\n\n{bepinex}\n{backup}\n\n联机房主请在 Steam 启动选项中填写 -allow_rejoin，然后重新启动游戏。",
                "安装成功",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog("安装失败：" + ex);
            MessageBox.Show(this, ex.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            RefreshStatus();
        }
    }

    private async void Uninstall()
    {
        if (MessageBox.Show(
                this,
                "只卸载 Sephiria Auto Retry？\n\nBepInEx、其他 Mod、配置和存档不会被删除。",
                "确认卸载",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        SetBusy(true);
        AppendLog("开始备份并卸载本 Mod……");
        try
        {
            string gameRoot = gamePathBox.Text;
            string? backup = await Task.Run(() => InstallerEngine.Uninstall(gameRoot, AppendLog));
            MessageBox.Show(this, backup == null ? "未检测到本 Mod。" : $"卸载完成。\n\n备份：\n{backup}", "卸载完成");
        }
        catch (Exception ex)
        {
            AppendLog("卸载失败：" + ex);
            MessageBox.Show(this, ex.Message, "卸载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            RefreshStatus();
        }
    }

    private void RefreshStatus()
    {
        bool valid = InstallerEngine.IsValidGameRoot(gamePathBox.Text);
        installButton.Enabled = valid && !busy;
        uninstallButton.Enabled = valid && !busy;
        statusLabel.ForeColor = valid ? Color.DarkGreen : Color.Firebrick;
        if (!valid)
        {
            statusLabel.Text = "尚未选择有效游戏目录。";
            installButton.Text = "一键安装";
            return;
        }

        string? version = InstallerEngine.GetInstalledVersion(gamePathBox.Text);
        statusLabel.Text = version == null ? "目录有效 · 尚未安装本 Mod" : $"目录有效 · 已安装本 Mod {version}";
        installButton.Text = version == null ? "一键安装" : $"更新/重装 {InstallerEngine.ModVersion}";
    }

    private void SetBusy(bool value)
    {
        busy = value;
        UseWaitCursor = value;
        gamePathBox.Enabled = !value;
        RefreshStatus();
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<string>(AppendLog), message);
            return;
        }

        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        logBox.SelectionStart = logBox.TextLength;
        logBox.ScrollToCaret();
        Application.DoEvents();
    }

    private static void OpenPath(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    private void ShowThirdPartyNotice()
    {
        const string resource = "SephiriaAutoRetry.Installer.ThirdPartyNotices.txt";
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        if (stream == null)
        {
            MessageBox.Show(this, "安装器缺少第三方软件说明。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using StreamReader reader = new(stream);
        MessageBox.Show(this, reader.ReadToEnd(), "第三方软件说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
