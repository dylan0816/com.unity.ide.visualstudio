/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using SimpleJSON;
using IOPath = System.IO.Path;

namespace Microsoft.Unity.VisualStudio.Editor
{
	internal class TraeCodeInstallation : VisualStudioInstallation
	{
		private static readonly IGenerator _generator = GeneratorFactory.GetInstance(GeneratorStyle.SDK);

		public override bool SupportsAnalyzers
		{
			get
			{
				return true;
			}
		}

		public override Version LatestLanguageVersionSupported
		{
			get
			{
				return new Version(13, 0);
			}
		}

		private string GetExtensionPath()
		{
			var vscode = IsPrerelease ? ".vscode-insiders" : ".vscode";
			var extensionsPath = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), vscode, "extensions");
			if (!Directory.Exists(extensionsPath))
				return null;

			return Directory
				.EnumerateDirectories(extensionsPath, $"{MicrosoftUnityExtensionId}*") // publisherid.extensionid
				.OrderByDescending(n => n)
				.FirstOrDefault();
		}

		public override string[] GetAnalyzers()
		{
			var vstuPath = GetExtensionPath();
			if (string.IsNullOrEmpty(vstuPath))
				return Array.Empty<string>();

			return GetAnalyzers(vstuPath);
		}

		public override IGenerator ProjectGenerator
		{
			get
			{
				return _generator;
			}
		}
		[Serializable]
		internal class VisualStudioCodeManifest
		{
			public string name;
			public string version;
		}

		public override void CreateExtraFiles(string projectDirectory)
		{
			try
			{
				var vscodeDirectory = IOPath.Combine(projectDirectory.NormalizePathSeparators(), ".vscode");
				Directory.CreateDirectory(vscodeDirectory);

				var enablePatch = !File.Exists(IOPath.Combine(vscodeDirectory, ".vstupatchdisable"));

				VisualStudioCodeInstallation.CreateRecommendedExtensionsFile(vscodeDirectory, enablePatch);
				CreateSettingsFile(vscodeDirectory, enablePatch);
				CreateLaunchFile(vscodeDirectory, enablePatch);
			}
			catch (IOException)
			{
			}
		}

		private const string DefaultLaunchFileContent = @"{
    ""version"": ""0.2.0"",
    ""configurations"": [
        {
            ""name"": ""Attach to Unity"",
            ""type"": ""vstuc"",
            ""request"": ""attach""
        }
     ]
}";

		private static void CreateLaunchFile(string vscodeDirectory, bool enablePatch)
		{
			var launchFile = IOPath.Combine(vscodeDirectory, "launch.json");
			if (File.Exists(launchFile))
			{
				if (enablePatch)
					PatchLaunchFile(launchFile);

				return;
			}

			File.WriteAllText(launchFile, DefaultLaunchFileContent);
		}

		private static void PatchLaunchFile(string launchFile)
		{
			try
			{
				const string configurationsKey = "configurations";
				const string typeKey = "type";

				var content = File.ReadAllText(launchFile);
				var launch = JSONNode.Parse(content);

				var configurations = launch[configurationsKey] as JSONArray;
				if (configurations == null)
				{
					configurations = new JSONArray();
					launch.Add(configurationsKey, configurations);
				}

				if (configurations.Linq.Any(entry => entry.Value[typeKey].Value == "vstuc"))
					return;

				var defaultContent = JSONNode.Parse(DefaultLaunchFileContent);
				configurations.Add(defaultContent[configurationsKey][0]);

				VisualStudioCodeInstallation.WriteAllTextFromJObject(launchFile, launch);
			}
			catch (Exception)
			{
				// do not fail if we cannot patch the launch.json file
			}
		}

		private void CreateSettingsFile(string vscodeDirectory, bool enablePatch)
		{
			var settingsFile = IOPath.Combine(vscodeDirectory, "settings.json");
			if (File.Exists(settingsFile))
			{
				if (enablePatch)
					PatchSettingsFile(settingsFile);

				return;
			}

			const string excludes = @"    ""files.exclude"": {
        ""**/.DS_Store"": true,
        ""**/.git"": true,
        ""**/.vs"": true,
        ""**/.gitmodules"": true,
        ""**/.vsconfig"": true,
        ""**/*.booproj"": true,
        ""**/*.pidb"": true,
        ""**/*.suo"": true,
        ""**/*.user"": true,
        ""**/*.userprefs"": true,
        ""**/*.unityproj"": true,
        ""**/*.dll"": true,
        ""**/*.exe"": true,
        ""**/*.pdf"": true,
        ""**/*.mid"": true,
        ""**/*.midi"": true,
        ""**/*.wav"": true,
        ""**/*.gif"": true,
        ""**/*.ico"": true,
        ""**/*.jpg"": true,
        ""**/*.jpeg"": true,
        ""**/*.png"": true,
        ""**/*.psd"": true,
        ""**/*.tga"": true,
        ""**/*.tif"": true,
        ""**/*.tiff"": true,
        ""**/*.3ds"": true,
        ""**/*.3DS"": true,
        ""**/*.fbx"": true,
        ""**/*.FBX"": true,
        ""**/*.lxo"": true,
        ""**/*.LXO"": true,
        ""**/*.ma"": true,
        ""**/*.MA"": true,
        ""**/*.obj"": true,
        ""**/*.OBJ"": true,
        ""**/*.asset"": true,
        ""**/*.cubemap"": true,
        ""**/*.flare"": true,
        ""**/*.mat"": true,
        ""**/*.meta"": true,
        ""**/*.prefab"": true,
        ""**/*.unity"": true,
        ""build/"": true,
        ""Build/"": true,
        ""Library/"": true,
        ""library/"": true,
        ""obj/"": true,
        ""Obj/"": true,
        ""Logs/"": true,
        ""logs/"": true,
        ""ProjectSettings/"": true,
        ""UserSettings/"": true,
        ""temp/"": true,
        ""Temp/"": true
    }";

			var content = @"{
" + excludes + @",
    ""files.associations"": {
        ""*.asset"": ""yaml"",
        ""*.meta"": ""yaml"",
        ""*.prefab"": ""yaml"",
        ""*.unity"": ""yaml"",
    },
    ""explorer.fileNesting.enabled"": true,
    ""explorer.fileNesting.patterns"": {
        ""*.sln"": ""*.csproj"",
        ""*.slnx"": ""*.csproj""
    },
    ""dotnet.defaultSolution"": """ + IOPath.GetFileName(ProjectGenerator.SolutionFile()) + @"""
}";

			File.WriteAllText(settingsFile, content);
		}

		private void PatchSettingsFile(string settingsFile)
		{
			try
			{
				const string excludesKey = "files.exclude";
				const string solutionKey = "dotnet.defaultSolution";

				var content = File.ReadAllText(settingsFile);
				var settings = JSONNode.Parse(content);

				var excludes = settings[excludesKey] as JSONObject;
				if (excludes == null)
					return;

				var patchList = new List<string>();
				var patched = false;

				// Remove files.exclude for solution+project files in the project root
				foreach (var exclude in excludes)
				{
					if (!bool.TryParse(exclude.Value, out var exc) || !exc)
						continue;

					var key = exclude.Key;

					if (!key.EndsWith(".sln") && !key.EndsWith(".csproj"))
						continue;

					if (!Regex.IsMatch(key, "^(\\*\\*[\\\\\\/])?\\*\\.(sln|csproj)$"))
						continue;

					patchList.Add(key);
					patched = true;
				}

				// Check default solution
				var defaultSolution = settings[solutionKey];
				var solutionFile = IOPath.GetFileName(ProjectGenerator.SolutionFile());
				if (defaultSolution == null || defaultSolution.Value != solutionFile)
				{
					settings[solutionKey] = solutionFile;
					patched = true;
				}

				if (!patched)
					return;

				foreach (var patch in patchList)
					excludes.Remove(patch);

				VisualStudioCodeInstallation.WriteAllTextFromJObject(settingsFile, settings);
			}
			catch (Exception)
			{
				// do not fail if we cannot patch the settings.json file
			}
		}

		private const string MicrosoftUnityExtensionId = "visualstudiotoolsforunity.vstuc";
		private const string DefaultRecommendedExtensionsContent = @"{
    ""recommendations"": [
      """ + MicrosoftUnityExtensionId + @"""
    ]
}
";

		// private static void CreateRecommendedExtensionsFile(string vscodeDirectory, bool enablePatch)
		// {
		// 	// see https://tattoocoder.com/recommending-vscode-extensions-within-your-open-source-projects/
		// 	var extensionFile = IOPath.Combine(vscodeDirectory, "extensions.json");
		// 	if (File.Exists(extensionFile))
		// 	{
		// 		if (enablePatch)
		// 			PatchRecommendedExtensionsFile(extensionFile);

		// 		return;
		// 	}

		// 	File.WriteAllText(extensionFile, DefaultRecommendedExtensionsContent);
		// }

		// private static void PatchRecommendedExtensionsFile(string extensionFile)
		// {
		// 	try
		// 	{
		// 		const string recommendationsKey = "recommendations";

		// 		var content = File.ReadAllText(extensionFile);
		// 		var extensions = JSONNode.Parse(content);

		// 		var recommendations = extensions[recommendationsKey] as JSONArray;
		// 		if (recommendations == null)
		// 		{
		// 			recommendations = new JSONArray();
		// 			extensions.Add(recommendationsKey, recommendations);
		// 		}

		// 		if (recommendations.Linq.Any(entry => entry.Value.Value == MicrosoftUnityExtensionId))
		// 			return;

		// 		recommendations.Add(MicrosoftUnityExtensionId);
		// 		WriteAllTextFromJObject(extensionFile, extensions);
		// 	}
		// 	catch (Exception)
		// 	{
		// 		// do not fail if we cannot patch the extensions.json file
		// 	}
		// }

		// private static void WriteAllTextFromJObject(string file, JSONNode node)
		// {
		// 	using (var fs = File.Open(file, FileMode.Create))
		// 	using (var sw = new StreamWriter(fs))
		// 	{
		// 		// Keep formatting/indent in sync with default contents
		// 		sw.Write(node.ToString(aIndent: 4));
		// 	}
		// }

		public override bool Open(string path, int line, int column, string solution)
		{
			var application = Path;

			line = Math.Max(1, line);
			column = Math.Max(0, column);

			var directory = IOPath.GetDirectoryName(solution);
			var workspace = VisualStudioCodeInstallation.TryFindWorkspace(directory);

			var target = workspace ?? directory;

			ProcessRunner.Start(string.IsNullOrEmpty(path)
				? VisualStudioCodeInstallation.ProcessStartInfoFor(application, $"\"{target}\"")
				: VisualStudioCodeInstallation.ProcessStartInfoFor(application, $"\"{target}\" -g \"{path}\":{line}:{column}"));

			return true;
		}

		// 		private static string TryFindWorkspace(string directory)
		// 		{
		// 			var files = Directory.GetFiles(directory, "*.code-workspace", SearchOption.TopDirectoryOnly);
		// 			if (files.Length == 0 || files.Length > 1)
		// 				return null;

		// 			return files[0];
		// 		}

		// 		private static ProcessStartInfo ProcessStartInfoFor(string application, string arguments)
		// 		{
		// #if UNITY_EDITOR_OSX
		// 			// wrap with built-in OSX open feature
		// 			arguments = $"-n \"{application}\" --args {arguments}";
		// 			application = "open";
		// 			return ProcessRunner.ProcessStartInfoFor(application, arguments, redirect:false, shell: true);
		// #else
		// 			return ProcessRunner.ProcessStartInfoFor(application, arguments, redirect: false);
		// #endif
		// 		}
	}
}
