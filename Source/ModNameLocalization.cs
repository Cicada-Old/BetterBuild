using System;
using HarmonyLib;
using Verse;

namespace BuildMonitor
{
    /// <summary>
    /// 英文界面下 Mod 列表里的本 mod 名字（T-083）。
    ///
    /// 为什么要打补丁：原版 `Verse.ModMetaData.Init()` 只读一次 About.xml 的 `name`，
    /// Name 属性直接返回那个值（同文件 L432-443）；RimWorld 没有 About 的语言层，DefInjected 只作用于 Def，管不到它。
    /// 所以切到英文时，DefInjected/MainButtonDef 那条能让底部按钮变成 Build，Mod 列表却还是「更好的建造」。
    ///
    /// 只改显示：packageId 与 About.xml 都不动——存档、依赖、冲突识别全按 packageId 走，Name 只影响界面文字。
    /// Description 是长文，翻译属内容工作，本轮不碰。
    /// </summary>
    [HarmonyPatch(typeof(ModMetaData), nameof(ModMetaData.Name), MethodType.Getter)]
    public static class Patch_ModName
    {
        // 与 Keyed 里窗口标题用的英文名保持一致
        private const string EnglishName = "Better Build";

        private const string SelfPackageId = "Cicada.BetterBuild";

        private static void Postfix(ModMetaData __instance, ref string __result)
        {
            if (!string.Equals(__instance.PackageId, SelfPackageId, StringComparison.OrdinalIgnoreCase)) return;

            // 启动早期语言还没选出来时按原值显示，不猜
            LoadedLanguage lang = LanguageDatabase.activeLanguage;
            if (lang == null) return;

            // 中文语言目录名形如「ChineseSimplified (简体中文)」，前缀判定足够
            if (lang.folderName != null && lang.folderName.StartsWith("ChineseSimplified")) return;

            __result = EnglishName;
        }
    }
}
