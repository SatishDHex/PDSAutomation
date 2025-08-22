using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace WindowsFormsApp1.Repositories.Interfaces
{
    /// <summary>
    /// Abstracts SPF (pdscomponent.xml) access.
    /// </summary>
    public interface ISpfRepository
    {
        XDocument Document { get; }
        string FilePath { get; }

        // Generic helpers
        XElement SelectOne(string xpath);
        IEnumerable<XElement> SelectMany(string xpath);

        // Save
        void Save(string targetPath = null);
    }
}
