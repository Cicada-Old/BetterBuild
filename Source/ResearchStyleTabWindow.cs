using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ResearchStyleUI
{
    /// <summary>
    /// 与原版「研究」一级页签同构的窗口骨架：底部主菜单页签、左右两栏、
    /// 顶部 TabDrawer 分页/翻页、右上 QuickSearchWidget。
    /// 只复刻研究菜单的外观与交互，内容由子类填充。
    /// </summary>
    public abstract class ResearchStyleTabWindow : MainTabWindow
    {
        protected readonly List<TabRecord> tabs = new List<TabRecord>();
        protected int curTab;
        protected readonly QuickSearchWidget search = new QuickSearchWidget();

        protected abstract int TabCount { get; }
        protected abstract string TabLabel(int index);
        protected abstract void DrawLeftPane(Rect rect);
        protected abstract void DrawTabContents(Rect contentRect, int tab);

        protected virtual float LeftAreaWidthPercent
        {
            get { return 0.22f; }
        }

        protected virtual float LeftAreaMinWidth
        {
            get { return 270f; }
        }

        protected virtual bool ShowSearch
        {
            get { return true; }
        }

        public override Vector2 RequestedTabSize
        {
            // 高度按屏幕一半给，不再写死 684：写死的高度在小屏/高 DPI 下会把窗口顶出屏幕。
            // 400 的下限保证表格与右栏仍留有可视高度（SplitPane 折行时右栏高度的上限也正是 400）。
            // 宽度保持 1010。
            get { return new Vector2(1010f, Mathf.Max(400f, UI.screenHeight * 0.5f)); }
        }

        protected bool MatchesSearch(string label)
        {
            return search.filter.Matches(label);
        }

        protected bool SearchActive
        {
            get { return search.filter.Active; }
        }

        public override void PostOpen()
        {
            base.PostOpen();
            tabs.Clear();
            for (int i = 0; i < TabCount; i++)
            {
                int idx = i;
                tabs.Add(new TabRecord(TabLabel(i), delegate { SelectTab(idx); }, () => curTab == idx));
            }
            if (curTab < 0 || curTab >= TabCount) curTab = 0;
            search.Reset();
            OnOpened();
        }

        protected virtual void OnOpened()
        {
        }

        protected virtual void OnTabChanged()
        {
        }

        protected void SelectTab(int index)
        {
            if (index < 0 || index >= TabCount || index == curTab) return;
            curTab = index;
            search.Reset();
            OnTabChanged();
        }

        public override void DoWindowContents(Rect inRect)
        {
            windowRect.width = UI.screenWidth;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            float leftWidth = Mathf.Max(LeftAreaMinWidth, inRect.width * LeftAreaWidthPercent);
            Rect leftRect = new Rect(0f, 0f, leftWidth, inRect.height);
            Rect searchRect = new Rect(inRect.xMax - 200f, 0f, 200f, 24f);
            Rect rightRect = new Rect(leftRect.xMax + 10f, 0f, inRect.width - leftWidth - 10f, inRect.height);

            if (ShowSearch) search.OnGUI(searchRect);

            Widgets.BeginGroup(leftRect);
            DrawLeftPane(leftRect);
            Widgets.EndGroup();

            DrawRightPane(rightRect, searchRect.x - 4f);
        }

        private void DrawRightPane(Rect rightRect, float maxTabX)
        {
            Rect baseRect = rightRect;
            baseRect.xMax = maxTabX;
            rightRect.yMin += TabDrawer.GetOverflowTabHeight(baseRect, tabs, 100f, 200f);
            Widgets.DrawMenuSection(rightRect);
            TabDrawer.DrawTabsOverflow(baseRect, tabs, 100f, 200f);

            Rect contentRect = rightRect.ContractedBy(1f);
            DrawTabContents(contentRect, curTab);
        }
    }
}
