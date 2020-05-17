﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Palmmedia.ReportGenerator.Core.Common;
using Palmmedia.ReportGenerator.Core.Logging;
using Palmmedia.ReportGenerator.Core.Parser.Analysis;
using Palmmedia.ReportGenerator.Core.Parser.Filtering;
using Palmmedia.ReportGenerator.Core.Properties;

namespace Palmmedia.ReportGenerator.Core.Parser
{
    /// <summary>
    /// Parser for XML reports generated by NCover.
    /// </summary>
    internal class NCoverParser : ParserBase
    {
        /// <summary>
        /// The Logger.
        /// </summary>
        private static readonly ILogger Logger = LoggerFactory.GetLogger(typeof(NCoverParser));

        /// <summary>
        /// Regex to analyze if a method name belongs to a lamda expression.
        /// </summary>
        private static Regex lambdaMethodNameRegex = new Regex("<.+>.+__.+", RegexOptions.Compiled);

        /// <summary>
        /// Initializes a new instance of the <see cref="NCoverParser" /> class.
        /// </summary>
        /// <param name="assemblyFilter">The assembly filter.</param>
        /// <param name="classFilter">The class filter.</param>
        /// <param name="fileFilter">The file filter.</param>
        internal NCoverParser(IFilter assemblyFilter, IFilter classFilter, IFilter fileFilter)
            : base(assemblyFilter, classFilter, fileFilter)
        {
        }

        /// <summary>
        /// Parses the given XML report.
        /// </summary>
        /// <param name="report">The XML report.</param>
        /// <param name="innerMaxDegreeOfParallism">The max degree of parallism for the class iteration foreach loop</param>
        /// <returns>The parser result.</returns>
        public ParserResult Parse(XContainer report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            var assemblies = new List<Assembly>();

            var modules = report.Descendants("module").ToArray();

            var assemblyNames = modules
                .Select(module => module.Attribute("assembly").Value)
                .Distinct()
                .Where(a => this.AssemblyFilter.IsElementIncludedInReport(a))
                .OrderBy(a => a)
                .ToArray();

            foreach (var assemblyName in assemblyNames)
            {
                assemblies.Add(this.ProcessAssembly(modules, assemblyName));
            }

            var result = new ParserResult(assemblies.OrderBy(a => a.Name).ToList(), false, this.ToString());
            return result;
        }

        /// <summary>
        /// Processes the given assembly.
        /// </summary>
        /// <param name="modules">The modules.</param>
        /// <param name="assemblyName">Name of the assembly.</param>
        /// <param name="innerMaxDegreeOfParallism">The max degree of parallism for the class iteration foreach loop</param>
        /// <returns>The <see cref="Assembly"/>.</returns>
        private Assembly ProcessAssembly(XElement[] modules, string assemblyName)
        {
            Logger.DebugFormat(Resources.CurrentAssembly, assemblyName);

            var classNames = modules
                .Where(module => module.Attribute("assembly").Value.Equals(assemblyName))
                .Elements("method")
                .Where(m => m.Attribute("excluded").Value == "false")
                .Select(method => method.Attribute("class").Value)
                .Where(value => !value.Contains("__") && !value.Contains("+"))
                .Distinct()
                .Where(c => this.ClassFilter.IsElementIncludedInReport(c))
                .OrderBy(name => name)
                .ToArray();

            var assembly = new Assembly(assemblyName);

            Parallel.ForEach(classNames, className => this.ProcessClass(modules, assembly, className));

            return assembly;
        }

        /// <summary>
        /// Processes the given class.
        /// </summary>
        /// <param name="modules">The modules.</param>
        /// <param name="assembly">The assembly.</param>
        /// <param name="className">Name of the class.</param>
        private void ProcessClass(XElement[] modules, Assembly assembly, string className)
        {
            var filesOfClass = modules
                .Where(module => module.Attribute("assembly").Value.Equals(assembly.Name)).Elements("method")
                .Where(method => method.Attribute("class").Value.Equals(className))
                .Where(m => m.Attribute("excluded").Value == "false")
                .Elements("seqpnt")
                .Select(seqpnt => seqpnt.Attribute("document").Value)
                .Distinct()
                .ToArray();

            var filteredFilesOfClass = filesOfClass
                .Where(f => this.FileFilter.IsElementIncludedInReport(f))
                .ToArray();

            // If all files are removed by filters, then the whole class is omitted
            if ((filesOfClass.Length == 0 && !this.FileFilter.HasCustomFilters) || filteredFilesOfClass.Length > 0)
            {
                var @class = new Class(className, assembly);

                foreach (var file in filteredFilesOfClass)
                {
                    @class.AddFile(ProcessFile(modules, @class, file));
                }

                assembly.AddClass(@class);
            }
        }

        /// <summary>
        /// Processes the file.
        /// </summary>
        /// <param name="modules">The modules.</param>
        /// <param name="class">The class.</param>
        /// <param name="filePath">The file path.</param>
        /// <returns>The <see cref="CodeFile"/>.</returns>
        private static CodeFile ProcessFile(XElement[] modules, Class @class, string filePath)
        {
            var methodsOfClass = modules
                .Where(type => type.Attribute("assembly").Value.Equals(@class.Assembly.Name))
                .Elements("method")
                .Where(m => m.Attribute("excluded").Value == "false")
                .Where(method => method.Attribute("class").Value.StartsWith(@class.Name, StringComparison.Ordinal))
                .ToArray();

            var seqpntsOfFile = methodsOfClass.Elements("seqpnt")
                .Where(seqpnt => seqpnt.Attribute("document").Value.Equals(filePath) && seqpnt.Attribute("line").Value != "16707566")
                .Select(seqpnt => new
                {
                    LineNumberStart = int.Parse(seqpnt.Attribute("line").Value, CultureInfo.InvariantCulture),
                    LineNumberEnd = int.Parse(seqpnt.Attribute("endline").Value, CultureInfo.InvariantCulture),
                    Visits = seqpnt.Attribute("visitcount").Value.ParseLargeInteger()
                })
                .OrderBy(seqpnt => seqpnt.LineNumberEnd)
                .ToArray();

            int[] coverage = new int[] { };
            LineVisitStatus[] lineVisitStatus = new LineVisitStatus[] { };

            if (seqpntsOfFile.Length > 0)
            {
                coverage = new int[seqpntsOfFile[seqpntsOfFile.LongLength - 1].LineNumberEnd + 1];
                lineVisitStatus = new LineVisitStatus[seqpntsOfFile[seqpntsOfFile.LongLength - 1].LineNumberEnd + 1];

                for (int i = 0; i < coverage.Length; i++)
                {
                    coverage[i] = -1;
                }

                foreach (var seqpnt in seqpntsOfFile)
                {
                    for (int lineNumber = seqpnt.LineNumberStart; lineNumber <= seqpnt.LineNumberEnd; lineNumber++)
                    {
                        coverage[lineNumber] = coverage[lineNumber] == -1 ? seqpnt.Visits : coverage[lineNumber] + seqpnt.Visits;
                        lineVisitStatus[lineNumber] = lineVisitStatus[lineNumber] == LineVisitStatus.Covered || seqpnt.Visits > 0 ? LineVisitStatus.Covered : LineVisitStatus.NotCovered;
                    }
                }
            }

            var codeFile = new CodeFile(filePath, coverage, lineVisitStatus);

            SetCodeElements(codeFile, methodsOfClass);

            return codeFile;
        }

        /// <summary>
        /// Extracts the methods/properties of the given <see cref="XElement">XElements</see>.
        /// </summary>
        /// <param name="codeFile">The code file.</param>
        /// <param name="methodsOfClass">The methods of the class.</param>
        private static void SetCodeElements(CodeFile codeFile, IEnumerable<XElement> methodsOfClass)
        {
            foreach (var method in methodsOfClass)
            {
                string methodName = method.Attribute("name").Value;

                if (lambdaMethodNameRegex.IsMatch(methodName))
                {
                    continue;
                }

                CodeElementType type = CodeElementType.Method;

                if (methodName.StartsWith("get_", StringComparison.OrdinalIgnoreCase)
                    || methodName.StartsWith("set_", StringComparison.OrdinalIgnoreCase))
                {
                    type = CodeElementType.Property;
                    methodName = methodName.Substring(4);
                }

                var seqpnts = method
                    .Elements("seqpnt")
                    .Where(seqpnt => seqpnt.Attribute("document").Value.Equals(codeFile.Path) && seqpnt.Attribute("line").Value != "16707566")
                    .Select(seqpnt => new
                    {
                        LineNumberStart = int.Parse(seqpnt.Attribute("line").Value, CultureInfo.InvariantCulture),
                        LineNumberEnd = int.Parse(seqpnt.Attribute("endline").Value, CultureInfo.InvariantCulture)
                    })
                    .ToArray();

                if (seqpnts.Length > 0)
                {
                    codeFile.AddCodeElement(new CodeElement(methodName, type, seqpnts.Min(s => s.LineNumberStart), seqpnts.Max(s => s.LineNumberEnd)));
                }
            }
        }
    }
}
