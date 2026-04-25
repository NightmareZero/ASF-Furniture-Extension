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
        private static readonly string[] AsfAssemblyNames =
        {
            "AdaptiveStorageFramework",
            "AdaptiveStorage"
        };

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

            // Set mod content pack
            newDef.modContentPack = AsfFurnitureExtMod.Instance.Content;

            // Note: Temporarily disable designator dropdown to test material selection
            // SetupDesignatorDropdown(originalDef, newDef);

            // Use DefGenerator to properly register the def (this calls PostLoad and other necessary initialization)
            DefGenerator.AddImpliedDef<ThingDef>(newDef, false);

            // Also add to BuildableDef database for construction
            if (!DefDatabase<BuildableDef>.AllDefs.Contains(newDef))
            {
                DefDatabase<BuildableDef>.Add(newDef);
            }

            // Register GraphicsDef for AdaptiveStorage
            RegisterGraphicsDef(newDef, config);

            // Re-resolve designation category to update build menu
            if (originalDef.designationCategory != null)
            {
                originalDef.designationCategory.ResolveReferences();
            }

            Log.Message(DefValue.LogMessage("log_cloning_success", newDefName));
        }

        private static void SetupDesignatorDropdown(ThingDef originalDef, ThingDef newDef)
        {
            // Create or use existing dropdown group
            if (originalDef.designatorDropdown == null)
            {
                var dropdown = new DesignatorDropdownGroupDef
                {
                    defName = originalDef.defName + "_ASFS_Group"
                };
                
                // Register the dropdown group def
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

        private static void RegisterGraphicsDef(ThingDef newDef, FurnitureCloneConfigDef config)
        {
            Type graphicsDefType = ResolveAsfType("AdaptiveStorage.GraphicsDef");
            if (graphicsDefType == null)
            {
                Log.Warning("[ASF Furniture Ext] Could not find AdaptiveStorage.GraphicsDef type");
                return;
            }

            try
            {
                var graphicsDef = (Def)Activator.CreateInstance(graphicsDefType);

                graphicsDefType.GetField("defName")?.SetValue(graphicsDef, newDef.defName + "_Graphics");
                graphicsDefType.GetProperty("modContentPack")?.SetValue(graphicsDef, AsfFurnitureExtMod.Instance.Content);

                // Point this GraphicsDef at our cloned ThingDef so Initialize() can
                // auto-create a StorageGraphic from newDef.graphicData and populate
                // GraphicsDef.Database[newDef].
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

                graphicsDefType.GetField("showContainedItems")?.SetValue(graphicsDef, true);

                // CRITICAL: Must use the correct generic type (GraphicsDef, not Def) so the def is
                // registered in DefDatabase<GraphicsDef>. ASF's PlayDataLoadingFinished (postfix on
                // PlayDataLoader.ResetStaticDataPost) clears GraphicsDef.Database and repopulates it
                // by iterating DefDatabase<GraphicsDef>. If we call DefGenerator.AddImpliedDef with
                // a compile-time type of Def, it goes into DefDatabase<Def> instead, and our entry
                // is lost after every save-load cycle (causing the null CurrentGraphic crash).
                var addImpliedDefMethod = typeof(DefGenerator)
                    .GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == "AddImpliedDef" && m.IsGenericMethodDefinition);
                if (addImpliedDefMethod != null)
                {
                    addImpliedDefMethod.MakeGenericMethod(graphicsDefType)
                        .Invoke(null, new object[] { graphicsDef, false });
                }
                else
                {
                    // Fallback: add directly to DefDatabase<GraphicsDef>
                    Log.Warning("[ASF Furniture Ext] DefGenerator.AddImpliedDef not found; using DefDatabase.Add fallback");
                    typeof(DefDatabase<>).MakeGenericType(graphicsDefType)
                        .GetMethod("Add", BindingFlags.Static | BindingFlags.Public, null,
                            new[] { graphicsDefType }, null)
                        ?.Invoke(null, new object[] { graphicsDef });
                }

                // ResolveReferences() MUST be called before Initialize() because
                // Initialize() → UpdateActiveLabelStyle() → ContentsFullyHidden reads
                // allowedFilter, which is only created inside ResolveReferences().
                // Without this, Initialize() throws a NullReferenceException (caught
                // silently), StorageGraphic._worker is never set, CurrentGraphic stays
                // null, and UpdateBuildingGraphicAtIndex crashes at line 397 every frame.
                graphicsDefType.GetMethod("ResolveReferences",
                    BindingFlags.Instance | BindingFlags.Public)?
                    .Invoke(graphicsDef, null);

                graphicsDefType.GetMethod("Initialize",
                    BindingFlags.Instance | BindingFlags.Public)?
                    .Invoke(graphicsDef, null);

                Log.Message($"[ASF Furniture Ext] Registered GraphicsDef for {newDef.defName}");
            }
            catch (Exception ex)
            {
                Log.Error($"[ASF Furniture Ext] Failed to register GraphicsDef: {ex}");
            }
        }

        private static ThingDef CopyThingDef(ThingDef original, FurnitureCloneConfigDef config, string newDefName)
        {
            // Generate label with prefix
            string newLabel = DefValue.LabelPrefix + original.label;

            // Generate description with suffix
            string descriptionSuffix = DefValue.DescSuffix(config.descriptionSuffixKey);
            string newDescription = original.description + descriptionSuffix;

            // Use MakeShallowCopy to copy all fields from original
            ThingDef newDef = MakeShallowCopy(original);
            
            // Override specific properties
            newDef.defName = newDefName;
            newDef.label = newLabel;
            newDef.description = newDescription;
            newDef.thingClass = typeof(AdaptiveStorage.ThingClass);
            newDef.surfaceType = SurfaceType.Item;
            
            // Copy graphic data (needs special handling)
            newDef.graphicData = CopyGraphicData(original.graphicData);
            
            // Copy building properties
            newDef.building = CopyBuildingProperties(original.building, config);

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
            Type extensionType = ResolveAsfType("AdaptiveStorage.Extension");
            if (extensionType != null)
            {
                DefModExtension extension = (DefModExtension)Activator.CreateInstance(extensionType);
                
                // Set labelFormat to Default using the field's actual enum type.
                var labelFormatField = extensionType.GetField("labelFormat");
                if (labelFormatField != null)
                {
                    object defaultLabelFormat = Enum.Parse(labelFormatField.FieldType, "Default");
                    labelFormatField.SetValue(extension, defaultLabelFormat);
                }
                
                newDef.modExtensions.Add(extension);
            }

            // Note: Don't call ResolveReferences() here - DefGenerator.AddImpliedDef will call PostLoad()

            // Generate new Blueprint and Frame for the new ThingDef
            // This must be done after setting thingClass to ensure they use the correct class
            GenerateBlueprintAndFrame(newDef);

            return newDef;
        }

        private static void GenerateBlueprintAndFrame(ThingDef newDef)
        {
            try
            {
                // Get the NewBlueprintDef_Thing method via reflection
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

                // Get the NewFrameDef_Thing method via reflection
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

        private static T MakeShallowCopy<T>(T from) where T : new()
        {
            var to = new T();
            CopyFields(from, to);
            return to;
        }

        private static void CopyFields<T>(T from, T to)
        {
            foreach (var fieldInfo in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                fieldInfo.SetValue(to, fieldInfo.GetValue(from));
            }
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

            // Create a new instance of the same type
            try
            {
                var copy = (CompProperties)Activator.CreateInstance(original.GetType());
                // Copy fields using reflection or manual assignment
                // For now, return the original to avoid issues
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
