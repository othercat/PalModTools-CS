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
 */
#endregion License

using Lib.Mod;
using System.Runtime.InteropServices;
using static Records.Pal.Entity;

namespace Records.Pal;

public static unsafe class Core
{
    public enum EventState : ushort
    {
        Hidden          = 0,        // 隐藏
        NonObstacle     = 1,        // 漂浮
        Obstacle        = 2,        // 障碍（阻碍领队通过）
    }

    public enum EventTriggerMode : ushort
    {
        None                = 0,        // 无法触发
        SearchNear          = 1,        // 手动触发，范围 1（须脸贴或重合，大部分道具的获取方式）
        SearchNormal        = 2,        // 手动触发，范围 3
        SearchFar           = 3,        // 手动触发，范围 5
        TouchNear           = 4,        // 自动触发，范围 0（须重合，如将军冢石板机关）
        TouchNormal         = 5,        // 自动触发，范围 1
        TouchFar            = 6,        // 自动触发，范围 2
        TouchFarther        = 7,        // 自动触发，范围 3
        TouchFarthest       = 8,        // 自动触发，范围 4
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CEvent
    {
        public  short                   VanishTime;             // 正数为剩余隐匿帧数，负数为逃跑后僵直帧数（一般为战斗事件）
        public  short                   X;                      // X 坐标
        public  short                   Y;                      // Y 坐标
        public  short                   Layer;                  // 图层
        public  ushort                  TriggerScript;          // 触发脚本
        public  ushort                  AutoScript;             // 自动脚本
        public  ushort                  State;                  // 触发状态
        public  EventTriggerMode        TriggerMode;            // 触发模式
        public  ushort                  SpriteId;               // 形象
        public  ushort                  SpriteFrames;           // 形象每个方向的帧数
        public  ushort                  Direction;              // 当前面朝方向
        public  ushort                  CurrentFrameId;         // 当前帧数（当前方向上的）
        public  ushort                  TriggerIdleFrame;       // 触发脚本累计被触发次数
        readonly    ushort              _unknown;               // 未知数据
        readonly    ushort              _spriteFramesAuto;      // 形象总帧数（自动计算，只在内存中有意义）
        public  ushort                  AutoIdleFrame;          // 自动脚本累计被触发次数
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CScene
    {
        public  ushort      MapId;                  // 实际地图
        public  ushort      ScriptOnEnter;          // 脚本：进入场景
        public  ushort      ScriptOnTeleport;       // 脚本：脱离场景（引路蜂、土灵珠）
        public  ushort      EventObjectIndex;       // 事件起始索引，实际索引为（EventObjectIndex + 1）
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CScript
    {
        public  ushort              Command;
        public  fixed   ushort      Args[3];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CScriptArgs
    {
        readonly    ushort      Value;

        public readonly short Short => (short)Value;
        public readonly ushort UShort => Value;
        public readonly bool Bool => UShort != 0;
        public readonly (short, short) SceneEvent => Config.GetSoftSceneEventId(Short);
        public readonly string Addr => Config.AddAddress(UShort);
        public readonly ushort HeroEntity => Config.GetSoftEntityId(Type.Hero, UShort);
        public readonly ushort ItemEntity => Config.GetSoftEntityId(Type.Item, UShort);
        public readonly short MagicEntity => Config.GetSoftMagicId(Short);
        //{
        //    get
        //    {
        //        (Short > 0) ? (short)((UShort == 0x0018) ? 1 : (short)(UShort - BeginId.Magic + 2)) : Short;
        //    };
        //}
        public readonly short EnemyEntity => (short)Config.GetSoftEntityId(Type.Enemy, UShort);
        public readonly ushort PoisonEntity => Config.GetSoftEntityId(Type.Poison, UShort);
        public readonly string Dialog => Message.GetDialogue(UShort);
        public readonly bool TriggerMode => UShort >= (ushort)EventTriggerMode.TouchNear;
        public readonly ushort TriggerRange => (ushort)(TriggerMode ? (UShort - (ushort)EventTriggerMode.TouchNear) : UShort);
        public readonly (bool, ushort) EventTrigger => (TriggerMode, TriggerRange);
    }
}
