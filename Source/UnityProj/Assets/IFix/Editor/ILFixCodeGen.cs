using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using IFix.Core;
using Unity.Collections.LowLevel.Unsafe;
using ValueType = System.ValueType;
using System.Reflection.Emit;
using UnityEditor;
using Debug = System.Diagnostics.Debug;

public static class OpCodeLookup
{
    // 单字节操作码集合
    public static readonly OpCode[] SingleByteOpCodes;

    // 多字节操作码集合
    public static readonly OpCode[] MultiByteOpCodes;

    // 静态构造函数，用于初始化操作码集合
    static OpCodeLookup()
    {
        // 获取所有操作码
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);

        // 初始化单字节操作码集合
        SingleByteOpCodes = new OpCode[256];
        foreach (var opcode in opcodes)
        {
            var code = (OpCode)opcode.GetValue(null);
            if (code.Size == 1)
            {
                SingleByteOpCodes[code.Value] = code;
            }
        }

        // 初始化多字节操作码集合
        MultiByteOpCodes = new OpCode[256];
        foreach (var opcode in opcodes)
        {
            var code = (OpCode)opcode.GetValue(null);
            if (code.Size == 2)
            {
                MultiByteOpCodes[code.Value & 0xff] = code;
            }
        }
    }
}

namespace IFix.Editor
{
    public class ILFixCodeGen
    {
        #region menu
        
        [MenuItem("InjectFix/GenBinding", false, 2)]
        public static void GenBinding()
        {
            ILFixCodeGen gen = new ILFixCodeGen();
            gen.path = "Assets/IFix/Binding/";
            gen.methodCount = 0;
            gen.ClearMethod();
            gen.DeleteAll();
            // custom type
            //gen.Generate(typeof(List<int>));
            //gen.Generate(typeof(string));

            // search by dll caller
            List<MethodBase> mbList = new List<MethodBase>();
            var asses = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in IFixEditor.injectAssemblys)
            {
                foreach (var ass in asses)
                {
                    if (!(ass.ManifestModule is System.Reflection.Emit.ModuleBuilder)
                        && (ass.GetName().Name == assembly))
                    {
                        UnityEngine.Debug.Log( string.Format("Scan {0} assembly", ass.GetName().Name));
                        mbList.AddRange(FindAllMethod(ass));
                    }
                }
            }
            
            gen.GenAll(mbList);
        }
        
        public static List<MethodBase> FindAllMethod(Assembly assembly)
        {
            HashSet<MethodBase> result = new HashSet<MethodBase>();
            Module[] modules = assembly.GetModules();
            Module mainModule = modules[0];
            // 遍历程序集中的所有类型
            foreach (var type in assembly.GetTypes())
            {
                if (type.FullName == "IFix.ILFixDynamicMethodWrapper")
                {
                    throw new Exception("You can't gen binding when dll is injected");
                }

                if (type.IsSubclassOf(typeof(Delegate)))
                    continue;
                // 遍历类型中的所有方法
                var methods = type.GetMethods(BindingFlags.Public 
                                              | BindingFlags.DeclaredOnly | BindingFlags.NonPublic 
                                              | BindingFlags.Instance | BindingFlags.Static);

                var constructors = type.GetConstructors(BindingFlags.Public
                                     | BindingFlags.DeclaredOnly | BindingFlags.NonPublic
                                     | BindingFlags.Instance | BindingFlags.Static);

                List<MethodBase> mbList = new List<MethodBase>();
                mbList.AddRange(methods);
                mbList.AddRange(constructors);
                
                foreach (var methodInfo in mbList)
                {
                    if (methodInfo == null) continue;
                    if(methodInfo.ReflectedType == null) continue;
                    if (!string.IsNullOrEmpty(methodInfo.ReflectedType.Namespace))
                    {
                        if(methodInfo.ReflectedType.Namespace == "IFix.Core") continue;
                        if(methodInfo.ReflectedType.Namespace == "IFix.Binding") continue;
                        if(methodInfo.ReflectedType.Namespace.Contains("UnityEditor")) continue;
                    }
                    // 输出方法的名称


                    // 获取方法体
                    var methodBody = methodInfo.GetMethodBody();

                    // 检查方法是否有方法体
                    if (methodBody != null)
                    {
                        // 获取方法体中的 IL 字节码
                        var ilBytes = methodBody.GetILAsByteArray();

                        // 创建一个指向 IL 字节码的索引
                        int ilIndex = 0;

                        // 遍历 IL 字节码
                        while (ilIndex < ilBytes.Length)
                        {
                            // 从 IL 字节码中读取操作码
                            var opcodeValue = ilBytes[ilIndex++];
                            var opcode = OpCodes.Nop; // 默认为 Nop，以防止未知操作码
                            if (opcodeValue != 0xFE) // 如果操作码的值不在 0xFE 开头的范围内
                            {
                                opcode = OpCodeLookup.SingleByteOpCodes[opcodeValue]; // 使用单字节操作码查找表
                            }
                            else // 如果操作码的值在 0xFE 开头的范围内
                            {
                                opcodeValue = ilBytes[ilIndex++];
                                opcode = OpCodeLookup.MultiByteOpCodes[opcodeValue]; // 使用多字节操作码查找表
                            }

                            // 检查是否是方法调用指令
                            if (opcode == OpCodes.Call || opcode == OpCodes.Callvirt || opcode == OpCodes.Newobj)
                            {
                                // 读取调用的方法的元数据标记
                                int metadataToken = BitConverter.ToInt32(ilBytes, ilIndex);
                                //ilIndex += 4;

                                // 解析元数据标记获取调用的方法信息
                                try
                                {
                                    var calledMethod = mainModule.ResolveMethod(metadataToken);
                                    if (!result.Contains(calledMethod))
                                    {
                                        if (calledMethod is ConstructorInfo)
                                        {
                                            // 构造函数暂时不处理
                                            // if (!calledMethod.ReflectedType.IsValueType)
                                            // {
                                            //     //UnityEngine.Debug.Log($"class ctor:{calledMethod.ReflectedType}, {calledMethod.Name}");
                                            // }
                                            // else if(calledMethod.IsPublic)
                                            // {
                                            //     result.Add(calledMethod);
                                            // }

                                        }
                                        else
                                        {
                                            // IFix.Core空间下不导出
                                            if (calledMethod.ReflectedType.FullName.Contains("IFix.Core"))
                                            {
                                            }
                                            else
                                            {
                                                result.Add(calledMethod);
                                            }
                                        }
                                   
                                        // 输出调用的方法信息
                                        //UnityEngine.Debug.Log($"    调用方法: {ILFixCodeGen.GetUniqueStringForMethod(calledMethod)}");
                                    }
                                }
                                catch //(Exception e)
                                {
                                    //UnityEngine.Debug.LogError(e);
                                }

                            }

                            // 检查是否有操作数，并根据操作码的操作数类型移动索引
                            switch (opcode.OperandType)
                            {
                                case OperandType.InlineBrTarget:
                                case OperandType.InlineField:
                                case OperandType.InlineI:
                                case OperandType.InlineI8:
                                case OperandType.InlineMethod:
                                case OperandType.InlineR:
                                case OperandType.InlineSig:
                                case OperandType.InlineString:
                                case OperandType.InlineSwitch:
                                case OperandType.InlineTok:
                                case OperandType.InlineType:
                                case OperandType.InlineVar:
                                    ilIndex += 4;
                                    break;
                                case OperandType.ShortInlineBrTarget:
                                case OperandType.ShortInlineI:
                                case OperandType.ShortInlineR:
                                case OperandType.ShortInlineVar:
                                    ilIndex += 1;
                                    break;
                            }
                        }
                    }
                }
            }

            return result.ToList();
        }
        
        #endregion
        
        #region static

        public Dictionary<string, Tuple<int, string>> delegateDict = new Dictionary<string, Tuple<int, string>>();
        public Dictionary<string, int> ctorCache = new Dictionary<string, int>();
        public Dictionary<string, int> publicInstanceStructCache = new Dictionary<string, int>();

        public void ClearMethod()
        {
            delegateDict.Clear();
            ctorCache.Clear();
            publicInstanceStructCache.Clear();
        }

        public bool TryGetDelegateStr(MethodInfo mi, out string ret)
        {
            bool result = true;
            var key = TypeNameUtils.GetMethodDelegateKey(mi);

            if (string.IsNullOrEmpty(key))
            {
                ret = "";
                return true;
            }

            if (key.Contains("!"))
            {
                ret = "";
                return true;
            }

            if (key.Contains(">d__"))
            {
                ret = "";
                return true;
            }

            if (key.Contains("<>"))
            {
                ret = "";
                return true;
            }

            if (!delegateDict.ContainsKey(key))
            {
                result = false;
                ret = MethodInfoToDelegate(mi);
                delegateDict.Add(key, new Tuple<int, string>(methodCount, ret));
            }
            else
            {
                ret = delegateDict[key].Item2;
            }

            return result;
        }

        #endregion

        #region cal name

        public string MethodInfoToDelegate(MethodInfo method)
        {
            List<string> args = new List<string>();
            var parameters = method.GetParameters();

            for (int i = 0, imax = parameters.Length; i < imax; i++)
            {
                var p = parameters[i];
                if (p.ParameterType.IsByRef && p.IsOut)
                    args.Add("out " + TypeNameUtils.SimpleType(p.ParameterType) + string.Format(" arg{0}", i + 1));
                else if (p.ParameterType.IsByRef)
                    args.Add("ref " + TypeNameUtils.SimpleType(p.ParameterType) + string.Format(" arg{0}", i + 1));
                else
                    args.Add(TypeNameUtils.SimpleType(p.ParameterType) + string.Format(" arg{0}", i + 1));
            }

            var parameterTypeNames = string.Join(",", args);

            Type retType = method.ReturnType;

            return string.Format("public delegate {0} IFixCallDel{2}({1});", TypeNameUtils.SimpleType(retType),
                parameterTypeNames, methodCount);
        }

        #endregion

        #region type

        static MethodInfo tryFixGenericMethod(MethodInfo method)
        {
            if (!method.ContainsGenericParameters)
                return method;

            try
            {
                Type[] genericTypes = method.GetGenericArguments();
                for (int j = 0; j < genericTypes.Length; j++)
                {
                    Type[] contraints = genericTypes[j].GetGenericParameterConstraints();
                    if (contraints != null && contraints.Length == 1 && contraints[0] != typeof(ValueType))
                        genericTypes[j] = contraints[0];
                    else
                        return method;
                }

                // only fixed here
                return method.MakeGenericMethod(genericTypes);
            }
            catch (Exception e)
            {
                //Debug.LogError(e);
            }

            return method;
        }

        ConstructorInfo[] GetValidConstructor(Type t)
        {
            List<ConstructorInfo> ret = new List<ConstructorInfo>();
            if (t.GetConstructor(Type.EmptyTypes) == null && t.IsAbstract && t.IsSealed)
                return ret.ToArray();
            if (t.IsAbstract)
                return ret.ToArray();
            if (t.BaseType != null && t.BaseType.Name == "MonoBehaviour")
                return ret.ToArray();

            ConstructorInfo[] cons = t.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public
            );
            foreach (ConstructorInfo ci in cons)
            {
                ret.Add(ci);
            }

            return ret.ToArray();
        }

        MethodInfo[] GetValidMethodInfo(Type t)
        {
            List<MethodInfo> methods = new List<MethodInfo>();

            BindingFlags bf = BindingFlags.Public
                              | BindingFlags.DeclaredOnly | BindingFlags.NonPublic
                              | BindingFlags.Instance | BindingFlags.Static;

            MethodInfo[] members = t.GetMethods(bf);
            foreach (MethodInfo mi in members)
            {
                if (mi.ReturnType.IsNotPublic) continue;
                bool hasPrivateType = false;
                foreach (var item in mi.GetParameters())
                {
                    if (item.ParameterType.IsNotPublic)
                    {
                        hasPrivateType = true;
                        break;
                    }
                }

                if (hasPrivateType) continue;
                methods.Add(tryFixGenericMethod(mi));
            }

            return methods.ToArray();
        }

        bool IsBaseType(Type t)
        {
            return t.IsPrimitive;
        }

        bool IsValueType(Type t)
        {
            if (t.IsByRef) t = t.GetElementType();
            return t.BaseType == typeof(ValueType) && !IsBaseType(t);
        }

        bool IsOutArg(ParameterInfo p)
        {
            return (p.IsOut || p.IsDefined(typeof(System.Runtime.InteropServices.OutAttribute), false)) &&
                   !p.ParameterType.IsArray;
        }

        string FuncCall(MethodBase m, int parOffset = 0)
        {
            string str = "";
            ParameterInfo[] pars = m.GetParameters();

            for (int n = 0; n < pars.Length; n++)
            {
                ParameterInfo p = pars[n];
                int idx = n + 1;
                if (p.ParameterType.IsByRef && p.IsOut)
                    str += string.Format("out arg{0}", idx);
                else if (p.ParameterType.IsByRef)
                    str += string.Format("ref arg{0}", idx);
                else
                    str += string.Format("arg{0}", idx);
                if (n < pars.Length - 1)
                    str += ",";
            }

            return str;
        }

        string FuncGetArg(ParameterInfo p)
        {
            Type t = p.ParameterType;
            return string.Format("({0})", TypeNameUtils.SimpleType(t));
        }

        string FuncPushResult(Type t)
        {
            if (t.IsPrimitive)
            {
                if (t == typeof(int))
                {
                    return "call.PushInt32AsResult(result);";
                }
                else if (t == typeof(uint))
                {
                    return "call.PushUInt32AsResult(result);";
                }
                else if (t == typeof(float))
                {
                    return "call.PushSingleAsResult(result);";
                }
                else if (t == typeof(bool))
                {
                    return "call.PushBooleanAsResult(result);";
                }
                else if (t == typeof(double))
                {
                    return "call.PushDoubleAsResult(result);";
                }
                else if (t == typeof(long))
                {
                    return "call.PushInt64AsResult(result);";
                }
                else if (t == typeof(byte))
                {
                    return "call.PushByteAsResult(result);";
                }
                else if (t == typeof(ushort))
                {
                    return "call.PushUInt16AsResult(result);";
                }
                else if (t == typeof(short))
                {
                    return "call.PushInt16AsResult(result);";
                }
                else if (t == typeof(char))
                {
                    return "call.PushCharAsResult(result);";
                }
                else if (t == typeof(ulong))
                {
                    return "call.PushUInt64AsResult(result);";
                }
                else if (t == typeof(sbyte))
                {
                    return "call.PushSByteAsResult(result);";
                }
                else if (t == typeof(IntPtr))
                {
                    return "call.PushIntPtr64AsResult(result);";
                }
                else if (t == typeof(UIntPtr))
                {
                    return "call.PushUIntPtr64AsResult(result);";
                }
            }
            else if (t.IsPointer)
            {
                return "call.PushIntPtr64AsResult((IntPtr)result);";
            }
            else if (t.IsEnum)
            {
                var underlyingType = Enum.GetUnderlyingType(t);
                if (underlyingType == typeof(long) || underlyingType == typeof(ulong))
                {
                    return "call.PushInt64AsResult((long)result);";
                }
                else
                {
                    return "call.PushInt32AsResult((int)result);";
                }
            }
            else if (t.IsValueType)
            {
                return "call.PushValueUnmanagedAsResult(result);";
            }

            return $"call.PushObjectAsResult(result, typeof({TypeNameUtils.SimpleType(t)}));";
        }

        string GetArg(Type t, int n)
        {
            if (t.IsPrimitive)
            {
                if (t == typeof(int))
                {
                    return $"var arg{n} = (curArgument++)->Value1;";
                }
                else if (t == typeof(uint))
                {
                    return $"var arg{n} = (uint)(curArgument++)->Value1;";
                }
                else if (t == typeof(float))
                {
                    return $"var arg{n} = (float)(curArgument++)->Value1;";
                }
                else if (t == typeof(ushort))
                {
                    return $"var arg{n} = (ushort)(curArgument++)->Value1;";
                }
                else if (t == typeof(short))
                {
                    return $"var arg{n} = (short)(curArgument++)->Value1;";
                }
                else if (t == typeof(char))
                {
                    return $"var arg{n} = (char)(curArgument++)->Value1;";
                }
                else if (t == typeof(bool))
                {
                    return $"var arg{n} = (curArgument++)->Value1 == 0;";
                }
                else if (t == typeof(byte))
                {
                    return $"var arg{n} = (byte)(curArgument++)->Value1;";
                }
                else if (t == typeof(sbyte))
                {
                    return $"var arg{n} = (sbyte)(curArgument++)->Value1;";
                }
                else if (t == typeof(double))
                {
                    return $"var arg{n} = *(double*)&(curArgument++)->Value1;";
                }
                else if (t == typeof(long))
                {
                    return $"var arg{n} = *(long*)&(curArgument++)->Value1;";
                }
                else if (t == typeof(ulong))
                {
                    return $"var arg{n} = *(ulong*)&(curArgument++)->Value1;";
                }
                else if (t == typeof(IntPtr))
                {
                    return $"var arg{n} = (IntPtr)(*(long*)&(curArgument++)->Value1);";
                }
                else if (t == typeof(UIntPtr))
                {
                    return $"var arg{n} = (UIntPtr)(*(long*)&(curArgument++)->Value1);";
                }
            }
            else if (t.IsPointer)
            {
                return $"var arg{n} = ({TypeNameUtils.SimpleType(t)})((IntPtr)(*(long*)&(curArgument++)->Value1)).ToPointer();";
            }
            else if (t.IsEnum)
            {
                var underlyingType = Enum.GetUnderlyingType(t);
                if (underlyingType == typeof(long) || underlyingType == typeof(ulong))
                {
                    return $"var arg{n} = ({TypeNameUtils.SimpleType(t)})(*(long*)&(curArgument++)->Value1);";
                }
                else
                {
                    return $"var arg{n} = ({TypeNameUtils.SimpleType(t)})(curArgument++)->Value1;";
                }
            }
            // else if (t.IsValueType)
            // {
            //     
            // }

            return $"var arg{n} = ({TypeNameUtils.SimpleType(t)})managedStack[(curArgument++)->Value1];";
        }


        string ToArgRef(int n)
        {
            return $"Value* arg{n}Ref = EvaluationStackOperation.ToBaseRef(curArgument++);";
        }

        string UpdateArgRef(Type t, int n, StreamWriter file)
        {
            string ret = "";
            if (t.IsPrimitive)
            {
                if (t == typeof(int))
                {
                    return $"arg{n}Ref->Value1 = arg{n};";
                }
                else if (t == typeof(uint))
                {
                    return $"arg{n}Ref->Value1 = (int)arg{n};";
                }
                else if (t == typeof(float))
                {
                    return $"*(float*)(&arg{n}Ref->Value1) = arg{n};";
                }
                else if (t == typeof(ushort))
                {
                    return $"*(ushort*)(&arg{n}Ref->Value1) = arg{n};";
                }
                else if (t == typeof(short))
                {
                    return $"*(short*)(&arg{n}Ref->Value1) = arg{n};";
                }
                else if (t == typeof(char))
                {
                    return $"*(char*)(&arg{n}Ref->Value1) = arg{n};";
                }
                else if (t == typeof(bool))
                {
                    return $"arg{n}Ref->Value1 = arg{n} ? 1 : 0;";
                }
                else if (t == typeof(byte))
                {
                    return $"*(byte*)(&arg{n}Ref->Value1) = arg{n};";
                }
                else if (t == typeof(sbyte))
                {
                    return $"*(sbyte*)(&arg{n}Ref->Value1) = arg{n};";
                }
                else if (t == typeof(double))
                {
                    return $"*(double*)(&arg{n}Ref->Value1) = arg{n};";
                }
                else if (t == typeof(long))
                {
                    return $"*(long*)(&arg{n}Ref->Value1) = arg{n};";
                }
                else if (t == typeof(ulong))
                {
                    return $"*(ulong*)(&arg{n}Ref->Value1) = arg{n};";
                }
                else if (t == typeof(IntPtr))
                {
                    return $"*(long*)(&arg{n}Ref->Value1) = arg{n}.ToInt64();";
                }
                else if (t == typeof(UIntPtr))
                {
                    return $"*(ulong*)(&arg{n}Ref->Value1) = arg{n}.ToUInt64();";
                }
            }
            else if (t.IsPointer)
            {
                return $"*(long*)(&arg{n}Ref->Value1) = (long)arg{n};";
            }
            else if (t.IsEnum)
            {
                var underlyingType = Enum.GetUnderlyingType(t);
                if (underlyingType == typeof(long) || underlyingType == typeof(ulong))
                {
                    return $"*(long*)(&arg{n}Ref->Value1) = (long)arg{n};";
                }
                else
                {
                    return $"arg{n}Ref->Value1 = (int)arg{n};";
                }
            }
            else
            {
                if (t.IsValueType)
                {
                    Write(file, $"BoxUtils.RecycleObject(managedStack[arg{n}Ref->Value1]);");
                }

                ret = $"managedStack[arg{n}Ref->Value1] = BoxUtils.BoxObject(arg{n});";
            }

            return ret;
        }

        string GetRefArg(Type t, int n)
        {
            if (t.IsPrimitive)
            {
                if (t == typeof(int))
                {
                    return $"var arg{n} = arg{n}Ref->Value1;";
                }
                else if (t == typeof(uint))
                {
                    return $"var arg{n} = (uint)arg{n}Ref->Value1;";
                }
                else if (t == typeof(float))
                {
                    return $"var arg{n} = (float)arg{n}Ref->Value1;";
                }
                else if (t == typeof(ushort))
                {
                    return $"var arg{n} = (ushort)arg{n}Ref->Value1;";
                }
                else if (t == typeof(short))
                {
                    return $"var arg{n} = (short)arg{n}Ref->Value1;";
                }
                else if (t == typeof(char))
                {
                    return $"var arg{n} = (char)arg{n}Ref->Value1;";
                }
                else if (t == typeof(bool))
                {
                    return $"var arg{n} = arg{n}Ref->Value1 == 0;";
                }
                else if (t == typeof(byte))
                {
                    return $"var arg{n} = (byte)arg{n}Ref->Value1;";
                }
                else if (t == typeof(sbyte))
                {
                    return $"var arg{n} = (sbyte)arg{n}Ref->Value1;";
                }
                else if (t == typeof(double))
                {
                    return $"var arg{n} = *(double*)&arg{n}Ref->Value1;";
                }
                else if (t == typeof(long))
                {
                    return $"var arg{n} = *(long*)&arg{n}Ref->Value1;";
                }
                else if (t == typeof(ulong))
                {
                    return $"var arg{n} = *(ulong*)&arg{n}Ref->Value1;";
                }
                else if (t == typeof(IntPtr))
                {
                    return $"var arg{n} = (IntPtr)(*(long*)&arg{n}Ref->Value1);";
                }
                else if (t == typeof(UIntPtr))
                {
                    return $"var arg{n} = (UIntPtr)(*(long*)&arg{n}Ref->Value1);";
                }
            }
            else if (t.IsPointer)
            {
                return $"var arg{n} = ({TypeNameUtils.SimpleType(t)})((IntPtr)(*(long*)&arg{n}Ref->Value1)).ToPointer();";
            }
            else if (t.IsEnum)
            {
                var underlyingType = Enum.GetUnderlyingType(t);
                if (underlyingType == typeof(long) || underlyingType == typeof(ulong))
                {
                    return $"var arg{n} = ({TypeNameUtils.SimpleType(t)})(*(long*)&arg{n}Ref->Value1);";
                }
                else
                {
                    return $"var arg{n} = ({TypeNameUtils.SimpleType(t)})arg{n}Ref->Value1;";
                }
            }

            return $"var arg{n} = ({TypeNameUtils.SimpleType(t)})managedStack[arg{n}Ref->Value1];";
        }
        
        
        #endregion

        #region write

        void Write(StreamWriter file, string fmt, params object[] args)
        {
            fmt = Regex.Replace(fmt, @"\r\n?|\n|\r", NewLine);

            if (fmt.StartsWith("}")) indent--;

            for (int n = 0; n < indent; n++)
                file.Write("\t");


            if (args.Length == 0)
                file.WriteLine(fmt);
            else
            {
                string line = string.Format(fmt, args);
                file.WriteLine(line);
            }

            if (fmt.EndsWith("{")) indent++;
        }

        private void WriteHead(StreamWriter file)
        {
            Write(file, "using System;");
            Write(file, "using IFix.Core;");
            Write(file, "using System.Collections.Generic;");
            Write(file, "using System.Reflection;");
            Write(file, "#if UNITY_5_5_OR_NEWER");
            Write(file, "using UnityEngine.Profiling;");
            Write(file, "#else");
            Write(file, "using UnityEngine;");
            Write(file, "#endif");

            Write(file, "namespace IFix.Core");
            Write(file, "{");
            Write(file, "public unsafe partial class IFixBindingCaller");
            Write(file, "{");
            Write(file, "");
        }

        private void End(StreamWriter file)
        {
            Write(file, "}");
            Write(file, "}");
            file.Flush();
            file.Close();

            GenFileDictionary();
        }

        private void GenFileDictionary()
        {
             string bindingDelDictPath = path + "/Resources/BindingDelDict.bytes";
             FileDictionary<string, int> file =
                 new FileDictionary<string, int>(bindingDelDictPath, 1024, true);
            
             foreach (var item in delegateDict)
             {
                 file.Add(item.Key, item.Value.Item1);
             }

            file.Close();
        }

        void WriteTry(StreamWriter file)
        {
            Write(file, "try");
            Write(file, "{");
            Write(file, "#if DEBUG");
            Write(file, "Profiler.BeginSample(methodName);");
            Write(file, "#endif");
        }

        void WriteFinaly(StreamWriter file, int paramStart, int paramCount)
        {
            Write(file, "}");
            Write(file, "finally");
            Write(file, "{");
            Write(file, "#if DEBUG");
            Write(file, "Profiler.EndSample();");
            Write(file, "#endif");
            Write(file, $"PopEvaluationStack(ref call, pushResult, {paramStart}, {paramCount});");
            Write(file, "}");
        }

        private bool WriteMethodCaller(MethodBase mb, StreamWriter file)
        {
            var key = TypeNameUtils.GetMethodDelegateKey(mb);
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (key.Contains("!"))
            {
                return false;
            }

            if (key.Contains(">d__"))
            {
                return false;
            }

            if (key.Contains("<>"))
            {
                return false;
            }

            // class method
            if (mb is MethodInfo)
            {
                string delegateStr;
                if (TryGetDelegateStr((MethodInfo)mb, out delegateStr))
                {
                    return false;
                }

                Write(file, delegateStr);
            }
            else
            {
                return false;
            }
            
            Write(file, "");
            Write(file, "public void Invoke{0}(VirtualMachine vm, ref Call call, bool instantiate) {{", methodCount);
            Write(file, "var curArgument = hasThis ? call.argumentBase + 1 : call.argumentBase;");
            Write(file, "var managedStack = call.managedStack;");
            ParameterInfo[] pars = mb.GetParameters();
            var mi = mb as MethodInfo;
            
            for (int n = 0; n < pars.Length; n++)
            {
                var p = pars[n];
                int idx = n + 1;
                if (!p.IsOut && !p.ParameterType.IsByRef)
                {
                    Write(file, GetArg(p.ParameterType, idx));
                }
                else
                {
                    Write(file, ToArgRef(idx));
                    Write(file, GetRefArg(p.ParameterType.GetElementType(), idx));
                }
            }

            if (mi.ReturnType != typeof(void))
            {
                Write(file, "var result = ((IFixCallDel{1})caller)({0});", FuncCall(mi), methodCount);
                Write(file, FuncPushResult(mi.ReturnType));
            }
            else
            {
                Write(file, "((IFixCallDel{1})caller)({0});", FuncCall(mi), methodCount);
            }


            for (int n = 0; n < pars.Length; n++)
            {
                var p = pars[n];
                var idx = n + 1;
                if (p.ParameterType.IsByRef)
                {
                    string updateStr = UpdateArgRef(p.ParameterType.GetElementType(), idx, file);
                    Write(file, updateStr);
                }
            }

            Write(file, "}");
            Write(file, "");
            return true;
        }

        #endregion

        #region public

        public void DeleteAll()
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
                {
                    File.Delete(file);
                }
            }
        }

        public void GenAll(List<MethodBase> mbList)
        {
            var file = Begin();
            WriteHead(file);

            for (int i = 0, imax = mbList.Count; i < imax; i++)
            {
                if (WriteMethodCaller(mbList[i], file))
                    methodCount++;
            }

            End(file);
        }

        #endregion

        StreamWriter Begin()
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            if (!Directory.Exists(path + "/Resources"))
            {
                Directory.CreateDirectory(path + "/Resources");
            }

            string bindingFile = path + "IFixBindingCaller.cs";

            StreamWriter file = new StreamWriter(bindingFile, false, Encoding.UTF8);
            file.NewLine = NewLine;
            return file;
        }

        #region property

        public EOL eol = EOL.CRLF;

        public enum EOL
        {
            Native,
            CRLF,
            CR,
            LF,
        }

        string NewLine
        {
            get
            {
                switch (eol)
                {
                    case EOL.Native:
                        return System.Environment.NewLine;
                    case EOL.CRLF:
                        return "\r\n";
                    case EOL.CR:
                        return "\r";
                    case EOL.LF:
                        return "\n";
                    default:
                        return "";
                }
            }
        }

        #endregion

        #region member

        public string path;
        public int methodCount = 0;
        int indent = 0;

        #endregion
    }
}