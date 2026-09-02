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
    ///   uwr.SetRequestHeader(name, value)     -> Interceptor.SetRequestHeader(uwr, name, value)
    ///   uwr.Dispose()                         -> Interceptor.Dispose(uwr)
    ///   ((IDisposable)uwr).Dispose()          -> Interceptor.Dispose(uwr)   [what `using` emits;
    ///                                            matched only when the preceding instruction
    ///                                            loads a UnityWebRequest-typed local/field/arg]
    ///   new HttpClient()                      -> Interceptor.CreateHttpClient()
    ///   new HttpClient(handler)               -> Interceptor.CreateHttpClient(handler)
    ///   new HttpClient(handler, dispose)      -> Interceptor.CreateHttpClient(handler, dispose)
    /// </summary>
    public sealed class Weaver
    {
        public const string RuntimeAssemblyName = "HttpMonitor.Runtime";
        
        private const string InterceptorNamespace = "HttpMonitor";
        private const string InterceptorTypeName = "Interceptor";
        private const string UnityWebRequestFullName = "UnityEngine.Networking.UnityWebRequest";
        private const string IDisposableFullName = "System.IDisposable";
        private const string HttpClientFullName = "System.Net.Http.HttpClient";
        private const string HttpMessageHandlerFullName = "System.Net.Http.HttpMessageHandler";

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
                        if (!(instruction.Operand is MethodReference target)) continue;

                        if (instruction.OpCode == OpCodes.Newobj)
                        {
                            if (IsSupportedHttpClientConstructor(target))
                                ReplaceConstructor(module, method, instruction, target, "CreateHttpClient");

                            continue;
                        }

                        if (instruction.OpCode != OpCodes.Callvirt && instruction.OpCode != OpCodes.Call) continue;

                        if (IsUnityWebRequestMethod(target, "SendWebRequest", 0))
                        {
                            Replace(module, method, instruction, target, target.DeclaringType);
                        }
                        else if (IsUnityWebRequestMethod(target, "SetRequestHeader", 2))
                        {
                            Replace(module, method, instruction, target, target.DeclaringType);
                        }
                        else if (IsUnityWebRequestMethod(target, "Dispose", 0))
                        {
                            Replace(module, method, instruction, target, target.DeclaringType);
                        }
                        else if (IsInterfaceDispose(target) && i > 0)
                        {
                            var loaded = LoadedType(method, instructions[i - 1]);
                            if (loaded != null && loaded.FullName == UnityWebRequestFullName)
                                Replace(module, method, instruction, target, loaded);
                        }
                    }
                }
            }

            return RewrittenCallSites > 0;
        }

        /// <summary>
        /// Rewrites an instance call into a static Interceptor call of the same name whose first
        /// parameter is the receiver, followed by the original parameters. Return type is unchanged,
        /// so the evaluation stack is identical before and after.
        /// </summary>
        private void Replace(ModuleDefinition module, MethodDefinition method, Instruction instruction,
            MethodReference target, TypeReference receiverType)
        {
            _interceptor = _interceptor ?? GetInterceptorType(module);

            var replacement = new MethodReference(target.Name, target.ReturnType, _interceptor) { HasThis = false };
            replacement.Parameters.Add(new ParameterDefinition(receiverType));

            foreach (var parameter in target.Parameters)
                replacement.Parameters.Add(new ParameterDefinition(parameter.ParameterType));

            instruction.OpCode = OpCodes.Call;
            instruction.Operand = module.ImportReference(replacement);

            RewrittenCallSites++;
            Log.Add($"{method.FullName} @ IL_{instruction.Offset:x4}: {target.Name}");
        }

        /// <summary>
        /// Rewrites <c>newobj T::.ctor(args)</c> into a static factory call taking the same args and
        /// returning T. A constructor pushes one T; so does the factory, so the stack is unchanged.
        /// </summary>
        private void ReplaceConstructor(ModuleDefinition module, MethodDefinition method, Instruction instruction,
            MethodReference constructor, string factoryName)
        {
            _interceptor = _interceptor ?? GetInterceptorType(module);

            var replacement = new MethodReference(factoryName, constructor.DeclaringType, _interceptor) { HasThis = false };

            foreach (var parameter in constructor.Parameters)
                replacement.Parameters.Add(new ParameterDefinition(parameter.ParameterType));

            instruction.OpCode = OpCodes.Call;
            instruction.Operand = module.ImportReference(replacement);

            RewrittenCallSites++;
            Log.Add($"{method.FullName} @ IL_{instruction.Offset:x4}: {factoryName}");
        }

        /// <summary>HttpClient(), HttpClient(HttpMessageHandler), HttpClient(HttpMessageHandler, bool).</summary>
        private static bool IsSupportedHttpClientConstructor(MethodReference method)
        {
            if (method.Name != ".ctor" || method.DeclaringType == null || method.DeclaringType.FullName != HttpClientFullName)
                return false;

            switch (method.Parameters.Count)
            {
                case 0:
                    return true;
                case 1:
                    return method.Parameters[0].ParameterType.FullName == HttpMessageHandlerFullName;
                case 2:
                    return method.Parameters[0].ParameterType.FullName == HttpMessageHandlerFullName
                        && method.Parameters[1].ParameterType.FullName == "System.Boolean";
                default:
                    return false;
            }
        }

        private static bool IsUnityWebRequestMethod(MethodReference method, string name, int parameterCount)
        {
            return method.HasThis
                && method.Parameters.Count == parameterCount
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
