using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsFormsApp1.Logging.Interfaces
{
    public interface ILogSink
    {
        void Write(string message);
    }
}
