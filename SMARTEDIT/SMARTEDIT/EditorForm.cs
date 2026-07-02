using System;
using System.IO;
using System.Windows.Forms;

namespace SMARTEDIT;

public class EditorForm : Form
{
    private RichTextBox editor;
    private string currentFile = "";

    public EditorForm()
    {
        InitializeEditor();
        InitializeMenu();
    }

    private void InitializeEditor()
    {
        Text = "Simple Notepad";
        Width = 1000;
        Height = 700;

        editor = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = new System.Drawing.Font("Consolas", 12),
            AcceptsTab = true
        };

        Controls.Add(editor);
    }

    private void InitializeMenu()
    {
        MenuStrip menu = new MenuStrip();

        // FILE MENU
        ToolStripMenuItem fileMenu = new("File");

        ToolStripMenuItem newFile = new ToolStripMenuItem("New");
        newFile.Click += NewFile_Click;

        ToolStripMenuItem openFile = new ToolStripMenuItem("Open");
        openFile.Click += OpenFile_Click;

        ToolStripMenuItem saveFile = new ToolStripMenuItem("Save");
        saveFile.Click += SaveFile_Click;

        ToolStripMenuItem saveAs = new ToolStripMenuItem("Save As");
        saveAs.Click += SaveAs_Click;

        ToolStripMenuItem exit = new("Exit");
        exit.Click += (s, e) => Close();

        fileMenu.DropDownItems.Add(newFile);
        fileMenu.DropDownItems.Add(openFile);
        fileMenu.DropDownItems.Add(saveFile);
        fileMenu.DropDownItems.Add(saveAs);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(exit);

        // EDIT MENU
        ToolStripMenuItem editMenu = new("Edit");

        ToolStripMenuItem cut = new("Cut");
        cut.Click += (s, e) => editor.Cut();

        ToolStripMenuItem copy = new("Copy");
        copy.Click += (s, e) => editor.Copy();

        ToolStripMenuItem paste = new("Paste");
        paste.Click += (s, e) => editor.Paste();

        editMenu.DropDownItems.Add(cut);
        editMenu.DropDownItems.Add(copy);
        editMenu.DropDownItems.Add(paste);

        // FORMAT MENU
        ToolStripMenuItem formatMenu = new("Format");

        ToolStripMenuItem wordWrap = new("Word Wrap");
        wordWrap.CheckOnClick = true;
        wordWrap.Checked = true;

        wordWrap.CheckedChanged += (s, e) =>
        {
            editor.WordWrap = wordWrap.Checked;
        };

        ToolStripMenuItem fontMenu = new("Font");
        fontMenu.Click += FontMenu_Click;

        formatMenu.DropDownItems.Add(wordWrap);
        formatMenu.DropDownItems.Add(fontMenu);

        menu.Items.Add(fileMenu);
        menu.Items.Add(editMenu);
        menu.Items.Add(formatMenu);

        MainMenuStrip = menu;
        Controls.Add(menu);
    }

    private void NewFile_Click(object sender, EventArgs e)
    {
        editor.Clear();
        currentFile = "";
        Text = "Simple Notepad";
    }

    private void OpenFile_Click(object sender, EventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Filter = "Text Files|*.txt|All Files|*.*"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            currentFile = dialog.FileName;
            editor.Text = File.ReadAllText(currentFile);
            Text = currentFile;
        }
    }

    private void SaveFile_Click(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(currentFile))
        {
            SaveAs_Click(sender, e);
        }
        else
        {
            File.WriteAllText(currentFile, editor.Text);
        }
    }

    private void SaveAs_Click(object sender, EventArgs e)
    {
        SaveFileDialog dialog = new()
        {
            Filter = "Text Files|*.txt|All Files|*.*"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            currentFile = dialog.FileName;
            File.WriteAllText(currentFile, editor.Text);
            Text = currentFile;
        }
    }

    private void FontMenu_Click(object sender, EventArgs e)
    {
        FontDialog fontDialog = new();

        if (fontDialog.ShowDialog() == DialogResult.OK)
        {
            editor.Font = fontDialog.Font;
        }
    }
}