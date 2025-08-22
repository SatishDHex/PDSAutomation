using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WindowsFormsApp1.Logging.Interfaces;

namespace WindowsFormsApp1.Logging
{
    public sealed class FileLogSink : ILogSink, IDisposable
    {
        private readonly StreamWriter _writer;

        public FileLogSink(string filePath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
            _writer = new StreamWriter(new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true
            };
        }

        public void Write(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            _writer.WriteLine(line);
        }

        public void Dispose()
        {
            _writer?.Dispose();
        }
    }
}
