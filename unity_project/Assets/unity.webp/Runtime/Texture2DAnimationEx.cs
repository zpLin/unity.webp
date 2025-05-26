using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using unity.libwebp;
using unity.libwebp.Interop;
using UnityEngine;

namespace WebP
{
    /// <summary> Texture2D Animation Extension for WebP </summary>
    public static class Texture2DAnimationEx
    {
        /// <summary> Load WebP animation data and return frames with timestamps </summary>
        public static unsafe (List<(byte[] bytes, int timestamp)> frames, int width, int height) LoadAnimationDataFromWebP(byte[] bytes)
        {
            List<(byte[] bytes, int timestamp)> ret = new();
            int height = 0;
            int width = 0;
            WebPDecoderConfig config = new WebPDecoderConfig();
            if (NativeLibwebp.WebPInitDecoderConfig(&config) == 0)
            {
                throw new Exception("WebPInitDecoderConfig failed. Wrong version?");
            }
            WebPIterator iter = new WebPIterator();
            fixed (byte* p = bytes)
            {
                WebPData webpdata = new WebPData
                {
                    bytes = p,
                    size = new UIntPtr((uint)bytes.Length)
                };
                WebPDemuxer* webPDemuxer = NativeLibwebpdemux.WebPDemuxInternal(&webpdata, 0, (WebPDemuxState*)IntPtr.Zero, NativeLibwebpdemux.WEBP_DEMUX_ABI_VERSION);

                VP8StatusCode result = NativeLibwebp.WebPGetFeatures(webpdata.bytes, webpdata.size, &config.input);
                if (result != VP8StatusCode.VP8_STATUS_OK)
                {
                    throw new Exception(string.Format("Failed WebPGetFeatures with error {0}.", result.ToString()));
                }

                width = config.input.width;
                height = config.input.height;

                int success = NativeLibwebpdemux.WebPDemuxGetFrame(webPDemuxer, 1, &iter);
                if (success != 1)
                {
                    return default;
                }
                int timestamp = 0;
                byte[] fullColor32Bytes = new byte[width * height * 4];

                do
                {

                    int frameWidth = iter.width;
                    int frameHeight = iter.height;
                    int frameSize = (int)iter.fragment.size;
                    WebPData frame = iter.fragment;

                    try
                    {

                        var frameBytes = new byte[frameSize];
                        Marshal.Copy((IntPtr)iter.fragment.bytes, frameBytes, 0, frameSize);
                        var loadedBytes = Texture2DExt.LoadRGBAFromWebP(frameBytes, ref frameWidth, ref frameHeight, false, out var error, null);

                        if (iter.blend_method == WebPMuxAnimBlend.WEBP_MUX_BLEND && iter.has_alpha == 1)
                        {
                            // Blend with previous frame
                            BlendBlockColor32(fullColor32Bytes, width, height, loadedBytes, iter.x_offset, (height - frameHeight) - iter.y_offset, frameWidth, frameHeight);
                        }
                        else if (frameWidth == width && frameHeight == height && iter.x_offset == 0 && iter.y_offset == 0)
                        {
                            // Full frame, replace
                            Buffer.BlockCopy(loadedBytes, 0, fullColor32Bytes, 0, fullColor32Bytes.Length);
                        }
                        else
                        {
                            // Merage with previous frame
                            MerageColor32Bytes(fullColor32Bytes, width, height, loadedBytes, iter.x_offset, (height - frameHeight) - iter.y_offset, frameWidth, frameHeight);
                        }

                        // copy bytes
                        var textureBytes = new byte[fullColor32Bytes.Length];
                        Buffer.BlockCopy(fullColor32Bytes, 0, textureBytes, 0, fullColor32Bytes.Length);

                        timestamp += iter.duration;
                        ret.Add((textureBytes, timestamp));

                        if (iter.dispose_method == WebPMuxAnimDispose.WEBP_MUX_DISPOSE_BACKGROUND)
                        {
                            // Clear background for next frame
                            if (frameWidth == width && frameHeight == height && iter.x_offset == 0 && iter.y_offset == 0)
                                Array.Clear(textureBytes, 0, textureBytes.Length); // Clear full canvas
                            else
                                ClearBlockBytes(fullColor32Bytes, width, height, iter.x_offset, (height - frameHeight) - iter.y_offset, frameWidth, frameHeight);
                        }
                    }
                    catch (Exception err)
                    {
                        UnityEngine.Debug.LogException(err);
                    }
                }
                while (NativeLibwebpdemux.WebPDemuxNextFrame(&iter) == 1);

                NativeLibwebpdemux.WebPDemuxDelete(webPDemuxer);
                NativeLibwebpdemux.WebPDemuxReleaseIterator(&iter);
            }

            return (frames: ret, width: width, height: height);
        }
        /// <summary> Load WebP animation data and return frames as Texture2D with timestamps </summary>
        public static List<(Texture2D texture, int timestamp)> LoadAnimationFromWebP(byte[] bytes, bool lMipmaps, bool lLinear)
        {
            List<ValueTuple<Texture2D, int>> ret = new List<ValueTuple<Texture2D, int>>();

            var animData = LoadAnimationDataFromWebP(bytes);

            var frames = animData.frames;
            int width = animData.width;
            int height = animData.height;
            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                var texture = Texture2DExt.CreateWebpTexture2D(width, height, lMipmaps, lLinear);
                texture.LoadRawTextureData(frame.bytes);
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                ret.Add((texture, frame.timestamp));
            }

            return ret;
        }
        /// <summary> Load a single frame from WebP animation data </summary>
        public static unsafe Texture2D LoadAnimationOneFrameFromWebP(byte[] bytes)
        {
            Texture2D texture = default;
            WebPDecoderConfig config = new WebPDecoderConfig();
            if (NativeLibwebp.WebPInitDecoderConfig(&config) == 0)
            {
                throw new Exception("WebPInitDecoderConfig failed. Wrong version?");
            }
            WebPIterator iter = new WebPIterator();
            fixed (byte* p = bytes)
            {
                WebPData webpdata = new WebPData
                {
                    bytes = p,
                    size = new UIntPtr((uint)bytes.Length)
                };
                WebPDemuxer* webPDemuxer = NativeLibwebpdemux.WebPDemuxInternal(&webpdata, 0, (WebPDemuxState*)IntPtr.Zero, NativeLibwebpdemux.WEBP_DEMUX_ABI_VERSION);

                VP8StatusCode result = NativeLibwebp.WebPGetFeatures(webpdata.bytes, webpdata.size, &config.input);
                if (result != VP8StatusCode.VP8_STATUS_OK)
                {
                    throw new Exception(string.Format("Failed WebPGetFeatures with error {0}.", result.ToString()));
                }

                int height = config.input.height;
                int width = config.input.width;
                int success = NativeLibwebpdemux.WebPDemuxGetFrame(webPDemuxer, 1, &iter);
                if (success != 1)
                {
                    return texture;
                }
                int frameWidth = iter.width;
                int frameHeight = iter.height;
                int size = (int)iter.fragment.size;
                WebPData frame = iter.fragment;
                var frameBytes = new byte[size];
                Marshal.Copy((IntPtr)iter.fragment.bytes, frameBytes, 0, size);
                var loadedBytes = Texture2DExt.LoadRGBAFromWebP(frameBytes, ref frameWidth, ref frameHeight, false, out var error, null);
                texture = Texture2DExt.CreateWebpTexture2D(width, height, isUseMipmap: false, isLinear: false);


                byte[] fullColor32Bytes = new byte[width * height * 4];
                MerageColor32Bytes(fullColor32Bytes, width, height, loadedBytes, iter.x_offset, (height - frameHeight) - iter.y_offset, frameWidth, frameHeight);
                texture.LoadRawTextureData(fullColor32Bytes);
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

                NativeLibwebpdemux.WebPDemuxDelete(webPDemuxer);
                NativeLibwebpdemux.WebPDemuxReleaseIterator(&iter);
            }
            return texture;
        }
        /// <summary> Clear a block of pixels in the canvas byte array </summary>
        public unsafe static void ClearBlockBytes(byte[] canvasBytes, int canvasWidth, int canvasHeight, int x, int y, int blockWidth, int blockHeight)
        {
            const int bytesPerPixel = 4;
            int rowStride = canvasWidth * bytesPerPixel;

            fixed (byte* ptr = canvasBytes)
            {
                for (int row = 0; row < blockHeight; row++)
                {
                    int py = y + row;
                    if (py < 0 || py >= canvasHeight) continue;

                    byte* rowStart = ptr + py * rowStride;
                    byte* pixelStart = rowStart + x * bytesPerPixel;

                    int lineLength = blockWidth * bytesPerPixel;

                    if (x < 0)
                    {
                        int skip = -x;
                        pixelStart += skip * bytesPerPixel;
                        lineLength -= skip * bytesPerPixel;
                    }

                    if (x + blockWidth > canvasWidth)
                    {
                        int overflow = x + blockWidth - canvasWidth;
                        lineLength -= overflow * bytesPerPixel;
                    }
                    Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemClear(pixelStart, lineLength);// Zeroes out
                }
            }
        }
        /// <summary> Merge a block of color32 bytes into the canvas byte array at specified position </summary>
        public unsafe static void MerageColor32Bytes(byte[] canvasBytes, int canvasWidth, int canvasHeight, byte[] blockBytes, int x, int y, int blockWidth, int blockHeight)
        {
            //useAlpha = canvasHeight != blockHeight || canvasHeight != blockHeight;
            if (canvasBytes.Length != canvasWidth * canvasHeight * 4)
                throw new ArgumentException("Canvas byte array length does not match the specified width and height.");
            if (blockBytes.Length != blockWidth * blockHeight * 4)
                throw new ArgumentException($"Block byte array length {blockBytes.Length} does not match the specified width and height.");

            /*
            for (int i = 0; i < blockHeight; i++)
            {
                for (int j = 0; j < blockWidth; j++)
                {
                    int canvasIndex = ((y + i) * canvasWidth + (x + j)) * 4;
                    int blockIndex = (i * blockWidth + j) * 4;

                    canvasBytes[canvasIndex] = blockBytes[blockIndex];
                    canvasBytes[canvasIndex + 1] = blockBytes[blockIndex + 1];
                    canvasBytes[canvasIndex + 2] = blockBytes[blockIndex + 2];
                    canvasBytes[canvasIndex + 3] = blockBytes[blockIndex + 3];
                }
            }
            */

            const int bytesPerPixel = 4;
            int canvasRowStride = canvasWidth * bytesPerPixel;
            int blockRowStride = blockWidth * bytesPerPixel;

            fixed (byte* canvasPtr = canvasBytes)
            fixed (byte* blockPtr = blockBytes)
            {
                for (int row = 0; row < blockHeight; row++)
                {
                    int canvasY = y + row;
                    if (canvasY < 0 || canvasY >= canvasHeight) continue;

                    int copyStartX = Math.Max(0, x);
                    int copyEndX = Math.Min(canvasWidth, x + blockWidth);

                    if (copyStartX >= copyEndX) continue;

                    int copyWidth = copyEndX - copyStartX;
                    int copyBytes = copyWidth * bytesPerPixel;

                    int canvasOffset = canvasY * canvasRowStride + copyStartX * bytesPerPixel;
                    int blockOffset = row * blockRowStride + (copyStartX - x) * bytesPerPixel;

                    void* dst = canvasPtr + canvasOffset;
                    void* src = blockPtr + blockOffset;

                    Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemCpy(dst, src, copyBytes);
                }
            }
        }
        /// <summary> Blend a block of color32 bytes into the canvas byte array at specified position </summary>
        public unsafe static void BlendBlockColor32(byte[] canvasBytes, int canvasWidth, int canvasHeight, byte[] blockBytes, int x, int y, int blockWidth, int blockHeight)
        {
            if (canvasBytes.Length != canvasWidth * canvasHeight * 4)
                throw new ArgumentException("Canvas byte array length does not match the specified width and height.");
            if (blockBytes.Length != blockWidth * blockHeight * 4)
                throw new ArgumentException($"Block byte array length {blockBytes.Length} does not match the specified width and height.");

            const int bpp = 4;
            int canvasStride = canvasWidth * bpp;
            int blockStride = blockWidth * bpp;

            fixed (byte* canvasPtr = canvasBytes)
            fixed (byte* blockPtr = blockBytes)
            {
                for (int row = 0; row < blockHeight; row++)
                {
                    int canvasY = y + row;
                    if (canvasY < 0 || canvasY >= canvasHeight) continue;

                    for (int col = 0; col < blockWidth; col++)
                    {
                        int canvasX = x + col;
                        if (canvasX < 0 || canvasX >= canvasWidth) continue;

                        byte* dst = canvasPtr + (canvasY * canvasStride + canvasX * bpp);
                        byte* src = blockPtr + (row * blockStride + col * bpp);

                        byte srcR = src[0], srcG = src[1], srcB = src[2], srcA = src[3];
                        if (srcA == 0)
                        {
                            // If source alpha is 0, skip blending
                            continue;
                        }
                        else if (srcA == 255)
                        {
                            // If source alpha is 255, copy directly
                            dst[0] = srcR;
                            dst[1] = srcG;
                            dst[2] = srcB;
                            dst[3] = srcA; // optional
                            continue;
                        }

                        byte dstR = dst[0], dstG = dst[1], dstB = dst[2], dstA = dst[3];
                        int invA = 255 - srcA;

                        dst[0] = (byte)((srcR * srcA + dstR * invA) / 255);
                        dst[1] = (byte)((srcG * srcA + dstG * invA) / 255);
                        dst[2] = (byte)((srcB * srcA + dstB * invA) / 255);
                        dst[3] = (byte)(srcA + (dstA * invA) / 255); // optional
                    }
                }
            }
            /*
            for (int i = 0; i < blockHeight; i++)
            {
                for (int j = 0; j < blockWidth; j++)
                {
                    int canvasIndex = ((y + i) * canvasWidth + (x + j)) * 4;
                    int blockIndex = (i * blockWidth + j) * 4;

                    byte aSrc = blockBytes[blockIndex + 3];
                    byte aDst = canvasBytes[canvasIndex + 3];

                    // let channel integer alpha blending
                    byte r = (byte)((blockBytes[blockIndex] * aSrc + canvasBytes[canvasIndex] * (255 - aSrc)) / 255);
                    byte g = (byte)((blockBytes[blockIndex + 1] * aSrc + canvasBytes[canvasIndex + 1] * (255 - aSrc)) / 255);
                    byte b = (byte)((blockBytes[blockIndex + 2] * aSrc + canvasBytes[canvasIndex + 2] * (255 - aSrc)) / 255);

                    // Optional: result alpha calc（optional）
                    byte a = (byte)(aSrc + aDst * (255 - aSrc) / 255);

                    canvasBytes[canvasIndex] = r;
                    canvasBytes[canvasIndex + 1] = g;
                    canvasBytes[canvasIndex + 2] = b;
                    canvasBytes[canvasIndex + 3] = a;

                }
            }
            */
        }
        /// <summary> Convert Color32 array to byte array </summary>
        public static byte[] ConvertColor32ToBytes(Color32[] colors, int x, int y, int blockWidth, int blockHeight)
        {
            int pixelCount = blockWidth * blockHeight;
            byte[] rawBytes = new byte[pixelCount * 4];
            for (int i = 0; i < pixelCount; i++)
            {
                int index = i * 4;
                Color32 color = colors[i];
                rawBytes[index] = color.r;
                rawBytes[index + 1] = color.g;
                rawBytes[index + 2] = color.b;
                rawBytes[index + 3] = color.a;
            }
            return rawBytes;

        }
        /// <summary> Convert byte array to Color32 array </summary>
        public static Color32[] ConvertBytesToColor32(byte[] rawBytes)
        {
            if (rawBytes.Length % 4 != 0)
                throw new ArgumentException("Input byte array length must be divisible by 4.");

            int pixelCount = rawBytes.Length / 4;
            Color32[] colors = new Color32[pixelCount];

            for (int i = 0; i < pixelCount; i++)
            {
                int index = i * 4;
                colors[i] = new Color32(
                    rawBytes[index],     // R
                    rawBytes[index + 1], // G
                    rawBytes[index + 2], // B
                    rawBytes[index + 3]  // A
                );
            }

            return colors;
        }

        /// <summary> Check WebP format </summary>
        public static bool IsWebP(byte[] imageData)
        {
            return imageData?.Length > 11 &&
                     imageData[0] == 0x52 && // 'R'
                     imageData[1] == 0x49 && // 'I'
                     imageData[2] == 0x46 && // 'F'
                     imageData[3] == 0x46 && // 'F'
                     imageData[8] == 0x57 && // 'W'
                     imageData[9] == 0x45 && // 'E'
                     imageData[10] == 0x42 && // 'B'
                     imageData[11] == 0x50;   // 'P'
        }

        /// <summary> Check if WebP is animated </summary>
        public static bool IsAnimatedWebP(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12)
                return false;
            // check RIFF header
            using (var ms = new System.IO.MemoryStream(bytes))
            using (var br = new System.IO.BinaryReader(ms))
            {
                // header RIFF + size + WEBP
                if (bytes.Length < 12)
                    return false;

                ms.Seek(12, System.IO.SeekOrigin.Begin); // Skip RIFF + Size + WEBP header

                while (ms.Position + 8 <= ms.Length)
                {
                    // read chunk type
                    string chunkType = new string(br.ReadChars(4));
                    if (ms.Position + 4 > ms.Length)
                        break;

                    int chunkSize = br.ReadInt32();

                    if (chunkType == "VP8X")
                    {
                        if (ms.Position + 1 > ms.Length)
                            break;

                        byte flags = br.ReadByte();
                        return (flags & 0b00000010) != 0; // Bit 1: Animation flag
                    }
                    else if (chunkType == "ANIM")
                    {
                        return true; // Animated WebP
                    }

                    long skipSize = chunkSize + (chunkSize % 2); // chunk size must be even
                    ms.Seek(skipSize, System.IO.SeekOrigin.Current);
                }

            }
            return false;
        }

        /// <summary> Check if WebP has alpha channel </summary>
        public static bool HasAlphaWebP(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes))
            using (var br = new BinaryReader(ms))
            {
                if (bytes.Length < 12)
                    return false;

                ms.Seek(12, SeekOrigin.Begin); // Skip RIFF header (12 bytes)

                while (ms.Position + 8 <= ms.Length)
                {
                    string chunkType = new string(br.ReadChars(4));
                    if (ms.Position + 4 > ms.Length)
                        break;

                    int chunkSize = br.ReadInt32();

                    if (chunkType == "VP8X")
                    {
                        if (ms.Position + 1 > ms.Length)
                            break;

                        byte flags = br.ReadByte();
                        // https://developers.google.com/speed/webp/docs/riff_container
                        return (flags & 0b00010000) != 0; // Bit 4 = Alpha 00010010
                    }

                    // Skip chunk data + padding (to even number)
                    long skipSize = chunkSize + (chunkSize % 2);
                    ms.Seek(skipSize, SeekOrigin.Current);
                }
            }

            return false;
        }
    }
}
