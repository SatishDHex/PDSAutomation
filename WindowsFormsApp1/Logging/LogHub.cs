using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WindowsFormsApp1.Logging.Interfaces;

namespace WindowsFormsApp1.Logging
{
    public sealed class LogHub : ILogger
    {
        private readonly List<ILogSink> _sinks = new List<ILogSink>();

        public void AddSink(ILogSink sink)
        {
            if (sink != null) _sinks.Add(sink);
        }

        public void RemoveSink(ILogSink sink)
        {
            _sinks.Remove(sink);
        }

        public void Info(string message) { Write("INFO: " + message); }
        public void Warn(string message) { Write("WARN: " + message); }
        public void Error(string message) { Write("ERROR: " + message); }
        public void Success(string message) { Write("OK: " + message); }

        private void Write(string message)
        {
            // Fan out to all sinks
            foreach (var s in _sinks) s.Write(message);
        }
    }
}
