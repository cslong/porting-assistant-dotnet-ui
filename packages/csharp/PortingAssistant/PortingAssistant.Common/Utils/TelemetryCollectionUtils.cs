using PortingAssistant.Client.Common.Utils;
using PortingAssistant.Client.Model;
using PortingAssistant.Common.Model;
using PortingAssistant.Telemetry.Model;
using PortingAssistant.Telemetry.Utils;
using PortingAssistantExtensionTelemetry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PortingAssistant.Common.Utils
{
    public static class TelemetryCollectionUtils
    {
        public static void CollectSolutionMetrics(SolutionAnalysisResult solutionAnalysisResult, AnalyzeSolutionRequest request, DateTime startTime, string tgtFramework)
        {
            var solutionMetrics = createSolutionMetric(solutionAnalysisResult, request.runId, request.triggerType, tgtFramework, startTime);
            TelemetryCollector.Collect<SolutionMetrics>(solutionMetrics);
        }

        public static void CollectProjectMetrics(ProjectAnalysisResult projectAnalysisResult, AnalyzeSolutionRequest request, string tgtFramework, PreTriggerData preTriggerData = null)
        {
            var projectMetrics = createProjectMetric(request.runId, request.triggerType, tgtFramework, projectAnalysisResult, preTriggerData);
            TelemetryCollector.Collect<ProjectMetrics>(projectMetrics);
        }

        public static void CollectNugetMetrics(Task<PackageAnalysisResult> packageAnalysisResult, AnalyzeSolutionRequest request, string tgtFramework)
        {
            var nugetMetrics = createNugetMetric(request.runId, request.triggerType, tgtFramework, packageAnalysisResult);
            TelemetryCollector.Collect<NugetMetrics>(nugetMetrics);
        }

        public static void FileAssessmentCollect(IEnumerable<ApiAnalysisResult> selectedApis, AnalyzeSolutionRequest request)
        {
            var date = DateTime.Now;
            var apiMetrics = selectedApis.GroupBy(elem => new
            {
                elem.CodeEntityDetails.Name,
                elem.CodeEntityDetails.Namespace,
                elem.CodeEntityDetails.OriginalDefinition,
                elem.CodeEntityDetails.Package?.PackageId,
                elem.CodeEntityDetails.Signature
            }).Select(
                group => createAPIMetric(request.runId, request.triggerType, request.settings.TargetFramework, date, group.First(), group.Count())
                );

            apiMetrics.ToList().ForEach(metric => TelemetryCollector.Collect(metric));
        }

        public static SolutionMetrics createSolutionMetric(SolutionAnalysisResult solutionAnalysisResult, string runId, string triggerType, string tgtFramework, DateTime startTime)
        {
            return new SolutionMetrics
            {
                MetricsType = MetricsType.Solution,
                RunId = runId,
                TriggerType = triggerType,
                TargetFramework = tgtFramework,
                TimeStamp = DateTime.Now.ToString("MM/dd/yyyy HH:mm"),
                SolutionPath = CryptoUtil.HashString(solutionAnalysisResult.SolutionDetails.SolutionFilePath),
                ApplicationGuid = solutionAnalysisResult.SolutionDetails.ApplicationGuid,
                SolutionGuid = solutionAnalysisResult.SolutionDetails.SolutionGuid,
                RepositoryUrl = solutionAnalysisResult.SolutionDetails.RepositoryUrl,
                AnalysisTime = DateTime.Now.Subtract(startTime).TotalMilliseconds,
                PortingAssistantVersion = MetricsBase.Version,
                UsingDefaultCreds = MetricsBase.UsingDefault,
            };
        }
        public static ProjectMetrics createProjectMetric(string runId, string triggerType, string tgtFramework, ProjectAnalysisResult projectAnalysisResult, PreTriggerData preTriggerData = null)
        {
            var preCompatibilityResult = (preTriggerData == null || preTriggerData?.sourceFileAnalysisResults == null || preTriggerData.sourceFileAnalysisResults.Length == 0)
                ? null : AnalysisUtils.GenerateCompatibilityResults(preTriggerData?.sourceFileAnalysisResults?.ToList(),
                    preTriggerData?.projectPath, preTriggerData?.ported ?? false);
            var compatabilityResult = projectAnalysisResult.ProjectCompatibilityResult;
            compatabilityResult.ProjectPath = CryptoUtil.HashString(compatabilityResult.ProjectPath);
            return new ProjectMetrics
            {
                MetricsType = MetricsType.Project,
                RunId = runId,
                TriggerType = triggerType,
                TargetFramework = tgtFramework,
                SourceFrameworks = projectAnalysisResult.TargetFrameworks,
                TimeStamp = DateTime.Now.ToString("MM/dd/yyyy HH:mm"),
                ProjectGuid = CryptoUtil.HashString(projectAnalysisResult.ProjectGuid),
                ProjectType = projectAnalysisResult.ProjectType,
                NumNugets = projectAnalysisResult.PackageReferences.Count,
                NumReferences = projectAnalysisResult.ProjectReferences.Count,
                IsBuildFailed = projectAnalysisResult.IsBuildFailed,
                CompatibilityResult = compatabilityResult,
                PreCompatibilityResult = preCompatibilityResult,
                PortingAssistantVersion = MetricsBase.Version,
                PreApiInCompatibilityCount = preTriggerData?.incompatibleApis,
                PostApiInCompatibilityCount = GetIncompatibleApiCount(projectAnalysisResult),
                PreFramework = preTriggerData?.targetFramework,
                PreBuildErrorCount = preTriggerData?.buildErrors,
                PostBuildErrorCount = projectAnalysisResult.Errors.Count,
                ProjectLanguage = GetProjectLanguage(projectAnalysisResult.ProjectFilePath)
            };
        }

        public static int GetIncompatibleApiCount(ProjectAnalysisResult projectAnalysisResult) {
            var dictionary = new Dictionary<string, bool>();
            var sourceFileAnalysisResults = projectAnalysisResult.SourceFileAnalysisResults;
            sourceFileAnalysisResults.ForEach(SourceFileAnalysisResult =>
            {
                SourceFileAnalysisResult.ApiAnalysisResults.ForEach(apiAnalysisResult =>
                {
                    var compatibility = apiAnalysisResult.CompatibilityResults?.FirstOrDefault().Value?.Compatibility;
                    bool isCompatible = (compatibility == Compatibility.UNKNOWN || compatibility == Compatibility.COMPATIBLE);
                    var key = apiAnalysisResult.CodeEntityDetails?.OriginalDefinition + "-" +
                        apiAnalysisResult.CodeEntityDetails?.Package?.PackageId?.ToLower() + "-" +
                        apiAnalysisResult.CodeEntityDetails?.Package?.Version?.ToLower();
                    if (!dictionary.ContainsKey(key))
                    {
                        dictionary.Add(key, isCompatible);
                    }
                });
            });
            return dictionary.Count - dictionary.Count(c => c.Value);
        }

        public static NugetMetrics createNugetMetric(string runId, string triggerType, string tgtFramework, Task<PackageAnalysisResult> result)
        { 
            return new NugetMetrics
            {
                MetricsType = MetricsType.Nuget,
                RunId = runId,
                TriggerType = triggerType,
                TargetFramework = tgtFramework,
                TimeStamp = DateTime.Now.ToString("MM/dd/yyyy HH:mm"),
                PackageName = result.Result.PackageVersionPair.PackageId,
                PackageVersion = result.Result.PackageVersionPair.Version,
                Compatibility = result.Result.CompatibilityResults[tgtFramework].Compatibility,
                PortingAssistantVersion = MetricsBase.Version
            };
        }

        public static APIMetrics createAPIMetric(string runId, string triggerType, string tgtFramework, DateTime date, ApiAnalysisResult api, int count)
        {
            return new APIMetrics
            {
                MetricsType = MetricsType.API,
                RunId = runId,
                TriggerType = triggerType,
                TargetFramework = tgtFramework,
                TimeStamp = date.ToString("MM/dd/yyyy HH:mm"),
                Name = api.CodeEntityDetails.Name,
                NameSpace = api.CodeEntityDetails.Namespace,
                OriginalDefinition = api.CodeEntityDetails.OriginalDefinition,
                Compatibility = api.CompatibilityResults[tgtFramework].Compatibility,
                PackageId = api.CodeEntityDetails.Package?.PackageId,
                PackageVersion = api.CodeEntityDetails.Package?.Version,
                ApiType = api.CodeEntityDetails.CodeEntityType.ToString(),
                HasActions = api.Recommendations.RecommendedActions.Any(action => action.RecommendedActionType != RecommendedActionType.NoRecommendation),
                ApiCounts = count,
                PortingAssistantVersion = MetricsBase.Version
            };
        }

        private static string GetProjectLanguage(string projectFilePath)
        {
            if (projectFilePath.EndsWith(".csproj"))
            {
                return "csharp";
            }
            if (projectFilePath.EndsWith(".vbproj"))
            {
                return "visualbasic";
            }
            return "invalid";
        }
    }
}