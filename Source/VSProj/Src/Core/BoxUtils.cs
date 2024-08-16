using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;
using Unsafe.As;
using Object = System.Object;

namespace IFix.Core
{
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public static unsafe class BoxUtils
    {
        [ThreadStatic]
        private static Stack<object>[] objectPool = null;

        public static readonly int OBJ_OFFSET = 2 * IntPtr.Size;
        public static readonly int ONE_OFFSET = IntPtr.Size;

        [ThreadStatic] 
        private static bool isDummpyInit = false;
        
        public static void InitD()
        {
            //var stack = Thread.GetData(localSlot) as ThreadStackInfo;
            if (!isDummpyInit)
            {
                objectPool = new Stack<object>[256];
                isDummpyInit = true;
            }
        }

        // 第二个位置 用来上锁的。但是一般不会上锁 type，所以拿来存储自定义数据
        // 这个设计的就是不能lock type
        public static void CacheTypeInfo(Type t, object obj = null)
        {
            void** monitorOffset = (void**)UnsafeAsUtility.AsPoint(ref t) + 1;
            if (*monitorOffset != null)
            {
                return;
            }

            void** info = (void**)UnsafeUtility.Malloc(32, 16, Allocator.Persistent);
            UnsafeUtility.MemClear((void*)info, 32);

            /*
             * typeHead（8）
             * isEnum（1）
             * isPrimitive（1）
             * isValueType（1）
             * isNullable（1）
             * size (4)
             * nullable UnderlyingType ptr(8)
             * nullableOffset (4)
             */
            // 把object的typeinfo 保存下来
            bool isNullable = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>);
            if (t.IsValueType && !isNullable)
            {
                if (obj == null) obj = Activator.CreateInstance(t);
                void** typeHead = (void**)UnsafeAsUtility.AsPoint(ref obj);
                *info = *typeHead;
                *((int*)((byte*)info + 12)) = UnsafeUtility.SizeOf(t);
            }
            else
            {
                *info = null;
            }

            bool* bptr = (bool*)info;
            *(bptr + 8) = t.IsEnum;
            *(bptr + 9) = t.IsPrimitive;
            *(bptr + 10) = t.IsValueType;
            *(bptr + 11) = isNullable;

            if (isNullable)
            {
                Type ut = Nullable.GetUnderlyingType(t);
                *((void**)(bptr + 16)) = UnsafeAsUtility.AsPoint(ref ut);
                
                var f = t.GetField("hasValue", BindingFlags.Instance| BindingFlags.NonPublic);
                if (f == null) f = t.GetField("has_value", BindingFlags.Instance| BindingFlags.NonPublic);

                *(int*)(bptr + 24) = UnsafeUtility.GetFieldOffset(f);
            }

            *monitorOffset = info;
        }

        
        /*
         * typeHead（8）
         * isEnum（1）
         * isPrimitive（1）
         * isValueType（1）
         * isNullable（1）
         * size (4)
         * nullable UnderlyingType ptr(8)
         * nullableOffset (4)
         */
        public static void* GetTypeHead(Type t)
        {
            void** monitorOffset = (void**)UnsafeAsUtility.AsPoint(ref t) + 1;
            // 没cache
            if (*monitorOffset == null)
            {
                CacheTypeInfo(t);
            }

            return **(void***)monitorOffset;
        }
        
        public static bool GetTypeIsEnum(Type t)
        {
            void** monitorOffset = (void**)UnsafeAsUtility.AsPoint(ref t) + 1;
            // 没cache
            if (*monitorOffset == null)
            {
                CacheTypeInfo(t);
            }

            return *(*(bool**)monitorOffset + 8);
        }
        
        public static bool GetTypeIsPrimitive(Type t)
        {
            void** monitorOffset = (void**)UnsafeAsUtility.AsPoint(ref t) + 1;
            // 没cache
            if (*monitorOffset == null)
            {
                CacheTypeInfo(t);
            }

            return *(*(bool**)monitorOffset + 9);
        }
        
        public static bool GetTypeIsValueType(Type t)
        {
            void** monitorOffset = (void**)UnsafeAsUtility.AsPoint(ref t) + 1;
            // 没cache
            if (*monitorOffset == null)
            {
                CacheTypeInfo(t);
            }

            return *(*(bool**)monitorOffset + 10);
        }
        
        public static bool GetTypeIsNullable(Type t)
        {
            void** monitorOffset = (void**)UnsafeAsUtility.AsPoint(ref t) + 1;
            // 没cache
            if (*monitorOffset == null)
            {
                CacheTypeInfo(t);
            }

            return *(*(bool**)monitorOffset + 11);
        }
        
        public static int GetTypeSize(Type t)
        {
            void** monitorOffset = (void**)UnsafeAsUtility.AsPoint(ref t) + 1;
            // 没cache
            if (*monitorOffset == null)
            {
                CacheTypeInfo(t);
            }

            return *(*(int**)monitorOffset + 3);
        }
        
        public static Type GetNullableUnderlying(Type t)
        {
            void** monitorOffset = (void**)UnsafeAsUtility.AsPoint(ref t) + 1;
            // 没cache
            if (*monitorOffset == null)
            {
                CacheTypeInfo(t);
            }

            void* ptr = *(void**)((byte*)*monitorOffset + 16);
            return UnsafeAsUtility.RefAs<Type>(&ptr);
        }
        
        public static int GetFieldOffset(FieldInfo fi)
        {
            long* monitorOffset = (long*)UnsafeAsUtility.AsPoint(ref fi) + 1;
            if (*monitorOffset == 0)
            {
                int offset = UnsafeUtility.GetFieldOffset(fi);
                if (fi.ReflectedType.IsValueType)
                {
                    *((int*)monitorOffset) = offset + 2 * OBJ_OFFSET;
                }
                else
                {
                    *((int*)monitorOffset) = offset + OBJ_OFFSET;
                }
            }

            return *((int*)monitorOffset) - OBJ_OFFSET;
        }
        
        public static object CreateDefaultBoxValue(Type t)
        {
            void* p = null;
            void** monitorOffset = (void**)UnsafeAsUtility.AsPoint(ref t) + 1;
            // 没cache
            if (*monitorOffset == null)
            {
                CacheTypeInfo(t);
            }

            byte* typeInfo = (byte*)*monitorOffset;
            bool isNullable = *(bool*)(typeInfo + 11);
            if (isNullable) return null;
            // bool isValueType = *(bool*)(typeInfo + 10);
            // if(isValueType)
            
            return CreateBoxValue(monitorOffset, ref p, true, true);
        }
        
        public static object CreateBoxValue(Type t, bool jumpNulable = false)
        {
            void* p = null;
            return CreateBoxValue(t, ref p, jumpNulable);
        }
        
        public static object CreateBoxValue(Type t, ref void* objPtr, bool jumpNulable = false, bool clearObj = false)
        {
            //if (t == typeof(void)) return null;
            //if (t == null) return null;
            // class type
            //if (!t.IsValueType) return null;
            
            void** monitorOffset = (void**)UnsafeAsUtility.AsPoint(ref t) + 1;
            // 没cache
            if (*monitorOffset == null)
            {
                CacheTypeInfo(t);
            }

            return CreateBoxValue(monitorOffset, ref objPtr, jumpNulable, clearObj);
        }
        
        public static object CreateBoxValue(void** monitorOffset, ref void* objPtr, bool jumpNulable = false, bool clearObj = false)
        {
            byte* typeInfo = (byte*)*monitorOffset;
            bool isValueType = *(bool*)(typeInfo + 10);
            if (!isValueType) return null;
            
            if (!jumpNulable)
            {
                bool isNullable = *(bool*)(typeInfo + 11);
                if (isNullable)
                {
                    var ut = UnsafeAsUtility.RefAs<Type>((void**)(typeInfo + 16));
                    
                    monitorOffset = (void**)UnsafeAsUtility.AsPoint(ref ut) + 1;
                    // 没cache
                    if (*monitorOffset == null)
                    {
                        CacheTypeInfo(ut);
                    }
                    typeInfo = (byte*)*monitorOffset;
                } 
            }
            
            int size = *(int*)(typeInfo + 12);
            int idx = (size + 15) / 16 - 1;
            
            var pool = objectPool[idx];
            if (pool == null)
            {
                pool = new Stack<object>();
                objectPool[idx] = pool;
            }

            object obj = null;
            void* p;
            if (pool.Count > 0)
            {
                //lock (pool)
                {
                    obj = pool.Pop();
                    p = UnsafeAsUtility.AsPoint(ref obj);
                }
            }
            else
            {
                p = UnsafeUtility.Malloc(size + OBJ_OFFSET, OBJ_OFFSET, Allocator.Persistent);
                // 把type头设置到 自定义申请的内存中，这样就可以伪造一个 C#的object
                *((void**)p) = *(void**)typeInfo;
                obj = UnsafeAsUtility.RefAs<object>(&p);
            }
            
            // 第二个字段是 lock的hash,一半我们不会用box对象lock
            // 所以这里直接拿来当是否被cache
            *(int*)((byte*)p + ONE_OFFSET) = size;
            // 把type头设置到 自定义申请的内存中，这样就可以伪造一个 C#的object
            *(void**)p = *(void**)typeInfo;
            objPtr = p;

            if(clearObj)
                UnsafeUtility.MemClear((byte*)p + OBJ_OFFSET, size);

            return obj;
        }

        public static object CreateReturnValue(Type returnType)
        {
            if (!GetTypeIsValueType(returnType)) return null;

            void* p = null;
            return CreateBoxValue(returnType, ref p);
        }
        
        public static object CloneObject(object value)
        {
            if (value == null) return null;
            Type t = value.GetType();
        
            void** monitorOffset = (void**)UnsafeAsUtility.AsPoint(ref t) + 1;
            // 没cache
            if (*monitorOffset == null)
            {
                CacheTypeInfo(t);
            }
            byte* typeInfo = (byte*)*monitorOffset;
            bool isValueType = *(bool*)(typeInfo + 10);
            if (!isValueType) return value;

            void* p = null;
            int len = *(int*)(typeInfo + 12);
            object result = CreateBoxValue(monitorOffset, ref p);
            byte* source = (byte*)UnsafeAsUtility.AsPoint(ref value) + OBJ_OFFSET;
            UnsafeUtility.MemCpy((byte*)p + OBJ_OFFSET, source, len);

            return result;
        }
        
        public static object GetStaticFieldValue(FieldInfo fi, Type t)
        {
            void** monitorOffset = (void**)UnsafeAsUtility.AsPoint(ref t) + 1;
            // 没cache
            if (*monitorOffset == null)
            {
                CacheTypeInfo(t);
            }
            byte* typeInfo = (byte*)*monitorOffset;
            bool isValueType = *(bool*)(typeInfo + 10);
            if (!isValueType) return fi.GetValue(null);

            bool isNullable = *(bool*)(typeInfo + 11);
            void* p = null;
            object ret = CreateBoxValue(monitorOffset, ref p,!isNullable, false);
            UnsafeUtility.GetStaticFieldValue(fi, ret);

            return ret;
        }
        
        public static object GetFieldValue(object thisArg, FieldInfo fi, Type t)
        {
            void** monitorOffset = (void**)UnsafeAsUtility.AsPoint(ref t) + 1;
            // 没cache
            if (*monitorOffset == null)
            {
                CacheTypeInfo(t);
            }
            byte* typeInfo = (byte*)*monitorOffset;
            bool isValueType = *(bool*)(typeInfo + 10);
            if (!isValueType) return fi.GetValue(thisArg);

            bool isNullable = *(bool*)(typeInfo + 11);
            byte* source = (byte*)UnsafeAsUtility.AsPoint(ref thisArg);
            int filedOffset = GetFieldOffset(fi);
            int len = *(int*)(typeInfo + 12);

            if (isNullable)
            {
                var ut = UnsafeAsUtility.RefAs<Type>((void**)(typeInfo + 16));
                int nullableOffset = *(int*)(typeInfo + 24);
                if (*(bool*)(source + filedOffset+ nullableOffset) == false) return null;

                t = ut;
                monitorOffset = (void**)UnsafeAsUtility.AsPoint(ref t) + 1;
                // 没cache
                if (*monitorOffset == null)
                {
                    CacheTypeInfo(t);
                }
                typeInfo = (byte*)*monitorOffset;
                len = *(int*)(typeInfo + 12);
            }

            void* b = null;
            object result = CreateBoxValue(monitorOffset, ref b, true);
            source = source + filedOffset;
            
            UnsafeUtility.MemCpy((byte*)b + OBJ_OFFSET, source, len);

            return result;
        }
        
        public static object BoxEnumObject(Type t, int value)
        {
            void* p = null;
            object obj = CreateBoxValue(t, ref p, true);
            *((int*)((byte*)p + OBJ_OFFSET)) = value;

            return obj;
        }
        
        public static object BoxEnumObject(Type t, long value)
        {
            void* p = null;
            object obj = CreateBoxValue(t, ref p, true);
            *((long*)((byte*)p + OBJ_OFFSET)) = value;

            return obj;
        }
        
        public static object BoxObject<T>(T value, bool jumpTypeCheck = false)
        {
            Type t = typeof(T);
            void** monitorOffset = (void**)UnsafeAsUtility.AsPoint(ref t) + 1;
            // 没cache
            if (*monitorOffset == null)
            {
                CacheTypeInfo(t);
            }
            byte* typeInfo = (byte*)*monitorOffset;

            if (!jumpTypeCheck)
            {
                bool isValueType = *(bool*)(typeInfo + 10);
                if (!isValueType) return value;
            }

            //var dummpy = new Dummpy<T>(value);
            void* addr = UnsafeAsUtility.AsPoint(ref value);// UnsafeUtility.AddressOf(ref dummpy);
            
            bool isNullable = *(bool*)(typeInfo + 11);
            // nullable特殊处理,用offset 判断出来是否为空
            if (isNullable)
            {
                t = UnsafeAsUtility.RefAs<Type>((void**)(typeInfo + 16));
                int offset = *(int*)(typeInfo + 24);
                if (*((bool*)addr + offset) == false) return null;
                monitorOffset = (void**)UnsafeAsUtility.AsPoint(ref t) + 1;
                typeInfo = (byte*)*monitorOffset;

                int len = *(int*)(typeInfo + 12);
                void* p = null;
                object obj = CreateBoxValue(monitorOffset, ref p, true);
                UnsafeUtility.MemCpy((byte*)p + OBJ_OFFSET, addr, len);
                
                return obj;  
            }
            else
            {
                void* p = null;
                int len = *(int*)(typeInfo + 12);
                object obj = CreateBoxValue(monitorOffset, ref p, true);
                UnsafeAsUtility.CopyStructureToPtr(ref value, (byte*)p + OBJ_OFFSET);

                return obj;  
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RecycleObject(object obj)
        {
            if (obj == null) return;
            byte* p = (byte*)UnsafeAsUtility.AsPoint(ref obj);
            // 第二个字段是 lock的hash,一般我们不会用box对象lock
            // 所以这里直接拿来当是否 isInPool
            int* sizePtr = (int*)(p + ONE_OFFSET);
            int size = *sizePtr;
            // monitor的lock 会超过4096的，但是一般value type的长度不会
            if (size == 0 || size > 4096) return;

            objectPool[(size + 15) / 16 - 1].Push(obj);

            //UnsafeUtility.MemClear(p + OBJ_OFFSET, size);
            *sizePtr = 0;
        }
    }
}