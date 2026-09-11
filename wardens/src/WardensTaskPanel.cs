// WardensTaskPanel.cs. The level's tasks on screen, and the level-end card.
//
// A small UI Toolkit panel (top-left, under the goods bar), built like WARDENS UPLINK (WardensChat.cs):
// VisualElementInitializer applies the game's styles, UILayout.AddAbsoluteItem places it. It lists
// every task of the level (WardensLevelTasks): done ones ticked, the current one with its instruction
// and a progress line per check, the rest dimmed. When the last task is done it becomes the level-end
// card: Continue saves this colony and starts the next level; Stay collapses it, and the header keeps
// the card one click away. Refreshed every half second, rebuilt only when what it shows changed.

using System;
using System.Text;
using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.SingletonSystem;
using Timberborn.UILayoutSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wardens
{
    public class WardensTaskPanel : ILoadableSingleton, IUpdatableSingleton
    {
        private const float RefreshSeconds = 0.5f;
        private readonly UILayout _layout;
        private readonly VisualElementInitializer _veInit;
        private readonly WardensLevelTasks _tasks;
        private readonly ILoc _loc;

        private VisualElement _root;
        private Label _title;
        private Label _count;
        private NineSliceButton _toggle;
        private VisualElement _body;
        private bool _collapsed;
        private string _shown = "";
        private float _nextRefresh;

        public WardensTaskPanel(UILayout layout, VisualElementInitializer veInit, WardensLevelTasks tasks, ILoc loc)
        {
            _layout = layout;
            _veInit = veInit;
            _tasks = tasks;
            _loc = loc;
        }

        public void Load()
        {
            if (!_tasks.Active) return;
            Build();
            _veInit.InitializeVisualElement(_root);
            _layout.AddAbsoluteItem(_root);
            _tasks.LevelComplete += () => { _collapsed = false; _shown = ""; Refresh(); };
            Refresh();
        }

        public void UpdateSingleton()
        {
            if (_root == null || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            try { Refresh(); }
            catch (Exception ex) { Debug.LogWarning("[Wardens] task panel: " + ex.Message); }
        }

        private void Build()
        {
            _root = new VisualElement { name = "WardensTasks" };
            // Under the goods bar, right of the settlement box: the right side is where the game opens
            // a selected building's panel, and it covered the list there (seen 2026-09-11).
            _root.style.position = Position.Absolute;
            _root.style.left = 290;
            _root.style.top = 84;
            _root.style.width = 360;
            _root.style.backgroundColor = new Color(0.19f, 0.14f, 0.22f, 0.85f);
            _root.style.paddingLeft = 8;
            _root.style.paddingRight = 6;
            _root.style.paddingTop = 4;
            _root.style.paddingBottom = 6;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            _title = new Label();
            _title.AddToClassList("game-text-normal");
            _title.AddToClassList("text--yellow");
            _title.style.flexGrow = 1;
            header.Add(_title);
            _count = new Label();
            _count.AddToClassList("game-text-normal");
            _count.style.marginRight = 6;
            header.Add(_count);
            _toggle = new NineSliceButton { text = "-" };
            _toggle.AddToClassList("button-game");
            _toggle.AddToClassList("game-text-normal");
            _toggle.style.width = 22;
            _toggle.style.height = 22;
            _toggle.clicked += () => { _collapsed = !_collapsed; _shown = ""; Refresh(); };
            header.Add(_toggle);
            _root.Add(header);

            _body = new VisualElement();
            _body.style.marginTop = 4;
            _root.Add(_body);
        }

        private void Refresh()
        {
            var level = _tasks.Level;
            if (level == null) return;
            var current = _tasks.Current;

            // What the panel would show, as one string: rebuild only when it differs.
            var sig = new StringBuilder();
            sig.Append(_collapsed).Append('|').Append(_tasks.Complete).Append('|').Append(_tasks.DoneCount);
            if (current != null) foreach (var c in current.Checks) sig.Append('|').Append(_tasks.Describe(c));
            if (sig.ToString() == _shown) return;
            _shown = sig.ToString();

            _title.text = _tasks.Complete
                ? _loc.T("Wardens.Tasks.Complete", level.Id, level.Title)
                : _loc.T("Wardens.Tasks.Header", level.Id, level.Title);
            _count.text = $"{_tasks.DoneCount}/{_tasks.Tasks.Count}";
            _toggle.text = _collapsed ? "+" : "-";
            _body.Clear();
            _body.style.display = _collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            if (_collapsed) return;

            if (_tasks.Complete)
            {
                BuildCard(level);
                return;
            }
            foreach (var task in _tasks.Tasks)
            {
                bool done = _tasks.IsDone(task);
                bool now = task == current;
                var row = Text((done ? "[x] " : now ? "> " : "[ ] ") + _loc.T(task.Title));
                if (done) row.AddToClassList("text--green");
                else if (now) row.AddToClassList("text--yellow");
                else row.style.opacity = 0.55f;
                _body.Add(row);
                if (!now) continue;
                var text = Text(_loc.T(task.Text));
                text.style.marginLeft = 14;
                text.style.whiteSpace = WhiteSpace.Normal;
                _body.Add(text);
                foreach (var check in task.Checks)
                {
                    var line = Text(_tasks.Describe(check));
                    line.style.marginLeft = 14;
                    if (_tasks.Have(check) >= check.Count) line.AddToClassList("text--green");
                    _body.Add(line);
                }
            }
        }

        private void BuildCard(WardensLevel level)
        {
            var next = _tasks.NextLevel;
            bool canGo = next != null && next.Shipped;
            var text = Text(canGo
                ? _loc.T("Wardens.Tasks.CompleteText", level.Title, next.Id, next.Title)
                : _loc.T("Wardens.Tasks.LastText", level.Title));
            text.style.whiteSpace = WhiteSpace.Normal;
            _body.Add(text);
            if (!canGo) return;
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 6;
            var go = new NineSliceButton { text = _loc.T("Wardens.Tasks.Continue", next.Id, next.Title) };
            go.AddToClassList("button-game");
            go.AddToClassList("game-text-normal");
            go.style.flexGrow = 1;
            go.style.height = 28;
            go.clicked += () => _tasks.StartNextLevel();
            row.Add(go);
            var stay = new NineSliceButton { text = _loc.T("Wardens.Tasks.Stay") };
            stay.AddToClassList("button-game");
            stay.AddToClassList("game-text-normal");
            stay.style.height = 28;
            stay.style.marginLeft = 6;
            stay.clicked += () => { _collapsed = true; _shown = ""; Refresh(); };
            row.Add(stay);
            _body.Add(row);
        }

        private static Label Text(string s)
        {
            var label = new Label(s);
            label.AddToClassList("game-text-normal");
            return label;
        }
    }
}
