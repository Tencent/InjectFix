using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using IFix.Utils;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.UI;

namespace IFix.Editor
{
    public class ILFixCodeGen
    {
        #region static

        public Dictionary<string, Tuple<int, string>> delegateDict = new Dictionary<string, Tuple<int, string>>();
        public Dictionary<string, Tuple<int, string>> ctorCache = new Dictionary<string, Tuple<int, string>>();
        public void ClearMethod()
        {
            delegateDict.Clear();
            ctorCache.Clear();
        }

        public bool TryGetDelegateStr(MethodInfo mi, out string ret)
        {
            bool result = true;
            var key = TypeNameUtils.GetUniqueMethodName(mi);
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
        public string MethodInfoToDelegate(MethodBase method)
        {
            List<string> args = new List<string>();
            var parameters = method.GetParameters();
            if (!method.IsStatic && !(method is ConstructorInfo))
            {
                args.Add( TypeNameUtils.SimpleType(method.ReflectedType) +" arg0");
            }

            for (int i = 0, imax = parameters.Length; i<imax; i++)
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

            Type retType = (method is MethodInfo)
                ? (method as MethodInfo).ReturnType
                : (method as ConstructorInfo).ReflectedType;
            
            return string.Format("delegate {0} IFixCallDel{2}({1});", TypeNameUtils.SimpleType(retType), parameterTypeNames, methodCount);
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
                Debug.LogError(e);
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
                if(mi.ReturnType.IsNotPublic) continue;
                bool hasPrivateType = false;
                foreach (var item in mi.GetParameters())
                {
                    if (item.ParameterType.IsNotPublic)
                    {
                        hasPrivateType = true;
                        break;
                    }
                }
                if(hasPrivateType)continue;
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
            if (!m.IsStatic && !(m is ConstructorInfo))
            {
                str += "a0";
                if (pars.Length > 0) str += ",";
            }

            for (int n = parOffset; n < pars.Length; n++)
            {
                ParameterInfo p = pars[n];
                int idx = (!m.IsStatic && !(m is ConstructorInfo)) ? n + 1 : n;
                if (p.ParameterType.IsByRef && p.IsOut)
                    str += string.Format("out a{0}", idx);
                else if (p.ParameterType.IsByRef)
                    str += string.Format("ref a{0}", idx);
                else
                    str += string.Format("a{0}", idx);
                if (n < pars.Length - 1)
                    str += ",";
            }

            return str;
        }

        string FuncGetArg(ParameterInfo p)
        {
            Type t = p.ParameterType;

            if (t == typeof(int))
            {
                return "call.GetInt32";
            }
            if (t == typeof(float))
            {
                return "call.GetSingle";
            }
            if (t == typeof(bool))
            {
                return "call.GetBoolean";
            }
            if (t == typeof(double))
            {
                return "call.GetDouble";
            }
            if (t == typeof(long))
            {
                return "call.GetInt64";
            }
            if (t == typeof(byte))
            {
                return "call.GetByte";
            }
            if (t == typeof(uint))
            {
                return "call.GetUInt32";
            }
            if (t == typeof(ushort))
            {
                return "call.GetUInt16";
            }
            if (t == typeof(short))
            {
                return "call.GetInt16";
            }
            if (t == typeof(char))
            {
                return "call.GetChar";
            }
            if (t == typeof(ulong))
            {
                return "call.GetUInt64";
            }
            if (t == typeof(sbyte))
            {
                return "call.GetSByte";
            }
            if (t == typeof(IntPtr))
            {
                return "call.GetIntPtr";
            }
            if (t == typeof(UIntPtr))
            {
                return "call.GetUIntPtr";
            }
    
            if (t.IsEnum)
            {
                var underlyingType = Enum.GetUnderlyingType(t);
                if (underlyingType == typeof(long) || underlyingType == typeof(ulong))
                {
                    return "call.GetInt64";
                }
                else
                {
                    return "call.GetInt32";
                }
            }
            else if (t.IsPointer)
            {
                var originType = t.GetElementType();
                if (originType == typeof(long) || originType == typeof(ulong))
                {
                    return "call.GetInt64Point";
                }
                else
                {
                    return "call.GetInt32Point";
                }
            }
            return "call.GetObject";
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
            else if (t.IsPointer)
            {
                return "call.PushIntPtr64AsResult((IntPtr)result);";
            }
            else if(UnsafeUtility.IsUnmanaged(t))
            {
                return "call.PushValueUnmanagedAsResult(result);";
            }
            return "call.PushObjectAsResult(result, result.GetType());";
        }

        
        #endregion

        #region write

        void Write(StreamWriter file, string fmt, params object[] args)
        {
            fmt = Regex.Replace(fmt, @"\r\n?|\n|\r", NewLine);

            if (fmt.StartsWith("}")) indent--;

            for (int n = 0; n < indent; n++)
                file.Write("    ");


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
            Write(file, "using IFix.Utils;");
            Write(file, "#if UNITY_5_5_OR_NEWER");
            Write(file, "using UnityEngine.Profiling;");
            Write(file, "#else");
            Write(file, "using UnityEngine;");
            Write(file, "#endif");

            Write(file, "namespace IFix.Binding");
            Write(file, "{");
            Write(file, "public unsafe class IFixBindingCaller");
            Write(file, "{");
            Write(file, "public ExternInvoker Invoke = null;");
            Write(file, "private MethodBase method;");
            Write(file, "private Delegate caller = null;");
        }

        private void End(StreamWriter file)
        {
            Write(file, "public IFixBindingCaller(MethodBase method, out bool isSuccess)");
            Write(file, "{");
            Write(file, "this.method = method;");
            Write(file, "isSuccess = false;");
            Write(file, "object methodUniqueStr = string.Intern(TypeNameUtils.GetUniqueMethodName(method));");
            Write(file, "");
            foreach (var item in delegateDict)
            {
                Write(file, $"if (methodUniqueStr == \"{item.Key}\")");
                Write(file, "{");
                Write(file, $"Invoke = Invoke{item.Value.Item1};");
                Write(file, "isSuccess = true;");
                Write(file, "return;");
                Write(file, "}");
            }

            Write(file, "");
            foreach (var item in ctorCache)
            {
                Write(file, $"if (methodUniqueStr == (object)\"{item.Key}\")");
                Write(file, "{");
                Write(file, $"Invoke = Invoke{item.Value.Item1};");
                Write(file, "isSuccess = true;");
                Write(file, "return;");
                Write(file, "}");
            }

            Write(file, "}");

            Write(file, "}");
            Write(file, "}");
            file.Flush();
            file.Close();
        }

        void WriteTry(StreamWriter file)
        {
            Write(file, "try");
            Write(file, "{");
            Write(file, "#if DEBUG");
            Write(file, "Profiler.BeginSample(method.Name);");
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
            
            Write(file, "Value* pArg = call.argumentBase;");
            Write(file, "if (pushResult) pArg++;");
            Write(file, "var managedStack = call.managedStack;");
            Write(file, $"for (int i = {paramStart}; i < {paramCount}; i++)");
            Write(file, "{");
            Write(file, "EvaluationStackOperation.RecycleObject(managedStack[pArg - call.evaluationStackBase]);");
            Write(file, "managedStack[pArg - call.evaluationStackBase] = null;");
            Write(file, "pArg++;");
            Write(file, "}");

            Write(file, "}");
        }

        private void WriteMethodCaller(MethodBase mb, StreamWriter file)
        {
            if (mb is MethodInfo)
            {
                string delegateStr;
                if (TryGetDelegateStr((MethodInfo)mb, out delegateStr))
                {
                    return;
                }
                Write(file, delegateStr);
            }
            else if (mb is ConstructorInfo)
            {
                var cr = TypeNameUtils.GetUniqueMethodName(mb);
                if (!ctorCache.ContainsKey(cr))
                {
                    ctorCache.Add(cr, new Tuple<int, string>(methodCount, MethodInfoToDelegate(mb)));
                }
            }

            Write(file, "");
            Write(file, "public void Invoke{0}(VirtualMachine vm, ref Call call, bool instantiate) {{", methodCount);

            bool pushResult = mb is ConstructorInfo ||
                              (mb is MethodInfo && ((MethodInfo)mb).ReturnType != typeof(void));

            int paramStart = 0;
            if (pushResult)
            {
                paramStart = 1;
            }

            int paramCount = mb.GetParameters().Length;
            if (mb is MethodInfo && !((MethodInfo)mb).IsStatic)
            {
                paramCount += 1;
            }

            string pushResultStr = pushResult ? "true" : "false";

            Write(file, $"bool pushResult = {pushResultStr};");
            WriteTry(file);

            ParameterInfo[] pars = mb.GetParameters();
            if (mb is ConstructorInfo)
            {
                Write(file, "{0} result;", TypeNameUtils.SimpleType(mb.ReflectedType));
                for (int n = 0; n < pars.Length; n++)
                {
                    var p = pars[n];
                    Write(file, "var a{0} = ({1}){3}({2});", n, TypeNameUtils.SimpleType(p.ParameterType) ,n, FuncGetArg(p));
                }
                Write(file, "result = new {0}({1});", TypeNameUtils.SimpleType(mb.ReflectedType), FuncCall(mb));
                if (UnsafeUtility.IsUnmanaged(mb.ReflectedType))
                {
                    Write(file, "call.PushValueUnmanagedAsResult(result);");
                }
                else
                {
                    Write(file, "call.PushObjectAsResult(result, result.GetType());");
                }


            }
            else
            {
                var mi = mb as MethodInfo;
                if (mi.ReturnType != typeof(void))
                {
                    Write(file, "{0} result;", TypeNameUtils.SimpleType(mi.ReturnType));
                }
                
                if (!mb.IsStatic)
                {
                    Write(file, "var a0 = ({0})call.GetObject(0);", TypeNameUtils.SimpleType(mb.DeclaringType));
                }

                for (int n = 0; n < pars.Length; n++)
                {
                    var p = pars[n];
                    var idx = mb.IsStatic ? n : n + 1;
                    Write(file, "var a{0} = ({1}){3}({2});", idx, TypeNameUtils.SimpleType(p.ParameterType) , idx, FuncGetArg(p));
                }
                
                Write(file, "if (caller == null)");
                Write(file, "{");
                Write(file, "caller = Delegate.CreateDelegate(typeof(IFixCallDel{0}), (MethodInfo)method);", methodCount);
                Write(file, "}");
                if (mi.ReturnType != typeof(void))
                {
                    Write(file, "result = ((IFixCallDel{1})caller)({0});", FuncCall(mi), methodCount);
                    Write(file, FuncPushResult(mi.ReturnType));
                    //Write(file, "call.PushObjectAsResult(result, result.GetType());");
                }
                else
                {
                    Write(file, "((IFixCallDel{1})caller)({0});", FuncCall(mi), methodCount);
                }

                for (int n = 0; n < pars.Length; n++)
                {
                    var p = pars[n];
                    var idx = mb.IsStatic ? n : n + 1;
                    if(p.ParameterType.IsByRef)
                        Write(file, "call.UpdateReference({0}, a{0}, vm, typeof({1}));", idx, TypeNameUtils.SimpleType(p.ParameterType));
                }
            }

            WriteFinaly(file, paramStart, paramCount);
            Write(file, "}");
            Write(file, "");
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
                WriteMethodCaller(mbList[i], file);
                methodCount++;
            }
            
            End(file);
        }

        #endregion

        StreamWriter Begin()
        {
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

        class PropPair
        {
            public string get = "null";
            public string set = "null";
            public bool isInstance = true;
        }

        public string path;
        public int methodCount = 0;
        int indent = 0;
        HashSet<string> funcname = new HashSet<string>();
        Dictionary<string, PropPair> propname = new Dictionary<string, PropPair>();
        Dictionary<string, bool> directfunc = new Dictionary<string, bool>();

        #endregion
    }
}