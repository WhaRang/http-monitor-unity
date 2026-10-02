using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;

namespace HttpMonitor.CodeGen
{
    /// <summary>
    /// Resolves assembly references from the explicit list Unity hands to the post-processor.
    /// Files are read fully into memory so no file handle is held while other compilations run.
    /// </summary>
    internal sealed class ReferencesResolver : IAssemblyResolver
    {
        private readonly Dictionary<string, string> _pathsByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AssemblyDefinition> _cache = new(StringComparer.OrdinalIgnoreCase);

        public ReferencesResolver(IEnumerable<string> referencePaths)
        {
            foreach (var path in referencePaths)
            {
                var name = Path.GetFileNameWithoutExtension(path);

                _pathsByName.TryAdd(name, path);
            }
        }

        public AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            return Resolve(name, new ReaderParameters(ReadingMode.Deferred));
        }

        public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(name.Name, out var cached))
                    return cached;

                if (!_pathsByName.TryGetValue(name.Name, out var path))
                    throw new AssemblyResolutionException(name);

                parameters.AssemblyResolver ??= this;

                var stream = new MemoryStream(File.ReadAllBytes(path));
                var assembly = AssemblyDefinition.ReadAssembly(stream, parameters);
                _cache.Add(name.Name, assembly);

                return assembly;
            }
        }

        public void Dispose()
        {
            lock (_cache)
            {
                foreach (var assembly in _cache.Values)
                    assembly.Dispose();

                _cache.Clear();
            }
        }
    }
}