using Lib.Mod;
using Lib.Pal;
using Records.Mod.RGame;
using Records.Pal;
using Records.Ts;
using SimpleUtility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using static Records.Pal.Core;
using static Records.Pal.Entity;
using static Vanara.PInvoke.User32;
using EntityType = Records.Pal.Entity.Type;

namespace ModTools.Compile;

public unsafe static class Script
{
    static ushort Address { get; set; }
    static List<FunctionEntry> FunctionEntries { get; set; } = [];

    public static void Save()
    {
        using FileWriter coreFile = new(Config.PalWorkPath.DataBase.Core);
        coreFile.Write(new Span<byte>(Message.PalFile.Core.Script, Message.PalFile.Core.ScriptCount));
    }

    /// <summary>
    /// 读取阉割语法后的 typescript
    /// </summary>
    static void ProcessTypeScript(string filePath)
    {
        int                 lineId, charBegin, charEnd;
        FastChars           line, arguments, arg;
        List<string>        args;
        string              addressTag, functionName, typeName, entryName;

        //
        // 打开 typescript 文件
        //
        using StreamReader reader = new(filePath, Encoding.UTF8);

        for (lineId = 1; ; lineId++)
        {
            //
            // 读取数据块
            //
            line = reader.ReadLine().AsSpan().Trim();

            //
            // 逐行读取缓冲区中的内容
            // 按情况出现频率进行先后判断
            //
            functionName = default!;
            args = [];
            if (line.Contains('(') && line.EndsWith(");")
               && line.IndexOf('(') < line.IndexOf(')'))
            {
                //
                // 情况 1：函数调用
                // 匹配 xxxx(xxxx, xxxx, xxxx); 格式
                //

                //
                // 提取函数名
                //
                charEnd = line.IndexOf('(');
                functionName = line[..charEnd].Trim().ToString();

                //
                // 提取参数
                //
                charBegin = line.IndexOf('(') + 1;
                charEnd = line.IndexOf(')');
                arguments = line[charBegin..charEnd];
                foreach (Range range in arguments.Split(','))
                {
                    //
                    // 分割出每个参数
                    //
                    arg = arguments[range].Trim();

                    if (arg.StartsWith('"') && arg.StartsWith('"'))
                    {
                        //
                        // 地址类型，直接去掉引号
                        //
                        arg = arg[1..^1];
                    }
                    else if (arg.Contains('.'))
                    {
                        //
                        // 枚举类型，转换为数值
                        //
                        charEnd = arg.IndexOf('.');
                        typeName = arg[..charEnd].ToString();
                        charBegin = charEnd + 1;
                        entryName = arg[charBegin..].ToString();

                        //
                        // 枚举类型
                        //
                        S.Failed(
                            "Script.Process",
                            $"There is no entry named '{typeName} in the enumeration type '{entryName}'",
                            Message.TryGetEnumValue(typeName, entryName, out var value)
                        );

                        //
                        // 解引用，获取对应的枚举值
                        //
                        args.Add($"{value}");

                        continue;
                    }

                    args.Add(arg.ToString());
                }
            }
            else if (line.StartsWith("//"))
            {
                //
                // 情况 2：对话文本
                // 匹配 //xxxx 格式
                //

                //
                // 将 //xxxx 的 xxxx 提取为对话，并放入对话列表
                //
                Message.AddDialogue(line[2..].ToString());

                //
                // 空行代表脚本上下文结束
                //
                functionName = Message.GetFuncName(0xFFFF);
                args.Add(((ushort)(Message.DialogueCount - 1)).ToString());
            }
            else if (line.StartsWith("['") && line.EndsWith("'];"))
            {
                //
                // 情况 3：地址标签
                // 匹配 ['xxxx'] 格式
                //

                //
                // 将 ['xxxx'] 内的 xxxx 提取为脚本标签
                //
                charBegin = line.IndexOf("['") + 2;
                charEnd = line.IndexOf("']");
                addressTag = line[charBegin..charEnd].Trim().ToString();

                //
                // 将脚本标签 xxxx 作为键，新分配的地址作为值
                //
                Config.AddNewAddress(addressTag, Address);

                //
                // 跳过地址分配
                //
                continue;
            }
            else if (line == "")
            {
                //
                // 情况 4：空行
                // 代表脚本上下文结束
                //

                //
                // 记录为脚本块结束标志
                //
                functionName = Message.GetFuncName(0x0000);
            }
            else if (line.IsEmpty)
                //
                // 情况 4：内容为 null
                // 文件读取完毕，结束读取
                //
                break;
            else
                //
                // 情况 5：内容为 null
                // 格式不匹配，报错退出
                //
                S.Failed(
                    "Script.ProcessTypeScript",
                    $"There is a syntax error in the script!\n{filePath}:\nLine {lineId}: '{line}'"
                );

            //
            // 将函数条目放入列表
            //
            FunctionEntries.Add(new(
                Name: functionName,
                Args: [.. args]
            ));

            //
            // 分配下一条地址
            //
            S.Failed(
                "Script.ProcessTypeScript",
                $"The number of lines of the script exceeds the maximum limit!\n{filePath}:\nLine {lineId}: '{line}'",
                Address++ < 0xFFFF
            );
        }
    }

    /// <summary>
    /// 编译 Base Data 和 Core Data
    /// </summary>
    public static void Process()
    {
        string      rootPath, scenePath, mainPath;
        int         i, endId;

        //
        // 获取脚本根目录
        //
        rootPath = Config.ModWorkPath.Game.Data.Script;
        mainPath = $@"{rootPath}\src";
        scenePath = $@"{mainPath}\Scene";

        //
        // 初始化默认空地址
        //
        Address = 1;
        Config.AddNewAddress("", 0);
        Config.AddNewEventId(0xFFFF, 0xFFFF, 0xFFFF);
        Config.AddNewEventId(0, 0, 0);

        //
        // 处理 Scene.ts
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Process Scene.ts/Event.ts.");

            const   int     beginId = 1;

            //
            // 检查有多少项 Scene.ts
            //
            endId = ModUtil.GetFileSequenceCount(scenePath, beginId, fileSuffix: ".ts");

            //
            // 输出 Scene.ts 计数
            //
            Util.Log($"Find {endId} scene typescript files. There are actually {endId = Math.Min(endId, Base.MaxEffectiveScenes)} valid ones");

            for (i = beginId; i <= endId; i++)
                //
                // 处理脚本文件
                //
                ProcessTypeScript($@"{scenePath}\{i:D5}.ts");
        }

        //
        // 处理 Public.ts
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Process Public.ts.");

            //
            // 处理脚本文件
            //
            ProcessTypeScript($@"{mainPath}\Public.ts");
        }

        //
        // 处理 Enemy.ts
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Process Enemy.ts.");

            //
            // 处理脚本文件
            //
            ProcessTypeScript($@"{mainPath}\Enemy.ts");
        }

        //
        // 处理 Hero.ts
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Process Hero.ts.");

            //
            // 处理脚本文件
            //
            ProcessTypeScript($@"{mainPath}\Hero.ts");
        }

        //
        // 处理 Poison.ts
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Process Poison.ts.");

            //
            // 处理脚本文件
            //
            ProcessTypeScript($@"{mainPath}\Poison.ts");
        }

        //
        // 处理 Item.ts
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Process Item.ts.");

            //
            // 处理脚本文件
            //
            ProcessTypeScript($@"{mainPath}\Item.ts");
        }

        //
        // 处理 Magic.ts
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Process Magic.ts.");

            //
            // 处理脚本文件
            //
            ProcessTypeScript($@"{mainPath}\Magic.ts");
        }
    }

    static ushort NewDescScriptAddress { get; set; } = 0;

    public static ushort AddDescriptionScript(string[]? descriptions)
    {
        ushort      address;

        if (descriptions is null)
            //
            // 无描述，返回空地址
            //
            return 0;

        //
        // 获取脚本地址
        //
        address = (ushort)(FunctionEntries.Count + 1);

        //
        // 添加设置对话框位置指令
        //
        FunctionEntries.Add(new(
            Name: Message.GetFuncName(0x00A7),
            Args: [$"{Message.DialogueCount - 1}"]
        ));

        foreach (string description in descriptions)
        {
            //
            // 将对话放入对话列表
            //
            Message.AddDialogue(description);

            //
            // 添加显示对话指令
            //
            FunctionEntries.Add(new(
                Name: Message.GetFuncName(0xFFFF),
                Args: [$"{Message.DialogueCount - 1}"]
            ));
        }

        //
        // 添加脚本上下文结束标志
        //
        FunctionEntries.Add(new(
            Name: Message.GetFuncName(0x0000),
            Args: []
        ));

        return address;
    }

    static Core.CScript* CScript { get; set; } = null;
    static FunctionEntry Entry { get; set; } = null!;
    static string Name => Entry.Name;
    static string[] Args => Entry.Args;
    static int ArgCount => Args.Length;
    static int ArgId { get; set; }

    public static void Compile()
    {
        int GetValue(string textValue) => S.StrToInt32(textValue);
        ushort GetBoolean(string textValue) => (ushort)((textValue == "true") ? 1 : 0);
        int GetArgVal(int argId) => GetValue(Args[argId]);
        ushort GetArgBool(int argId) => GetBoolean(Args[argId]);
        ushort GetAddress(int argId) => Config.GetNewAddress(Args[argId]);
        void Command(int command = -1) => CScript->Command = (ushort)((command == -1) ? Message.GetAssemblyCommand(Name) : command);
        void Value(int value) => CScript->Args[ArgId++] = (ushort)value;
        void Val(int argId) => Value(GetArgVal(argId));
        void Bool(int argId) => Value(GetArgBool(argId));
        void Addr(int argId) => Value(GetAddress(argId));
        void SceneEvent(int sceneArgId, int eventArgId) => Value(Config.GetNewEventId(GetValue(Args[sceneArgId]), GetValue(Args[eventArgId])));
        void EventTrigger(int triggerModeArgId, int triggerRangeArgId) => Value(GetArgVal(triggerRangeArgId) + (int)(S.StrToBool(Args[triggerModeArgId]) ? EventTriggerMode.TouchNear : EventTriggerMode.None));

        int                     i, count, argId, progress, end;
        FuncData                funcData;
        AssemblyCases           asmCase;
        FuncInformation[]       asm;
        int[]                   argIds;
        string                  pathName, typeName;

        //
        // 申请内存空间（不要忘记带上特殊的空地址 0x0000！）
        //
        Message.PalFile.Core.ScriptCount = count = FunctionEntries.Count + 1;
        Message.PalFile.Core.Script = (Core.CScript*)C.malloc(sizeof(Core.CScript) * count);

        pathName = $@"{Config.LogOutPath}\Script.txt";
        File.Delete(pathName);
        File.AppendAllText(pathName, $"@{0:X4}: {0:X4} {0:X4} {0:X4} {0:X4} : 0 0 0\n");

        //
        // 将“助记符（Ts 脚本）”转换为“伪定长汇编码（ASM）”
        //
        progress = (count / 10);
        end = count - 1;
        for (i = 1; i <= end; i++)
        {
            //
            // 输出处理进度
            //
            if (i % progress == 0 || i == end)
                Util.Log($"Compiling the game data. <Scirpt Addr: {((float)i / count * 100):f2}%>");

            //
            // 获取当前条目
            //
            Entry = FunctionEntries[i - 1];
            CScript = &Message.PalFile.Core.Script[i];
            ArgId = 0;

            //
            // 编译为默认 ASM 指令
            //
            Command();

            //
            // 检查函数与 ASM 指令关系是否为一对多
            //
            funcData = Message.GetFuncData(Name);
            asmCase = funcData.AssemblyCases;
            if (asmCase != null)
            {
                //
                // 获取决定二者关系的 Ts 参数编号
                //
                argId = asmCase.ArgId;

                if (asmCase.Forwards.TryGetValue(GetArgVal(argId), out var command))
                    //
                    // 根据参数值找到了对应的 ASM 指令
                    // 匹配以下格式:
                    // "XXXX=0xXXXX"
                    //
                    Command(command);
                else
                    //
                    // 
                    // 匹配以下格式:
                    // "other=0xXXXX"
                    //
                    Command(asmCase.Forwards[AssemblyCases.OtherCode]);
            }

            //
            // 检查该函数有没有自定义赋值参数
            //
            if ((asm = funcData.Assembly) != null)
            {
                foreach (var info in asm)
                {
                    //
                    // 获取需要使用第几个伪汇编参数
                    //
                    argId = info.ArgId;
                    argIds = info.ArgIds;

                    switch (info.Type)
                    {
                        case FuncInformation.SpecialType.Address:
                            Addr(argId);
                            break;

                        case FuncInformation.SpecialType.SceneEvent:
                            SceneEvent(argIds[0], argIds[1]);
                            break;

                        case FuncInformation.SpecialType.EventTrigger:
                            EventTrigger(argIds[0], argIds[1]);
                            break;

                        case FuncInformation.SpecialType.HeroEntity:
                            Config.GetNewEntityId(EntityType.Hero, GetArgVal(argId));
                            break;

                        case FuncInformation.SpecialType.ItemEntity:
                            Config.GetNewEntityId(EntityType.Item, GetArgVal(argId));
                            break;

                        case FuncInformation.SpecialType.MagicEntity:
                            Config.GetNewEntityId(EntityType.Magic, GetArgVal(argId));
                            break;

                        case FuncInformation.SpecialType.EnemyEntity:
                            Config.GetNewEntityId(EntityType.Enemy, GetArgVal(argId));
                            break;

                        case FuncInformation.SpecialType.PoisonEntity:
                            Config.GetNewEntityId(EntityType.Poison, GetArgVal(argId));
                            break;

                        default:
                            //
                            // 其他基础类型、自定义数值等类型
                            //
                            typeName = funcData.ArgType[argId];
                            switch (typeName)
                            {
                                case "boolean":
                                    Bool(argId);
                                    break;

                                default:
                                    Val(argId);
                                    break;
                            }
                            break;
                    }
                }
            }
            else
                for (argId = 0; (funcData.ArgType != null) && (argId < funcData.ArgType.Length); argId++)
                {
                    //
                    // 其他基础类型、自定义数值等类型
                    //
                    typeName = funcData.ArgType[argId];
                    switch (typeName)
                    {
                        case "boolean":
                            Bool(argId);
                            break;

                        default:
                            Val(argId);
                            break;
                    }
                }

            File.AppendAllText(pathName, $"@{i:X4}: {CScript->Command:X4} {CScript->Args[0]:X4} {CScript->Args[1]:X4} {CScript->Args[2]:X4} : {(CScript->Command == 0xFFFF ? $"显示对话   >{Message.GetDialogue(CScript->Args[0])}" : $"{(short)CScript->Args[0]} {(short)CScript->Args[1]} {(short)CScript->Args[2]}")}\n");
        }
    }
}
