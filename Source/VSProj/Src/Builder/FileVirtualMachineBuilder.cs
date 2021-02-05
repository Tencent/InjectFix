/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

namespace IFix.Core
{
    using System.Collections.Generic;
    using System;
    using System.IO;
    using System.Reflection;
    using System.Linq;
    //using System.Text;

    public class IDTagAttribute : Attribute
    {
        public int ID;

        public IDTagAttribute(int id)
        {
            ID = id;
        }
    }

    public static class PatchManager
    {
        static Dictionary<Assembly, Action> removers = new Dictionary<Assembly, Action>();

        static public VirtualMachine Load(string filepath)
        {
            using (FileStream fs = File.Open(filepath, FileMode.Open))
            {
                return Load(fs);
            }
        }

        // #lizard forgives
        static MethodBase readMethod(BinaryReader reader, Type[] externTypes)
        {
            bool isGenericInstance = reader.ReadBoolean();
            BindingFlags flag = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.NonPublic | BindingFlags.Public;
            if (isGenericInstance)
            {
                Type declaringType = externTypes[reader.ReadInt32()];
                string methodName = reader.ReadString();
                //Console.WriteLine("load generic method: " + declaringType + " ?? " + methodName);
                int genericArgCount = reader.ReadInt32();
                //Console.WriteLine("genericArgCount:" + genericArgCount);
                Type[] genericArgs = new Type[genericArgCount];
                for (int j = 0; j < genericArgCount; j++)
                {
                    genericArgs[j] = externTypes[reader.ReadInt32()];
                    //Console.WriteLine(j + " ga:" + genericArgs[j]);
                }
                int paramCount = reader.ReadInt32();
                object[] paramMatchInfo = new object[paramCount];
                for (int j = 0; j < paramCount; j++)
                {
                    bool isGeneric = reader.ReadBoolean();
                    paramMatchInfo[j] = isGeneric ? (object)reader.ReadString() : externTypes[reader.ReadInt32()];
                }
                MethodInfo matchMethod = null;
                foreach (var method in declaringType.GetMethods(flag))
                {
                    var paramInfos = method.GetParameters();

                    Type[] genericArgInfos = null;
                    if (method.IsGenericMethodDefinition)
                    {
                        //UnityEngine.Debug.Log("get generic arg of "+ method);
                        genericArgInfos = method.GetGenericArguments();
                    }
                    bool paramMatch = paramInfos.Length == paramCount && method.Name == methodName;
                    if (paramMatch && genericArgCount > 0) // need a generic method
                    {
                        if (!method.IsGenericMethodDefinition || genericArgInfos.Length != genericArgCount)
                        {
                            paramMatch = false;
                        }
                    }
                    if (paramMatch)
                    {
                        for (int j = 0; j < paramCount; j++)
                        {
                            string strMatchInfo = paramMatchInfo[j] as string;
                            if (strMatchInfo != null)
                            {
                                if (!method.IsGenericMethodDefinition)
                                {
                                    paramMatch = false;
                                    break;
                                }
                                strMatchInfo = System.Text.RegularExpressions.Regex
                                    .Replace(strMatchInfo, @"!!\d+", m =>
                                        genericArgInfos[int.Parse(m.Value.Substring(2))].Name);
                                if (strMatchInfo != paramInfos[j].ParameterType.ToString())
                                {
                                    //Console.WriteLine("gp not match:" + strMatchInfo + " ??? "
                                    //    + paramInfos[j].ParameterType.ToString());
                                    paramMatch = false;
                                    break;
                                }
                            }
                            else
                            {
                                if ((paramMatchInfo[j] as Type) != paramInfos[j].ParameterType)
                                {
                                    //Console.WriteLine("pt not match:" + paramMatchInfo[j] + " ??? "
                                    //    + paramInfos[j].ParameterType);
                                    paramMatch = false;
                                    break;
                                }
                            }
                        }
                    }
                    if (paramMatch)
                    {
                        matchMethod = method;
                        break;
                    }
                }
                if (matchMethod == null)
                {
                    throw new Exception("can not load generic method [" + methodName + "] of " + declaringType);
                }
                return matchMethod.MakeGenericMethod(genericArgs);
            }
            else
            {
                Type declaringType = externTypes[reader.ReadInt32()];
                string methodName = reader.ReadString();
                int paramCount = reader.ReadInt32();
                //Console.WriteLine("load no generic method: " + declaringType + " ?? " + methodName + " pc "
                //    + paramCount);
                Type[] paramTypes = new Type[paramCount];
                for (int j = 0; j < paramCount; j++)
                {
                    paramTypes[j] = externTypes[reader.ReadInt32()];
                }
                bool isConstructor = methodName == ".ctor" || methodName == ".cctor";
                MethodBase externMethod = null;
                //StringBuilder sb = new StringBuilder();
                //sb.Append("method to find name: ");
                //sb.AppendLine(methodName);
                //for (int j = 0; j < paramCount; j++)
                //{
                //    sb.Append("p ");
                //    sb.Append(j);
                //    sb.Append(": ");
                //    sb.AppendLine(paramTypes[j].ToString());
                //}
                if (isConstructor)
                {
                    externMethod = declaringType.GetConstructor(BindingFlags.Public | (methodName == ".ctor" ?
                        BindingFlags.Instance : BindingFlags.Static) |
                        BindingFlags.NonPublic, null, paramTypes, null);
                    // : (MethodBase)(declaringType.GetMethod(methodName, paramTypes));
                }
                else
                {
                    foreach (var method in declaringType.GetMethods(flag))
                    {
                        if (method.Name == methodName && !method.IsGenericMethodDefinition
                            && method.GetParameters().Length == paramCount)
                        {
                            var methodParameterTypes = method.GetParameters().Select(p => p.ParameterType);
                            if (methodParameterTypes.SequenceEqual(paramTypes))
                            {
                                externMethod = method;
                                break;
                            }
                            //else
                            //{
                            //    var mptlist = methodParameterTypes.ToList();
                            //    for (int j = 0; j < mptlist.Count; j++)
                            //    {
                            //        sb.Append("not match p ");
                            //        sb.Append(j);
                            //        sb.Append(": ");
                            //        sb.AppendLine(mptlist[j].ToString());
                            //    }
                            //}
                        }
                    }
                }
                if (externMethod == null)
                {
                    throw new Exception("can not load method [" + methodName + "] of "
                        + declaringType/* + ", info:\r\n" + sb.ToString()*/);
                }
                return externMethod;
            }
        }

        static FieldInfo getRedirectField(MethodBase method)
        {
            var redirectTypeName = "IFix.RedirectTo." + method.DeclaringType.AssemblyQualifiedName;
            var redirectType = Type.GetType(redirectTypeName);
            if (redirectType == null)
            {
                throw new Exception("cat not find redirect type: " + redirectTypeName);
            }
            IDTagAttribute id = Attribute.GetCustomAttribute(method, typeof(IDTagAttribute), false) as IDTagAttribute;
            var redirectFieldName = string.Format("_rf_{0}{1}", method.Name, id == null ? 0 : id.ID);
            var redirectField = redirectType.GetField(redirectFieldName, BindingFlags.DeclaredOnly | BindingFlags.Static
                | BindingFlags.Public);
            if (redirectField == null)
            {
                throw new Exception(string.Format("cat not find redirect field: {0}, for {1}", redirectFieldName,
                    redirectType));
            }
            return redirectField;
        }

        static int getMapId(List<Type> idMapArray, MethodBase method)
        {
            IDTagAttribute id = Attribute.GetCustomAttribute(method, typeof(IDTagAttribute), false) as IDTagAttribute;
            int overrideId = id == null ? 0 : id.ID;
            var fieldName = string.Format("{0}-{1}{2}", method.DeclaringType.FullName.Replace('.', '-')
                .Replace('+', '-'), method.Name, overrideId);
            FieldInfo field = null;
            
            for (int i = 0; i < idMapArray.Count; i++)
            {
                field = idMapArray[i].GetField(fieldName);
                if (field != null) break;
            }
            if (field == null)
            {
                throw new Exception(string.Format("cat not find id field: {0}, for {1}", fieldName, method));
            }
            return (int)field.GetValue(null);
        }

        static int[] readSlotInfo(BinaryReader reader, Dictionary<MethodInfo, int> itfMethodToId, Type[] externTypes,
            int maxId)
        {
            int interfaceCount = reader.ReadInt32();

            if (interfaceCount == 0) return null;

            int[] slots = new int[maxId + 1];
            for (int j = 0; j < slots.Length; j++)
            {
                slots[j] = -1;
            }
            
            //VirtualMachine._Info(string.Format("-------{0}----------", interfaceCount));
            for (int i = 0; i < interfaceCount; i++)
            {
                var itfId = reader.ReadInt32();
                var itf = externTypes[itfId];
                //VirtualMachine._Info(itf.ToString());
                foreach (var method in itf.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public
                    | BindingFlags.Instance))
                {
                    int methodId = reader.ReadInt32();
                    if (!itfMethodToId.ContainsKey(method))
                    {
                        throw new Exception("can not find slot for " + method + " of " + itf);
                    }
                    slots[itfMethodToId[method]] = methodId;
                    //VirtualMachine._Info(string.Format("<<< {0} [{1}]", method, methodId));
                }
            }
            return slots;
        }

        // #lizard forgives
        unsafe static public VirtualMachine Load(Stream stream, bool checkNew = true)
        {
            List<IntPtr> nativePointers = new List<IntPtr>();

            IntPtr nativePointer;
            Instruction** unmanagedCodes = null;
            Type[] externTypes;
            MethodBase[] externMethods;
            List<ExceptionHandler[]> exceptionHandlers = new List<ExceptionHandler[]>();
            Dictionary<int, NewFieldInfo> newFieldInfo = new Dictionary<int, NewFieldInfo>();
            string[] internStrings;
            FieldInfo[] fieldInfos;
            Type[] staticFieldTypes;
            int[] cctors;
            AnonymousStoreyInfo[] anonymousStoreyInfos;

            using (BinaryReader reader = new BinaryReader(stream))
            {
                var instructionMagic = reader.ReadUInt64();
                if (instructionMagic != Instruction.INSTRUCTION_FORMAT_MAGIC)
                {
                    throw new Exception("instruction magic not match, expect "
                        + Instruction.INSTRUCTION_FORMAT_MAGIC
                        + ", but got " + instructionMagic);
                }

                var interfaceBridgeTypeName = reader.ReadString();
                var interfaceBridgeType = Type.GetType(interfaceBridgeTypeName);
                if (interfaceBridgeType == null)
                {
                    throw new Exception("assembly may be not injected yet, cat find "
                        + interfaceBridgeTypeName);
                }

                //BindingFlags flag = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static
                //    | BindingFlags.NonPublic |BindingFlags.Public;
                int externTypeCount = reader.ReadInt32();
                externTypes = new Type[externTypeCount];
                for (int i = 0; i < externTypeCount; i++)
                {
                    var assemblyQualifiedName = reader.ReadString();
                    externTypes[i] = Type.GetType(assemblyQualifiedName);
                    if (externTypes[i] == null)
                    {
                        throw new Exception("can not load type [" + assemblyQualifiedName + "]");
                    }
                }

                int methodCount = reader.ReadInt32();

                nativePointer = System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(Instruction*) * methodCount);
                unmanagedCodes = (Instruction**)nativePointer.ToPointer();
                nativePointers.Add(nativePointer);

                for (int j = 0; j < methodCount; j++)
                {
                    //Console.WriteLine("==================method" + j + "==================");
                    int codeSize = reader.ReadInt32();
                    nativePointer = System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(Instruction) * codeSize);
                    var unmanagedCode = (Instruction*)nativePointer.ToPointer();
                    for (int i = 0; i < codeSize; i++)
                    {
                        unmanagedCode[i].Code = (Code)reader.ReadInt32();
                        unmanagedCode[i].Operand = reader.ReadInt32();
                        //Console.WriteLine(i + " Code=" + unmanagedCode[i].Code + " Operand="
                        //    + unmanagedCode[i].Operand);
                    }
                    unmanagedCodes[j] = unmanagedCode;
                    nativePointers.Add(nativePointer);
                    ExceptionHandler[] ehsOfMethod = new ExceptionHandler[reader.ReadInt32()];
                    for (int i = 0; i < ehsOfMethod.Length; i++)
                    {
                        ExceptionHandler ehOfMethod = new ExceptionHandler();
                        ehOfMethod.HandlerType = (ExceptionHandlerType)reader.ReadInt32();
                        ehOfMethod.CatchTypeId = reader.ReadInt32();
                        ehOfMethod.TryStart = reader.ReadInt32();
                        ehOfMethod.TryEnd = reader.ReadInt32();
                        ehOfMethod.HandlerStart = reader.ReadInt32();
                        ehOfMethod.HandlerEnd = reader.ReadInt32();
                        ehsOfMethod[i] = ehOfMethod;
                        if (ehOfMethod.HandlerType == ExceptionHandlerType.Catch)
                        {
                            ehOfMethod.CatchType = ehOfMethod.CatchTypeId == -1 ?
                                typeof(object) : externTypes[ehOfMethod.CatchTypeId];
                        }
                    }
                    exceptionHandlers.Add(ehsOfMethod);
                }

                int externMethodCount = reader.ReadInt32();
                externMethods = new MethodBase[externMethodCount];
                for (int i = 0; i < externMethodCount; i++)
                {
                    externMethods[i] = readMethod(reader, externTypes);
                }

                int internStringsCount = reader.ReadInt32();
                internStrings = new string[internStringsCount];
                for (int i = 0; i < internStringsCount; i++)
                {
                    internStrings[i] = reader.ReadString();
                }

                fieldInfos = new FieldInfo[reader.ReadInt32()];
                for (int i = 0; i < fieldInfos.Length; i++)
                {
                    var isNewField = reader.ReadBoolean();
                    var declaringType = externTypes[reader.ReadInt32()];
                    var fieldName = reader.ReadString();
                    
                    fieldInfos[i] = declaringType.GetField(fieldName, 
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

                    if(!isNewField)
                    {
                        if(fieldInfos[i] == null)
                        {
                            throw new Exception("can not load field [" + fieldName + "] " + " of " + declaringType);
                        }
                    }
                    else
                    {
                        var fieldType = externTypes[reader.ReadInt32()];
                        var methodId = reader.ReadInt32();
                        
                        if(fieldInfos[i] == null)
                        {
                            newFieldInfo.Add(i, new NewFieldInfo{
                                Name = fieldName,
                                FieldType = fieldType,
                                DeclaringType = declaringType,
                                MethodId = methodId,
                            });
                        }
                        else
                        {
                            if(fieldInfos[i].FieldType != fieldType)
                            {
                                throw new Exception("can not change existing field [" + declaringType + "." + fieldName + "]'s type " + " from " + fieldInfos[i].FieldType + " to " + fieldType);
                            }
                            else
                            {
                                throw new Exception(declaringType + "." + fieldName + " is expected to be a new field , but it already exists ");
                            }
                        }
                    }
                }

                staticFieldTypes = new Type[reader.ReadInt32()];
                cctors = new int[staticFieldTypes.Length];
                for (int i = 0; i < staticFieldTypes.Length; i++)
                {
                    staticFieldTypes[i] = externTypes[reader.ReadInt32()];
                    cctors[i] = reader.ReadInt32();
                }

                Dictionary<MethodInfo, int> itfMethodToId = new Dictionary<MethodInfo, int>();
                int maxId = 0;

                foreach (var itf in interfaceBridgeType.GetInterfaces())
                {
                    InterfaceMapping map = interfaceBridgeType.GetInterfaceMap(itf);
                    for (int i = 0; i < map.InterfaceMethods.Length; i++)
                    {
                        IDTagAttribute idTag = Attribute.GetCustomAttribute(map.TargetMethods[i],
                            typeof(IDTagAttribute), false) as IDTagAttribute;
                        MethodInfo im = map.InterfaceMethods[i];
                        if (idTag == null)
                        {
                            throw new Exception(string.Format("can not find id for {0}", im));
                        }
                        int id = idTag.ID;
                        //VirtualMachine._Info(string.Format("{0} [{1}]", im, id));
                        maxId = id > maxId ? id : maxId;
                        itfMethodToId.Add(im, id);
                    }
                }

                anonymousStoreyInfos = new AnonymousStoreyInfo[reader.ReadInt32()];
                for (int i = 0; i < anonymousStoreyInfos.Length; i++)
                {
                    int fieldNum = reader.ReadInt32();
                    int[] fieldTypes = new int[fieldNum];
                    for (int fieldIdx = 0; fieldIdx < fieldNum; ++fieldIdx)
                    {
                        fieldTypes[fieldIdx] = reader.ReadInt32();
                    }
                    int ctorId = reader.ReadInt32();
                    int ctorParamNum = reader.ReadInt32();
                    var slots = readSlotInfo(reader, itfMethodToId, externTypes, maxId);
                    
                    int virtualMethodNum = reader.ReadInt32();
                    int[] vTable = new int[virtualMethodNum];
                    for (int vm = 0 ;vm < virtualMethodNum; vm++)
                    {
                        vTable[vm] = reader.ReadInt32();
                    }
                    anonymousStoreyInfos[i] = new AnonymousStoreyInfo()
                    {
                        CtorId = ctorId,
                        FieldNum = fieldNum,
                        FieldTypes = fieldTypes,
                        CtorParamNum = ctorParamNum,
                        Slots = slots,
                        VTable = vTable
                    };
                }


                var virtualMachine = new VirtualMachine(unmanagedCodes, () =>
                {
                    for (int i = 0; i < nativePointers.Count; i++)
                    {
                        System.Runtime.InteropServices.Marshal.FreeHGlobal(nativePointers[i]);
                    }
                })
                {
                    ExternTypes = externTypes,
                    ExternMethods = externMethods,
                    ExceptionHandlers = exceptionHandlers.ToArray(),
                    InternStrings = internStrings,
                    FieldInfos = fieldInfos,
                    NewFieldInfos = newFieldInfo,
                    AnonymousStoreyInfos = anonymousStoreyInfos,
                    StaticFieldTypes = staticFieldTypes,
                    Cctors = cctors
                };

                var wrappersManagerImplName = reader.ReadString();
                WrappersManager wrapperManager = Activator.CreateInstance(Type.GetType(wrappersManagerImplName, true),
                    virtualMachine) as WrappersManager;
                if (wrapperManager == null)
                {
                    throw new Exception("can not create WrappersManager!");
                }
                virtualMachine.WrappersManager = wrapperManager;

                var assemblyStr = reader.ReadString();
                var idMapList = new List<Type>();
                for(int i = 0; i < 100; i++)
                {

                    var idMapType = Type.GetType("IFix.IDMAP" + i + assemblyStr, false);
                    if (idMapType == null) break;
                    idMapList.Add(idMapType);
                }

                lock (removers)
                {
                    var assembly = wrapperManager.GetType().Assembly;
                    Action remover;
                    if (removers.TryGetValue(assembly, out remover))
                    {
                        removers.Remove(assembly);
                        remover();
                    }

                    //int fixCount = reader.ReadInt32();
                    //FieldInfo[] toSet = new FieldInfo[fixCount];

                    //for (int i = 0; i < fixCount; i++)
                    //{
                    //    var fixMethod = readMethod(reader, externTypes);
                    //    var fixMethodIdx = reader.ReadInt32();
                    //    var redirectField = getRedirectField(fixMethod);
                    //    toSet[i] = redirectField;
                    //    var wrapper = wrapperManager.CreateWrapper(fixMethodIdx);
                    //    if (wrapper == null)
                    //    {
                    //        throw new Exception("create wrapper fail");
                    //    }
                    //    redirectField.SetValue(null, wrapper);
                    //}

                    //removers[assembly] = () =>
                    //{
                    //    for (int i = 0; i < fixCount; i++)
                    //    {
                    //        toSet[i].SetValue(null, null);
                    //    }
                    //};

                    int fixCount = reader.ReadInt32();
                    int[] methodIdArray = new int[fixCount];
                    int[] posArray = new int[fixCount];
                    int maxPos = -1;
                    for (int i = 0; i < fixCount; i++)
                    {
                        var fixMethod = readMethod(reader, externTypes);
                        var fixMethodId = reader.ReadInt32();
                        var pos = getMapId(idMapList, fixMethod);
                        methodIdArray[i] = fixMethodId;
                        posArray[i] = pos;
                        if (pos > maxPos)
                        {
                            maxPos = pos;
                        }
                    }
                    Array arr = wrapperManager.InitWrapperArray(maxPos + 1) as Array;
                    for (int i = 0; i < fixCount; i++)
                    {
                        var wrapper = wrapperManager.CreateWrapper(methodIdArray[i]);
                        if (wrapper == null)
                        {
                            throw new Exception("create wrapper fail");
                        }
                        arr.SetValue(wrapper, posArray[i]);
                    }
                    removers[assembly] = () =>
                    {
                        wrapperManager.InitWrapperArray(0);
                    };
                }

                if (checkNew)
                {
                    int newClassCount = reader.ReadInt32();
                    for (int i = 0; i < newClassCount; i++)
                    {
                        var newClassFullName = reader.ReadString();
                        var newClassName = Type.GetType(newClassFullName);
                        if (newClassName != null)
                        {
                            throw new Exception(newClassName + " class is expected to be a new class , but it already exists ");
                        }
                    }
                }

                return virtualMachine;
            }
        }

        public static void Unload(Assembly assembly)
        {
            lock (removers)
            {
                Action remover;
                if (removers.TryGetValue(assembly, out remover))
                {
                    removers.Remove(assembly);
                    remover();
                }
            }
        }
    }
}