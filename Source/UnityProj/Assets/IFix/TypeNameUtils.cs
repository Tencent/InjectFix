using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace IFix.Utils
{
    public static class TypeNameUtils
    {
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

        public static string FullName(Type t)
        {
            if (t.FullName == null)
            {
                Debug.Log(t.Name);
                return t.Name;
            }

            return FullName(t.FullName);
        }

        public static string FullName(string str)
        {
            if (str == null)
            {
                throw new NullReferenceException();
            }

            return RemoveRef(str.Replace("+", "."));
        }
        
        public static string SimpleType(Type t)
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
                case "Void*":
                    return "void*";
                default:
                    tn = TypeDecl(t);
                    tn = tn.Replace("System.Collections.Generic.", "");
                    tn = tn.Replace("System.Object", "object");
                    return tn;
            }
        }

        public static string _Name(string n)
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

        public static string GenericBaseName(Type t)
        {
            string n = t.FullName;
            if (n.IndexOf('[') > 0)
            {
                n = n.Substring(0, n.IndexOf('['));
            }

            return n.Replace("+", ".");
        }

        public static string GetUniqueMethodName(MethodBase method)
        {
            List<string> args = new List<string>();
            var parameters = method.GetParameters();
            if (!method.IsStatic && !(method is ConstructorInfo))
            {
                args.Add( TypeNameUtils.SimpleType(method.ReflectedType));
            }

            for (int i = 0, imax = parameters.Length; i<imax; i++)
            {
                var p = parameters[i];
                if (p.ParameterType.IsByRef && p.IsOut)
                    args.Add("out " + TypeNameUtils.SimpleType(p.ParameterType));
                else if (p.ParameterType.IsByRef)
                    args.Add("ref " + TypeNameUtils.SimpleType(p.ParameterType));
                else
                    args.Add(TypeNameUtils.SimpleType(p.ParameterType));
            }
            var parameterTypeNames = string.Join(",", args);

            Type retType = (method is MethodInfo)
                ? (method as MethodInfo).ReturnType
                : (method as ConstructorInfo).ReflectedType;
            
            return string.Format("{0} ({1})", TypeNameUtils.SimpleType(retType), parameterTypeNames);
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
    }
}