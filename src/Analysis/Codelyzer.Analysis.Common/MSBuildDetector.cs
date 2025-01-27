﻿using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Setup.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Codelyzer.Analysis.Common
{
    /// <summary>
    /// Detects the MSBuild Path that best matches the solution
    /// </summary>
    public class MSBuildDetector
    {
        private List<VisualStudioInstanceData> _visualStudioInstances;

        private const string SolutionFileVSVersion = "VisualStudioVersion";

        public class VisualStudioInstanceData
        {
            public VisualStudioInstanceData()
            {
                Workloads = new List<string>();
            }
            public string Id { get; set; }
            public string Name { get; set; }
            public string Version { get; set; }
            public string InstallationPath { get; set; }
            public List<string> Workloads { get; set; }
            public string VisualStudioMSBuildPath { get; set; }
        }

        public MSBuildDetector()
        {
            _visualStudioInstances = GetVisualStudioInstallations();
        }

        public List<VisualStudioInstanceData> GetVisualStudioInstanceData() => _visualStudioInstances;

        public string MatchSolution(string solutionPath)
        {
            var version = GetVisualStudioVersionNumber(solutionPath);

            var solutionMajorVersion = GetMajorVersion(version);

            var firstInstanceWithMatchingVersion = _visualStudioInstances?.OrderByDescending(v => v.Version).FirstOrDefault(vr => solutionMajorVersion == GetMajorVersion(vr.Version));

            //If there's a visual studio solution that matches the one used to create the solution:
            if (firstInstanceWithMatchingVersion != null)
            {
                return firstInstanceWithMatchingVersion.VisualStudioMSBuildPath;
            }
            //If no version matches, return the max version
            else
            {
                return _visualStudioInstances?.OrderByDescending(v => v.Version).FirstOrDefault().VisualStudioMSBuildPath;
            }
        }

        private int GetMajorVersion(string version)
        {
            int index = version.IndexOf('.');
            if(index < 0)
            {
                index = version.Length;
            }
            return int.Parse(version.Substring(0, index));
        }

        private string GetVisualStudioVersionNumber(string solutionPath)
        {
            var solutionFileText = File.ReadAllText(solutionPath);
            var lines = solutionFileText.Split(Environment.NewLine);
            var constantLine = lines.FirstOrDefault(l => l.StartsWith(SolutionFileVSVersion));
            var minNumberArray = constantLine.Split("=");
            return minNumberArray[minNumberArray.Length - 1].Trim();
        }

        public List<VisualStudioInstanceData> GetVisualStudioInstallations()
        {
            var visualStudioInstances = new List<VisualStudioInstanceData>();
            try
            {
                var query = new SetupConfiguration();
                var query2 = (ISetupConfiguration2)query;
                var e = query2.EnumAllInstances();

                int fetched;
                var instances = new ISetupInstance[1];
                do
                {
                    e.Next(1, instances, out fetched);
                    if (fetched > 0)
                    {
                        var visualStudioInstance = ParseVisualStudioInstanceData(instances[0]);
                        visualStudioInstances.Add(visualStudioInstance);
                    }
                }
                while (fetched > 0);
            }
            catch (Exception ex)
            {
                //Visual studio is not installed
            }
            if (!visualStudioInstances.Any())
            {
                var msbuildExes = GetFileSystemMsBuildExePath()?.ToList();
                msbuildExes.ForEach(msbuildExe =>
                {
                    visualStudioInstances.Add(new VisualStudioInstanceData()
                    {
                        Name = "MSBuild",
                        VisualStudioMSBuildPath = msbuildExe
                    });
                });
            }
            return visualStudioInstances;
        }

        private VisualStudioInstanceData ParseVisualStudioInstanceData(ISetupInstance instance)
        {
            var visualStudioInstanceData = new VisualStudioInstanceData();
            
            var instance2 = (ISetupInstance2)instance;
            var state = instance2.GetState();

            if (state == InstanceState.Complete)
            {
                visualStudioInstanceData.Id = instance2.GetProduct().GetId();
                visualStudioInstanceData.Name = instance2.GetDisplayName();
                visualStudioInstanceData.Version = instance.GetInstallationVersion();
                visualStudioInstanceData.InstallationPath = instance2.GetInstallationPath();

                var packages = instance2.GetPackages()?.ToList();

                    packages.Where(package => package.GetType() == "Workload")?.ToList()
                    .ForEach(package =>
                    {
                        visualStudioInstanceData.Workloads.Add(package.GetId());
                    });

                var msBuildPackage = packages.FirstOrDefault(package => package.GetId().Equals("Microsoft.Component.MSBuild", StringComparison.InvariantCultureIgnoreCase));
                if(msBuildPackage != null)
                {
                    var msBuildBinDir = Path.Combine(visualStudioInstanceData.InstallationPath, "MSBuild", "Current", "Bin");
                    if (Directory.Exists(msBuildBinDir))
                    {
                        visualStudioInstanceData.VisualStudioMSBuildPath = Path.Combine(msBuildBinDir, "MSBuild.exe");
                    }
                }
                
            }

            return visualStudioInstanceData;
        }

        public string GetFirstMatchingMsBuildFromPath(string programFilesPath = null, string programFilesX86Path = null, string toolsVersion = null) =>
            GetFileSystemMsBuildExePath(programFilesPath, programFilesX86Path, toolsVersion).FirstOrDefault();

        public List<string> GetFileSystemMsBuildExePath(string programFilesPath = null, string programFilesX86Path = null, string toolsVersion = null)
        {
            // Could not find the tools path, possibly due to https://github.com/Microsoft/msbuild/issues/2369
            // Try to poll for it. From https://github.com/KirillOsenkov/MSBuildStructuredLog/blob/4649f55f900a324421bad5a714a2584926a02138/src/StructuredLogViewer/MSBuildLocator.cs

            var result = new List<string>();

            List<string> editions = new List<string> { "Enterprise", "Professional", "Community", "BuildTools" };
            var targets = new string[] { "Microsoft.CSharp.targets", "Microsoft.CSharp.CurrentVersion.targets", "Microsoft.Common.targets" };
            DirectoryInfo vsDirectory;
            var programFiles = programFilesPath ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = programFilesX86Path ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86);


            //2022              
            vsDirectory = new DirectoryInfo(Path.Combine(programFiles, "Microsoft Visual Studio"));
            // "Microsoft.CSharp.CrossTargeting.targets"

            string msbuildpath = GetMsBuildPathFromVSDirectory(vsDirectory, editions, targets, toolsVersion);
            if (!string.IsNullOrEmpty(msbuildpath))
            {
                result.Add(msbuildpath);
            }

            // 2019, 2017
            vsDirectory = new DirectoryInfo(Path.Combine(programFilesX86, "Microsoft Visual Studio"));
            msbuildpath = GetMsBuildPathFromVSDirectory(vsDirectory, editions, targets, toolsVersion);
            if (!string.IsNullOrEmpty(msbuildpath))
            {
                result.Add(msbuildpath);
            }

            // 14.0, 12.0 
            vsDirectory = new DirectoryInfo(Path.Combine(programFilesX86, "MSBuild"));
            msbuildpath = GetMsBuildPathFromVSDirectoryBelow15(vsDirectory, editions, targets);
            if (!string.IsNullOrEmpty(msbuildpath))
            {
                result.Add(msbuildpath);
            }
            return result;
        }


        private string GetMsBuildPathFromVSDirectory(DirectoryInfo vsDirectory, List<string> editions, string[] targets, string projectToolsVersion)
        {
            double.TryParse(projectToolsVersion, out double projectMsbuildVersionNumber);

            if (vsDirectory.Exists)
            {
                List<FileInfo> msBuildExePath = vsDirectory
                    .GetDirectories("MSBuild", SearchOption.AllDirectories)
                    .SelectMany(msBuildDir => msBuildDir.GetFiles("MSBuild.exe", SearchOption.AllDirectories))
                    .OrderByDescending(msbuild => FileVersionInfo.GetVersionInfo(msbuild.FullName).FileVersion)
                    .ThenBy(msbuild => editions.IndexOf(GetEditionType(msbuild.DirectoryName, editions)))
                    .ThenBy(msbuild =>
                    {
                        var folderName = GetVersionFolder(msbuild.FullName);
                        if (folderName.ToLower() == "current") return -100;
                        else
                        {
                            if (double.TryParse(folderName, out double result))
                            {
                                return -1 * result;
                            }
                            return -100;
                        }
                    })
                    .Where(msbuild =>
                    {
                        var targetsWithPath = GetTargetsWithPath(msbuild.DirectoryName, targets);
                        if (targetsWithPath.TrueForAll(File.Exists)) return true;
                        return false;
                    })
                    .ToList();



                if (projectMsbuildVersionNumber > 0)
                {
                    // If we have a tools version, use that to remove any versions of msbuild that are earlier than the project version
                    msBuildExePath = msBuildExePath?.Where(
                        msbuild =>
                        {
                            var fileVersionArray = FileVersionInfo.GetVersionInfo(msbuild.FullName).FileVersion?.Split('.');
                            var fileVersionString = fileVersionArray.Length > 1 ? String.Join(".", fileVersionArray[0], fileVersionArray[1]) : fileVersionArray[0];
                            double.TryParse(fileVersionString, out double msBuildVersion);
                            return msBuildVersion >= projectMsbuildVersionNumber;
                        }
                        )?.ToList();
                }
                return msBuildExePath?.FirstOrDefault()?.FullName;
            };
            return null;
        }

        private string GetMsBuildPathFromVSDirectoryBelow15(DirectoryInfo vsDirectory, List<string> editions, string[] targets)
        {
            if (vsDirectory.Exists)
            {
                List<FileInfo> msBuildExePath = vsDirectory
                    .GetFiles("MSBuild.exe", SearchOption.AllDirectories)
                    .OrderByDescending(msbuild => FileVersionInfo.GetVersionInfo(msbuild.FullName).FileVersion)
                    .ThenBy(msbuild =>
                    {
                        var folderName = GetVersionFolder(msbuild.FullName);
                        if (folderName.ToLower() == "current") return 1;
                        else return -Convert.ToDouble(folderName);

                    })
                    .Where(msbuild =>
                    {
                        var targetsWithPath = GetTargetsWithPath(msbuild.DirectoryName, targets);
                        if (targetsWithPath.TrueForAll(File.Exists)) return true;
                        return false;
                    })
                    .ToList();
                return msBuildExePath?.FirstOrDefault()?.FullName;
            };
            return "";
        }

        private string GetEditionType(string vsPath, List<string> editions)
        {
            string[] elements = vsPath.Split(Path.DirectorySeparatorChar);
            foreach (var edition in editions)
            {
                if (elements.Contains(edition))
                {
                    return edition;
                }
            }
            return "";
        }

        private string GetVersionFolder(string vsPath)
        {
            List<string> elements = vsPath.Split(Path.DirectorySeparatorChar).ToList();
            var folderIdx = elements.IndexOf("MSBuild");
            return elements[folderIdx + 1];
        }
        private List<string> GetTargetsWithPath(string vsPath, string[] targets)
        {
            List<string> targetsWithPath = new List<string>();
            foreach (string target in targets)
            {
                targetsWithPath.Add(Path.Combine(vsPath, target));
            }
            return targetsWithPath;
        }
    }
}
