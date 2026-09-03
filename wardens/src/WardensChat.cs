// WardensChat.cs. In-game chat between the player and the agent.
//
// A small UI Toolkit panel (bottom-left): message log, one input line, Send. The player's
// messages go into a thread-safe store that the MCP server reads from its listener thread:
// chat_read long-polls WaitForUser, and every tool result carries the not-yet-delivered
// player messages under "chat", so the agent sees what the player typed without asking.
// Agent replies arrive through the `say` tool on the main thread and are appended to the log.
//
// Built the same way as the Timberbot widget (TimberbotPanel): VisualElementInitializer applies
// the game's styles, UILayout.AddAbsoluteItem places it. UI Toolkit text fields already keep
// their key presses away from the game's key bindings; Enter is intercepted to send.

using System;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;
using Timberborn.CoreUI;
using Timberborn.SingletonSystem;
using Timberborn.UILayoutSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wardens
{
    public class WardensChat : ILoadableSingleton
    {
        public class Message
        {
            public long Seq;
            public string From;      // "player" | "agent" | "system"
            public string Text;
            public DateTime TimeUtc;
            public bool Delivered;   // player messages: handed to the agent already

            public JObject ToJson() => new JObject
            {
                ["seq"] = Seq,
                ["from"] = From,
                ["text"] = Text,
                ["time"] = TimeUtc.ToString("o"),
            };
        }

        private const int MaxMessages = 400;
        private readonly UILayout _layout;
        private readonly VisualElementInitializer _veInit;
        private readonly object _lock = new object();
        private readonly List<Message> _messages = new List<Message>();
        private long _seq;

        private VisualElement _root;
        private VisualElement _body;
        private ScrollView _log;
        private NineSliceTextField _input;
        private NineSliceButton _toggle;
        private bool _collapsed;

        public WardensChat(UILayout layout, VisualElementInitializer veInit)
        {
            _layout = layout;
            _veInit = veInit;
        }

        // ---- store (any thread) -------------------------------------------------------

        public Message Add(string from, string text)
        {
            var m = new Message { From = from, Text = text ?? "", TimeUtc = DateTime.UtcNow };
            lock (_lock)
            {
                m.Seq = ++_seq;
                m.Delivered = from != "player";
                _messages.Add(m);
                if (_messages.Count > MaxMessages) _messages.RemoveAt(0);
                Monitor.PulseAll(_lock);
            }
            return m;
        }

        public JArray TakeUndelivered()
        {
            var arr = new JArray();
            lock (_lock)
            {
                foreach (var m in _messages)
                {
                    if (m.From == "player" && !m.Delivered)
                    {
                        m.Delivered = true;
                        arr.Add(m.ToJson());
                    }
                }
            }
            return arr;
        }

        public int UndeliveredCount()
        {
            lock (_lock)
            {
                int n = 0;
                foreach (var m in _messages) if (m.From == "player" && !m.Delivered) n++;
                return n;
            }
        }

        // Blocks the CALLING thread (the MCP listener thread, never the main thread) until a
        // player message is waiting or the timeout passes.
        public JArray WaitForUser(int timeoutMs)
        {
            var deadline = Environment.TickCount + Math.Max(0, timeoutMs);
            lock (_lock)
            {
                while (true)
                {
                    var pending = TakeUndeliveredLocked();
                    if (pending.Count > 0) return pending;
                    int remaining = deadline - Environment.TickCount;
                    if (remaining <= 0) return pending;
                    Monitor.Wait(_lock, remaining);
                }
            }
        }

        private JArray TakeUndeliveredLocked()
        {
            var arr = new JArray();
            foreach (var m in _messages)
            {
                if (m.From == "player" && !m.Delivered)
                {
                    m.Delivered = true;
                    arr.Add(m.ToJson());
                }
            }
            return arr;
        }

        public JArray History(int limit)
        {
            var arr = new JArray();
            lock (_lock)
            {
                int start = Math.Max(0, _messages.Count - Math.Max(1, limit));
                for (int i = start; i < _messages.Count; i++) arr.Add(_messages[i].ToJson());
            }
            return arr;
        }

        // ---- main-thread entry points ----------------------------------------------------

        public void AgentSays(string text)
        {
            var m = Add("agent", text);
            AppendLine(m);
            if (_collapsed) SetCollapsed(false);
        }

        public void SystemSays(string text)
        {
            var m = Add("system", text);
            AppendLine(m);
        }

        // ---- UI -------------------------------------------------------------------------

        public void Load()
        {
            BuildPanel();
            _veInit.InitializeVisualElement(_root);
            _layout.AddAbsoluteItem(_root);
        }

        private void BuildPanel()
        {
            _root = new VisualElement { name = "WardensChat" };
            _root.style.position = Position.Absolute;
            _root.style.left = 8;
            _root.style.bottom = 150;
            _root.style.width = 400;
            _root.style.backgroundColor = new Color(0.19f, 0.14f, 0.22f, 0.85f);
            _root.style.paddingLeft = 6;
            _root.style.paddingRight = 6;
            _root.style.paddingTop = 4;
            _root.style.paddingBottom = 4;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            var title = new Label("WARDENS UPLINK");
            title.AddToClassList("game-text-normal");
            title.AddToClassList("text--yellow");
            header.Add(title);
            _toggle = new NineSliceButton { text = "-" };
            _toggle.AddToClassList("button-game");
            _toggle.AddToClassList("game-text-normal");
            _toggle.style.width = 22;
            _toggle.style.height = 22;
            _toggle.clicked += () => SetCollapsed(!_collapsed);
            header.Add(_toggle);
            _root.Add(header);

            _body = new VisualElement();
            _log = new ScrollView(ScrollViewMode.Vertical);
            _log.style.height = 150;
            _log.style.marginTop = 4;
            _log.style.marginBottom = 4;
            _body.Add(_log);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            _input = new NineSliceTextField();
            _input.AddToClassList("text-field");
            _input.style.flexGrow = 1;
            _input.style.height = 24;
            _input.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            row.Add(_input);
            var send = new NineSliceButton { text = "Send" };
            send.AddToClassList("button-game");
            send.AddToClassList("game-text-normal");
            send.style.height = 24;
            send.style.marginLeft = 4;
            send.clicked += Send;
            row.Add(send);
            _body.Add(row);
            _root.Add(_body);

            var hint = new Label("Type to talk to the agent. Enter sends.");
            hint.AddToClassList("game-text-normal");
            hint.style.opacity = 0.6f;
            hint.style.fontSize = 10;
            _body.Add(hint);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                Send();
                evt.StopPropagation();
            }
        }

        private void Send()
        {
            var text = (_input.value ?? "").Trim();
            if (text.Length == 0) return;
            _input.value = "";
            var m = Add("player", text);
            AppendLine(m);
        }

        private void SetCollapsed(bool collapsed)
        {
            _collapsed = collapsed;
            _body.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            _toggle.text = collapsed ? "+" : "-";
        }

        private void AppendLine(Message m)
        {
            if (_log == null) return;
            string prefix = m.From == "player" ? "You" : m.From == "agent" ? "Agent" : "*";
            var label = new Label($"{prefix}: {m.Text}");
            label.AddToClassList("game-text-normal");
            if (m.From == "player") label.AddToClassList("text--yellow");
            else if (m.From == "agent") label.AddToClassList("text--green");
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 3;
            _log.Add(label);
            while (_log.childCount > 200) _log.RemoveAt(0);
            _log.schedule.Execute(() => _log.scrollOffset = new Vector2(0f, _log.contentContainer.layout.height)).StartingIn(30);
        }
    }
}
