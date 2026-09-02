using System;
using System.IO;
using System.Linq;
using HttpMonitor.CodeGen;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace HttpMonitor.Tests.Editor
{
    /// <summary>
    /// Runs the weaver over synthetic Cecil modules and asserts on the exact instructions it leaves
    /// behind. No Unity assemblies are loaded: the weaver matches types by full name, so plain
    /// references to the expected names are enough.
    /// </summary>
    public class WeaverTests
    {
        private const string Uwr = "UnityEngine.Networking.UnityWebRequest";
        private const string UwrModule = "UnityEngine.UnityWebRequestModule";

        // ------------------------------------------------------------ synthetic module helpers

        private static ModuleDefinition NewModule()
        {
            var name = new AssemblyNameDefinition("Synthetic", new Version(1, 0, 0, 0));

            return AssemblyDefinition.CreateAssembly(name, "Synthetic.dll", ModuleKind.Dll).MainModule;
        }

        private static TypeReference Type(ModuleDefinition module, string ns, string name, string assembly)
        {
            var scope = module.AssemblyReferences.FirstOrDefault(a => a.Name == assembly);

            if (scope == null)
            {
                scope = new AssemblyNameReference(assembly, new Version(0, 0, 0, 0));
                module.AssemblyReferences.Add(scope);
            }

            return new TypeReference(ns, name, module, scope);
        }

        private static TypeReference UwrType(ModuleDefinition module) => Type(module, "UnityEngine.Networking", "UnityWebRequest", UwrModule);

        private static TypeDefinition Host(ModuleDefinition module)
        {
            var host = module.Types.FirstOrDefault(t => t.Name == "Host");

            if (host == null)
            {
                host = new TypeDefinition("Synthetic", "Host", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
                module.Types.Add(host);
            }

            return host;
        }

        private static MethodDefinition NewMethod(ModuleDefinition module, string name, bool isStatic, params TypeReference[] parameters)
        {
            var attributes = MethodAttributes.Public | (isStatic ? MethodAttributes.Static : 0);
            var method = new MethodDefinition(name, attributes, module.TypeSystem.Void);

            foreach (var parameter in parameters)
                method.Parameters.Add(new ParameterDefinition(parameter));

            Host(module).Methods.Add(method);

            return method;
        }

        private static MethodReference SendWebRequest(ModuleDefinition module)
        {
            var returnType = Type(module, "UnityEngine.Networking", "UnityWebRequestAsyncOperation", UwrModule);

            return new MethodReference("SendWebRequest", returnType, UwrType(module)) { HasThis = true };
        }

        private static MethodReference SetRequestHeader(ModuleDefinition module)
        {
            var method = new MethodReference("SetRequestHeader", module.TypeSystem.Void, UwrType(module)) { HasThis = true };
            method.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            method.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

            return method;
        }

        private static MethodReference UwrDispose(ModuleDefinition module)
        {
            return new MethodReference("Dispose", module.TypeSystem.Void, UwrType(module)) { HasThis = true };
        }

        private static MethodReference InterfaceDispose(ModuleDefinition module)
        {
            return new MethodReference("Dispose", module.TypeSystem.Void, Type(module, "System", "IDisposable", "netstandard")) { HasThis = true };
        }

        private static MethodReference HttpClientCtor(ModuleDefinition module, params TypeReference[] parameters)
        {
            var httpClient = Type(module, "System.Net.Http", "HttpClient", "netstandard");
            var ctor = new MethodReference(".ctor", module.TypeSystem.Void, httpClient) { HasThis = true };

            foreach (var parameter in parameters)
                ctor.Parameters.Add(new ParameterDefinition(parameter));

            return ctor;
        }

        private static TypeReference HttpMessageHandler(ModuleDefinition module) => Type(module, "System.Net.Http", "HttpMessageHandler", "netstandard");

        private static MethodReference Target(MethodDefinition method, int index)
        {
            var instruction = method.Body.Instructions[index];
            Assert.AreEqual(OpCodes.Call, instruction.OpCode, $"instruction {index} should have become a static call");

            return (MethodReference)instruction.Operand;
        }

        private static void AssertInterceptorCall(MethodReference target, string name, params string[] parameterTypes)
        {
            Assert.AreEqual("HttpMonitor.Interceptor", target.DeclaringType.FullName);
            Assert.AreEqual(Weaver.RuntimeAssemblyName, target.DeclaringType.Scope.Name);
            Assert.AreEqual(name, target.Name);
            Assert.IsFalse(target.HasThis);
            Assert.AreEqual(parameterTypes, target.Parameters.Select(p => p.ParameterType.FullName).ToArray());
        }

        private static void AssertWritable(ModuleDefinition module)
        {
            using (var stream = new MemoryStream())
                module.Assembly.Write(stream);
        }

        // ------------------------------------------------------------ SendWebRequest

        [Test]
        public void SendWebRequest_BecomesStaticInterceptorCall_WithSameReturnType()
        {
            var module = NewModule();
            var method = NewMethod(module, "Send", true, UwrType(module));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, SendWebRequest(module));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);

            var weaver = new Weaver();
            Assert.IsTrue(weaver.Weave(module));
            Assert.AreEqual(1, weaver.RewrittenCallSites);

            var target = Target(method, 1);
            AssertInterceptorCall(target, "SendWebRequest", Uwr);
            Assert.AreEqual("UnityEngine.Networking.UnityWebRequestAsyncOperation", target.ReturnType.FullName);
            Assert.AreEqual(OpCodes.Ldarg_0, method.Body.Instructions[0].OpCode, "surrounding instructions untouched");
            Assert.AreEqual(4, method.Body.Instructions.Count, "nothing inserted or removed");
            AssertWritable(module);
        }

        [Test]
        public void PlainCall_NotJustCallvirt_IsAlsoRewritten()
        {
            var module = NewModule();
            var method = NewMethod(module, "Send", true, UwrType(module));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, SendWebRequest(module));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);

            Assert.IsTrue(new Weaver().Weave(module));
            AssertInterceptorCall(Target(method, 1), "SendWebRequest", Uwr);
        }

        // ------------------------------------------------------------ SetRequestHeader

        [Test]
        public void SetRequestHeader_BecomesStaticCall_WithReceiverPrepended()
        {
            var module = NewModule();
            var method = NewMethod(module, "Header", true, UwrType(module));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "X-A");
            il.Emit(OpCodes.Ldstr, "1");
            il.Emit(OpCodes.Callvirt, SetRequestHeader(module));
            il.Emit(OpCodes.Ret);

            Assert.IsTrue(new Weaver().Weave(module));

            var target = Target(method, 3);
            AssertInterceptorCall(target, "SetRequestHeader", Uwr, "System.String", "System.String");
            Assert.AreEqual("System.Void", target.ReturnType.FullName);
        }

        // ------------------------------------------------------------ Dispose

        [Test]
        public void UnityWebRequestDispose_IsRewritten()
        {
            var module = NewModule();
            var method = NewMethod(module, "DisposeIt", true, UwrType(module));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, UwrDispose(module));
            il.Emit(OpCodes.Ret);

            Assert.IsTrue(new Weaver().Weave(module));
            AssertInterceptorCall(Target(method, 1), "Dispose", Uwr);
        }

        [Test]
        public void InterfaceDispose_AfterLoadingAUnityWebRequestLocal_IsRewritten()
        {
            var module = NewModule();
            var method = NewMethod(module, "UsingLocal", true);
            method.Body.Variables.Add(new VariableDefinition(UwrType(module)));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Callvirt, InterfaceDispose(module));
            il.Emit(OpCodes.Ret);

            Assert.IsTrue(new Weaver().Weave(module));
            AssertInterceptorCall(Target(method, 1), "Dispose", Uwr);
        }

        [Test]
        public void InterfaceDispose_AfterLoadingAHoistedField_IsRewritten()
        {
            var module = NewModule();
            var field = new FieldDefinition("<request>5__2", FieldAttributes.Private, UwrType(module));
            Host(module).Fields.Add(field);
            var method = NewMethod(module, "<>m__Finally1", false);
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Callvirt, InterfaceDispose(module));
            il.Emit(OpCodes.Ret);

            Assert.IsTrue(new Weaver().Weave(module));
            AssertInterceptorCall(Target(method, 2), "Dispose", Uwr);
        }

        [Test]
        public void InterfaceDispose_AfterLoadingAnArgument_RespectsThis()
        {
            var module = NewModule();
            var method = NewMethod(module, "Instance", false, UwrType(module));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0); // this (Host), must NOT match
            il.Emit(OpCodes.Callvirt, InterfaceDispose(module));
            il.Emit(OpCodes.Ldarg_1); // the UnityWebRequest parameter, must match
            il.Emit(OpCodes.Callvirt, InterfaceDispose(module));
            il.Emit(OpCodes.Ret);

            var weaver = new Weaver();
            Assert.IsTrue(weaver.Weave(module));
            Assert.AreEqual(1, weaver.RewrittenCallSites);
            Assert.AreEqual(OpCodes.Callvirt, method.Body.Instructions[1].OpCode);
            AssertInterceptorCall(Target(method, 3), "Dispose", Uwr);
        }

        [Test]
        public void InterfaceDispose_OnAnotherType_IsLeftAlone()
        {
            var module = NewModule();
            var method = NewMethod(module, "UsingStream", true);
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Object));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Callvirt, InterfaceDispose(module));
            il.Emit(OpCodes.Ret);

            Assert.IsFalse(new Weaver().Weave(module));
            Assert.AreEqual(OpCodes.Callvirt, method.Body.Instructions[1].OpCode);
            Assert.IsFalse(module.AssemblyReferences.Any(a => a.Name == Weaver.RuntimeAssemblyName), "no reference added when nothing changed");
        }

        // ------------------------------------------------------------ HttpClient

        [Test]
        public void HttpClientConstructors_BecomeFactoryCalls()
        {
            var module = NewModule();
            var handler = HttpMessageHandler(module);
            var method = NewMethod(module, "Clients", true, handler);
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Newobj, HttpClientCtor(module));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, HttpClientCtor(module, handler));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newobj, HttpClientCtor(module, handler, module.TypeSystem.Boolean));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);

            var weaver = new Weaver();
            Assert.IsTrue(weaver.Weave(module));
            Assert.AreEqual(3, weaver.RewrittenCallSites);

            var zero = Target(method, 0);
            AssertInterceptorCall(zero, "CreateHttpClient");
            Assert.AreEqual("System.Net.Http.HttpClient", zero.ReturnType.FullName);

            AssertInterceptorCall(Target(method, 3), "CreateHttpClient", "System.Net.Http.HttpMessageHandler");
            AssertInterceptorCall(Target(method, 7), "CreateHttpClient", "System.Net.Http.HttpMessageHandler", "System.Boolean");
            AssertWritable(module);
        }

        [Test]
        public void UnsupportedHttpClientConstructor_IsLeftAlone()
        {
            var module = NewModule();
            var method = NewMethod(module, "Odd", true);
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldstr, "x");
            il.Emit(OpCodes.Newobj, HttpClientCtor(module, module.TypeSystem.String));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);

            Assert.IsFalse(new Weaver().Weave(module));
            Assert.AreEqual(OpCodes.Newobj, method.Body.Instructions[1].OpCode);
        }

        // ------------------------------------------------------------ bookkeeping

        [Test]
        public void RuntimeAssemblyReference_IsAddedOnce_AcrossManyRewrites()
        {
            var module = NewModule();
            var method = NewMethod(module, "Many", true, UwrType(module));
            var il = method.Body.GetILProcessor();

            for (var i = 0; i < 3; i++)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Callvirt, SendWebRequest(module));
                il.Emit(OpCodes.Pop);
            }

            il.Emit(OpCodes.Ret);

            var weaver = new Weaver();
            Assert.IsTrue(weaver.Weave(module));
            Assert.AreEqual(3, weaver.RewrittenCallSites);
            Assert.AreEqual(3, weaver.Log.Count);
            Assert.AreEqual(1, module.AssemblyReferences.Count(a => a.Name == Weaver.RuntimeAssemblyName));
        }

        [Test]
        public void ExistingRuntimeReference_IsReused()
        {
            var module = NewModule();
            var existing = new AssemblyNameReference(Weaver.RuntimeAssemblyName, new Version(0, 0, 0, 0));
            module.AssemblyReferences.Add(existing);
            var method = NewMethod(module, "Send", true, UwrType(module));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, SendWebRequest(module));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);

            Assert.IsTrue(new Weaver().Weave(module));
            Assert.AreSame(existing, Target(method, 1).DeclaringType.Scope);
        }

        [Test]
        public void DoNotWeaveAttribute_SkipsTheWholeAssembly()
        {
            var module = NewModule();
            var attributeType = Type(module, "HttpMonitor", "DoNotWeaveAttribute", Weaver.RuntimeAssemblyName);
            var ctor = new MethodReference(".ctor", module.TypeSystem.Void, attributeType) { HasThis = true };
            module.Assembly.CustomAttributes.Add(new CustomAttribute(ctor));

            var method = NewMethod(module, "Send", true, UwrType(module));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, SendWebRequest(module));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);

            var weaver = new Weaver();
            Assert.IsFalse(weaver.Weave(module));
            Assert.AreEqual(0, weaver.RewrittenCallSites);
            Assert.AreEqual(OpCodes.Callvirt, method.Body.Instructions[1].OpCode);
            Assert.That(weaver.Log, Has.Some.Contains("DoNotWeave"));
        }

        [Test]
        public void MethodsWithoutBodies_AreSkipped()
        {
            var module = NewModule();
            var abstractMethod = new MethodDefinition("Abstract", MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual, module.TypeSystem.Void);
            Host(module).Attributes |= TypeAttributes.Abstract;
            Host(module).Methods.Add(abstractMethod);

            Assert.IsFalse(new Weaver().Weave(module));
        }
    }
}
