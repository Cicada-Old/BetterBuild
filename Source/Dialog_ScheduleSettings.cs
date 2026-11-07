using System;
using UnityEngine;
using Verse;

namespace BuildMonitor
{
    /// <summary>
    /// 调度设置弹窗：材料保护期、等待老化期与建造页行高。
    /// 这几项都属于「配一次就不动」，常驻主界面会白占高度，所以收进弹窗。
    /// </summary>
    public class Dialog_ScheduleSettings : Window
    {
        private string rowHeightBuf;

        public override Vector2 InitialSize
        {
            get { return new Vector2(500f, 404f); }
        }

        public Dialog_ScheduleSettings()
        {
            doCloseButton = true;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), "BM_SchedTitle".Translate());
            Text.Font = GameFont.Small;

            float y = 36f;
            GUI.color = new Color(0.72f, 0.78f, 0.84f);
            Widgets.Label(new Rect(0f, y, inRect.width, 40f), "BM_SchedHint".Translate());
            GUI.color = Color.white;
            y += 44f;

            BuildOrderComp comp = BuildOrderComp.Instance;
            if (comp == null)
            {
                Widgets.Label(new Rect(0f, y, inRect.width, 22f), "BM_SchedNoGame".Translate());
                return;
            }

            y = DrawDaysRow(inRect.width, y, "BM_ProtectDays", "BM_ProtectHint", comp.protectDays,
                delegate(float v) { comp.protectDays = v; });
            y += 4f;
            y = DrawDaysRow(inRect.width, y, "BM_AgeDays", "BM_AgeHint", comp.ageDays,
                delegate(float v) { comp.ageDays = v; });
            y += 4f;
            DrawRowHeightRow(inRect.width, y, comp);
        }

        /// <summary>
        /// 行高：直接输入数字。超范围按 clamp 处理（999 → 64），因为「要很大」的本意是明确可预期的；
        /// 非数字或留空则沿用当前值（不写）。重新打开弹窗时输入框显示的是真正生效的值。
        /// </summary>
        private void DrawRowHeightRow(float w, float y, BuildOrderComp comp)
        {
            if (rowHeightBuf == null) rowHeightBuf = comp.rowHeight.ToString();

            Widgets.Label(new Rect(0f, y, w - 120f, 24f), "BM_RowHeight".Translate());
            string edited = Widgets.TextField(new Rect(w - 80f, y + 1f, 60f, 24f), rowHeightBuf);
            if (edited != rowHeightBuf)
            {
                rowHeightBuf = edited;
                int v;
                if (int.TryParse(rowHeightBuf, out v))
                {
                    v = BuildOrderComp.ClampRowHeight(v);
                    if (v != comp.rowHeight) comp.rowHeight = v;
                }
            }
            y += 26f;

            GUI.color = new Color(0.66f, 0.71f, 0.77f);
            float tipH = Text.CalcHeight("BM_RowHeightTip".Translate(
                BuildOrderComp.RowHeightMin, BuildOrderComp.RowHeightMax, BuildOrderComp.RowHeightDefault), w);
            Widgets.Label(new Rect(0f, y, w, tipH), "BM_RowHeightTip".Translate(
                BuildOrderComp.RowHeightMin, BuildOrderComp.RowHeightMax, BuildOrderComp.RowHeightDefault));
            GUI.color = Color.white;
        }

        /// <summary>一行设置：标签 + 值 + 加减按钮 + 下方灰字说明。返回下一行的 y。</summary>
        private static float DrawDaysRow(float w, float y, string labelKey, string hintKey,
            float value, Action<float> setter)
        {
            Widgets.Label(new Rect(0f, y, w - 120f, 24f), labelKey.Translate(value.ToString("0.#")));
            if (Widgets.ButtonText(new Rect(w - 112f, y + 2f, 30f, 20f), "-"))
            {
                setter(Mathf.Max(0f, value - 0.5f));
            }
            if (Widgets.ButtonText(new Rect(w - 44f, y + 2f, 30f, 20f), "+"))
            {
                setter(Mathf.Min(30f, value + 0.5f));
            }
            y += 26f;

            GUI.color = new Color(0.66f, 0.71f, 0.77f);
            Widgets.Label(new Rect(0f, y, w, 38f), hintKey.Translate());
            GUI.color = Color.white;
            return y + 40f;
        }
    }
}
