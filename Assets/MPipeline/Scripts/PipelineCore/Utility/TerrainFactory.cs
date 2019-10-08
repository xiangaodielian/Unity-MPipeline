﻿#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Unity.Mathematics;
using Unity.Collections;
using static Unity.Mathematics.math;
using Unity.Collections.LowLevel.Unsafe;
namespace MPipeline
{
    public unsafe sealed class TerrainFactory
    {
        private FileStream streamer;
        private NativeArray<long> positionOffset;
        private int initLevel;
        private byte[] readBuffer;
        private int[] offsetIndicesArr = new int[2];
        private ComputeShader generatorShader;
        private const int CHUNK_SIZE = MTerrain.MASK_RESOLUTION * MTerrain.MASK_RESOLUTION + MTerrain.HEIGHT_RESOLUTION * MTerrain.HEIGHT_RESOLUTION * 2;
        public TerrainFactory(int initialLevel, int mipLevel, string path)
        {
            initLevel = initialLevel;
            generatorShader = Resources.Load<ComputeShader>("TerrainGenerator");
            readBuffer = new byte[CHUNK_SIZE];
            streamer = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite, readBuffer.Length);
            positionOffset = VirtualTextureLoader.GetStreamingPositionOffset(initialLevel, mipLevel);

        }

        private long GetOffset(int2 position, int mipLevel)
        {
            return (long)(0.1 + pow(2.0, mipLevel)) * position.y + position.x + positionOffset[mipLevel] * CHUNK_SIZE;
        }

        public void ReadBytes(int2 position, int mipLevel, byte[] maskBytes, short[] heightBytes)
        {
            mipLevel -= initLevel;
            streamer.Position = GetOffset(position, mipLevel);
            streamer.Read(readBuffer, 0, readBuffer.Length);
            UnsafeUtility.MemCpy(maskBytes.Ptr(), readBuffer.Ptr(), MTerrain.MASK_RESOLUTION * MTerrain.MASK_RESOLUTION);
            UnsafeUtility.MemCpy(heightBytes.Ptr(), readBuffer.Ptr() + MTerrain.MASK_RESOLUTION * MTerrain.MASK_RESOLUTION, MTerrain.HEIGHT_RESOLUTION * MTerrain.HEIGHT_RESOLUTION * 2);
        }

        public void WriteBytes(int2 position, int mipLevel, byte[] maskBytes, short[] heightBytes)
        {
            mipLevel -= initLevel;
            streamer.Position = GetOffset(position, mipLevel);
            UnsafeUtility.MemCpy(readBuffer.Ptr(), maskBytes.Ptr(), MTerrain.MASK_RESOLUTION * MTerrain.MASK_RESOLUTION);
            UnsafeUtility.MemCpy(readBuffer.Ptr() + MTerrain.MASK_RESOLUTION * MTerrain.MASK_RESOLUTION, heightBytes.Ptr(), MTerrain.HEIGHT_RESOLUTION * MTerrain.HEIGHT_RESOLUTION * 2);
            streamer.Write(readBuffer, 0, readBuffer.Length);
        }

        public void BlitMask(int2 targetPosition, int mipLevel, Texture tex, float2 scale, float2 offset)
        {
            ComputeBuffer cb = new ComputeBuffer(MTerrain.MASK_RESOLUTION * MTerrain.MASK_RESOLUTION, sizeof(float));
            generatorShader.SetTexture(0, ShaderIDs._MainTex, tex);
            generatorShader.SetBuffer(0, ShaderIDs._TextureBuffer, cb);
            generatorShader.SetVector(ShaderIDs._TextureSize, float4(scale, offset));
            generatorShader.SetInt(ShaderIDs._Count, MTerrain.MASK_RESOLUTION);
            generatorShader.Dispatch(0, MTerrain.MASK_RESOLUTION / 8, MTerrain.MASK_RESOLUTION / 8, 1);
            float[] cbValue = new float[cb.count];
            cb.GetData(cbValue);
            for (int i = 0; i < cbValue.Length; ++i)
            {
                readBuffer[i] = (byte)(cbValue[i] * 255);
            }
            mipLevel -= initLevel;
            streamer.Position = GetOffset(targetPosition, mipLevel);
            streamer.Write(readBuffer, 0, MTerrain.MASK_RESOLUTION * MTerrain.MASK_RESOLUTION);
            cb.Dispose();
        }

        public void BlitHeight(int2 targetPosition, int mipLevel, Texture tex, float2 scale, float2 offset)
        {
            ComputeBuffer cb = new ComputeBuffer(MTerrain.HEIGHT_RESOLUTION * MTerrain.HEIGHT_RESOLUTION, sizeof(float));
            generatorShader.SetTexture(0, ShaderIDs._MainTex, tex);
            generatorShader.SetBuffer(0, ShaderIDs._TextureBuffer, cb);
            generatorShader.SetVector(ShaderIDs._TextureSize, float4(scale, offset));
            generatorShader.SetInt(ShaderIDs._Count, MTerrain.HEIGHT_RESOLUTION);
            generatorShader.Dispatch(0, MTerrain.HEIGHT_RESOLUTION / 8, MTerrain.HEIGHT_RESOLUTION / 8, 1);
            float[] cbValue = new float[cb.count];
            cb.GetData(cbValue);
            short* ptr = (short*)readBuffer.Ptr();
            for (int i = 0; i < cbValue.Length; ++i)
            {
                ptr[i] = (short)(cbValue[i] * 65535);
            }
            mipLevel -= initLevel;
            streamer.Position = GetOffset(targetPosition, mipLevel) + MTerrain.MASK_RESOLUTION * MTerrain.MASK_RESOLUTION;
            streamer.Write(readBuffer, 0, cbValue.Length * 2);
            cb.Dispose();
        }

        public void ReadHeight(int2 targetPosition, int2 offset, int mipLevel, RenderTexture targetHeight)
        {
            mipLevel -= initLevel;
            offsetIndicesArr[0] = offset.x;
            offsetIndicesArr[1] = offset.y;
            streamer.Position = GetOffset(targetPosition, mipLevel) + MTerrain.MASK_RESOLUTION * MTerrain.MASK_RESOLUTION;
            streamer.Write(readBuffer, 0, MTerrain.HEIGHT_RESOLUTION * MTerrain.HEIGHT_RESOLUTION * 2);
            ComputeBuffer cb = new ComputeBuffer(MTerrain.HEIGHT_RESOLUTION * MTerrain.HEIGHT_RESOLUTION / 2, sizeof(uint));
            cb.SetData(readBuffer, 0, 0, cb.count);
            generatorShader.SetInts(ShaderIDs._OffsetIndex, offsetIndicesArr);
            generatorShader.SetBuffer(4, "_SourceBuffer", cb);
            generatorShader.SetTexture(4, ShaderIDs._DestTex, targetHeight);
            generatorShader.SetInt(ShaderIDs._Count, MTerrain.HEIGHT_RESOLUTION);
            generatorShader.Dispatch(4, MTerrain.HEIGHT_RESOLUTION / 8, MTerrain.HEIGHT_RESOLUTION / 8, 1);
            cb.Dispose();
        }

        public void ReadMask(int2 targetPosition, int2 offset, int mipLevel, RenderTexture targetMask)
        {
            mipLevel -= initLevel;
            offsetIndicesArr[0] = offset.x;
            offsetIndicesArr[1] = offset.y;
            streamer.Position = GetOffset(targetPosition, mipLevel);
            streamer.Write(readBuffer, 0, MTerrain.MASK_RESOLUTION * MTerrain.MASK_RESOLUTION);
            ComputeBuffer cb = new ComputeBuffer(MTerrain.MASK_RESOLUTION * MTerrain.MASK_RESOLUTION / 4, sizeof(uint));
            cb.SetData(readBuffer, 0, 0,cb.count);
            generatorShader.SetBuffer(3, "_SourceBuffer", cb);
            generatorShader.SetInts(ShaderIDs._OffsetIndex, offsetIndicesArr);
            generatorShader.SetTexture(3, ShaderIDs._DestTex, targetMask);
            generatorShader.SetInt(ShaderIDs._Count, MTerrain.MASK_RESOLUTION);
            generatorShader.Dispatch(3, MTerrain.MASK_RESOLUTION / 8, MTerrain.MASK_RESOLUTION / 8, 1);
            cb.Dispose();
        }

        public void GenerateMaskMip(int2 targetPosition, int mipLevel)
        {

            RenderTexture tempMask = RenderTexture.GetTemporary(MTerrain.MASK_RESOLUTION * 2, MTerrain.MASK_RESOLUTION * 2, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear, 1, RenderTextureMemoryless.None, VRTextureUsage.None, false);
            tempMask.filterMode = FilterMode.Point;
            tempMask.enableRandomWrite = true;
            tempMask.Create();
            ReadMask(targetPosition * 2, 0, mipLevel + 1, tempMask);
            ReadMask(targetPosition * 2 + int2(1, 0), int2(MTerrain.MASK_RESOLUTION, 0), mipLevel + 1, tempMask);
            ReadMask(targetPosition * 2 + int2(0, 1), int2(0, MTerrain.MASK_RESOLUTION), mipLevel + 1, tempMask);
            ReadMask(targetPosition * 2 + 1, MTerrain.MASK_RESOLUTION, mipLevel + 1, tempMask);
            RenderTexture mipMask = RenderTexture.GetTemporary(MTerrain.MASK_RESOLUTION, MTerrain.MASK_RESOLUTION, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear, 1, RenderTextureMemoryless.None, VRTextureUsage.None, false);
            mipMask.filterMode = FilterMode.Point;
            mipMask.enableRandomWrite = true;
            mipMask.Create();
            generatorShader.SetTexture(2, ShaderIDs._SourceTex, tempMask);
            generatorShader.SetTexture(2, ShaderIDs._DestTex, mipMask);
            generatorShader.Dispatch(2, MTerrain.MASK_RESOLUTION / 8, MTerrain.MASK_RESOLUTION / 8, 1);
            BlitMask(targetPosition, mipLevel, mipMask, 1, 0);
            RenderTexture.ReleaseTemporary(tempMask);
            RenderTexture.ReleaseTemporary(mipMask);
        }

        public void GenerateHeightMip(int2 targetPosition, int mipLevel)
        {
            RenderTexture tempHeight = RenderTexture.GetTemporary(MTerrain.HEIGHT_RESOLUTION * 2, MTerrain.HEIGHT_RESOLUTION * 2, 0, RenderTextureFormat.R16, RenderTextureReadWrite.Linear, 1, RenderTextureMemoryless.None, VRTextureUsage.None, false);
            tempHeight.enableRandomWrite = true;
            tempHeight.Create();
            ReadHeight(targetPosition * 2, 0, mipLevel + 1, tempHeight);
            ReadHeight(targetPosition * 2 + int2(1, 0), int2(MTerrain.HEIGHT_RESOLUTION, 0), mipLevel + 1, tempHeight);
            ReadHeight(targetPosition * 2 + int2(0, 1), int2(0, MTerrain.HEIGHT_RESOLUTION), mipLevel + 1, tempHeight);
            ReadHeight(targetPosition * 2 + 1, MTerrain.HEIGHT_RESOLUTION, mipLevel + 1, tempHeight);
            RenderTexture mipHeight = RenderTexture.GetTemporary(MTerrain.HEIGHT_RESOLUTION, MTerrain.HEIGHT_RESOLUTION, 0, RenderTextureFormat.R16, RenderTextureReadWrite.Linear, 1, RenderTextureMemoryless.None, VRTextureUsage.None, false);
            mipHeight.enableRandomWrite = true;
            mipHeight.filterMode = FilterMode.Bilinear;
            mipHeight.Create();
            generatorShader.SetTexture(1, ShaderIDs._SourceTex, tempHeight);
            generatorShader.SetTexture(1, ShaderIDs._DestTex, mipHeight);
            generatorShader.Dispatch(1, MTerrain.HEIGHT_RESOLUTION / 8, MTerrain.HEIGHT_RESOLUTION / 8, 1);
            BlitHeight(targetPosition, mipLevel, mipHeight, 1, 0);
            RenderTexture.ReleaseTemporary(tempHeight);
            RenderTexture.ReleaseTemporary(mipHeight);
        }

        public void Dispose()
        {
            streamer.Dispose();
            positionOffset.Dispose();
            Resources.UnloadAsset(generatorShader);
        }
    }
}
#endif