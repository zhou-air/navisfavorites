using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using NavisFavorites.Models;
using NavisFavorites.Services;

namespace NavisFavorites.UI
{
    internal sealed class MigrationDialog : Form
    {
        private sealed class TargetOption
        {
            public Model Model { get; set; }

            public string DisplayName { get; set; }

            public override string ToString() => DisplayName;
        }

        private readonly FavoritesMigrationService _migration;
        private readonly Document _document;
        private readonly ComboBox _targetCombo;
        private readonly ComboBox _sourceCombo;
        private readonly TextBox _details;
        private readonly Button _applyButton;
        private MigrationReport _preview;

        public MigrationDialog(FavoritesMigrationService migration, Document document, Model preferredModel)
        {
            _migration = migration ?? throw new ArgumentNullException(nameof(migration));
            _document = document ?? throw new ArgumentNullException(nameof(document));

            Text = "收藏夹版本迁移";
            Font = SystemFonts.MessageBoxFont;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            MinimumSize = new Size(600, 440);
            ClientSize = new Size(720, 520);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 2,
                RowCount = 5
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(CreateLabel("目标修改版："), 0, 0);
            _targetCombo = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _targetCombo.SelectedIndexChanged += (sender, args) => ReloadSources();
            layout.Controls.Add(_targetCombo, 1, 0);

            layout.Controls.Add(CreateLabel("收藏来源版："), 0, 1);
            _sourceCombo = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = nameof(ModelFavoriteScope.DisplayName)
            };
            _sourceCombo.SelectedIndexChanged += (sender, args) => ClearPreview();
            layout.Controls.Add(_sourceCombo, 1, 1);

            var explanation = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                ForeColor = SystemColors.GrayText,
                Text = "旧版收藏保持不变；只迁移新版中能够确定匹配的项目，同名收藏夹合并并去重。",
                Margin = new Padding(0, 8, 0, 8)
            };
            layout.Controls.Add(explanation, 0, 2);
            layout.SetColumnSpan(explanation, 2);

            _details = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = SystemColors.Window,
                Text = "选择来源和目标后点击“预览匹配”。"
            };
            layout.Controls.Add(_details, 0, 3);
            layout.SetColumnSpan(_details, 2);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0, 10, 0, 0)
            };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
            _applyButton = new Button { Text = "确认迁移", AutoSize = true, Enabled = false };
            _applyButton.Click += ApplyMigration;
            var preview = new Button { Text = "预览匹配", AutoSize = true };
            preview.Click += PreviewMigration;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(_applyButton);
            buttons.Controls.Add(preview);
            layout.Controls.Add(buttons, 0, 4);
            layout.SetColumnSpan(buttons, 2);

            Controls.Add(layout);
            CancelButton = cancel;

            foreach (var model in document.Models)
            {
                _targetCombo.Items.Add(new TargetOption
                {
                    Model = model,
                    DisplayName = FavoritesService.GetModelDisplayName(model)
                });
            }

            var preferred = _targetCombo.Items.Cast<TargetOption>()
                .FirstOrDefault(option => preferredModel != null && option.Model.Equals(preferredModel));
            _targetCombo.SelectedItem = preferred ?? _targetCombo.Items.Cast<object>().FirstOrDefault();
        }

        public MigrationReport AppliedReport { get; private set; }

        public static void ShowReport(IWin32Window owner, MigrationReport report)
        {
            if (report == null)
            {
                return;
            }

            MessageBox.Show(owner, FormatReport(report), "NavisFavorites 迁移明细",
                MessageBoxButtons.OK, report.Applied ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private Model TargetModel => (_targetCombo.SelectedItem as TargetOption)?.Model;

        private ModelFavoriteScope SourceScope => _sourceCombo.SelectedItem as ModelFavoriteScope;

        private static Label CreateLabel(string text)
        {
            return new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Text = text,
                Margin = new Padding(0, 5, 8, 8)
            };
        }

        private void ReloadSources()
        {
            _sourceCombo.BeginUpdate();
            try
            {
                _sourceCombo.Items.Clear();
                var target = TargetModel;
                var targetKey = ModelFamilyName.GetFamilyKey(target?.FileName);
                var sources = _migration.GetManualSources(target)
                    .OrderByDescending(scope => string.Equals(scope.FamilyKey, targetKey,
                        StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(scope => scope.FirstSeenUtc)
                    .ToList();
                foreach (var source in sources)
                {
                    _sourceCombo.Items.Add(source);
                }

                if (_sourceCombo.Items.Count > 0)
                {
                    _sourceCombo.SelectedIndex = 0;
                }
            }
            finally
            {
                _sourceCombo.EndUpdate();
            }

            ClearPreview();
        }

        private void PreviewMigration(object sender, EventArgs args)
        {
            if (TargetModel == null || SourceScope == null)
            {
                MessageBox.Show(this, "请选择目标模型和收藏来源版本。", "NavisFavorites",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _preview = _migration.Preview(_document, TargetModel, SourceScope, false);
            _details.Text = FormatReport(_preview);
            _applyButton.Enabled = _preview.MatchedCount > 0;
        }

        private void ApplyMigration(object sender, EventArgs args)
        {
            if (_preview == null || _preview.MatchedCount == 0)
            {
                return;
            }

            var result = _migration.Apply(_preview);
            if (!result.Success)
            {
                MessageBox.Show(this, result.Message, "NavisFavorites", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            AppliedReport = _preview;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ClearPreview()
        {
            _preview = null;
            if (_details != null)
            {
                _details.Text = _sourceCombo.Items.Count == 0
                    ? "没有可作为来源的旧版收藏。"
                    : "选择来源和目标后点击“预览匹配”。";
            }

            if (_applyButton != null)
            {
                _applyButton.Enabled = false;
            }
        }

        private static string FormatReport(MigrationReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine(report.Message);
            if (report.StructureSimilarity > 0)
            {
                builder.AppendLine($"结构相似度：{report.StructureSimilarity:P0}");
            }

            builder.AppendLine();
            foreach (var item in report.Items)
            {
                builder.Append(item.IsMatched ? "✓ " : item.IsAmbiguous ? "⚠ " : "× ")
                    .Append(item.SourceFolder?.Name ?? "（未知收藏夹）").Append(" / ")
                    .Append(item.SourceItem?.DisplayName ?? "（未命名）");
                if (item.IsMatched)
                {
                    builder.Append("  [").Append(item.Method).Append(']');
                }
                else if (!string.IsNullOrWhiteSpace(item.FailureReason))
                {
                    builder.Append("  — ").Append(item.FailureReason);
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }
    }
}
