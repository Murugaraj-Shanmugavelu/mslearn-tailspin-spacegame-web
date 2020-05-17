using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Palmmedia.ReportGenerator.Core.Logging;
using Palmmedia.ReportGenerator.Core.Parser.Analysis;
using Palmmedia.ReportGenerator.Core.Parser.Filtering;
using Palmmedia.ReportGenerator.Core.Properties;

namespace Palmmedia.ReportGenerator.Core.Parser
{
    /// <summary>
    /// Parser for XML reports generated by Visual Studio.
    /// </summary>
    internal class VisualStudioParser : ParserBase
    {
        /// <summary>
        /// The Logger.
        /// </summary>
        private static readonly ILogger Logger = LoggerFactory.GetLogger(typeof(VisualStudioParser));

        /// <summary>
        /// Regex to analyze if a method name belongs to a lamda expression.
        /// </summary>
        private static Regex lambdaMethodNameRegex = new Regex("<.+>.+__", RegexOptions.Compiled);

        /// <summary>
        /// Regex to analyze if a method name is generated by compiler.
        /// </summary>
        private static Regex compilerGeneratedMethodNameRegex = new Regex(@"^.*<(?<CompilerGeneratedName>.+)>.+__.+!MoveNext\(\)!.+$", RegexOptions.Compiled);

        /// <summary>
        /// Regex to extract short method name.
        /// </summary>
        private static Regex methodRegex = new Regex(@"^(?<MethodName>.+)\((?<Arguments>.*)\).*$", RegexOptions.Compiled);

        /// <summary>
        /// Initializes a new instance of the <see cref="VisualStudioParser" /> class.
        /// </summary>
        /// <param name="assemblyFilter">The assembly filter.</param>
        /// <param name="classFilter">The class filter.</param>
        /// <param name="fileFilter">The file filter.</param>
        internal VisualStudioParser(IFilter assemblyFilter, IFilter classFilter, IFilter fileFilter)
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

            var modules = report.Descendants("Module").ToArray();
            var files = report.Descendants("SourceFileNames").ToArray();

            var assemblyNames = modules
                .Select(m => m.Element("ModuleName").Value)
                .Distinct()
                .Where(a => this.AssemblyFilter.IsElementIncludedInReport(a))
                .OrderBy(a => a)
                .ToArray();

            foreach (var assemblyName in assemblyNames)
            {
                assemblies.Add(this.ProcessAssembly(modules, files, assemblyName));
            }

            var result = new ParserResult(assemblies.OrderBy(a => a.Name).ToList(), false, this.ToString());
            return result;
        }

        /// <summary>
        /// Processes the given assembly.
        /// </summary>
        /// <param name="modules">The modules.</param>
        /// <param name="files">The files.</param>
        /// <param name="assemblyName">Name of the assembly.</param>
        /// <param name="innerMaxDegreeOfParallism">The max degree of parallism for the class iteration foreach loop</param>
        /// <returns>The <see cref="Assembly"/>.</returns>
        private Assembly ProcessAssembly(XElement[] modules, XElement[] files, string assemblyName)
        {
            Logger.DebugFormat(Resources.CurrentAssembly, assemblyName);

            var classNames = modules
                .Where(m => m.Element("ModuleName").Value.Equals(assemblyName))
                .Elements("NamespaceTable")
                .Elements("Class")
                .Elements("ClassName")
                .Where(c => !c.Value.Contains("<>")
                    && !c.Value.StartsWith("$", StringComparison.OrdinalIgnoreCase))
                .Select(c =>
                {
                    string fullname = c.Value;
                    int nestedClassSeparatorIndex = fullname.IndexOf('.');
                    fullname = nestedClassSeparatorIndex > -1 ? fullname.Substring(0, nestedClassSeparatorIndex) : fullname;
                    return c.Parent.Parent.Element("NamespaceName").Value + "." + fullname;
                })
                .Distinct()
                .Where(c => this.ClassFilter.IsElementIncludedInReport(c))
                .OrderBy(name => name)
                .ToArray();

            var assembly = new Assembly(assemblyName);

            Parallel.ForEach(classNames, className => this.ProcessClass(modules, files, assembly, className));

            return assembly;
        }

        /// <summary>
        /// Processes the given class.
        /// </summary>
        /// <param name="modules">The modules.</param>
        /// <param name="files">The files.</param>
        /// <param name="assembly">The assembly.</param>
        /// <param name="className">Name of the class.</param>
        private void ProcessClass(XElement[] modules, XElement[] files, Assembly assembly, string className)
        {
            var fileIdsOfClass = modules
                .Where(m => m.Element("ModuleName").Value.Equals(assembly.Name))
                .Elements("NamespaceTable")
                .Elements("Class")
                .Where(c => (c.Parent.Element("NamespaceName").Value + "." + c.Element("ClassName").Value).Equals(className, StringComparison.Ordinal)
                            || (c.Parent.Element("NamespaceName").Value + "." + c.Element("ClassName").Value).StartsWith(className + ".", StringComparison.Ordinal))
                .Elements("Method")
                .Elements("Lines")
                .Elements("SourceFileID")
                .Select(m => m.Value)
                .Distinct()
                .ToArray();

            var filteredFilesOfClass = fileIdsOfClass
                .Select(fileId =>
                    new
                    {
                        FileId = fileId,
                        FilePath = files.First(f => f.Element("SourceFileID").Value == fileId).Element("SourceFileName").Value
                    })
                .Where(f => this.FileFilter.IsElementIncludedInReport(f.FilePath))
                .ToArray();

            // If all files are removed by filters, then the whole class is omitted
            if ((fileIdsOfClass.Length == 0 && !this.FileFilter.HasCustomFilters) || filteredFilesOfClass.Length > 0)
            {
                var @class = new Class(className, assembly);

                foreach (var file in filteredFilesOfClass)
                {
                    @class.AddFile(ProcessFile(modules, file.FileId, @class, file.FilePath));
                }

                assembly.AddClass(@class);
            }
        }

        /// <summary>
        /// Processes the file.
        /// </summary>
        /// <param name="modules">The modules.</param>
        /// <param name="fileId">The file id.</param>
        /// <param name="class">The class.</param>
        /// <param name="filePath">The file path.</param>
        /// <returns>The <see cref="CodeFile"/>.</returns>
        private static CodeFile ProcessFile(XElement[] modules, string fileId, Class @class, string filePath)
        {
            var methods = modules
                .Where(m => m.Element("ModuleName").Value.Equals(@class.Assembly.Name))
                .Elements("NamespaceTable")
                .Elements("Class")
                .Where(c => (c.Parent.Element("NamespaceName").Value + "." + c.Element("ClassName").Value).Equals(@class.Name, StringComparison.Ordinal)
                            || (c.Parent.Element("NamespaceName").Value + "." + c.Element("ClassName").Value).StartsWith(@class.Name + ".", StringComparison.Ordinal))
                .Elements("Method")
                .Where(m => m.Elements("Lines").Elements("SourceFileID").Any(s => s.Value == fileId))
                .ToArray();

            var linesOfFile = methods
                .Elements("Lines")
                .Select(l => new
                {
                    LineNumberStart = int.Parse(l.Element("LnStart").Value, CultureInfo.InvariantCulture),
                    LineNumberEnd = int.Parse(l.Element("LnEnd").Value, CultureInfo.InvariantCulture),
                    Coverage = int.Parse(l.Element("Coverage").Value, CultureInfo.InvariantCulture)
                })
                .OrderBy(seqpnt => seqpnt.LineNumberEnd)
                .ToArray();

            int[] coverage = new int[] { };
            LineVisitStatus[] lineVisitStatus = new LineVisitStatus[] { };

            if (linesOfFile.Length > 0)
            {
                coverage = new int[linesOfFile[linesOfFile.LongLength - 1].LineNumberEnd + 1];
                lineVisitStatus = new LineVisitStatus[linesOfFile[linesOfFile.LongLength - 1].LineNumberEnd + 1];

                for (int i = 0; i < coverage.Length; i++)
                {
                    coverage[i] = -1;
                }

                foreach (var seqpnt in linesOfFile)
                {
                    for (int lineNumber = seqpnt.LineNumberStart; lineNumber <= seqpnt.LineNumberEnd; lineNumber++)
                    {
                        int visits = seqpnt.Coverage < 2 ? 1 : 0;
                        coverage[lineNumber] = coverage[lineNumber] == -1 ? visits : Math.Min(coverage[lineNumber] + visits, 1);
                        lineVisitStatus[lineNumber] = lineVisitStatus[lineNumber] == LineVisitStatus.Covered || visits > 0 ? LineVisitStatus.Covered : LineVisitStatus.NotCovered;
                    }
                }
            }

            var codeFile = new CodeFile(filePath, coverage, lineVisitStatus);

            SetMethodMetrics(codeFile, methods);
            SetCodeElements(codeFile, methods);

            return codeFile;
        }

        /// <summary>
        /// Extracts the metrics from the given <see cref="XElement">XElements</see>.
        /// </summary>
        /// <param name="codeFile">The code file.</param>
        /// <param name="methodsOfFile">The methods of the file.</param>
        private static void SetMethodMetrics(CodeFile codeFile, IEnumerable<XElement> methodsOfFile)
        {
            foreach (var method in methodsOfFile)
            {
                string fullName = method.Element("MethodName").Value;

                // Exclude properties and lambda expressions
                if (fullName.StartsWith("get_", StringComparison.Ordinal)
                    || fullName.StartsWith("set_", StringComparison.Ordinal)
                    || lambdaMethodNameRegex.IsMatch(fullName))
                {
                    continue;
                }

                fullName = ExtractMethodName(fullName, method.Element("MethodKeyName").Value);
                string shortName = methodRegex.Replace(fullName, m => string.Format(CultureInfo.InvariantCulture, "{0}({1})", m.Groups["MethodName"].Value, m.Groups["Arguments"].Value.Length > 0 ? "..." : string.Empty));

                var metrics = new[]
                {
                    new Metric(
                        ReportResources.BlocksCovered,
                        ParserBase.CodeCoverageUri,
                        MetricType.CoverageAbsolute,
                        int.Parse(method.Element("BlocksCovered").Value, CultureInfo.InvariantCulture)),
                    new Metric(
                        ReportResources.BlocksNotCovered,
                        ParserBase.CodeCoverageUri,
                        MetricType.CoverageAbsolute,
                        int.Parse(method.Element("BlocksNotCovered").Value, CultureInfo.InvariantCulture),
                        MetricMergeOrder.LowerIsBetter)
                };

                var methodMetric = new MethodMetric(fullName, shortName, metrics);

                var seqpnt = method
                    .Elements("Lines")
                    .Elements("LnStart")
                    .FirstOrDefault();

                if (seqpnt != null)
                {
                    methodMetric.Line = int.Parse(seqpnt.Value, CultureInfo.InvariantCulture);
                }

                codeFile.AddMethodMetric(methodMetric);
            }
        }

        /// <summary>
        /// Extracts the methods/properties of the given <see cref="XElement">XElements</see>.
        /// </summary>
        /// <param name="codeFile">The code file.</param>
        /// <param name="methodsOfFile">The methods of the file.</param>
        private static void SetCodeElements(CodeFile codeFile, IEnumerable<XElement> methodsOfFile)
        {
            foreach (var method in methodsOfFile)
            {
                if (lambdaMethodNameRegex.IsMatch(method.Element("MethodName").Value))
                {
                    continue;
                }

                string methodName = ExtractMethodName(method.Element("MethodName").Value, method.Element("MethodKeyName").Value);

                CodeElementType type = CodeElementType.Method;

                if (methodName.StartsWith("get_", StringComparison.OrdinalIgnoreCase)
                    || methodName.StartsWith("set_", StringComparison.OrdinalIgnoreCase))
                {
                    type = CodeElementType.Property;
                    methodName = methodName.Substring(4);
                }

                var seqpnts = method
                    .Elements("Lines")
                    .Select(l => new
                    {
                        LineNumberStart = int.Parse(l.Element("LnStart").Value, CultureInfo.InvariantCulture),
                        LineNumberEnd = int.Parse(l.Element("LnEnd").Value, CultureInfo.InvariantCulture)
                    })
                    .ToArray();

                if (seqpnts.Length > 0)
                {
                    codeFile.AddCodeElement(new CodeElement(methodName, type, seqpnts.Min(s => s.LineNumberStart), seqpnts.Max(s => s.LineNumberEnd)));
                }
            }
        }

        /// <summary>
        /// Extracts the method name. For async methods the original name is returned.
        /// </summary>
        /// <param name="methodName">The full method name.</param>
        /// <param name="methodKeyName">The method key name.</param>
        /// <returns>The method name.</returns>
        private static string ExtractMethodName(string methodName, string methodKeyName)
        {
            // Quick check before expensive regex is called
            if (methodKeyName.Contains("MoveNext()"))
            {
                Match match = compilerGeneratedMethodNameRegex.Match(methodKeyName);

                if (match.Success)
                {
                    methodName = match.Groups["CompilerGeneratedName"].Value + "()";
                }
            }

            return methodName;
        }
    }
}
