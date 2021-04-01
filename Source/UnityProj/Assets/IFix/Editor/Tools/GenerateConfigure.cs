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
                generateConfigure.blackListMethodInfo = readMatchInfo(reader);
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

        public abstract bool IsNewClass(TypeReference type);

        public abstract bool isNewField(FieldReference field);

        public abstract void AddNewMethod(MethodReference method);

        public abstract void AddNewClass(TypeReference type);

        public abstract void AddNewField(FieldReference field);

        //参数类型信息
        internal class ParameterMatchInfo
        {
            public bool IsOut;
            public string ParameterType;
        }

        //方法签名信息
        internal class MethodMatchInfo
        {
            public string Name;
            public string ReturnType;
            public ParameterMatchInfo[] Parameters;
        }

        internal class FieldMatchInfo
        {
            public string Name;
            public string FieldType;
        }

        internal class PropertyMatchInfo
        {
            public string Name;
            public string PropertyType;
        }

        //判断一个方法是否能够在matchInfo里头能查询到
        internal static bool isMatch(Dictionary<string, MethodMatchInfo[]> matchInfo, MethodReference method)
        {
            MethodMatchInfo[] mmis;
            if (matchInfo.TryGetValue(method.DeclaringType.FullName, out mmis))
            {
                foreach (var mmi in mmis)
                {
                    if (mmi.Name == method.Name && mmi.ReturnType == method.ReturnType.FullName
                        && mmi.Parameters.Length == method.Parameters.Count)
                    {
                        bool paramMatch = true;
                        for (int i = 0; i < mmi.Parameters.Length; i++)
                        {
                            var paramType = method.Parameters[i].ParameterType;
                            if (paramType.IsRequiredModifier)
                            {
                                paramType = (paramType as RequiredModifierType).ElementType;
                            }
                            if (mmi.Parameters[i].IsOut != method.Parameters[i].IsOut
                                || mmi.Parameters[i].ParameterType != paramType.FullName)
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

        internal static bool isMatchForField(Dictionary<string, FieldMatchInfo[]> matchInfo, FieldReference field)
        {
            FieldMatchInfo[] mmis;
            if (matchInfo.TryGetValue(field.DeclaringType.FullName, out mmis))
            {
                foreach (var mmi in mmis)
                {
                    if (mmi.Name == field.Name && mmi.FieldType == field.FieldType.FullName)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        internal static bool isMatchForProperty(Dictionary<string, PropertyMatchInfo[]> matchInfo, PropertyReference property)
        {
            PropertyMatchInfo[] mmis;
            if (matchInfo.TryGetValue(property.DeclaringType.FullName, out mmis))
            {
                foreach (var mmi in mmis)
                {
                    if (mmi.Name == property.Name && mmi.PropertyType == property.PropertyType.FullName)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        internal static bool isMatchForClass(HashSet<string> matchInfo, TypeReference type)
        {
            if (matchInfo.Contains(type.ToString()))
            {
                return true;
            }
            return false;
        }

        //读取方法信息，主要是方法的签名信息，名字+参数类型+返回值类型
        internal static Dictionary<string, MethodMatchInfo[]> readMatchInfo(BinaryReader reader)
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

        internal static Dictionary<string, FieldMatchInfo[]> readFieldInfo(BinaryReader reader)
        {
            Dictionary<string, FieldMatchInfo[]> matchInfo = new Dictionary<string, FieldMatchInfo[]>();

            int typeCount = reader.ReadInt32();
            for (int k = 0; k < typeCount; k++)
            {
                string typeName = reader.ReadString();
                int methodCount = reader.ReadInt32();
                FieldMatchInfo[] fieldMatchInfos = new FieldMatchInfo[methodCount];
                for (int i = 0; i < methodCount; i++)
                {
                    FieldMatchInfo fmi = new FieldMatchInfo();
                    fmi.Name = reader.ReadString();
                    fmi.FieldType = reader.ReadString();
                    fieldMatchInfos[i] = fmi;
                }
                matchInfo[typeName] = fieldMatchInfos;
            }

            return matchInfo;
        }

        internal static Dictionary<string, PropertyMatchInfo[]> readPropertyInfo(BinaryReader reader)
        {
            Dictionary<string, PropertyMatchInfo[]> matchInfo = new Dictionary<string, PropertyMatchInfo[]>();

            int typeCount = reader.ReadInt32();
            for (int k = 0; k < typeCount; k++)
            {
                string typeName = reader.ReadString();
                int methodCount = reader.ReadInt32();
                PropertyMatchInfo[] propertyMatchInfos = new PropertyMatchInfo[methodCount];
                for (int i = 0; i < methodCount; i++)
                {
                    PropertyMatchInfo pmi = new PropertyMatchInfo();
                    pmi.Name = reader.ReadString();
                    pmi.PropertyType = reader.ReadString();
                    propertyMatchInfos[i] = pmi;
                }
                matchInfo[typeName] = propertyMatchInfos;
            }

            return matchInfo;
        }

        internal static HashSet<string> readMatchInfoForClass(BinaryReader reader)
        {
            HashSet<string> setMatchInfoForClass = new HashSet<string>();
            int typeCount = reader.ReadInt32();
            for (int k = 0; k < typeCount; k++)
            {
                string className = reader.ReadString();
                setMatchInfoForClass.Add(className);
            }
            return setMatchInfoForClass;
        }
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
        public override bool IsNewClass(TypeReference type)
        {
            return false;
        }

        public override bool isNewField(FieldReference field)
        {
            return false;
        }

        public override void AddNewMethod(MethodReference method)
        {

        }

        public override void AddNewClass(TypeReference type)
        {

        }

        public override void AddNewField(FieldReference field)
        {

        }
    }

    //注入配置使用
    public class DefaultGenerateConfigure : GenerateConfigure
    {
        internal Dictionary<string, Dictionary<string, int>> configures
            = new Dictionary<string, Dictionary<string, int>>();

        internal Dictionary<string, MethodMatchInfo[]> blackListMethodInfo = null;

        public override bool TryGetConfigure(string tag, MethodReference method, out int flag)
        {
            Dictionary<string, int> configure;
            flag = 0;
            if(tag == "IFix.IFixAttribute" && blackListMethodInfo != null)
            {
                if(isMatch(blackListMethodInfo, method))
                {
                    return false;
                }
            }
            return (configures.TryGetValue(tag, out configure)
                && configure.TryGetValue(method.DeclaringType.FullName, out flag));
        }

        public override bool IsNewMethod(MethodReference method)
        {
            return false;
        }
        public override bool IsNewClass(TypeReference type)
        {
            return false;
        }
        public override bool isNewField(FieldReference field)
        {
            return false;
        }
        public override void AddNewMethod(MethodReference method)
        {
            
        }
        public override void AddNewClass(TypeReference type)
        {

        }
        public override void AddNewField(FieldReference field)
        {

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

        public override bool IsNewClass(TypeReference type)
        {
            return newClasses.Contains(type);
        }

        public override bool isNewField(FieldReference field)
        {
            return newFields.Contains(field);
        }

        public override void AddNewMethod(MethodReference method)
        {
            newMethods.Add(method);
        }

        public override void AddNewClass(TypeReference type)
        {
            newClasses.Add(type);
        }

        public override void AddNewField(FieldReference field)
        {
            newFields.Add(field);
        }

        //暂时不支持redirect类型的方法
        HashSet<MethodReference> redirectMethods = new HashSet<MethodReference>();
        HashSet<MethodReference> switchMethods = new HashSet<MethodReference>();
        HashSet<MethodReference> newMethods = new HashSet<MethodReference>();
        HashSet<TypeReference> newClasses = new HashSet<TypeReference>();
        HashSet<FieldReference> newFields = new HashSet<FieldReference>();
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

        private static bool isCompilerGenerated(FieldReference field)
        {
            var fd = field as FieldDefinition;
            return fd != null && fd.CustomAttributes.Any(ca => ca.AttributeType.FullName 
            == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");
        }

        //读取配置信息（要patch的方法列表，新增方法列表）
        public PatchGenerateConfigure(AssemblyDefinition newAssembly, string cfgPath)
        {
            Dictionary<string, MethodMatchInfo[]> patchMethodInfo = null;
            Dictionary<string, MethodMatchInfo[]> newMethodInfo = null;
            Dictionary<string, FieldMatchInfo[]> newFieldsInfo = null;
            Dictionary<string, PropertyMatchInfo[]> newPropertiesInfo = null;
            HashSet<string> newClassInfo = null;

            using (BinaryReader reader = new BinaryReader(File.Open(cfgPath, FileMode.Open)))
            {
                patchMethodInfo = readMatchInfo(reader);
                newMethodInfo = readMatchInfo(reader);
                newFieldsInfo = readFieldInfo(reader);
                newPropertiesInfo = readPropertyInfo(reader);
                newClassInfo = readMatchInfoForClass(reader);
            }

            foreach (var method in (from type in newAssembly.GetAllType() from method in type.Methods select method ))
            {
                if (isMatch(patchMethodInfo, method))
                {
                    switchMethods.Add(method);
                }
                if (isMatch(newMethodInfo, method))
                {
                    AddNewMethod(method);
                }
            }
            foreach (var clas in newAssembly.GetAllType())
            {
                if (isMatchForClass(newClassInfo, clas))
                {
                    AddNewClass(clas);
                }
            }
            foreach (var property in (from type in newAssembly.GetAllType() from property in type.Properties select property))
            {
                if (isMatchForProperty(newPropertiesInfo, property))
                {
                    AddNewMethod(property.SetMethod);
                    AddNewMethod(property.GetMethod);

                    var methods = new List<MethodDefinition>{property.GetMethod, property.SetMethod};
                    
                    var defination = newAssembly.MainModule.GetType(property.DeclaringType.FullName);
                    foreach (var field in ( from method in methods
                        where method != null && method.IsSpecialName && method.Body != null 
                            && method.Body.Instructions != null
                        from instruction in method.Body.Instructions
                        where instruction.OpCode.Code == Mono.Cecil.Cil.Code.Ldsfld
                            || instruction.OpCode.Code == Mono.Cecil.Cil.Code.Stsfld
                            || instruction.OpCode.Code == Mono.Cecil.Cil.Code.Ldsflda
                            || instruction.OpCode.Code == Mono.Cecil.Cil.Code.Ldfld
                            || instruction.OpCode.Code == Mono.Cecil.Cil.Code.Stfld
                            || instruction.OpCode.Code == Mono.Cecil.Cil.Code.Ldflda
                        where isCompilerGenerated(instruction.Operand as Mono.Cecil.FieldReference)
                        select (instruction.Operand as Mono.Cecil.FieldReference).Resolve()).Distinct())
                    {
                        var backingField = property.DeclaringType.Fields.FirstOrDefault(f => f.FullName == field.FullName);
                        if(backingField != null)
                        {
                            AddNewField(backingField);
                        }
                    }
                }
            }
            foreach (var field in (from type in newAssembly.GetAllType() from field in type.Fields select field ))
            {
                if (isMatchForField(newFieldsInfo, field))
                {
                    AddNewField(field);
                }
            }
        }
    }
}