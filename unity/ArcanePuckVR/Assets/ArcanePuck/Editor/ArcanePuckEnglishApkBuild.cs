using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ArcanePuck.Editor
{
    /// <summary>
    /// 英文版 APK 构建包装：复用工程的场景列表和 Player/XR 设置，不调用 Configure()，
    /// 只把输出写到命令行参数 -apkOutput 指定的位置（默认在工程外层 APK/ 目录）。
    /// </summary>
    public static class ArcanePuckEnglishApkBuild
    {
        public static void BuildEnglishDevelopmentApk()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException("Active build target is not Android; launch Unity with -buildTarget Android.");

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("No enabled scenes in EditorBuildSettings.");

            var args = Environment.GetCommandLineArgs();
            var idx = Array.IndexOf(args, "-apkOutput");
            string output;
            if (idx >= 0 && idx + 1 < args.Length)
                output = args[idx + 1];
            else
                output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "APK",
                    $"ArcanePuckVR-{PlayerSettings.bundleVersion}-en.apk"));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);

            Debug.Log($"ARCANE_PUCK_EN_BUILD: scenes={string.Join(",", scenes)} version={PlayerSettings.bundleVersion} " +
                      $"code={PlayerSettings.Android.bundleVersionCode} product={PlayerSettings.productName} out={output}");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"English APK build failed: {report.summary.result} errors={report.summary.totalErrors}");
            Debug.Log($"ARCANE_PUCK_EN_APK: {output} ({report.summary.totalSize} bytes)");
        }
    }
}
