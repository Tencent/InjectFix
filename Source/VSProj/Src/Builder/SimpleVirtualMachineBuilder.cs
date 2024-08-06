/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using System.Reflection;

namespace IFix.Core
{
    using System.Collections.Generic;
    using System;


    public class SimpleVirtualMachineBuilder
    {
        public unsafe int GetValue(long* a, long* b)
        {
            long ret = 0;
            for (int i = 0; i < 1000; i++)
            {
                ret = *a + *b;
            }

            return (int)ret;
        }
        
        public unsafe int CallTest(List<int> v)
        {
            var l = new List<int>();
            l.Add(1);
            l[0] = 1;
            var c = l.Count;
            
            return v.Count;
        }

        public unsafe int CallGetValue(long a, long b)
        {
            KeyValuePair<long, long> v = default(KeyValuePair<long, long>);
            return (int)v.Value;
        }
        
        public static string GetUniqueStringForMethod(MethodBase method)
        {
            var parameters = method.GetParameters();
            var parameterTypeNames = string.Join(",", Array.ConvertAll(parameters, p => p.ParameterType.FullName));
            return $"{method.DeclaringType.FullName}.{method.Name}({parameterTypeNames})";
        }

        unsafe static public VirtualMachine CreateVirtualMachine(int loopCount)
        {
            Instruction[][] methods = new Instruction[][]
            {
                new Instruction[] //int add(int a, int b)
                {
                    new Instruction {Code = Code.StackSpace, Operand = 2 },
                    new Instruction {Code = Code.Ldarg, Operand = 0 },
                    new Instruction {Code = Code.Ldarg, Operand = 1 },
                    new Instruction {Code = Code.Add },
                    new Instruction {Code = Code.Ret , Operand = 1},
                },
                new Instruction[] // void test()
                {
                    new Instruction {Code = Code.StackSpace, Operand = (1 << 16) | 2}, // local | maxstack
                    //TODO: local init
                    new Instruction {Code = Code.Ldc_I4, Operand = 0 }, //1
                    new Instruction {Code = Code.Stloc, Operand = 0},   //2
                    new Instruction {Code = Code.Br, Operand =  9}, // 3

                    new Instruction {Code = Code.Ldc_I4, Operand = 1 }, //4
                    new Instruction {Code = Code.Ldc_I4, Operand = 2 }, //5
                    new Instruction {Code = Code.Call, Operand = (2 << 16) | 0}, //6
                    new Instruction {Code = Code.Pop }, //7

                    new Instruction {Code = Code.Ldloc, Operand = 0 }, //8
                    new Instruction {Code = Code.Ldc_I4, Operand = 1 }, //9
                    new Instruction {Code = Code.Add }, //10
                    new Instruction {Code = Code.Stloc, Operand = 0 }, //11

                    new Instruction {Code = Code.Ldloc, Operand = 0 }, // 12
                    new Instruction {Code = Code.Ldc_I4, Operand =  loopCount}, // 13
                    new Instruction {Code = Code.Blt, Operand = -10 }, //14

                    new Instruction {Code = Code.Ret, Operand = 0 }
                },
                new Instruction[] // call extern fun
                {
                    new Instruction {Code = Code.StackSpace, Operand = (1 << 16) | 1 },
                    new Instruction {Code = Code.Ldarg, Operand = 1 },

                    new Instruction {Code = Code.CallExtern, Operand = (1 << 16) | 2},
                    // new Instruction {Code = Code.Stloc, Operand = 0 },
                    // new Instruction {Code = Code.Ldloc, Operand = 0 },
                    new Instruction {Code = Code.Ret, Operand = 1 }
                }
            };

            List<IntPtr> nativePointers = new List<IntPtr>();

            IntPtr nativePointer = System.Runtime.InteropServices.Marshal.AllocHGlobal(
                sizeof(Instruction*) * methods.Length);
            Instruction** unmanagedCodes = (Instruction**)nativePointer.ToPointer();
            nativePointers.Add(nativePointer);

            for (int i = 0; i < methods.Length; i++)
            {
                nativePointer = System.Runtime.InteropServices.Marshal.AllocHGlobal(
                    sizeof(Instruction) * methods[i].Length);
                unmanagedCodes[i] = (Instruction*)nativePointer.ToPointer();
                for (int j = 0; j < methods[i].Length; j++)
                {
                    unmanagedCodes[i][j] = methods[i][j];
                }
                nativePointers.Add(nativePointer);
            }
            var newM = typeof(List<int>).GetMethod("get_Count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var externMethods = new MethodBase[3];
            externMethods[0] = typeof(SimpleVirtualMachineBuilder).GetMethod("GetValue");
            externMethods[2] = newM;
    
            var ret = new VirtualMachine(unmanagedCodes, () =>
            {
                for (int i = 0; i < nativePointers.Count; i++)
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(nativePointers[i]);
                }
            })
            {
                ExternMethods = externMethods,
            };

            return ret;
        }
    }
}
