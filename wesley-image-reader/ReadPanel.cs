using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WesleyImageReader
{
    public sealed class ReadPanel : MonoBehaviour
    {
        public string CurrentId;
        internal bool FollowsNativeInspect => entry?.inspect == true;

        GrabbableObject item, initialSlot;
        Transform anchor;
        Vector3 origin;
        Entry entry;
        int page;
        GUIContent body;
        readonly List<string> lines = new List<string>();
        List<string> titleLines;
        float scroll, height, viewport, nextStateCheck, textWidth, lineHeight, paragraphGap, titleLineHeight;
        Vector2Int layoutSize;
        Font font;
        GUIStyle style, heading;

        internal void Show(Entry reading, GrabbableObject held, Transform target)
        {
            item = held;
            initialSlot = Reader.Player.currentlyHeldObjectServer;
            anchor = target;
            origin = Reader.Player.transform.position;
            page = Targets.Page(held);
            Load(reading);
            enabled = true;
        }

        void Load(Entry reading)
        {
            using (var file = Reader.Active.Data.GetEntry(reading.id + ".txt").Open())
            using (var reader = new StreamReader(file))
                body = new GUIContent(reader.ReadToEnd());
            entry = reading;
            CurrentId = reading.id;
            scroll = 0;
            nextStateCheck = 0;
            layoutSize = default;
        }

        void Update()
        {
            try
            {
                var p = Reader.Player;
                if (!Reader.Usable(p) || !anchor || !anchor.gameObject.activeInHierarchy ||
                    initialSlot != p.currentlyHeldObjectServer || (FollowsNativeInspect && !p.IsInspectingItem) ||
                    (!item && (origin - p.transform.position).sqrMagnitude > 16f))
                {
                    Close();
                    return;
                }

                if (item && Targets.Page(item) != page)
                {
                    var current = Targets.Held(item);
                    if (current == null || current.native) { Close(); return; }
                    page = Targets.Page(item);
                    Load(current);
                }
                else if (!item && Time.unscaledTime >= nextStateCheck)
                {
                    var current = Targets.Displayed(anchor, entry);
                    if (current == null || current.native || current.inspect) { Close(); return; }
                    if (current != entry) Load(current);
                    nextStateCheck = Time.unscaledTime + .15f;
                }

                var keyboard = Keyboard.current;
                if (keyboard == null) return;
                float step = Time.unscaledDeltaTime * viewport;
                if (keyboard.upArrowKey.isPressed) scroll -= step;
                if (keyboard.downArrowKey.isPressed) scroll += step;
                if (keyboard.pageDownKey.wasPressedThisFrame) scroll += viewport * .85f;
                if (keyboard.pageUpKey.wasPressedThisFrame) scroll -= viewport * .85f;
                scroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, height - viewport));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WesleyImageReader] " + ex.Message);
                Close();
            }
        }

        internal void Close()
        {
            enabled = false;
            CurrentId = null;
            entry = null;
            body = null;
            anchor = null;
            item = null;
            initialSlot = null;
        }

        void OnDestroy() { if (font) Destroy(font); }

        void InitStyles()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 20);
            if ((!font || !font.HasCharacter('中')) && Chainloader.PluginInfos.TryGetValue("Aueser.LCChineseProject", out var chinese))
            {
                string bundledFont = Path.Combine(Path.GetDirectoryName(chinese.Location), "fonts", "NotoSansSC-VF.ttf");
                if (File.Exists(bundledFont))
                {
                    if (font) Destroy(font);
                    font = new Font(bundledFont);
                }
            }
            style = new GUIStyle(GUI.skin.label)
            {
                font = font, wordWrap = false, richText = false, alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(), clipping = TextClipping.Clip
            };
            style.normal.textColor = new Color(.94f, .96f, .98f);
            heading = new GUIStyle(style) { fontStyle = FontStyle.Bold };
            heading.normal.textColor = Color.white;
        }

        void Measure(float scale)
        {
            layoutSize = new Vector2Int(Screen.width, Screen.height);
            style.fontSize = Mathf.RoundToInt(20 * scale);
            heading.fontSize = Mathf.RoundToInt(22 * scale);
            lineHeight = Mathf.Ceil(Mathf.Max(style.lineHeight, style.CalcSize(new GUIContent("国Ag")).y)) + 4 * scale;
            paragraphGap = 9 * scale;

            var paragraphs = body.text.Split('\n');
            var min = 248 * scale;
            var max = Mathf.Min(Screen.width * (FollowsNativeInspect ? .39f : .73f), 634 * scale);
            var natural = string.IsNullOrEmpty(entry.title) ? min : heading.CalcSize(new GUIContent(entry.title)).x;
            foreach (var paragraph in paragraphs)
                natural = Mathf.Max(natural, style.CalcSize(new GUIContent(paragraph)).x);
            textWidth = Mathf.Min(max, Mathf.Max(min, Mathf.Ceil(natural) + 4 * scale));
            titleLines = LineBreaks.Wrap(entry.title ?? string.Empty, textWidth - 2 * scale,
                value => heading.CalcSize(new GUIContent(value)).x);
            titleLineHeight = Mathf.Ceil(heading.CalcSize(new GUIContent("国Ag")).y) + 2 * scale;
            lines.Clear();
            height = 0;
            foreach (var paragraph in paragraphs)
            {
                if (string.IsNullOrWhiteSpace(paragraph))
                {
                    lines.Add(string.Empty);
                    height += paragraphGap;
                    continue;
                }
                var wrapped = LineBreaks.Wrap(paragraph, textWidth - 2 * scale, value => style.CalcSize(new GUIContent(value)).x);
                lines.AddRange(wrapped);
                height += wrapped.Count * lineHeight;
            }
            height += 3 * scale;
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || body == null) return;
            if (style == null) InitStyles();
            float scale = Mathf.Clamp(Screen.height / 860f, .86f, 1.75f);
            if (layoutSize.x != Screen.width || layoutSize.y != Screen.height) Measure(scale);

            float pad = 18 * scale;
            float titleHeight = titleLines.Count == 0 ? 0 : titleLines.Count * titleLineHeight + 7 * scale;
            viewport = Mathf.Min(height, Mathf.Max(lineHeight, Screen.height * .74f - titleHeight - pad * 2));
            float maxScroll = height - viewport;
            scroll = Mathf.Clamp(scroll, 0, maxScroll);
            float width = textWidth + pad * 2;
            float outerHeight = titleHeight + viewport + pad * 2;
            float x = FollowsNativeInspect ? Screen.width * .07f : (Screen.width - width) / 2;
            float y = FollowsNativeInspect ? Screen.height * .65f - outerHeight / 2 : (Screen.height - outerHeight) / 2;
            var area = new Rect(x, y, width, outerHeight);
            var old = GUI.color;
            try
            {
                GUI.color = new Color(.035f, .045f, .055f, .80f);
                GUI.DrawTexture(area, Texture2D.whiteTexture);
                GUI.color = Color.white;
                for (int i = 0; i < titleLines.Count; i++)
                    GUI.Label(new Rect(x + pad, y + pad + i * titleLineHeight, textWidth, titleLineHeight), titleLines[i], heading);
                var content = new Rect(x + pad, y + pad + titleHeight, textWidth, viewport);
                GUI.BeginGroup(content);
                try
                {
                    float row = -scroll;
                    foreach (var line in lines)
                    {
                        if (line.Length == 0) { row += paragraphGap; continue; }
                        if (row >= viewport) break;
                        if (row + lineHeight > 0) GUI.Label(new Rect(0, row, textWidth, lineHeight), line, style);
                        row += lineHeight;
                    }
                }
                finally { GUI.EndGroup(); }
                if (maxScroll > .1f)
                {
                    float grip = Mathf.Max(12 * scale, viewport * viewport / height);
                    float offset = (viewport - grip) * scroll / maxScroll;
                    GUI.color = new Color(.53f, .62f, .7f, .7f);
                    GUI.DrawTexture(new Rect(content.xMax + 6 * scale, content.y + offset, 2 * scale, grip), Texture2D.whiteTexture);
                }
            }
            finally { GUI.color = old; }
        }
    }
}
