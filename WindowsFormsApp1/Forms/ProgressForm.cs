using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WindowsFormsApp1.Logging.Interfaces;

namespace WindowsFormsApp1.Forms
{
    public partial class ProgressForm : Form, ILogSink
    {
        public ProgressForm()
        {
            InitializeComponent();
        }

        // ILogSink implementation — safe for cross-thread calls
        public void Write(string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)(() => Append(message))); } catch { }
            }
            else
            {
                Append(message);
            }
        }

        private void Append(string message)
        {
            Color color = Color.Magenta; // default Info

            if (message.StartsWith("WARN:")) color = Color.Goldenrod;
            else if (message.StartsWith("ERROR:")) color = Color.Red;
            else if (message.StartsWith("OK:")) color = Color.Green;

            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.SelectionLength = 0;
            txtLog.SelectionColor = color;
            txtLog.AppendText(message + Environment.NewLine);
            txtLog.SelectionColor = txtLog.ForeColor; // reset
            txtLog.ScrollToCaret();
        }
    }
}
