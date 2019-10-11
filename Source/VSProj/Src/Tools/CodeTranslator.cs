/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace IFix
{
    enum ProcessMode
    {
        Inject,
        Patch
    }

    class CodeTranslator
    {
        private Dictionary<int, List<Core.Instruction>> codes = new Dictionary<int, List<Core.Instruction>>();
        private HashSet<int> codeMustWriteToPatch = new HashSet<int>();
        private Dictionary<MethodReference, int> methodToId = new Dictionary<MethodReference, int>();
        private Dictionary<int, Core.ExceptionHandler[]> methodIdToExceptionHandler =
            new Dictionary<int, Core.ExceptionHandler[]>();

        private List<TypeReference> externTypes = new List<TypeReference>();
        private List<TypeReference> contextTypeOfExternType = new List<TypeReference>();
        private Dictionary<TypeReference, int> externTypeToId = new Dictionary<TypeReference, int>();
        private Dictionary<string, TypeReference> nameToExternType = new Dictionary<string, TypeReference>();
        private List<MethodReference> externMethods = new List<MethodReference>();
        private Dictionary<MethodReference, int> externMethodToId = new Dictionary<MethodReference, int>();

        private List<string> internStrings = new List<string>();
        private Dictionary<string, int> internStringsToId = new Dictionary<string, int>();

        private List<FieldReference> fields = new List<FieldReference>();
        private List<FieldDefinition> fieldsStoreInVirtualMachine = new List<FieldDefinition>();
        private Dictionary<FieldReference, int> fieldToId = new Dictionary<FieldReference, int>();

        const string Wrap_Perfix = "__Gen_Wrap_";

        int nextAllocId = 0;

        bool isCompilerGenerated(TypeReference type)
        {
            if (type.IsGenericInstance)
            {
                return isCompilerGenerated((type as GenericInstanceType).ElementType);
            }
            var td = type as TypeDefinition;
            if (td != null && td.IsNested)
            {
                if (isCompilerGenerated(td.DeclaringType))
                {
                    return true;
                }
            }
            return td != null && !td.IsInterface && td
                .CustomAttributes
                .Any(ca => ca.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");
        }

        bool isCompilerGenerated(MethodReference method)
        {
            if (method.IsGenericInstance)
            {
                return isCompilerGenerated((method as GenericInstanceMethod).ElementMethod);
            }
            var md = method as MethodDefinition;
            return md != null && md.CustomAttributes.Any(ca => ca.AttributeType.FullName 
            == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");
        }

        bool isCompilerGenerated(FieldReference field)
        {
            var fd = field as FieldDefinition;
            return fd != null && fd.CustomAttributes.Any(ca => ca.AttributeType.FullName 
            == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");
        }

        bool isCompilerGeneratedPlainObject(TypeReference type)
        {
            var td = type as TypeDefinition;

            return td != null
                && !td.IsInterface
                //&& td.Interfaces.Count == 0
                && isCompilerGenerated(type)
                && td.BaseType.IsSameType(objType);
        }

        bool isCompilerGeneratedByNotPlainObject(TypeReference type)
        {
            var td = type as TypeDefinition;

            return (type.IsGenericInstance || (td != null
                && !td.IsInterface
                //&& (!td.BaseType.IsSameType(objType) || td.Interfaces.Count != 0)))
                && !td.BaseType.IsSameType(objType)))
                && isCompilerGenerated(type);
        }

        Dictionary<TypeDefinition, HashSet<FieldDefinition>> typeToSpecialGeneratedFields 
            = new Dictionary<TypeDefinition, HashSet<FieldDefinition>>();

        Dictionary<TypeDefinition, int> typeToCctor = new Dictionary<TypeDefinition, int>();

        /// <summary>
        /// 获取简写属性（例如public int a{get;set;}），事件等所生成的字段
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        HashSet<FieldDefinition> getSpecialGeneratedFields(TypeDefinition type)
        {
            HashSet<FieldDefinition> ret;
            if (!typeToSpecialGeneratedFields.TryGetValue(type, out ret))
            {
                ret = new HashSet<FieldDefinition>();
                typeToSpecialGeneratedFields[type] = ret;
                if (!typeToCctor.ContainsKey(type))
                {
                    typeToCctor[type] = -1;
                    var cctor = type.Methods.FirstOrDefault(m => m.Name == ".cctor");
                    if (cctor != null)
                    {
                        var cctorInfo = getMethodId(cctor, null, false, InjectType.Redirect);
                        typeToCctor[type] = cctorInfo.Type == CallType.Internal ? cctorInfo.Id : -2;
                    }
                }
                
                foreach (var field in ( from method in type.Methods
                                        where method.IsSpecialName && method.Body != null 
                                            && method.Body.Instructions != null
                                        from instruction in method.Body.Instructions
                                        where instruction.OpCode.Code == Code.Ldsfld
                                            || instruction.OpCode.Code == Code.Stsfld
                                            || instruction.OpCode.Code == Code.Ldsflda
                                        where isCompilerGenerated(instruction.Operand as FieldReference)
                                        select (instruction.Operand as FieldReference).Resolve()).Distinct())
                {
                    ret.Add(field);
                }
            }
            return ret;
        }

        //再补丁新增一个对原生方法的引用
        int addExternType(TypeReference type, TypeReference contextType = null)
        {
            if (type.IsGenericParameter || type.HasGenericArgumentFromMethod())
            {
                throw new InvalidProgramException("try to use a generic type definition");
            }
            if (externTypeToId.ContainsKey(type))
            {
                return externTypeToId[type];
            }
            if (isCompilerGenerated(type))
            {
                throw new Exception(type + " is CompilerGenerated");
            }
            TypeReference theSameNameType;
            var typeName = type.GetAssemblyQualifiedName(contextType);
            if (nameToExternType.TryGetValue(typeName, out theSameNameType))
            {
                var ret = addExternType(theSameNameType, contextType);
                externTypeToId.Add(type, ret);
                return ret;
            }
            nameToExternType.Add(typeName, type);
            externTypeToId.Add(type, externTypes.Count);
            externTypes.Add(type);
            contextTypeOfExternType.Add(contextType);
            return externTypes.Count - 1;
        }

        //假如是注入模式，而且该函数配置是IFix的话，不需要真的为其访问的资源分配id
        //TODO: 更理想的做法是剥离一个分析代码流程，仅分析要生产哪些适配器，反向适配器，反剪裁配置
        bool doNoAdd(MethodDefinition caller)
        {
            InjectType injectType;
            return mode == ProcessMode.Inject && caller != null && methodToInjectType.TryGetValue(caller,
                out injectType) && injectType == InjectType.Switch;
        }

        //原生字段
        int addRefField(FieldReference field, MethodDefinition caller)
        {
            if (doNoAdd(caller))
            {
                return int.MaxValue;
            }

            int id;
            if (!fieldToId.TryGetValue(field, out id))
            {
                id = fields.Count;
                fieldToId.Add(field, id);
                fields.Add(field);
                addExternType(field.DeclaringType);
            }
            return id;
        }

        //虚拟机存储字段
        int addStoreField(FieldDefinition field, MethodDefinition caller)
        {
            if (doNoAdd(caller))
            {
                return int.MaxValue;
            }

            int id;
            if (!fieldToId.TryGetValue(field, out id))
            {
                id = -(fieldsStoreInVirtualMachine.Count + 1);
                fieldToId.Add(field, id);
                fieldsStoreInVirtualMachine.Add(field);
                addExternType(isCompilerGenerated(field.FieldType) ? objType : field.FieldType);
            }
            return id;
        }

        //新增一个字符串字面值
        int addInternString(string str, MethodDefinition caller)
        {
            if (doNoAdd(caller))
            {
                return int.MaxValue;
            }

            int id;
            if (!internStringsToId.TryGetValue(str, out id))
            {
                id = internStrings.Count;
                internStrings.Add(str);
                internStringsToId.Add(str, id);
            }
            return id;
        }

        //原生方法的引用
        int addExternMethod(MethodReference callee, MethodDefinition caller)
        {
            if (doNoAdd(caller))
            {
                return ushort.MaxValue;
            }

            if (externMethodToId.ContainsKey(callee))
            {
                return externMethodToId[callee];
            }

            if (callee.IsGeneric())
            {
                throw new InvalidProgramException("try to call a generic method definition: " + callee 
                    + ", caller is:" + caller);
            }

            if (isCompilerGenerated(callee) && !(callee as MethodDefinition).IsSpecialName)
            {
                throw new Exception(callee + " is CompilerGenerated");
            }

            if (callee.IsGenericInstance)
            {
                foreach (var typeArg in ((GenericInstanceMethod)callee).GenericArguments)
                {
                    addExternType(typeArg);
                }
            }

            if (callee.ReturnType.IsGenericParameter)
            {
                var resolveType = (callee.ReturnType as GenericParameter).ResolveGenericArgument(callee.DeclaringType);
                if (resolveType != null)
                {
                    addExternType(resolveType);
                }
            }
            else if (!callee.ReturnType.HasGenericArgumentFromMethod())
            {
                addExternType(callee.ReturnType, callee.DeclaringType);
            }
            addExternType(callee.DeclaringType);
            foreach (var p in callee.Parameters)
            {
                if (p.ParameterType.IsGenericParameter)
                {
                    var resolveType = (p.ParameterType as GenericParameter).ResolveGenericArgument(
                        callee.DeclaringType);
                    if (resolveType != null)
                    {
                        addExternType(resolveType);
                    }
                }
                else if (!p.ParameterType.HasGenericArgumentFromMethod())
                {
                    addExternType(p.ParameterType, callee.DeclaringType);
                }
            }

            int methodId = externMethods.Count;
            if (methodId > ushort.MaxValue)
            {
                throw new OverflowException("too many extern methods");
            }
            externMethodToId[callee] = methodId;
            externMethods.Add(callee);
            return methodId;
        }

        HashSet<MethodDefinition> antiLoop = new HashSet<MethodDefinition>();
        Dictionary<MethodDefinition, bool> cacheCheckResult = new Dictionary<MethodDefinition, bool>();
        bool checkILAndGetOffset(MethodDefinition method,
            Mono.Collections.Generic.Collection<Instruction> instructions)
        {
            if (cacheCheckResult.ContainsKey(method)) return cacheCheckResult[method];
            if (antiLoop.Contains(method)) return true;
            antiLoop.Add(method);
            int p;
            bool ret = checkILAndGetOffset(method, instructions, null, out p);
            cacheCheckResult[method] = ret;
            return ret;
        }

        /// <summary>
        /// 判断一个名字是否是一个合法id
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static bool IsVaildIdentifierName(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            if (!char.IsLetter(text[0]) && text[0] != '_')
                return false;
            for (int ix = 1; ix < text.Length; ++ix)
                if (!char.IsLetterOrDigit(text[ix]) && text[ix] != '_')
                    return false;
            return true;
        }

        public bool isRefBySpecialMethodNoCache(FieldDefinition field)
        {
            foreach(var instructions in field.DeclaringType.Methods
                .Where(m => m.IsSpecialName && m.Body != null && m.Body.Instructions != null)
                .Select(m => m.Body.Instructions))
            {
                if (instructions.Any(i => i.Operand == field))
                {
                    return true;
                }
            }
            return false;
        }

        Dictionary<FieldDefinition, bool> isRefBySpecialMethodCache = new Dictionary<FieldDefinition, bool>();

        public bool isRefBySpecialMethod(FieldDefinition field)
        {
            bool ret;
            if (!isRefBySpecialMethodCache.TryGetValue(field, out ret))
            {
                ret = isRefBySpecialMethodNoCache(field);
                isRefBySpecialMethodCache.Add(field, ret);
            }
            return ret;
        }

        // #lizard forgives
        bool checkILAndGetOffset(MethodDefinition method,
            Mono.Collections.Generic.Collection<Instruction> instructions,
            Dictionary<Instruction, int> ilOffset, out int stopPos)
        {
            int offset = 0;
            stopPos = 0;
            for (int i = 0; i < instructions.Count; i++)
            {
                if (ilOffset != null)
                {
                    ilOffset.Add(instructions[i], offset + 1);
                }
                stopPos = i;
                //Console.WriteLine(i + " instruction:" + instructions[i].OpCode + " offset:" + offset);
                switch (instructions[i].OpCode.Code)
                {
                    case Code.Nop://先忽略
                        break;
                    case Code.Constrained:
                        {
                            TypeReference tr = instructions[i].Operand as TypeReference;
                            if (tr != null && !tr.IsGeneric())
                            {
                                offset += 2;
                                break;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    case Code.Ldc_I8:
                    case Code.Ldc_R8:
                    case Code.Leave:
                    case Code.Leave_S:
                        offset += 2;
                        break;
                    case Code.Switch:
                        Instruction[] jmpTargets = instructions[i].Operand as Instruction[];
                        offset += ((jmpTargets.Length + 1) >> 1) + 1;
                        break;
                    case Code.Castclass:
                    case Code.Initobj:
                    case Code.Newarr:
                    case Code.Stobj:
                    case Code.Box:
                    case Code.Isinst:
                    case Code.Unbox_Any:
                    case Code.Unbox:
                    case Code.Ldobj:
                    case Code.Ldtoken:
                        {
                            TypeReference tr = instructions[i].Operand as TypeReference;
                            if (tr != null && !tr.IsGeneric())
                            {
                                offset += 1;
                                break;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    case Code.Stfld:
                    case Code.Ldfld:
                    case Code.Ldflda:
                        {
                            FieldReference fr = instructions[i].Operand as FieldReference;
                            //如果是生成的字段，而且不是Getter/Setter/Adder/Remover
                            if (isCompilerGenerated(fr) && !method.IsSpecialName) 
                            {
                                if (!IsVaildIdentifierName(fr.Name)//不是合法名字，就肯定是随机变量
                                    //如果是合法名字，但不被任何SpecialName方法引用，也归为随机变量
                                    || !isRefBySpecialMethod(fr as FieldDefinition))

                                {
                                    return false;
                                }
                            }


                            if (fr != null/* && !fr.IsGeneric()*/)
                            {
                                offset += 1;
                                break;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    case Code.Stsfld:
                    case Code.Ldsfld:
                    case Code.Ldsflda:
                        {
                            FieldReference fr = instructions[i].Operand as FieldReference;
                            //如果访问了生成的静态字段，而且不能存到虚拟机，不是Getter/Setter/Adder/Remover
                            //if ((isCompilerGenerated(fr) || isCompilerGenerated(fr.DeclaringType)) 
                            //    && !isFieldStoreInVitualMachine(fr) && !method.IsSpecialName)
                            //{
                            //    return false;
                            //}
                            if (fr != null/* && !fr.IsGeneric()*/)
                            {
                                offset += 1;
                                break;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    case Code.Newobj:
                    case Code.Callvirt:
                    case Code.Call:
                    case Code.Ldftn:
                    case Code.Ldvirtftn:
                        {
                            //LINQ通常是ldftn，要验证ldftn所加载的函数是否含非法指令（不支持，或者引用了个生成字段，
                            //或者一个生成NotPlainObject）
                            MethodReference mr = instructions[i].Operand as MethodReference;
                            if (mr != null && !mr.IsGeneric() 
                                && !isCompilerGeneratedByNotPlainObject(mr.DeclaringType))
                            {
                                if (isCompilerGenerated(mr)
                                    || (/*instructions[i].OpCode.Code != Code.Newobj && */
                                    isCompilerGeneratedPlainObject(mr.DeclaringType)))
                                {
                                    var md = mr as MethodDefinition;
                                    if (md.Body != null && !checkILAndGetOffset(md, md.Body.Instructions))
                                    {
                                        //Console.WriteLine("check " + md + " fail il = " + md.Body.Instructions[p]
                                        //    + ",caller=" + method);
                                        return false;
                                    }
                                    //编译器生成类要检查所有实现方法
                                    if (instructions[i].OpCode.Code == Code.Newobj 
                                        && isCompilerGeneratedPlainObject(mr.DeclaringType))
                                    {
                                        foreach(var m in mr.DeclaringType.Resolve().Methods
                                            .Where(m => !m.IsConstructor))
                                        {
                                            if (m.Body != null && !checkILAndGetOffset(m, m.Body.Instructions))
                                            {
                                                //Console.WriteLine("check " + md + " fail il = " 
                                                //   + md.Body.Instructions[p] + ",caller=" + method);
                                                return false;
                                            }
                                        }
                                    }
                                }
                                offset += 1;
                                break;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    case Code.Conv_I: //Convert to native int, pushing native int on stack.
                    //case Code.Conv_U: //Convert to unsigned native int, pushing native int on stack.
                    case Code.Conv_Ovf_U:
                    case Code.Conv_Ovf_U_Un:
                    case Code.Calli: // not support op
                    case Code.Cpobj:
                    case Code.Refanyval:
                    case Code.Ckfinite:
                    case Code.Mkrefany:
                    case Code.Arglist:
                    case Code.Localloc:
                    case Code.Endfilter:
                    case Code.Unaligned:
                    case Code.Tail:
                    case Code.Cpblk:
                    case Code.Initblk:
                    case Code.No:
                    case Code.Sizeof: // support?
                    case Code.Refanytype:
                    case Code.Readonly:
                        return false;
                    default:
                        offset += 1;
                        break;
                }
            }
            return true;
        }

        void processMethod(MethodDefinition method)
        {
            getMethodId(method, null);
        }

        Core.ExceptionHandler findExceptionHandler(Core.ExceptionHandler[] ehs, Core.ExceptionHandlerType type,
            int offset)
        {
            int dummy;
            return findExceptionHandler(ehs, type, offset, out dummy);
        }

        /// <summary>
        /// 查找一个指令异常时的异常处理块
        /// </summary>
        /// <param name="ehs">当前函数的所有异常处理块</param>
        /// <param name="type">异常类型</param>
        /// <param name="offset">指令偏移</param>
        /// <param name="idx">异常处理块的索引</param>
        /// <returns></returns>
        Core.ExceptionHandler findExceptionHandler(Core.ExceptionHandler[] ehs, Core.ExceptionHandlerType type,
            int offset, out int idx)
        {
            Core.ExceptionHandler ret = null;
            idx = -1;
            for (int i = 0; i < ehs.Length; i++)
            {
                var eh = ehs[i];
                if (eh.HandlerType == type && eh.TryStart <= offset && eh.TryEnd > offset)
                {
                    if (ret == null || ((eh.TryEnd - eh.TryStart) < (ret.TryEnd - ret.TryStart)))
                    {
                        ret = eh;
                        idx = i;
                    }
                }
            }
            return ret;
        }

        MethodDefinition findOverride(TypeDefinition type, MethodReference vmethod)
        {
            foreach (var method in type.Methods)
            {
                if (method.IsVirtual && !method.IsAbstract && isTheSameDeclare(method, vmethod))
                {
                    return method;
                }
            }
            return null;
        }

        bool isTheSameDeclare(MethodReference m1, MethodReference m2)
        {
            if (m1.Name == m2.Name && m1.ReturnType.IsSameName(m2.ReturnType)
                && m1.Parameters.Count == m2.Parameters.Count)
            {
                bool isParamsMatch = true;
                for (int i = 0; i < m1.Parameters.Count; i++)
                {
                    if (m1.Parameters[i].Attributes != m2.Parameters[i].Attributes
                        || !m1.Parameters[i].ParameterType.IsSameName(m2.Parameters[i].ParameterType))
                    {
                        isParamsMatch = false;
                        break;
                    }
                }
                return isParamsMatch;
            }
            return false;
        }

        MethodReference _findBase(TypeReference type, MethodDefinition method)
        {
            TypeDefinition td = type.Resolve();
            if (td == null)
            {
                return null;
            }

            var m = findOverride(td, method);
            if (m != null)
            {
                if (type.IsGenericInstance)
                {
                    return m.MakeGeneric(method.DeclaringType);
                }
                else
                {
                    return m.TryImport(method.DeclaringType.Module);
                }
            }
            return _findBase(td.BaseType, method);
        }

        MethodReference findBase(TypeDefinition type, MethodDefinition method)
        {
            if (method.IsVirtual && !method.IsNewSlot) //表明override
            {
                try
                {
                    //TODO: 如果后续支持泛型解析，需要考虑这块的实现，xlua目前泛型直接不支持base调用
                    return _findBase(type.BaseType, method);
                }
                catch { }
            }
            return null;
        }

        const string BASE_RPOXY_PERFIX = "<>iFixBaseProxy_";

        //方案2
        //var method = typeof(object).GetMethod("ToString");
        //var ftn = method.MethodHandle.GetFunctionPointer();
        //var func = (Func<string>)Activator.CreateInstance(typeof(Func<string>), obj, ftn);
        MethodDefinition tryAddBaseProxy(TypeDefinition type, MethodDefinition method)
        {
            var mbase = findBase(type, method);
            if (mbase != null)
            {
                var proxyMethod = new MethodDefinition(BASE_RPOXY_PERFIX + method.Name, MethodAttributes.Private,
                    method.ReturnType);
                for(int i = 0; i < method.Parameters.Count; i++)
                {
                    proxyMethod.Parameters.Add(new ParameterDefinition("P" + i, method.Parameters[i].IsOut
                        ? ParameterAttributes.Out : ParameterAttributes.None, method.Parameters[i].ParameterType));
                }
                var instructions = proxyMethod.Body.Instructions;
                var ilProcessor = proxyMethod.Body.GetILProcessor();
                int paramCount = method.Parameters.Count + 1;
                for(int i = 0; i < paramCount; i++)
                {
                    emitLdarg(instructions, ilProcessor, i);
                    if (i == 0 && type.IsValueType)
                    {
                        instructions.Add(Instruction.Create(OpCodes.Ldobj, type));
                        instructions.Add(Instruction.Create(OpCodes.Box, type));
                    }
                }
                instructions.Add(Instruction.Create(OpCodes.Call, mbase));
                instructions.Add(Instruction.Create(OpCodes.Ret));
                type.Methods.Add(proxyMethod);
                return proxyMethod;
            }
            return null;
        }

        enum CallType
        {
            Extern,
            Internal,
            Invalid
        }

        struct MethodIdInfo
        {
            public int Id;
            public CallType Type;
        }

        enum InjectType
        {
            Redirect,
            Switch
        }

        bool isFieldStoreInVitualMachine(FieldReference field)
        {
            var fieldDef = field.Resolve();
            if (fieldDef == null)
            {
                return false;
            }
            if (!fieldDef.IsStatic)
            {
                return false;
            }

            if (!isCompilerGenerated(field) && !isCompilerGenerated(field.DeclaringType))
            {
                return false;
            }

            if (field.FieldType.Resolve().IsDelegate())
            {
                return true;
            }

            //TODO: switch(str)

            return false;
        }

        bool isNewMethod(MethodDefinition method)
        {
            return configure.IsNewMethod(method);
        }


        Dictionary<MethodDefinition, int> interpretMethods = new Dictionary<MethodDefinition, int>();
        void addInterpretMethod(MethodDefinition method, int methodId)
        {
            if (method.IsGenericInstance || method.HasGenericParameters)
            {
                throw new NotSupportedException("generic method definition");
            }
            addExternType(method.ReturnType, method.DeclaringType);
            addExternType(method.DeclaringType);
            foreach(var pinfo in method.Parameters)
            {
                addExternType(pinfo.ParameterType, method.DeclaringType);
            }
            interpretMethods.Add(method, methodId);
        }

        bool isFieldAccessInject(MethodDefinition method, int methodId)
        {
            return false;
        }

        //字段注入方式处理逻辑
        //目前用不上，但后续支持泛型修复需要用到
        void fieldAccessInject(InjectType injectType, MethodDefinition method, int methodId)
        {
            var redirectBridge = getRedirectField(method);
            var body = method.Body;
            var msIls = body.Instructions;
            var ilProcessor = body.GetILProcessor();

            var redirectTo = getWrapperMethod(wrapperType, anonObjOfWrapper, method, false, false);
            Instruction insertPoint;
            if (injectType == InjectType.Redirect)
            {
                msIls.Clear();
                body.ExceptionHandlers.Clear();
                body.Variables.Clear();

                msIls.Add(Instruction.Create(OpCodes.Ret));
                insertPoint = msIls[0];
                ilProcessor.InsertBefore(insertPoint, ilProcessor.Create(OpCodes.Ldsfld, redirectBridge));
            }
            else
            {
                ilProcessor.InsertBefore(msIls[0], Instruction.Create(OpCodes.Ret));
                insertPoint = msIls[0];
                var redirectBridgeTmp = new VariableDefinition(wrapperType);
                method.Body.Variables.Add(redirectBridgeTmp);
                ilProcessor.InsertBefore(insertPoint, ilProcessor.Create(OpCodes.Ldsfld, redirectBridge));
                ilProcessor.InsertBefore(insertPoint, ilProcessor.Create(OpCodes.Stloc, redirectBridgeTmp));
                ilProcessor.InsertBefore(insertPoint, ilProcessor.Create(OpCodes.Ldloc, redirectBridgeTmp));
                ilProcessor.InsertBefore(insertPoint, ilProcessor.Create(OpCodes.Brfalse, insertPoint.Next));
                ilProcessor.InsertBefore(insertPoint, ilProcessor.Create(OpCodes.Ldloc, redirectBridgeTmp));
            }

            int argPos = 0;

            if (method.HasThis)
            {
                ilProcessor.InsertBefore(insertPoint, createLdarg(ilProcessor, 0));
                argPos = 1;
            }

            for (int i = 0; i < method.Parameters.Count; i++)
            {
                ilProcessor.InsertBefore(insertPoint, createLdarg(ilProcessor, argPos++));
                var ptype = method.Parameters[i].ParameterType;
                if (wrapperParamerterType(ptype) != ptype && ptype.IsValueType)
                {
                    ilProcessor.InsertBefore(insertPoint, Instruction.Create(OpCodes.Box, ptype));
                }
            }
            ilProcessor.InsertBefore(insertPoint, Instruction.Create(OpCodes.Callvirt, redirectTo));
        }

        //id注入方式处理逻辑
        void idAccessInject(InjectType injectType, MethodDefinition method, int methodId)
        {
            addRedirectIdInfo(method, methodId);
            var body = method.Body;
            var msIls = body.Instructions;
            var ilProcessor = body.GetILProcessor();

            var redirectTo = getWrapperMethod(wrapperType, anonObjOfWrapper, method, false, false);
            Instruction insertPoint;
            if (injectType == InjectType.Redirect)
            {
                msIls.Clear();
                body.ExceptionHandlers.Clear();
                body.Variables.Clear();

                msIls.Add(Instruction.Create(OpCodes.Ret));
                insertPoint = msIls[0];
            }
            else
            {
                ilProcessor.InsertBefore(msIls[0], Instruction.Create(OpCodes.Ret));
                insertPoint = msIls[0];
                ilProcessor.InsertBefore(insertPoint, createLdcI4(methodId));
                ilProcessor.InsertBefore(insertPoint, ilProcessor.Create(OpCodes.Call, isPatched));
                ilProcessor.InsertBefore(insertPoint, ilProcessor.Create(OpCodes.Brfalse, insertPoint.Next));
            }

            ilProcessor.InsertBefore(insertPoint, createLdcI4(methodId));
            ilProcessor.InsertBefore(insertPoint, Instruction.Create(OpCodes.Call, getPatch));

            int argPos = 0;

            if (method.HasThis)
            {
                ilProcessor.InsertBefore(insertPoint, createLdarg(ilProcessor, 0));
                argPos = 1;
            }

            for (int i = 0; i < method.Parameters.Count; i++)
            {
                ilProcessor.InsertBefore(insertPoint, createLdarg(ilProcessor, argPos++));
                var ptype = method.Parameters[i].ParameterType;
                if (wrapperParamerterType(ptype) != ptype && ptype.IsValueType)
                {
                    ilProcessor.InsertBefore(insertPoint, Instruction.Create(OpCodes.Box, ptype));
                }
            }
            ilProcessor.InsertBefore(insertPoint, Instruction.Create(OpCodes.Callvirt, redirectTo));
        }

        void injectMethod(MethodDefinition method, int methodId)
        {
            InjectType injectType;
            if (methodToInjectType.TryGetValue(method, out injectType))
            {
                if (mode == ProcessMode.Patch || injectType == InjectType.Redirect)
                {
                    addInterpretMethod(method, methodId);
                }
                if (isFieldAccessInject(method, methodId))
                {
                    fieldAccessInject(injectType, method, methodId);
                }
                else
                {
                    idAccessInject(injectType, method, methodId);
                }
            }
        }

        int allocMethodId(MethodDefinition method)
        {
            int methodId = nextAllocId++;

            methodToId.Add(method, methodId);

            if (methodId > ushort.MaxValue)
            {
                throw new OverflowException("too many internal methods");
            }
            return methodId;
        }

        /// <summary>
        /// 获取一个函数的id
        /// 该函数会触发指令序列生成
        /// </summary>
        /// <param name="callee">被调用函数</param>
        /// <param name="caller">调用者</param>
        /// <param name="directCallVirtual">是个虚函数，会生成指令序列，
        /// 但是调用通过反射来调用</param>
        /// <param name="callerInjectType">调用者的注入类型</param>
        /// <returns>负数表示需要反射访问原生，0或正数是指令数组下标</returns>
        // #lizard forgives
        unsafe MethodIdInfo getMethodId(MethodReference callee, MethodDefinition caller,
            bool directCallVirtual = false, InjectType callerInjectType = InjectType.Switch)
        {
            //Console.WriteLine("callee:" + callee + ", caller:" + caller);
            MethodDefinition method = callee as MethodDefinition;

            if (!directCallVirtual && externMethodToId.ContainsKey(callee))
            {
                return new MethodIdInfo()
                {
                    Id = addExternMethod(callee, caller),
                    Type = CallType.Extern
                };
            }

            if (methodToId.ContainsKey(callee))
            {
                return new MethodIdInfo()
                {
                    Id = methodToId[callee],
                    Type = CallType.Internal
                };
            }

            //如果是dll之外的方法，或者是构造函数，析构函数，作为虚拟机之外（extern）的方法
            if (method == null || (method.IsConstructor && !isCompilerGeneratedPlainObject(method.DeclaringType))
                || method.IsFinalizer()
                || method.IsAbstract || method.IsPInvokeImpl || method.Body == null
                || method.DeclaringType.IsInterface
                || (!methodToInjectType.ContainsKey(method) && !isCompilerGenerated(method.DeclaringType)
                && !isCompilerGenerated(method) && !(mode == ProcessMode.Patch && isNewMethod(method))))
            {
                //Console.WriteLine("do no tranlater:" + callee + "," + callee.GetType());

                return new MethodIdInfo()
                {
                    Id = addExternMethod(callee, caller),
                    Type = CallType.Extern
                };
            }

            if (method.Parameters.Any(p => p.ParameterType.IsPointer) || method.ReturnType.IsPointer)
            {
                Console.WriteLine("Warning: unsafe method, " + method + " in " + method.DeclaringType);

                return new MethodIdInfo()
                {
                    Id = addExternMethod(callee, caller),
                    Type = CallType.Extern
                };
            }

            if (method.IsGeneric())//暂时不支持解析泛型
            {
                return new MethodIdInfo() { Id = 0, Type = CallType.Invalid };
            }

            var baseProxy = tryAddBaseProxy(method.DeclaringType, method);

            var body = method.Body;
            var msIls = body.Instructions;
            var ilOffset = new Dictionary<Instruction, int>();

            //Console.WriteLine("process method id:" + codes.Count);

            //if (mode == ProcessMode.Patch || !methodToInjectType.ContainsKey(method)
            //    || methodToInjectType[method] == InjectType.Redirect)
            {
                int stopPos;
                //包含不支持指令的方法，作为虚拟机之外（extern）的方法
                if (!checkILAndGetOffset(method, msIls, ilOffset, out stopPos))
                {
                    InjectType it;
                    if (methodToInjectType.TryGetValue(method, out it))
                    {
                        if (mode == ProcessMode.Patch || it == InjectType.Redirect)
                        {
                            // 打patch发现不支持指令应该报错
                            throw new InvalidDataException("not support il[" + msIls[stopPos] + "] in " + method
                                + ", caller is " + caller);
                        }
                        else
                        {
                            Console.WriteLine("Warning: not support il[" + msIls[stopPos] + "] in " + method
                                + (caller == null ? "" : (", caller is " + caller)));
                            injectMethod(method, allocMethodId(method));
                        }
                    }
                    else
                    {
                        Console.WriteLine("Info: not support il[" + msIls[stopPos] + "] in " + method
                            + (caller == null ? "" : (", caller is " + caller)));
                    }

                    return new MethodIdInfo()
                    {
                        Id = addExternMethod(callee, caller),
                        Type = CallType.Extern
                    };
                }
            }

            int methodId = allocMethodId(method);

            InjectType injectType;
            InjectType injectTypePassToNext;
            if (!methodToInjectType.TryGetValue(method, out injectTypePassToNext))
            {
                injectTypePassToNext = callerInjectType;
            }
            //if (!methodToInjectType.TryGetValue(method, out injectType)
            //    || injectType == InjectType.Redirect || mode == ProcessMode.Patch)
            try
            {
                var code = new List<Core.Instruction>();
                codes.Add(methodId, code);
                if (!codeMustWriteToPatch.Contains(methodId) && 
                    (
                        mode == ProcessMode.Patch  || //patch阶段无论哪种类型都要写入补丁
                        (methodToInjectType.TryGetValue(method, out injectType)
                            && injectType == InjectType.Redirect) || //注入阶段，重定向类型需要写入补丁
                        (callerInjectType == InjectType.Redirect) //被重定向类型函数调用，也需要写入补丁
                    ))
                {
                    codeMustWriteToPatch.Add(methodId);
                }

                code.Add(new Core.Instruction { Code = Core.Code.StackSpace, Operand = (body.Variables.Count << 16)
                    | body.MaxStackSize }); // local | maxstack

                //TODO: locals init，复杂值类型要new，引用类型要留空位

                Core.ExceptionHandler[] exceptionHandlers = new Core.ExceptionHandler[body.ExceptionHandlers.Count];

                for (int i = 0; i < body.ExceptionHandlers.Count; i++)
                {
                    var exceptionHandler = body.ExceptionHandlers[i];
                    if (exceptionHandler.HandlerType == ExceptionHandlerType.Fault 
                        && exceptionHandler.HandlerType == ExceptionHandlerType.Filter)
                    {
                        throw new NotImplementedException(exceptionHandler.HandlerType.ToString() + " no support!");
                    }
                    exceptionHandlers[i] = new Core.ExceptionHandler()
                    {
                        HandlerType = (Core.ExceptionHandlerType)(int)exceptionHandler.HandlerType,
                        CatchTypeId = exceptionHandler.CatchType == null ? -1 
                            : addExternType(exceptionHandler.CatchType),
                        TryStart = ilOffset[exceptionHandler.TryStart],
                        TryEnd = ilOffset[exceptionHandler.TryEnd],
                        HandlerStart = ilOffset[exceptionHandler.HandlerStart],
                        HandlerEnd = exceptionHandler.HandlerEnd == null ? -1 : ilOffset[exceptionHandler.HandlerEnd]
                    };
                    //Console.WriteLine("---------------" + i + "---------------");
                    //Console.WriteLine("HandlerType:" + exceptionHandlers[i].HandlerType);
                    //Console.WriteLine("CatchTypeId:" + exceptionHandlers[i].CatchTypeId);
                    //Console.WriteLine("TryStart:" + exceptionHandlers[i].TryStart);
                    //Console.WriteLine("TryEnd:" + exceptionHandlers[i].TryEnd);
                    //Console.WriteLine("HandlerStart:" + exceptionHandlers[i].HandlerStart);
                    //Console.WriteLine("HandlerEnd:" + exceptionHandlers[i].HandlerEnd);
                }

                methodIdToExceptionHandler.Add(methodId, exceptionHandlers);

                bool typeofDetected = false;

                Core.Instruction operand;
                for (int i = 0; i < msIls.Count; i++)
                {
                    var msIl = msIls[i];
                    //Console.WriteLine("msIl:" + msIl.OpCode.Code + ", idx:" + code.Count);

                    string strCode = msIls[i].OpCode.Code.ToString();
                    if (strCode.EndsWith("_S"))
                    {
                        strCode = strCode.Substring(0, strCode.Length - 2);
                    }
                    switch (msIl.OpCode.Code)
                    {
                        case Code.Nop:
                            break;
                        //case Code.Conv_U8:
                        //case Code.Conv_I8:
                        //case Code.Conv_Ovf_I8:
                        //case Code.Conv_Ovf_I8_Un:
                        //case Code.Conv_Ovf_U8:
                        //case Code.Conv_Ovf_U8_Un: // 指令合并
                        //    code.Add(new Core.Instruction
                        //    {
                        //        Code = Core.Code.Conv_I8,
                        //        Operand = 0
                        //    });
                        //    break;
                        case Code.Leave:
                        case Code.Leave_S:
                            var exceptionHandler = findExceptionHandler(exceptionHandlers,
                                Core.ExceptionHandlerType.Finally, ilOffset[msIl]);
                            int leaveTo = ilOffset[msIl.Operand as Instruction];
                            if (exceptionHandler == null
                                || (exceptionHandler.TryStart <= leaveTo
                                && exceptionHandler.TryEnd > leaveTo)) // 退化成Br
                            {
                                code.Add(new Core.Instruction
                                {
                                    Code = Core.Code.Br,
                                    Operand = leaveTo - ilOffset[msIl]
                                });
                                code.Add(new Core.Instruction //补指令
                                {
                                    Code = Core.Code.Nop,
                                    Operand = 0
                                });
                            }
                            else
                            {
                                code.Add(new Core.Instruction
                                {
                                    Code = Core.Code.Leave,
                                    Operand = leaveTo
                                });
                                code.Add(new Core.Instruction
                                {
                                    Code = Core.Code.Br,
                                    Operand = exceptionHandler.HandlerStart - (ilOffset[msIl] + 1)
                                });
                            }
                            break;
                        case Code.Endfinally:
                            {
                                int nextIdx;
                                int os = ilOffset[msIl];
                                //int idx = -1;
                                //for (int j = 0; j < exceptionHandlers.Length; j++)
                                //{
                                //    if (exceptionHandlers[j].HandlerEnd == os + 1)
                                //    {
                                //        idx = j;
                                //    }
                                //}
                                //if (idx == -1)
                                //{
                                //    throw new InvalidProgramException("can not find finally block!");
                                //}
                                findExceptionHandler(exceptionHandlers, Core.ExceptionHandlerType.Finally, os,
                                    out nextIdx);
                                code.Add(new Core.Instruction
                                {
                                    Code = Core.Code.Endfinally,
                                    Operand = nextIdx // -1表示最外层
                                });
                            }
                            break;
                        case Code.Br:
                        case Code.Br_S:
                        case Code.Brtrue:
                        case Code.Brtrue_S:
                        case Code.Brfalse:
                        case Code.Brfalse_S:
                        case Code.Beq:
                        case Code.Beq_S:
                        case Code.Bne_Un:
                        case Code.Bne_Un_S:
                        case Code.Bge:
                        case Code.Bge_S:
                        case Code.Bge_Un:
                        case Code.Bge_Un_S:
                        case Code.Bgt:
                        case Code.Bgt_S:
                        case Code.Bgt_Un:
                        case Code.Bgt_Un_S:
                        case Code.Ble:
                        case Code.Ble_S:
                        case Code.Ble_Un:
                        case Code.Ble_Un_S:
                        case Code.Blt:
                        case Code.Blt_S:
                        case Code.Blt_Un:
                        case Code.Blt_Un_S:
                            strCode = msIls[i].OpCode.Code.ToString();
                            if (strCode.EndsWith("_S"))
                            {
                                strCode = strCode.Substring(0, strCode.Length - 2);
                            }
                            code.Add(new Core.Instruction
                            {
                                Code = (Core.Code)Enum.Parse(typeof(Core.Code), strCode),
                                Operand = ilOffset[msIl.Operand as Instruction] - ilOffset[msIl]
                            });
                            //if (msIl.OpCode.Code == Code.Br_S || msIl.OpCode.Code == Code.Br)
                            //{
                            //    Console.WriteLine("il:" + msIl + ",jump to:" + msIl.Operand);
                            //    Console.WriteLine("il pos:" + ilOffset[msIl] + ",jump to pos:" 
                            //        + ilOffset[msIl.Operand as Instruction]);
                            //}
                            break;
                        case Code.Ldc_I8:
                            code.Add(new Core.Instruction
                            {
                                Code = Core.Code.Ldc_I8,
                                Operand = 0
                            });

                            *((long*)&operand) = (long)msIl.Operand;
                            code.Add(operand);
                            break;
                        case Code.Ldc_R8:
                            code.Add(new Core.Instruction
                            {
                                Code = Core.Code.Ldc_R8,
                                Operand = 0
                            });

                            *((double*)&operand) = (double)msIl.Operand;
                            code.Add(operand);
                            break;
                        case Code.Ldc_I4:
                        case Code.Ldc_I4_S:
                            code.Add(new Core.Instruction
                            {
                                Code = Core.Code.Ldc_I4,
                                Operand = Convert.ToInt32(msIl.Operand)
                            });
                            break;
                        case Code.Ldc_R4:
                            {
                                float val = (float)msIl.Operand;
                                code.Add(new Core.Instruction
                                {
                                    Code = Core.Code.Ldc_R4,
                                    Operand = *(int*)&val
                                });
                            }
                            break;
                        case Code.Stloc:
                        case Code.Stloc_S:
                        case Code.Ldloc:
                        case Code.Ldloc_S:
                        case Code.Ldloca:
                        case Code.Ldloca_S:
                            code.Add(new Core.Instruction
                            {
                                Code = (Core.Code)Enum.Parse(typeof(Core.Code), strCode),
                                Operand = (msIl.Operand as VariableDefinition).Index
                            });
                            break;
                        case Code.Ldarg_S:
                        case Code.Ldarg:
                        case Code.Ldarga:
                        case Code.Ldarga_S:
                        case Code.Starg:
                        case Code.Starg_S:
                            code.Add(new Core.Instruction
                            {
                                Code = (Core.Code)Enum.Parse(typeof(Core.Code), strCode),
                                Operand = (msIl.Operand as ParameterDefinition).Index + (method.IsStatic ? 0 : 1)
                            });
                            break;
                        case Code.Ldarg_0:
                        case Code.Ldarg_1:
                        case Code.Ldarg_2:
                        case Code.Ldarg_3:
                            code.Add(new Core.Instruction
                            {
                                Code = Core.Code.Ldarg,
                                Operand = int.Parse(strCode.Substring(strCode.Length - 1)),
                            });
                            break;
                        case Code.Ldloc_0:
                        case Code.Ldloc_1:
                        case Code.Ldloc_2:
                        case Code.Ldloc_3:
                            code.Add(new Core.Instruction
                            {
                                Code = Core.Code.Ldloc,
                                Operand = int.Parse(strCode.Substring(strCode.Length - 1)),
                            });
                            break;
                        case Code.Stloc_0:
                        case Code.Stloc_1:
                        case Code.Stloc_2:
                        case Code.Stloc_3:
                            code.Add(new Core.Instruction
                            {
                                Code = Core.Code.Stloc,
                                Operand = int.Parse(strCode.Substring(strCode.Length - 1)),
                            });
                            break;
                        case Code.Ldc_I4_0:
                        case Code.Ldc_I4_1:
                        case Code.Ldc_I4_2:
                        case Code.Ldc_I4_3:
                        case Code.Ldc_I4_4:
                        case Code.Ldc_I4_5:
                        case Code.Ldc_I4_6:
                        case Code.Ldc_I4_7:
                        case Code.Ldc_I4_8:
                            code.Add(new Core.Instruction
                            {
                                Code = Core.Code.Ldc_I4,
                                Operand = int.Parse(strCode.Substring(strCode.Length - 1)),
                            });
                            break;
                        case Code.Ldc_I4_M1:
                            code.Add(new Core.Instruction
                            {
                                Code = Core.Code.Ldc_I4,
                                Operand = -1,
                            });
                            break;
                        case Code.Ret:
                            code.Add(new Core.Instruction
                            {
                                Code = Core.Code.Ret,
                                Operand = callee.ReturnType.ToString() == "System.Void" ? 0 : 1,
                            });
                            break;
                        case Code.Newobj:
                        case Code.Callvirt:
                        case Code.Call:
                            {
                                if (typeofDetected)
                                {
                                    typeofDetected = false;
                                    code.Add(new Core.Instruction
                                    {
                                        Code = Core.Code.Nop,
                                        Operand = 0,
                                    });
                                    break;
                                }
                                var methodToCall = msIl.Operand as MethodReference;
                                if (msIl.OpCode.Code == Code.Newobj && isCompilerGeneratedPlainObject(
                                    methodToCall.DeclaringType))
                                {
                                    TypeDefinition td = methodToCall.DeclaringType as TypeDefinition;
                                    var anonymousCtorInfo = getMethodId(methodToCall, method, false, 
                                        injectTypePassToNext);
                                    if (anonymousCtorInfo.Type != CallType.Internal)
                                    {
                                        throw new InvalidProgramException("Newobj for " + td);
                                    }
                                    //Console.WriteLine("")
                                    code.Add(new Core.Instruction
                                    {
                                        Code = Core.Code.Newanon,
                                        Operand = addAnonymousCtor(methodToCall as MethodDefinition)
                                    });
                                    break;
                                }

                                MethodDefinition or = null;
                                var lastInstruction = code.Last();
                                if (lastInstruction.Code == Core.Code.Constrained)
                                {
                                    var constrainedType = externTypes[lastInstruction.Operand];
                                    var hasOverrideMethod = false;
                                    if (constrainedType.IsValueType && constrainedType is TypeDefinition)
                                    {
                                        or = findOverride(constrainedType as TypeDefinition, methodToCall);
                                        if (or != null)
                                        {
                                            methodToCall = or;
                                            code[code.Count - 1] = new Core.Instruction
                                            {
                                                Code = Core.Code.Nop,
                                                Operand = 0,
                                            };
                                            hasOverrideMethod = true;
                                        }
                                    }
                                    if (constrainedType.IsValueType && !hasOverrideMethod)
                                    {
                                        code[code.Count - 2] = new Core.Instruction
                                        {
                                            Code = Core.Code.Ldobj,
                                            Operand = lastInstruction.Operand,
                                        };
                                        code[code.Count - 1] = new Core.Instruction
                                        {
                                            Code = Core.Code.Box,
                                            Operand = lastInstruction.Operand,
                                        };
                                    }
                                    //code.RemoveAt(code.Count - 1);
                                }
                                int paramCount = (methodToCall.Parameters.Count + (msIl.OpCode.Code != Code.Newobj 
                                    && methodToCall.HasThis ? 1 : 0));
                                var methodIdInfo = getMethodId(methodToCall, method, or != null || directCallVirtual,
                                    injectTypePassToNext);

                                bool callingBaseMethod = false;

                                try
                                {
                                    var callingType = methodToCall.DeclaringType;
                                    var baseType = method.DeclaringType.BaseType;
                                    while (baseType != null)
                                    {
                                        if (callingType.IsSameType(baseType))
                                        {
                                            callingBaseMethod = true;
                                            break;
                                        }
                                        baseType = baseType.Resolve().BaseType;
                                    }
                                }
                                catch { }

                                if (callingBaseMethod && msIl.OpCode.Code == Code.Call && baseProxy != null 
                                    && isTheSameDeclare(methodToCall, method))
                                {
                                    code.Add(new Core.Instruction
                                    {
                                        Code = Core.Code.CallExtern,
                                        Operand = (paramCount << 16) | addExternMethod(baseProxy, method)
                                    });
                                }
                                else if (methodIdInfo.Type == CallType.Internal)
                                {
                                    code.Add(new Core.Instruction
                                    {
                                        Code = (or != null) ? Core.Code.Call :
                                            (Core.Code)Enum.Parse(typeof(Core.Code), strCode),
                                        Operand = (paramCount << 16) | methodIdInfo.Id
                                    });
                                    if (msIl.OpCode.Code == Code.Newobj)
                                    {
                                        throw new InvalidProgramException("Newobj's Operand is not a constructor?");
                                    }
                                }
                                else if (methodIdInfo.Type == CallType.Extern)
                                {
                                    code.Add(new Core.Instruction
                                    {
                                        Code = msIl.OpCode.Code == Code.Newobj ? Core.Code.Newobj :
                                            Core.Code.CallExtern,
                                        Operand = (paramCount << 16) | methodIdInfo.Id
                                    });
                                }
                                else
                                {
                                    throw new InvalidProgramException("call a generic method definition");
                                }
                            }
                            break;
                        case Code.Ldftn:
                        case Code.Ldvirtftn:
                            {
                                var methodToCall = msIl.Operand as MethodReference;
                                var methodIdInfo = getMethodId(methodToCall, method, false, injectTypePassToNext);
                                if (methodIdInfo.Type == CallType.Internal
                                    && isCompilerGeneratedPlainObject(methodToCall.DeclaringType)) // closure
                                {
                                    //Console.WriteLine("closure: " + methodToCall);
                                    getWrapperMethod(wrapperType, anonObjOfWrapper, methodToCall as MethodDefinition,
                                        true, true);
                                    code.Add(new Core.Instruction
                                    {
                                        Code = Core.Code.Ldc_I4,
                                        Operand = methodIdInfo.Id
                                    });
                                    break;
                                }
                                //TODO： 如果生成代码做了delegate的cache怎么办呢？
                                else if (methodIdInfo.Type == CallType.Internal
                                    && (isCompilerGenerated(methodToCall as MethodDefinition)
                                    || isNewMethod(methodToCall as MethodDefinition)) )
                                {
                                    //Console.WriteLine("loadftn for static: " + methodToCall);
                                    getWrapperMethod(wrapperType, anonObjOfWrapper, methodToCall as MethodDefinition,
                                        !(methodToCall as MethodDefinition).IsStatic, true);
                                    code.Add(new Core.Instruction
                                    {
                                        Code = Core.Code.Ldc_I4,
                                        Operand = methodIdInfo.Id
                                    });
                                    break;
                                }
                                else //TODO：如果闭包含不支持的指令怎么办？
                                {
                                    code.Add(new Core.Instruction
                                    {
                                        Code = (Core.Code)Enum.Parse(typeof(Core.Code), strCode),
                                        Operand = addExternMethod(methodToCall, method)
                                    });
                                    break;
                                }
                                //throw new NotImplementedException(msIl.OpCode.Code.ToString() + ":" + msIl.Operand);
                            }
                        //break;
                        case Code.Constrained:
                            code.Add(new Core.Instruction
                            {
                                Code = Core.Code.Nop,
                                Operand = 0,
                            });
                            if ((msIl.Operand as TypeReference).IsValueType)
                            {
                                code.Add(new Core.Instruction
                                {
                                    Code = Core.Code.Constrained,
                                    Operand = addExternType(msIl.Operand as TypeReference)
                                });
                            }
                            else
                            {
                                code.Add(new Core.Instruction
                                {
                                    Code = Core.Code.Ldind_Ref,
                                    Operand = 0
                                });
                            }
                            break;
                        case Code.Box:
                        case Code.Isinst:
                        case Code.Unbox_Any:
                        case Code.Unbox:
                        case Code.Newarr:
                        case Code.Ldelema:
                        case Code.Initobj:
                        case Code.Ldobj:
                        case Code.Stobj:
                        case Code.Castclass:
                            code.Add(new Core.Instruction
                            {
                                Code = (Core.Code)Enum.Parse(typeof(Core.Code), strCode),
                                Operand = addExternType(msIl.Operand as TypeReference)
                            });
                            break;
                        case Code.Stfld:
                        case Code.Ldfld:
                        case Code.Ldflda:
                            {
                                var field = msIl.Operand as FieldReference;
                                if (isCompilerGeneratedPlainObject(field.DeclaringType))
                                {
                                    var declaringType = field.DeclaringType as TypeDefinition;
                                    code.Add(new Core.Instruction
                                    {
                                        Code = (Core.Code)Enum.Parse(typeof(Core.Code), strCode),
                                        Operand = -(declaringType.Fields.IndexOf(field as FieldDefinition) + 1)
                                    });
                                    //Console.WriteLine("anon obj field:" + field + ",idx:" +
                                    //    declaringType.Fields.IndexOf(field as FieldDefinition));
                                }
                                else
                                {
                                    code.Add(new Core.Instruction
                                    {
                                        Code = (Core.Code)Enum.Parse(typeof(Core.Code), strCode),
                                        Operand = addRefField(field, method)
                                    });
                                }
                            }
                            break;
                        case Code.Stsfld:
                        case Code.Ldsfld:
                        case Code.Ldsflda:
                            var fr = msIl.Operand as FieldReference;
                            var fd = fr.Resolve();
                            bool storeInVitualMachine = (isCompilerGenerated(fr)
                                || isCompilerGenerated(fr.DeclaringType)) &&
                                !getSpecialGeneratedFields(fr.DeclaringType.Resolve()).Contains(fd)
                                && typeToCctor[fd.DeclaringType] > -2;
                            if (!storeInVitualMachine && isCompilerGenerated(fr) && fd.Name.IndexOf("$cache") >= 0
                                && fd.FieldType.Resolve().IsDelegate())
                            {
                                storeInVitualMachine = true;
                            }
                            code.Add(new Core.Instruction
                            {
                                Code = (Core.Code)Enum.Parse(typeof(Core.Code), strCode),
                                Operand = storeInVitualMachine ? addStoreField(fd, method)
                                    : addRefField(msIl.Operand as FieldReference, method)
                            });
                            break;
                        case Code.Ldstr:
                            code.Add(new Core.Instruction
                            {
                                Code = Core.Code.Ldstr,
                                Operand = addInternString(msIls[i].Operand as string, method)
                            });
                            break;
                        case Code.Ldtoken:
                            if (msIls[i].Next != null && msIls[i].Next.OpCode.Code == Code.Call)
                            {
                                var m = msIls[i].Next.Operand as MethodReference;
                                if (m.Name == "GetTypeFromHandle" && m.DeclaringType.IsSameType(
                                    assembly.MainModule.ImportReference(typeof(Type))))
                                {
                                    code.Add(new Core.Instruction
                                    {
                                        Code = Core.Code.Ldtype,
                                        Operand = addExternType(msIls[i].Operand as TypeReference)
                                    });
                                    typeofDetected = true;
                                    break;
                                }
                            }
                            code.Add(new Core.Instruction
                            {
                                Code = Core.Code.Ldtoken,
                                Operand = addExternType(msIls[i].Operand as TypeReference)
                            });
                            break;
                        case Code.Switch:
                            Instruction[] jmpTargets = msIls[i].Operand as Instruction[];
                            code.Add(new Core.Instruction
                            {
                                Code = Core.Code.Switch,
                                Operand = jmpTargets.Length
                            });
                            //Console.WriteLine("jmpTargets.Length:" + jmpTargets.Length);
                            for (int j = 0; j < jmpTargets.Length; j++)
                            {
                                int diffOffset = ilOffset[jmpTargets[j]] - ilOffset[msIl];
                                //Console.WriteLine("jmp " + j + " :" + diffOffset);
                                if ((j % 2) == 0)
                                {
                                    *((int*)&operand) = diffOffset;
                                }
                                else
                                {
                                    *(((int*)&operand) + 1) = diffOffset;
                                    //Console.WriteLine("two jump table");
                                    code.Add(operand);
                                }
                            }
                            if ((jmpTargets.Length % 2) == 1)
                            {
                                *(((int*)&operand) + 1) = 0;
                                //Console.WriteLine("two jump table--");
                                code.Add(operand);
                            }
                            break;
                        default:
                            code.Add(new Core.Instruction
                            {
                                Code = (Core.Code)Enum.Parse(typeof(Core.Code), strCode),
                                Operand = 0
                            });
                            break;

                    }
                }

                if (mode == ProcessMode.Patch && methodToInjectType.TryGetValue(method, out injectType)
                    && injectType == InjectType.Switch)
                {
                    addInterpretMethod(method, methodId);
                    Console.WriteLine("patched: " + method);
                }
            }
            catch(Exception e)
            {
                if (mode == ProcessMode.Inject)
                {
                    Console.WriteLine("Warning: process " + method + " il throw " + e);
                }
                else
                {
                    throw e;
                }
            }

            //Console.WriteLine("process finish:" + method);
            if (mode == ProcessMode.Inject)
            {
                injectMethod(method, methodId);
            }

            if (!directCallVirtual && method.IsVirtual)
            {
                return new MethodIdInfo()
                {
                    Id = addExternMethod(callee, caller),
                    Type = CallType.Extern
                };
            }
            else
            {
                return new MethodIdInfo()
                {
                    Id = methodId,
                    Type = CallType.Internal
                };
            }
        }

        public enum ProcessResult
        {
            OK,
            Processed
        }

        AssemblyDefinition assembly;

        private TypeReference objType;
        private TypeReference voidType;
        private TypeDefinition wrapperType;
        private TypeDefinition idMapType;
        private TypeDefinition itfBridgeType;
        private int bridgeMethodId;
        private TypeReference anonymousStoreyTypeRef;
        private MethodReference anonymousStoreyCtorRef;

        private FieldDefinition virualMachineFieldOfWrapper;
        private FieldDefinition virualMachineFieldOfBridge;
        private FieldDefinition methodIdFieldOfWrapper;
        private FieldDefinition anonObjOfWrapper;
        private FieldDefinition wrapperArray;
        private MethodDefinition ctorOfWrapper;
        private MethodDefinition getPatch;
        private MethodDefinition isPatched;
        private MethodDefinition createDelegate;

        private TypeReference Call_Ref;
        private MethodReference Call_Begin_Ref;
        private MethodReference Call_PushRef_Ref;
        private MethodReference Call_GetAsType_Ref;

        private TypeReference VirtualMachineType;
        private TypeReference WrappersManagerType;
        private TypeDefinition wrapperMgrImpl;
        private MethodDefinition ctorOfWrapperMgrImpl;
        private MethodReference VirtualMachine_Execute_Ref;

        private MethodReference Utils_TryAdapterToDelegate_Ref;

        private MethodReference idTagCtor_Ref;

        private List<MethodDefinition> wrapperMethods;

        private Dictionary<TypeReference, MethodReference> pushMap =
            new Dictionary<TypeReference, MethodReference>();

        private Dictionary<TypeReference, MethodReference> getMap =
            new Dictionary<TypeReference, MethodReference>();

        private Dictionary<string, TypeReference> nameToTypeReference =
            new Dictionary<string, TypeReference>();

        private MethodReference Call_PushValueType_Ref;
        TypeReference wrapperParamerterType(TypeReference type)
        {
            if (type.IsByReference)
            {
                return type;
            }
            if (type.IsValueType)
            {
                if (type.IsPrimitive)
                {
                    return type;
                }
                try
                {
                    if (type.Resolve().IsEnum)
                    {
                        return type;
                    }
                }
                catch { }
            }
            return objType;
        }

        TypeReference getRawType(TypeReference type)
        {
            return type.IsByReference ? ((ByReferenceType)type).ElementType : type;
        }

        private List<MethodDefinition> anonymousTypeInfos = new List<MethodDefinition>();
        private Dictionary<MethodDefinition, int> anonymousTypeToId = new Dictionary<MethodDefinition, int>();

        int addAnonymousCtor(MethodDefinition ctor)
        {
            int id;
            if (anonymousTypeToId.TryGetValue(ctor, out id))
            {
                return id;
            }
            addInterfacesOfTypeToBridge(ctor.DeclaringType as TypeDefinition);
            foreach(var method in ctor.DeclaringType.Methods.Where(m => !m.IsConstructor))
            {
                getMethodId(method, null, true, InjectType.Redirect);
            }
            id = anonymousTypeInfos.Count;
            anonymousTypeInfos.Add(ctor);
            anonymousTypeToId[ctor] = id;
            return id;
        }

        Dictionary<MethodReference, int> interfaceSlot = new Dictionary<MethodReference, int>();
        Dictionary<MethodDefinition, MethodReference> implementMap
            = new Dictionary<MethodDefinition, MethodReference>();

        List<TypeReference> bridgeInterfaces = new List<TypeReference>();
        void bridgeImplement(TypeReference itf)
        {
            if (bridgeInterfaces.Any(item => item.AreEqualIgnoreAssemblyVersion(itf))) return;
            addExternType(itf);
            bridgeInterfaces.Add(itf);
            itfBridgeType.Interfaces.Add(new InterfaceImplementation(itf));
        }

        /// <summary>
        /// 桥接器实现一个类的所有接口，一般来说是个匿名类
        /// </summary>
        /// <param name="anonType">要实现桥接的匿名类</param>
        void addInterfacesOfTypeToBridge(TypeDefinition anonType)
        {
            if (anonType.Interfaces.Count == 0)
            {
                return;
            }
            List<TypeReference> toImplement = new List<TypeReference>();
            //Console.WriteLine("begin type:" + anonType);
            foreach (var method in anonType.Methods.Where(m => !m.IsConstructor))
            {
                MethodReference matchItfMethod = null;
                //Console.WriteLine("method:" + method);
                if (method.Overrides.Count == 1)
                {
                    matchItfMethod = method.Overrides[0];
                    implementMap[method] = matchItfMethod;
                    if (itfBridgeType.Interfaces.Any(ii => ii.InterfaceType.IsSameType(matchItfMethod.DeclaringType)))
                    {
                        continue;
                    }
                }
                else if (method.IsPublic)
                {
                    //var m = MetadataResolver.GetMethod(or.DeclaringType.Resolve().Methods, method);
                    foreach(var itf in anonType.Interfaces.Select(ii => ii.InterfaceType))
                    {
                        matchItfMethod = itf.FindMatch(method);
                        if (matchItfMethod != null) break;
                    }
                    
                    //implementMap[method] = matchItfMethod;
                    if (itfBridgeType.Interfaces.Any(ii => ii.InterfaceType.IsSameType(matchItfMethod.DeclaringType)))
                    {
                        continue;
                    }
                }
                else //Enumerator 语法糖里头再有个闭包语法糖，会在类那生成一个非私有，非公有的函数
                {
                    continue;
                }

                if (matchItfMethod == null)
                {
                    throw new Exception("can not find base method for " + method);
                }

                toImplement.Add(matchItfMethod.DeclaringType);
                //Console.WriteLine("add slot " + matchItfMethod + ",m=" + method);
                interfaceSlot.Add(matchItfMethod, bridgeMethodId);
                var impl = getWrapperMethod(itfBridgeType, null, method, false, true, true, bridgeMethodId);
                addIDTag(impl, bridgeMethodId++);
                impl.Overrides.Add(matchItfMethod);
            }

            //Console.WriteLine("end type:" + anonType);

            foreach (var itf in toImplement.Distinct())
            {
                bridgeImplement(itf);
            }
            //foreach (var itf in anonType.Interfaces.Select(ii => ii.InterfaceType))
            //{
            //    if (itfBridgeType.Interfaces.Any(ii => ii.InterfaceType.IsSameType(itf)))
            //    {
            //        continue;
            //    }
            //    foreach(var method in itf.Resolve().Methods)
            //    {
            //        interfaceSlot.Add(method, bridgeMethodId);
            //        var impl = getWrapperMethod(itfBridgeType, null, method, false, true, true, bridgeMethodId++);
            //        impl.Overrides.Add(method);
            //    }
            //}
        }

        void addInterfaceToBridge(TypeReference itf)
        {
            var itfDef = itf.Resolve();
            if (!itfDef.IsInterface
                || itf.HasGenericParameters
                || itfBridgeType.Interfaces.Any(ii => ii.InterfaceType.FullName == itf.FullName)
                || itfDef.Methods.Any(m => m.HasGenericParameters))
            {
                return;
            }
            foreach(var method in itfDef.Methods)
            {
                var methodToImpl = itf.IsGenericInstance ? method.MakeGeneric(itf) : method.TryImport(itf.Module);
                interfaceSlot.Add(methodToImpl, bridgeMethodId);
                var impl = getWrapperMethod(itfBridgeType, null, methodToImpl, false, true, true, bridgeMethodId);
                addIDTag(impl, bridgeMethodId++);
                impl.Overrides.Add(methodToImpl);
            }
            bridgeImplement(itf);
            foreach (var ii in itfDef.Interfaces)
            {
                addInterfaceToBridge(ii.InterfaceType.FillGenericArgument(null, itf).TryImport(itf.Module));
            }
        }

        /// <summary>
        /// 获取一个方法的适配器
        /// </summary>
        /// <param name="type">方法适配器的放置类</param>
        /// <param name="anonObj">适配器所绑定的匿名对象</param>
        /// <param name="method">要适配的方法</param>
        /// <param name="isClosure">是不是闭包</param>
        /// <param name="noBaselize">是否向基类收敛（如果是delegate适配器，就不能收敛）</param>
        /// <param name="isInterfaceBridge">是否是接口桥接器</param>
        /// <param name="mid">方法id</param>
        /// <returns></returns>
        // #lizard forgives
        MethodDefinition getWrapperMethod(TypeDefinition type, FieldDefinition anonObj, MethodReference method,
            bool isClosure, bool noBaselize, bool isInterfaceBridge = false, int mid = -1)
        {
            MethodDefinition md = method as MethodDefinition;
            if(md == null)
            {
                md = method.Resolve();
            }
            //原始参数类型
            List<TypeReference> parameterTypes = new List<TypeReference>();
            //适配器参数类型，不是强制noBaselize的话，引用类型，复杂非引用值类型，均转为object
            List<TypeReference> wrapperParameterTypes = new List<TypeReference>();
            List<bool> isOut = new List<bool>();
            //List<ParameterAttributes> paramAttrs = new List<ParameterAttributes>();
            if (!md.IsStatic && !isClosure && !isInterfaceBridge) //匿名类闭包的this是自动传，不需要显式参数
            {
                isOut.Add(false);
                //paramAttrs.Add(Mono.Cecil.ParameterAttributes.None);
                if (method.DeclaringType.IsValueType)
                {
                    var dt = new ByReferenceType(method.DeclaringType);
                    parameterTypes.Add(dt);
                    wrapperParameterTypes.Add(dt);
                }
                else
                {
                    parameterTypes.Add(method.DeclaringType);
                    wrapperParameterTypes.Add(noBaselize ? method.DeclaringType
                        : wrapperParamerterType(method.DeclaringType));
                }
            }

            for (int i = 0; i < method.Parameters.Count; i++)
            {
                isOut.Add(method.Parameters[i].IsOut);
                //paramAttrs.Add(method.Parameters[i].Attributes);
                var paramType = method.Parameters[i].ParameterType;
                if (paramType.IsGenericParameter)
                {
                    paramType = (paramType as GenericParameter).ResolveGenericArgument(method.DeclaringType);
                }
                parameterTypes.Add(paramType);
                wrapperParameterTypes.Add(noBaselize ? paramType : wrapperParamerterType(paramType));
            }

            var returnType = method.ReturnType.FillGenericArgument(method, method.DeclaringType);

            MethodDefinition wrapperMethod;

            if (isInterfaceBridge)
            {
                var attributes = md.Attributes;
                var methodName = method.Name;
                if (attributes != 0 && ((attributes & MethodAttributes.Abstract) == MethodAttributes.Abstract))
                {
                    attributes = attributes & (~MethodAttributes.Abstract) & (~MethodAttributes.Public)
                        | MethodAttributes.Private;
                    methodName = System.Text.RegularExpressions.Regex.Replace(method.DeclaringType.FullName,
                        @"`\d+", "") + "." + methodName;
                }
                wrapperMethod = new MethodDefinition(methodName, attributes,
                    returnType.TryImport(assembly.MainModule));
            }
            else
            {
                List<MethodDefinition> cacheToCheck = wrapperMethods;
                for (int i = 0; i < cacheToCheck.Count; i++)
                {
                    wrapperMethod = cacheToCheck[i];
                    if (wrapperMethod.Parameters.Count != wrapperParameterTypes.Count
                        || !wrapperMethod.ReturnType.IsSameType(returnType))
                    {
                        continue;
                    }
                    bool paramMatch = true;
                    for (int j = 0; j < wrapperParameterTypes.Count; j++)
                    {
                        if (!wrapperParameterTypes[j].IsSameType(wrapperMethod.Parameters[j].ParameterType)
                            || isOut[j] != wrapperMethod.Parameters[j].IsOut)
                        {
                            paramMatch = false;
                            break;
                        }
                    }
                    if (!paramMatch)
                    {
                        continue;
                    }
                    return wrapperMethod;
                }
                wrapperMethod = new MethodDefinition(Wrap_Perfix + cacheToCheck.Count,
                    Mono.Cecil.MethodAttributes.Public, returnType.TryImport(assembly.MainModule));
                cacheToCheck.Add(wrapperMethod);
            }
            var instructions = wrapperMethod.Body.Instructions;

            int refCount = 0;
            int[] refPos = new int[parameterTypes.Count];

            for (int i = 0; i < parameterTypes.Count; i++)
            {
                refPos[i] = parameterTypes[i].IsByReference ? refCount++ : -1;
                wrapperMethod.Parameters.Add(new ParameterDefinition("P" + i, isOut[i] ? ParameterAttributes.Out
                    : ParameterAttributes.None, wrapperParameterTypes[i].TryImport(assembly.MainModule)));
            }

            var ilProcessor = wrapperMethod.Body.GetILProcessor();

            VariableDefinition call = new VariableDefinition(Call_Ref);
            wrapperMethod.Body.Variables.Add(call);

            instructions.Add(Instruction.Create(OpCodes.Call, Call_Begin_Ref));
            instructions.Add(Instruction.Create(OpCodes.Stloc, call));

            if (refCount > 0)
            {
                for (int i = 0; i < parameterTypes.Count; i++)
                {
                    if (parameterTypes[i].IsByReference)
                    {
                        var paramRawType = tryGetUnderlyingType(getRawType(parameterTypes[i]));
                        if (isOut[i]) // push default
                        {
                            instructions.Add(Instruction.Create(OpCodes.Ldloca_S, call));
                            MethodReference push;
                            var wpt = wrapperParamerterType(paramRawType);
                            if (pushMap.TryGetValue(wpt, out push))
                            {
                                if (wpt == assembly.MainModule.TypeSystem.Object)
                                {
                                    instructions.Add(Instruction.Create(OpCodes.Ldnull));
                                }
                                else
                                {
                                    instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
                                }
                                if (wpt == assembly.MainModule.TypeSystem.Int64
                                    || wpt == assembly.MainModule.TypeSystem.UInt64
                                    || wpt == assembly.MainModule.TypeSystem.IntPtr
                                    || wpt == assembly.MainModule.TypeSystem.UIntPtr)
                                {
                                    instructions.Add(Instruction.Create(OpCodes.Conv_I8));
                                    push = pushMap[assembly.MainModule.TypeSystem.Int64];
                                }
                                else if (wpt == assembly.MainModule.TypeSystem.Single)
                                {
                                    instructions.Add(Instruction.Create(OpCodes.Conv_R4));
                                }
                                else if (wpt == assembly.MainModule.TypeSystem.Double)
                                {
                                    instructions.Add(Instruction.Create(OpCodes.Conv_R8));
                                }
                                else if (wpt == assembly.MainModule.TypeSystem.Object)
                                {
                                    if(paramRawType.IsValueType)
                                    {
                                        push = Call_PushValueType_Ref;
                                    }
                                }
                                else
                                {
                                    push = pushMap[assembly.MainModule.TypeSystem.Int32];
                                }
                            }
                            else
                            {
                                throw new NotImplementedException("no push for " + paramRawType + " at " + method);
                            }
                            instructions.Add(Instruction.Create(OpCodes.Callvirt, push));
                        }
                        else
                        {
                            instructions.Add(Instruction.Create(OpCodes.Ldloca_S, call));
                            emitLdarg(instructions, ilProcessor, i + 1);
                            emitLoadRef(instructions, paramRawType);
                            MethodReference push;
                            if (pushMap.TryGetValue(tryGetUnderlyingType(paramRawType), out push))
                            {
                                instructions.Add(Instruction.Create(OpCodes.Callvirt, push));
                            }
                            else
                            {
                                if (paramRawType.IsValueType)
                                {
                                    instructions.Add(Instruction.Create(OpCodes.Callvirt, Call_PushValueType_Ref));
                                }
                                else
                                {
                                    instructions.Add(Instruction.Create(OpCodes.Callvirt,
                                        pushMap[assembly.MainModule.TypeSystem.Object]));
                                }
                            }
                        }
                    }
                }
            }

            if (isInterfaceBridge)
            {
                instructions.Add(Instruction.Create(OpCodes.Ldloca_S, call));
                instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                instructions.Add(Instruction.Create(OpCodes.Callvirt, pushMap[assembly.MainModule.TypeSystem.Object]));
            }
            else
            {
                instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                instructions.Add(Instruction.Create(OpCodes.Ldfld, anonObj));

                var nop = Instruction.Create(OpCodes.Nop);
                instructions.Add(Instruction.Create(OpCodes.Brfalse_S, nop));

                instructions.Add(Instruction.Create(OpCodes.Ldloca_S, call));
                instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                instructions.Add(Instruction.Create(OpCodes.Ldfld, anonObj));
                instructions.Add(Instruction.Create(OpCodes.Callvirt, pushMap[assembly.MainModule.TypeSystem.Object]));
                instructions.Add(nop);
            }

            for (int i = 0; i < parameterTypes.Count; i++)
            {
                instructions.Add(Instruction.Create(OpCodes.Ldloca_S, call));

                if (parameterTypes[i].IsByReference)
                {
                    emitLdcI4(instructions, refPos[i]);
                    instructions.Add(Instruction.Create(OpCodes.Callvirt, Call_PushRef_Ref));
                }
                else
                {
                    emitLdarg(instructions, ilProcessor, i + 1);
                    var paramRawType = getRawType(wrapperParameterTypes[i]);
                    MethodReference push;
                    if (pushMap.TryGetValue(tryGetUnderlyingType(paramRawType), out push))
                    {
                        instructions.Add(Instruction.Create(OpCodes.Callvirt, push));
                    }
                    else
                    {
                        if (paramRawType.IsValueType)
                        {
                            instructions.Add(Instruction.Create(OpCodes.Box, paramRawType));
                            //Console.WriteLine("Call_PushValueType_Ref for " + method.Name + ", pidx:" + i
                            //    + ", ptype:" + parameterTypes[i] + ", paramRawType:" + paramRawType + ",wrap:"
                            //    + wrapperMethod.Name);
                            instructions.Add(Instruction.Create(OpCodes.Callvirt, Call_PushValueType_Ref));
                        }
                        else
                        {
                            instructions.Add(Instruction.Create(OpCodes.Callvirt,
                                pushMap[assembly.MainModule.TypeSystem.Object]));
                        }
                    }
                }
            }

            instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            instructions.Add(Instruction.Create(OpCodes.Ldfld, isInterfaceBridge ?
                virualMachineFieldOfBridge : virualMachineFieldOfWrapper));
            if (isInterfaceBridge)
            {
                var methodId = new FieldDefinition(METHODIDPERFIX + mid, FieldAttributes.Private,
                    assembly.MainModule.TypeSystem.Int32);
                type.Fields.Add(methodId);
                instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                instructions.Add(Instruction.Create(OpCodes.Ldfld, methodId));
            }
            else
            {
                instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                instructions.Add(Instruction.Create(OpCodes.Ldfld, methodIdFieldOfWrapper));
            }
            instructions.Add(Instruction.Create(OpCodes.Ldloca_S, call));

            var ldci4_ref_count = createLdcI4(refCount);
            if (isInterfaceBridge)
            {
                emitLdcI4(instructions, parameterTypes.Count + 1);
            }
            else
            {
                instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                instructions.Add(Instruction.Create(OpCodes.Ldfld, anonObj));
                var ldci4_param_count = createLdcI4(parameterTypes.Count + 1);
                instructions.Add(Instruction.Create(OpCodes.Brtrue_S, ldci4_param_count));
                emitLdcI4(instructions, parameterTypes.Count);
                instructions.Add(Instruction.Create(OpCodes.Br_S, ldci4_ref_count));
                instructions.Add(ldci4_param_count);
            }
            instructions.Add(ldci4_ref_count);
            
            instructions.Add(Instruction.Create(OpCodes.Callvirt, VirtualMachine_Execute_Ref));

            if (refCount > 0)
            {

                // Ref param
                for (int i = 0; i < parameterTypes.Count; i++)
                {
                    if (parameterTypes[i].IsByReference)
                    {
                        emitLdarg(instructions, ilProcessor, i + 1);
                        var paramRawType = tryGetUnderlyingType(getRawType(parameterTypes[i]));
                        instructions.Add(Instruction.Create(OpCodes.Ldloca_S, call));

                        emitLdcI4(instructions, refPos[i]);
                        if (getMap.ContainsKey(paramRawType))
                        {
                            instructions.Add(Instruction.Create(OpCodes.Callvirt, getMap[paramRawType]));
                        }
                        else
                        {
                            instructions.Add(Instruction.Create(OpCodes.Callvirt,
                                makeGenericMethod(Call_GetAsType_Ref, paramRawType)));
                        }
                        emitStoreRef(instructions, paramRawType);
                    }
                }
            }

            if (!returnType.IsSameType(voidType))
            {
                instructions.Add(Instruction.Create(OpCodes.Ldloca_S, call));
                MethodReference get;
                emitLdcI4(instructions, refCount);
                if (getMap.TryGetValue(tryGetUnderlyingType(returnType), out get))
                {
                    instructions.Add(Instruction.Create(OpCodes.Callvirt, get));
                }
                else
                {
                    instructions.Add(Instruction.Create(OpCodes.Callvirt,
                        makeGenericMethod(Call_GetAsType_Ref, returnType)));
                }
            }

            instructions.Add(Instruction.Create(OpCodes.Ret));

            type.Methods.Add(wrapperMethod);

            return wrapperMethod;
        }

        void emitLdcI4(Mono.Collections.Generic.Collection<Instruction> instructions, int i)
        {
            instructions.Add(createLdcI4(i));
            
        }

        Instruction createLdcI4(int i)
        {
            if (i < ldc4s.Length && i >= 0)
            {
                return Instruction.Create(ldc4s[i]);
            }
            else if (i == -1)
            {
                return Instruction.Create(OpCodes.Ldc_I4_M1);
            }
            else
            {
                return Instruction.Create(OpCodes.Ldc_I4, i);
            }
        }

        void emitLdarg(Mono.Collections.Generic.Collection<Instruction> instructions, ILProcessor ilProcessor, int i)
        {
            instructions.Add(createLdarg(ilProcessor, i));
        }

        Instruction createLdarg(ILProcessor ilProcessor, int i)
        {
            if (i < ldargs.Length)
            {
                return Instruction.Create(ldargs[i]);
            }
            else if (i < 256)
            {
                return ilProcessor.Create(OpCodes.Ldarg_S, (byte)i);
            }
            else
            {
                return ilProcessor.Create(OpCodes.Ldarg, (short)i);
            }
        }

        TypeReference tryGetUnderlyingType(TypeReference type)
        {
            try
            {
                TypeDefinition typeDefinition = type.Resolve();
                if (typeDefinition.IsEnum)
                {
                    var fields = typeDefinition.Fields;
                    for (int i = 0; i < fields.Count; i++)
                    {
                        if (!fields[i].IsStatic)
                            return nameToTypeReference[fields[i].FieldType.FullName];
                    }
                }
            }
            catch { }
            return type;
        }

        //不能直接栈上表示的值类型，都boxing
        void emitLoadRef(Mono.Collections.Generic.Collection<Instruction> instructions, TypeReference type)
        {
            var underlyingTypetype = tryGetUnderlyingType(type);
            OpCode op;
            if (ldinds.TryGetValue(underlyingTypetype, out op))
            {
                instructions.Add(Instruction.Create(op));
                //if (type == assembly.MainModule.TypeSystem.IntPtr
                //    || type == assembly.MainModule.TypeSystem.UIntPtr)
                //{
                //    instructions.Add(Instruction.Create(OpCodes.Box, type));
                //}
            }
            else
            {
                if (type.IsValueType)
                {
                    instructions.Add(Instruction.Create(OpCodes.Ldobj, type));
                    instructions.Add(Instruction.Create(OpCodes.Box, type));
                }
                else
                {
                    instructions.Add(Instruction.Create(OpCodes.Ldind_Ref));
                }
            }
        }

        void emitStoreRef(Mono.Collections.Generic.Collection<Instruction> instructions, TypeReference type)
        {
            var underlyingTypetype = tryGetUnderlyingType(type);
            OpCode op;
            if (stinds.TryGetValue(underlyingTypetype, out op))
            {
                instructions.Add(Instruction.Create(op));
            }
            else
            {
                if (type.IsValueType)
                {
                    instructions.Add(Instruction.Create(OpCodes.Stobj, type));
                }
                else
                {
                    instructions.Add(Instruction.Create(OpCodes.Stind_Ref));
                }
            }
        }

        private OpCode[] ldargs = new OpCode[] { OpCodes.Ldarg_0, OpCodes.Ldarg_1, OpCodes.Ldarg_2, OpCodes.Ldarg_3 };
        private OpCode[] ldc4s = new OpCode[] { OpCodes.Ldc_I4_0, OpCodes.Ldc_I4_1, OpCodes.Ldc_I4_2,
            OpCodes.Ldc_I4_3,OpCodes.Ldc_I4_4, OpCodes.Ldc_I4_5, OpCodes.Ldc_I4_6, OpCodes.Ldc_I4_7 };
        private Dictionary<TypeReference, OpCode> ldinds = null;
        private Dictionary<TypeReference, OpCode> stinds = null;

        void init(AssemblyDefinition assembly, AssemblyDefinition ilfixAassembly)
        {
            this.assembly = assembly;
            objType = assembly.MainModule.TypeSystem.Object;
            voidType = assembly.MainModule.TypeSystem.Void;

            wrapperType = new TypeDefinition("IFix", DYNAMICWRAPPER, Mono.Cecil.TypeAttributes.Class
                | Mono.Cecil.TypeAttributes.Public, objType);
            assembly.MainModule.Types.Add(wrapperType);

            TypeDefinition VirtualMachine;
            VirtualMachine = ilfixAassembly.MainModule.Types.Single(t => t.Name == "VirtualMachine");
            VirtualMachineType = assembly.MainModule.ImportReference(VirtualMachine);
            WrappersManagerType = assembly.MainModule.ImportReference(
                ilfixAassembly.MainModule.Types.Single(t => t.Name == "WrappersManager"));

            virualMachineFieldOfWrapper = new FieldDefinition("virtualMachine", Mono.Cecil.FieldAttributes.Private,
                    VirtualMachineType);
            wrapperType.Fields.Add(virualMachineFieldOfWrapper);
            methodIdFieldOfWrapper = new FieldDefinition("methodId", Mono.Cecil.FieldAttributes.Private,
                    assembly.MainModule.TypeSystem.Int32);
            wrapperType.Fields.Add(methodIdFieldOfWrapper);
            anonObjOfWrapper = new FieldDefinition("anonObj", Mono.Cecil.FieldAttributes.Private,
                    objType);
            wrapperType.Fields.Add(anonObjOfWrapper);
            wrapperArray = new FieldDefinition("wrapperArray", Mono.Cecil.FieldAttributes.Public
                | Mono.Cecil.FieldAttributes.Static,
                new ArrayType(wrapperType));
            wrapperType.Fields.Add(wrapperArray);

            idTagCtor_Ref = assembly.MainModule.ImportReference(
                ilfixAassembly.MainModule.Types.Single(t => t.Name == "IDTagAttribute")
                .Methods.Single(m => m.Name == ".ctor" && m.Parameters.Count == 1));

            var objEmptyConstructor = assembly.MainModule.ImportReference(objType.Resolve().Methods.
                Single(m => m.Name == ".ctor" && m.Parameters.Count == 0));
            var methodAttributes = MethodAttributes.Public
                | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName;
            ctorOfWrapper = new MethodDefinition(".ctor", methodAttributes, voidType);
            ctorOfWrapper.Parameters.Add(new ParameterDefinition("virtualMachine",
                Mono.Cecil.ParameterAttributes.None, VirtualMachineType));
            ctorOfWrapper.Parameters.Add(new ParameterDefinition("methodId",
                Mono.Cecil.ParameterAttributes.None, assembly.MainModule.TypeSystem.Int32));
            ctorOfWrapper.Parameters.Add(new ParameterDefinition("anonObj",
                Mono.Cecil.ParameterAttributes.None, objType));
            var instructions = ctorOfWrapper.Body.Instructions;
            instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            instructions.Add(Instruction.Create(OpCodes.Call, objEmptyConstructor));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            instructions.Add(Instruction.Create(OpCodes.Stfld, virualMachineFieldOfWrapper));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
            instructions.Add(Instruction.Create(OpCodes.Stfld, methodIdFieldOfWrapper));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_3));
            instructions.Add(Instruction.Create(OpCodes.Stfld, anonObjOfWrapper));
            instructions.Add(Instruction.Create(OpCodes.Ret));
            wrapperType.Methods.Add(ctorOfWrapper);

            //begin init itfBridgeType
            bridgeMethodId = 0;
            var anonymousStoreyType = ilfixAassembly.MainModule.Types.Single(t => t.Name == "AnonymousStorey");
            anonymousStoreyTypeRef = assembly.MainModule.ImportReference(anonymousStoreyType);
            anonymousStoreyCtorRef = assembly.MainModule.ImportReference(
                anonymousStoreyType.Methods.Single(m => m.Name == ".ctor" && m.Parameters.Count == 1));

            itfBridgeType = new TypeDefinition("IFix", INTERFACEBRIDGE, TypeAttributes.Class | TypeAttributes.Public,
                    anonymousStoreyTypeRef);
            virualMachineFieldOfBridge = new FieldDefinition("virtualMachine", Mono.Cecil.FieldAttributes.Private,
                    VirtualMachineType);
            itfBridgeType.Fields.Add(virualMachineFieldOfBridge);
            assembly.MainModule.Types.Add(itfBridgeType);

            //end init itfBridgeType

            //begin init idMapper
            var enumType = assembly.MainModule.ImportReference(typeof(System.Enum));
            idMapType = new TypeDefinition("IFix", "IDMAP", TypeAttributes.Public | TypeAttributes.Sealed,
                    enumType);
            assembly.MainModule.Types.Add(idMapType);
            idMapType.Fields.Add(new FieldDefinition("value__", FieldAttributes.Public | FieldAttributes.SpecialName
                | FieldAttributes.RTSpecialName, assembly.MainModule.TypeSystem.Int32));
            //end init idMapper

            wrapperMethods = new List<MethodDefinition>();

            TypeDefinition Call;
            Call = ilfixAassembly.MainModule.Types.Single(t => t.Name == "Call");
            Call_Ref = assembly.MainModule.ImportReference(Call);
            Call_Begin_Ref = importMethodReference(Call, "Begin");
            Call_PushRef_Ref = importMethodReference(Call, "PushRef");
            Call_PushValueType_Ref = importMethodReference(Call, "PushValueType");
            Call_GetAsType_Ref = importMethodReference(Call, "GetAsType");

            VirtualMachine_Execute_Ref = assembly.MainModule.ImportReference(
                VirtualMachine.Methods.Single(m => m.Name == "Execute" && m.Parameters.Count == 4));

            Utils_TryAdapterToDelegate_Ref = assembly.MainModule.ImportReference(
                ilfixAassembly.MainModule.Types.Single(t => t.FullName == "IFix.Core.Utils")
                .Methods.Single(m => m.Name == "TryAdapterToDelegate"));

            ldinds = new Dictionary<TypeReference, OpCode>()
            {
                {assembly.MainModule.TypeSystem.Boolean, OpCodes.Ldind_U1 },
                {assembly.MainModule.TypeSystem.Byte, OpCodes.Ldind_U1 },
                {assembly.MainModule.TypeSystem.SByte, OpCodes.Ldind_I1 },
                {assembly.MainModule.TypeSystem.Int16, OpCodes.Ldind_I2 },
                {assembly.MainModule.TypeSystem.Char, OpCodes.Ldind_U2 },
                {assembly.MainModule.TypeSystem.UInt16, OpCodes.Ldind_U2 },
                {assembly.MainModule.TypeSystem.Int32, OpCodes.Ldind_I4 },
                {assembly.MainModule.TypeSystem.UInt32, OpCodes.Ldind_U4 },
                {assembly.MainModule.TypeSystem.Int64, OpCodes.Ldind_I8 },
                {assembly.MainModule.TypeSystem.UInt64, OpCodes.Ldind_I8 },
                {assembly.MainModule.TypeSystem.Single, OpCodes.Ldind_R4 },
                {assembly.MainModule.TypeSystem.Double, OpCodes.Ldind_R8 },
                {assembly.MainModule.TypeSystem.IntPtr, OpCodes.Ldind_I },
                {assembly.MainModule.TypeSystem.UIntPtr, OpCodes.Ldind_I },
            };

            stinds = new Dictionary<TypeReference, OpCode>()
            {
                {assembly.MainModule.TypeSystem.Boolean, OpCodes.Stind_I1 },
                {assembly.MainModule.TypeSystem.Byte, OpCodes.Stind_I1 },
                {assembly.MainModule.TypeSystem.SByte, OpCodes.Stind_I1 },
                {assembly.MainModule.TypeSystem.Int16, OpCodes.Stind_I2 },
                {assembly.MainModule.TypeSystem.Char, OpCodes.Stind_I2 },
                {assembly.MainModule.TypeSystem.UInt16, OpCodes.Stind_I2 },
                {assembly.MainModule.TypeSystem.Int32, OpCodes.Stind_I4 },
                {assembly.MainModule.TypeSystem.UInt32, OpCodes.Stind_I4 },
                {assembly.MainModule.TypeSystem.Int64, OpCodes.Stind_I8 },
                {assembly.MainModule.TypeSystem.UInt64, OpCodes.Stind_I8 },
                {assembly.MainModule.TypeSystem.Single, OpCodes.Stind_R4 },
                {assembly.MainModule.TypeSystem.Double, OpCodes.Stind_R8 },
                {assembly.MainModule.TypeSystem.IntPtr, OpCodes.Stind_I },
                {assembly.MainModule.TypeSystem.UIntPtr, OpCodes.Stind_I },
            };

            initStackOp(Call, assembly.MainModule.TypeSystem.Boolean);
            initStackOp(Call, assembly.MainModule.TypeSystem.Byte);
            initStackOp(Call, assembly.MainModule.TypeSystem.SByte);
            initStackOp(Call, assembly.MainModule.TypeSystem.Int16);
            initStackOp(Call, assembly.MainModule.TypeSystem.UInt16);
            initStackOp(Call, assembly.MainModule.TypeSystem.Char);
            initStackOp(Call, assembly.MainModule.TypeSystem.Int32);
            initStackOp(Call, assembly.MainModule.TypeSystem.UInt32);
            initStackOp(Call, assembly.MainModule.TypeSystem.Int64);
            initStackOp(Call, assembly.MainModule.TypeSystem.UInt64);
            initStackOp(Call, assembly.MainModule.TypeSystem.Single);
            initStackOp(Call, assembly.MainModule.TypeSystem.Double);
            initStackOp(Call, assembly.MainModule.TypeSystem.Object);
            initStackOp(Call, assembly.MainModule.TypeSystem.IntPtr);
            initStackOp(Call, assembly.MainModule.TypeSystem.UIntPtr);
        }

        void initStackOp(TypeDefinition call, TypeReference type)
        {
            pushMap[type] = importMethodReference(call, "Push" + type.Name);
            getMap[type] = importMethodReference(call, "Get" + type.Name);
            nameToTypeReference[type.FullName] = type;
        }

        void emitCCtor()
        {
            var staticConstructorAttributes =
                    MethodAttributes.Private |
                    MethodAttributes.HideBySig |
                    MethodAttributes.Static |
                    MethodAttributes.SpecialName |
                    MethodAttributes.RTSpecialName;

            MethodDefinition cctor = new MethodDefinition(".cctor", staticConstructorAttributes, voidType);
            wrapperType.Methods.Add(cctor);
            //wrapperType.IsBeforeFieldInit = true;
            var instructions = cctor.Body.Instructions;
            instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 0));
            instructions.Add(Instruction.Create(OpCodes.Newarr, wrapperType));
            instructions.Add(Instruction.Create(OpCodes.Stsfld, wrapperArray));
            instructions.Add(Instruction.Create(OpCodes.Ret));
        }

        void emitWrapperManager()
        {
            wrapperMgrImpl = new TypeDefinition("IFix", "WrappersManagerImpl", Mono.Cecil.TypeAttributes.Class
                | Mono.Cecil.TypeAttributes.Public,
                    objType);
            wrapperMgrImpl.Interfaces.Add(new InterfaceImplementation(WrappersManagerType));

            var virualMachineFieldOfWrapperMgr = new FieldDefinition("virtualMachine", FieldAttributes.Private,
                    VirtualMachineType);
            wrapperMgrImpl.Fields.Add(virualMachineFieldOfWrapperMgr);

            var objEmptyConstructor = assembly.MainModule.ImportReference(objType.Resolve().Methods.
                Single(m => m.Name == ".ctor" && m.Parameters.Count == 0));
            var methodAttributes = MethodAttributes.Public
                | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName;
            ctorOfWrapperMgrImpl = new MethodDefinition(".ctor", methodAttributes, voidType);
            ctorOfWrapperMgrImpl.Parameters.Add(new ParameterDefinition("virtualMachine",
                Mono.Cecil.ParameterAttributes.None, VirtualMachineType));
            wrapperMgrImpl.Methods.Add(ctorOfWrapperMgrImpl);
            var instructions = ctorOfWrapperMgrImpl.Body.Instructions;
            instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            instructions.Add(Instruction.Create(OpCodes.Call, objEmptyConstructor));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            instructions.Add(Instruction.Create(OpCodes.Stfld, virualMachineFieldOfWrapperMgr));
            instructions.Add(Instruction.Create(OpCodes.Ret));

            getPatch = new MethodDefinition("GetPatch", MethodAttributes.Public | MethodAttributes.Static, wrapperType);
            wrapperMgrImpl.Methods.Add(getPatch);
            getPatch.Parameters.Add(new ParameterDefinition("id", Mono.Cecil.ParameterAttributes.None,
                assembly.MainModule.TypeSystem.Int32));
            instructions = getPatch.Body.Instructions;
            instructions.Add(Instruction.Create(OpCodes.Ldsfld, wrapperArray));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));
            instructions.Add(Instruction.Create(OpCodes.Ret));

            isPatched = new MethodDefinition("IsPatched", MethodAttributes.Public | MethodAttributes.Static,
                assembly.MainModule.TypeSystem.Boolean);
            wrapperMgrImpl.Methods.Add(isPatched);
            isPatched.Parameters.Add(new ParameterDefinition("id", Mono.Cecil.ParameterAttributes.None,
                assembly.MainModule.TypeSystem.Int32));
            instructions = isPatched.Body.Instructions;
            instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            instructions.Add(Instruction.Create(OpCodes.Ldsfld, wrapperArray));
            instructions.Add(Instruction.Create(OpCodes.Ldlen));
            instructions.Add(Instruction.Create(OpCodes.Conv_I4));

            var bp0 = Instruction.Create(OpCodes.Ldc_I4_0);
            var bp1 = Instruction.Create(OpCodes.Ret);

            instructions.Add(Instruction.Create(OpCodes.Bge, bp0));
            instructions.Add(Instruction.Create(OpCodes.Ldsfld, wrapperArray));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));
            instructions.Add(Instruction.Create(OpCodes.Ldnull));
            instructions.Add(Instruction.Create(OpCodes.Ceq));
            instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
            instructions.Add(Instruction.Create(OpCodes.Ceq));
            instructions.Add(Instruction.Create(OpCodes.Br_S, bp1));
            instructions.Add(bp0);
            instructions.Add(bp1);

            //CreateDelegate
            createDelegate = new MethodDefinition("CreateDelegate", MethodAttributes.Public
                | MethodAttributes.HideBySig
                | MethodAttributes.NewSlot
                | MethodAttributes.Virtual
                | MethodAttributes.Final, assembly.MainModule.ImportReference(typeof(Delegate)));
            wrapperMgrImpl.Methods.Add(createDelegate);
            createDelegate.Parameters.Add(new ParameterDefinition("type", ParameterAttributes.None,
                assembly.MainModule.ImportReference(typeof(Type))));
            createDelegate.Parameters.Add(new ParameterDefinition("id", ParameterAttributes.None,
                assembly.MainModule.TypeSystem.Int32));
            createDelegate.Parameters.Add(new ParameterDefinition("anon", ParameterAttributes.None, objType));

            VariableDefinition lcw = new VariableDefinition(wrapperType);
            createDelegate.Body.Variables.Add(lcw);

            instructions = createDelegate.Body.Instructions;

            //instructions.Add(Instruction.Create(OpCodes.Call, VirtualMachine_GetGlobal_Ref));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            instructions.Add(Instruction.Create(OpCodes.Ldfld, virualMachineFieldOfWrapperMgr));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_3));
            instructions.Add(Instruction.Create(OpCodes.Newobj, ctorOfWrapper));
            instructions.Add(Instruction.Create(OpCodes.Stloc, lcw));

            //instructions.Add(Instruction.Create(OpCodes.Ldnull));
            instructions.Add(Instruction.Create(OpCodes.Ldloc, lcw));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            instructions.Add(Instruction.Create(OpCodes.Ldstr, Wrap_Perfix));
            instructions.Add(Instruction.Create(OpCodes.Call, Utils_TryAdapterToDelegate_Ref));
            instructions.Add(Instruction.Create(OpCodes.Ret));

            //CreateWrapper
            var createWrapper = new MethodDefinition("CreateWrapper", MethodAttributes.Public
                | MethodAttributes.HideBySig
                | MethodAttributes.NewSlot
                | MethodAttributes.Virtual
                | MethodAttributes.Final, assembly.MainModule.TypeSystem.Object);
            wrapperMgrImpl.Methods.Add(createWrapper);
            createWrapper.Parameters.Add(new ParameterDefinition("id", ParameterAttributes.None,
                assembly.MainModule.TypeSystem.Int32));

            instructions = createWrapper.Body.Instructions;
            //instructions.Add(Instruction.Create(OpCodes.Call, VirtualMachine_GetGlobal_Ref));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            instructions.Add(Instruction.Create(OpCodes.Ldfld, virualMachineFieldOfWrapperMgr));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            instructions.Add(Instruction.Create(OpCodes.Ldnull));
            instructions.Add(Instruction.Create(OpCodes.Newobj, ctorOfWrapper));
            instructions.Add(Instruction.Create(OpCodes.Ret));

            assembly.MainModule.Types.Add(wrapperMgrImpl);

            var initWrapperArray = new MethodDefinition("InitWrapperArray", MethodAttributes.Public
                | MethodAttributes.HideBySig
                | MethodAttributes.NewSlot
                | MethodAttributes.Virtual
                | MethodAttributes.Final, assembly.MainModule.TypeSystem.Object);
            wrapperMgrImpl.Methods.Add(initWrapperArray);
            initWrapperArray.Parameters.Add(new ParameterDefinition("len", ParameterAttributes.None,
                assembly.MainModule.TypeSystem.Int32));
            instructions = initWrapperArray.Body.Instructions;
            instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            instructions.Add(Instruction.Create(OpCodes.Newarr, wrapperType));
            instructions.Add(Instruction.Create(OpCodes.Stsfld, wrapperArray));
            instructions.Add(Instruction.Create(OpCodes.Ldsfld, wrapperArray));
            instructions.Add(Instruction.Create(OpCodes.Ret));

        }

        //void makeCloneFast(AssemblyDefinition ilfixAassembly)
        //{
        //    var clone = ilfixAassembly.MainModule.Types.Single(t => t.Name == "ObjectClone").Methods.
        //        Single(m => m.Name == "Clone");
        //    var instructions = clone.Body.Instructions;
        //    instructions.Clear();
        //    var memberwiseClone = ilfixAassembly.MainModule.ImportReference(
        //        ilfixAassembly.MainModule.TypeSystem.Object.Resolve().Methods
        //        .Single(m => m.Name == "MemberwiseClone"));
        //    instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        //    instructions.Add(Instruction.Create(OpCodes.Call, memberwiseClone));
        //    instructions.Add(Instruction.Create(OpCodes.Ret));
        //}

        private MethodReference importMethodReference(TypeDefinition type, string name)
        {
            return assembly.MainModule.ImportReference(type.Methods.Single(m => m.Name == name));
        }

        static MethodReference makeGenericMethod(MethodReference self, params TypeReference[] arguments)
        {
            if (self.GenericParameters.Count != arguments.Length)
                throw new ArgumentException();

            var instance = new GenericInstanceMethod(self);
            foreach (var argument in arguments)
                instance.GenericArguments.Add(argument);

            return instance;
        }

        const string DYNAMICWRAPPER = "ILFixDynamicMethodWrapper";

        const string INTERFACEBRIDGE = "ILFixInterfaceBridge";

        const string METHODIDPERFIX = "methodId_";

        const string REDIRECT_NAMESPACE = "IFix.RedirectTo";

        Dictionary<TypeDefinition, TypeDefinition> redirectTypeMap = new Dictionary<TypeDefinition, TypeDefinition>();
        Dictionary<MethodDefinition, FieldDefinition> redirectMemberMap
            = new Dictionary<MethodDefinition, FieldDefinition>();
        Dictionary<MethodDefinition, FieldDefinition> redirectIdMap
            = new Dictionary<MethodDefinition, FieldDefinition>();

        TypeDefinition getRedirectType(TypeDefinition type)
        {
            TypeDefinition redirectType;
            if (!redirectTypeMap.TryGetValue(type, out redirectType))
            {
                bool isNestedType = type.DeclaringType != null;
                string ns = "";
                TypeAttributes typeAttributes = TypeAttributes.Class | TypeAttributes.Public
                    | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit;
                if (!isNestedType)
                {
                    ns = string.IsNullOrEmpty(type.Namespace) ? REDIRECT_NAMESPACE
                        : string.Format("{0}.{1}", REDIRECT_NAMESPACE, type.Namespace);
                }
                else
                {
                    typeAttributes = TypeAttributes.Class | TypeAttributes.NestedPublic | TypeAttributes.Abstract
                        | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit;
                }
                redirectType = new TypeDefinition(ns, type.Name, typeAttributes, objType);
                redirectTypeMap.Add(type, redirectType);

                if (type.DeclaringType != null)
                {
                    var redirectParentType = getRedirectType(type.DeclaringType);
                    redirectParentType.NestedTypes.Add(redirectType);
                }
                else
                {
                    assembly.MainModule.Types.Add(redirectType);
                }
            }
            return redirectType;
        }

        void addRedirectIdInfo(MethodDefinition method, int id)
        {
            if (redirectIdMap.ContainsKey(method))
            {
                throw new Exception("try inject method twice: " + method);
            }
            var redirectIdField = new FieldDefinition("tmp_r_field_" + redirectIdMap.Count, FieldAttributes.Public
                | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, idMapType);
            idMapType.Fields.Add(redirectIdField);
            redirectIdField.Constant = id;
            redirectIdMap.Add(method, redirectIdField);
        }

        //先加字段，建立关联关系
        FieldDefinition getRedirectField(MethodDefinition method)
        {
            if (redirectMemberMap.ContainsKey(method))
            {
                throw new Exception("try inject method twice: " + method);
            }

            var redirectType = getRedirectType(method.DeclaringType);

            var redirectField = new FieldDefinition("tmp_r_field_" + redirectMemberMap.Count, FieldAttributes.Public
                | FieldAttributes.Static, wrapperType);
            redirectType.Fields.Add(redirectField);
            redirectMemberMap.Add(method, redirectField);
            return redirectField;
        }

        void addIDTag(MethodDefinition method, int id)
        {
            var newAttr = new CustomAttribute(idTagCtor_Ref)
            {
                ConstructorArguments =
                {
                    new CustomAttributeArgument(assembly.MainModule.TypeSystem.Int32, id)
                }
            };
            method.CustomAttributes.Add(newAttr);
        }

        string getIdInfoName(MethodDefinition method, int id)
        {
            return string.Format("{0}-{1}{2}", method.DeclaringType.FullName.Replace('.', '-')
                .Replace('/', '-'), method.Name, id);
        }

        void redirectFieldRename()
        {
            foreach(var infosOfType in redirectMemberMap.GroupBy(kv => kv.Key.DeclaringType))
            {
                foreach(var methodGroup in infosOfType.GroupBy(kv => kv.Key.Name))
                {
                    var methodName = methodGroup.Key;
                    int id = 0;
                    foreach(var kv in methodGroup)
                    {
                        kv.Value.Name = "_rf_" + methodName + (id++);
                    }
                    if (id > 1) //有重载
                    {
                        id = 0;
                        foreach(var kv in methodGroup)
                        {
                            addIDTag(kv.Key, id++);
                        }
                    }
                }
            }

            HashSet<string> duplicateCheck = new HashSet<string>();
            foreach (var infosOfType in redirectIdMap.GroupBy(kv => kv.Key.DeclaringType))
            {
                foreach (var methodGroup in infosOfType.GroupBy(kv => kv.Key.Name))
                {
                    int id = 0;
                    foreach (var kv in methodGroup)
                    {
                        kv.Value.Name = getIdInfoName(kv.Key, id++);
                        if (duplicateCheck.Contains(kv.Value.Name))
                        {
                            throw new Exception("duplicate id map info:" + kv.Value.Name);
                        }
                        duplicateCheck.Add(kv.Value.Name);
                    }
                    if (id > 1) //有重载
                    {
                        id = 0;
                        foreach (var kv in methodGroup)
                        {
                            addIDTag(kv.Key, id++);
                        }
                    }
                }
            }
        }

        //1、构造函数及析构函数不转，不支持的指令不转，转的函数留下函数定义，所以支持反射
        //2、不转的函数通过反射调用，已转函数调用已转函数在虚拟机内部完成
        //3、已转函数可以支持直接重定向并删除原实现（减包场景），以及保留原实现增加切换代码（修bug场景）
        //4、已转函数需要生成wrap
        //5、TODO：虚函数用base如何处理？私有函数是否需要保留入口？简单函数（比如getter/setter）是否要转？
        //6、应该为基本值类型生成出入栈函数，防止过大GC
        //7、Callvirt分析其是否真的是虚函数，虚函数反射调用，非虚并且动态方法直接调用
        //8、泛型等同多了一个Type[]参数
        //工具输入一个dll，输出dll+dif
        Dictionary<MethodDefinition, InjectType> methodToInjectType = new Dictionary<MethodDefinition, InjectType>();
        bool hasRedirect = false;
        ProcessMode mode;
        GenerateConfigure configure;

        public ProcessResult Process(AssemblyDefinition assembly, AssemblyDefinition ilfixAassembly,
            GenerateConfigure configure, ProcessMode mode)
        {
            if (assembly.MainModule.Types.Any(t => t.Name == DYNAMICWRAPPER))
            {
                return ProcessResult.Processed;
            }

            this.mode = mode;
            this.configure = configure;
            nextAllocId = 0;

            init(assembly, ilfixAassembly);

            emitWrapperManager();
            //makeCloneFast(ilfixAassembly);

            var allTypes = (from type in assembly.GetAllType()
                            where type.Namespace != "IFix" && !type.IsGeneric() && !isCompilerGenerated(type)
                            select type);

            foreach (var method in (
                from type in allTypes
                where !isCompilerGenerated(type) && !type.HasGenericParameters
                from method in type.Methods
                where !method.IsConstructor && !isCompilerGenerated(method) && !method.HasGenericParameters
                select method))
            {
                int flag;
                if (configure.TryGetConfigure("IFix.InterpretAttribute", method, out flag))
                {
                    methodToInjectType[method] = InjectType.Redirect;
                    hasRedirect = true;
                }
                else if(configure.TryGetConfigure("IFix.IFixAttribute", method, out flag))
                {
                    methodToInjectType[method] = InjectType.Switch;
                }
            }

            foreach(var kv in methodToInjectType)
            {
                processMethod(kv.Key);
            }

            genCodeForCustomBridge();

            emitCCtor();

            postProcessInterfaceBridge();

            if (mode == ProcessMode.Inject)
            {
                redirectFieldRename();
            }

            return ProcessResult.OK;
        }

        void postProcessInterfaceBridge()
        {
            //foreach(var g in itfBridgeType.Methods.GroupBy(m => m.Name).Select(g => g.ToList()))
            //{
            //    if (g.Count > 1)
            //    {

            //    }
            //}

            //为getter setter增加对应的property
            foreach (var m in itfBridgeType.Methods)
            {
                if (m.IsSpecialName && !m.IsConstructor)
                {
                    var name = m.Name;
                    int dotPos = name.LastIndexOf('.');
                    if (dotPos > 0)
                    {
                        name = name.Substring(dotPos + 1);
                    }

                    if (!name.StartsWith("get_") && !name.StartsWith("set_"))
                    {
                        throw new NotImplementedException("do not support special method: " + m);
                    }

                    var propName = name.Substring(4);
                    if (dotPos > 0)
                    {
                        propName = m.Name.Substring(0, dotPos + 1) + propName;
                    }
                    var prop = itfBridgeType.Properties.SingleOrDefault(p => p.Name == propName);
                    if (prop == null)
                    {
                        if (name.StartsWith("get_"))
                        {
                            prop = new PropertyDefinition(propName, PropertyAttributes.None, m.ReturnType);
                        }
                        else
                        {
                            prop = new PropertyDefinition(propName, PropertyAttributes.None,
                                m.Parameters[0].ParameterType);
                        }
                        itfBridgeType.Properties.Add(prop);
                    }
                    if (name.StartsWith("get_"))
                    {
                        prop.GetMethod = m;
                    }
                    else
                    {
                        prop.SetMethod = m;
                    }
                }
            }

            //bridge的构造函数
            var methodIdFields = itfBridgeType.Fields.Where(f => f.Name.StartsWith(METHODIDPERFIX)).ToList();
            int methodIdPerfixLen = METHODIDPERFIX.Length;
            methodIdFields.Sort((l, r) => int.Parse(l.Name.Substring(methodIdPerfixLen))
                - int.Parse(r.Name.Substring(methodIdPerfixLen)));
            var ctorOfItfBridgeType = new MethodDefinition(".ctor", MethodAttributes.Public
                | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName, voidType);
            ctorOfItfBridgeType.Parameters.Add(new ParameterDefinition("fieldNum",
                Mono.Cecil.ParameterAttributes.None, assembly.MainModule.TypeSystem.Int32));
            ctorOfItfBridgeType.Parameters.Add(new ParameterDefinition("methodIdArray",
                Mono.Cecil.ParameterAttributes.None, new ArrayType(assembly.MainModule.TypeSystem.Int32)));
            ctorOfItfBridgeType.Parameters.Add(new ParameterDefinition("virtualMachine",
                Mono.Cecil.ParameterAttributes.None, VirtualMachineType));
            var instructions = ctorOfItfBridgeType.Body.Instructions;
            instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            var callBaseCtor = Instruction.Create(OpCodes.Call, anonymousStoreyCtorRef);
            instructions.Add(callBaseCtor);

            instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_3));
            instructions.Add(Instruction.Create(OpCodes.Stfld, virualMachineFieldOfBridge));

            for (int i = 0; i < methodIdFields.Count; i++)
            {
                instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
                emitLdcI4(instructions, i);
                instructions.Add(Instruction.Create(OpCodes.Ldelem_I4));
                instructions.Add(Instruction.Create(OpCodes.Stfld, methodIdFields[i]));
            }

            instructions.Add(Instruction.Create(OpCodes.Ret));

            var insertPoint = callBaseCtor.Next;
            var processor = ctorOfItfBridgeType.Body.GetILProcessor();
            processor.InsertBefore(insertPoint, Instruction.Create(OpCodes.Ldarg_2));
            processor.InsertBefore(insertPoint, Instruction.Create(OpCodes.Ldlen));
            processor.InsertBefore(insertPoint, Instruction.Create(OpCodes.Conv_I4));
            processor.InsertBefore(insertPoint, createLdcI4(methodIdFields.Count));
            processor.InsertBefore(insertPoint, Instruction.Create(OpCodes.Beq_S, insertPoint));
            processor.InsertBefore(insertPoint, Instruction.Create(OpCodes.Ldstr, "invalid length of methodId array"));
            processor.InsertBefore(insertPoint, Instruction.Create(OpCodes.Newobj,
                assembly.MainModule.ImportReference(typeof(Exception)
                    .GetConstructor(new Type[] { typeof(string) }))));
            processor.InsertBefore(insertPoint, Instruction.Create(OpCodes.Throw));

            itfBridgeType.Methods.Add(ctorOfItfBridgeType);

            //在WrappersManagerImpl增加创建接口
            var createBridge = new MethodDefinition("CreateBridge", MethodAttributes.Public
                | MethodAttributes.HideBySig
                | MethodAttributes.NewSlot
                | MethodAttributes.Virtual
                | MethodAttributes.Final, anonymousStoreyTypeRef);
            createBridge.Parameters.Add(new ParameterDefinition("fieldNum", Mono.Cecil.ParameterAttributes.None,
                assembly.MainModule.TypeSystem.Int32));
            createBridge.Parameters.Add(new ParameterDefinition("slots", Mono.Cecil.ParameterAttributes.None,
                new ArrayType(assembly.MainModule.TypeSystem.Int32)));
            createBridge.Parameters.Add(new ParameterDefinition("virtualMachine", Mono.Cecil.ParameterAttributes.None,
                VirtualMachineType));
            instructions = createBridge.Body.Instructions;
            instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_3));
            instructions.Add(Instruction.Create(OpCodes.Newobj, ctorOfItfBridgeType));
            instructions.Add(Instruction.Create(OpCodes.Ret));

            wrapperMgrImpl.Methods.Add(createBridge);
            
        }

        void genCodeForCustomBridge()
        {
            var customBirdgeTypes = (
                from module in assembly.Modules
                from type in module.Types
                where type.CustomAttributes.Any(ca => ca.AttributeType.FullName == "IFix.CustomBridgeAttribute")
                from method in type.Methods
                where method.IsConstructor && method.Name == ".cctor" && method.Body != null
                    && method.Body.Instructions != null
                from instruction in method.Body.Instructions
                where instruction.OpCode.Code == Code.Ldtoken && instruction.Operand is TypeReference
                select instruction.Operand as TypeReference);
            foreach(var t in customBirdgeTypes)
            {
                var td = t.Resolve();
                if(td.IsDelegate())
                {
                    var invoke = td.Methods.Single(m => m.Name == "Invoke");
                    if (t.IsGenericInstance)
                    {
                        getWrapperMethod(wrapperType, anonObjOfWrapper, invoke.MakeGeneric(t), true, true);
                    }
                    else
                    {
                        getWrapperMethod(wrapperType, anonObjOfWrapper, invoke, true, true);
                    }
                }
                else if (td.IsInterface)
                {
                    addInterfaceToBridge(t);
                }
            }
        }

        void writeMethod(BinaryWriter writer, MethodReference method)
        {
            writer.Write(method.IsGenericInstance);
            if (method.IsGenericInstance)
            {
                //Console.WriteLine("GenericInstance:" + externMethod);
                writer.Write(externTypeToId[method.DeclaringType]);
                writer.Write(method.Name);
                var typeArgs = ((GenericInstanceMethod)method).GenericArguments;
                writer.Write(typeArgs.Count);
                foreach (var typeArg in typeArgs)
                {
                    writer.Write(externTypeToId[typeArg]);
                }
                writer.Write(method.Parameters.Count);
                //var genericParameters = ((GenericInstanceMethod)externMethod).ElementMethod.GenericParameters;
                //Console.WriteLine(">>>" + ((GenericInstanceMethod)externMethod).GetElementMethod());
                //Console.WriteLine(((GenericInstanceMethod)externMethod).ElementMethod.HasGenericParameters);
                //foreach (var gp in genericParameters)
                //{
                //    Console.WriteLine("gp:" + gp + ",gpdm:" + gp.Type);
                //}
                foreach (var p in method.Parameters)
                {
                    bool paramIsGeneric = p.ParameterType.HasGenericArgumentFromMethod();
                    writer.Write(paramIsGeneric);
                    if (paramIsGeneric)
                    {
                        //
                        //if (System.Text.RegularExpressions.Regex.IsMatch(p.ParameterType.FullName, @"!!\d+"))
                        //{

                        //}
                        //if (p.ParameterType.IsGenericParameter)
                        //{
                        //    bool isGP = false;
                        //    for(int k =0; k < genericDefinition.GenericParameters.Count; k++)
                        //    {
                        //        if (p.ParameterType == genericDefinition.GenericParameters[k])
                        //        {
                        //            isGP = true;
                        //            break;
                        //        }
                        //    }
                        //    if (!isGP)
                        //    {
                        //        var paramType = findGenericArg(externMethod.DeclaringType, p.ParameterType.Name);
                        //        if (paramType == null)
                        //        {
                        //            throw new InvalidProgramException("can not resolve method:" + externMethod);
                        //        }

                        //    }
                        //}
                        //Console.WriteLine("p.ParameterType:" + p.ParameterType.FullName + ",isg:"
                        //    + p.ParameterType.IsGenericInstance);
                        if (p.ParameterType.IsGenericParameter)
                        {
                            writer.Write(p.ParameterType.Name);
                        }
                        else
                        {
                            writer.Write(p.ParameterType.GetAssemblyQualifiedName(method.DeclaringType, true));
                        }
                    }
                    else
                    {
                        if (p.ParameterType.IsGenericParameter)
                        {
                            writer.Write(externTypeToId[(p.ParameterType as GenericParameter)
                                .ResolveGenericArgument(method.DeclaringType)]);
                        }
                        else
                        {
                            writer.Write(externTypeToId[p.ParameterType]);
                        }
                    }
                }
            }
            else
            {
                //Console.WriteLine("not GenericInstance:" + externMethod);
                if (!externTypeToId.ContainsKey(method.DeclaringType))
                {
                    throw new Exception("externTypeToId do not exist key: " + method.DeclaringType
                        + ", while process method: " + method);
                }
                writer.Write(externTypeToId[method.DeclaringType]);
                writer.Write(method.Name);
                writer.Write(method.Parameters.Count);
                foreach (var p in method.Parameters)
                {
                    var paramType = p.ParameterType;
                    if (paramType.IsGenericParameter)
                    {
                        paramType = (paramType as GenericParameter).ResolveGenericArgument(method.DeclaringType);
                    }
                    if (!externTypeToId.ContainsKey(paramType))
                    {
                        throw new Exception("externTypeToId do not exist key: " + paramType
                            + ", while process parameter of method: " + method);
                    }
                    //Console.WriteLine("paramType: " + paramType);
                    //Console.WriteLine("externTypeToId[paramType] : " + externTypeToId[paramType]);
                    //Console.WriteLine("externTypes[externTypeToId[paramType]] : "
                    //    + externTypes[externTypeToId[paramType]]);
                    writer.Write(externTypeToId[paramType]);
                }
            }
        }

        void writeSlotInfo(BinaryWriter writer, TypeDefinition type)
        {
            writer.Write(type.Interfaces.Count);
            //Console.WriteLine(string.Format("-------{0}----------", type.Interfaces.Count));
            foreach(var ii in type.Interfaces)
            {
                var itf = bridgeInterfaces.Find(t => t.AreEqualIgnoreAssemblyVersion(ii.InterfaceType));
                //Console.WriteLine(itf.ToString());
                var itfDef = itf.Resolve();
                writer.Write(externTypeToId[itf]); // interface
                foreach (var method in itfDef.Methods)
                {
                    var itfMethod = itf.IsGenericInstance ? method.MakeGeneric(itf) : method.TryImport(itf.Module);
                    var implMethod = type.Methods.SingleOrDefault(m => itfMethod.CheckImplemention(m));
                    if (implMethod == null)
                    {
                        //Console.WriteLine(string.Format("check {0} in {1}", itfMethod, type));
                        //foreach(var cm in type.Methods)
                        //{
                        //    Console.WriteLine(string.Format("{0} {1}", cm, itfMethod.CheckImplemention(cm)));
                        //}
                        throw new Exception(string.Format("can not find method {0} of {1}", itfMethod, itf));
                    }
                    //Console.WriteLine(string.Format(">>>{0} [{1}]", itfMethod, methodToId[implMethod]));
                    writer.Write(methodToId[implMethod]);
                }
            }
        }

        public void Serialize(string filename)
        {
            if (!hasRedirect && mode == ProcessMode.Inject)
            {
                return;
            }

            using (FileStream output = new FileStream(filename, FileMode.Create, FileAccess.Write))
            {
                Serialize(output);
            }
        }

        //TODO: 预分析，生成link.xml之类的文件
        public void Serialize(Stream output)
        {
            using (BinaryWriter writer = new BinaryWriter(output))
            {
                writer.Write(IFix.Core.Instruction.INSTRUCTION_FORMAT_MAGIC);
                writer.Write(itfBridgeType.GetAssemblyQualifiedName());

                //---------------extern type---------------
                writer.Write(externTypes.Count);
                for (int i = 0; i < externTypes.Count; i++)
                {
                    //Console.WriteLine("externTypes[" + i + "]: " + externTypes[i] + ", is ref:"
                    //    + externTypes[i].IsByReference + ",aqn:" + externTypes[i]
                    //    .GetAssemblyQualifiedName(contextTypeOfExternType[i]));
                    writer.Write(externTypes[i].GetAssemblyQualifiedName(contextTypeOfExternType[i]));
                }
                //Console.WriteLine("serialize code...");
                //---------------code---------------
                writer.Write(nextAllocId);
                //int realCodeNumber = 0;
                for (int i = 0; i < nextAllocId; i++)
                {
                    List<Core.Instruction> code;
                    if (codes.TryGetValue(i, out code) && codeMustWriteToPatch.Contains(i))
                    {
                        //realCodeNumber++;
                        writer.Write(code.Count);
                        //Console.WriteLine("methodid=" + i + ",instruction.cout=" + codes[i].Count);
                        for (int j = 0; j < code.Count; j++)
                        {
                            writer.Write((int)code[j].Code);
                            writer.Write(code[j].Operand);
                            //Console.WriteLine(j + " :code = " + codes[i][j].Code + ", operand="
                            //    + codes[i][j].Operand);
                        }
                        var exhs = methodIdToExceptionHandler[i];
                        writer.Write(exhs.Length);
                        for (int j = 0; j < exhs.Length; j++)
                        {
                            writer.Write((int)exhs[j].HandlerType);
                            writer.Write(exhs[j].CatchTypeId);
                            writer.Write(exhs[j].TryStart);
                            writer.Write(exhs[j].TryEnd);
                            writer.Write(exhs[j].HandlerStart);
                            writer.Write(exhs[j].HandlerEnd);
                        }
                    }
                    else
                    {
                        //Console.WriteLine("no code for methodId:" + i);
                        writer.Write(0);
                        writer.Write(0);
                    }
                }
                //Console.WriteLine("nextAllocId:" + nextAllocId + ", realCodeNumber:" + realCodeNumber);
                //Console.WriteLine("serialize extern method...");
                //---------------extern method signture---------------
                writer.Write(externMethods.Count);
                for (int i = 0; i < externMethods.Count; i++)
                {
                    writeMethod(writer, externMethods[i]);
                }
                //Console.WriteLine("serialize string...");
                writer.Write(internStrings.Count);
                for (int i = 0; i < internStrings.Count; i++)
                {
                    writer.Write(internStrings[i]);
                }
                //Console.WriteLine("serialize field...");
                writer.Write(fields.Count);
                for (int i = 0; i < fields.Count; i++)
                {
                    writer.Write(addExternType(fields[i].DeclaringType));
                    writer.Write(fields[i].Name);
                }

                writer.Write(fieldsStoreInVirtualMachine.Count);
                for (int i = 0; i < fieldsStoreInVirtualMachine.Count; i++)
                {
                    var fieldType = fieldsStoreInVirtualMachine[i].FieldType;
                    if (isCompilerGenerated(fieldType))
                    {
                        fieldType = objType;
                    }
                    writer.Write(addExternType(fieldType));
                    //字段静态构造函数
                    writer.Write(typeToCctor[fieldsStoreInVirtualMachine[i].DeclaringType]);
                }

                //Console.WriteLine("serialize anonymous type...");
                writer.Write(anonymousTypeInfos.Count);
                for(int i = 0; i < anonymousTypeInfos.Count; i++)
                {
                    //Console.WriteLine("anonymous type: " + anonymousTypeInfos[i]);
                    var anonymousType = anonymousTypeInfos[i].DeclaringType as TypeDefinition;
                    writer.Write(anonymousType.Fields.Count);
                    writer.Write(methodToId[anonymousTypeInfos[i]]);
                    writer.Write(anonymousTypeInfos[i].Parameters.Count);
                    writeSlotInfo(writer, anonymousType);
                }

                writer.Write(wrapperMgrImpl.GetAssemblyQualifiedName());
                writer.Write(idMapType.GetAssemblyQualifiedName());

                writer.Write(interpretMethods.Count);
                //Console.WriteLine("interpretMethods.Count:" + interpretMethods.Count);
                //int idx = 0;
                foreach (var kv in interpretMethods)
                {
                    //Console.WriteLine((idx++) +" method:" + kv.Key + ", id:" + kv.Value);
                    writeMethod(writer, kv.Key);
                    writer.Write(kv.Value);
                }
            }

            //var allTypes = (from module in assembly.Modules
            //                from type in module.Types
            //                where type.IsGeneric()
            //                select type).SelectMany(type => new TypeDefinition[] { type }.Concat(type.NestedTypes));

            //foreach (var method in (
            //    from type in allTypes
            //    from method in type.Methods
            //    where method.HasGenericParameters
            //    select method))
            //{
            //    Console.WriteLine("hgp:" + method + ",type:" + method.GetType());
            //}
        }
    }

}