using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NavisFavorites.Models;
using NavisFavorites.Services;

namespace NavisFavorites.UI
{
    internal sealed class FolderPickerDialog : Form
    {
        private readonly FavoritesService _service;
        private readonly ModelFavoriteScope _scope;
        private readonly ListBox _folders;

        public FolderPickerDialog(FavoritesService service, ModelFavoriteScope scope)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _scope = scope ?? throw new ArgumentNullException(nameof(scope));

            Text = "添加到收藏夹";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(400, 340);

            var modelLabel = new Label
            {
                AutoEllipsis = true,
                Text = "源模型：" + scope.DisplayName,
                Location = new Point(12, 12),
                Size = new Size(376, 22)
            };
            var prompt = new Label
            {
                AutoSize = true,
                Text = "选择目标收藏夹：",
                Location = new Point(12, 41)
            };

            _folders = new ListBox
            {
                DisplayMember = nameof(FavoriteFolder.Name),
                Location = new Point(15, 65),
                Size = new Size(370, 210),
                IntegralHeight = false
            };
            _folders.DoubleClick += (sender, args) => ConfirmSelection();

            var create = new Button
            {
                Text = "+ 新建收藏夹",
                Location = new Point(15, 292),
                Size = new Size(120, 30)
            };
            create.Click += CreateFolder;

            var ok = new Button
            {
                Text = "添加",
                Location = new Point(229, 292),
                Size = new Size(75, 30)
            };
            ok.Click += (sender, args) => ConfirmSelection();

            var cancel = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(310, 292),
                Size = new Size(75, 30)
            };

            Controls.Add(modelLabel);
            Controls.Add(prompt);
            Controls.Add(_folders);
            Controls.Add(create);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            ReloadFolders();
        }

        public FavoriteFolder SelectedFolder => _folders.SelectedItem as FavoriteFolder;

        private void ReloadFolders(Guid? preferredId = null)
        {
            _folders.BeginUpdate();
            try
            {
                _folders.Items.Clear();
                foreach (var folder in _scope.Folders)
                {
                    _folders.Items.Add(folder);
                }

                var targetId = preferredId ?? _service.Settings.LastFolderId;
                var selected = _scope.Folders.FirstOrDefault(folder => folder.Id == targetId)
                               ?? _scope.Folders.FirstOrDefault();
                if (selected != null)
                {
                    _folders.SelectedItem = selected;
                }
            }
            finally
            {
                _folders.EndUpdate();
            }
        }

        private void CreateFolder(object sender, EventArgs args)
        {
            using (var dialog = new TextPromptDialog("新建收藏夹", "收藏夹名称："))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    var folder = _service.CreateFolder(_scope, dialog.Value);
                    ReloadFolders(folder.Id);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "NavisFavorites", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void ConfirmSelection()
        {
            if (SelectedFolder == null)
            {
                MessageBox.Show(this, "请选择一个收藏夹。", "NavisFavorites", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
