using System;
using System.Runtime.InteropServices;

namespace ECS
{
    static class BinarySerializer
    {
        public static void SerializeInt(int i, byte[] outBytes, ref int startIndex)
        {
            outBytes[startIndex++] = (byte) i;
            outBytes[startIndex++] = (byte)(i >> 8);
            outBytes[startIndex++] = (byte)(i >> 16);
            outBytes[startIndex++] = (byte)(i >> 24);
        }

        public static int DeserializeInt(byte[] bytes, ref int startIndex)
        {
            int i = bytes[startIndex++];
            i |= bytes[startIndex++] << 8;
            i |= bytes[startIndex++] << 16;
            i |= bytes[startIndex++] << 24;
            return i;
        }

        public static void SerializeIntegerArray(int[] arr, byte[] outBytes, ref int startIndex)
        {
            var count = arr.Length * 4;
            Buffer.BlockCopy(arr, 0, outBytes, startIndex, count);
            startIndex += count;
        }

        public static int[] DeserializeIntegerArray(byte[] bytes, ref int startIndex, int sizeOfArr)
        {
            var size = bytes.Length / sizeof(int);
            var arr = new int[sizeOfArr];
            for (var i = 0; i < sizeOfArr; i++, startIndex += sizeof(int))
                arr[i] = BitConverter.ToInt32(bytes, startIndex);

            return arr;
        }

        //struct shouldn't contain reference types to be serialized properly
        public static void SerializeStruct<T>(T str, byte[] outBytes, ref int startIndex)
        {
#if DEBUG
            if (!typeof(T).IsValueType)
                throw new EcsException("T must be the value type");
#endif

            int size = Marshal.SizeOf(str);

            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(str, ptr, true);
            Marshal.Copy(ptr, outBytes, startIndex, size);
            startIndex += size;
            Marshal.FreeHGlobal(ptr);
        }

        public static T DeserializeStruct<T>(byte[] bytes, ref int startIndex, int sizeOfInstance)
        {
#if DEBUG
            if (!typeof(T).IsValueType)
                throw new EcsException("T must be the value type");
#endif

            T str = default;
            IntPtr ptr = Marshal.AllocHGlobal(sizeOfInstance);

            Marshal.Copy(bytes, startIndex, ptr, sizeOfInstance);

            str = (T)Marshal.PtrToStructure(ptr, str.GetType());
            Marshal.FreeHGlobal(ptr);

            startIndex += sizeOfInstance;

            return str;
        }

        /*
         * TODO: serialize without extra allocations
        private static BinaryFormatter binaryFormatter;

        public static void SerializeClass(object obj, byte[] outBytes)
        {
            if (obj == null)
                return;
            if (binaryFormatter == null)
                binaryFormatter = new BinaryFormatter();
            using (MemoryStream ms = new MemoryStream())
            {
                binaryFormatter.Serialize(ms, obj);
                //return ms.ToArray();
            }
        }
        */
    }
}
