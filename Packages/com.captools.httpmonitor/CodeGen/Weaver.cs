using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace HttpMonitor.CodeGen
{
    /// <summary>
    /// The pure Cecil transform. Deliberately free of Unity types so it can be exercised on any
    /// assembly in a plain unit test.
    ///
    /// Every rewrite is a call-site substitution that preserves the evaluation stack shape:
    /// the instruction's opcode and operand change, nothing is inserted or removed. That is what
    /// keeps this safe inside async state machines, iterators, try/catch regions and lambdas.
    ///
    /// Rules:
    ///   uwr.SendWebRequest()                  -> Interceptor.SendWebRequest(uwr)
    ///   uwr.Dispose()                         -> Interceptor.Dispose(uwr)
    ///   ((IDisposable)uwr).Dispose()          -> Interceptor.Dispose(uwr)   [what `using` emits;
    ///                                            matched only when the preceding instruction
    ///                                            loads a UnityWebRequest-typed local/field/arg]
    /// </summary>
    public sealed class Weaver
    {
        public const string RuntimeAssemblyName = "HttpMonitor.Runtime";
        
        private const string InterceptorNamespace = "HttpMonitor";
        private const string InterceptorTypeName = "Interceptor";
        private const string UnityWebRequestFullName = "UnityEngine.Networking.UnityWebRequest";
        private const string IDisposableFullName = "System.IDisposable";

        public int RewrittenCallSites { get; private set; }
        public List<string> Log { get; } = new List<string>();

        private TypeReference _interceptor;

        /// <returns>true when at least one instruction was rewritten.</returns>
        public bool Weave(ModuleDefinition module)
        {
            _interceptor = null;

            foreach (var type in module.GetTypes())
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody) continue;

                    var instructions = method.Body.Instructions;
                    for (var i = 0; i < instructions.Count; i++)
                    {
                        var instruction = instructions[i];
                        if (instruction.OpCode != OpCodes.Callvirt && instruction.OpCode != OpCodes.Call) continue;
                        if (!(instruction.Operand is MethodReference target)) continue;

                        if (IsUnityWebRequestMethod(target, "SendWebRequest"))
                        {
                            Replace(module, method, instruction, "SendWebRequest", target.ReturnType, target.DeclaringType);
                        }
                        else if (IsUnityWebRequestMethod(target, "Dispose"))
                        {
                            Replace(module, method, instruction, "Dispose", target.ReturnType, target.DeclaringType);
                        }
                        else if (IsInterfaceDispose(target) && i > 0)
                        {
                            var loaded = LoadedType(method, instructions[i - 1]);
                            if (loaded != null && loaded.FullName == UnityWebRequestFullName)
                                Replace(module, method, instruction, "Dispose", target.ReturnType, loaded);
                        }
                    }
                }
            }

            return RewrittenCallSites > 0;
        }

        private void Replace(ModuleDefinition module, MethodDefinition method, Instruction instruction,
            string interceptorMethod, TypeReference returnType, TypeReference parameterType)
        {
            _interceptor = _interceptor ?? GetInterceptorType(module);

            var replacement = new MethodReference(interceptorMethod, returnType, _interceptor) { HasThis = false };
            replacement.Parameters.Add(new ParameterDefinition(parameterType));

            instruction.OpCode = OpCodes.Call;
            instruction.Operand = module.ImportReference(replacement);

            RewrittenCallSites++;
            Log.Add($"{method.FullName} @ IL_{instruction.Offset:x4}: {interceptorMethod}");
        }

        private static bool IsUnityWebRequestMethod(MethodReference method, string name)
        {
            return method.HasThis
                && !method.HasParameters
                && method.Name == name
                && method.DeclaringType != null
                && method.DeclaringType.FullName == UnityWebRequestFullName;
        }

        private static bool IsInterfaceDispose(MethodReference method)
        {
            return method.HasThis
                && !method.HasParameters
                && method.Name == "Dispose"
                && method.DeclaringType != null
                && method.DeclaringType.FullName == IDisposableFullName;
        }

        /// <summary>Static type pushed by a load instruction, or null when it is not a plain load.</summary>
        private static TypeReference LoadedType(MethodDefinition method, Instruction instruction)
        {
            switch (instruction.OpCode.Code)
            {
                case Code.Ldloc_0: return method.Body.Variables[0].VariableType;
                case Code.Ldloc_1: return method.Body.Variables[1].VariableType;
                case Code.Ldloc_2: return method.Body.Variables[2].VariableType;
                case Code.Ldloc_3: return method.Body.Variables[3].VariableType;
                case Code.Ldloc:
                case Code.Ldloc_S: return ((VariableDefinition)instruction.Operand).VariableType;

                case Code.Ldfld:
                case Code.Ldsfld: return ((FieldReference)instruction.Operand).FieldType;

                case Code.Ldarg_0: return ArgumentType(method, 0);
                case Code.Ldarg_1: return ArgumentType(method, 1);
                case Code.Ldarg_2: return ArgumentType(method, 2);
                case Code.Ldarg_3: return ArgumentType(method, 3);
                case Code.Ldarg:
                case Code.Ldarg_S: return ((ParameterDefinition)instruction.Operand).ParameterType;

                default: return null;
            }
        }

        private static TypeReference ArgumentType(MethodDefinition method, int index)
        {
            if (method.HasThis)
            {
                if (index == 0) return method.DeclaringType;
                index--;
            }
            return index < method.Parameters.Count ? method.Parameters[index].ParameterType : null;
        }

        /// <summary>
        /// Builds a reference to HttpMonitor.Interceptor without resolving the runtime assembly.
        /// The woven assembly may never have referenced HttpMonitor.Runtime at compile time; Cecil
        /// adds the assembly reference row and the player binds it by name at load time.
        /// </summary>
        private static TypeReference GetInterceptorType(ModuleDefinition module)
        {
            var scope = module.AssemblyReferences.FirstOrDefault(a => a.Name == RuntimeAssemblyName);
            if (scope == null)
            {
                scope = new AssemblyNameReference(RuntimeAssemblyName, new Version(0, 0, 0, 0));
                module.AssemblyReferences.Add(scope);
            }

            return new TypeReference(InterceptorNamespace, InterceptorTypeName, module, scope)
            {
                IsValueType = false,
            };
        }
    }
}
