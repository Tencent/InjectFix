using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using IFix.Utils;
using Unity.Collections.LowLevel.Unsafe;

namespace IFix.Editor
{
    public class ILFixCodeGen
    {
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
            var key = TypeNameUtils.GetUniqueMethodName(mi);
            
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
            
            if(key.Contains(">d__"))
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
            
            return string.Format("public delegate {0} IFixCallDel{2}({1});", TypeNameUtils.SimpleType(retType), parameterTypeNames, methodCount);
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
                if (!(!m.IsStatic && m.ReflectedType.IsValueType && m.IsPublic))
                {
                    str += "a0";
                    if (pars.Length > 0) str += ",";
                }
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
            return string.Format("({0})", TypeNameUtils.SimpleType(t));
        }

        string FuncPushResult(Type t)
        {
            try{
                var enn = t.IsEnum;
            }
            catch
            {
                UnityEngine.Debug.Log(t);
            }
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
            else if (!t.IsGenericType && t.IsEnum)
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
            else if(UnsafeUtility.IsUnmanaged(t)
                    && Nullable.GetUnderlyingType(t) == null )
            {
                return "call.PushValueUnmanagedAsResult(result);";
            }
            return $"call.PushObjectAsResult(result, typeof({TypeNameUtils.SimpleType(t)}));";
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
            Write(file, "using IFix.Utils;");
            Write(file, "#if UNITY_5_5_OR_NEWER");
            Write(file, "using UnityEngine.Profiling;");
            Write(file, "#else");
            Write(file, "using UnityEngine;");
            Write(file, "#endif");

            Write(file, "namespace IFix.Binding");
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

            //GenFileDictionary();
        }

        /*
        private void GenFileDictionary()
        {
            string bindingDelDictPath = path + "/Resources/BindingDelDict.bytes";
            FileDictionary<string, Tuple<int, bool>> file =
                new FileDictionary<string, Tuple<int, bool>>(bindingDelDictPath, 1024, true);
            
            foreach (var item in delegateDict)
            {
                file.Add(item.Key, new Tuple<int, bool>(item.Value.Item1, true));
            }
            
            foreach (var item in ctorCache)
            {
                file.Add(item.Key, new Tuple<int, bool>(item.Value, false));
            }
            
            foreach (var item in publicInstanceStructCache)
            {
                file.Add(item.Key, new Tuple<int, bool>(item.Value, false));
            }

            file.Close();
        }*/

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
            var key = TypeNameUtils.GetUniqueMethodName(mb);
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (key.Contains("!"))
            {
                return false;
            }
            
            if(key.Contains(">d__"))
            {
                return false;
            }

            if (key.Contains("<>"))
            {
                return false;
            }

            // class method
            if (mb is MethodInfo &&
                 !TypeNameUtils.MethodIsStructPublic(mb))
            {
                string delegateStr;
                if (TryGetDelegateStr((MethodInfo)mb, out delegateStr))
                {
                    return false;
                }
                Write(file, delegateStr);
            }
            else if (mb is ConstructorInfo)
            {
                var cr = TypeNameUtils.GetUniqueMethodName(mb);
                if (cr == "") return false;
                if (!ctorCache.ContainsKey(cr))
                {
                    ctorCache.Add(cr, methodCount);
                }
                else
                {
                    return false;
                }
            }
            else if (TypeNameUtils.MethodIsStructPublic(mb))
            {
                if (!publicInstanceStructCache.ContainsKey(key))
                {
                    publicInstanceStructCache.Add(key, methodCount);
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            Write(file, "");
            Write(file, "public void Invoke{0}(VirtualMachine vm, ref Call call, bool instantiate) {{", methodCount);

            ParameterInfo[] pars = mb.GetParameters();
            if (mb is ConstructorInfo)
            {
                for (int n = 0; n < pars.Length; n++)
                {
                    var p = pars[n];
                    if (p.ParameterType.IsPointer)
                    {
                        Write(file, "var a{0} = ({1})((IntPtr)args[{0}]);", n, TypeNameUtils.SimpleType(p.ParameterType));
                    }
                    else
                    {
                        Write(file, "var a{0} = ({1})args[{0}];", n, TypeNameUtils.SimpleType(p.ParameterType));
                    }
                }

                string retType = TypeNameUtils.SimpleType(mb.ReflectedType);
                Write(file, "var result = new {0}({1});", retType, FuncCall(mb));
                if (UnsafeUtility.IsUnmanaged(mb.ReflectedType)
                    && Nullable.GetUnderlyingType(mb.ReflectedType) == null)
                {
                    Write(file, "ret = BoxUtils.BoxObject(result);");
                }
                else
                {
                    Write(file, "ret = result;");
                }
            }
            else
            {
                var mi = mb as MethodInfo;
                
                if (!mb.IsStatic)
                {
                    Write(file, "var a0 = ({0})instance;", TypeNameUtils.SimpleType(mb.DeclaringType));
                }

                for (int n = 0; n < pars.Length; n++)
                {
                    var p = pars[n];
                    var idx = mb.IsStatic ? n : n + 1;
                    if (!p.IsOut)
                    {
                        if (p.ParameterType.IsPointer)
                        {
                            Write(file, "var a{0} = ({1})((IntPtr)args[{2}]);", idx, TypeNameUtils.SimpleType(p.ParameterType), n);
                        }
                        else
                        {
                            Write(file, "var a{0} = ({1})args[{2}];", idx, TypeNameUtils.SimpleType(p.ParameterType), n);
                        }
                    }
                    else
                    {
                        Write(file, "{1} a{0};", idx, TypeNameUtils.SimpleType(p.ParameterType));
                    }
                }

                if (TypeNameUtils.MethodIsStructPublic(mb))
                {
                    string methodName = mi.Name;
                    if (methodName.Contains("get_"))
                    {
                        if (methodName == "get_Item")
                        {
                            Write(file, "var result = a0[a1];");
                        }
                        else
                        {
                            Write(file, "var result = a0.{0};", methodName.Remove(0, 4));
                        }
                    }
                    else if (methodName.Contains("set_"))
                    {
                        if (methodName == "set_Item")
                        {
                            Write(file, "a0[a1] = a2;");
                        }
                        else
                        {
                            Write(file, "a0.{0} = a1;", methodName.Remove(0, 4));
                        }
                    }
                    else
                    {
                        if (mi.ReturnType != typeof(void))
                        {
                            Write(file, "var result = a0.{0}({1});", methodName, FuncCall(mi));
                            //Write(file, "call.PushObjectAsResult(result, result.GetType());");
                        }
                        else
                        {
                            Write(file, "a0.{0}({1});", methodName, FuncCall(mi));
                        }
                    }
                }
                else
                {
                    if (mi.ReturnType != typeof(void))
                    {
                        Write(file, "var result = ((IFixCallDel{1})caller)({0});", FuncCall(mi), methodCount);
                        //Write(file, "call.PushObjectAsResult(result, result.GetType());");
                    }
                    else
                    {
                        Write(file, "((IFixCallDel{1})caller)({0});", FuncCall(mi), methodCount);
                    }
                }

                
                if(mi.ReturnType != typeof(void))
                {
                    if (UnsafeUtility.IsUnmanaged(mi.ReturnType)
                        && Nullable.GetUnderlyingType(mi.ReturnType) == null)
                    {
                        Write(file, "ret = BoxUtils.BoxObject(result);");
                    }
                    else
                    {
                        Write(file, "ret = result;");
                    }
                }

                
                for (int n = 0; n < pars.Length; n++)
                {
                    var p = pars[n];
                    var idx = mb.IsStatic ? n : n + 1;
                    if (p.ParameterType.IsByRef)
                    {
                        Write(file, "BoxUtils.RecycleObject(args[{0}]);", n);
                        if (UnsafeUtility.IsUnmanaged(p.ParameterType)
                            && Nullable.GetUnderlyingType(p.ParameterType) == null)
                        {
                            Write(file, "args[{0}] = BoxUtils.BoxObject(a{1});", n, idx);
                        }
                        else
                        {
                            Write(file, "args[{0}] = a{1};", n, idx);
                        }
                    }
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
                if(WriteMethodCaller(mbList[i], file))
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