using Lib.Mod;
using Records.Mod.RGame;
using Records.Pal;
using SimpleUtility;
using System.IO;
using static Records.Pal.Base;
using static Records.Pal.Core;
using RWorkPathData = Records.Mod.WorkPathData;

namespace ModTools.Compile;

public static unsafe class Data
{
    /// <summary>
    /// 编译 Base Data 和 Core Data
    /// </summary>
    public static void Process()
    {
        const   int         beginId = 1;
        string              dirName, pathName, logPath;
        string[]            systemEntitynames;
        int                 endId, endId2, count, count2, i, j, entityId, entityCount, debugIndex;
        bool                isDosGame;
        RWorkPathData       dataWorkPath;
        short*              p16;

        isDosGame = false;
        dataWorkPath = Config.ModWorkPath.Game.Data;

        //
        // 初始化 Entity
        //
        {
            //
            // 记录 System Entity 个数，固定 61 项
            //
            Config.AddNewEntityBeginId(Entity.Type.System, 0);
            count = 61;

            //
            // 检查有多少项 Poison Entity
            //
            dirName = dataWorkPath.Entity.Poison;
            Config.AddNewEntityBeginId(Entity.Type.Poison, count);
            count += ModUtil.GetFileSequenceCount(dirName, beginId);

            //
            // 检查有多少项 Item Entity
            //
            dirName = dataWorkPath.Entity.Item;
            Config.AddNewEntityBeginId(Entity.Type.Item, count);
            count += ModUtil.GetFileSequenceCount(dirName, beginId);

            //
            // 检查有多少项 Magic Entity
            //
            dirName = dataWorkPath.Entity.Magic;
            Config.AddNewEntityBeginId(Entity.Type.Magic, count);
            count += ModUtil.GetFileSequenceCount(dirName, beginId);

            //
            // 检查有多少项 SummonGold Entity
            //
            dirName = dataWorkPath.Entity.SummonGold;
            Config.AddNewEntityBeginId(Entity.Type.SummonGold, count);
            count += ModUtil.GetFileSequenceCount(dirName, beginId);

            //
            // 检查有多少项 Enemy Entity
            //
            dirName = dataWorkPath.Entity.Enemy;
            Config.AddNewEntityBeginId(Entity.Type.Enemy, count);
            count += ModUtil.GetFileSequenceCount(dirName, beginId);

            //
            // 记录 Hero Entity 个数，固定 6 项（将 Hero Entity 从 System Entity 分离出来）
            //
            Config.AddNewEntityBeginId(Entity.Type.Hero, count);
            entityCount = count += Base.MaxHero;

            //
            // 输出处理进度
            //
            Util.Log($"The number of statistical entities: {entityCount}.");

            //
            // 根据游戏版本初始化 Entity
            //
            if (isDosGame)
                Message.PalFile.Core.EntityDos = (Entity.Dos*)C.malloc(sizeof(Entity.Dos) * entityCount);
            else
                Message.PalFile.Core.EntityWin = (Entity.Win*)C.malloc(sizeof(Entity.Win) * entityCount);
        }

        //
        // 读取系统 Entity
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Compiling System Entity.");

            //
            // 读取 json 数据
            //
            pathName = $"{dataWorkPath.Entity.System}.json";
            S.JsonLoad(out systemEntitynames, pathName);

            //
            // 记录 Entity 名称
            //
            Message.AddEntityNames(systemEntitynames);
        }

        //
        // 读取 Poison
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Compiling Poison.");

            Entity.PoisonCommon*        poisonEntity;
            Poison                      poison;
            PosionScript                script;

            //
            // 检查有多少项 Poison
            //
            dirName = dataWorkPath.Entity.Poison;
            endId = ModUtil.GetFileSequenceCount(dirName, beginId);

            for (i = 0; i <= endId; i++)
            {
                if (!S.FileExist((pathName = $@"{dirName}\{i:D5}.json"), isAssert: false))
                    //
                    // 跳过不存在的文件
                    //
                    continue;

                //
                // 读取 json 数据
                //
                S.JsonLoad(out poison, pathName);

                //
                // 记录 Entity 名称
                //
                Message.AddEntityName(poison.Name);
                entityId = Message.EntityNameCount - 1;

                //
                // 根据版本加载 Item Entity
                //
                script = poison.Script;
                poisonEntity = isDosGame ? &Message.PalFile.Core.EntityDos[entityId].Poison : &Message.PalFile.Core.EntityWin[entityId].Poison;
                poisonEntity->Level = poison.Level;
                poisonEntity->Color = poison.Color;
                poisonEntity->PlayerScript = Config.GetNewAddress(script.Player);
                poisonEntity->EnemyScript = Config.GetNewAddress(script.Enemy);
            }
        }

        //
        // 读取 Item
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Compiling Item.");

            Entity.ItemDos*     itemEntityDos;
            Entity.ItemWin*     itemEntityWin;
            Entity.ItemMask     mask;
            ItemScope           scope;
            ItemScript          script;
            Item                item;

            //
            // 检查有多少项 Item
            //
            dirName = dataWorkPath.Entity.Item;
            endId = ModUtil.GetFileSequenceCount(dirName, beginId);

            for (i = 0; i <= endId; i++)
            {
                if (!S.FileExist((pathName = $@"{dirName}\{i:D5}.json"), isAssert: false))
                    //
                    // 跳过不存在的文件
                    //
                    continue;

                //
                // 读取 json 数据
                //
                S.JsonLoad(out item, pathName);

                //
                // 记录 Entity 名称
                //
                Message.AddEntityName(item.Name);
                entityId = Message.EntityNameCount - 1;

                //
                // 计算 Item Mask
                //
                mask = 0;
                scope = item.Scope;
                if (scope.Usable) mask |= Entity.ItemMask.Usable;
                if (scope.Equipable) mask |= Entity.ItemMask.Equipable;
                if (scope.Throwable) mask |= Entity.ItemMask.Throwable;
                if (scope.Consuming) mask |= Entity.ItemMask.Consuming;
                if (!scope.NeedSelectTarget) mask |= Entity.ItemMask.SkipTargetSelection;
                if (scope.Sellable) mask |= Entity.ItemMask.Sellable;
                if (scope.WhoCanEquip != null && scope.WhoCanEquip != "")
                {
                    if (scope.WhoCanEquip.Contains('#'))
                    {
                        //
                        // # 符代表排除模式，代表除了 # 之后的，其他的都是期望的
                        //
                        for (j = 0; j < Base.MaxHero; j++)
                            mask |= (Entity.ItemMask)((int)Entity.ItemMask.EquipableByHeroFirst << j);

                        //
                        // 去掉不期望的掩码
                        //
                        for (j = 0; j < scope.WhoCanEquip.Length; j++)
                            mask &= ~(Entity.ItemMask)((int)Entity.ItemMask.EquipableByHeroFirst << S.StrToInt32(scope.WhoCanEquip[j]));
                    }
                    else
                    {
                        //
                        // 不包含 # 符，正常判断
                        //
                        for (j = 0; j < scope.WhoCanEquip.Length; j++)
                            mask |= (Entity.ItemMask)((int)Entity.ItemMask.EquipableByHeroFirst << S.StrToInt32(scope.WhoCanEquip[j]));
                    }
                }

                //
                // 根据版本加载 Item Entity
                //
                script = item.Script;
                if (isDosGame)
                {
                    itemEntityDos = &Message.PalFile.Core.EntityDos[entityId].Item;
                    itemEntityDos->BitmapId = (ushort)i;
                    itemEntityDos->Price = item.Price;
                    itemEntityDos->ScriptOnUse = Config.GetNewAddress(script.Use);
                    itemEntityDos->ScriptOnEquip = Config.GetNewAddress(script.Equip);
                    itemEntityDos->ScriptOnThrow = Config.GetNewAddress(script.Throw);
                    itemEntityDos->Flags = mask;
                }
                else
                {
                    itemEntityWin = &Message.PalFile.Core.EntityWin[entityId].Item;
                    itemEntityWin->BitmapId = item.BitmapId;
                    itemEntityWin->Price = item.Price;
                    itemEntityWin->ScriptOnUse = Config.GetNewAddress(script.Use);
                    itemEntityWin->ScriptOnEquip = Config.GetNewAddress(script.Equip);
                    itemEntityWin->ScriptOnThrow = Config.GetNewAddress(script.Throw);
                    itemEntityWin->ScriptDesc = Script.AddDescriptionScript(item.Description);
                    itemEntityWin->Flags = mask;
                }
            }
        }

        //
        // 读取 Magic 和 SummonGold
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Compiling Magic.");

            Base.CMagic*            cMagic;
            Base.CSummon*           cSummon;
            Entity.MagicDos*        magicEntityDos;
            Entity.MagicWin*        magicEntityWin;
            Entity.MagicMask        mask;
            Magic                   magic;
            MagicScope              scope;
            MagicScript             script;
            SummonGold              summonGold;
            bool                    isSummon;

            //
            // 检查有多少项 Magic
            //
            count = 0;
            dirName = dataWorkPath.Entity.Magic;
            endId = ModUtil.GetFileSequenceCount(dirName, beginId);
            count += endId + beginId;

            //
            // 检查有多少项 SummonGold
            //
            dirName = dataWorkPath.Entity.SummonGold;
            endId2 = ModUtil.GetFileSequenceCount(dirName, beginId);
            count += endId2 + beginId;

            //
            // 申请内存空间
            //
            Message.PalFile.Base.Magic = (Base.CMagic*)C.malloc(sizeof(Base.CMagic) * count);

            magic = null!;
            summonGold = null!;
            isSummon = false;
            dirName = dataWorkPath.Entity.Magic;
            for (i = 0, j = 0; ; i++, j++)
            {
                if (!isSummon && (j > endId))
                {
                    //
                    // 将任务变成读取 SummonGold
                    //
                    isSummon = true;
                    j = 0;
                    dirName = dataWorkPath.Entity.SummonGold;
                }
                else if (isSummon && (j > endId2))
                    //
                    // 处理完毕
                    //
                    break;

                if (!S.FileExist((pathName = $@"{dirName}\{j:D5}.json"), isAssert: false))
                    //
                    // 跳过不存在的文件
                    //
                    continue;

                if (!isSummon)
                {
                    //
                    // 读取 json 数据
                    //
                    S.JsonLoad(out magic, pathName);

                    cMagic = &Message.PalFile.Base.Magic[i];
                    Message.AddEntityName(magic.Name);
                    cMagic->EffectId = magic.Effect.EffectId;
                    cMagic->ActionType = magic.ActionType;
                    cMagic->XOffset = magic.Effect.XOffset;
                    cMagic->YOffset = magic.Effect.YOffset;
                    cMagic->LayerOffset = magic.Effect.LayerOffset;
                    cMagic->FrameDelay = magic.Effect.FrameDelay;
                    cMagic->KeepEffect = magic.Effect.KeepEffect;
                    cMagic->PreviewFrames = magic.Effect.PreviewFrames;
                    cMagic->EffectTimes = magic.Effect.EffectTimes;
                    cMagic->Shake = magic.Effect.Shake;
                    cMagic->Wave = magic.Effect.Wave;
                    cMagic->CostMP = magic.CostMP;
                    cMagic->BaseDamage = magic.BaseDamage;
                    cMagic->Type = magic.Type;
                    cMagic->SoundId = magic.SoundId;
                }
                else
                {
                    //
                    // 读取 json 数据
                    //
                    S.JsonLoad(out summonGold, pathName);

                    cSummon = (Base.CSummon*)&Message.PalFile.Base.Magic[i];
                    Message.AddEntityName(summonGold.Name);
                    cSummon->MagicDataId = (ushort)i;
                    cSummon->ActionType = Base.MagicActionType.Summon;
                    cSummon->XOffset = summonGold.Effect.XOffset;
                    cSummon->YOffset = summonGold.Effect.YOffset;
                    cSummon->SpriteId = summonGold.Effect.SpriteId;
                    cSummon->IdleFrames = summonGold.Effect.IdleFrames;
                    cSummon->MagicFrames = summonGold.Effect.MagicFrames;
                    cSummon->AttackFrames = summonGold.Effect.AttackFrames;
                    cSummon->ColorShift = summonGold.Effect.ColorShift;
                    cSummon->Shake = summonGold.Effect.Shake;
                    cSummon->Wave = summonGold.Effect.Wave;
                    cSummon->CostMP = summonGold.CostMP;
                    cSummon->CostMP = summonGold.CostMP;
                    cSummon->BaseDamage = summonGold.BaseDamage;
                    cSummon->Type = summonGold.Type;
                    cSummon->SoundId = summonGold.SoundId;
                }
                entityId = Message.EntityNameCount - 1;

                //
                // 计算 Magic Mask
                //
                mask = 0;
                scope = (!isSummon ? magic.Scope : summonGold.Scope);
                if (scope.UsableOutsideBattle) mask |= Entity.MagicMask.UsableOutsideBattle;
                if (scope.UsableInBattle) mask |= Entity.MagicMask.UsableInBattle;
                if (scope.UsableToEnemy) mask |= Entity.MagicMask.UsableToEnemy;
                if (!scope.NeedSelectTarget) mask |= Entity.MagicMask.SkipTargetSelection;

                //
                // 根据版本加载 Magic Entity
                //
                script = (!isSummon ? magic.Script : summonGold.Script);
                if (isDosGame)
                {
                    magicEntityDos = &Message.PalFile.Core.EntityDos[entityId].Magic;
                    magicEntityDos->MagicDataId = (ushort)i;
                    magicEntityDos->ScriptOnSuccess = Config.GetNewAddress(script.Success);
                    magicEntityDos->ScriptOnUse = Config.GetNewAddress(script.Use);
                    magicEntityDos->Flags = mask;
                }
                else
                {
                    magicEntityWin = &Message.PalFile.Core.EntityWin[entityId].Magic;
                    magicEntityWin->MagicDataId = (ushort)i;
                    magicEntityWin->ScriptOnSuccess = Config.GetNewAddress(script.Success);
                    magicEntityWin->ScriptOnUse = Config.GetNewAddress(script.Use);
                    magicEntityWin->ScriptDesc = Script.AddDescriptionScript(!isSummon ? magic.Description : summonGold.Description);
                    magicEntityWin->Flags = mask;
                }
            }
        }

        //
        // 读取 Enemy
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Compiling Enemy.");
            File.Delete(logPath = $@"{Config.LogOutPath}\Enemy.txt");
            string[]        tabNames = [
                "蠕动帧", "施法帧", "攻击帧", "蠕延迟", "动延迟", "Y偏移",
                "普攻音", "行动音", "施法音", "阵亡音", "进场音", "HP",
                "经验值", "金钱", "修行", "法术", "施法率", "普攻附",
                "攻附率", "可偷", "可偷数", "武术", "灵力", "防御", "身法",
                "吉运", "毒抗", "灵抗", "物抗", "二动", "灵葫",
            ];
            File.AppendAllText(logPath, $"\t\t");
            for (debugIndex = 0; debugIndex < tabNames.Length; debugIndex++)
            {
                File.AppendAllText(logPath, $"{tabNames[debugIndex]}");
                File.AppendAllText(logPath, (debugIndex % tabNames.Length != (tabNames.Length - 1)) ? "\t" : "\n");
            }

            Base.CEnemy*            cEnemy;
            Entity.EnemyCommon*     enemyEntity;
            Enemy                   enemy;

            //
            // 检查有多少项 Enemy
            //
            dirName = dataWorkPath.Entity.Enemy;
            endId = ModUtil.GetFileSequenceCount(dirName, beginId);
            count = endId + beginId;

            //
            // 申请内存空间
            //
            Message.PalFile.Base.Enemy = (Base.CEnemy*)C.malloc(sizeof(Base.CEnemy) * count);

            for (i = 0; i <= endId; i++)
            {
                if (!S.FileExist((pathName = $@"{dirName}\{i:D5}.json"), isAssert: false))
                    //
                    // 跳过不存在的文件
                    //
                    continue;

                //
                // 读取 json 数据
                //
                S.JsonLoad(out enemy, pathName);

                //
                // 将数据写入内存
                //
                cEnemy = &Message.PalFile.Base.Enemy[i];
                Message.AddEntityName(enemy.Name);
                entityId = Message.EntityNameCount - 1;
                cEnemy->IdleFrames = enemy.Effect.IdleFrames;
                cEnemy->MagicFrames = enemy.Effect.MagicFrames;
                cEnemy->AttackFrames = enemy.Effect.AttackFrames;
                cEnemy->IdleAnimSpeed = enemy.Effect.IdleAnimSpeed;
                cEnemy->ActWaitFrames = enemy.Effect.ActWaitFrames;
                cEnemy->YPosOffset = enemy.Effect.YPosOffset;
                cEnemy->AttackSound = enemy.Sound.AttackSound;
                cEnemy->ActionSound = enemy.Sound.ActionSound;
                cEnemy->MagicSound = enemy.Sound.MagicSound;
                cEnemy->DeathSound = enemy.Sound.DeathSound;
                cEnemy->CallSound = enemy.Sound.CallSound;
                cEnemy->Health = enemy.Health;
                cEnemy->Exp = enemy.Exp;
                cEnemy->Cash = enemy.Cash;
                cEnemy->Level = enemy.Level;
                cEnemy->MagicId = Config.GetNewMagicId(enemy.MagicId);
                cEnemy->MagicRate = enemy.MagicRate;
                cEnemy->AttackEquivItemId = (ushort)Config.GetNewEntityId(Entity.Type.Item, enemy.AttackEquivItemId);
                cEnemy->AttackEquivItemRate = enemy.AttackEquivItemRate;
                cEnemy->StealItemId = (ushort)Config.GetNewEntityId(Entity.Type.Item, enemy.StealItemId);
                cEnemy->StealItemCount = enemy.StealItemCount;
                cEnemy->Attribute.AttackStrength = enemy.BaseAttribute.AttackStrength;
                cEnemy->Attribute.MagicStrength = enemy.BaseAttribute.MagicStrength;
                cEnemy->Attribute.Defense = enemy.BaseAttribute.Defense;
                cEnemy->Attribute.Dexterity = enemy.BaseAttribute.Dexterity;
                cEnemy->Attribute.FleeRate = enemy.BaseAttribute.FleeRate;
                cEnemy->PoisonResistance = enemy.Resistance.Poison;
                cEnemy->ElementalResistance[0] = enemy.Resistance.Elemental.Wind;
                cEnemy->ElementalResistance[1] = enemy.Resistance.Elemental.Thunder;
                cEnemy->ElementalResistance[2] = enemy.Resistance.Elemental.Water;
                cEnemy->ElementalResistance[3] = enemy.Resistance.Elemental.Fire;
                cEnemy->ElementalResistance[4] = enemy.Resistance.Elemental.Earth;
                cEnemy->PhysicalResistance = enemy.Resistance.Physical;
                cEnemy->DualMove = (ushort)(enemy.DualMove ? 1 : 0);
                cEnemy->CollectValue = enemy.CollectValue;
                enemyEntity = isDosGame ? &Message.PalFile.Core.EntityDos[entityId].Enemy : &Message.PalFile.Core.EntityWin[entityId].Enemy;
                enemyEntity->EnemyDataId = (ushort)i;
                enemyEntity->ResistanceToSorcery = enemy.Resistance.Sorcery;
                enemyEntity->ScriptOnTurnStart = Config.GetNewAddress(enemy.Script.TurnStart);
                enemyEntity->ScriptOnBattleWon = Config.GetNewAddress(enemy.Script.BattleWon);
                enemyEntity->ScriptOnAction = Config.GetNewAddress(enemy.Script.Action);

                p16 = (short*)cEnemy;
                File.AppendAllText(logPath, $"${i:X4}({i:D5})\t[");
                for (debugIndex = 0; debugIndex < tabNames.Length; debugIndex++)
                {
                    File.AppendAllText(logPath, $"{p16[debugIndex]}");
                    File.AppendAllText(logPath, (debugIndex % tabNames.Length != (tabNames.Length - 1)) ? "\t" : "]\n");
                }
            }
        }

        //
        // 读取 Hero
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Compiling Hero.");

            Base.CHero*                     cHero;
            Base.CLevelUpMagicGroup*        cLevelUpMagicGroup;
            Base.CLevelUpMagic*             cLevelUpMagic;
            Entity.HeroCommon*              heroEntiy;
            Hero                            hero;
            HeroMagicLearnable[]            Learnable;

            //
            // 申请内存空间
            //
            Message.PalFile.Base.Hero = (Base.CHero*)C.malloc(sizeof(Base.CHero));
            Message.PalFile.Base.LevelUpMagicGroup = (Base.CLevelUpMagicGroup*)C.malloc(sizeof(Base.CLevelUpMagicGroup) * Base.MaxHeroMagic);

            //
            // 将数据写入内存
            //
            dirName = dataWorkPath.Entity.Hero;
            cHero = Message.PalFile.Base.Hero;
            cLevelUpMagicGroup = Message.PalFile.Base.LevelUpMagicGroup;
            for (i = 0; i < Base.MaxHero; i++)
            {
                if (!S.FileExist((pathName = $@"{dirName}\{(i + 1):D5}.json"), isAssert: false))
                    //
                    // 跳过不存在的文件
                    //
                    continue;

                //
                // 读取 json 数据
                //
                S.JsonLoad(out hero, pathName);

                Message.AddEntityName(hero.Name);
                entityId = Message.EntityNameCount - 1;
                cHero->AvatarId[i] = hero.AvatarId;
                cHero->SpriteIdInBattle[i] = hero.SpriteIdInBattle;
                cHero->SpriteId[i] = hero.SpriteId;
                cHero->Name[i] = 0;
                cHero->AttackAll[i] = (ushort)(hero.CanAttackAll ? 1 : 0);
                cHero->Level[i] = hero.Level;
                cHero->MaxHP[i] = hero.MaxHP;
                cHero->MaxMP[i] = hero.MaxMP;
                cHero->HP[i] = hero.HP;
                cHero->MP[i] = hero.MP;
                cHero->Equipment[i, 0] = hero.Equipment.Head;
                cHero->Equipment[i, 1] = hero.Equipment.Cloak;
                cHero->Equipment[i, 2] = hero.Equipment.Body;
                cHero->Equipment[i, 3] = hero.Equipment.Hand;
                cHero->Equipment[i, 4] = hero.Equipment.Foot;
                cHero->Equipment[i, 5] = hero.Equipment.Ornament;
                cHero->Attribute.AttackStrength[i] = hero.BaseAttribute.AttackStrength;
                cHero->Attribute.MagicStrength[i] = hero.BaseAttribute.MagicStrength;
                cHero->Attribute.Defense[i] = hero.BaseAttribute.Defense;
                cHero->Attribute.Dexterity[i] = hero.BaseAttribute.Dexterity;
                cHero->Attribute.FleeRate[i] = hero.BaseAttribute.FleeRate;
                cHero->PoisonResistance[i] = hero.Resistance.Poison;
                cHero->ElementalResistance[i, 0] = hero.Resistance.Elemental.Wind;
                cHero->ElementalResistance[i, 1] = hero.Resistance.Elemental.Thunder;
                cHero->ElementalResistance[i, 2] = hero.Resistance.Elemental.Water;
                cHero->ElementalResistance[i, 3] = hero.Resistance.Elemental.Fire;
                cHero->ElementalResistance[i, 4] = hero.Resistance.Elemental.Earth;
                cHero->CoveredBy[i] = hero.CoveredBy;
                Learnable = hero.Magic.Learnable;
                for (j = 0; j < Learnable.Length; j++)
                {
                    cHero->Magic[i, j] = Config.GetNewMagicId(hero.Magic.Learned[j]);

                    //
                    // 最后一个 Hero "盖罗娇" 没有可领悟的法术，故跳过
                    //
                    if (i < Base.MaxHero - 1)
                    {
                        cLevelUpMagic = i switch
                        {
                            0 => &cLevelUpMagicGroup[j].Hero1,
                            1 => &cLevelUpMagicGroup[j].Hero2,
                            2 => &cLevelUpMagicGroup[j].Hero3,
                            3 => &cLevelUpMagicGroup[j].Hero4,
                            4 => &cLevelUpMagicGroup[j].Hero5,
                            _ => throw new System.NotImplementedException(),
                        };
                        cLevelUpMagic->Level = Learnable[j].Level;
                        cLevelUpMagic->MagicId = Config.GetNewMagicId(Learnable[j].MagicId);
                    }
                }
                cHero->WalkFrames[i] = hero.WalkFrames;
                cHero->CooperativeMagic[i] = Config.GetNewMagicId(hero.Magic.Cooperative);
                cHero->DeathSound[i] = hero.Sound.Death;
                cHero->AttackSound[i] = hero.Sound.Attack;
                cHero->WeaponSound[i] = hero.Sound.Weapon;
                cHero->CriticalSound[i] = hero.Sound.Critical;
                cHero->MagicSound[i] = hero.Sound.Magic;
                cHero->CoverSound[i] = hero.Sound.Cover;
                cHero->DyingSound[i] = hero.Sound.Dying;
                heroEntiy = isDosGame ? &Message.PalFile.Core.EntityDos[entityId].Hero : &Message.PalFile.Core.EntityWin[entityId].Hero;
                heroEntiy->ScriptOnFriendDeath = Config.GetNewAddress(hero.Script.FriendDeath);
                heroEntiy->ScriptOnDying = Config.GetNewAddress(hero.Script.Dying);
            }
        }

        //
        // 读取 Shop
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Compiling Shop.");
            File.Delete(logPath = $@"{Config.LogOutPath}\Shop.txt");

            short[]     items;

            //
            // 检查有多少项 Shop
            //
            dirName = dataWorkPath.Shop;
            endId = ModUtil.GetFileSequenceCount(dirName, beginId);
            count = endId + beginId;

            //
            // 申请内存空间
            //
            Message.PalFile.Base.Shop = (Base.CShop*)C.malloc(sizeof(Base.CShop) * count);

            for (i = 0; i <= endId; i++)
            {
                if (!S.FileExist((pathName = $@"{dirName}\{i:D5}.json"), isAssert: false))
                    //
                    // 跳过不存在的文件
                    //
                    continue;

                //
                // 读取 json 数据
                //
                S.JsonLoad(out items, pathName);

                //
                // 将数据写入内存
                //
                for (j = 0; j < Base.MaxShopItem; j++)
                    Message.PalFile.Base.Shop[i].Items[j] = (ushort)Config.GetNewEntityId(Entity.Type.Item, items[j]);

                p16 = (short*)Message.PalFile.Base.Shop[i].Items;
                File.AppendAllText(logPath, $"${i:X4}({i:D5})\t{p16[0]}\t{p16[1]}\t{p16[2]}\t{p16[3]}\t{p16[4]}\t{p16[5]}\t{p16[6]}\t{p16[7]}\t{p16[8]}\t: (");

                for (j = 0; j < Base.MaxShopItem; j++)
                {
                    var name = Message.GetEntityName(p16[j]);
                    File.AppendAllText(logPath, $"{((name == "") ? "空" : name)}");
                    File.AppendAllText(logPath, (j % Base.MaxShopItem != Base.MaxShopItem - 1) ? "|" : ")\n");
                }
            }
        }

        //
        // 读取 EnemyTeam
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Compiling EnemyTeam.");
            File.Delete(logPath = $@"{Config.LogOutPath}\EnemyTeam.txt");

            Base.CEnemyTeam*        cEnemyTeam;
            short[]                 enemyTeam;

            //
            // 检查有多少项 EnemyTeam
            //
            dirName = dataWorkPath.EnemyTeam;
            endId = ModUtil.GetFileSequenceCount(dirName, beginId);
            count = endId + beginId;

            //
            // 申请内存空间
            //
            Message.PalFile.Base.EnemyTeam = (Base.CEnemyTeam*)C.malloc(sizeof(Base.CEnemyTeam) * count);

            for (i = 0; i <= endId; i++)
            {
                if (!S.FileExist((pathName = $@"{dirName}\{i:D5}.json"), isAssert: false))
                    //
                    // 跳过不存在的文件
                    //
                    continue;

                //
                // 读取 json 数据
                //
                S.JsonLoad(out enemyTeam, pathName);

                //
                // 将数据写入内存
                //
                cEnemyTeam = &Message.PalFile.Base.EnemyTeam[i];
                for (j = 0; j < Base.MaxEnemysInTeam; j++)
                    cEnemyTeam->EnemyIds[j] = Config.GetNewEntityId(Entity.Type.Enemy, enemyTeam[j]);

                p16 = cEnemyTeam->EnemyIds;
                File.AppendAllText(logPath, $"${i:X4}({i:D5})\t{p16[0]}\t{p16[1]}\t{p16[2]}\t{p16[3]}\t{p16[4]}\t: (");

                for (j = 0; j < Base.MaxEnemysInTeam; j++)
                {
                    File.AppendAllText(logPath, $"{(((pathName = Message.GetEntityName(p16[j], "禁用")) == "") ? "空" : pathName)}");
                    File.AppendAllText(logPath, (j % Base.MaxEnemysInTeam != Base.MaxEnemysInTeam - 1) ? "|" : ")\n");
                }
            }
        }

        //
        // 读取 BattleField
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Compiling BattleField.");
            File.Delete(logPath = $@"{Config.LogOutPath}\BattleField.txt");

            Base.CBattleField*       cBattleField;
            BattleField         battleField;

            //
            // 检查有多少项 BattleField
            //
            dirName = dataWorkPath.BattleField;
            endId = ModUtil.GetFileSequenceCount(dirName, beginId);
            count = endId + beginId;

            //
            // 申请缓冲区
            //
            Message.PalFile.Base.BattleField = (Base.CBattleField*)C.malloc(sizeof(Base.CBattleField) * count);

            for (i = 0; i <= endId; i++)
            {
                if (!S.FileExist((pathName = $@"{dirName}\{i:D5}.json"), isAssert: false))
                    //
                    // 跳过不存在的文件
                    //
                    continue;

                //
                // 读取 json 数据
                //
                S.JsonLoad(out battleField, pathName);

                //
                // 写入缓冲区
                //
                cBattleField = &Message.PalFile.Base.BattleField[i];
                cBattleField->ScreenWave = battleField.ScreenWave;
                cBattleField->ElementalEffect[0] = battleField.ElementalEffect.Wind;
                cBattleField->ElementalEffect[1] = battleField.ElementalEffect.Thunder;
                cBattleField->ElementalEffect[2] = battleField.ElementalEffect.Water;
                cBattleField->ElementalEffect[3] = battleField.ElementalEffect.Fire;
                cBattleField->ElementalEffect[4] = battleField.ElementalEffect.Earth;

                p16 = (short*)cBattleField;
                File.AppendAllText(logPath, $"${i:X4}({i:D5})\t{p16[0]:X4}\t{p16[1]:X4}\t{p16[2]:X4}\t{p16[3]:X4}\t{p16[4]:X4}\t{p16[5]:X4}\t: ");
                File.AppendAllText(logPath, $"({p16[0]}\t{p16[1]}\t{p16[2]}\t{p16[3]}\t{p16[4]}\t{p16[5]})\n");
            }
        }

        //
        // 读取 HeroActionEffect
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Compiling HeroActionEffect.");
            File.Delete(logPath = $@"{Config.LogOutPath}\HeroActionEffect.txt");

            Base.CHeroActionEffect*     cHeroActionEffect;
            HeroActionEffect            heroActionEffect;

            //
            // 检查有多少项 HeroActionEffect
            //
            dirName = dataWorkPath.HeroActionEffect;
            endId = ModUtil.GetFileSequenceCount(dirName, beginId);
            count = endId + beginId;

            //
            // 申请缓冲区
            //
            Message.PalFile.Base.HeroActionEffect = (Base.CHeroActionEffect*)C.malloc(sizeof(Base.CHeroActionEffect) * count);

            for (i = 0; i <= endId; i++)
            {
                if (!S.FileExist((pathName = $@"{dirName}\{i:D5}.json"), isAssert: false))
                    //
                    // 跳过不存在的文件
                    //
                    continue;

                //
                // 读取 json 数据
                //
                S.JsonLoad(out heroActionEffect, pathName);

                //
                // 写入缓冲区
                //
                cHeroActionEffect = &Message.PalFile.Base.HeroActionEffect[i];
                cHeroActionEffect->Magic = heroActionEffect.Magic;
                cHeroActionEffect->Attack = heroActionEffect.Attack;

                p16 = (short*)cHeroActionEffect;
                File.AppendAllText(logPath, $"${i:X4}({i:D5})\t{p16[0]:X4}\t{p16[1]:X4}\t: ");
                File.AppendAllText(logPath, $"({p16[0]}\t{p16[1]})\n");
            }
        }

        //
        // 读取 EnemyPosition
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Compiling EnemyPosition.");
            File.Delete(logPath = $@"{Config.LogOutPath}\EnemyPosition.txt");

            Base.CEnemyPosition*        cEnemyPosition;
            CPos*                       cPos;
            EnemyPosition               enemyPosition;

            //
            // 申请缓冲区
            //
            Message.PalFile.Base.EnemyPositionGroup = (Base.CEnemyPositionGroup*)C.malloc(sizeof(Base.CEnemyPositionGroup));
            cEnemyPosition = (Base.CEnemyPosition*)Message.PalFile.Base.EnemyPositionGroup;

            dirName = dataWorkPath.EnemyPosition;
            for (i = 0; i < Base.MaxEnemysInTeam; i++)
            {
                if (!S.FileExist((pathName = $@"{dirName}\{i:D5}.json"), isAssert: false))
                    //
                    // 跳过不存在的文件
                    //
                    continue;

                //
                // 读取 json 数据
                //
                S.JsonLoad(out enemyPosition, pathName);

                //
                // 写入缓冲区
                //
                cPos = (CPos*)&cEnemyPosition[i];
                cPos[0].X = enemyPosition.Pos5.X;
                cPos[0].Y = enemyPosition.Pos5.Y;
                cPos[1].X = enemyPosition.Pos4.X;
                cPos[1].Y = enemyPosition.Pos4.Y;
                cPos[2].X = enemyPosition.Pos3.X;
                cPos[2].Y = enemyPosition.Pos3.Y;
                cPos[3].X = enemyPosition.Pos2.X;
                cPos[3].Y = enemyPosition.Pos2.Y;
                cPos[4].X = enemyPosition.Pos1.X;
                cPos[4].Y = enemyPosition.Pos1.Y;

                p16 = (short*)cPos;
                File.AppendAllText(logPath, $"${i:X4}({i:D5})\t{p16[0]:X4}\t{p16[1]:X4}\t{p16[2]:X4}\t{p16[3]:X4}\t{p16[4]:X4}\t{p16[5]:X4}\t{p16[6]:X4}\t{p16[7]:X4}\t{p16[8]:X4}\t{p16[9]:X4}\t: ");
                File.AppendAllText(logPath, $"({p16[0]}\t{p16[1]}\t{p16[2]}\t{p16[3]}\t{p16[4]}\t{p16[5]}\t{p16[6]}\t{p16[7]}\t{p16[8]}\t{p16[9]})\n");
            }
        }

        //
        // 读取 LevelUpExp
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Compiling LevelUpExp.");
            File.Delete(logPath = $@"{Config.LogOutPath}\LevelUpExp.txt");

            Base.CLevelUpExp*       cLevelUpExp;
            LevelUpExp[]            levelUpExp;

            //
            // 申请缓冲区
            //
            Message.PalFile.Base.LevelUpExp = (Base.CLevelUpExp*)C.malloc(sizeof(Base.CLevelUpExp));
            cLevelUpExp = Message.PalFile.Base.LevelUpExp;

            //
            // 读取 json 数据
            //
            S.JsonLoad(out levelUpExp, $@"{dataWorkPath.LevelUpExp}.json");

            for (i = 0; i < Base.MaxLevel; i++)
            {
                //
                // 写入缓冲区
                //
                cLevelUpExp->Exp[i] = levelUpExp[i].Exp;

                p16 = cLevelUpExp->Exp;
                File.AppendAllText(logPath, $"${i:X4}({i:D5}): {p16[i]:X4} : ");
                File.AppendAllText(logPath, $"({p16[i]})\n");
            }
        }

        //
        // 读取 Scene 和 Event
        //
        {
            //
            // 输出处理进度
            //
            Util.Log("Compiling Scene/Event.");

            Core.CScene*        cScene;
            Core.CEvent*        cEvent;
            string[]            eventPaths;
            int[]               eventCounts;
            Scene               scene;
            Event               evt;

            //
            // 检查有多少项 Scene
            //
            dirName = dataWorkPath.Scene;
            endId = ModUtil.GetDirectorySequenceCount(dirName, beginId);
            count = endId + beginId;

            //
            // 申请 Scene 缓冲区
            //
            cScene = Message.PalFile.Core.Scene = (Core.CScene*)C.malloc(sizeof(Core.CScene) * count);
            eventPaths = new string[count];
            eventCounts = new int[count];
            Config.SceneEventIndexs = new ushort[count];

            //
            // 统计 Event
            //
            count2 = 0;
            for (i = beginId; i <= endId; i++)
                //
                // 检查每个 Scene 下有多少项 Event
                //
                Config.SceneEventIndexs[i] = (ushort)(count2 += eventCounts[i] = (ModUtil.GetFileSequenceCount(eventPaths[i] = $@"{dirName}\{i:D5}", beginId) + beginId));

            //
            // 申请 Event 缓冲区
            //
            cEvent = Message.PalFile.Core.Event = (Core.CEvent*)C.malloc(sizeof(Core.CEvent) * count2);

            File.Delete(logPath = $@"{Config.LogOutPath}\Scene.txt");

            for (i = beginId, entityId = 0; i <= endId; i++)
            {
                //
                // 严格检查，务必需要有 Scene.json
                //
                S.FileExist(pathName = $@"{dirName}\{i:D5}\Scene.json");

                //
                // 读取 json 数据
                //
                S.JsonLoad(out scene, pathName);

                //
                // 写入 Scene 缓冲区
                //
                cScene[i].MapId = scene.MapId;
                cScene[i].ScriptOnEnter = Config.GetNewAddress(scene.Script.Enter);
                cScene[i].ScriptOnTeleport = Config.GetNewAddress(scene.Script.Teleport);

                //
                // Debug Log Output: Event
                //
                p16 = (short*)&cScene[i];
                File.AppendAllText(logPath, $"\n\t\t\t\t[Map\t进场\t脱离\t事件始]");
                File.AppendAllText(logPath, $"\n({Message.GetEnumEntry($"Scene{Config.Version}", i)}) ${i:X4}({i:D5})\t[{p16[0]}\t{p16[1]:X4}\t{p16[2]:X4}\t{p16[3]}]");
                File.AppendAllText(logPath, $"\n\t\t[隐匿\tX\tY\t图层\t触发\t循环\t状态\t模式\t形象\t方向帧\t面朝\t置帧\t触发数\t？？？\t总帧数\t循环数]\n");

                for (j = beginId; j < eventCounts[i]; j++, entityId++)
                {
                    //
                    // 严格检查，不可缺少一个已经被统计的 Event
                    //
                    S.FileExist(pathName = $@"{dirName}\{i:D5}\{j:D5}.json");

                    //
                    // 读取 json 数据
                    //
                    S.JsonLoad(out evt, pathName);

                    //
                    // 为 Event 分配新编号
                    //
                    Config.AddNewEventId((ushort)i, (ushort)j, (ushort)entityId);

                    //
                    // 写入 Event 缓冲区
                    //
                    cEvent[entityId].VanishTime = evt.VanishTime;
                    cEvent[entityId].X = evt.X;
                    cEvent[entityId].Y = evt.Y;
                    cEvent[entityId].Layer = evt.Layer;
                    cEvent[entityId].TriggerScript = Config.GetNewAddress(evt.Script.Trigger);
                    cEvent[entityId].AutoScript = Config.GetNewAddress(evt.Script.Auto);
                    cEvent[entityId].State = evt.Trigger.StateCode;
                    if (evt.Trigger.Range == -1)
                        //
                        // 无法触发
                        //
                        cEvent[entityId].TriggerMode = EventTriggerMode.None;
                    else
                    {
                        cEvent[entityId].TriggerMode = (EventTriggerMode)evt.Trigger.Range;

                        if (evt.Trigger.IsAutoTrigger)
                            //
                            // Event 是靠近后自动触发的
                            // TriggerMode = EventTriggerMode.TouchNear + Range
                            //
                            cEvent[entityId].TriggerMode += (ushort)EventTriggerMode.TouchNear;
                    }
                    cEvent[entityId].SpriteId = evt.Sprite.SpriteId;
                    cEvent[entityId].SpriteFrames = evt.Sprite.SpriteFrames;
                    cEvent[entityId].Direction = evt.Sprite.Direction;
                    cEvent[entityId].CurrentFrameId = evt.Sprite.CurrentFrameId;
                    cEvent[entityId].TriggerIdleFrame = evt.Script.TriggerIdleFrame;
                    cEvent[entityId].AutoIdleFrame = evt.Script.AutoIdleFrame;

                    //
                    // Debug Log Output: Event
                    //
                    p16 = (short*)&cEvent[entityId];
                    File.AppendAllText(logPath, $"${entityId:X4}({entityId:D5})\t[{p16[0]}\t{p16[1]}\t{p16[2]}\t{p16[3]}\t{p16[4]:X4}\t{p16[5]:X4}\t{p16[6]}\t{p16[7]}\t{p16[8]}\t{p16[9]}\t{p16[10]}\t{p16[11]}\t{p16[12]}\t{p16[13]}\t{p16[14]}\t{p16[15]}] {Message.GetEnumEntry("Sprite", p16[8])}\n");
                }
            }
        }

        //
        // Debug Log Output: Entity
        //
        {
            Entity.Win*     entity;

            entity = Message.PalFile.Core.EntityWin;

            File.Delete(logPath = $@"{Config.LogOutPath}\Entity.txt");

            for (i = 0; i < entityCount; i++)
            {
                p16 = (short*)&entity[i];

                File.AppendAllText(
                    logPath,
                    $"#0x{i:X4}({i:D5}): {p16[0]:X4} {p16[1]:X4} {p16[2]:X4} {p16[3]:X4} {p16[4]:X4} {p16[5]:X4} {p16[6]:X4} : {Message.GetEntityName(i)}({p16[0]} {p16[1]} {p16[2]} {p16[3]} {p16[4]} {p16[5]} {p16[6]})\n"
                );
            }
        }

        //
        // Debug Log Output: Hero
        //
        {
            string[] tabNames = [
                "肖像", "战像", "行象", "名称", "全攻", "？？？",
                "修行", "MaxHP", "MaxMP", "HP", "MP",
                "头戴", "披挂", "身穿", "手持", "脚穿", "佩带",
                "武术", "灵力", "防御", "身法", "吉运",
                "毒抗", "风抗", "雷抗", "水抗", "火抗", "土抗",
                "？？？", "？？？", "？？？", "受援", "仙术1", "仙术2",
                "仙术3", "仙术4", "仙术5", "仙术6", "仙术7",
                "仙术8", "仙术9", "仙术10", "仙术11", "仙术12",
                "仙术13", "仙术14", "仙术15", "仙术16", "仙术17",
                "仙术18", "仙术19", "仙术20", "仙术21", "仙术22",
                "仙术23", "仙术24", "仙术25", "仙术26", "仙术27",
                "仙术28", "仙术29", "仙术30", "仙术31", "仙术32",
                "方向帧", "合体", "？？？", "？？？", "阵亡音",
                "普攻音", "挥砍音", "暴击音", "施法音", "格挡音",
                "濒死音",
            ];

            File.Delete(logPath = $@"{Config.LogOutPath}\Hero.txt");
            p16 = (short*)Message.PalFile.Base.Hero;
            File.AppendAllText(logPath, "\t李逍遥\t赵灵儿\t林月如\t巫后\t阿奴\t盖罗娇\n");
            for (i = 0; i < tabNames.Length; i++, p16 += Base.MaxHero)
                File.AppendAllText(logPath, $"{tabNames[i]}\t[{p16[0]}\t{p16[1]}\t{p16[2]}\t{p16[3]}\t{p16[4]}\t{p16[5]}]\n");
        }
    }
}
