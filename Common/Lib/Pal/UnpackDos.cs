#region License
/*
 * Copyright (c) 2025, liuzhier <lichunxiao_lcx@qq.com>.
 * 
 * This file is part of SDLPAL-CS.
 * 
 * SDLPAL-CS is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License, version 3
 * as published by the Free Software Foundation.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 * 
 * Portions based on PalLibrary by Lou Yihua <louyihua@21cn.com>.
 * Copyright (c) 2006-2007, Lou Yihua.
 * 
 * Ported to C from C++ and modified for compatibility with Big-Endian
 * by Wei Mingzhi <whistler_wmz@users.sf.net>.
 * 
 * Ported to C# from C by Li Chunxiao <lichunxiao_lcx@qq.com>.
 * 
 */
#endregion License

using SimpleUtility;
using System;
using System.Runtime.InteropServices;

namespace Lib.Pal;

public static unsafe partial class PalUtil
{
    struct YJ1_TreeNode
    {
        public  byte        value;
        public  bool        leaf;
        public  YJ1_TreeNode        *parent = null;
        public  YJ1_TreeNode        *left   = null;
        public  YJ1_TreeNode        *right  = null;

        public YJ1_TreeNode() { }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct YJ_1_FILEHEADER
    {
        public  uint        Signature;              // 'YJ_1'
        public  int         UncompressedLength;     // size before compression
        public  int         CompressedLength;       // size after compression
        public  ushort      BlockCount;             // number of blocks
        public  byte        Unknown;
        public  byte        HuffmanTreeLength;      // length of huffman tree
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct YJ_1_BLOCKHEADER
    {
        public  ushort      UncompressedLength;     // maximum 0x4000
        public  ushort      CompressedLength;       // including the header
        public  fixed   ushort      LZSSRepeatTable[4];
        public  fixed   byte        LZSSOffsetCodeLengthTable[4];
        public  fixed   byte        LZSSRepeatCodeLengthTable[3];
        public  fixed   byte        CodeCountCodeLengthTable[3];
        public  fixed   byte        CodeCountTable[2];
    }

    static int Yj1_get_bits(void *src, int *bitptr, int count, int sourceLength = -1)
    {
        if (count is < 0 or > 16) throw new FormatException("Invalid YJ1 bit width.");
        if (count == 0) return 0;
        int offset = (*bitptr >> 4) << 1;
        int required = count > 16 - (*bitptr & 15) ? 4 : 2;
        if (sourceLength >= 0 && (offset < 0 || offset > sourceLength - required))
            throw new FormatException("Truncated YJ1 bit stream.");
        byte        *temp = ((byte*)src) + ((* bitptr >> 4) << 1);
        int         mask;
        byte        bptr = (byte)(*bitptr & 0xf);
        *bitptr += count;
        if (count > 16 - bptr)
        {
            count = count + bptr - 16;
            mask = 0xffff >> bptr;
            return (ushort)((((temp[0] | (temp[1] << 8)) & mask) << count) | ((temp[2] | (temp[3] << 8)) >> (16 - count)));
        }
        else
            return (ushort)(((ushort)((temp[0] | (temp[1] << 8)) << bptr)) >> (16 - count));
    }

    static ushort Yj1_get_loop(void* src, int* bitptr, YJ_1_BLOCKHEADER* header, int sourceLength = -1)
    {
        if (Yj1_get_bits(src, bitptr, 1, sourceLength) != 0)
            return header->CodeCountTable[0];
        else
        {
            int temp = Yj1_get_bits(src, bitptr, 2, sourceLength);
            if (temp != 0)
                return (ushort)Yj1_get_bits(src, bitptr, header->CodeCountCodeLengthTable[temp - 1], sourceLength);
            else
                return header->CodeCountTable[1];
        }
    }

    static ushort Yj1_get_count(void* src, int* bitptr, YJ_1_BLOCKHEADER* header, int sourceLength = -1)
    {
        ushort temp;
        if ((temp = (ushort)Yj1_get_bits(src, bitptr, 2, sourceLength)) != 0)
        {
            if (Yj1_get_bits(src, bitptr, 1, sourceLength) != 0)
                return (ushort)Yj1_get_bits(src, bitptr, header->LZSSRepeatCodeLengthTable[temp - 1], sourceLength);
            else
                return header->LZSSRepeatTable[temp];
        }
        else
            return header->LZSSRepeatTable[0];
    }

    /// <summary>
    /// 对二进制流进行解码
    /// </summary>
    /// <param name="source">源二进制流</param>
    /// <returns>解码后的二进制流和流长度</returns>
    static (nint, int) UnpackDos(nint source) => UnpackDos(source, -1, int.MaxValue);

    // The legacy pointer-only entry cannot prove input bounds. File importers
    // must supply the chunk length and their output allocation budget.
    static (nint, int) UnpackDos(nint source, int sourceLength, int outputLimit)
    {
        if (source == 0 || sourceLength < -1 || (sourceLength >= 0 && sourceLength < 16))
            throw new FormatException("Truncated YJ1 file header.");
        var hdr = (YJ_1_FILEHEADER*)source;
        if (hdr->Signature != 0x315f4a59 || hdr->UncompressedLength < 1 || hdr->UncompressedLength > outputLimit)
            throw new FormatException("Invalid YJ1 signature or output size.");
        if (sourceLength >= 0)
        {
            if (hdr->CompressedLength < 16 || hdr->CompressedLength > sourceLength)
                throw new FormatException("Invalid YJ1 compressed length.");
            sourceLength = hdr->CompressedLength;
        }
        var src = (byte*)source;
        int treeLength = hdr->HuffmanTreeLength * 2;
        int flagLength = ((treeLength + 15) >> 4) * 2;
        int treeEnd = 16 + treeLength + flagLength;
        if (sourceLength >= 0 && treeEnd > sourceLength)
            throw new FormatException("Truncated YJ1 Huffman table.");
        YJ1_TreeNode* root = null;
        nint destination = 0;
        bool completed = false;
        try
        {
            root = (YJ1_TreeNode*)C.malloc(sizeof(YJ1_TreeNode) * (treeLength + 1));
            root[0].left = treeLength >= 2 ? root + 1 : null;
            root[0].right = treeLength >= 2 ? root + 2 : null;
            int bitptr = 0;
            byte* flag = src + 16 + treeLength;
            for (int i = 1; i <= treeLength; i++)
            {
                root[i].leaf = Yj1_get_bits(flag, &bitptr, 1, flagLength) == 0;
                root[i].value = src[15 + i];
                if (!root[i].leaf)
                {
                    int child = (root[i].value << 1) + 1;
                    if (child + 1 > treeLength) throw new FormatException("YJ1 Huffman child is outside the table.");
                    root[i].left = root + child;
                    root[i].right = root[i].left + 1;
                }
            }
            src += treeEnd;
            destination = C.malloc(hdr->UncompressedLength);
            var dest = (byte*)destination;
            for (int i = 0; i < hdr->BlockCount; i++)
            {
                long offset = src - (byte*)source;
                if (sourceLength >= 0 && offset > sourceLength - 4)
                    throw new FormatException("Truncated YJ1 block header.");
                var header = (YJ_1_BLOCKHEADER*)src;
                int blockOutput = header->UncompressedLength;
                int blockBytes = header->CompressedLength == 0 ? blockOutput + 4 : header->CompressedLength;
                if (sourceLength >= 0 && blockBytes > sourceLength - offset)
                    throw new FormatException("Truncated YJ1 block.");
                if (blockOutput > hdr->UncompressedLength - (dest - (byte*)destination))
                    throw new FormatException("YJ1 block exceeds the output size.");
                var blockStart = dest;
                src += 4;
                if (header->CompressedLength == 0)
                {
                    for (int j = 0; j < blockOutput; j++) *dest++ = *src++;
                    continue;
                }
                if (blockBytes < 24 || treeLength < 2)
                    throw new FormatException("Invalid YJ1 compressed block or Huffman table.");
                src += 20;
                int bitBytes = blockBytes - 24;
                bitptr = 0;
                for (;;)
                {
                    int loop = Yj1_get_loop(src, &bitptr, header, bitBytes);
                    if (loop == 0) break;
                    if (loop > blockOutput - (dest - blockStart))
                        throw new FormatException("YJ1 literal run exceeds its block.");
                    while (loop-- > 0)
                    {
                        var node = root;
                        int depth = 0;
                        while (!node->leaf)
                        {
                            if (++depth > treeLength) throw new FormatException("Cyclic YJ1 Huffman table.");
                            node = Yj1_get_bits(src, &bitptr, 1, bitBytes) != 0 ? node->right : node->left;
                        }
                        *dest++ = node->value;
                    }
                    loop = Yj1_get_loop(src, &bitptr, header, bitBytes);
                    if (loop == 0) break;
                    while (loop-- > 0)
                    {
                        int count = Yj1_get_count(src, &bitptr, header, bitBytes);
                        int selector = Yj1_get_bits(src, &bitptr, 2, bitBytes);
                        int pos = Yj1_get_bits(src, &bitptr, header->LZSSOffsetCodeLengthTable[selector], bitBytes);
                        if (count > blockOutput - (dest - blockStart) || (count > 0 && (pos < 1 || pos > dest - (byte*)destination)))
                            throw new FormatException("Invalid YJ1 back-reference.");
                        // Overlapping copies may also refer to an earlier block.
                        while (count-- > 0) { *dest = *(dest - pos); dest++; }
                    }
                }
                if (dest - blockStart != blockOutput) throw new FormatException("YJ1 block output is incomplete.");
                src = (byte*)header + blockBytes;
            }
            int written = checked((int)(dest - (byte*)destination));
            if (written != hdr->UncompressedLength) throw new FormatException("YJ1 file output is incomplete.");
            completed = true;
            return (destination, written);
        }
        finally { C.free(root); if (!completed) C.free(destination); }
    }
}
