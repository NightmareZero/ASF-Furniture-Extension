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

        public static bool IsASFEnabled()
        {
            return ModLister.AllInstalledMods.Any(mod =>
                mod.PackageId.ToString().ToLower().Contains("adaptive.storage.framework") && mod.Active);
        }

        // Translation helpers
        public static string LabelPrefix => $"{TranslationPrefix}.label_prefix".Translate();
        public static string DescSuffix(string key) => $"{TranslationPrefix}.{key}".Translate();
        public static string LogMessage(string key) => $"{TranslationPrefix}.{key}".Translate();
        public static string LogMessage(string key, params object[] args) => $"{TranslationPrefix}.{key}".Translate(args);
    }

    internal class AsfFurnitureExtSettings : ModSettings
    {
        public bool inited = false;
        public bool enabled = true;

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

        public AsfFurnitureExtMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<AsfFurnitureExtSettings>();
            Instance = this;
        }

        public override string SettingsCategory() => "ASF Furniture Ext";

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
        static AsfFurniturePatcher()
        {
            Log.Message(DefValue.LogMessage("log_loaded"));

            if (!DefValue.IsASFEnabled())
            {
                Log.Warning(DefValue.LogMessage("log_asf_missing"));
                return;
            }

            // Clone furniture after all defs are loaded
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

        private static void CloneFurnitureWithStorage(FurnitureCloneConfigDef config)
        {
            ThingDef originalDef = DefDatabase<ThingDef>.GetNamed(config.originalDefName, false);
            if (originalDef == null)
            {
                Log.Warning(DefValue.LogMessage("log_cloning_original_not_found", config.originalDefName));
                return;
            }

            // Generate new def name with prefix
            string newDefName = DefValue.NewDefPrefix + config.originalDefName;

            Log.Message($"[ASF Furniture Ext] Cloning {config.originalDefName} -> {newDefName}");

            // Create new ThingDef by copying original
            ThingDef newDef = CopyThingDef(originalDef, config, newDefName);

            // Register the new def
            DefDatabase<ThingDef>.Add(newDef);

            Log.Message(DefValue.LogMessage("log_cloning_success", newDefName));
        }

        private static ThingDef CopyThingDef(ThingDef original, FurnitureCloneConfigDef config, string newDefName)
        {
            // Generate label with prefix
            string newLabel = DefValue.LabelPrefix + original.label;

            // Generate description with suffix
            string descriptionSuffix = DefValue.DescSuffix(config.descriptionSuffixKey);
            string newDescription = original.description + descriptionSuffix;

            ThingDef newDef = new ThingDef
            {
                defName = newDefName,
                label = newLabel,
                description = newDescription,

                // Copy basic properties
                thingClass = typeof(AdaptiveStorage.ThingClass),
                category = original.category,
                tickerType = original.tickerType,
                altitudeLayer = original.altitudeLayer,
                useHitPoints = original.useHitPoints,
                selectable = original.selectable,
                statBases = CopyStatBases(original.statBases),
                size = original.size,
                costStuffCount = original.costStuffCount,
                stuffCategories = original.stuffCategories?.ToList(),
                costList = original.costList?.ToList(),
                researchPrerequisites = original.researchPrerequisites?.ToList(),
                designationCategory = original.designationCategory,
                rotatable = original.rotatable,
                defaultPlacingRot = original.defaultPlacingRot,
                passability = original.passability,
                pathCost = original.pathCost,
                fillPercent = original.fillPercent,
                castEdgeShadows = original.castEdgeShadows,
                staticSunShadowHeight = original.staticSunShadowHeight,
                canOverlapZones = original.canOverlapZones,
                surfaceType = SurfaceType.Item,

                // Copy graphic data
                graphicData = CopyGraphicData(original.graphicData),
                uiIconPath = original.uiIconPath,
                uiIconScale = original.uiIconScale,

                // Copy comps
                comps = CopyComps(original.comps),

                // Copy place workers
                placeWorkers = original.placeWorkers?.ToList(),

                // Copy other properties
                drawerType = original.drawerType,
                drawGUIOverlay = original.drawGUIOverlay,
                minifiedDef = original.minifiedDef,
                thingCategories = original.thingCategories?.ToList(),
            };

            // Setup building properties for storage
            newDef.building = CopyBuildingProperties(original.building, config);

            // Setup inspector tabs
            newDef.inspectorTabs = new List<Type>();
            if (original.inspectorTabs != null)
            {
                newDef.inspectorTabs.AddRange(original.inspectorTabs);
            }
            // Add storage tab if not already present
            if (!newDef.inspectorTabs.Contains(typeof(ITab_Storage)))
            {
                newDef.inspectorTabs.Add(typeof(ITab_Storage));
            }

            // Add mod extension for AdaptiveStorage
            if (newDef.modExtensions == null)
                newDef.modExtensions = new List<DefModExtension>();

            // Create AdaptiveStorage extension via reflection
            Type extensionType = Type.GetType("AdaptiveStorage.Extension, AdaptiveStorage");
            if (extensionType != null)
            {
                DefModExtension extension = (DefModExtension)Activator.CreateInstance(extensionType);
                newDef.modExtensions.Add(extension);
            }

            // Resolve references
            newDef.ResolveReferences();

            return newDef;
        }

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

        private static List<CompProperties> CopyComps(List<CompProperties> original)
        {
            if (original == null) return new List<CompProperties>();

            List<CompProperties> copy = new List<CompProperties>();
            foreach (var comp in original)
            {
                // Deep copy comp properties
                CompProperties copiedComp = CopyCompProperties(comp);
                if (copiedComp != null)
                {
                    copy.Add(copiedComp);
                }
            }
            return copy;
        }

        private static CompProperties CopyCompProperties(CompProperties original)
        {
            if (original == null) return null;

            // Use serialization to deep copy
            try
            {
                string xml = Scribe.saver.DebugOutputFor(original);
                // For now, just return the same type with basic copying
                // In a real implementation, you'd want proper deep copy
                return original;
            }
            catch
            {
                return original;
            }
        }

        private static BuildingProperties CopyBuildingProperties(BuildingProperties original, FurnitureCloneConfigDef config)
        {
            BuildingProperties copy = new BuildingProperties
            {
                // Storage settings
                preventDeteriorationOnTop = true,
                ignoreStoredThingsBeauty = true,
                maxItemsInCell = config.maxItemsInCell,
                blueprintClass = typeof(Blueprint_Storage),
                paintable = original?.paintable ?? true,
                storageGroupTag = "Shelf",

                // Copy other building properties
                buildingTags = original?.buildingTags?.ToList(),
                ai_chillDestination = original?.ai_chillDestination ?? false,
                allowAutoroof = original?.allowAutoroof ?? true,
                canPlaceOverImpassablePlant = original?.canPlaceOverImpassablePlant ?? false,
                canPlaceOverWall = original?.canPlaceOverWall ?? false,
                deconstructible = original?.deconstructible ?? true,
                isEdifice = original?.isEdifice ?? true,
                isInert = original?.isInert ?? false,
                isPlayerEjectable = original?.isPlayerEjectable ?? false,
                isResourceRock = original?.isResourceRock ?? false,
                claimable = original?.claimable ?? true,
                expandHomeArea = original?.expandHomeArea ?? true,
                shipPart = original?.shipPart ?? false,
                wantsHopperAdjacent = original?.wantsHopperAdjacent ?? false,
            };

            // Setup storage settings
            copy.fixedStorageSettings = CreateStorageSettings(config);
            copy.defaultStorageSettings = CreateDefaultStorageSettings(config);

            return copy;
        }

        private static StorageSettings CreateStorageSettings(FurnitureCloneConfigDef config)
        {
            StorageSettings settings = new StorageSettings();
            settings.filter = new ThingFilter();

            // Allow categories
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

            // Disallow specific things
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

        private static StorageSettings CreateDefaultStorageSettings(FurnitureCloneConfigDef config)
        {
            StorageSettings settings = new StorageSettings
            {
                Priority = StoragePriority.Preferred
            };
            settings.filter = new ThingFilter();

            // Default to first category
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
