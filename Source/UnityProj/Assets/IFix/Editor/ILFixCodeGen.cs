using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace IFix.Editor
{
    public class ILFixCodeGen
    {
        #region static

        public static HashSet<string> delegateDict = new HashSet<string>();
        public static HashSet<ConstructorInfo> ctorCache = new HashSet<ConstructorInfo>();
        public static void ClearMethod()
        {
            delegateDict.Clear();
            ctorCache.Clear();
        }

        public static bool TryGetDelegateStr(MethodInfo mi, out string ret)
        {
            bool result = true;
            ret = MethodInfoToDelegateStr(mi);
            if (!delegateDict.Contains(ret))
            {
                result = false;
                delegateDict.Add(ret);
            }

            return result;
        }

        #endregion

        #region cal name

        static string[] prefix = new string[] { "System.Collections.Generic" };

        static string RemoveRef(string s, bool removearray = true)
        {
            if (s.EndsWith("&")) s = s.Substring(0, s.Length - 1);
            if (s.EndsWith("[]") && removearray) s = s.Substring(0, s.Length - 2);
            if (s.StartsWith(prefix[0])) s = s.Substring(prefix[0].Length + 1, s.Length - prefix[0].Length - 1);

            s = s.Replace("+", ".");
            if (s.Contains("`"))
            {
                string regstr = @"`\d";
                Regex r = new Regex(regstr, RegexOptions.None);
                s = r.Replace(s, "");
                s = s.Replace("[", "<");
                s = s.Replace("]", ">");
            }

            return s;
        }

        static string FullName(Type t)
        {
            if (t.FullName == null)
            {
                Debug.Log(t.Name);
                return t.Name;
            }

            return FullName(t.FullName);
        }

        static string FullName(string str)
        {
            if (str == null)
            {
                throw new NullReferenceException();
            }

            return RemoveRef(str.Replace("+", "."));
        }
        
        static string SimpleType(Type t)
        {
            string tn = t.Name;
            switch (tn)
            {
                case "Single":
                    return "float";
                case "String":
                    return "string";
                case "String[]":
                    return "string[]";
                case "Double":
                    return "double";
                case "Boolean":
                    return "bool";
                case "Int32":
                    return "int";
                case "Int32[]":
                    return "int[]";
                case "UInt32":
                    return "uint";
                case "UInt32[]":
                    return "uint[]";
                case "Int16":
                    return "short";
                case "Int16[]":
                    return "short[]";
                case "UInt16":
                    return "ushort";
                case "UInt16[]":
                    return "ushort[]";
                case "Object":
                    return FullName(t);
                default:
                    tn = TypeDecl(t);
                    tn = tn.Replace("System.Collections.Generic.", "");
                    tn = tn.Replace("System.Object", "object");
                    return tn;
            }
        }

        static string _Name(string n)
        {
            n = n.Replace("*", "__X");
            string ret = "";
            for (int i = 0; i < n.Length; i++)
            {
                if (char.IsLetterOrDigit(n[i]))
                    ret += n[i];
                else
                    ret += "_";
            }

            return ret;
        }

        static string GenericBaseName(Type t)
        {
            string n = t.FullName;
            if (n.IndexOf('[') > 0)
            {
                n = n.Substring(0, n.IndexOf('['));
            }

            return n.Replace("+", ".");
        }

        static string GenericName(Type t, string sep = "_")
        {
            try
            {
                Type[] tt = t.GetGenericArguments();
                string ret = "";
                for (int n = 0; n < tt.Length; n++)
                {
                    string dt = SimpleType(tt[n]);
                    ret += dt;
                    if (n < tt.Length - 1)
                        ret += sep;
                }

                return ret;
            }
            catch (Exception e)
            {
                Debug.Log(e.ToString());
                return "";
            }
        }

        public static string ExportName(MethodBase mb)
        {
            string clsname = ExportName(mb.DeclaringType);
            ParameterInfo[] pars = mb.GetParameters();
            var parameterTypeNames = string.Join("_", Array.ConvertAll(pars, p => ExportName(p.ParameterType)));
            return clsname + "_" + parameterTypeNames + _Name(mb.Name);
        }
        
        public static string ExportName(Type t)
        {
            if (t.IsGenericType)
            {
                return string.Format("{0}_{1}", _Name(GenericBaseName(t)), _Name(GenericName(t)));
            }
            else
            {
                string name = RemoveRef(t.FullName, true);
                return name.Replace(".", "_").Replace("*", "__X");
            }
        }

        static string TypeDecl(Type t)
        {
            if (t == typeof(void)) return "void";
            if (t.IsGenericType)
            {
                string ret = GenericBaseName(t);

                string gs = "";
                gs += "<";
                Type[] types = t.GetGenericArguments();
                for (int n = 0; n < types.Length; n++)
                {
                    gs += SimpleType(types[n]);
                    if (n < types.Length - 1)
                        gs += ",";
                }

                gs += ">";

                ret = Regex.Replace(ret, @"`\d", gs);

                return RemoveRef(ret);
            }

            if (t.IsArray)
            {
                return TypeDecl(t.GetElementType()) + "[]";
            }
            else
                return RemoveRef(t.ToString(), false);
        }

        static string MethodInfoToDelegateStr(MethodInfo method)
        {
            List<string> args = new List<string>();
            var parameters = method.GetParameters();
            if (!method.IsStatic)
            {
                args.Add(_Name(SimpleType(method.ReflectedType)));
            }

            for (int i = 0, imax = parameters.Length; i<imax; i++)
            {
                var p = parameters[i];
                if (p.ParameterType.IsByRef && p.IsOut)
                    args.Add("out_" + _Name(SimpleType(p.ParameterType)));
                else if (p.ParameterType.IsByRef)
                    args.Add("ref_" + _Name(SimpleType(p.ParameterType)));
                else
                    args.Add(_Name(SimpleType(p.ParameterType)));
            }
            var parameterTypeNames = string.Join("_", args);
            
            string ret = string.Format("__{1}__{0}", _Name(SimpleType(method.ReturnType)), parameterTypeNames);

            ret = ret.Replace(".", "_");
            return ret;
        }

        static string MethodInfoToDelegate(MethodInfo method)
        {
            List<string> args = new List<string>();
            var parameters = method.GetParameters();
            if (!method.IsStatic && !(method is ConstructorInfo))
            {
                args.Add(SimpleType(method.ReflectedType) +" arg0");
            }

            for (int i = 0, imax = parameters.Length; i<imax; i++)
            {
                var p = parameters[i];
                if (p.ParameterType.IsByRef && p.IsOut)
                    args.Add("out " + SimpleType(p.ParameterType) + string.Format(" arg{0}", i + 1));
                else if (p.ParameterType.IsByRef)
                    args.Add("ref " + SimpleType(p.ParameterType) + string.Format(" arg{0}", i + 1));
                else
                    args.Add(SimpleType(p.ParameterType) + string.Format(" arg{0}", i + 1));
            }
            var parameterTypeNames = string.Join(",", args);
            
            return string.Format("delegate {0} IFixCallDel({1});", SimpleType(method.ReturnType), parameterTypeNames);
        }

        #endregion

        #region type

        public static string GetUniqueStringForMethod(MethodBase method)
        {
            var parameters = method.GetParameters();
            var parameterTypeNames = string.Join(",", Array.ConvertAll(parameters, p => TypeDecl(p.ParameterType)));
            return $"{TypeDecl(method.DeclaringType)}.{method.Name}({parameterTypeNames})";
        }
        
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
                if (p.ParameterType.IsByRef && p.IsOut)
                    str += string.Format("out a{0}", n + 1);
                else if (p.ParameterType.IsByRef)
                    str += string.Format("ref a{0}", n + 1);
                else
                    str += string.Format("a{0}", n + 1);
                if (n < pars.Length - 1)
                    str += ",";
            }

            return str;
        }

        string FuncGetArg(ParameterInfo p)
        {
            Type t = p.ParameterType;

            if (t.IsPrimitive)
            {
                if (t == typeof(int))
                {
                    return "call.GetInt32";
                }
                else if (t == typeof(float))
                {
                    return "call.GetSingle";
                }
                else if (t == typeof(bool))
                {
                    return "call.GetBoolean";
                }
                else if (t == typeof(double))
                {
                    return "call.GetDouble";
                }
                else if (t == typeof(long))
                {
                    return "call.GetInt64";
                }
                else if (t == typeof(byte))
                {
                    return "call.GetByte";
                }
                else if (t == typeof(uint))
                {
                    return "call.GetUInt32";
                }
                else if (t == typeof(ushort))
                {
                    return "call.GetUInt16";
                }
                else if (t == typeof(short))
                {
                    return "call.GetInt16";
                }
                else if (t == typeof(char))
                {
                    return "call.GetChar";
                }
                else if (t == typeof(ulong))
                {
                    return "call.GetUInt64";
                }
                else if (t == typeof(sbyte))
                {
                    return "call.GetSByte";
                }
                else if (t == typeof(IntPtr))
                {
                    return "call.GetIntPtr";
                }
                else if (t == typeof(UIntPtr))
                {
                    return "call.GetUIntPtr";
                }
            }
            else if (t.IsEnum)
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

        private void WriteHead(MethodBase mb, StreamWriter file, string exportName)
        {
            
            Write(file, "using System;");
            Write(file, "using IFix.Core;");
            Write(file, "using System.Collections.Generic;");
            Write(file, "using System.Reflection;");
            Write(file, "#if UNITY_5_5_OR_NEWER");
            Write(file, "using UnityEngine.Profiling;");
            Write(file, "#endif");

            Write(file, "namespace IFix.Binding");
            Write(file, "{");
            Write(file, "public unsafe class {0} : IFixBindingCaller", exportName);
            Write(file, "{");
            if (mb is MethodInfo)
            {
                Write(file, MethodInfoToDelegate((MethodInfo)mb));
                Write(file, "");
                Write(file, "private IFixCallDel del;");
                Write(file, "");
            }
            Write(file, "public {0}(MethodBase method) : base(method) {{", exportName);
            if (mb is MethodInfo)
            {
                Write(file, "del = (IFixCallDel)Delegate.CreateDelegate(typeof(IFixCallDel), (MethodInfo)method);");
            }
            Write(file, "}");

        }

        private void End(StreamWriter file)
        {
            Write(file, "}");
            Write(file, "}");
            file.Flush();
            file.Close();
        }

        private void WriteFunctionAttr(StreamWriter file)
        {
//             Write(file, "[SLua.MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]");
// #if UNITY_5_3_OR_NEWER
//             Write(file, "[UnityEngine.Scripting.Preserve]");
// #endif
        }

        void WriteTry(StreamWriter file)
        {
            Write(file, "#if DEBUG");
            Write(file, "try {");
            Write(file, "Profiler.BeginSample(method.Name);");
            Write(file, "#endif");
        }

        void WriteCatchExecption(StreamWriter file)
        {
            // Write(file, "}");
            // Write(file, "catch(Exception e) {");
            // Write(file, "return error(l,e);");
            Write(file, "#if DEBUG");
            Write(file, "}");
            WriteFinaly(file);
            Write(file, "#endif");
        }

        void WriteFinaly(StreamWriter file)
        {
            Write(file, "finally {");
            Write(file, "Profiler.EndSample();");
            Write(file, "}");

        }

        void WriteOk(StreamWriter file)
        {
            Write(file, "pushValue(l,true);");
        }

        void WriteBad(StreamWriter file)
        {
            Write(file, "pushValue(l,false);");
        }

        void WriteError(StreamWriter file, string err)
        {
            WriteBad(file);
            Write(file, "LuaDLL.lua_pushstring(l,\"{0}\");", err);
            Write(file, "return 2;");
        }

        void WriteReturn(StreamWriter file, string val)
        {
            Write(file, "pushValue(l,true);");
            Write(file, "pushValue(l,{0});", val);
            Write(file, "return 2;");
        }

        private void WriteMethodCaller(MethodBase mb, StreamWriter file)
        {
            Write(file, "");
            Write(file, "public void Invoke(VirtualMachine vm, ref Call call, bool instantiate) {");
            WriteTry(file);

            ParameterInfo[] pars = mb.GetParameters();
            if (mb is ConstructorInfo)
            {
                Write(file, "{0} result;", TypeDecl(mb.ReflectedType));
                for (int n = 0; n < pars.Length; n++)
                {
                    var p = pars[n];
                    Write(file, "var a{0} = ({1}){3}({2});", n+1, TypeDecl(p.ParameterType) ,n, FuncGetArg(p));
                }
                Write(file, "result = new {0}({1});", TypeDecl(mb.ReflectedType), FuncCall(mb));
                Write(file, "call.PushObjectAsResult(result, result.GetType());");
            }
            else
            {
                var mi = mb as MethodInfo;
                if (mi.ReturnType != typeof(void))
                {
                    Write(file, "{0} result;", SimpleType(mi.ReturnType));
                }
                
                if (!mb.IsStatic)
                {
                    Write(file, "var a0 = ({0})call.GetObject(0);", SimpleType(mb.DeclaringType));
                }

                for (int n = 0; n < pars.Length; n++)
                {
                    var p = pars[n];
                    Write(file, "var a{0} = ({1}){3}({2});", n+1, SimpleType(p.ParameterType) ,mb.IsStatic ? n : n + 1, FuncGetArg(p));
                }

                if (mi.ReturnType != typeof(void))
                {
                    Write(file, "result = del({0});", FuncCall(mi));
                    Write(file, FuncPushResult(mi.ReturnType));
                    //Write(file, "call.PushObjectAsResult(result, result.GetType());");
                }
                else
                {
                    Write(file, "del({0});", FuncCall(mi));
                }
            }

            WriteCatchExecption(file);
            Write(file, "}");
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

        public void Generate(Type t)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            ConstructorInfo[] cons = GetValidConstructor(t);
            string exportName;
            foreach (var item in cons)
            {
                GenerateMethod(item);
            }
            
            var mis = GetValidMethodInfo(t);
            foreach (var item in mis)
            {
                GenerateMethod(item);
            }

        }

        public void GenerateMethod(MethodBase mb)
        {
            try
            {
                if (mb is ConstructorInfo)
                {
                    ConstructorInfo ci = mb as ConstructorInfo;
                    if (ci.ReflectedType.IsSubclassOf(typeof(Delegate))) return;
                    if (ctorCache.Contains(ci))
                    {
                        return;
                    }

                    ctorCache.Add(ci);
                    StreamWriter file = Begin(mb);
                    WriteHead(mb, file, ExportName(mb));
                    WriteMethodCaller(mb, file);
                    End(file);
                }
                else if (mb is MethodInfo)
                {
                    string exportName;
                    var item = mb as MethodInfo;
                    if (item.IsGenericMethodDefinition) return;

                    if (TryGetDelegateStr(item, out exportName)) return;
                    StreamWriter file = Begin(item);
                    WriteHead(item, file, exportName);
                    WriteMethodCaller(item, file);
                    End(file);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }


        }

        #endregion

        StreamWriter Begin(MethodBase mb)
        {
            string clsname = ExportName(mb);
            if (mb is MethodInfo)
            {
                TryGetDelegateStr((MethodInfo)mb, out clsname);
            }

            string f = path + clsname + ".cs";
            StreamWriter file = new StreamWriter(f, false, Encoding.UTF8);
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

        int indent = 0;
        HashSet<string> funcname = new HashSet<string>();
        Dictionary<string, PropPair> propname = new Dictionary<string, PropPair>();
        Dictionary<string, bool> directfunc = new Dictionary<string, bool>();

        #endregion
    }
}