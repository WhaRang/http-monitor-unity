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
    /// Unity discovers this class because the containing assembly's name matches "Unity.*.CodeGen".
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
            "Mono.", "System.", "mscorlib", "netstandard", "nunit.",
        };

        /// <summary>Our own code calls the real SendWebRequest/Dispose and must never be rewritten.</summary>
        private static readonly string[] SkipExact =
        {
            "HttpMonitor.Runtime",
            "HttpMonitor.Editor",
        };

        public override ILPostProcessor GetInstance() => this;

        /// <summary>
        /// Every user assembly is scanned: UnityWebRequest lives in an engine module and HttpClient
        /// in netstandard, both referenced by practically everything, so a reference filter would
        /// not save work. The scan itself is cheap and leaves untouched assemblies untouched.
        /// </summary>
        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            var name = compiledAssembly.Name;

            if (SkipExact.Contains(name))
                return false;

            if (SkipPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
                return false;

            return compiledAssembly.Defines == null || !compiledAssembly.Defines.Contains(DisableDefine);
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
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

                        if (!weaver.Weave(assembly.MainModule))
                            return null; // Unity keeps the original bytes.

                        var peOut = new MemoryStream();
                        var pdbOut = new MemoryStream();

                        var writerParameters = new WriterParameters
                        {
                            WriteSymbols = hasSymbols,
                            SymbolWriterProvider = hasSymbols ? new PortablePdbWriterProvider() : null,
                            SymbolStream = hasSymbols ? pdbOut : null,
                        };

                        assembly.Write(peOut, writerParameters);

                        return new ILPostProcessResult(new InMemoryAssembly(peOut.ToArray(), pdbOut.ToArray()));
                    }
                }
            }
            catch (Exception e)
            {
                // A broken debugger must never break the build: warn and hand back the untouched assembly.
                var diagnostics = new List<DiagnosticMessage>
                {
                    new DiagnosticMessage
                    {
                        DiagnosticType = DiagnosticType.Warning,
                        MessageData = $"[HttpMonitor] weaver failed on {compiledAssembly.Name}, assembly left unmodified: {e}",
                    },
                };

                return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, diagnostics);
            }
        }
    }
}
