using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ExpControlsLib;

/// <summary>Provides the shared new-folder name prompt used by explorer controls.</summary>
internal static class NewFolderPrompt
{
    private const string DefaultFolderName = "New Folder";

    internal static string GetAvailableFolderName(
        string currentFolderPath,
        string baseName = DefaultFolderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentFolderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);

        string candidate = baseName;
        int suffix = 2;
        while (File.Exists(Path.Combine(currentFolderPath, candidate)) ||
               Directory.Exists(Path.Combine(currentFolderPath, candidate)))
        {
            candidate = $"{baseName} ({suffix++})";
        }

        return candidate;
    }

    internal static bool TryShow(
        IWin32Window owner,
        string currentFolderPath,
        out string folderName)
    {
        folderName = string.Empty;
        string defaultName = GetAvailableFolderName(currentFolderPath);
        using var dialog = CreateDialog(currentFolderPath, defaultName);

        if (dialog.ShowDialog(owner) != DialogResult.OK)
            return false;

        folderName = ((TextBox)dialog.Tag!).Text;
        return true;
    }

    private static Form CreateDialog(string currentFolderPath, string defaultName)
    {
        var dialog = new Form
        {
            Text = "New Folder",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(390, 132),
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false
        };

        var prompt = new Label
        {
            AutoSize = true,
            Location = new Point(12, 14),
            Text = "Enter a name for the new folder:"
        };
        var nameTextBox = new TextBox
        {
            Location = new Point(15, 39),
            Size = new Size(360, 23),
            Text = defaultName
        };
        var okButton = new Button
        {
            Location = new Point(219, 87),
            Size = new Size(75, 27),
            Text = "OK"
        };
        var cancelButton = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(300, 87),
            Size = new Size(75, 27),
            Text = "Cancel"
        };

        okButton.Click += (_, _) =>
        {
            string candidateName = nameTextBox.Text.Trim();
            string? validationError = ValidateFolderName(currentFolderPath, candidateName);
            if (validationError != null)
            {
                MessageBox.Show(dialog, validationError, "New Folder",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                nameTextBox.Focus();
                nameTextBox.SelectAll();
                return;
            }

            nameTextBox.Text = candidateName;
            dialog.DialogResult = DialogResult.OK;
        };

        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;
        dialog.Controls.AddRange(new Control[] { prompt, nameTextBox, okButton, cancelButton });
        dialog.Tag = nameTextBox;
        dialog.Shown += (_, _) =>
        {
            nameTextBox.Focus();
            nameTextBox.SelectAll();
        };
        return dialog;
    }

    private static string? ValidateFolderName(string currentFolderPath, string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
            return "Enter a folder name.";

        if (folderName is "." or ".." ||
            folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            folderName.EndsWith(' ') ||
            folderName.EndsWith('.'))
        {
            return "The folder name contains invalid characters or formatting.";
        }

        string candidatePath = Path.Combine(currentFolderPath, folderName);
        if (File.Exists(candidatePath) || Directory.Exists(candidatePath))
            return $"A file or folder named \"{folderName}\" already exists.";

        return null;
    }
}
