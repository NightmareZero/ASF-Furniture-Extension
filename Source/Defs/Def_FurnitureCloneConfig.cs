using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AsfFurnitureExt
{
    /// <summary>
    /// 家具克隆配置定义 - 从XML加载
    /// </summary>
    public class Def_FurnitureCloneConfig : Def
    {
        // 原版家具defName
        public string originalDefName;

        // 存储容量（每格最大物品数）
        public int maxItemsInCell = 1;

        // 存储过滤类别（ThingCategoryDef的defName列表）
        public List<string> storageFilterCategories = new List<string>();

        // 禁止存储的物品（ThingDef的defName列表）
        public List<string> storageDisallowedThingDefs = new List<string>();

        // 描述后缀的翻译键
        public string descriptionSuffixKey = "desc_suffix";

        // 是否启用此配置
        public bool enabled = true;

        public override void ResolveReferences()
        {
            base.ResolveReferences();

            // 验证原版家具是否存在
            if (!string.IsNullOrEmpty(originalDefName))
            {
                ThingDef originalDef = DefDatabase<ThingDef>.GetNamed(originalDefName, false);
                if (originalDef == null)
                {
                    Log.Warning($"[ASF Furniture Ext] Def {defName}: Original furniture '{originalDefName}' not found!");
                }
            }
            else
            {
                Log.Error($"[ASF Furniture Ext] Def {defName}: originalDefName is empty!");
            }
        }
    }
}
