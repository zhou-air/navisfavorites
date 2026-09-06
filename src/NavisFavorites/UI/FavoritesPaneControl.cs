using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using NavisFavorites.Models;
using NavisFavorites.Plugin;
using NavisFavorites.Services;
using NwApplication = Autodesk.Navisworks.Api.Application;

namespace NavisFavorites.UI
{
    public sealed class FavoritesPaneControl : UserControl
    {
        private sealed class FolderNodeInfo
        {
            public FavoriteFolder Folder { get; set; }
        }

        private sealed class ItemNodeInfo
        {
            public FavoriteFolder Folder { get; set; }

            public FavoriteItem Item { get; set; }

            public bool IsValid { get; set; }
        }

        private readonly ComboBox _modelCombo;
        private readonly TreeView _tree;
        private readonly ToolStripStatusLabel _statusLabel;
        private readonly Panel _migrationBanner;
        private readonly Label _migrationLabel;
        private readonly Button _migrationDetailsButton;
        private readonly ContextMenuStrip _itemMenu;
        private readonly ContextMenuStrip _folderMenu;
        private readonly HashSet<Guid> _selectedItemIds = new HashSet<Guid>();
        private Guid? _selectionAnchorId;
        private Guid? _activeFolderId;
        private bool _suppressModelChange;
        private bool _reloadingModels;
        private Document _subscribedDocument;
        private MigrationReport _lastMigrationReport;

        public FavoritesPaneControl()
        {
            Font = SystemFonts.MessageBoxFont;
            BackColor = SystemColors.Control;
            Dock = DockStyle.Fill;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(6)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var modelRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Margin = new Padding(0, 0, 0, 6)
            };
            modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            modelRow.Controls.Add(new Label
            {
                Text = "源模型：",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 5, 6, 0)
            }, 0, 0);
            _modelCombo = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = nameof(LiveModelContext.DisplayName)
            };
            _modelCombo.SelectedIndexChanged += OnModelChanged;
            modelRow.Controls.Add(_modelCombo, 1, 0);

            _migrationBanner = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                BackColor = System.Drawing.Color.FromArgb(225, 242, 255),
                Padding = new Padding(8),
                Margin = new Padding(0, 0, 0, 6),
                Visible = false
            };
            var migrationBannerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 3,
                RowCount = 1
            };
            migrationBannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            migrationBannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            migrationBannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _migrationLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                Text = string.Empty,
                Anchor = AnchorStyles.Left
            };
            _migrationDetailsButton = new Button { Text = "查看", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
            _migrationDetailsButton.Click += (sender, args) => MigrationDialog.ShowReport(this, _lastMigrationReport);
            var dismissMigration = new Button { Text = "关闭", AutoSize = true, Margin = new Padding(4, 0, 0, 0) };
            dismissMigration.Click += (sender, args) =>
            {
                if (_lastMigrationReport != null)
                {
                    PluginRuntime.Favorites.AcknowledgeMigration(_lastMigrationReport.Id);
                }

                _migrationBanner.Visible = false;
            };
            migrationBannerLayout.Controls.Add(_migrationLabel, 0, 0);
            migrationBannerLayout.Controls.Add(_migrationDetailsButton, 1, 0);
            migrationBannerLayout.Controls.Add(dismissMigration, 2, 0);
            _migrationBanner.Controls.Add(migrationBannerLayout);

            var managementBar = CreateFlowPanel();
            managementBar.Margin = new Padding(0, 0, 0, 6);
            managementBar.Controls.Add(CreateButton("+ 新建", (sender, args) => CreateFolder()));
            managementBar.Controls.Add(CreateButton("重命名", (sender, args) => RenameFolder()));
            managementBar.Controls.Add(CreateButton("删除", (sender, args) => DeleteFolder()));
            managementBar.Controls.Add(CreateButton("★ 收藏当前选择…", (sender, args) => PluginRuntime.AddCurrentSelectionWithDialog(this)));
            managementBar.Controls.Add(CreateButton("版本迁移…", (sender, args) =>
                PluginRuntime.ShowMigrationDialog(this, SelectedModelContext?.Model)));

            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                FullRowSelect = true,
                DrawMode = TreeViewDrawMode.OwnerDrawText,
                BorderStyle = BorderStyle.FixedSingle
            };
            _tree.DrawNode += DrawTreeNode;
            _tree.MouseDown += TreeMouseDown;
            _tree.MouseUp += TreeMouseUp;
            _tree.KeyDown += TreeKeyDown;
            _tree.NodeMouseDoubleClick += (sender, args) =>
            {
                if (args.Node?.Tag is ItemNodeInfo)
                {
                    ApplySelection();
                }
            };

            var actionBar = CreateFlowPanel();
            actionBar.Margin = new Padding(0, 6, 0, 2);
            actionBar.Controls.Add(CreateButton("全选", (sender, args) => SelectAllInContext()));
            actionBar.Controls.Add(CreateButton("选择", (sender, args) => ApplySelection()));
            actionBar.Controls.Add(CreateButton("隐藏", (sender, args) => ApplyVisibility(VisibilityAction.Hide)));
            actionBar.Controls.Add(CreateButton("隐藏未选定", (sender, args) => ApplyVisibility(VisibilityAction.HideUnselected)));
            actionBar.Controls.Add(CreateButton("显示", (sender, args) => ApplyVisibility(VisibilityAction.Show)));
            actionBar.Controls.Add(CreateButton("全部显示", (sender, args) => ApplyVisibility(VisibilityAction.ShowAll)));

            var statusStrip = new StatusStrip
            {
                Dock = DockStyle.Fill,
                SizingGrip = false,
                Padding = new Padding(0),
                Margin = new Padding(0, 3, 0, 0)
            };
            _statusLabel = new ToolStripStatusLabel
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "就绪"
            };
            statusStrip.Items.Add(_statusLabel);

            root.Controls.Add(modelRow, 0, 0);
            root.Controls.Add(_migrationBanner, 0, 1);
            root.Controls.Add(managementBar, 0, 2);
            root.Controls.Add(_tree, 0, 3);
            root.Controls.Add(actionBar, 0, 4);
            root.Controls.Add(statusStrip, 0, 5);
            Controls.Add(root);

            _itemMenu = BuildItemMenu();
            _folderMenu = BuildFolderMenu();

            PluginRuntime.Favorites.Changed += OnFavoritesChanged;
            PluginRuntime.Migration.ReportAvailable += OnMigrationReportAvailable;
            PluginRuntime.StatusChanged += OnRuntimeStatusChanged;
            NwApplication.ActiveDocumentChanged += OnActiveDocumentChanged;
            AttachDocument(NwApplication.ActiveDocument);
            ReloadModels(true);

            if (!string.IsNullOrWhiteSpace(PluginRuntime.Favorites.LastStorageWarning))
            {
                SetStatus(PluginRuntime.Favorites.LastStorageWarning);
            }
            else
            {
                SetStatus(PluginRuntime.LastStatus);
            }
        }

        private enum VisibilityAction
        {
            Hide,
            Show,
            HideUnselected,
            ShowAll
        }

        private LiveModelContext SelectedModelContext => _modelCombo.SelectedItem as LiveModelContext;

        private ModelFavoriteScope CurrentScope => SelectedModelContext?.Scope;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                PluginRuntime.Favorites.Changed -= OnFavoritesChanged;
                PluginRuntime.Migration.ReportAvailable -= OnMigrationReportAvailable;
                PluginRuntime.StatusChanged -= OnRuntimeStatusChanged;
                NwApplication.ActiveDocumentChanged -= OnActiveDocumentChanged;
                AttachDocument(null);
                _itemMenu?.Dispose();
                _folderMenu?.Dispose();
            }

            base.Dispose(disposing);
        }

        private static FlowLayoutPanel CreateFlowPanel()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight
            };
        }

        private static Button CreateButton(string text, EventHandler click)
        {
            var button = new Button
            {
                AutoSize = true,
                MinimumSize = new Size(64, 28),
                Text = text,
                Margin = new Padding(0, 0, 5, 4)
            };
            button.Click += click;
            return button;
        }

        private ContextMenuStrip BuildItemMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("选择", null, (sender, args) => ApplySelection());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("隐藏", null, (sender, args) => ApplyVisibility(VisibilityAction.Hide));
            menu.Items.Add("隐藏未选定", null, (sender, args) => ApplyVisibility(VisibilityAction.HideUnselected));
            menu.Items.Add("显示", null, (sender, args) => ApplyVisibility(VisibilityAction.Show));
            menu.Items.Add("全部显示", null, (sender, args) => ApplyVisibility(VisibilityAction.ShowAll));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("从收藏夹删除", null, (sender, args) => DeleteSelectedItems());
            return menu;
        }

        private ContextMenuStrip BuildFolderMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("全选", null, (sender, args) => SelectAllInContext());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("重命名", null, (sender, args) => RenameFolder());
            menu.Items.Add("删除收藏夹", null, (sender, args) => DeleteFolder());
            return menu;
        }

        private void OnActiveDocumentChanged(object sender, EventArgs args)
        {
            RunOnUiThread(() =>
            {
                AttachDocument(NwApplication.ActiveDocument);
                ReloadModels(true);
            });
        }

        private void AttachDocument(Document document)
        {
            if (_subscribedDocument != null)
            {
                _subscribedDocument.CurrentSelection.Changed -= OnDocumentSelectionChanged;
                _subscribedDocument.Models.CollectionChanged -= OnModelsChanged;
            }

            _subscribedDocument = document;
            if (_subscribedDocument != null)
            {
                _subscribedDocument.CurrentSelection.Changed += OnDocumentSelectionChanged;
                _subscribedDocument.Models.CollectionChanged += OnModelsChanged;
            }
        }

        private void OnDocumentSelectionChanged(object sender, EventArgs args)
        {
            RunOnUiThread(FollowCurrentSelectionModel);
        }

        private void OnModelsChanged(object sender, EventArgs args)
        {
            RunOnUiThread(() => ReloadModels(true));
        }

        private void OnFavoritesChanged(object sender, EventArgs args)
        {
            RunOnUiThread(() => ReloadModels(false));
        }

        private void OnRuntimeStatusChanged(object sender, string message)
        {
            RunOnUiThread(() => SetStatus(message));
        }

        private void OnMigrationReportAvailable(object sender, MigrationReport report)
        {
            RunOnUiThread(() => ShowMigrationBanner(report));
        }

        private void RunOnUiThread(Action action)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }

        private void ReloadModels(bool followSelection)
        {
            if (_reloadingModels)
            {
                return;
            }

            _reloadingModels = true;
            try
            {
                var automaticReport = PluginRuntime.CheckAutomaticInheritance();
                if (automaticReport != null)
                {
                    ShowMigrationBanner(automaticReport);
                }

            var previousModel = SelectedModelContext?.Model;
            var preferredScopeId = CurrentScope?.ScopeId ?? PluginRuntime.Favorites.Settings.LastModelScopeId;
            var models = PluginRuntime.Favorites.GetLiveModels(NwApplication.ActiveDocument);

            _suppressModelChange = true;
            try
            {
                _modelCombo.Items.Clear();
                foreach (var model in models)
                {
                    _modelCombo.Items.Add(model);
                }

                var selected = models.FirstOrDefault(item => previousModel != null && item.Model.Equals(previousModel))
                               ?? models.FirstOrDefault(item => item.Scope?.ScopeId == preferredScopeId)
                               ?? models.FirstOrDefault();
                if (selected != null)
                {
                    _modelCombo.SelectedItem = selected;
                }
            }
            finally
            {
                _suppressModelChange = false;
            }

            if (followSelection)
            {
                FollowCurrentSelectionModel();
            }

            RefreshTree();
            }
            finally
            {
                _reloadingModels = false;
            }
        }

        private void ShowMigrationBanner(MigrationReport report)
        {
            if (report == null || PluginRuntime.Favorites.Settings.LastAcknowledgedMigrationId == report.Id)
            {
                return;
            }

            _lastMigrationReport = report;
            _migrationLabel.Text = report.Message;
            _migrationDetailsButton.Enabled = report.Items != null && report.Items.Count > 0;
            _migrationBanner.Visible = true;
        }

        private void FollowCurrentSelectionModel()
        {
            var document = NwApplication.ActiveDocument;
            if (document == null || document.IsClear || document.CurrentSelection.IsEmpty)
            {
                return;
            }

            var models = document.CurrentSelection.SelectedItems
                .Where(item => item != null)
                .Select(item => ModelOwnership.GetModel(document, item))
                .Where(model => model != null)
                .Distinct()
                .ToList();
            if (models.Count != 1)
            {
                if (models.Count > 1)
                {
                    SetStatus("当前选择横跨多个源模型；收藏前请缩小到一个模型。");
                }

                return;
            }

            var target = _modelCombo.Items.Cast<LiveModelContext>().FirstOrDefault(item => item.Model.Equals(models[0]));
            if (target != null && !ReferenceEquals(_modelCombo.SelectedItem, target))
            {
                _modelCombo.SelectedItem = target;
            }
        }

        private void OnModelChanged(object sender, EventArgs args)
        {
            if (_suppressModelChange)
            {
                return;
            }

            _activeFolderId = CurrentScope?.Folders.FirstOrDefault()?.Id;
            _selectedItemIds.Clear();
            _selectionAnchorId = null;
            PluginRuntime.Favorites.SaveSettings(CurrentScope?.ScopeId, _activeFolderId);
            RefreshTree();
        }

        private void RefreshTree()
        {
            var selectedBefore = new HashSet<Guid>(_selectedItemIds);
            var scope = CurrentScope;
            var document = NwApplication.ActiveDocument;

            _tree.BeginUpdate();
            try
            {
                _tree.Nodes.Clear();
                if (scope == null)
                {
                    _selectedItemIds.Clear();
                    return;
                }

                foreach (var folder in scope.Folders)
                {
                    var folderNode = new TreeNode(folder.Name)
                    {
                        Tag = new FolderNodeInfo { Folder = folder },
                        ToolTipText = $"{folder.Items.Count} 个收藏项目"
                    };
                    foreach (var item in folder.Items)
                    {
                        var valid = document != null && PluginRuntime.Resolver.IsValid(document, scope, item);
                        var itemNode = new TreeNode(valid ? item.DisplayName : "⚠ " + item.DisplayName + "（对象已失效）")
                        {
                            Tag = new ItemNodeInfo { Folder = folder, Item = item, IsValid = valid },
                            ForeColor = valid ? SystemColors.WindowText : System.Drawing.Color.DarkOrange
                        };
                        folderNode.Nodes.Add(itemNode);
                    }

                    _tree.Nodes.Add(folderNode);
                    folderNode.Expand();
                }
            }
            finally
            {
                _tree.EndUpdate();
            }

            var availableIds = new HashSet<Guid>(GetAllItemNodes().Select(node => ((ItemNodeInfo)node.Tag).Item.Id));
            _selectedItemIds.Clear();
            foreach (var id in selectedBefore.Where(availableIds.Contains))
            {
                _selectedItemIds.Add(id);
            }

            if (_activeFolderId == null || scope.Folders.All(folder => folder.Id != _activeFolderId))
            {
                _activeFolderId = scope.Folders.FirstOrDefault()?.Id;
            }

            _tree.Invalidate();
        }

        private void DrawTreeNode(object sender, DrawTreeNodeEventArgs args)
        {
            if (!(args.Node.Tag is ItemNodeInfo itemInfo) || !_selectedItemIds.Contains(itemInfo.Item.Id))
            {
                args.DrawDefault = true;
                return;
            }

            var bounds = new Rectangle(args.Bounds.X, args.Bounds.Y,
                Math.Max(0, _tree.ClientSize.Width - args.Bounds.X), args.Bounds.Height);
            using (var background = new SolidBrush(SystemColors.Highlight))
            {
                args.Graphics.FillRectangle(background, bounds);
            }

            TextRenderer.DrawText(args.Graphics, args.Node.Text, _tree.Font, bounds,
                SystemColors.HighlightText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        private void TreeMouseDown(object sender, MouseEventArgs args)
        {
            var node = _tree.GetNodeAt(args.Location);
            if (node == null)
            {
                return;
            }

            _tree.SelectedNode = node;
            if (node.Tag is FolderNodeInfo folderInfo)
            {
                _activeFolderId = folderInfo.Folder.Id;
                if (args.Button == MouseButtons.Left)
                {
                    _selectedItemIds.Clear();
                    _selectionAnchorId = null;
                }

                _tree.Invalidate();
                return;
            }

            if (!(node.Tag is ItemNodeInfo itemInfo))
            {
                return;
            }

            _activeFolderId = itemInfo.Folder.Id;
            var modifiers = ModifierKeys;
            if (args.Button == MouseButtons.Right && _selectedItemIds.Contains(itemInfo.Item.Id))
            {
                return;
            }

            if ((modifiers & Keys.Shift) == Keys.Shift && _selectionAnchorId.HasValue)
            {
                SelectRange(_selectionAnchorId.Value, itemInfo.Item.Id, (modifiers & Keys.Control) == Keys.Control);
            }
            else if ((modifiers & Keys.Control) == Keys.Control)
            {
                MultiSelectionLogic.Toggle(_selectedItemIds, itemInfo.Item.Id);
                _selectionAnchorId = itemInfo.Item.Id;
            }
            else
            {
                MultiSelectionLogic.SelectSingle(_selectedItemIds, itemInfo.Item.Id);
                _selectionAnchorId = itemInfo.Item.Id;
            }

            _tree.Invalidate();
        }

        private void TreeMouseUp(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Right)
            {
                return;
            }

            var node = _tree.GetNodeAt(args.Location);
            if (node?.Tag is ItemNodeInfo)
            {
                _itemMenu.Show(_tree, args.Location);
            }
            else if (node?.Tag is FolderNodeInfo)
            {
                _folderMenu.Show(_tree, args.Location);
            }
        }

        private void TreeKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Control && args.KeyCode == Keys.A)
            {
                SelectAllInContext();
                args.Handled = true;
                args.SuppressKeyPress = true;
            }
            else if (args.KeyCode == Keys.Delete && _selectedItemIds.Count > 0)
            {
                DeleteSelectedItems();
                args.Handled = true;
            }
        }

        private void SelectRange(Guid anchorId, Guid targetId, bool preserveExisting)
        {
            var visibleIds = GetVisibleItemNodes()
                .Select(node => ((ItemNodeInfo)node.Tag).Item.Id)
                .ToList();
            MultiSelectionLogic.SelectRange(_selectedItemIds, visibleIds, anchorId, targetId, preserveExisting);
        }

        private IEnumerable<TreeNode> GetVisibleItemNodes()
        {
            foreach (TreeNode folderNode in _tree.Nodes)
            {
                if (!folderNode.IsExpanded)
                {
                    continue;
                }

                foreach (TreeNode itemNode in folderNode.Nodes)
                {
                    yield return itemNode;
                }
            }
        }

        private IEnumerable<TreeNode> GetAllItemNodes()
        {
            foreach (TreeNode folderNode in _tree.Nodes)
            {
                foreach (TreeNode itemNode in folderNode.Nodes)
                {
                    yield return itemNode;
                }
            }
        }

        private void SelectAllInContext()
        {
            var folderNode = _tree.Nodes.Cast<TreeNode>()
                .FirstOrDefault(node => node.Tag is FolderNodeInfo info && info.Folder.Id == _activeFolderId);
            var nodes = folderNode != null
                ? folderNode.Nodes.Cast<TreeNode>()
                : GetVisibleItemNodes();
            var ids = nodes.Where(node => node.Tag is ItemNodeInfo)
                .Select(node => ((ItemNodeInfo)node.Tag).Item.Id)
                .ToList();
            MultiSelectionLogic.SelectAll(_selectedItemIds, ids);

            _selectionAnchorId = _selectedItemIds.FirstOrDefault();
            _tree.Invalidate();
            SetStatus($"已选中 {_selectedItemIds.Count} 个收藏项目。");
        }

        private List<FavoriteItem> GetSelectedFavoriteItems()
        {
            var scope = CurrentScope;
            if (scope == null)
            {
                return new List<FavoriteItem>();
            }

            return scope.Folders.SelectMany(folder => folder.Items)
                .Where(item => _selectedItemIds.Contains(item.Id))
                .ToList();
        }

        private void ApplySelection()
        {
            var result = PluginRuntime.Selection.Select(NwApplication.ActiveDocument, CurrentScope, GetSelectedFavoriteItems());
            PluginRuntime.PublishStatus(result.Message);
        }

        private void ApplyVisibility(VisibilityAction action)
        {
            OperationResult result;
            if (action == VisibilityAction.ShowAll)
            {
                result = PluginRuntime.Visibility.ShowAll(NwApplication.ActiveDocument);
            }
            else
            {
                var items = GetSelectedFavoriteItems();
                switch (action)
                {
                    case VisibilityAction.Hide:
                        result = PluginRuntime.Visibility.Hide(NwApplication.ActiveDocument, CurrentScope, items);
                        break;
                    case VisibilityAction.Show:
                        result = PluginRuntime.Visibility.Show(NwApplication.ActiveDocument, CurrentScope, items);
                        break;
                    default:
                        result = PluginRuntime.Visibility.HideUnselected(NwApplication.ActiveDocument, CurrentScope, items);
                        break;
                }
            }

            PluginRuntime.PublishStatus(result.Message);
            RefreshTree();
        }

        private void CreateFolder()
        {
            var context = SelectedModelContext;
            if (context == null)
            {
                SetStatus("当前没有源模型。");
                return;
            }

            using (var dialog = new TextPromptDialog("新建收藏夹", "收藏夹名称："))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    var scope = context.Scope ?? PluginRuntime.Favorites.GetOrCreateScope(context.Model);
                    context.Scope = scope;
                    var folder = PluginRuntime.Favorites.CreateFolder(scope, dialog.Value);
                    _activeFolderId = folder.Id;
                    ReloadModels(false);
                    SetStatus("已新建收藏夹：“" + folder.Name + "”。");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "NavisFavorites", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private FavoriteFolder GetActiveFolder()
        {
            var scope = CurrentScope;
            if (scope == null)
            {
                return null;
            }

            if (_tree.SelectedNode?.Tag is FolderNodeInfo folderInfo)
            {
                return folderInfo.Folder;
            }

            if (_tree.SelectedNode?.Tag is ItemNodeInfo itemInfo)
            {
                return itemInfo.Folder;
            }

            return scope.Folders.FirstOrDefault(folder => folder.Id == _activeFolderId);
        }

        private void RenameFolder()
        {
            var folder = GetActiveFolder();
            if (folder == null)
            {
                SetStatus("请先选择收藏夹。");
                return;
            }

            using (var dialog = new TextPromptDialog("重命名收藏夹", "新名称：", folder.Name))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    PluginRuntime.Favorites.RenameFolder(CurrentScope, folder, dialog.Value);
                    SetStatus("收藏夹已重命名。");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "NavisFavorites", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void DeleteFolder()
        {
            var folder = GetActiveFolder();
            if (folder == null)
            {
                SetStatus("请先选择收藏夹。");
                return;
            }

            var message = folder.Items.Count == 0
                ? $"确定删除收藏夹“{folder.Name}”吗？"
                : $"收藏夹“{folder.Name}”包含 {folder.Items.Count} 项，确定全部删除吗？";
            if (MessageBox.Show(this, message, "NavisFavorites", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                != DialogResult.Yes)
            {
                return;
            }

            PluginRuntime.Favorites.DeleteFolder(CurrentScope, folder);
            _activeFolderId = CurrentScope?.Folders.FirstOrDefault()?.Id;
            _selectedItemIds.Clear();
            SetStatus("收藏夹已删除。");
        }

        private void DeleteSelectedItems()
        {
            if (_selectedItemIds.Count == 0)
            {
                SetStatus("请先选择收藏项目。");
                return;
            }

            var count = _selectedItemIds.Count;
            if (MessageBox.Show(this, $"确定从收藏夹删除选中的 {count} 项吗？", "NavisFavorites",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            var ids = new HashSet<Guid>(_selectedItemIds);
            var scope = CurrentScope;
            var removed = 0;
            foreach (var folder in scope.Folders.ToList())
            {
                removed += PluginRuntime.Favorites.DeleteItems(folder, ids);
            }

            _selectedItemIds.Clear();
            _selectionAnchorId = null;
            SetStatus($"已删除 {removed} 个收藏项目。");
        }

        private void SetStatus(string message)
        {
            _statusLabel.Text = string.IsNullOrWhiteSpace(message) ? "就绪" : message;
            _statusLabel.ToolTipText = _statusLabel.Text;
        }
    }
}
