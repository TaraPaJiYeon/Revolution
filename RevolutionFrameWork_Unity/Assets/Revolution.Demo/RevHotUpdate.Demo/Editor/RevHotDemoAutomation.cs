// RevHotDemoAutomation.cs —— 热更演示工具的无人值守入口，仍复用原打包链与目录约定。
using System;
using System.IO;
using Revolution.Editor;
using UnityEditor;

namespace Revolution.Demo.HotUpdate
{
    public static class RevHotDemoAutomation
    {
        [RevAutomationAction("hot.demo-prepare")]
        public static object Prepare(RevAutomationRequest request)
        {
            if (!request.confirm) { throw new InvalidOperationException("演示准备会更新演示资源、版本及打包产物，需要 --confirm"); }
            RevHotUpdateDemoTools.PrepareAll();
            return new RevAutomationActions.RevAutomationText(RevHotUpdateDemoTools.DescribeUrls());
        }

        [RevAutomationAction("hot.demo-clear")]
        public static object Clear(RevAutomationRequest request)
        {
            if (!request.confirm) { throw new InvalidOperationException("删除客户端热更缓存需要 --confirm"); }
            return new RevAutomationActions.RevAutomationText(RevHotUpdateDemoTools.DeleteClientCache());
        }

        [RevAutomationAction("hot.demo-urls")]
        public static object Urls(RevAutomationRequest request)
        {
            return new RevAutomationActions.RevAutomationText(RevHotUpdateDemoTools.DescribeUrls());
        }

        [RevAutomationAction("hot.demo-open")]
        public static object Open(RevAutomationRequest request)
        {
            string root = Path.GetFullPath("LocalCDN");
            Directory.CreateDirectory(root);
            if (!UnityEngine.Application.isBatchMode) { EditorUtility.RevealInFinder(root); }
            return new RevAutomationActions.RevAutomationText(root);
        }
    }
}
