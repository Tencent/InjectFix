/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Linq;

namespace IFix
{
    public abstract class GenerateConfigure
    {
        public static GenerateConfigure Empty()
        {
            return new EmptyGenerateConfigure();
        }

        //仅仅简单的从文件加载类名而已
        public static GenerateConfigure FromFile(string filename)
        {
            DefaultGenerateConfigure generateConfigure = new DefaultGenerateConfigure();

            using (BinaryReader reader = new BinaryReader(File.Open(filename, FileMode.Open)))
            {
                int configureNum = reader.ReadInt32();
                for (int i = 0; i < configureNum; i++)
                {
                    string configureName = reader.ReadString();
                    Dictionary<string, int> configure = new Dictionary<string, int>();
                    int cfgItemCount = reader.ReadInt32();
                    for (int j = 0; j < cfgItemCount; j++)
                    {
                        string typeName = reader.ReadString();
                        int flag = reader.ReadInt32();
                        configure[typeName] = flag;
                    }
                    generateConfigure.configures[configureName] = configure;
                }
            }

            return generateConfigure;
        }

        /// <summary>
        /// 如果一个方法打了指定的标签，返回其配置的标志位
        /// </summary>
        /// <param name="tag">标签</param>
        /// <param name="method">要查询的方法</param>
        /// <param name="flag">输出参数，用户配置的标志位</param>
        /// <returns></returns>
        public abstract bool TryGetConfigure(string tag, MethodReference method, out int flag);

        /// <summary>
        /// 判断一个方法是否是新增方法
        /// </summary>
        /// <param name="method">要查询的方法</param>
        /// <returns></returns>
        public abstract bool IsNewMethod(MethodReference method);
    }

    //内部测试专用
    public class EmptyGenerateConfigure : GenerateConfigure
    {
        public override bool TryGetConfigure(string tag, MethodReference method, out int flag)
        {
            flag = 0;
            return true;
        }

        public override bool IsNewMethod(MethodReference method)
        {
            return false;
        }
    }

    //注入配置使用
    public class DefaultGenerateConfigure : GenerateConfigure
    {
        internal Dictionary<string, Dictionary<string, int>> configures
            = new Dictionary<string, Dictionary<string, int>>();

        public override bool TryGetConfigure(string tag, MethodReference method, out int flag)
        {
            Dictionary<string, int> configure;
            flag = 0;
            return (configures.TryGetValue(tag, out configure)
                && configure.TryGetValue(method.DeclaringType.FullName, out flag));
        }

        public override bool IsNewMethod(MethodReference method)
        {
            return false;
        }
    }

    //patch配置使用
    public class PatchGenerateConfigure : GenerateConfigure
    {
        public override bool TryGetConfigure(string tag, MethodReference method, out int flag)
        {
            flag = 0;
            if (tag == "IFix.InterpretAttribute")
            {
                return redirectMethods.Contains(method);
            }
            else if (tag == "IFix.IFixAttribute")
            {
                return switchMethods.Contains(method);
            }
            return false;
        }

        public override bool IsNewMethod(MethodReference method)
        {
            return newMethods.Contains(method);
        }

        //暂时不支持redirect类型的方法
        HashSet<MethodReference> redirectMethods = new HashSet<MethodReference>();
        HashSet<MethodReference> switchMethods = new HashSet<MethodReference>();
        HashSet<MethodReference> newMethods = new HashSet<MethodReference>();

        MethodDefinition findMatchMethod(Dictionary<string, Dictionary<string, List<MethodDefinition>>> searchData,
            MethodDefinition method)
        {
            Dictionary<string, List<MethodDefinition>> methodsOfType;
            List<MethodDefinition> overloads;
            if (searchData.TryGetValue(method.DeclaringType.FullName, out methodsOfType)
                && methodsOfType.TryGetValue(method.Name, out overloads))
            {
                foreach (var overload in overloads)
                {
                    if (overload.IsTheSame(method))
                    {
                        return overload;
                    }
                }
            }
            return null;
        }
        //参数类型信息
        class ParameterMatchInfo
        {
            public bool IsOut;
            public string ParameterType;
        }

        //方法签名信息
        class MethodMatchInfo
        {
            public string Name;
            public string ReturnType;
            public ParameterMatchInfo[] Parameters;
        }

        //判断一个方法是否能够在matchInfo里头能查询到
        bool isMatch(Dictionary<string, MethodMatchInfo[]> matchInfo, MethodReference method)
        {
            MethodMatchInfo[] mmis;
            if (matchInfo.TryGetValue(method.DeclaringType.FullName, out mmis))
            {
                foreach(var mmi in mmis)
                {
                    if (mmi.Name == method.Name && mmi.ReturnType == method.ReturnType.FullName
                        && mmi.Parameters.Length == method.Parameters.Count)
                    {
                        bool paramMatch = true;
                        for(int i = 0; i < mmi.Parameters.Length; i++)
                        {
                            if (mmi.Parameters[i].IsOut != method.Parameters[i].IsOut
                                || mmi.Parameters[i].ParameterType != method.Parameters[i].ParameterType.FullName)
                            {
                                paramMatch = false;
                                break;
                            }
                        }
                        if (paramMatch) return true;
                    }
                }
            }
            return false;
        }

        //读取方法信息，主要是方法的签名信息，名字+参数类型+返回值类型
        static Dictionary<string, MethodMatchInfo[]> readMatchInfo(BinaryReader reader)
        {
            Dictionary<string, MethodMatchInfo[]> matchInfo = new Dictionary<string, MethodMatchInfo[]>();

            int typeCount = reader.ReadInt32();
            for (int k = 0; k < typeCount; k++)
            {
                string typeName = reader.ReadString();
                int methodCount = reader.ReadInt32();
                MethodMatchInfo[] methodMatchInfos = new MethodMatchInfo[methodCount];
                for (int i = 0; i < methodCount; i++)
                {
                    MethodMatchInfo mmi = new MethodMatchInfo();
                    mmi.Name = reader.ReadString();
                    mmi.ReturnType = reader.ReadString();
                    int parameterCount = reader.ReadInt32();
                    mmi.Parameters = new ParameterMatchInfo[parameterCount];
                    for (int p = 0; p < parameterCount; p++)
                    {
                        mmi.Parameters[p] = new ParameterMatchInfo();
                        mmi.Parameters[p].IsOut = reader.ReadBoolean();
                        mmi.Parameters[p].ParameterType = reader.ReadString();
                    }
                    methodMatchInfos[i] = mmi;
                }
                matchInfo[typeName] = methodMatchInfos;
            }

            return matchInfo;
        }

        //读取配置信息（要patch的方法列表，新增方法列表）
        public PatchGenerateConfigure(AssemblyDefinition newAssembly, string cfgPath)
        {
            Dictionary<string, MethodMatchInfo[]> patchMethodInfo = null;
            Dictionary<string, MethodMatchInfo[]> newMethodInfo = null;

            using (BinaryReader reader = new BinaryReader(File.Open(cfgPath, FileMode.Open)))
            {
                patchMethodInfo = readMatchInfo(reader);
                newMethodInfo = readMatchInfo(reader);
            }

            foreach (var method in (from type in newAssembly.GetAllType() from method in type.Methods select method ))
            {
                if (isMatch(patchMethodInfo, method))
                {
                    switchMethods.Add(method);
                }
                if (isMatch(newMethodInfo, method))
                {
                    newMethods.Add(method);
                }
            }
        }
    }
}