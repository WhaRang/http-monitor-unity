using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace HttpMonitor.CodeGen
{
    /// <summary>
    /// Unity discovers this class because the containing assembly's name ends in ".CodeGen".
    /// It runs in Unity's compilation pipeline process after each assembly compiles: no UnityEditor
    /// API is available here, configuration arrives via scripting defines and files on disk.
    /// </summary>
    public sealed class HttpMonitorILPostProcessor : ILPostProcessor
    {
        /// <summary>Add this scripting define to turn the weaver off for a build target.</summary>
        private const string DisableDefine = "HTTP_MONITOR_DISABLE";

        private static readonly string[] SkipPrefixes =
        {
            "Unity.", "UnityEngine.", "UnityEditor.",
            "HttpMonitor.",
            "Mono.", "System.", "mscorlib", "netstandard", "nunit.",
        };

        private static readonly string[] RequiredReferenceSuffixes =
        {
            "UnityEngine.UnityWebRequestModule.dll",
            "UnityEngine.dll",
        };

        public override ILPostProcessor GetInstance() => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            var name = compiledAssembly.Name;
            
            if (SkipPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
                return false;
            
            if (compiledAssembly.Defines != null && compiledAssembly.Defines.Contains(DisableDefine))
                return false;

            return compiledAssembly.References.Any(r =>
                RequiredReferenceSuffixes.Any(s => r.EndsWith(s, StringComparison.OrdinalIgnoreCase)));
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            var diagnostics = new List<DiagnosticMessage>();

            try
            {
                var input = compiledAssembly.InMemoryAssembly;
                var hasSymbols = input.PdbData != null && input.PdbData.Length > 0;

                using (var resolver = new ReferencesResolver(compiledAssembly.References))
                using (var peStream = new MemoryStream(input.PeData))
                {
                    var readerParameters = new ReaderParameters
                    {
                        AssemblyResolver = resolver,
                        ReadingMode = ReadingMode.Immediate,
                        ReadSymbols = hasSymbols,
                        SymbolReaderProvider = hasSymbols ? new PortablePdbReaderProvider() : null,
                        SymbolStream = hasSymbols ? new MemoryStream(input.PdbData) : null,
                    };

                    using (var assembly = AssemblyDefinition.ReadAssembly(peStream, readerParameters))
                    {
                        var weaver = new Weaver();
                        var changed = weaver.Weave(assembly.MainModule);

                        SpikeLog.Write(compiledAssembly, weaver);

                        if (!changed) 
                            return null;

                        var peOut = new MemoryStream();
                        var pdbOut = new MemoryStream();
                        
                        var writerParameters = new WriterParameters
                        {
                            WriteSymbols = hasSymbols,
                            SymbolWriterProvider = hasSymbols ? new PortablePdbWriterProvider() : null,
                            SymbolStream = hasSymbols ? pdbOut : null,
                        };
                        
                        assembly.Write(peOut, writerParameters);

                        diagnostics.Add(new DiagnosticMessage
                        {
                            DiagnosticType = DiagnosticType.Warning, // Spike: visible in the Console. Downgrade later.
                            MessageData = $"[HttpMonitor] wove {weaver.RewrittenCallSites} call site(s) in {compiledAssembly.Name}",
                        });

                        return new ILPostProcessResult(new InMemoryAssembly(peOut.ToArray(), pdbOut.ToArray()), diagnostics);
                    }
                }
            }
            catch (Exception e)
            {
                diagnostics.Add(new DiagnosticMessage
                {
                    DiagnosticType = DiagnosticType.Warning,
                    MessageData = $"[HttpMonitor] weaver failed on {compiledAssembly.Name}, assembly left unmodified: {e}",
                });
                
                return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, diagnostics);
            }
        }
    }

    /// <summary>
    /// M0 only: records what the weaver process sees (working directory, defines, references) so
    /// the plan's open questions about the ILPP environment get answered from a real run.
    /// Written to Library/HttpMonitor/weaver.log relative to the process working directory.
    /// </summary>
    internal static class SpikeLog
    {
        private static readonly object Gate = new object();

        public static void Write(ICompiledAssembly compiledAssembly, Weaver weaver)
        {
            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var libraryDir = Path.Combine(cwd, "Library");
                if (!Directory.Exists(libraryDir)) return;

                var dir = Path.Combine(libraryDir, "HttpMonitor");
                Directory.CreateDirectory(dir);

                var lines = new List<string>
                {
                    $"=== {DateTime.Now:O} {compiledAssembly.Name}",
                    $"cwd: {cwd}",
                    $"defines: {string.Join(" ", compiledAssembly.Defines ?? Array.Empty<string>())}",
                    $"references: {compiledAssembly.References.Length}",
                    $"rewritten: {weaver.RewrittenCallSites}",
                };
                lines.AddRange(weaver.Log.Select(l => "  " + l));
                lines.Add(string.Empty);

                lock (Gate) File.AppendAllLines(Path.Combine(dir, "weaver.log"), lines);
            }
            catch
            {
                // Diagnostics only; never let logging affect the build.
            }
        }
    }
}
