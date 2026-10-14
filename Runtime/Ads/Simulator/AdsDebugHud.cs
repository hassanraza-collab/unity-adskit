using System.Text;
using UnityEngine;

namespace NineDTech.AdsKit.Ads
{
    /// <summary>On-screen ads state (slots, cooldowns, banner). Enabled by AdsSettings.showDebugHud in Editor/dev builds.</summary>
    public sealed class AdsDebugHud : MonoBehaviour
    {
        private readonly StringBuilder _sb = new StringBuilder(512);
        private AdsService _service;
        private bool _expanded = true;
        private GUIStyle _style;
        private float _refresh;
        private string _text = "";
        private bool _mrecShown;

        internal static void Create(AdsService service)
        {
            var go = new GameObject("[AdsKit Debug HUD]");
            DontDestroyOnLoad(go);
            go.AddComponent<AdsDebugHud>()._service = service;
        }

        private void Update()
        {
            _refresh -= Time.unscaledDeltaTime;
            if (_refresh > 0f || _service == null) return;
            _refresh = 0.5f;
            _sb.Clear();
            _service.AppendDebugText(_sb);
            _text = _sb.ToString();
        }

        private void OnGUI()
        {
            if (_style == null)
                _style = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = Mathf.Max(11, Screen.height / 70),
                    wordWrap = false
                };
            var size = _style.fontSize * 4;
            if (GUI.Button(new Rect(4, 4, size, size * 0.5f), _expanded ? "Ads -" : "Ads +")) _expanded = !_expanded;
            if (!_expanded) return;
            var content = new GUIContent(_text);
            var h = _style.CalcHeight(content, Screen.width * 0.9f);
            GUI.Box(new Rect(4, 8 + size * 0.5f, Screen.width * 0.9f, h), content, _style);
            if (GUI.Button(new Rect(8 + size, 4, size * 1.6f, size * 0.5f), "Debugger")) Ads.OpenDebugger();
            if (GUI.Button(new Rect(12 + size * 2.6f, 4, size * 1.6f, size * 0.5f), "Resume"))
                _service.SimulateResume(_service.Config?.AppOpenResume?.MinBackgroundSec ?? 30);
            if (GUI.Button(new Rect(16 + size * 4.2f, 4, size * 1.6f, size * 0.5f), "MREC"))
            {
                if (_mrecShown) Ads.HideMrec();
                else Ads.ShowMrec(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
                _mrecShown = !_mrecShown;
            }
        }
    }
}
