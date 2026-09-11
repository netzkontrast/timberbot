// WardensGateFragment.cs. The Gate's panel: which district it links, what arrives, and the buttons.
//
// An IEntityPanelFragment (Timberborn.EntityPanelSystem, 1.1.2.4): the panel calls ShowFragment on
// every fragment for every selection, so this one shows itself only for a WardensMapGate, and
// UpdateFragment runs each frame while something is selected. Built in code with the entity panel's
// own style classes (entity-sub-panel, entity-panel__text, entity-fragment__button), which the
// panel's root style sheets supply. The vanilla inventory fragment (SimpleOutputInventoryFragmentEnabler,
// decorated in WardensConfigurator) shows what is waiting for the haulers.

using System.Collections.Generic;
using System.Text;
using Bindito.Core;
using Timberborn.BaseComponentSystem;
using Timberborn.EntityPanelSystem;
using Timberborn.Goods;
using Timberborn.Localization;
using UnityEngine.UIElements;

namespace Wardens
{
    public class WardensMapGateFragment : IEntityPanelFragment
    {
        private readonly ILoc _loc;
        private readonly IGoodService _goodService;
        private VisualElement _root;
        private Label _text;
        private Button _next;
        private Button _unlink;
        private WardensMapGate _gate;
        private string _shown;

        public WardensMapGateFragment(ILoc loc, IGoodService goodService)
        {
            _loc = loc;
            _goodService = goodService;
        }

        public VisualElement InitializeFragment()
        {
            _root = new VisualElement();
            _root.AddToClassList("entity-sub-panel");
            _root.AddToClassList("bg-sub-box--green");
            _text = new Label();
            _text.AddToClassList("entity-panel__text");
            _text.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(_text);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            _next = MakeButton(_loc.T("Wardens.Gate.Next"), () => _gate?.LinkNext());
            _unlink = MakeButton(_loc.T("Wardens.Gate.Unlink"), () => _gate?.Unlink());
            buttons.Add(_next);
            buttons.Add(_unlink);
            _root.Add(buttons);

            _root.style.display = DisplayStyle.None;
            return _root;
        }

        private static Button MakeButton(string text, System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("entity-panel__text");
            button.AddToClassList("entity-fragment__button");
            button.style.flexGrow = 1;
            return button;
        }

        public void ShowFragment(BaseComponent entity)
        {
            _gate = entity != null ? entity.GetComponent<WardensMapGate>() : null;
            _shown = null;
            _root.style.display = _gate != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void ClearFragment()
        {
            _gate = null;
            _root.style.display = DisplayStyle.None;
        }

        public void UpdateFragment()
        {
            if (_gate == null) return;
            var link = _gate.Link;
            var linkable = _gate.Linkable();
            string text;
            if (link != null)
            {
                text = _loc.T("Wardens.Gate.Linked", link.District, link.Settlement) + "\n" +
                       (link.Exports.Count > 0 ? _loc.T("Wardens.Gate.Rates", Rates(link.Exports)) : _loc.T("Wardens.Gate.NoSurplus"));
            }
            else
            {
                text = linkable.Count > 0 ? _loc.T("Wardens.Gate.Unlinked", linkable.Count) : _loc.T("Wardens.Gate.None");
            }
            if (text != _shown)
            {
                _text.text = text;
                _shown = text;
            }
            _next.SetEnabled(linkable.Count > 0 && !(linkable.Count == 1 && link != null));
            _unlink.style.display = link != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private string Rates(Dictionary<string, float> exports)
        {
            var sb = new StringBuilder();
            foreach (var pair in exports)
            {
                if (sb.Length > 0) sb.Append(", ");
                var good = _goodService.GetGoodOrNull(pair.Key);
                sb.Append(good != null ? good.DisplayName.Value : pair.Key).Append(' ').Append(pair.Value.ToString("0.#"));
            }
            return sb.ToString();
        }
    }

    public class WardensGatePanelModuleProvider : IProvider<EntityPanelModule>
    {
        private readonly WardensMapGateFragment _fragment;

        public WardensGatePanelModuleProvider(WardensMapGateFragment fragment)
        {
            _fragment = fragment;
        }

        public EntityPanelModule Get()
        {
            var builder = new EntityPanelModule.Builder();
            builder.AddMiddleFragment(_fragment);
            return builder.Build();
        }
    }
}
