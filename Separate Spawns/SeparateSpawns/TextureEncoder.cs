using System;
using System.Reflection;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class TextureEncoder
    {
        public static byte[] EncodeToPng(Texture2D texture)
        {
            var conversionType = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
            if (conversionType != null)
            {
                var method = conversionType.GetMethod("EncodeToPNG", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Texture2D) }, null);
                if (method != null)
                {
                    return (byte[])method.Invoke(null, new object[] { texture });
                }
            }

            return EncodeSimpleBmp(texture);
        }

        private static byte[] EncodeSimpleBmp(Texture2D texture)
        {
            var width = texture.width;
            var height = texture.height;
            var pixels = texture.GetPixels32();
            var bytes = new byte[54 + width * height * 3];
            bytes[0] = (byte)'B';
            bytes[1] = (byte)'M';
            Buffer.BlockCopy(BitConverter.GetBytes(bytes.Length), 0, bytes, 2, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(54), 0, bytes, 10, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(40), 0, bytes, 14, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(width), 0, bytes, 18, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(height), 0, bytes, 22, 4);
            bytes[26] = 1;
            bytes[28] = 24;

            var offset = 54;
            // BMP rows are bottom-up. Unity Texture2D y=0 is also bottom, so write y=0 first.
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var pixel = pixels[y * width + x];
                    bytes[offset++] = pixel.b;
                    bytes[offset++] = pixel.g;
                    bytes[offset++] = pixel.r;
                }
            }

            return bytes;
        }
    }
}
