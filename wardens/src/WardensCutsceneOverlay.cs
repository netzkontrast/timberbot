// WardensCutsceneOverlay.cs. Letterbox, caption, step dots, Skip and Continue.
//
// The mockup is design/wardens-ui/ColdBoot.dc.html: two black bars, one caption above the bottom
// bar under a cyan rule, a dot per shot, Skip at the top right. Built the way WardensChat and
// TimberbotPanel are built (UI Toolkit, VisualElementInitializer for the game's styles,
// UILayout.AddAbsoluteItem; TimberbotPanel's modal overlay is the precedent for a root with zero
// insets). The root and the bars ignore pointer events so the game's own panels (the tutorial cards
// on the right) stay visible and clickable while a scene plays; only the two buttons pick.
// The runner (WardensCutscenes) owns the state and calls in; this class only draws and reports
// clicks. Text alignment goes through the game's `text--centered` class rather than
// style.unityTextAlign, whose TextAnchor type lives in a Unity module the csproj does not reference.

using System;
using System.Collections.Generic;
using Timberborn.CoreUI;
using Timberborn.SingletonSystem;
using Timberborn.UILayoutSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wardens
{
    public class WardensCutsceneOverlay : ILoadableSingleton
    {
        public const float BarPercent = 12f;
        private const int CaptionMaxWidth = 760;
        private const int CaptionFontSize = 26;
        private static readonly Color Cyan = new Color(0f, 0.9f, 1f, 1f);          // the Wardens' data-light accent (WardensPointer)
        private static readonly Color Bar = new Color(0f, 0f, 0f, 1f);
        private static readonly Color Clear = new Color(0f, 0f, 0f, 0f);
        private static readonly Color CaptionColor = new Color(0.88f, 0.88f, 0.88f, 1f);

        private readonly UILayout _layout;
        private readonly VisualElementInitializer _veInit;

        private VisualElement _root;
        private VisualElement _top;
        private VisualElement _bottom;
        private VisualElement _stack;
        private Label _caption;
        private VisualElement _dots;
        private NineSliceButton _continue;
        private NineSliceButton _skip;
        private readonly List<VisualElement> _dotList = new List<VisualElement>();

        public event Action SkipClicked;
        public event Action ContinueClicked;
        public bool Visible { get; private set; }

        public WardensCutsceneOverlay(UILayout layout, VisualElementInitializer veInit)
        {
            _layout = layout;
            _veInit = veInit;
        }

        public void Load()
        {
            Build();
            _veInit.InitializeVisualElement(_root);
            _layout.AddAbsoluteItem(_root);
            Hide();
        }

        // ---- the runner's entry points (main thread) ------------------------------------------

        public void Show(bool letterbox, bool skippable, int shots)
        {
            _top.style.display = letterbox ? DisplayStyle.Flex : DisplayStyle.None;
            _bottom.style.display = letterbox ? DisplayStyle.Flex : DisplayStyle.None;
            _skip.style.display = skippable ? DisplayStyle.Flex : DisplayStyle.None;
            _dots.Clear();
            _dotList.Clear();
            for (int i = 0; i < shots; i++)
            {
                var dot = new VisualElement();
                dot.style.width = 8;
                dot.style.height = 8;
                dot.style.marginLeft = 4;
                dot.style.marginRight = 4;
                dot.style.borderTopLeftRadius = 4;
                dot.style.borderTopRightRadius = 4;
                dot.style.borderBottomLeftRadius = 4;
                dot.style.borderBottomRightRadius = 4;
                dot.pickingMode = PickingMode.Ignore;
                StyleDot(dot, false);
                _dots.Add(dot);
                _dotList.Add(dot);
            }
            _dots.style.display = shots > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            _continue.style.display = DisplayStyle.None;
            _caption.text = "";
            _root.style.display = DisplayStyle.Flex;
            Visible = true;
            if (_root.parent != null) _root.BringToFront();
        }

        public void SetShot(int index, string caption)
        {
            _caption.text = caption ?? "";
            _caption.style.display = string.IsNullOrEmpty(caption) ? DisplayStyle.None : DisplayStyle.Flex;
            for (int i = 0; i < _dotList.Count; i++) StyleDot(_dotList[i], i <= index);
            _continue.style.display = DisplayStyle.None;
        }

        public void SetWaitingForContinue(bool waiting)
        {
            _continue.style.display = waiting ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Hide()
        {
            _root.style.display = DisplayStyle.None;
            Visible = false;
        }

        // ---- UI -------------------------------------------------------------------------------

        private void Build()
        {
            _root = new VisualElement { name = "WardensCutscene" };
            _root.style.position = Position.Absolute;
            _root.style.left = 0;
            _root.style.top = 0;
            _root.style.right = 0;
            _root.style.bottom = 0;
            _root.pickingMode = PickingMode.Ignore;

            _top = MakeBar();
            _top.style.top = 0;
            _root.Add(_top);
            _bottom = MakeBar();
            _bottom.style.bottom = 0;
            _root.Add(_bottom);

            // The caption column sits just above the bottom bar: rule, caption, dots, Continue.
            _stack = new VisualElement();
            _stack.style.position = Position.Absolute;
            _stack.style.left = 0;
            _stack.style.right = 0;
            _stack.style.bottom = Length.Percent(BarPercent + 2f);
            _stack.style.flexDirection = FlexDirection.Column;
            _stack.style.alignItems = Align.Center;
            _stack.pickingMode = PickingMode.Ignore;
            _root.Add(_stack);

            var rule = new VisualElement();
            rule.style.width = 48;
            rule.style.height = 2;
            rule.style.backgroundColor = Cyan;
            rule.style.marginBottom = 12;
            rule.pickingMode = PickingMode.Ignore;
            _stack.Add(rule);

            _caption = new Label("");
            _caption.AddToClassList("game-text-normal");
            _caption.AddToClassList("text--centered");
            _caption.style.fontSize = CaptionFontSize;
            _caption.style.color = CaptionColor;
            _caption.style.whiteSpace = WhiteSpace.Normal;
            _caption.style.maxWidth = CaptionMaxWidth;
            _caption.style.paddingLeft = 16;
            _caption.style.paddingRight = 16;
            _caption.pickingMode = PickingMode.Ignore;
            _stack.Add(_caption);

            _dots = new VisualElement();
            _dots.style.flexDirection = FlexDirection.Row;
            _dots.style.marginTop = 14;
            _dots.pickingMode = PickingMode.Ignore;
            _stack.Add(_dots);

            _continue = new NineSliceButton { text = "Continue" };
            _continue.AddToClassList("button-game");
            _continue.AddToClassList("game-text-normal");
            _continue.style.marginTop = 12;
            _continue.style.height = 28;
            _continue.style.paddingLeft = 14;
            _continue.style.paddingRight = 14;
            _continue.clicked += () => ContinueClicked?.Invoke();
            _stack.Add(_continue);

            _skip = new NineSliceButton { text = "Skip" };
            _skip.AddToClassList("button-game");
            _skip.AddToClassList("game-text-normal");
            _skip.style.position = Position.Absolute;
            _skip.style.right = 28;
            _skip.style.top = Length.Percent(BarPercent + 1.5f);
            _skip.style.height = 24;
            _skip.style.paddingLeft = 12;
            _skip.style.paddingRight = 12;
            _skip.clicked += () => SkipClicked?.Invoke();
            _root.Add(_skip);
        }

        private static VisualElement MakeBar()
        {
            var bar = new VisualElement();
            bar.style.position = Position.Absolute;
            bar.style.left = 0;
            bar.style.right = 0;
            bar.style.height = Length.Percent(BarPercent);
            bar.style.backgroundColor = Bar;
            bar.pickingMode = PickingMode.Ignore;
            return bar;
        }

        // A filled dot for the shots played (the current one included), an outline for the rest.
        private static void StyleDot(VisualElement dot, bool filled)
        {
            dot.style.backgroundColor = filled ? Cyan : Clear;
            float width = filled ? 0f : 1.5f;
            dot.style.borderTopWidth = width;
            dot.style.borderRightWidth = width;
            dot.style.borderBottomWidth = width;
            dot.style.borderLeftWidth = width;
            dot.style.borderTopColor = Cyan;
            dot.style.borderRightColor = Cyan;
            dot.style.borderBottomColor = Cyan;
            dot.style.borderLeftColor = Cyan;
        }
    }
}
