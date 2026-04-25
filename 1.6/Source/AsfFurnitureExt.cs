using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using UnityEngine;
using Verse;
using RimWorld;

namespace AsfFurnitureExt
{
    internal static class DefValue
    {
        internal const string ModName = "ASF Furniture Extension";
        internal const string TranslationPrefix = "nzasf";
        internal const string NewDefPrefix = "Nz_ASFS_";

        /// <summary>
        /// 判断 ASF 是否已启用。
        /// </summary>
        public static bool IsASFEnabled()
        {
            return ModLister.AllInstalledMods.Any(mod =>
                mod.PackageId.ToString().ToLower().Contains("adaptive.storage.framework") && mod.Active);
        }

        // 翻译辅助
        public static string LabelPrefix => $"{TranslationPrefix}.label_prefix".Translate();
        public static string DescSuffix(string key) => $"{TranslationPrefix}.{key}".Translate();
        public static string LogMessage(string key) => $"{TranslationPrefix}.{key}".Translate();
        public static string LogMessage(string key, params object[] args) => $"{TranslationPrefix}.{key}".Translate(args);
    }

    internal class AsfFurnitureExtSettings : ModSettings
    {
        public bool inited = false;
        public bool enabled = true;

        /// <summary>
        /// 读取或写入模组设置数据。
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(value: ref inited, label: "AsfFurnitureExtInited", defaultValue: false);
            Scribe_Values.Look(value: ref enabled, label: "AsfFurnitureExtEnabled", defaultValue: true);

            if (!this.inited)
            {
                this.InitData();
            }
        }

        /// <summary>
        /// 初始化默认设置数据。
        /// </summary>
        public void InitData()
        {
            Log.Message(DefValue.LogMessage("log_init"));
            this.inited = true;
            this.enabled = true;
        }
    }

    [StaticConstructorOnStartup]
    internal class AsfFurnitureExtMod : Mod
    {
        public static AsfFurnitureExtSettings settings;
        public static AsfFurnitureExtMod Instance { get; private set; }

        /// <summary>
        /// 构造模组实例并加载设置。
        /// </summary>
        public AsfFurnitureExtMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<AsfFurnitureExtSettings>();
            Instance = this;
        }

        /// <summary>
        /// 返回设置界面名称。
        /// </summary>
        public override string SettingsCategory() => "ASF Furniture Ext";

        /// <summary>
        /// 绘制设置窗口内容。
        /// </summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            base.DoSettingsWindowContents(inRect: inRect);

            Listing_Standard ls = new Listing_Standard();
            ls.Begin(inRect);

            ls.CheckboxLabeled(DefValue.LogMessage("settings_enable"), ref settings.enabled,
                DefValue.LogMessage("settings_enable_desc"));

            ls.GapLine();

            if (DefValue.IsASFEnabled())
            {
                ls.Label(DefValue.LogMessage("asf_detected"));
            }
            else
            {
                GUI.color = Color.red;
                ls.Label(DefValue.LogMessage("asf_not_detected"));
                GUI.color = Color.white;
            }

            ls.GapLine();

            ls.Label(DefValue.LogMessage("cloned_furniture"));
            foreach (var config in DefDatabase<FurnitureCloneConfigDef>.AllDefs)
            {
                if (config.enabled)
                {
                    ThingDef originalDef = DefDatabase<ThingDef>.GetNamed(config.originalDefName, false);
                    if (originalDef != null)
                    {
                        string label = DefValue.LabelPrefix + originalDef.label;
                        ls.Label($"  - {label}");
                    }
                }
            }

            ls.End();

            settings.Write();
        }
    }

    [StaticConstructorOnStartup]
    public static class AsfFurniturePatcher
    {
        private static readonly string[] AsfAssemblyNames =
        {
            "AdaptiveStorageFramework",
            "AdaptiveStorage"
        };

        /// <summary>
        /// 执行静态初始化并安排家具克隆流程。
        /// </summary>
        static AsfFurniturePatcher()
        {
            Log.Message(DefValue.LogMessage("log_loaded"));

            if (!DefValue.IsASFEnabled())
            {
                Log.Warning(DefValue.LogMessage("log_asf_missing"));
                return;
            }

            // 在所有定义加载完毕后克隆家具
            // 使用 ExecuteWhenFinished 确保在 XML 配置加载完成后再执行
            LongEventHandler.ExecuteWhenFinished(action: () =>
            {
                if (AsfFurnitureExtMod.settings.enabled)
                {
                    Log.Message(DefValue.LogMessage("log_cloning_start"));

                    // 从 DefDatabase 加载所有配置
                    foreach (var config in DefDatabase<FurnitureCloneConfigDef>.AllDefs)
                    {
                        if (!config.enabled)
                        {
                            Log.Message($"[ASF Furniture Ext] Skipping disabled config: {config.defName}");
                            continue;
                        }

                        try
                        {
                            CloneFurnitureWithStorage(config);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[ASF Furniture Ext] Failed to clone {config.originalDefName}: {ex}");
                        }
                    }

                    Log.Message(DefValue.LogMessage("log_cloning_complete"));
                }
                else
                {
                    Log.Message(DefValue.LogMessage("log_cloning_disabled"));
                }
            });
        }

        /// <summary>
        /// 按类型全名在 ASF 的已知程序集里解析类型。
        /// </summary>
        private static Type ResolveAsfType(string fullTypeName)
        {
            foreach (string assemblyName in AsfAssemblyNames)
            {
                Type type = Type.GetType(fullTypeName + ", " + assemblyName, false);
                if (type != null)
                {
                    return type;
                }
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullTypeName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        /// <summary>
        /// 克隆原始家具并附加 ASF 储物功能。
        /// </summary>
        private static void CloneFurnitureWithStorage(FurnitureCloneConfigDef config)
        {
            ThingDef originalDef = DefDatabase<ThingDef>.GetNamed(config.originalDefName, false);
            if (originalDef == null)
            {
                Log.Warning(DefValue.LogMessage("log_cloning_original_not_found", config.originalDefName));
                return;
            }

            // 使用前缀生成新的定义名称
            string newDefName = DefValue.NewDefPrefix + config.originalDefName;

            Log.Message($"[ASF Furniture Ext] Cloning {config.originalDefName} -> {newDefName}");

            // 复制原始定义，生成新的 ThingDef
            ThingDef newDef = CopyThingDef(originalDef, config, newDefName);

            // 设置模组内容包
            newDef.modContentPack = AsfFurnitureExtMod.Instance.Content;

            // 注意：此处暂时禁用设计器下拉组，以便测试材质选择
            // 暂时不启用设计器下拉组：SetupDesignatorDropdown(originalDef, newDef);

            // 使用 DefGenerator 正确注册定义（这会触发 PostLoad 及其他必要初始化）
            DefGenerator.AddImpliedDef<ThingDef>(newDef, false);

            // 同时加入 BuildableDef 数据库以用于建造
            if (!DefDatabase<BuildableDef>.AllDefs.Contains(newDef))
            {
                DefDatabase<BuildableDef>.Add(newDef);
            }

            // 为 AdaptiveStorage 注册 GraphicsDef
            RegisterGraphicsDef(newDef);

            // 重新解析指定类别以刷新建造菜单
            if (originalDef.designationCategory != null)
            {
                originalDef.designationCategory.ResolveReferences();
            }

            Log.Message(DefValue.LogMessage("log_cloning_success", newDefName));
        }

        /// <summary>
        /// 创建或同步设计器下拉分组。
        /// </summary>
        private static void SetupDesignatorDropdown(ThingDef originalDef, ThingDef newDef)
        {
            // 创建或复用已有的下拉分组
            if (originalDef.designatorDropdown == null)
            {
                var dropdown = new DesignatorDropdownGroupDef
                {
                    defName = originalDef.defName + "_ASFS_Group"
                };
                
                // 注册下拉分组定义
                dropdown.modContentPack = AsfFurnitureExtMod.Instance.Content;
                DefGenerator.AddImpliedDef<DesignatorDropdownGroupDef>(dropdown, false);
                
                originalDef.designatorDropdown = dropdown;
                newDef.designatorDropdown = dropdown;
            }
            else
            {
                newDef.designatorDropdown = originalDef.designatorDropdown;
            }
        }

        /// <summary>
        /// 为新定义注册 ASF 的 GraphicsDef，并完成其初始化流程。
        /// </summary>
        /// <param name="newDef">要绑定 ASF 图形配置的新家具定义。</param>
        private static void RegisterGraphicsDef(ThingDef newDef)
        {
            Type graphicsDefType = ResolveAsfType("AdaptiveStorage.GraphicsDef");
            if (graphicsDefType == null)
            {
                Log.Warning("[ASF Furniture Ext] Could not find AdaptiveStorage.GraphicsDef type");
                return;
            }

            try
            {
                // 创建 ASF 的 GraphicsDef 实例。
                var graphicsDef = (Def)Activator.CreateInstance(graphicsDefType);

                // 写入定义名和模组来源。
                graphicsDefType.GetField("defName")?.SetValue(graphicsDef, newDef.defName + "_Graphics");
                graphicsDefType.GetProperty("modContentPack")?.SetValue(graphicsDef, AsfFurnitureExtMod.Instance.Content);

                // 将新家具作为目标定义挂到 GraphicsDef 上。
                var targetDefsField = graphicsDefType.GetField("targetDefs");
                if (targetDefsField != null)
                {
                    var targetDefs = targetDefsField.GetValue(graphicsDef);
                    if (targetDefs == null)
                    {
                        targetDefs = Activator.CreateInstance(typeof(List<ThingDef>));
                        targetDefsField.SetValue(graphicsDef, targetDefs);
                    }
                    targetDefs.GetType().GetMethod("Add")?.Invoke(targetDefs, new object[] { newDef });
                }

                // 让 ASF 在渲染储物内容时始终显示内容层。
                graphicsDefType.GetField("showContainedItems")?.SetValue(graphicsDef, true);

                // 通过泛型反射把 GraphicsDef 注册进正确的 DefDatabase。
                var addImpliedDefMethod = typeof(DefGenerator)
                    .GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == "AddImpliedDef" && m.IsGenericMethodDefinition);
                if (addImpliedDefMethod == null)
                {
                    throw new MissingMethodException("DefGenerator", "AddImpliedDef");
                }

                addImpliedDefMethod.MakeGenericMethod(graphicsDefType)
                    .Invoke(null, new object[] { graphicsDef, false });

                // 先解析引用，再执行 ASF 的初始化。
                graphicsDefType.GetMethod("ResolveReferences",
                    BindingFlags.Instance | BindingFlags.Public)?
                    .Invoke(graphicsDef, null);

                graphicsDefType.GetMethod("Initialize",
                    BindingFlags.Instance | BindingFlags.Public)?
                    .Invoke(graphicsDef, null);

                // 记录注册结果，便于日志排查。
                Log.Message($"[ASF Furniture Ext] Registered GraphicsDef for {newDef.defName}");
            }
            catch (Exception ex)
            {
                Log.Error($"[ASF Furniture Ext] Failed to register GraphicsDef: {ex}");
            }
        }

        /// <summary>
        /// 复制原始 ThingDef 并应用本模组的储物配置。
        /// </summary>
        private static ThingDef CopyThingDef(ThingDef original, FurnitureCloneConfigDef config, string newDefName)
        {
            // 使用前缀生成名称标签
            string newLabel = DefValue.LabelPrefix + original.label;

            // 使用后缀生成说明文本
            string descriptionSuffix = DefValue.DescSuffix(config.descriptionSuffixKey);
            string newDescription = original.description + descriptionSuffix;

            // 使用 MakeShallowCopy 复制原始对象的字段
            ThingDef newDef = MakeShallowCopy(original);
            
            // 覆盖特定属性
            newDef.defName = newDefName;
            newDef.label = newLabel;
            newDef.description = newDescription;
            newDef.thingClass = typeof(AdaptiveStorage.ThingClass);
            newDef.surfaceType = SurfaceType.Item;
            
            // 复制图形数据（需要特殊处理）
            newDef.graphicData = CopyGraphicData(original.graphicData);
            
            // 复制建筑属性
            newDef.building = CopyBuildingProperties(original.building, config);

            // 为储物功能再次设置建筑属性
            newDef.building = CopyBuildingProperties(original.building, config);

            // 设置检查面板标签
            newDef.inspectorTabs = new List<Type>();
            if (original.inspectorTabs != null)
            {
                newDef.inspectorTabs.AddRange(original.inspectorTabs);
            }
            // 如果尚未存在，则添加储物标签页
            if (!newDef.inspectorTabs.Contains(typeof(ITab_Storage)))
            {
                newDef.inspectorTabs.Add(typeof(ITab_Storage));
            }

            // 添加 AdaptiveStorage 的模组扩展
            if (newDef.modExtensions == null)
                newDef.modExtensions = new List<DefModExtension>();

            // 通过反射创建 AdaptiveStorage 扩展
            Type extensionType = ResolveAsfType("AdaptiveStorage.Extension");
            if (extensionType != null)
            {
                DefModExtension extension = (DefModExtension)Activator.CreateInstance(extensionType);
                
                // 使用字段的真实枚举类型把 labelFormat 设为 Default
                var labelFormatField = extensionType.GetField("labelFormat");
                if (labelFormatField != null)
                {
                    object defaultLabelFormat = Enum.Parse(labelFormatField.FieldType, "Default");
                    labelFormatField.SetValue(extension, defaultLabelFormat);
                }
                
                newDef.modExtensions.Add(extension);
            }

            // 注意：这里不要调用 ResolveReferences()，DefGenerator.AddImpliedDef 会触发 PostLoad()

            // 为新的 ThingDef 生成蓝图和框架
            // 必须在设置 thingClass 之后执行，确保它们使用正确的类
            GenerateBlueprintAndFrame(newDef);

            return newDef;
        }

        /// <summary>
        /// 为新定义生成蓝图和框架定义。
        /// </summary>
        private static void GenerateBlueprintAndFrame(ThingDef newDef)
        {
            try
            {
                // 通过反射获取 NewBlueprintDef_Thing 方法
                var newBlueprintDefMethod = typeof(ThingDefGenerator_Buildings).GetMethod("NewBlueprintDef_Thing", BindingFlags.Static | BindingFlags.NonPublic);
                if (newBlueprintDefMethod != null)
                {
                    var blueprintDef = newBlueprintDefMethod.Invoke(null, new object[] { newDef, false, null, false }) as ThingDef;
                    if (blueprintDef != null)
                    {
                        blueprintDef.shortHash = 0;
                        DefGenerator.AddImpliedDef(blueprintDef, false);
                    }
                }

                // 通过反射获取 NewFrameDef_Thing 方法
                var newFrameDefMethod = typeof(ThingDefGenerator_Buildings).GetMethod("NewFrameDef_Thing", BindingFlags.Static | BindingFlags.NonPublic);
                if (newFrameDefMethod != null)
                {
                    var frameDef = newFrameDefMethod.Invoke(null, new object[] { newDef, false }) as ThingDef;
                    if (frameDef != null)
                    {
                        frameDef.shortHash = 0;
                        DefGenerator.AddImpliedDef(frameDef, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[ASF Furniture Ext] Failed to generate blueprint/frame for {newDef.defName}: {ex}");
            }
        }

        /// <summary>
        /// 使用浅拷贝方式复制对象字段。
        /// </summary>
        private static T MakeShallowCopy<T>(T from) where T : new()
        {
            var to = new T();
            CopyFields(from, to);
            return to;
        }

        /// <summary>
        /// 复制指定类型的所有实例字段。
        /// </summary>
        private static void CopyFields<T>(T from, T to)
        {
            foreach (var fieldInfo in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                fieldInfo.SetValue(to, fieldInfo.GetValue(from));
            }
        }

        /// <summary>
        /// 复制数值修正列表。
        /// </summary>
        private static List<StatModifier> CopyStatBases(List<StatModifier> original)
        {
            if (original == null) return null;

            List<StatModifier> copy = new List<StatModifier>();
            foreach (var stat in original)
            {
                copy.Add(new StatModifier
                {
                    stat = stat.stat,
                    value = stat.value
                });
            }
            return copy;
        }

        /// <summary>
        /// 复制图形数据对象。
        /// </summary>
        private static GraphicData CopyGraphicData(GraphicData original)
        {
            if (original == null) return null;

            return new GraphicData
            {
                texPath = original.texPath,
                graphicClass = original.graphicClass,
                drawSize = original.drawSize,
                drawOffset = original.drawOffset,
                drawOffsetNorth = original.drawOffsetNorth,
                drawOffsetEast = original.drawOffsetEast,
                drawOffsetSouth = original.drawOffsetSouth,
                drawOffsetWest = original.drawOffsetWest,
                shadowData = original.shadowData,
                damageData = original.damageData,
                color = original.color,
                colorTwo = original.colorTwo,
                shaderType = original.shaderType,
                onGroundRandomRotateAngle = original.onGroundRandomRotateAngle,
                flipExtraRotation = original.flipExtraRotation
            };
        }

        /// <summary>
        /// 复制组件属性列表。
        /// </summary>
        private static List<CompProperties> CopyComps(List<CompProperties> original)
        {
            if (original == null) return new List<CompProperties>();

            List<CompProperties> copy = new List<CompProperties>();
            foreach (var comp in original)
            {
                // 深拷贝组件属性
                CompProperties copiedComp = CopyCompProperties(comp);
                if (copiedComp != null)
                {
                    copy.Add(copiedComp);
                }
            }
            return copy;
        }

        /// <summary>
        /// 复制单个组件属性对象。
        /// </summary>
        private static CompProperties CopyCompProperties(CompProperties original)
        {
            if (original == null) return null;

            // 创建同类型的新实例
            try
            {
                var copy = (CompProperties)Activator.CreateInstance(original.GetType());
                // 这里保留原始行为，避免组件属性复制不完整导致异常
                return original;
            }
            catch
            {
                return original;
            }
        }

        /// <summary>
        /// 复制建筑属性并注入储物设置。
        /// </summary>
        private static BuildingProperties CopyBuildingProperties(BuildingProperties original, FurnitureCloneConfigDef config)
        {
            BuildingProperties copy = new BuildingProperties
            {
                // 储物设置
                preventDeteriorationOnTop = true,
                ignoreStoredThingsBeauty = true,
                maxItemsInCell = config.maxItemsInCell,
                blueprintClass = typeof(Blueprint_Storage),
                paintable = original?.paintable ?? true,
                storageGroupTag = "Shelf",

                // 复制其他建筑属性
                buildingTags = original?.buildingTags?.ToList(),
                ai_chillDestination = original?.ai_chillDestination ?? false,
                allowAutoroof = original?.allowAutoroof ?? true,
                canPlaceOverImpassablePlant = original?.canPlaceOverImpassablePlant ?? false,
                canPlaceOverWall = original?.canPlaceOverWall ?? false,
                isEdifice = original?.isEdifice ?? true,
                isInert = original?.isInert ?? false,
                isPlayerEjectable = original?.isPlayerEjectable ?? false,
                isResourceRock = original?.isResourceRock ?? false,
                claimable = original?.claimable ?? true,
                expandHomeArea = original?.expandHomeArea ?? true,
                shipPart = original?.shipPart ?? false,
                wantsHopperAdjacent = original?.wantsHopperAdjacent ?? false,
            };

            // 配置储物设置
            copy.fixedStorageSettings = CreateStorageSettings(config);
            copy.defaultStorageSettings = CreateDefaultStorageSettings(config);

            return copy;
        }

        /// <summary>
        /// 根据配置创建固定储物设置。
        /// </summary>
        private static StorageSettings CreateStorageSettings(FurnitureCloneConfigDef config)
        {
            StorageSettings settings = new StorageSettings();
            settings.filter = new ThingFilter();

            // 允许的类别
            if (config.storageFilterCategories != null)
            {
                foreach (string categoryName in config.storageFilterCategories)
                {
                    ThingCategoryDef categoryDef = DefDatabase<ThingCategoryDef>.GetNamed(categoryName, false);
                    if (categoryDef != null)
                    {
                        settings.filter.SetAllow(categoryDef, true);
                    }
                }
            }

            // 禁止的具体物品
            if (config.storageDisallowedThingDefs != null)
            {
                foreach (string thingDefName in config.storageDisallowedThingDefs)
                {
                    ThingDef thingDef = DefDatabase<ThingDef>.GetNamed(thingDefName, false);
                    if (thingDef != null)
                    {
                        settings.filter.SetAllow(thingDef, false);
                    }
                }
            }

            return settings;
        }

        /// <summary>
        /// 根据配置创建默认储物设置。
        /// </summary>
        private static StorageSettings CreateDefaultStorageSettings(FurnitureCloneConfigDef config)
        {
            StorageSettings settings = new StorageSettings
            {
                Priority = StoragePriority.Preferred
            };
            settings.filter = new ThingFilter();

            // 默认使用第一个类别
            if (config.storageFilterCategories != null && config.storageFilterCategories.Count > 0)
            {
                ThingCategoryDef categoryDef = DefDatabase<ThingCategoryDef>.GetNamed(config.storageFilterCategories[0], false);
                if (categoryDef != null)
                {
                    settings.filter.SetAllow(categoryDef, true);
                }
            }

            return settings;
        }
    }
}
