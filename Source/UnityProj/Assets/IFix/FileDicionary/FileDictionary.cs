using System.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace IFix.Core
{
    public unsafe class FileDictionary<TKey, TValue> : IDictionary<TKey, TValue>,IDisposable
        where TKey : IEquatable<TKey>
    {
        #region member

        private IFileDictionaryDelegate<TKey, TValue> funs;
        
        const int SIZE_COUNT = 2;
        const int MAX_CONFLIGCT_TIME = 8;
        private Stream _stream;
        private string _fileName;
        private int _capacity = 0;
        private int _dataOffset = 0;
        private int _size = 0;

        #endregion

        ~FileDictionary()
        {
            Dispose(true);
        }

        #region cache

        private const int CACHE_COUNT = 256;
        private Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _cache = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(CACHE_COUNT);
        private LinkedList<KeyValuePair<TKey, TValue>> _lruQueue = new LinkedList<KeyValuePair<TKey, TValue>>();
        private ICollection _keys;
        private ICollection _values;

        private bool TryGetCache(TKey key, out TValue value)
        {
            LinkedListNode<KeyValuePair<TKey, TValue>> ln;
            bool ret = _cache.TryGetValue(key, out ln);

            if (ret)
            {
                value = ln.Value.Value;
                _lruQueue.Remove(ln);
                _lruQueue.AddLast(ln);
            }
            else
            {
                value = default(TValue);
            }

            return ret;
        }

        private void AddToCache(TKey key, TValue value)
        {
            if (_lruQueue.Count >= CACHE_COUNT)
            {
                var first = _lruQueue.First;
                _lruQueue.RemoveFirst();
                
                first.Value = new KeyValuePair<TKey, TValue>(key, value);
                _cache.Add(key, first);
                _lruQueue.AddLast(first);
            }
            else
            {
                LinkedListNode<KeyValuePair<TKey, TValue>>  lruNode = new LinkedListNode<KeyValuePair<TKey, TValue>>(new KeyValuePair<TKey, TValue>(key, value));
                _cache.Add(key, lruNode);
                _lruQueue.AddLast(lruNode);
            }
        }

        #endregion
        
        #region private

        public static int FindNextPowerOfTwo(int number)
        {
            if (number <= 0)
            {
                return 1; // 最小的2幂是2^0 = 1
            }

            int result = 1;
            while (result < number)
            {
                result <<= 1; // 左移一位，相当于乘以2
            }

            return result;
        }

        private void Resize(int capacity)
        {
            _stream.Seek(0, SeekOrigin.Begin);
            // 写入capacity
            WriteInt(capacity);
            WriteInt(_size);

            int* p;
            fixed (byte* bp = FileDictionaryUtils.intBuff)
            {
                p = (int*)bp;
            }

            *p = -1;

            if (_dataOffset != 0) // 不是第一次resize
            {
                // 移动数据区的数据
                int endIndex = (int)_stream.Length;
                int delta = (capacity - _capacity) * 4;
                int index = endIndex;

                while (index > _dataOffset)
                {
                    int newIndex = index - FileDictionaryUtils.MOVE_BUFF_SIZE;
                    if (_dataOffset > newIndex)
                    {
                        newIndex = _dataOffset;
                    }

                    int size = index - newIndex;

                    _stream.Seek(newIndex, SeekOrigin.Begin);
                    _stream.Read(FileDictionaryUtils.moveTempBuff, 0, size);

                    _stream.Seek(newIndex + delta, SeekOrigin.Begin);
                    _stream.Write(FileDictionaryUtils.moveTempBuff, 0, size);

                    index = newIndex;
                }

                // 把索引区的数据都读取到内存中
                byte[] oldData = new byte[4 * (_capacity + MAX_CONFLIGCT_TIME)];
                int* oldDataPoint = null;

                _stream.Seek(4 * SIZE_COUNT, SeekOrigin.Begin);
                _stream.Read(oldData, 0, 4 * (_capacity + MAX_CONFLIGCT_TIME));

                // 开辟空间,并把索引区数据全部改成-1
                _stream.Seek(4 * SIZE_COUNT, SeekOrigin.Begin);
                for (int i = 0; i < capacity + MAX_CONFLIGCT_TIME; i++)
                {
                    _stream.Write(FileDictionaryUtils.intBuff, 0, 4);
                }

                _dataOffset = (capacity + SIZE_COUNT + MAX_CONFLIGCT_TIME) * 4;
                int oldCapacity = _capacity;
                _capacity = capacity;
                // 重新插入 索引区数据
                fixed (byte* b = oldData)
                {
                    oldDataPoint = (int*)b;
                }

                for (int i = 0; i < oldCapacity + MAX_CONFLIGCT_TIME; i++)
                {
                    int offset = *(oldDataPoint + i);
                    ResetVal(offset);
                }
            }
            else
            {
                // 开辟空间默认值为-1
                for (int i = 0; i < capacity + MAX_CONFLIGCT_TIME; i++)
                {
                    _stream.Write(FileDictionaryUtils.intBuff, 0, 4);
                }

                _dataOffset = (capacity + SIZE_COUNT + MAX_CONFLIGCT_TIME) * 4;
                _capacity = capacity;
                _size = 0;
            }

            _stream.Flush();
        }

        private int ReadInt()
        {
            _stream.Read(FileDictionaryUtils.intBuff, 0, 4);

            int* p;
            fixed (byte* bp = FileDictionaryUtils.intBuff)
            {
                p = (int*)bp;
            }

            return *p;
        }

        private void WriteInt(int val)
        {
            int* p;
            fixed (byte* bp = FileDictionaryUtils.intBuff)
            {
                p = (int*)bp;
            }

            *p = val;

            _stream.Write(FileDictionaryUtils.intBuff, 0, 4);
        }

        private void ResetVal(int offset)
        {
            if (offset < 0)
            {
                return;
            }

            // 拿到原来的string值
            int len;
            TKey key = GetKey(offset, out len);

            DoSetVal(key, offset);
        }

        private void DoSetVal(TKey key, int offset, int resizeTime = 0)
        {
            // 计算CRC32 并计算出来一个索引
            int index = (int)(funs.GetHashCode(key) & (_capacity - 1));

            bool isReset = false;
            for (int i = 0; i < MAX_CONFLIGCT_TIME; i++)
            {
                int ii = index + i;
                if (SetVal(ii, offset))
                {
                    isReset = true;
                    break;
                }
            }

            if (!isReset)
            {
                if (resizeTime > 4)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.Append("hash code conflict is too high,you can change hashcode,values:");
                    for (int i = 0; i < MAX_CONFLIGCT_TIME; i++)
                    {
                        int ii = index + i;
                        _stream.Seek((ii + SIZE_COUNT) * 4, SeekOrigin.Begin);
                        int addr = ReadInt();
                        int keyLen = 0;
                        var k = GetKey(addr, out keyLen);
                        sb.Append(k);
                        sb.Append(",");
                    }
                    sb.Append(key);
                    throw new Exception(sb.ToString());
                }
                else
                {
                    Resize(_capacity * 2);
                    resizeTime++;
                }


                DoSetVal(key, offset, resizeTime);
            }
        }

        private bool SetVal(int index, int offset)
        {
            _stream.Seek((index + SIZE_COUNT) * 4, SeekOrigin.Begin);
            int v = ReadInt();
            if (v < 0)
            {
                _stream.Seek(-4, SeekOrigin.Current);
                WriteInt(offset);
                return true;
            }

            return false;
        }

        private TKey GetKey(int offset, out int len)
        {
            _stream.Seek(offset + _dataOffset, SeekOrigin.Begin);
            len = ReadInt();
            return funs.DeserializeKey(_stream, len);
        }

        private bool TryGetValue(TKey key, out TValue value, out int ofs, out int index, out int oldValueLen, out int keyLen)
        {
            value = default(TValue);
            ofs = 0;
            // 计算CRC32 并计算出来一个索引
            index = (int)(funs.GetHashCode(key) & (_capacity - 1));
            oldValueLen = 0;
            keyLen = 0;

            bool isFind = false;
            TKey k;
            int offset;
            for (int i = 0; i < MAX_CONFLIGCT_TIME; i++)
            {
                int ii = index + i;
                _stream.Seek((ii + SIZE_COUNT) * 4, SeekOrigin.Begin);
                offset = ReadInt();
                // 没有值
                if (offset < 0)
                {
                    return false;
                }

                k = GetKey(offset, out keyLen);
                if (k.Equals(key))
                {
                    ofs = offset;
                    oldValueLen = ReadInt();
                    value = funs.DeserializeValue(_stream, oldValueLen);
                    AddToCache(key, value);
                    return true;
                }
            }

            return isFind;
        }

        #endregion

        #region public
        
        // 文件存在就从文件里面读取 capacity，文件不存在就用capacity new 一个 文件hash表出来
        public FileDictionary(string fileName, int capacity = 1024, bool isClear = false, bool readOnly = false)
        {
            bool fileExit = File.Exists(fileName);
            if (isClear && fileExit)
            {
                File.Delete(fileName);
                fileExit = false;
            }

            if (readOnly)
            {
                _stream = new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.Read);
            }
            else
            {
                _stream = new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            }
            
            _fileName = fileName;
            funs = FileDictionaryDelegateFactory.GetFileDictionaryDelegate<TKey, TValue>();
            if (funs == null)
            {
                throw new Exception(string.Format("Implement IFileDictionaryDelegate<{0}, {1}>", typeof(TKey).Name, typeof(TValue).Name) );
            }

            // 文件不存在就先写入点东西
            if (!fileExit)
            {
                //capacity = Math.Max(1024, capacity);
                capacity = FindNextPowerOfTwo(capacity);
                Resize(capacity);
            }
            else
            {
                _capacity = ReadInt();
                _size = ReadInt();
                _dataOffset = (_capacity + SIZE_COUNT + MAX_CONFLIGCT_TIME) * 4;
            }
        }

        public void Flush()
        {
            _stream.Flush();
        }

        public void Close()
        {
            _stream.Flush();
            _stream.Close();
            _stream = null;
        }

        #endregion

        #region interface

        public bool Contains(object key)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            _stream.Seek(_dataOffset, SeekOrigin.Begin);
            
            for (int i = 0; i < _size; i++)
            {
                int len = ReadInt();
                var key = funs.DeserializeKey(_stream, len);
                len = ReadInt();
                var value = funs.DeserializeValue(_stream, len);

                yield return new KeyValuePair<TKey, TValue>(key, value);
            }
        }


        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void AddNewValue(TKey key, TValue value)
        {
            int offset;
            ++_size;
            _stream.Seek(4, SeekOrigin.Begin);
            WriteInt(_size);
            offset = (int)_stream.Length - _dataOffset;
                
            _stream.Seek(offset + _dataOffset, SeekOrigin.Begin);
            funs.SerializeKey(_stream, key);
            funs.SerializeValue(_stream, value);

            DoSetVal(key, offset);
        }

        public void Add(TKey key, TValue value)
        {
            TValue oldValue;
            int offset, index, oldValueLen, keyLen;
            bool hasValue = TryGetValue(key, out oldValue, out offset, out index, out oldValueLen, out keyLen);
            if (hasValue)
            {
                if (oldValue.Equals(value)) return;
            }
            int newValueLen = funs.GetValueLen(value);
            if (hasValue && oldValueLen >= newValueLen)
            {
                _stream.Seek(offset + _dataOffset + 4 + keyLen, SeekOrigin.Begin);
                funs.SerializeValue(_stream, value);
            }
            else if (hasValue)
            {
                offset = (int)_stream.Length - _dataOffset;
                
                _stream.Seek((index + SIZE_COUNT) * 4, SeekOrigin.Begin);
                WriteInt(offset);
                
                _stream.Seek(offset + _dataOffset, SeekOrigin.Begin);
                funs.SerializeKey(_stream, key);
                funs.SerializeValue(_stream, value);
            }
            else
            {
                ++_size;
                _stream.Seek(4, SeekOrigin.Begin);
                WriteInt(_size);
                offset = (int)_stream.Length - _dataOffset;
                
                _stream.Seek(offset + _dataOffset, SeekOrigin.Begin);
                funs.SerializeKey(_stream, key);
                funs.SerializeValue(_stream, value);

                DoSetVal(key, offset);
            }
        }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            Add(item.Key, item.Value);
        }
        
        public void Add(IDictionary<TKey, TValue> dict)
        {
            foreach (var item in dict)
            {
                Add(item.Key, item.Value);
            }
        }

        public void Clear()
        {
            _stream.Close();
            File.Delete(_fileName);
            _dataOffset = 0;
            _size = 0;

            _stream = new FileStream(_fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            var capacity = _capacity;
            _capacity = 0;
            Resize(capacity);
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return ContainsKey(item.Key);
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            return Remove(item.Key);
        }

        public void CopyTo(Array array, int index)
        {
            throw new NotImplementedException();
        }

        public int Count
        {
            get { return _size; }
        }

        public bool IsSynchronized { get; }
        public object SyncRoot { get; }

        public bool IsReadOnly
        {
            get { return false; }
        }

        public object this[object key]
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public bool ContainsKey(TKey key)
        {
            TValue value;
            bool find = TryGetValue(key, out value);
            return find;
        }

        public bool Remove(TKey key)
        {
            TValue oldValue;
            int offset, index, oldLen, keyLen;
            bool hasValue = TryGetValue(key, out oldValue, out offset, out index, out oldLen, out keyLen);
            if (hasValue)
            {
                _stream.Seek((index + SIZE_COUNT) * 4, SeekOrigin.Begin);
                WriteInt(-1);
                _size--;
                _stream.Seek(4, SeekOrigin.Begin);
                WriteInt(_size);
            }

            return hasValue;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            // LRU cahce
            bool ret = TryGetCache(key, out value);
            if (ret) return ret;

            int ofs, index, oldLen, keyLen;
            return TryGetValue(key, out value, out ofs, out index, out oldLen, out keyLen);
        }

        public TValue this[TKey key]
        {
            get
            {
                TValue value;
                bool find = TryGetValue(key, out value);
                if (!find)
                {
                    value = default(TValue);
                }

                return value;
            }
            set { Add(key, value); }
        }

        public Dictionary<TKey, TValue> ToDictionary()
        {
            Dictionary<TKey, TValue> result = new Dictionary<TKey, TValue>(Count);
            _stream.Seek(_dataOffset, SeekOrigin.Begin);
            
            for (int i = 0; i < _size; i++)
            {
                int len = ReadInt();
                var key = funs.DeserializeKey(_stream, len);
                len = ReadInt();
                var value = funs.DeserializeValue(_stream, len);

                result.Add(key, value);
            }

            return result;
        }

        public ICollection<TKey> Keys
        {
            get { throw new NotImplementedException(); }
        }

        public ICollection<TValue> Values
        {
            get { throw new NotImplementedException(); }
        }

        #endregion
        
        private void Dispose(bool disposing)
        {
            _stream?.Dispose();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}