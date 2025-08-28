using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
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
using WindowsFormsApp1.Utilities;

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

                // Load the toolmap schema into memory
                _log.Info("Loading ToolMap schema: " + toolMapPath);
                _toolMapRepo = new ToolMapSchemaXmlRepository(toolMapPath);
                _log.Info("ToolMap schema loaded.");

                //Load the PDSComponent data into memory
                _log.Info("Loading SPF schema: " + spfPath);
                _spfRepo = new SpfSchemaXmlRepository(spfPath);
                _log.Info("SPF schema loaded.");

                //Read sheet names from INI
                string directoryPath = Path.GetDirectoryName(rtbAllCodeListFilePath.Text.Trim());
                string txtIniPath = Path.Combine(directoryPath, "PDSConfig.ini");


                var enumToSheet = IniEnumToSheetReader.ReadMap(txtIniPath);

                //Derive the sheet list for the Excel hierarchy parser
                var sheets = IniEnumToSheetReader.GetDistinctSheets(enumToSheet);

                //Build hierarchies just for those sheets
                var hierarchies = ExcelHierarchyParser.BuildHierarchiesMultiLevel(
                    rtbAllCodeListFilePath.Text.Trim(),
                    sheets,
                    _log
                );

                // Scan the Toolmapschema for Enumlists and their SPF side relations
                _log.Info("Scanning ToolMap EnumLists, verifying relations, and cross-checking SPF…");
                var scanResults = ToolMapEnumListScanner.ScanEnumListsAndRelations(
                    _toolMapRepo,
                    _spfRepo,
                    _log
                );
                _log.Success(
                    $"Scan done. Total: {scanResults.Count}, " +
                    $"relations present: {scanResults.Count(r => r.RelationExists)}, " +
                    $"SPF matches: {scanResults.Count(r => r.SpfEnumListExists)}."
                );

                // parse the standard note library and store it to validate EnumEnums. 
                _log.Info("Parsing codelists from: " + codelistFolder);
                _parsedCodeLists = CodeListParser.ParseFolder(
                    codelistFolder,
                    enumToSheet,
                    hierarchies,
                    true,
                    _log,
                    _toolMapRepo, 
                    _spfRepo,
                    /* toolMapParentElementName: */ null
                );
                _log.Success("Finished Processing the Standard Note Library.");



                var tmTarget = VersionedFile.GetNextVersionPath(_toolMapRepo.FilePath);
                _toolMapRepo.Save(tmTarget);
                _log.Success("ToolMap schema saved as: " + tmTarget);


                var spfTarget = VersionedFile.GetNextVersionPath(_spfRepo.FilePath);
                _spfRepo.Save(spfTarget);
                _log.Success("Updated PDSComponentFile saved as: " + spfTarget);

                _log.Success("Process complete.");
            }
            catch (Exception ex)
            {
                _log.Error("Unhandled error: " + ex.Message);
            }
        }
    }
}
