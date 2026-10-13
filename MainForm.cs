using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Drawing.Imaging;

namespace Wallcraft;

public sealed class MainForm : Form
{
    private const int GalleryTileSize = 199;
    private readonly TextBox _installedOutput = new() { Location = new Point(60, 1), Size = new Size(400, 24), BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _output = new() { Location = new Point(107, 54), Size = new Size(650, 28) };
    private readonly TextBox _mediaOutput = new() { Location = new Point(107, 96), Size = new Size(650, 28) };
    private readonly TextBox _log = new() { Location = new Point(12, 414), Size = new Size(860, 280), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly DataGridView _files = new();
    private readonly Label _fileCount = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleRight };
    private readonly Label _emptyFiles = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        Text = "将 PKG / MPKG 文件拖到这里\r\n或点击上方“添加文件”", ForeColor = Color.FromArgb(132, 137, 146), BackColor = Color.White };
    private readonly PackageButton _extract = new() { Text = "解包到壁纸项目", Primary = true };
    private readonly PackageButton _bulkMedia = new() { Text = "批量导出订阅媒体" };
    private readonly Label _status = new() { Text = "就绪", Location = new Point(12, 703), Size = new Size(860, 28), TextAlign = ContentAlignment.MiddleLeft };
    private readonly List<string> _paths = [];
    private bool _busy;

    public MainForm()
    {
        Text = "Wallcraft";
        BackColor = Color.White;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Size = new Size(900, 820);
        StartPosition = FormStartPosition.CenterScreen;
        FormClosing += (_, _) => { SaveWindowPosition(); SavePathSettings(); };
        Font = new Font("Microsoft YaHei UI", 10);
        _output.Text = SteamPaths.FindMyProjects() ?? "";
        _mediaOutput.Text = GetDownloadsFolder();
        _installedOutput.Text = GetDownloadsFolder();
        RestorePathSettings();
        foreach (var input in new[] { _installedOutput, _output, _mediaOutput })
            input.Leave += (_, _) => SavePathSettings();
        Shown += (_, _) => ActiveControl = null;

        var installed = new Panel { Location = new Point(0, 38), Size = new Size(884, Math.Max(0, ClientSize.Height - 38)), BackColor = Color.White };
        var work = new Panel { Location = new Point(0, 38), Size = new Size(884, 742), BackColor = Color.White, Visible = false };
        var settings = new Panel { Location = new Point(0, 38), Size = new Size(884, 742), BackColor = Color.White, Visible = false };
        Controls.AddRange([installed, work, settings]);
        // Size 包含标题栏和边框，页面底边必须以实际客户区为准。
        void FitInstalledPage()
        {
            var height = Math.Max(0, ClientSize.Height - installed.Top);
            if (installed.Height != height) installed.Height = height;
        }
        Layout += (_, _) => FitInstalledPage();
        FitInstalledPage();
        var labels = new[] { "已安装", "PKG / MPKG", "设置" };
        var navBounds = new[] { new Rectangle(5, 3, 88, 32), new Rectangle(100, 3, 150, 32), new Rectangle(254, 3, 68, 32) };
        var views = new[] { installed, work, settings };
        var navButtons = new Label[views.Length];
        for (var i = 0; i < views.Length; i++)
        {
            var index = i;
            var button = new Label
            {
                Text = labels[i], Location = navBounds[i].Location, Size = navBounds[i].Size, BackColor = Color.White,
                Font = new Font(Font.FontFamily, 16, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand
            };
            button.Click += (_, _) => ShowPage(index);
            navButtons[i] = button;
            Controls.Add(button);
        }
        void ShowPage(int active)
        {
            for (var i = 0; i < views.Length; i++)
            {
                views[i].Visible = i == active;
                navButtons[i].ForeColor = i == active ? Color.FromArgb(42, 103, 225) : Color.FromArgb(132, 137, 146);
            }
            views[active].BringToFront();
        }
        BuildInstalledTab(installed);
        ShowPage(0);

        BuildPackageTab(work);

        _extract.Click += async (_, _) => await ImportPackagesAsync();
        _bulkMedia.Click += async (_, _) => await ExportSubscribedMediaAsync();
        foreach (Control dropTarget in new Control[] { this, work, _files, _emptyFiles })
        {
            dropTarget.AllowDrop = true;
            dropTarget.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
            dropTarget.DragDrop += (_, e) => AddFiles((e.Data?.GetData(DataFormats.FileDrop) as string[]) ?? []);
        }

        var info = new TextBox
        {
            Location = new Point(12, 12), Size = new Size(860, 718), Multiline = true, ReadOnly = true,
            Text = "Wallcraft 用于恢复 PKG / MPKG 壁纸项目，以及导出已订阅壁纸的图片或原始 MP4。\r\n\r\n" +
                   "已安装：单个壁纸导出媒体，默认保存到系统下载文件夹。\r\n" +
                   "PKG / MPKG：多选或拖入包文件，恢复到 Wallpaper Engine 的 myprojects；也可批量导出已订阅媒体。\r\n\r\n" +
                   "场景壁纸可导出纹理 PNG；视频壁纸直接复制原始 MP4。场景特效不会录制成 MP4。\r\n" +
                   "部分 Android MPKG 纹理可能无法在桌面版正确显示；提取后请在 Wallpaper Engine 中检查。\r\n" +
                   "repkg-ng 的 ETC2 解码在部分壁纸上可能产生彩色杂点，请目视检查导出的 PNG。"
        };
        settings.Controls.Add(info);
        // 首次显示前确定位置，避免在 Load 阶段从默认屏幕移动到保存的屏幕。
        RestoreWindowPosition();
    }

    private void BuildPackageTab(Panel page)
    {
        void FitPage() => page.SetBounds(0, page.Top, ClientSize.Width, Math.Max(0, ClientSize.Height - page.Top));
        Layout += (_, _) => FitPage();
        FitPage();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 20),
            ColumnCount = 1, RowCount = 6, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 56f, 52f, -1f, 80f, 80f, 52f })
            layout.RowStyles.Add(new RowStyle(height < 0 ? SizeType.Percent : SizeType.Absolute, height < 0 ? 100 : height));
        page.Controls.Add(layout);
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        heading.Controls.Add(new Label { Text = "壁纸包处理", AutoSize = true, Font = new Font(Font.FontFamily, 14, FontStyle.Bold), Margin = Padding.Empty });
        heading.Controls.Add(new Label { Text = "添加 PKG / MPKG 文件，恢复项目或导出订阅媒体", AutoSize = true,
            ForeColor = Color.FromArgb(112, 118, 128), Margin = Padding.Empty });
        layout.Controls.Add(heading, 0, 0);

        Button ActionButton(string text, EventHandler click)
        {
            var button = new PackageButton { Text = text, Size = new Size(96, 36),
                Margin = new Padding(0, 0, 8, 0) };
            button.Click += click;
            return button;
        }
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(0, 0, 0, 16) };
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        tools.Controls.Add(ActionButton("添加文件", (_, _) => { if (!_busy) PickFiles(); }));
        tools.Controls.Add(ActionButton("移除选中", (_, _) =>
        {
            if (_busy) return;
            foreach (var row in _files.SelectedRows.Cast<DataGridViewRow>().ToArray())
            {
                _paths.Remove((string)row.Tag!);
                _files.Rows.Remove(row);
            }
            UpdateFileSummary();
        }));
        tools.Controls.Add(ActionButton("清空", (_, _) =>
        {
            if (_busy) return;
            _paths.Clear();
            _files.Rows.Clear();
            UpdateFileSummary();
        }));
        toolbar.Controls.Add(tools, 0, 0);
        _fileCount.Dock = DockStyle.Fill;
        _fileCount.Margin = new Padding(0, 0, 8, 0);
        toolbar.Controls.Add(_fileCount, 1, 0);
        layout.Controls.Add(toolbar, 0, 1);

        _files.Dock = DockStyle.Fill;
        _files.ReadOnly = true;
        _files.AllowUserToAddRows = _files.AllowUserToDeleteRows = _files.AllowUserToResizeRows = false;
        _files.RowHeadersVisible = false;
        _files.MultiSelect = true;
        _files.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _files.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _files.BackgroundColor = Color.White;
        _files.BorderStyle = BorderStyle.None;
        _files.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _files.GridColor = Color.FromArgb(235, 238, 242);
        _files.EnableHeadersVisualStyles = false;
        _files.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _files.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 247, 250);
        _files.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(85, 92, 104);
        _files.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
        _files.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(245, 247, 250);
        _files.ColumnHeadersDefaultCellStyle.Padding = new Padding(12, 0, 12, 0);
        _files.ColumnHeadersHeight = 40;
        _files.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _files.RowTemplate.Height = 44;
        _files.DefaultCellStyle.ForeColor = Color.FromArgb(45, 53, 65);
        _files.DefaultCellStyle.Padding = new Padding(12, 0, 12, 0);
        _files.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 253);
        _files.DefaultCellStyle.SelectionBackColor = Color.FromArgb(229, 239, 255);
        _files.DefaultCellStyle.SelectionForeColor = Color.FromArgb(32, 78, 150);
        foreach (var (name, weight) in new[] { ("文件名", 65f), ("类型", 15f), ("状态", 20f) })
            _files.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, FillWeight = weight,
                SortMode = DataGridViewColumnSortMode.NotSortable });
        var fileArea = new PackageSurface { Dock = DockStyle.Fill, Padding = new Padding(6), Margin = new Padding(0, 0, 0, 18) };
        fileArea.Controls.Add(_files);
        fileArea.Controls.Add(_emptyFiles);
        layout.Controls.Add(fileArea, 0, 2);

        Control PathRow(string caption, TextBox input, EventHandler browse)
        {
            var group = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            group.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            group.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            group.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var label = new Label { Text = caption, AutoSize = true, Margin = Padding.Empty,
                ForeColor = Color.FromArgb(85, 92, 104) };
            group.Controls.Add(label, 0, 0);
            group.SetColumnSpan(label, 2);
            var field = new PackageSurface { Dock = DockStyle.Top, Height = 38, Margin = new Padding(0, 0, 10, 0) };
            input.BorderStyle = BorderStyle.None;
            input.AutoSize = false;
            input.BackColor = Color.White;
            input.ForeColor = Color.FromArgb(45, 53, 65);
            input.Dock = DockStyle.None;
            field.Controls.Add(input);
            void LayoutPathText()
            {
                // 无边框单行编辑框也需要字形上下留白，不能直接压到 PreferredHeight。
                var textHeight = Math.Max(input.PreferredHeight, input.Font.Height) + field.ScalePixels(6);
                input.SetBounds(field.ScalePixels(12), Math.Max(0, (field.ClientSize.Height - textHeight) / 2),
                    Math.Max(0, field.ClientSize.Width - field.ScalePixels(24)), textHeight);
            }
            field.Layout += (_, _) => LayoutPathText();
            input.FontChanged += (_, _) => LayoutPathText();
            input.DpiChangedAfterParent += (_, _) => LayoutPathText();
            input.GotFocus += (_, _) => field.Invalidate();
            input.LostFocus += (_, _) => field.Invalidate();
            group.Controls.Add(field, 0, 1);
            var button = ActionButton("浏览", browse);
            button.Dock = DockStyle.Top;
            button.Height = 38;
            button.Margin = Padding.Empty;
            group.Controls.Add(button, 1, 1);
            return group;
        }
        layout.Controls.Add(PathRow("项目保存位置", _output, (_, _) => PickOutput()), 0, 3);
        layout.Controls.Add(PathRow("媒体保存位置", _mediaOutput, (_, _) => PickMediaOutput()), 0, 4);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var button in new[] { _bulkMedia, _extract })
        {
            button.AutoSize = false;
            button.Size = new Size(156, 40);
            button.Anchor = AnchorStyles.Right;
            button.Margin = new Padding(8, 0, 0, 0);
        }
        _bulkMedia.Text = "导出订阅媒体";
        _extract.Text = "恢复壁纸项目";
        _extract.BackColor = Color.FromArgb(42, 103, 225);
        _extract.ForeColor = Color.White;
        _extract.FlatAppearance.BorderSize = 0;
        actions.Controls.Add(_bulkMedia, 1, 0);
        actions.Controls.Add(_extract, 2, 0);
        layout.Controls.Add(actions, 0, 5);
        UpdateFileSummary();
    }

    private void UpdateFileSummary()
    {
        _fileCount.Text = $"共 {_paths.Count} 个文件";
        _emptyFiles.Visible = _paths.Count == 0;
        if (_paths.Count == 0) _emptyFiles.BringToFront();
    }

    private void SetFileStatus(string path, string status, string? detail = null)
    {
        foreach (DataGridViewRow row in _files.Rows)
            if (string.Equals(row.Tag as string, path, StringComparison.OrdinalIgnoreCase))
            {
                row.Cells[2].Value = status;
                row.Cells[2].ToolTipText = detail ?? status;
            }
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedOutline(RectangleF bounds, float radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class PackageSurface : Panel
    {
        public PackageSurface()
        {
            BackColor = Color.White;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        public int ScalePixels(int pixels) => LogicalToDeviceUnits(pixels);
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var inset = ContainsFocus ? Math.Max(1f, DeviceDpi / 96f) : 0.5f;
            using var outline = RoundedOutline(new RectangleF(inset, inset, Width - inset * 2, Height - inset * 2), ScalePixels(7));
            using var pen = new Pen(ContainsFocus ? Color.FromArgb(88, 137, 230) : Color.FromArgb(222, 227, 234), inset * 2);
            e.Graphics.DrawPath(pen, outline);
        }
    }

    private sealed class PackageButton : Button
    {
        [System.ComponentModel.DefaultValue(false)]
        public bool Primary { get; set; }
        private bool _hover;
        private bool _pressed;
        public PackageButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.White;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = _pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _pressed = e.Button == MouseButtons.Left; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _pressed = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) { _pressed = true; Invalidate(); } }
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); _pressed = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 2 || Height < 2) return;
            e.Graphics.Clear(Parent?.BackColor ?? Color.White);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var fill = !Enabled ? Color.FromArgb(240, 242, 245) : Primary
                ? (_pressed ? Color.FromArgb(29, 77, 176) : _hover ? Color.FromArgb(35, 91, 205) : Color.FromArgb(42, 103, 225))
                : (_pressed ? Color.FromArgb(229, 235, 245) : _hover ? Color.FromArgb(240, 245, 253) : Color.FromArgb(248, 250, 252));
            using var outline = RoundedOutline(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), LogicalToDeviceUnits(7));
            using var brush = new SolidBrush(fill);
            e.Graphics.FillPath(brush, outline);
            using var pen = new Pen(Focused && ShowFocusCues ? Color.FromArgb(88, 137, 230) : Primary ? fill : Color.FromArgb(222, 227, 234));
            e.Graphics.DrawPath(pen, outline);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
                !Enabled ? Color.FromArgb(156, 163, 175) : Primary ? Color.White : Color.FromArgb(55, 65, 81),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    private void BuildInstalledTab(Control tab)
    {
        var gallery = new WallpaperGallery
        {
            Location = new Point(2, 28), Size = new Size(614, Math.Max(0, tab.ClientSize.Height - 28)), AutoScroll = true,
            BackColor = Color.White
        };
        var output = _installedOutput;
        var preview = new PictureBox { Location = new Point(0, 28), Size = new Size(262, 262), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
        var title = new Label { Location = new Point(5, 300), Size = new Size(256, 65), TextAlign = ContentAlignment.MiddleCenter, Font = new Font(Font, FontStyle.Bold) };
        var location = new Label { Location = new Point(5, 365), Size = new Size(256, 52), TextAlign = ContentAlignment.TopCenter, AutoEllipsis = true };
        var extract = new Button { Text = "提取", Location = new Point(13, 420), Size = new Size(230, 34), Enabled = false };
        var open = new Button { Text = "打开所在目录", Location = new Point(13, 454), Size = new Size(230, 34), Enabled = false };
        var detail = new Panel { Location = new Point(619, 0), Size = new Size(265, 742), BackColor = Color.White };
        detail.Controls.AddRange([preview, title, location, extract, open]);

        output.TabStop = false;
        var browseButton = new CenteredToolbarButton("浏览") { Bounds = new Rectangle(460, 1, 68, 24) };
        browseButton.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = output.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK) output.Text = dialog.SelectedPath;
        };
        var openButton = new CenteredToolbarButton("打开") { Bounds = new Rectangle(528, 1, 68, 24) };
        openButton.Click += (_, _) =>
        {
            if (!Directory.Exists(output.Text)) Directory.CreateDirectory(output.Text);
            Process.Start(new ProcessStartInfo("explorer.exe", output.Text) { UseShellExecute = true });
        };

        tab.Controls.AddRange([
            new Label { Text = "保存到", Location = new Point(8, 1), Size = new Size(52, 24), TextAlign = ContentAlignment.MiddleLeft },
            output, browseButton, openButton, gallery, detail
        ]);
        // 视口占满工具栏下方的可用高度，滚动内容高度仍由壁纸数量决定。
        void FitGalleryViewport()
        {
            var height = Math.Max(0, tab.ClientSize.Height - gallery.Top);
            if (gallery.Height != height) gallery.Height = height;
        }
        tab.Layout += (_, _) => FitGalleryViewport();
        FitGalleryViewport();

        WallpaperEntry? selected = null;
        void Select(WallpaperEntry entry)
        {
            selected = entry;
            preview.Image = entry.Preview;
            title.Text = entry.Title;
            location.Text = entry.Directory;
            open.Enabled = true;
            extract.Enabled = CanExportMedia(entry);
        }
        open.Click += (_, _) =>
        {
            if (selected is not null)
                Process.Start(new ProcessStartInfo("explorer.exe", selected.Directory) { UseShellExecute = true });
        };
        extract.Click += async (_, _) =>
        {
            if (selected is null || _busy) return;
            _busy = true;
            extract.Enabled = false;
            try
            {
                var destination = await ExportMediaAsync(selected, output.Text);
                MessageBox.Show(this, $"已导出到：\n{destination}");
            }
            catch (Exception error) { MessageBox.Show(this, error.Message); }
            finally { _busy = false; extract.Enabled = selected is not null && CanExportMedia(selected); }
        };

        var entries = new List<WallpaperEntry>();
        void Load()
        {
            selected = null;
            preview.Image = null;
            title.Text = location.Text = "";
            extract.Enabled = open.Enabled = false;
            foreach (var old in entries) old.Preview?.Dispose();
            entries = GetInstalledWallpapers().ToList();
            gallery.SetEntries(entries);
            if (entries.Count > 0) Select(entries[0]);
        }

        gallery.EntryClicked += Select;
        Load();
    }

    private sealed record WindowPosition(string Screen, int X, int Y);

    private sealed record PathSettings(string? InstalledOutput, string? ProjectOutput, string? MediaOutput);

    private static string PathSettingsPath => Path.Combine(Program.PortableDirectory, "settings.json");

    private void RestorePathSettings()
    {
        try
        {
            if (!File.Exists(PathSettingsPath)) return;
            var settings = JsonSerializer.Deserialize<PathSettings>(File.ReadAllText(PathSettingsPath));
            if (settings is null) return;
            _installedOutput.Text = settings.InstalledOutput ?? _installedOutput.Text;
            _output.Text = settings.ProjectOutput ?? _output.Text;
            _mediaOutput.Text = settings.MediaOutput ?? _mediaOutput.Text;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Log($"读取路径设置失败：{error.Message}\r\n");
        }
    }

    private void SavePathSettings()
    {
        try
        {
            var settings = new PathSettings(_installedOutput.Text, _output.Text, _mediaOutput.Text);
            Directory.CreateDirectory(Path.GetDirectoryName(PathSettingsPath)!);
            var temporary = PathSettingsPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, PathSettingsPath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log($"保存路径设置失败：{error.Message}\r\n");
        }
    }

    private static string WindowPositionPath => Path.Combine(Program.PortableDirectory, "window.json");

    private void RestoreWindowPosition()
    {
        try
        {
            if (!File.Exists(WindowPositionPath)) return;
            var position = JsonSerializer.Deserialize<WindowPosition>(File.ReadAllText(WindowPositionPath));
            if (position is null) return;
            var screen = Screen.AllScreens.FirstOrDefault(item =>
                item.DeviceName.Equals(position.Screen, StringComparison.OrdinalIgnoreCase));
            if (screen is null) return;
            var area = screen.WorkingArea;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(
                Math.Clamp(area.Left + position.X, area.Left, Math.Max(area.Left, area.Right - Width)),
                Math.Clamp(area.Top + position.Y, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
    }

    private void SaveWindowPosition()
    {
        try
        {
            var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            var screen = Screen.FromRectangle(bounds);
            var area = screen.WorkingArea;
            var position = new WindowPosition(screen.DeviceName, bounds.Left - area.Left, bounds.Top - area.Top);
            Directory.CreateDirectory(Path.GetDirectoryName(WindowPositionPath)!);
            File.WriteAllText(WindowPositionPath, JsonSerializer.Serialize(position));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static IEnumerable<WallpaperEntry> GetInstalledWallpapers(bool subscribedOnly = false, bool loadPreviews = true)
    {
        foreach (var root in SteamPaths.WallpaperRoots(subscribedOnly).Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                WallpaperEntry? entry = null;
                try
                {
                    var project = Path.Combine(directory, "project.json");
                    if (!File.Exists(project)) continue;
                    using var json = JsonDocument.Parse(File.ReadAllText(project));
                    var name = json.RootElement.TryGetProperty("title", out var value) ? value.GetString() : Path.GetFileName(directory);
                    var type = json.RootElement.TryGetProperty("type", out value) ? value.GetString() : null;
                    var contentFile = json.RootElement.TryGetProperty("file", out value) ? value.GetString() : null;
                    Image? image = null;
                    if (loadPreviews && json.RootElement.TryGetProperty("preview", out value))
                    {
                        var imagePath = ResolveProjectFile(directory, value.GetString());
                        if (imagePath is not null && File.Exists(imagePath))
                        {
                            using var source = Image.FromFile(imagePath);
                            image = new Bitmap(source);
                        }
                    }
                    entry = new WallpaperEntry(name ?? Path.GetFileName(directory), directory, type, contentFile, image);
                }
                catch (Exception) { /* 单个损坏项目不影响其他壁纸。 */ }
                if (entry is not null) yield return entry;
            }
    }

    private sealed record WallpaperEntry(string Title, string Directory, string? Type, string? ContentFile, Image? Preview);

    private static string? ResolveProjectFile(string directory, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return null;
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(directory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
    }

    private static bool CanExportMedia(WallpaperEntry entry)
    {
        var file = ResolveProjectFile(entry.Directory, entry.ContentFile);
        if (entry.Type?.Equals("video", StringComparison.OrdinalIgnoreCase) == true)
            return file is not null && File.Exists(file) && Path.GetExtension(file).Equals(".mp4", StringComparison.OrdinalIgnoreCase);
        if (entry.Type?.Equals("image", StringComparison.OrdinalIgnoreCase) == true)
            return file is not null && File.Exists(file);
        return File.Exists(Path.Combine(entry.Directory, "scene.pkg")) || GetLooseSceneAssets(entry).Length > 0;
    }

    private static string[] GetLooseSceneAssets(WallpaperEntry entry)
    {
        if (!string.Equals(entry.Type, "scene", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(ResolveProjectFile(entry.Directory, entry.ContentFile))) return [];
        var preview = ResolveProjectFile(entry.Directory, GetProjectPreview(entry.Directory));
        return Directory.EnumerateFiles(entry.Directory, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint
        }).Where(path => !string.Equals(path, preview, StringComparison.OrdinalIgnoreCase) &&
            new[] { ".tex", ".png", ".jpg", ".jpeg", ".bmp", ".mp4" }
                .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    private async Task ExportLooseSceneAsync(WallpaperEntry entry, string staging)
    {
        var assets = GetLooseSceneAssets(entry);
        if (assets.Length == 0) throw new InvalidDataException("场景中没有可导出的素材");
        foreach (var asset in assets)
        {
            var target = Path.Combine(staging, Path.GetRelativePath(entry.Directory, asset));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!Path.GetExtension(asset).Equals(".tex", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(asset, target, false);
                continue;
            }

            // 工具可能删除输入 TEX；始终在独立工作目录中转换副本。
            var work = Path.Combine(staging, $".convert-{Guid.NewGuid():N}");
            Directory.CreateDirectory(work);
            try
            {
                var copy = Path.Combine(work, Path.GetFileName(asset));
                File.Copy(asset, copy);
                await RunToolAsync("extract", copy, "-o", work, "--no-keep-tex");
                var converted = Directory.GetFiles(work).Where(path =>
                    new[] { ".png", ".mp4", ".jpg", ".jpeg", ".bmp" }
                        .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)).ToArray();
                if (converted.Length == 0) throw new InvalidDataException($"纹理转换未生成图片或视频：{asset}");
                foreach (var file in converted)
                    File.Move(file, Path.Combine(Path.GetDirectoryName(target)!, Path.GetFileName(file)), false);
            }
            finally { Directory.Delete(work, true); }
        }
    }

    private sealed class WallpaperGallery : Panel
    {
        private IReadOnlyList<WallpaperEntry> _entries = Array.Empty<WallpaperEntry>();
        private readonly Dictionary<int, Bitmap> _tiles = new();
        private int TileSize => LogicalToDeviceUnits(GalleryTileSize);
        public event Action<WallpaperEntry>? EntryClicked;

        public WallpaperGallery()
        {
            Cursor = Cursors.Hand;
            TabStop = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
        }

        public void SetEntries(IReadOnlyList<WallpaperEntry> entries)
        {
            ClearTiles();
            _entries = entries;
            UpdateExtent();
        }

        private void ClearTiles()
        {
            foreach (var tile in _tiles.Values) tile.Dispose();
            _tiles.Clear();
        }

        private void UpdateExtent()
        {
            AutoScrollPosition = Point.Empty;
            AutoScrollMinSize = _entries.Count == 0 ? Size.Empty :
                new Size(3 * TileSize, ((_entries.Count + 2) / 3) * TileSize);
            Invalidate();
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            ClearTiles();
            UpdateExtent();
        }

        protected override void OnScroll(ScrollEventArgs e)
        {
            base.OnScroll(e);
            // 一次重绘整个可见区域，避免滚动复制的旧像素留下拖影。
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            var x = e.X - AutoScrollPosition.X;
            var y = e.Y - AutoScrollPosition.Y;
            if (x < 0 || x >= 3 * TileSize || y < 0) return;
            var index = y / TileSize * 3 + x / TileSize;
            if (index < _entries.Count) EntryClicked?.Invoke(_entries[index]);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BackColor);
            var size = TileSize;
            var offset = AutoScrollPosition;
            var firstRow = Math.Max(0, -offset.Y / size);
            var lastRow = Math.Min((_entries.Count + 2) / 3,
                (ClientSize.Height - offset.Y + size - 1) / size);
            for (var row = firstRow; row < lastRow; row++)
                for (var column = 0; column < 3; column++)
                {
                    var index = row * 3 + column;
                    if (index >= _entries.Count) break;
                    if (!_tiles.TryGetValue(index, out var tile))
                    {
                        tile = RenderTile(_entries[index], size, column == 2);
                        _tiles.Add(index, tile);
                    }
                    e.Graphics.DrawImageUnscaled(tile, column * size + offset.X, row * size + offset.Y);
                }
        }

        private Bitmap RenderTile(WallpaperEntry entry, int size, bool lastColumn)
        {
            var tile = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using var graphics = Graphics.FromImage(tile);
            graphics.Clear(BackColor);
            var shadowSize = Math.Max(1, LogicalToDeviceUnits(4));
            var rightSpacing = lastColumn ? Math.Max(1, LogicalToDeviceUnits(3)) : shadowSize;
            var bounds = new Rectangle(0, 0, size - rightSpacing, size - shadowSize);
            DrawTileShadow(graphics, bounds, shadowSize);
            if (entry.Preview is not null)
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                var image = entry.Preview;
                var scale = Math.Max((float)bounds.Width / image.Width, (float)bounds.Height / image.Height);
                var sourceWidth = bounds.Width / scale;
                var sourceHeight = bounds.Height / scale;
                var source = new RectangleF((image.Width - sourceWidth) / 2, (image.Height - sourceHeight) / 2,
                    sourceWidth, sourceHeight);
                graphics.DrawImage(image, bounds, source, GraphicsUnit.Pixel);
            }
            else
            {
                using var brush = new SolidBrush(Color.FromArgb(235, 238, 242));
                graphics.FillRectangle(brush, bounds);
                TextRenderer.DrawText(graphics, "无预览", Font, bounds, Color.DimGray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            return tile;
        }

        private static void DrawTileShadow(Graphics graphics, Rectangle bounds, int spread)
        {
            // 边缘和拐角共用同一衰减曲线；不叠加两条阴影，避免右下角发黑或缺口。
            int Opacity(double distance)
            {
                var strength = Math.Max(0, 1 - distance / spread);
                return (int)Math.Round(52 * strength * strength);
            }

            for (var i = 0; i < spread; i++)
            {
                using var brush = new SolidBrush(Color.FromArgb(Opacity(i + 0.5), Color.Black));
                graphics.FillRectangle(brush, bounds.Right + i, bounds.Top, 1, bounds.Height);
                graphics.FillRectangle(brush, bounds.Left, bounds.Bottom + i, bounds.Width, 1);
            }
            for (var y = 0; y < spread; y++)
                for (var x = 0; x < spread; x++)
                {
                    var distance = Math.Sqrt((x + 0.5) * (x + 0.5) + (y + 0.5) * (y + 0.5));
                    using var brush = new SolidBrush(Color.FromArgb(Opacity(distance), Color.Black));
                    graphics.FillRectangle(brush, bounds.Right + x, bounds.Bottom + y, 1, 1);
                }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ClearTiles();
            base.Dispose(disposing);
        }
    }

    private static Button Button(string text, EventHandler handler, Rectangle bounds)
    {
        var button = new Button { Text = text, Bounds = bounds, TextAlign = ContentAlignment.MiddleCenter };
        button.Click += handler;
        return button;
    }

    private static string GetDownloadsFolder()
    {
        var folderId = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        var result = SHGetKnownFolderPath(ref folderId, 0, IntPtr.Zero, out var pathPointer);
        try
        {
            if (result == 0 && pathPointer != IntPtr.Zero)
            {
                var path = Marshal.PtrToStringUni(pathPointer);
                if (!string.IsNullOrWhiteSpace(path)) return path;
            }
        }
        finally
        {
            if (pathPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pathPointer);
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);

    private sealed class CenteredToolbarButton : Button
    {
        private readonly string _caption;

        public CenteredToolbarButton(string caption)
        {
            _caption = caption;
            AccessibleName = caption;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var textBounds = ClientRectangle;
            textBounds.Inflate(-2, -1);
            textBounds.Offset(0, -2);
            TextRenderer.DrawText(e.Graphics, _caption, Font, textBounds,
                Enabled ? ForeColor : SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }

    private void PickFiles()
    {
        using var dialog = new OpenFileDialog { Filter = "壁纸包|*.pkg;*.mpkg|所有文件|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) AddFiles(dialog.FileNames);
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        if (_busy) return;
        foreach (var path in paths.Where(File.Exists).Where(p => new[] { ".pkg", ".mpkg" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)))
            if (!_paths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _paths.Add(path);
                var index = _files.Rows.Add(Path.GetFileName(path), Path.GetExtension(path).TrimStart('.').ToUpperInvariant(), "待处理");
                _files.Rows[index].Tag = path;
                _files.Rows[index].Cells[0].ToolTipText = path;
            }
        _files.ClearSelection();
        UpdateFileSummary();
    }

    private void PickOutput()
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = _output.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK) _output.Text = dialog.SelectedPath;
    }

    private void PickMediaOutput()
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = _mediaOutput.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK) _mediaOutput.Text = dialog.SelectedPath;
    }

    private void OpenOutput()
    {
        if (string.IsNullOrWhiteSpace(_output.Text)) { MessageBox.Show(this, "请先选择项目目录。"); return; }
        Directory.CreateDirectory(_output.Text);
        Process.Start(new ProcessStartInfo("explorer.exe", _output.Text) { UseShellExecute = true });
    }

    private async Task ImportPackagesAsync()
    {
        if (_busy) return;
        var selected = _files.SelectedRows.Cast<DataGridViewRow>().OrderBy(row => row.Index)
            .Select(row => (string)row.Tag!).ToArray();
        if (selected.Length == 0) selected = _paths.ToArray();
        if (selected.Length == 0) { MessageBox.Show(this, "请先选择 PKG 或 MPKG 文件。"); return; }
        if (string.IsNullOrWhiteSpace(_output.Text)) { MessageBox.Show(this, "未找到 Wallpaper Engine 的 myprojects，请先选择恢复目录。"); return; }

        _busy = true;
        _extract.Enabled = _bulkMedia.Enabled = false;
        try
        {
            Directory.CreateDirectory(_output.Text);
            var completed = 0;
            foreach (var file in selected)
            {
                var name = GetProjectFolderName(file);
                var destination = Path.Combine(_output.Text, name);
                if (Directory.Exists(destination) || File.Exists(destination))
                {
                    SetFileStatus(file, "已跳过：目录已存在");
                    Log($"跳过 {name}：项目目录已存在，避免覆盖。\r\n");
                    continue;
                }
                _status.Text = $"正在恢复：{name}";
                SetFileStatus(file, "正在恢复");
                var staging = Path.Combine(_output.Text, $".wallcraft-{Guid.NewGuid():N}");
                Directory.CreateDirectory(staging);
                try
                {
                    await RunToolAsync("extract", file, "-o", staging, "--no-tex-convert");
                    PrepareProject(file, staging, name);
                    Directory.Move(staging, destination);
                    Log($"已恢复：{destination}\r\n");
                    if (Path.GetExtension(file).Equals(".mpkg", StringComparison.OrdinalIgnoreCase))
                        Log("注意：Android MPKG 的纹理未转码，桌面版可能无法显示，请在 Wallpaper Engine 中检查。\r\n");
                    completed++;
                    SetFileStatus(file, "已完成");
                }
                catch (Exception error) { SetFileStatus(file, "恢复失败", error.Message); Log($"恢复失败：{file}\r\n{error.Message}\r\n"); }
                finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            }
            _status.Text = $"恢复完成：{completed}/{selected.Length} 个项目";
        }
        catch (Exception error)
        {
            Log($"恢复失败：{error.Message}\r\n");
            _status.Text = "恢复失败";
        }
        finally { _busy = false; _extract.Enabled = _bulkMedia.Enabled = true; }
    }

    private static string GetProjectFolderName(string package)
    {
        var name = Path.GetFileNameWithoutExtension(package);
        if (!name.Equals("scene", StringComparison.OrdinalIgnoreCase)) return name;
        return Path.GetFileName(Path.GetDirectoryName(package)) ?? name;
    }

    private static void PrepareProject(string package, string staging, string name)
    {
        var metadataPath = Path.Combine(staging, "project.json");
        var packageFolder = Path.GetDirectoryName(package) ?? "";
        var sidecar = Path.Combine(packageFolder, "project.json");
        if (!File.Exists(metadataPath) && File.Exists(sidecar)) File.Copy(sidecar, metadataPath);

        JsonObject project;
        if (File.Exists(metadataPath))
            project = JsonNode.Parse(File.ReadAllText(metadataPath)) as JsonObject
                ?? throw new InvalidDataException("project.json 格式无效");
        else if (File.Exists(Path.Combine(staging, "scene.json")))
            project = new JsonObject { ["type"] = "scene", ["file"] = "scene.json" };
        else if (File.Exists(Path.Combine(staging, "index.html")))
            project = new JsonObject { ["type"] = "web", ["file"] = "index.html" };
        else
            throw new InvalidDataException("包内没有可识别的壁纸入口文件，未加入 myprojects");

        if (project["title"] is null) project["title"] = name;
        var mainFile = ResolveProjectFile(staging, project["file"]?.ToString());
        if (mainFile is null || !File.Exists(mainFile))
            throw new InvalidDataException("project.json 指向的入口文件不存在，未加入 myprojects");

        var previewName = project["preview"]?.ToString();
        if (string.IsNullOrWhiteSpace(previewName))
        {
            previewName = new[] { "preview.jpg", "preview.png", "preview.gif" }
                .FirstOrDefault(file => File.Exists(Path.Combine(packageFolder, file)));
            if (previewName is not null) project["preview"] = previewName;
        }
        var previewSource = ResolveProjectFile(packageFolder, previewName);
        var previewTarget = ResolveProjectFile(staging, previewName);
        if (previewSource is not null && previewTarget is not null && File.Exists(previewSource) && !File.Exists(previewTarget))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(previewTarget)!);
            File.Copy(previewSource, previewTarget);
        }
        File.WriteAllText(metadataPath, project.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task ExportSubscribedMediaAsync()
    {
        if (_busy) return;
        var entries = GetInstalledWallpapers(subscribedOnly: true, loadPreviews: false).ToList();
        if (entries.Count == 0) { MessageBox.Show(this, "没有找到已订阅的壁纸。"); return; }
        _busy = true;
        _extract.Enabled = _bulkMedia.Enabled = false;
        var completed = 0;
        try
        {
            foreach (var entry in entries)
            {
                if (!CanExportMedia(entry)) { Log($"跳过 {entry.Title}：没有可直接导出的 PNG 或 MP4。\r\n"); continue; }
                _status.Text = $"正在导出：{completed}/{entries.Count} {entry.Title}";
                try { await ExportMediaAsync(entry, _mediaOutput.Text); completed++; }
                catch (Exception error) { Log($"导出失败：{entry.Title}：{error.Message}\r\n"); }
            }
            _status.Text = $"媒体导出完成：{completed}/{entries.Count}";
        }
        finally { _busy = false; _extract.Enabled = _bulkMedia.Enabled = true; }
    }

    private async Task<string> ExportMediaAsync(WallpaperEntry entry, string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot)) throw new InvalidOperationException("请先选择媒体保存目录");
        Directory.CreateDirectory(outputRoot);
        var destination = Path.Combine(outputRoot, Path.GetFileName(entry.Directory));
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"目标目录已存在，未覆盖：{destination}");
        var staging = Path.Combine(outputRoot, $".wallcraft-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var source = ResolveProjectFile(entry.Directory, entry.ContentFile);
            if (entry.Type?.Equals("video", StringComparison.OrdinalIgnoreCase) == true && source is not null && File.Exists(source))
            {
                if (!Path.GetExtension(source).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("源视频不是 MP4，当前不进行转码");
                await using var input = File.OpenRead(source);
                await using var output = File.Create(Path.Combine(staging, Path.GetFileName(source)));
                await input.CopyToAsync(output);
            }
            else if (entry.Type?.Equals("image", StringComparison.OrdinalIgnoreCase) == true && source is not null && File.Exists(source))
            {
                using var image = Image.FromFile(source);
                image.Save(Path.Combine(staging, "wallpaper.png"), ImageFormat.Png);
            }
            else
            {
                var package = Path.Combine(entry.Directory, "scene.pkg");
                if (File.Exists(package))
                    await RunToolAsync("extract", package, "-o", staging, "-e", "tex,mp4", "--no-keep-tex");
                else
                    await ExportLooseSceneAsync(entry, staging);
                foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories)
                    .Where(path => new[] { ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)).ToArray())
                {
                    var png = Path.ChangeExtension(file, ".png");
                    if (File.Exists(png)) throw new IOException($"素材转换后的文件名冲突：{png}");
                    using (var image = Image.FromFile(file)) image.Save(png, ImageFormat.Png);
                    File.Delete(file);
                }
                if (!Directory.EnumerateFiles(staging, "*.png", SearchOption.AllDirectories).Any() &&
                    !Directory.EnumerateFiles(staging, "*.mp4", SearchOption.AllDirectories).Any())
                    throw new InvalidDataException("场景包里没有可导出的图片或 MP4");
                var preview = ResolveProjectFile(entry.Directory, GetProjectPreview(entry.Directory));
                if (preview is not null && File.Exists(preview))
                {
                    using var image = Image.FromFile(preview);
                    image.Save(Path.Combine(staging, "preview.png"), ImageFormat.Png);
                }
            }
            if (!Directory.EnumerateFiles(staging, "*.png", SearchOption.AllDirectories).Any() &&
                !Directory.EnumerateFiles(staging, "*.mp4", SearchOption.AllDirectories).Any())
                throw new InvalidDataException("没有生成 PNG 或 MP4");
            Directory.Move(staging, destination);
            Log($"已导出：{entry.Title} → {destination}\r\n");
            return destination;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    private static string? GetProjectPreview(string directory)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "project.json")));
        return json.RootElement.TryGetProperty("preview", out var preview) ? preview.GetString() : null;
    }

    private async Task RunToolAsync(params string[] arguments)
    {
        var tool = Path.Combine(AppContext.BaseDirectory, "repkg-ng", "repkg-win-x64.exe");
        if (!File.Exists(tool)) throw new FileNotFoundException("找不到解包工具", tool);
        var start = new ProcessStartInfo(tool)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动解包工具");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Log(await stdout);
        Log(await stderr);
        if (process.ExitCode != 0) throw new InvalidOperationException($"解包工具退出码：{process.ExitCode}");
    }

    private void Log(string message) => _log.AppendText(message);
}
