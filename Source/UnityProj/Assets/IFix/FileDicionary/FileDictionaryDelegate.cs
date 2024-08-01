using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine.Experimental.XR;

namespace IFix.Core
{
    public static class CRC32
    {
        private static readonly uint[] table;

        static CRC32()
        {
            uint polynomial = 0xEDB88320;
            table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) == 1)
                    {
                        crc = (crc >> 1) ^ polynomial;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }

                table[i] = crc;
            }
        }

        public static uint Compute(byte[] bytes)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in bytes)
            {
                crc = (crc >> 8) ^ table[(crc ^ b) & 0xFF];
            }

            return ~crc;
        }
    }
    
    public unsafe static class FileDictionaryUtils
    {
        public static byte[] intBuff = new byte[4];

        public const int MOVE_BUFF_SIZE = 4 * 4096;

        public static byte[] moveTempBuff = new byte[MOVE_BUFF_SIZE];

        public const int SERIALIZE_BUFF_SIZE = 4096 * 4;
        
        public static byte[] SerializeTempBuff = new byte[SERIALIZE_BUFF_SIZE];
        
        public static void WriteInt(Stream stream, int val)
        {
            int* p;
            fixed (byte* bp = intBuff)
            {
                p = (int*)bp;
            }

            *p = val;

            stream.Write(intBuff, 0, 4);
        }
        
        public static int ReadInt(Stream stream)
        {
            stream.Read(intBuff, 0, 4);

            int* p;
            fixed (byte* bp = intBuff)
            {
                p = (int*)bp;
            }

            return *p;
        }
        
        #region int
        
        public static int DeserializeInt(Stream stream, int len)
        {
            byte[] intBuff = FileDictionaryUtils.intBuff;
            stream.Read(intBuff, 0, len);
            
            int* intb;
            fixed (byte* b = intBuff)
            {
                intb = (int*)(b);
            }
            
            return *intb;
        }

        public static void SerializeInt(Stream stream, int value)
        {
            WriteInt(stream, 4);

            byte[] intBuff = FileDictionaryUtils.intBuff;
            int* intb;
            fixed (byte* b = intBuff)
            {
                intb = (int*)(b);
                *intb = value;
            }
            stream.Write(intBuff, 0, 4);
        }
        #endregion
        
        #region bool 

        public static bool DeserializeBool(Stream stream, int len)
        {
            byte[] intBuff = FileDictionaryUtils.intBuff;
            stream.Read(intBuff, 0, len);
            
            fixed (byte* b = intBuff)
            {
                return *b > (byte)0;
            }
        }

        public static void SerializeBool(Stream stream, bool value)
        {
            WriteInt(stream, 1);

            byte[] intBuff = FileDictionaryUtils.intBuff;
            fixed (byte* b = intBuff)
            {
                *b = value ? (byte)1: (byte)0;
            }
            stream.Write(intBuff, 0, 1);
        }
        #endregion

        #region byte[]
        public static byte[] DeserializeBytes(Stream stream, int len)
        {
            byte[] result = new byte[len];
            stream.Read(result, 0, len);

            return result;
        }

        public static void SerializeBytes(Stream stream, byte[] value)
        {
            WriteInt(stream, value.Length);
            stream.Write(value, 0, value.Length);
        }
        #endregion
        
        #region string
        
        public static string DeserializeString(Stream stream, int len)
        {
            var buff = SerializeTempBuff;
            if (len > SerializeTempBuff.Length)
            {
                buff = new byte[len];
            }

            stream.Read(buff, 0, len);
            return System.Text.Encoding.UTF8.GetString(buff, 0, len);
        }
        
        public static void SerializeString(Stream stream, string value)
        {
            int len = System.Text.Encoding.UTF8.GetByteCount(value);
            var buff = SerializeTempBuff;
            if (len <= SerializeTempBuff.Length)
            {
                System.Text.Encoding.UTF8.GetBytes(value, 0, value.Length, buff, 0);
                WriteInt(stream, len);
                stream.Write(buff, 0, len);
            }
            else
            {
                byte[] data = System.Text.Encoding.UTF8.GetBytes(value);
                WriteInt(stream, data.Length);
                stream.Write(data, 0, data.Length);
            }
        }
        
        #endregion
        
        #region string[]
        
        public static string[] DeserializeStringArray(Stream stream, int len)
        {
            byte[] data = SerializeTempBuff;

            if (len > data.Length)
            {
                data = new byte[len];
            }

            stream.Read(data, 0, len);
            string[] result = null;
            fixed (byte* bp = data)
            {
                byte* p = bp;
                int count = (*(int*)p);
                p = p + 4;

                result = new string[count];

                for (int i = 0; i < count; i++)
                {
                    int strLen = (*(int*)p);
                    p = p + 4;
                    result[i] = System.Text.Encoding.UTF8.GetString(data, (int)(p - bp), strLen);
                    p = p + strLen;
                }
            }
            
            return result;
        }
        
        public static int GetValueLen(string[] value)
        {
            // string len
            int len = 4;
            for (int i = 0, imax = value.Length; i < imax; i++)
            {
                len += 4 + UTF8Encoding.UTF8.GetByteCount(value[i]);
            }
            return len;
        }
        
        public static void SerializeStringArray(Stream stream, string[] value)
        {
            WriteInt(stream, GetValueLen(value));
            WriteInt(stream, value.Length);
            
            for (int i = 0, imax = value.Length; i < imax; i++)
            {
                var data = System.Text.Encoding.UTF8.GetBytes(value[i]);
                WriteInt(stream, data.Length);
                stream.Write(data, 0, data.Length);
            }
        }
        #endregion
    }

    public interface IFileDictionaryDelegate<TKey, TValue>
    {
        uint GetHashCode(TKey key);

        TValue DeserializeValue(Stream stream, int len);
        
        int GetValueLen(TValue value);
        
        void SerializeValue(Stream stream, TValue value);

        TKey DeserializeKey(Stream stream, int len);
        void SerializeKey(Stream stream, TKey value);
    }

    public class IntBytesFileDictionaryDelegate : IFileDictionaryDelegate<int, byte[]>
    {
        public static IntBytesFileDictionaryDelegate Default = new IntBytesFileDictionaryDelegate();
        public uint GetHashCode(int key)
        {
            return (uint)key * 2654435761U;
        }

        public int GetValueLen(byte[] value)
        {
            return value.Length;
        }

        public byte[] DeserializeValue(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeBytes(stream, len);
        }

        public void SerializeValue(Stream stream, byte[] value)
        {
            FileDictionaryUtils.SerializeBytes(stream, value);
        }

        public int DeserializeKey(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeInt(stream, len);
        }

        public void SerializeKey(Stream stream, int value)
        {
            FileDictionaryUtils.SerializeInt(stream, value);
        }
    }
    
    public class IntBoolFileDictionaryDelegate : IFileDictionaryDelegate<int, bool>
    {
        public static IntBoolFileDictionaryDelegate Default = new IntBoolFileDictionaryDelegate();
        public uint GetHashCode(int key)
        {
            return (uint)key * 2654435761U;
        }

        public bool DeserializeValue(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeBool(stream, len);
        }

        public int GetValueLen(bool value)
        {
            return 1;
        }

        public void SerializeValue(Stream stream, bool value)
        {
            FileDictionaryUtils.SerializeBool(stream, value);
        }

        public int DeserializeKey(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeInt(stream, len);
        }

        public void SerializeKey(Stream stream, int value)
        {
            FileDictionaryUtils.SerializeInt(stream, value);
        }
        
    }
    
    public class IntStringFileDictionaryDelegate : IFileDictionaryDelegate<int, string>
    {
        public static IntStringFileDictionaryDelegate Default = new IntStringFileDictionaryDelegate();
        
        public uint GetHashCode(int key)
        {
            return (uint)key * 2654435761U;
        }
        
        public int GetValueLen(string value)
        {
            return System.Text.Encoding.UTF8.GetByteCount(value);
        }

        public string DeserializeValue(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeString(stream, len);
        }
        
        public void SerializeValue(Stream stream, string value)
        {
            FileDictionaryUtils.SerializeString(stream, value);
        }

        public int DeserializeKey(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeInt(stream, len);
        }

        public void SerializeKey(Stream stream, int value)
        {
            FileDictionaryUtils.SerializeInt(stream, value);
        }
    }
    
    public class IntIntFileDictionaryDelegate : IFileDictionaryDelegate<int, int>
    {
        public static IntIntFileDictionaryDelegate Default = new IntIntFileDictionaryDelegate();
        public uint GetHashCode(int key)
        {
            return (uint)key * 2654435761U;
        }

        public int DeserializeValue(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeInt(stream, len);
        }

        public int GetValueLen(int value)
        {
            return 4;
        }

        public void SerializeValue(Stream stream, int value)
        {
            FileDictionaryUtils.SerializeInt(stream, value);
        }

        public int DeserializeKey(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeInt(stream, len);
        }

        public void SerializeKey(Stream stream, int value)
        {
            FileDictionaryUtils.SerializeInt(stream, value);
        }
        
    }

    public class IntStringArrayFileDictionaryDelegate : IFileDictionaryDelegate<int, string[]>
    {
        public static IntStringArrayFileDictionaryDelegate Default = new IntStringArrayFileDictionaryDelegate();
        public uint GetHashCode(int key)
        {
            return (uint)key * 2654435761U;
        }

        public string[] DeserializeValue(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeStringArray(stream, len);
        }

        public int GetValueLen(string[] value)
        {
            // string len
            int len = 4;
            for (int i = 0, imax = value.Length; i < imax; i++)
            {
                len += 4 + UTF8Encoding.UTF8.GetByteCount(value[i]);
            }
            return len;
        }

        public void SerializeValue(Stream stream, string[] value)
        {
            FileDictionaryUtils.SerializeStringArray(stream, value);
        }

        public int DeserializeKey(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeInt(stream, len);
        }

        public void SerializeKey(Stream stream, int value)
        {
            FileDictionaryUtils.SerializeInt(stream, value);
        }
        
    }
    
    public class StringBytesFileDictionaryDelegate : IFileDictionaryDelegate<string, byte[]>
    {
        public static StringBytesFileDictionaryDelegate Default = new StringBytesFileDictionaryDelegate();

        public uint GetHashCode(string key)
        {
            var data = System.Text.Encoding.UTF8.GetBytes(key);
            return CRC32.Compute(data);
        }

        public int GetValueLen(byte[] value)
        {
            return value.Length;
        }

        public byte[] DeserializeValue(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeBytes(stream, len);
        }

        public void SerializeValue(Stream stream, byte[] value)
        {
            FileDictionaryUtils.SerializeBytes(stream, value);
        }

        public string DeserializeKey(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeString(stream, len);
        }

        public void SerializeKey(Stream stream, string value)
        {
            FileDictionaryUtils.SerializeString(stream, value);
        }
        
    }
    
    public class StringIntFileDictionaryDelegate : IFileDictionaryDelegate<string, int>
    {
        public static StringIntFileDictionaryDelegate Default = new StringIntFileDictionaryDelegate();

        public uint GetHashCode(string key)
        {
            var data = System.Text.Encoding.UTF8.GetBytes(key);
            return CRC32.Compute(data);
        }

        public int GetValueLen(int value)
        {
            return 4;
        }

        public int DeserializeValue(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeInt(stream, len);
        }

        public void SerializeValue(Stream stream, int value)
        {
            FileDictionaryUtils.SerializeInt(stream, value);
        }

        public string DeserializeKey(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeString(stream, len);
        }

        public void SerializeKey(Stream stream, string value)
        {
            FileDictionaryUtils.SerializeString(stream, value);
        }
        
    }
    
    public class StringBoolFileDictionaryDelegate : IFileDictionaryDelegate<string, bool>
    {
        public static StringBoolFileDictionaryDelegate Default = new StringBoolFileDictionaryDelegate();

        public uint GetHashCode(string key)
        {
            var data = System.Text.Encoding.UTF8.GetBytes(key);
            return CRC32.Compute(data);
        }

        public int GetValueLen(bool value)
        {
            return 1;
        }

        public bool DeserializeValue(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeBool(stream, len);
        }

        public void SerializeValue(Stream stream, bool value)
        {
            FileDictionaryUtils.SerializeBool(stream, value);
        }

        public string DeserializeKey(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeString(stream, len);
        }

        public void SerializeKey(Stream stream, string value)
        {
            FileDictionaryUtils.SerializeString(stream, value);
        }
        
    }
    
    public class StringStringFileDictionaryDelegate : IFileDictionaryDelegate<string, string>
    {
        public static StringStringFileDictionaryDelegate Default = new StringStringFileDictionaryDelegate();
        
        public uint GetHashCode(string key)
        {
            var data = System.Text.Encoding.UTF8.GetBytes(key);
            return CRC32.Compute(data);
        }

        public int GetValueLen(string value)
        {
            return System.Text.Encoding.UTF8.GetByteCount(value);
        }

        public string DeserializeValue(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeString(stream, len);
        }
        
        public void SerializeValue(Stream stream, string value)
        {
            FileDictionaryUtils.SerializeString(stream, value);
        }

        public string DeserializeKey(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeString(stream, len);
        }

        public void SerializeKey(Stream stream, string value)
        {
            FileDictionaryUtils.SerializeString(stream, value);
        }
    }
    
    public class StringStringArrayFileDictionaryDelegate : IFileDictionaryDelegate<string, string[]>
    {
        public static StringStringArrayFileDictionaryDelegate Default = new StringStringArrayFileDictionaryDelegate();
        public uint GetHashCode(string key)
        {
            var data = System.Text.Encoding.UTF8.GetBytes(key);
            return CRC32.Compute(data);
        }

        public string[] DeserializeValue(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeStringArray(stream, len);
        }

        public int GetValueLen(string[] value)
        {
            // string len
            int len = 4;
            for (int i = 0, imax = value.Length; i < imax; i++)
            {
                len += 4 + UTF8Encoding.UTF8.GetByteCount(value[i]);
            }
            return len;
        }

        public void SerializeValue(Stream stream, string[] value)
        {
            FileDictionaryUtils.SerializeStringArray(stream, value);
        }

        public string DeserializeKey(Stream stream, int len)
        {
            return FileDictionaryUtils.DeserializeString(stream, len);
        }

        public void SerializeKey(Stream stream, string value)
        {
            FileDictionaryUtils.SerializeString(stream, value);
        }
    }
    
    public static class FileDictionaryDelegateFactory
    {
        public static IFileDictionaryDelegate<TKey, TValue> GetFileDictionaryDelegate<TKey, TValue>()
        {
            if (typeof(TKey) == typeof(string) && typeof(TValue) == typeof(byte[]))
                return (IFileDictionaryDelegate<TKey, TValue>)StringBytesFileDictionaryDelegate.Default;
            
            if (typeof(TKey) == typeof(string) && typeof(TValue) == typeof(string))
                return (IFileDictionaryDelegate<TKey, TValue>)StringStringFileDictionaryDelegate.Default;
            
            if (typeof(TKey) == typeof(string) && typeof(TValue) == typeof(string[]))
                return (IFileDictionaryDelegate<TKey, TValue>)StringStringArrayFileDictionaryDelegate.Default;

            if (typeof(TKey) == typeof(string) && typeof(TValue) == typeof(int))
                return (IFileDictionaryDelegate<TKey, TValue>)StringIntFileDictionaryDelegate.Default;
            
            if (typeof(TKey) == typeof(string) && typeof(TValue) == typeof(bool))
                return (IFileDictionaryDelegate<TKey, TValue>)StringBoolFileDictionaryDelegate.Default;
            
            if (typeof(TKey) == typeof(int) && typeof(TValue) == typeof(byte[]))
                return (IFileDictionaryDelegate<TKey, TValue>)IntBytesFileDictionaryDelegate.Default;

            if (typeof(TKey) == typeof(int) && typeof(TValue) == typeof(string))
                return (IFileDictionaryDelegate<TKey, TValue>)IntStringFileDictionaryDelegate.Default;

            if (typeof(TKey) == typeof(int) && typeof(TValue) == typeof(string[]))
                return (IFileDictionaryDelegate<TKey, TValue>)IntStringArrayFileDictionaryDelegate.Default;
            
            if (typeof(TKey) == typeof(int) && typeof(TValue) == typeof(int))
                return (IFileDictionaryDelegate<TKey, TValue>)IntIntFileDictionaryDelegate.Default;

            if (typeof(TKey) == typeof(int) && typeof(TValue) == typeof(bool))
                return (IFileDictionaryDelegate<TKey, TValue>)IntBoolFileDictionaryDelegate.Default;
            
            return null;
        }
    }

}