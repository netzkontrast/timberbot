// WardensTaskPanel.cs. The level's tasks on screen, and the level-end card.
//
// It renders into the Wardens' one window (WardensChat.TaskSlot, above the log) and sets that window's
// header to the level and its progress; the window's collapse folds it with the log. It lists
// every task of the level (WardensLevelTasks): done ones ticked, the live ones (at most two; a task
// goes live when the tasks it waits for are done) with their instruction and a progress line per
// check, the rest dimmed. When the last task is done it becomes the level-end
// card: Continue saves this colony and starts the next level; Stay collapses it, and the header keeps
// the card one click away. Refreshed every half second, rebuilt only when what it shows changed.

using System;
using System.Text;
using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.SingletonSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wardens
{
    public class WardensTaskPanel : ILoadableSingleton, IUpdatableSingleton
    {
        private const float RefreshSeconds = 0.5f;
        private readonly WardensChat _window;
        private readonly WardensLevelTasks _tasks;
        private readonly ILoc _loc;

        private VisualElement _body;
        private bool _collapsed;       // the end card folded by Stay
        private string _shown = "";
        private float _nextRefresh;

        public WardensTaskPanel(WardensChat window, WardensLevelTasks tasks, ILoc loc)
        {
            _window = window;
            _tasks = tasks;
            _loc = loc;
        }

        public void Load()
        {
            if (!_tasks.Active) return;
            _body = _window.TaskSlot;
            _tasks.LevelComplete += () => { _collapsed = false; _shown = ""; _window.Expand(); Refresh(); };
            Refresh();
        }

        public void UpdateSingleton()
        {
            if (_body == null || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            try { Refresh(); }
            catch (Exception ex) { Debug.LogWarning("[Wardens] task panel: " + ex.Message); }
        }

        private void Refresh()
        {
            var level = _tasks.Level;
            if (level == null) return;
            var live = _tasks.Live;

            // What the panel would show, as one string: rebuild only when it differs.
            var sig = new StringBuilder();
            sig.Append(_collapsed).Append('|').Append(_tasks.Complete).Append('|').Append(_tasks.DoneCount);
            foreach (var task in live)
            {
                sig.Append('|').Append(task.Id);
                foreach (var c in task.Checks) sig.Append('|').Append(_tasks.Describe(c));
            }
            if (sig.ToString() == _shown) return;
            _shown = sig.ToString();

            _window.SetHeader(_tasks.Complete
                ? _loc.T("Wardens.Tasks.Complete", level.Id, level.Title)
                : _loc.T("Wardens.Tasks.Header", level.Id, level.Title),
                $"{_tasks.DoneCount}/{_tasks.Tasks.Count}");
            _body.Clear();

            if (_tasks.Complete)
            {
                if (_collapsed) BuildFolded();
                else BuildCard(level);
                return;
            }
            foreach (var task in _tasks.Tasks)
            {
                bool done = _tasks.IsDone(task);
                bool now = live.Contains(task);
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

        // Stay folded the card: one button brings it back (the header already says the level is complete).
        private void BuildFolded()
        {
            var show = new NineSliceButton { text = "+" };
            show.AddToClassList("button-game");
            show.AddToClassList("game-text-normal");
            show.style.width = 22;
            show.style.height = 22;
            show.clicked += () => { _collapsed = false; _shown = ""; Refresh(); };
            _body.Add(show);
        }

        private static Label Text(string s)
        {
            var label = new Label(s);
            label.AddToClassList("game-text-normal");
            return label;
        }
    }
}
