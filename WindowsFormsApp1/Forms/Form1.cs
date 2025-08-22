using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WindowsFormsApp1.Forms;
using WindowsFormsApp1.Logging;
using WindowsFormsApp1.Models;
using WindowsFormsApp1.Parsers;
using WindowsFormsApp1.Repositories;
using WindowsFormsApp1.Repositories.Interfaces;

namespace WindowsFormsApp1
{
    public partial class Form1 : Form
    {

        private LogHub _log;
        private ProgressForm _progressForm;


        private IToolMapSchemaRepository _toolMapRepo;
        private ISpfRepository _spfRepo;
        private List<CodeList> _parsedCodeLists; // from earlier step

        public Form1()
        {
            InitializeComponent();
        }

        private void btnbrowsePDSComponentXMLFile_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "Select a file";
                openFileDialog.Filter = "All files (*.*)|*.*";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    // Set the file path into the textbox
                    rtbPDSComponentXMLFile.Text = openFileDialog.FileName;
                }
            }
        }

        private void btnBrowseToolMapSchemaXML_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "Select a file";
                openFileDialog.Filter = "All files (*.*)|*.*";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    // Set the file path into the textbox
                    rtbToolMapSchemaXML.Text = openFileDialog.FileName;
                }
            }
        }

        private void btnBrowseStandardNotesPath_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "Select a folder";

                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    rtbStandardNotesPath.Text = folderDialog.SelectedPath;
                }
            }
        }

        private void btnBrowseAllCodeListFilePath_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "Select a file";
                openFileDialog.Filter = "All files (*.*)|*.*";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    // Set the file path into the textbox
                    rtbAllCodeListFilePath.Text = openFileDialog.FileName;
                }
            }
        }

        private void btnProcess_Click(object sender, EventArgs e)
        {
            try
            {
                // 1) Bring up progress window
                _progressForm = new ProgressForm();
                _progressForm.Show(this);

                // 2) Create log hub and attach UI sink
                _log = new LogHub();
                _log.AddSink(_progressForm);

                // 3) Kick off the work on a background task
                Task.Run(() => RunProcessAsync());

            }
            catch (Exception ex)
            {
                MessageBox.Show("Parsing failed:\n" + ex.Message,
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void RunProcessAsync()
        {
            try
            {
                _log.Info("Starting process…");

                var codelistFolder = rtbStandardNotesPath.InvokeRequired
                    ? (string)rtbStandardNotesPath.Invoke(new Func<string>(() => rtbStandardNotesPath.Text.Trim()))
                    : rtbStandardNotesPath.Text.Trim();

                var toolMapPath = rtbToolMapSchemaXML.InvokeRequired
                    ? (string)rtbToolMapSchemaXML.Invoke(new Func<string>(() => rtbToolMapSchemaXML.Text.Trim()))
                    : rtbToolMapSchemaXML.Text.Trim();

                var spfPath = rtbPDSComponentXMLFile.InvokeRequired
                    ? (string)rtbPDSComponentXMLFile.Invoke(new Func<string>(() => rtbPDSComponentXMLFile.Text.Trim()))
                    : rtbPDSComponentXMLFile.Text.Trim();

                _log.Info("Loading ToolMap schema: " + toolMapPath);
                _toolMapRepo = new ToolMapSchemaXmlRepository(toolMapPath);
                _log.Info("ToolMap schema loaded.");

                _log.Info("Loading SPF schema: " + spfPath);
                _spfRepo = new SpfSchemaXmlRepository(spfPath);
                _log.Info("SPF schema loaded.");

                _log.Info("Parsing codelists from: " + codelistFolder);
                _parsedCodeLists = CodeListParser.ParseFolder(
                    codelistFolder,
                    true,
                    _log,
                    _toolMapRepo,                // <-- pass ToolMap repo so creation happens during parse
                    /* toolMapParentElementName: */ null // or "SPMapEnumListDefs" if your XML has a wrapper
                );
                _log.Success("Finished Processing the Standard Note Library.");

                // TODO: next steps (validation, apply) will also call _log.Info/Warn/Error.

                _log.Success("Process complete.");
            }
            catch (Exception ex)
            {
                _log.Error("Unhandled error: " + ex.Message);
            }
        }
    }
}
