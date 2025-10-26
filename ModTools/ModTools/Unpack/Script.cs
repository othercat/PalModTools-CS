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
using Records.Ts;
using SimpleUtility;
using System;
using System.Collections.Generic;
using System.IO;
using static Records.Pal.Core;
using EntityType = Records.Pal.Entity.Type;
using RAddress = Records.Mod.RGame.Address;

namespace ModTools.Unpack;

public static unsafe class Script
{
    /// <summary>
    /// 初始化脚本转换系统
    /// </summary>
    public static void Init()
    {
        string      pathOut, pathDependency, pathIn;

        //
        // 创建输出目录 Scirpt
        //
        pathOut = Config.ModWorkPath.Game.Data.Script;
        COS.Dir(pathOut);

        //
        // 复制脚本工作目录
        //
        pathDependency = "Dependency";
        pathIn = $@"{pathDependency}\Script";
        S.DirCopy(pathIn, "*", pathOut);
        S.DirCopy($@"{pathIn}\.vscode", "*", $@"{pathOut}\.vscode");
        S.DirCopy($@"{pathIn}\include", "*", $@"{pathOut}\include");

        //
        // 创建输出目录 Scirpt\src
        //
        COS.Dir($@"{pathOut}\src");

        //
        // 初始化脚本地址列表
        //
        Config.AddAddress(0, string.Empty);
    }

    /// <summary>
    /// 将 C# bool 转换为 typescript boolean
    /// </summary>
    /// <param name="value">待转换的布尔值</param>
    /// <returns>typescript boolean</returns>
    static string GetBoolean(bool value) => value ? "true" : "false";

    static List<string> _args { get; set; } = null!;

    /// <summary>
    /// 解档 Scirpt 条目
    /// </summary>
    public static void Process()
    {
        void AddAddr(CScriptArgs arg) => Config.AddAddress(arg.UShort);
        void Str(string text) => _args.Add(text);
        //void TupleNumNum((short, short) texts) => _args.AddRange([texts.Item1.ToString(), texts.Item2.ToString()]);
        //void TupleBoolNum((bool, int) texts) => _args.AddRange([GetBoolean(texts.Item1), texts.Item2.ToString()]);
        void Addr(CScriptArgs arg) => Str($"\"{arg.Addr}\"");
        void Num(CScriptArgs arg) => Str(arg.Short.ToString());
        void UNum(CScriptArgs arg) => Str(arg.UShort.ToString());
        void Bool(CScriptArgs arg) => Str(GetBoolean(arg.Bool));
        //void SceneEvent(CScriptArgs arg) => TupleNumNum(arg.SceneEvent);
        void Scene(CScriptArgs arg) => Str(arg.SceneEvent.Item1.ToString());
        void Event(CScriptArgs arg) => Str(arg.SceneEvent.Item2.ToString());
        //void EventTrigger(CScriptArgs arg) => TupleBoolNum(arg.EventTrigger);
        void TriggerMode(CScriptArgs arg) => Str(GetBoolean(arg.TriggerMode));
        void TriggerRange(CScriptArgs arg) => Str(arg.TriggerRange.ToString());
        void HeroEntity(CScriptArgs arg) => Str(arg.HeroEntity.ToString());
        void ItemEntity(CScriptArgs arg) => Str(arg.ItemEntity.ToString());
        void MagicEntity(CScriptArgs arg) => Str(arg.MagicEntity.ToString());
        void EnemyEntity(CScriptArgs arg) => Str(arg.EnemyEntity.ToString());
        void PoisonEntity(CScriptArgs arg) => Str(arg.PoisonEntity.ToString());

        string                  pathOut, name, typeName;
        FuncInformation[]       ts;
        StreamWriter[]          fileScenes;
        StreamWriter            file;
        string?                 scriptText;
        nint                    pNative;
        CScript*                pScript, pThis;
        int                     i, count, progress, end, j;
        CScriptArgs*            pArgs;
        bool                    needSelectFile, isValidScript;
        List<int>               enumTypeArgIds;

        //
        // 输出处理进度
        //
        Util.Log("Unpack the game data. <Scirpt>");

        //
        // 读取 Scirpt 数据
        //
        (pNative, count) = Config.MkfCore.ReadChunk(4);
        pScript = (CScript*)pNative;
        count /= sizeof(CScript);

        //
        // 创建场景脚本目录
        //
        pathOut = Config.ModWorkPath.Game.Data.Script;
        COS.Dir($@"{pathOut}\src\Scene");

        //
        // 创建阉割语法后的 typescript 文件
        //
        using StreamWriter filePublic = File.CreateText($@"{pathOut}\src\Public.ts");
        using StreamWriter fileHero = File.CreateText($@"{pathOut}\src\Hero.ts");
        using StreamWriter fileItem = File.CreateText($@"{pathOut}\src\Item.ts");
        using StreamWriter fileMagic = File.CreateText($@"{pathOut}\src\Magic.ts");
        using StreamWriter fileEnemy = File.CreateText($@"{pathOut}\src\Enemy.ts");
        using StreamWriter filePoison = File.CreateText($@"{pathOut}\src\Poison.ts");
        file = filePublic;
        fileScenes = new StreamWriter[Records.Pal.Base.MaxScenes];
        COS.Dir($@"{pathOut}\src\Scene");
        for (i = 1; i < fileScenes.Length; i++)
            fileScenes[i] = File.CreateText($@"{pathOut}\src\Scene\{i:D5}.ts");

        //
        // 处理 Scirpt
        //
        enumTypeArgIds = [];
        progress = (count / 10);
        end = count - 1;
        for (i = 1; i <= end; i++)
        {
            //
            // 输出处理进度
            //
            if (i % progress == 0 || i == end)
                Util.Log($"Unpack the game data. <Scirpt Addr: {((float)i / count * 100):f2}%>");

            //
            // 获取当前 Scirpt 条目
            //
            pThis = &pScript[i];
            pArgs = (CScriptArgs*)pThis->Args;
            name = Message.GetFuncName(pThis->Command);

            //
            // 注册所有脚本中出现的地址
            //
            if ((ts = Message.GetFuncData(name).Ts) != null)
                foreach (var information in ts)
                    if (information.Type == FuncInformation.SpecialType.Address)
                        AddAddr(pArgs[information.ArgId]);
        }
        needSelectFile = true;
        isValidScript = true;
        for (i = 1; i <= end; i++)
        {
            //
            // 输出处理进度
            //
            if (i % progress == 0 || i == end)
                Util.Log($"Unpack the game data. <Scirpt: {((float)i / count * 100):f2}%>");

            //
            // 获取当前 Scirpt 条目
            //
            pThis = &pScript[i];
            pArgs = (CScriptArgs*)pThis->Args;

            //
            // 检查脚本地址是否被注册
            //
            if (Config.GetAddress(i, out var address))
            {
                if (needSelectFile)
                {
                    needSelectFile = false;

                    file = address.Type switch
                    {
                        RAddress.AddrType.Public => filePublic,
                        RAddress.AddrType.Hero => fileHero,
                        RAddress.AddrType.Item => fileItem,
                        RAddress.AddrType.Magic => fileMagic,
                        RAddress.AddrType.Enemy => fileEnemy,
                        RAddress.AddrType.Poison => filePoison,
                        RAddress.AddrType.Scene => fileScenes[address.ObjectId],
                        _ => throw new NotImplementedException(),
                    };
                }

                //
                // 写入脚本入口
                //
                file.WriteLine($"['{Config.AddAddress(i)}'];");

                //
                // 将后续指令标记为有效脚本
                //
                isValidScript = true;
            }

            //
            // 处理当前脚本条目
            //
            enumTypeArgIds.Clear();
            scriptText = null;
            _args = [];
            name = Message.GetFuncName(pThis->Command);
            switch (pThis->Command)
            {
                case 0xFFFF: // 显示对话
                    scriptText = $@"//{pArgs[0].Dialog}";
                    break;

                case 0x0000: // 脚本块上下文结束
                    scriptText = "";
                    needSelectFile = true;
                    break;

                case 0x00A7: // 将对话框设置在 Item/Magic 描述的位置
                    //
                    // 将后续指令标记为无效脚本
                    //
                    isValidScript = false;
                    break;

                default: // 其他标准伪汇编指令码
                    {
                        FuncData            funcData;
                        TsCases             tsCases;
                        int                 argId, value;
                        string[]            argType;
                        AssemblyCases       asmCases;

                        //
                        // 检查该指令对应的是哪个函数
                        //
                        funcData = Message.GetFuncData(name);
                        argType = funcData.ArgType;
                        asmCases = funcData.AssemblyCases;
                        if ((tsCases = funcData.TsCases) != null)
                        {
                            //
                            // 获取决定最终函数名称的伪汇编参数编号
                            //
                            argId = tsCases.ArgId;

                            //
                            // 根据实体类型名称来决定函数名称
                            //
                            name = tsCases[Message.GetEntityType(pThis->Args[argId])];
                        }

                        //
                        // 检查该函数有没有自定义赋值参数
                        //
                        if ((ts = funcData.Ts) != null)
                        {
                            foreach (var info in ts)
                            {
                                //
                                // 获取需要使用第几个伪汇编参数
                                //
                                argId = info.ArgId;

                                switch (info.Type)
                                {
                                    case FuncInformation.SpecialType.Address:
                                        Addr(pArgs[argId]);
                                        break;

                                    case FuncInformation.SpecialType.Scene:
                                        Scene(pArgs[argId]);
                                        break;

                                    case FuncInformation.SpecialType.Event:
                                        Event(pArgs[argId]);
                                        break;

                                    case FuncInformation.SpecialType.TriggerMode:
                                        TriggerMode(pArgs[argId]);
                                        break;

                                    case FuncInformation.SpecialType.TriggerRange:
                                        TriggerRange(pArgs[argId]);
                                        break;

                                    case FuncInformation.SpecialType.Entity:
                                        //
                                        // 实体对象类型
                                        //
                                        j = pThis->Args[argId];
                                        switch (Message.GetEntityType(j))
                                        {
                                            case EntityType.Hero:
                                                HeroEntity(pArgs[argId]);
                                                break;

                                            case EntityType.Item:
                                                ItemEntity(pArgs[argId]);
                                                break;

                                            case EntityType.Magic:
                                                MagicEntity(pArgs[argId]);
                                                break;

                                            case EntityType.Enemy:
                                                EnemyEntity(pArgs[argId]);
                                                break;

                                            case EntityType.Poison:
                                                PoisonEntity(pArgs[argId]);
                                                break;

                                            case EntityType.System:
                                            default:
                                                //
                                                // 使用了没有脚本参数的实体，直接崩溃
                                                //
                                                S.Failed(
                                                    "Script.Process",
                                                    $"The entity numbered '{j}' does not contain a script."
                                                );
                                                break;
                                        }
                                        break;

                                    default:
                                        //
                                        // 其他基础类型、自定义数值等类型
                                        //
                                        funcData = Message.GetFuncData(name);
                                        typeName = funcData.ArgType[argId];
                                        switch (typeName)
                                        {
                                            case "boolean":
                                                Bool(pArgs[argId]);
                                                break;

                                            default:
                                                if (Message.TryGetTypeSign(typeName, out var isSigned))
                                                    if (isSigned)
                                                        Num(pArgs[argId]);
                                                    else
                                                        UNum(pArgs[argId]);
                                                else
                                                {
                                                    enumTypeArgIds.Add(argId);
                                                    argId = pArgs[argId].Short;

                                                    //
                                                    // 枚举类型
                                                    //
                                                    S.Failed(
                                                        "Script.Process",
                                                        $"The enumeration type '{typeName}' does not have an entry with the value '{argId}'",
                                                        Message.TryGetEnumEntry(typeName, argId, out var entryName)
                                                    );

                                                    //
                                                    // 组合枚举引用
                                                    //
                                                    Str($"{typeName}.{entryName}");
                                                }
                                                break;
                                        }
                                        break;
                                }
                            }
                        }
                        else if (argType != null)
                        {
                            //
                            // 直接按原生函数声明转换参数
                            //
                            funcData = Message.GetFuncData(name);
                            for (argId = 0; argId < argType.Length; argId++)
                            {
                                typeName = funcData.ArgType[argId];
                                switch (typeName)
                                {
                                    case "boolean":
                                        Bool(pArgs[argId]);
                                        break;

                                    default:
                                        if (Message.TryGetTypeSign(typeName, out var isSigned))
                                            if (isSigned)
                                                Num(pArgs[argId]);
                                            else
                                                UNum(pArgs[argId]);
                                        else
                                        {
                                            enumTypeArgIds.Add(argId);
                                            value = pArgs[argId].Short;

                                            //
                                            // 枚举类型
                                            //
                                            S.Failed(
                                                "Script.Process",
                                                $"The enumeration type '{typeName}' does not have an entry with the value '{value}'",
                                                Message.TryGetEnumEntry(typeName, value, out var entryName)
                                            );

                                            //
                                            // 组合枚举引用
                                            //
                                            Str($"{typeName}.{entryName}");
                                        }
                                        break;
                                }
                            }
                        }

                        //
                        // 根据对应的伪汇编指令来分配指定的参数
                        //
                        if (asmCases != null)
                            foreach (var asmCase in asmCases.Reverses)
                                if (asmCase.Key == pThis->Command && asmCase.Value != AssemblyCases.OtherCode)
                                {
                                    _args[asmCases.ArgId] = asmCase.Value.ToString();

                                    if (enumTypeArgIds.Contains(asmCases.ArgId))
                                    {
                                        typeName = funcData.ArgType[asmCases.ArgId];
                                        value = asmCase.Value;

                                        //
                                        // 枚举类型
                                        //
                                        S.Failed(
                                            "Script.Process",
                                            $"The enumeration type '{typeName}' does not have an entry with the value '{value}'",
                                            Message.TryGetEnumEntry(typeName, value, out var entryName)
                                        );


                                        //
                                        // 组合枚举引用
                                        //
                                        _args[asmCases.ArgId] = $"{typeName}.{entryName}";
                                    }
                                    break;
                                }
                    }
                    break;
            }

            //
            // 跳过后续的无效脚本
            //
            if (!isValidScript)
                continue;

            //
            // 拼合脚本条目
            //
            scriptText ??= $"{name}({string.Join(", ", _args)});";

            //
            // 写入脚本条目
            //
            file.WriteLine(scriptText);
        }

        //
        // 关闭所有场景脚本文件
        //
        foreach (var fileScene in fileScenes)
            fileScene?.Dispose();

        //
        // 释放非托管内存
        //
        C.free(pNative);
    }
}
