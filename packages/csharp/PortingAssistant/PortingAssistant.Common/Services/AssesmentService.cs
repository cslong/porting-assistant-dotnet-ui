﻿using System;
using System.Collections.Generic;
using System.Linq;
using PortingAssistant.Common.Listener;
using PortingAssistant.Common.Model;
using Microsoft.Extensions.Logging;
using PortingAssistant.Client.Client;
using PortingAssistant.Client.Model;
using PortingAssistant.Common.Utils;
using Newtonsoft.Json;

namespace PortingAssistant.Common.Services
{
    public class AssessmentService : IAssessmentService
    {
        private readonly ILogger _logger;
        private readonly IPortingAssistantClient _client;
        private readonly List<OnApiAnalysisUpdate> _apiAnalysisListeners;
        private readonly List<OnNugetPackageUpdate> _nugetPackageListeners;

        public AssessmentService(ILogger<AssessmentService> logger,
            IPortingAssistantClient client)
        {
            _logger = logger;
            _client = client;
            _apiAnalysisListeners = new List<OnApiAnalysisUpdate>();
            _nugetPackageListeners = new List<OnNugetPackageUpdate>();
        }

        public Response<SolutionDetails, string> AnalyzeSolution(AnalyzeSolutionRequest request)
        {
            try
            {
                var startTime = DateTime.Now;
                string tgtFramework = request.settings.TargetFramework;

                var solutionAnalysisResult = _client.AnalyzeSolutionAsync(request.solutionFilePath, request.settings);
                solutionAnalysisResult.Wait();

                var preProjectTriggerDataDictionary = new Dictionary<string, PreTriggerData>();
                if (request.preTriggerData != null && request.preTriggerData.Length > 0)
                {
                    Array.ForEach(request.preTriggerData, prop => {
                        var proj = JsonConvert.DeserializeObject<PreTriggerData>(prop);
                        if (!preProjectTriggerDataDictionary.ContainsKey(proj.projectName))
                        {
                            preProjectTriggerDataDictionary.Add(proj.projectName, proj);
                        }
                    });
                }

                if (solutionAnalysisResult.IsCompletedSuccessfully)
                {
                    TelemetryCollectionUtils.CollectSolutionMetrics(solutionAnalysisResult.Result, request, startTime, tgtFramework);
                    solutionAnalysisResult.Result.ProjectAnalysisResults.ForEach(projectAnalysisResult =>
                    {
                        if (projectAnalysisResult == null)
                        {
                            return;
                        }
                        var preTriggerProjectData = preProjectTriggerDataDictionary.ContainsKey(projectAnalysisResult.ProjectName) ?
                            preProjectTriggerDataDictionary[projectAnalysisResult.ProjectName] : null;
                        TelemetryCollectionUtils.CollectProjectMetrics(projectAnalysisResult, request, tgtFramework, preTriggerProjectData);

                        projectAnalysisResult.PackageAnalysisResults.ToList()
                        .ForEach(p =>
                        {
                            p.Value.ContinueWith(result =>
                            {
                                if (result.IsCompletedSuccessfully)
                                {
                                    TelemetryCollectionUtils.CollectNugetMetrics(result, request, tgtFramework);

                                    _nugetPackageListeners.ForEach(l => l.Invoke(new Response<PackageAnalysisResult, PackageVersionPair>
                                    {
                                        Value = result.Result,
                                        Status = Response<PackageAnalysisResult, PackageVersionPair>.Success()
                                    }));
                                    return;
                                }

                                _nugetPackageListeners.ForEach(l => l.Invoke(new Response<PackageAnalysisResult, PackageVersionPair>
                                {
                                    ErrorValue = p.Key,
                                    Status = Response<PackageAnalysisResult, PackageVersionPair>.Failed(result.Exception)
                                }));
                            });
                        });

                        if (projectAnalysisResult.SourceFileAnalysisResults != null &&
                            projectAnalysisResult.ProjectGuid != null &&
                            projectAnalysisResult.ProjectFilePath != null) {
                            var selectedApis = projectAnalysisResult.SourceFileAnalysisResults.SelectMany(s => s.ApiAnalysisResults);
                            var allActions = projectAnalysisResult.SourceFileAnalysisResults.SelectMany(a => a.RecommendedActions);
                            allActions.ToList().ForEach(action => {
                                    var selectedApi = selectedApis.FirstOrDefault(s => s.CodeEntityDetails.TextSpan.Equals(action.TextSpan));
                                    selectedApi?.Recommendations?.RecommendedActions?.Add(action);
                                });
                            TelemetryCollectionUtils.FileAssessmentCollect(selectedApis, request);
                            }

                        if (projectAnalysisResult.IsBuildFailed)
                        {
                            _apiAnalysisListeners.ForEach(listener =>
                                listener.Invoke(new Response<ProjectApiAnalysisResult, SolutionProject>
                                {
                                    ErrorValue = new SolutionProject
                                    {
                                        ProjectPath = projectAnalysisResult.ProjectFilePath,
                                        SolutionPath = request.solutionFilePath
                                    },
                                    Status = Response<ProjectApiAnalysisResult, SolutionProject>
                                    .Failed(new PortingAssistantClientException($"Errors during compilation in {projectAnalysisResult.ProjectName}.", null))
                                }));

                            return;
                        }

                        _apiAnalysisListeners.ForEach(listener =>
                        {
                            listener.Invoke(new Response<ProjectApiAnalysisResult, SolutionProject>
                            {
                                Value = new ProjectApiAnalysisResult
                                {
                                    Errors = projectAnalysisResult.Errors,
                                    SolutionFile = request.solutionFilePath,
                                    ProjectFile = projectAnalysisResult.ProjectFilePath,
                                    ProjectGuid = projectAnalysisResult.ProjectGuid,
                                    SourceFileAnalysisResults = projectAnalysisResult.SourceFileAnalysisResults
                                },
                                Status = Response<ProjectApiAnalysisResult, SolutionProject>.Success()
                            });
                        });

                        return;
                    });

                    solutionAnalysisResult.Result.FailedProjects.ForEach(projectFilePath =>
                    {
                        _apiAnalysisListeners.ForEach(listener =>
                            listener.Invoke(new Response<ProjectApiAnalysisResult, SolutionProject>
                            {
                                ErrorValue = new SolutionProject
                                {
                                    ProjectPath = projectFilePath,
                                    SolutionPath = request.solutionFilePath
                                },
                                Status = Response<ProjectApiAnalysisResult, SolutionProject>
                                .Failed(new PortingAssistantClientException($"Errors during compilation in {projectFilePath}.", null))
                            }));
                    });

                    return new Response<SolutionDetails, string>
                    {
                        Value = solutionAnalysisResult.Result.SolutionDetails,
                        Status = Response<SolutionDetails, string>.Success()
                    };
                }
                else
                {
                    throw new PortingAssistantClientException($"anaylze solution {request.solutionFilePath} failed", solutionAnalysisResult.Exception);
                }
            }
            catch (Exception ex)
            {
                return new Response<SolutionDetails, string>
                {
                    ErrorValue = request.solutionFilePath,
                    Status = Response<SolutionDetails, string>.Failed(ex)
                };

            }
        }

        public void AddApiAnalysisListener(OnApiAnalysisUpdate listener)
        {
            _apiAnalysisListeners.Add(listener);
        }

        public void AddNugetPackageListener(OnNugetPackageUpdate listener)
        {
            _nugetPackageListeners.Add(listener);
        }
    }
}

