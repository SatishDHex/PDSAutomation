using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace WindowsFormsApp1.Utilities
{
    public sealed class XmlDatabase
    {
        public string FilePath { get; private set; }
        public XDocument Document { get; private set; }
        public XmlNamespaceManager NamespaceManager { get; private set; }

        public XmlDatabase(string filePath, XmlNamespaceManager nsMgr = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("filePath is required", nameof(filePath));
            if (!File.Exists(filePath))
                throw new FileNotFoundException("XML file not found", filePath);

            FilePath = filePath;
            // Load preserving whitespace so we don't disturb formatting unintentionally
            using (var reader = XmlReader.Create(filePath, new XmlReaderSettings { IgnoreComments = false }))
            {
                Document = XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            }
            NamespaceManager = nsMgr;
        }

        public void Save(string targetPath = null)
        {
            var path = string.IsNullOrWhiteSpace(targetPath) ? FilePath : targetPath;
            // Create a writer that keeps indentation tidy
            var settings = new XmlWriterSettings
            {
                Indent = true,
                NewLineOnAttributes = false,
                OmitXmlDeclaration = false
            };
            using (var writer = XmlWriter.Create(path, settings))
            {
                Document.Save(writer);
            }
        }

        public XElement SelectElement(string xpath)
        {
            if (string.IsNullOrWhiteSpace(xpath)) return null;
            return (XElement)Document.XPathSelectElement(xpath, NamespaceManager);
        }

        public IEnumerable<XElement> SelectElements(string xpath)
        {
            if (string.IsNullOrWhiteSpace(xpath)) yield break;
            var nodes = Document.XPathSelectElements(xpath, NamespaceManager);
            foreach (var n in nodes) yield return n;
        }

        public XAttribute GetAttribute(XElement el, string name)
        {
            return el == null ? null : el.Attribute(name);
        }

        public string GetAttributeValue(XElement el, string name)
        {
            var a = GetAttribute(el, name);
            return a == null ? null : a.Value;
        }
    }
}
