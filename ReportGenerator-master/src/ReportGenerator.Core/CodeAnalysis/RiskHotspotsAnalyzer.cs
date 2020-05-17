﻿using System.Collections.Generic;
using System.Linq;
using Palmmedia.ReportGenerator.Core.Parser.Analysis;
using Palmmedia.ReportGenerator.Core.Properties;

namespace Palmmedia.ReportGenerator.Core.CodeAnalysis
{
    /// <summary>
    /// Analyses the method metrics for risk hotspots based on exceeded thresholds.
    /// </summary>
    internal class RiskHotspotsAnalyzer : IRiskHotspotsAnalyzer
    {
        /// <summary>
        /// Indicates whether risk hotspots should be disabled or not.
        /// </summary>
        private readonly bool disabled;

        /// <summary>
        /// The thresholds of the various metrics.
        /// </summary>
        private readonly Dictionary<string, decimal> thresholdsByMetricName;

        /// <summary>
        /// Initializes a new instance of the <see cref="RiskHotspotsAnalyzer"/> class.
        /// </summary>
        /// <param name="riskHotspotsAnalysisThresholds">The metric thresholds.</param>
        /// <param name="disableRiskHotspots">Indicates whether risk hotspots should be disabled or not.</param>
        public RiskHotspotsAnalyzer(RiskHotspotsAnalysisThresholds riskHotspotsAnalysisThresholds, bool disableRiskHotspots)
        {
            this.disabled = disableRiskHotspots;

            this.thresholdsByMetricName = new Dictionary<string, decimal>()
            {
                { ReportResources.CyclomaticComplexity, riskHotspotsAnalysisThresholds.MetricThresholdForCyclomaticComplexity },
                { ReportResources.NPathComplexity, riskHotspotsAnalysisThresholds.MetricThresholdForNPathComplexity },
                { ReportResources.CrapScore, riskHotspotsAnalysisThresholds.MetricThresholdForCrapScore }
            };
        }

        /// <summary>
        /// Performs a risk hotspot analysis on the given assemblies.
        /// </summary>
        /// <param name="assemblies">The assemlies to analyze.</param>
        /// <returns>The risk hotspot analysis result.</returns>
        public RiskHotspotAnalysisResult PerformRiskHotspotAnalysis(IEnumerable<Assembly> assemblies)
        {
            var riskHotspots = new List<RiskHotspot>();

            if (this.disabled)
            {
                return new RiskHotspotAnalysisResult(riskHotspots, false);
            }

            decimal threshold = -1;

            bool codeCodeQualityMetricsAvailable = false;

            foreach (var assembly in assemblies)
            {
                foreach (var clazz in assembly.Classes)
                {
                    int fileIndex = 0;

                    foreach (var file in clazz.Files)
                    {
                        foreach (var methodMetric in file.MethodMetrics)
                        {
                            var codeCodeQualityMetrics = methodMetric.Metrics
                                .Where(m => m.MetricType == MetricType.CodeQuality);

                            codeCodeQualityMetricsAvailable |= codeCodeQualityMetrics.Any();

                            var statusMetrics = codeCodeQualityMetrics
                                .Select(m => new MetricStatus(m, this.thresholdsByMetricName.TryGetValue(m.Name, out threshold) && m.Value > threshold))
                                .ToArray();

                            if (statusMetrics.Any(m => m.Exceeded))
                            {
                                riskHotspots.Add(new RiskHotspot(assembly, clazz, methodMetric, statusMetrics, fileIndex));
                            }
                        }

                        fileIndex++;
                    }
                }
            }

            var result = new RiskHotspotAnalysisResult(
                riskHotspots
                .OrderByDescending(r => r.StatusMetrics.Where(m => m.Exceeded).Max(m => m.Metric.Value))
                .ToList(),
                codeCodeQualityMetricsAvailable);

            return result;
        }
    }
}