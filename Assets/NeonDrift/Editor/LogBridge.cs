using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace NeonDrift.EditorTools
{
    /// <summary>
    /// 把编译结果和 Console 里的警告 / 报错同步写到项目的 Logs 文件夹：
    ///   Logs/compile.txt  最近一次编译的错误和警告
    ///   Logs/console.txt  最近一次进入 Play 以来的报错
    /// 方便协作者（包括 Claude）不打开编辑器也能看到问题。Logs 文件夹不会提交到 Git。
    /// </summary>
    [InitializeOnLoad]
    static class LogBridge
    {
        static readonly object Gate = new object();
        static string LogsDir => Path.Combine(Directory.GetCurrentDirectory(), "Logs");
        static string CompileFile => Path.Combine(LogsDir, "compile.txt");
        static string ConsoleFile => Path.Combine(LogsDir, "console.txt");

        static LogBridge()
        {
            Directory.CreateDirectory(LogsDir);
            CompilationPipeline.compilationStarted += _ => Write(CompileFile, $"# compile started {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n", false);
            CompilationPipeline.assemblyCompilationFinished += (path, messages) =>
            {
                var sb = new StringBuilder();
                int errors = 0;
                foreach (var m in messages)
                {
                    if (m.type == CompilerMessageType.Error) errors++;
                    sb.AppendLine($"{m.type}: {m.file}({m.line},{m.column}) {m.message}");
                }
                sb.Insert(0, $"## {Path.GetFileName(path)}  errors={errors}\n");
                Write(CompileFile, sb.ToString(), true);
            };
            CompilationPipeline.compilationFinished += _ => Write(CompileFile, $"# compile finished {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n", true);

            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.ExitingEditMode)
                    Write(ConsoleFile, $"# play started {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n", false);
            };
            Application.logMessageReceivedThreaded += (condition, stack, type) =>
            {
                if (type == LogType.Log) return;
                Write(ConsoleFile, $"[{DateTime.Now:HH:mm:ss}] {type}: {condition}\n{(type == LogType.Warning ? "" : stack)}\n", true);
            };
            Write(CompileFile, $"# editor domain loaded {DateTime.Now:yyyy-MM-dd HH:mm:ss} (all scripts compiled)\n", true);
        }

        static void Write(string file, string text, bool append)
        {
            try
            {
                lock (Gate)
                {
                    if (append) File.AppendAllText(file, text);
                    else File.WriteAllText(file, text);
                }
            }
            catch (Exception) { /* 日志写失败不影响编辑器 */ }
        }
    }
}
