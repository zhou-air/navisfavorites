using System;
using System.Drawing;
using System.Windows.Forms;

namespace NavisFavorites.UI
{
    internal sealed class TextPromptDialog : Form
    {
        private readonly TextBox _textBox;

        public TextPromptDialog(string title, string prompt, string initialValue = "")
        {
            Text = title;
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(390, 132);

            var label = new Label
            {
                AutoSize = true,
                Text = prompt,
                Location = new Point(12, 14)
            };

            _textBox = new TextBox
            {
                Location = new Point(15, 40),
                Size = new Size(360, 24),
                Text = initialValue ?? string.Empty
            };

            var ok = new Button
            {
                Text = "确定",
                DialogResult = DialogResult.OK,
                Location = new Point(219, 88),
                Size = new Size(75, 28)
            };
            var cancel = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(300, 88),
                Size = new Size(75, 28)
            };

            Controls.Add(label);
            Controls.Add(_textBox);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            Shown += (sender, args) =>
            {
                _textBox.Focus();
                _textBox.SelectAll();
            };
        }

        public string Value => _textBox.Text;
    }
}
