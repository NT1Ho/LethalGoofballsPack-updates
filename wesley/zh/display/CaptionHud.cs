using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LethalGoofballsPack.WesleyZh
{
    internal sealed class CaptionHud : IDisposable
    {
        private const string DialogueColor = "<color=#FFECB1>";
        private delegate bool SetPrimaryFont(TMP_Text text, string content);
        private delegate void ApplyFont(TMP_Text text, string content, bool eastAsian);
        private static readonly SetPrimaryFont UseChineseFont =
            AccessTools.MethodDelegate<SetPrimaryFont>(AccessTools.Method("V81TestChn.FontFallbackService:TryUseManagedFallbackAsPrimary"));
        private static readonly ApplyFont ApplyFallback =
            AccessTools.MethodDelegate<ApplyFont>(AccessTools.Method("V81TestChn.FontFallbackService:ApplyFallback"));

        private readonly Vector3[] slotCorners = new Vector3[4];
        private RectTransform root;
        private RectTransform parent;
        private RectTransform backplate;
        private TextMeshProUGUI label;
        private HUDManager owner;
        private Transform companyCaptions;
        private string previousDialogue;
        private string previousWriting;
        private float previousWidth;

        internal void Show(string dialogue, string writing)
        {
            if (dialogue == null && writing == null)
            {
                if (root) root.gameObject.SetActive(false);
                return;
            }

            var hud = HUDManager.Instance;
            if (!hud || !hud.HUDContainer) return;
            if (!label || owner != hud) Create(hud);
            if (companyCaptions && companyCaptions.gameObject.activeInHierarchy)
            {
                root.gameObject.SetActive(false);
                return;
            }

            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            float width = label.rectTransform.rect.width;
            if (dialogue != previousDialogue || writing != previousWriting || !Mathf.Approximately(width, previousWidth))
            {
                previousDialogue = dialogue;
                previousWriting = writing;
                previousWidth = width;
                string spoken = dialogue == null ? null : DialogueColor + dialogue + "</color>";
                label.text = writing ?? spoken;
                if (spoken != null && writing != null && FitsLine(spoken) && FitsLine(writing))
                    label.text = writing + "\n" + spoken;

                label.ForceMeshUpdate();
                var bounds = label.textBounds;
                var padding = new Vector2(label.fontSize * 0.46f, label.fontSize * 0.26f);
                backplate.anchoredPosition = bounds.center;
                backplate.sizeDelta = (Vector2)bounds.size + padding * 2f;
            }

            float plateBottom = root.rect.height * 0.5f + backplate.anchoredPosition.y - backplate.rect.height * 0.5f;
            float offset = InventorySafeBottom() - plateBottom;
            if (!Mathf.Approximately(root.anchoredPosition.y, offset))
                root.anchoredPosition = new Vector2(0f, offset);
        }

        private float InventorySafeBottom()
        {
            float height = parent.rect.height;
            float bottom = height * 0.22f;
            var slots = owner.itemSlotIconFrames;
            if (slots == null) return bottom;

            float gap = label.fontSizeMax * 0.64f;
            foreach (var slot in slots)
            {
                if (!slot || !slot.isActiveAndEnabled) continue;
                slot.rectTransform.GetWorldCorners(slotCorners);
                foreach (var corner in slotCorners)
                {
                    float fromBottom = parent.InverseTransformPoint(corner).y - parent.rect.yMin;
                    if (fromBottom >= 0f && fromBottom <= height * 0.55f)
                        bottom = Mathf.Max(bottom, fromBottom + gap);
                }
            }
            return bottom;
        }

        private bool FitsLine(string value) =>
            value.IndexOf('\n') < 0 && label.GetPreferredValues(value).x <= label.rectTransform.rect.width;

        private void Create(HUDManager hud)
        {
            Dispose();
            owner = hud;
            previousDialogue = previousWriting = null;
            parent = (RectTransform)hud.HUDContainer.transform;
            companyCaptions = parent.Find("__V81_CompanyAudioSubtitles");
            var container = new GameObject("WesleySubtitles", typeof(RectTransform));
            root = (RectTransform)container.transform;
            root.SetParent(parent, false);
            root.anchorMin = new Vector2(0.08f, 0f);
            root.anchorMax = new Vector2(0.92f, 0f);
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = new Vector2(0f, 70f);
            root.anchoredPosition = Vector2.zero;

            var panel = new GameObject("Backplate", typeof(RectTransform), typeof(Image));
            var fill = panel.GetComponent<Image>();
            fill.color = new Color(0f, 0f, 0f, 0.5f);
            fill.raycastTarget = false;
            backplate = fill.rectTransform;
            backplate.SetParent(root, false);
            backplate.anchorMin = backplate.anchorMax = new Vector2(0.5f, 0.5f);

            var text = new GameObject("Text", typeof(RectTransform));
            text.transform.SetParent(root, false);
            label = text.AddComponent<TextMeshProUGUI>();
            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var standard = hud.controlTipLines != null && hud.controlTipLines.Length != 0
                ? hud.controlTipLines[0] : hud.weightCounter;
            if (standard)
            {
                label.font = standard.font;
                label.fontSharedMaterial = standard.fontSharedMaterial;
            }
            const string fallbackProbe = "中文对白";
            UseChineseFont(label, fallbackProbe);
            ApplyFallback(label, fallbackProbe, true);
            label.color = Color.white;
            label.outlineWidth = 0.18f;
            label.outlineColor = Color.black;
            label.alignment = TextAlignmentOptions.Bottom;
            label.fontSize = 25f;
            label.fontSizeMin = 19f;
            label.fontSizeMax = 25f;
            label.enableAutoSizing = true;
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Truncate;
            label.maxVisibleLines = 2;
            label.raycastTarget = false;
        }

        public void Dispose()
        {
            if (root) UnityEngine.Object.Destroy(root.gameObject);
            root = null;
            label = null;
            owner = null;
        }
    }
}
