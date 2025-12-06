using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace UnityLibary.Tools
{
    public class Fontastic : EditorWindow
    {
        [Serializable]
        private class FontListItem
        {
            public string id;
            public string family;
            public string[] variants;
            public string[] subsets;
            public string category;
            public string version;
            public string lastModified;
            public int popularity;
            public string defSubset;
            public string defVariant;
        }

        [Serializable]
        private class FontListWrapper
        {
            public FontListItem[] items;
        }

        [Serializable]
        private class FontVariant
        {
            public string id;
            public string eot;
            public string fontFamily;
            public string fontStyle;
            public string fontWeight;
            public string woff;
            public string[] local;
            public string ttf;
            public string svg;
            public string woff2;
        }

        [Serializable]
        private class FontDetail
        {
            public string id;
            public string family;
            public FontVariant[] variants;
            public string[] subsets;
            public string category;
            public string version;
            public string lastModified;
            public int popularity;
            public string defSubset;
            public string defVariant;
        }

        [Serializable]
        private class LocalFontItem
        {
            public string filePath;
            public string name;
        }

        private enum FontSourceMode
        {
            WebFonts,
            LocalFonts
        }

        private const string ApiListUrl = "https://gwfh.mranftl.com/api/fonts";
        private const string ApiDetailUrlFormat = "https://gwfh.mranftl.com/api/fonts/{0}?subsets=latin";

        private const string DownloadFolder = "Assets/Temp/WebFonts";
        private const string FontsFolder = "Assets/Fonts";

        private FontSourceMode fontSourceMode = FontSourceMode.LocalFonts;

        private List<FontListItem> webFontList = new List<FontListItem>();
        private List<LocalFontItem> localFontList = new List<LocalFontItem>();

        private Vector2 listScroll;
        private string searchTerm = "";
        private bool isLoading = false;
        private string statusMessage = "";

        private readonly List<int> displayedIndices = new List<int>();

        private int selectedWebIndex = -1;
        private int selectedLocalIndex = -1;
        private Rect scrollViewRect;
        private bool pendingScrollToSelection = false;

        private int SelectedIndex
        {
            get { return fontSourceMode == FontSourceMode.WebFonts ? selectedWebIndex : selectedLocalIndex; }
            set
            {
                if (fontSourceMode == FontSourceMode.WebFonts)
                    selectedWebIndex = value;
                else
                    selectedLocalIndex = value;
            }
        }

        private readonly Dictionary<Component, UnityEngine.Object> originalFonts =
            new Dictionary<Component, UnityEngine.Object>();

        private string lastTtfPath;
        private string lastTmpPath;
        private readonly Dictionary<int, Rect> rowRects = new Dictionary<int, Rect>();


        [MenuItem("Tools/UnityLibrary/Fontastic")]
        public static void Open()
        {
            var window = GetWindow<Fontastic>("Fontastic");
            window.minSize = new Vector2(520f, 380f);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Font Preview and Applier", EditorStyles.boldLabel);

            EditorGUILayout.Space();

            DrawSourceModeRadioButtons();

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            if (fontSourceMode == FontSourceMode.WebFonts)
            {
                if (GUILayout.Button("Fetch web font list", GUILayout.Width(160)))
                {
                    FetchWebFontList();
                }
            }
            else
            {
                if (GUILayout.Button("Refresh local fonts", GUILayout.Width(160)))
                {
                    RefreshLocalFonts();
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
            searchTerm = EditorGUILayout.TextField(searchTerm);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            int prevSelectedIndex = SelectedIndex;

            using (new EditorGUI.DisabledScope(isLoading))
            {
                // 1) Draw list (builds displayedIndices and row rects for this event)
                DrawFontList();

                // 2) Handle keyboard AFTER list has been drawn
                HandleKeyboardNavigationAndAutoScroll();
            }

            if (!isLoading && SelectedIndex >= 0 && SelectedIndex != prevSelectedIndex)
            {
                DownloadAndApplySelectedFont();
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(isLoading || SelectedIndex < 0))
            {
                if (GUILayout.Button("Download and apply to selection (manual)", GUILayout.Height(24)))
                {
                    DownloadAndApplySelectedFont();
                }
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(lastTtfPath) && string.IsNullOrEmpty(lastTmpPath)))
            {
                if (GUILayout.Button("Copy current font to Assets/Fonts", GUILayout.Height(24)))
                {
                    CopyCurrentFontToProject();
                }
            }

            using (new EditorGUI.DisabledScope(!Directory.Exists(DownloadFolder)))
            {
                if (GUILayout.Button("Clear temp fonts", GUILayout.Height(24)))
                {
                    ClearTempFonts();
                }
            }

            using (new EditorGUI.DisabledScope(originalFonts.Count == 0))
            {
                if (GUILayout.Button("Restore original fonts", GUILayout.Height(24)))
                {
                    RestoreOriginalFonts();
                }
            }

            EditorGUILayout.Space();

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
            }
        }

        private void DrawSourceModeRadioButtons()
        {
            EditorGUILayout.BeginHorizontal();
            bool webSelected = fontSourceMode == FontSourceMode.WebFonts;
            bool localSelected = fontSourceMode == FontSourceMode.LocalFonts;

            bool newWeb = GUILayout.Toggle(webSelected, "Web Fonts", "Radio", GUILayout.Width(100));
            bool newLocal = GUILayout.Toggle(localSelected, "Local Fonts", "Radio", GUILayout.Width(100));

            if (newWeb && !webSelected)
            {
                fontSourceMode = FontSourceMode.WebFonts;
            }
            else if (newLocal && !localSelected)
            {
                fontSourceMode = FontSourceMode.LocalFonts;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawFontList()
        {
            displayedIndices.Clear();
            rowRects.Clear();

            if (fontSourceMode == FontSourceMode.WebFonts)
            {
                if (webFontList == null || webFontList.Count == 0)
                {
                    EditorGUILayout.HelpBox("Web font list is empty. Click \"Fetch web font list\".", MessageType.None);
                    return;
                }

                EditorGUILayout.LabelField("Loaded web fonts: " + webFontList.Count, EditorStyles.miniLabel);
            }
            else
            {
                if (localFontList == null || localFontList.Count == 0)
                {
                    EditorGUILayout.HelpBox("Local font list is empty. Click \"Refresh local fonts\".", MessageType.None);
                    return;
                }

                EditorGUILayout.LabelField("Loaded local fonts: " + localFontList.Count, EditorStyles.miniLabel);
            }

            listScroll = EditorGUILayout.BeginScrollView(listScroll);

            string filter = string.IsNullOrEmpty(searchTerm)
                ? null
                : searchTerm.Trim().ToLowerInvariant();

            int currentSelected = SelectedIndex;

            if (fontSourceMode == FontSourceMode.WebFonts)
            {
                for (int i = 0; i < webFontList.Count; i++)
                {
                    var item = webFontList[i];
                    if (item == null) continue;

                    if (!string.IsNullOrEmpty(filter))
                    {
                        if (!item.family.ToLowerInvariant().Contains(filter) &&
                            !item.id.ToLowerInvariant().Contains(filter))
                        {
                            continue;
                        }
                    }

                    displayedIndices.Add(i);

                    string displayName = item.family;
                    if (!string.IsNullOrEmpty(item.category))
                    {
                        displayName += " (" + item.category + ")";
                    }

                    bool isSelected = (i == currentSelected);

                    var style = new GUIStyle(EditorStyles.miniButton)
                    {
                        alignment = TextAnchor.MiddleLeft
                    };

                    if (isSelected)
                    {
                        style.fontStyle = FontStyle.Bold;
                    }

                    bool clicked = GUILayout.Toggle(isSelected, displayName, style);
                    if (clicked && !isSelected)
                    {
                        SelectedIndex = i;
                        currentSelected = i;
                    }

                    Rect rowRect = GUILayoutUtility.GetLastRect();
                    rowRects[i] = rowRect;
                }
            }
            else
            {
                for (int i = 0; i < localFontList.Count; i++)
                {
                    var item = localFontList[i];
                    if (item == null) continue;

                    if (!string.IsNullOrEmpty(filter))
                    {
                        if (!item.name.ToLowerInvariant().Contains(filter) &&
                            !item.filePath.ToLowerInvariant().Contains(filter))
                        {
                            continue;
                        }
                    }

                    displayedIndices.Add(i);

                    string displayName = item.name;

                    bool isSelected = (i == currentSelected);

                    var style = new GUIStyle(EditorStyles.miniButton)
                    {
                        alignment = TextAnchor.MiddleLeft
                    };

                    if (isSelected)
                    {
                        style.fontStyle = FontStyle.Bold;
                    }

                    bool clicked = GUILayout.Toggle(isSelected, displayName, style);
                    if (clicked && !isSelected)
                    {
                        SelectedIndex = i;
                        currentSelected = i;
                    }

                    Rect rowRect = GUILayoutUtility.GetLastRect();
                    rowRects[i] = rowRect;
                }
            }

            EditorGUILayout.EndScrollView();

            // Rect of the visible scroll view area
            scrollViewRect = GUILayoutUtility.GetLastRect();

            // Only do the scroll adjust on Repaint, when layout is final
            if (pendingScrollToSelection && Event.current.type == EventType.Repaint)
            {
                ScrollToSelectedRowIfNeeded();
                pendingScrollToSelection = false;
            }
        }

        private void ScrollToSelectedRowIfNeeded()
        {
            int selectedIndex = SelectedIndex;
            if (selectedIndex < 0)
                return;

            if (!rowRects.TryGetValue(selectedIndex, out Rect rowRect))
                return; // selected row might be filtered out

            // Height of the visible area of the scroll view
            float viewHeight = scrollViewRect.height;

            // IMPORTANT:
            // - rowRect.yMin / yMax are in *view space* (0 at top of visible area)
            // - visible range in this space is [0, viewHeight]
            // So we do not compare against listScroll.y here.

            // Row is above the visible area
            if (rowRect.yMin < 0f)
            {
                // Move content up by how much the row is above the top
                listScroll.y += rowRect.yMin;
            }
            // Row is below the visible area
            else if (rowRect.yMax > viewHeight)
            {
                // Move content down by how much the row is below the bottom
                listScroll.y += (rowRect.yMax - viewHeight);
            }

            if (listScroll.y < 0f)
                listScroll.y = 0f;
        }


        private void HandleKeyboardNavigationAndAutoScroll()
        {
            if (displayedIndices.Count == 0)
                return;

            Event e = Event.current;
            if (e.type != EventType.KeyDown)
                return;

            int currentSelected = SelectedIndex;
            int pos = displayedIndices.IndexOf(currentSelected);
            if (pos < 0)
                pos = 0;

            bool handled = false;
            int pageSize = 10;

            switch (e.keyCode)
            {
                case KeyCode.UpArrow:
                    pos = Mathf.Max(0, pos - 1);
                    handled = true;
                    break;

                case KeyCode.DownArrow:
                    pos = Mathf.Min(displayedIndices.Count - 1, pos + 1);
                    handled = true;
                    break;

                case KeyCode.PageUp:
                    pos = Mathf.Max(0, pos - pageSize);
                    handled = true;
                    break;

                case KeyCode.PageDown:
                    pos = Mathf.Min(displayedIndices.Count - 1, pos + pageSize);
                    handled = true;
                    break;
            }

            if (!handled)
                return;

            SelectedIndex = displayedIndices[pos];
            pendingScrollToSelection = true;

            e.Use();
            Repaint();
        }


        private void FetchWebFontList()
        {
            try
            {
                isLoading = true;
                statusMessage = "Fetching web font list...";

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                using (var client = new WebClient())
                {
                    string json = client.DownloadString(ApiListUrl);
                    if (string.IsNullOrEmpty(json))
                    {
                        statusMessage = "Empty response from API.";
                        return;
                    }

                    string wrappedJson = "{ \"items\": " + json + " }";

                    var wrapper = JsonUtility.FromJson<FontListWrapper>(wrappedJson);
                    webFontList.Clear();

                    if (wrapper != null && wrapper.items != null)
                    {
                        webFontList.AddRange(wrapper.items);
                        statusMessage = "Loaded " + wrapper.items.Length + " web fonts.";
                    }
                    else
                    {
                        statusMessage = "Could not parse web font list.";
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("WebFontApplierWindow: Error fetching web font list: " + ex.Message);
                statusMessage = "Error fetching web font list. See console for details.";
            }
            finally
            {
                isLoading = false;
            }
        }

        private void RefreshLocalFonts()
        {
            try
            {
                isLoading = true;
                statusMessage = "Scanning local fonts folder...";

                localFontList.Clear();

                string fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                if (string.IsNullOrEmpty(fontsDir) || !Directory.Exists(fontsDir))
                {
                    statusMessage = "Fonts folder not found on this system.";
                    return;
                }

                string[] fontFiles = Directory.GetFiles(fontsDir, "*.ttf");
                string[] otfFiles = Directory.GetFiles(fontsDir, "*.otf");

                foreach (string file in fontFiles)
                {
                    localFontList.Add(new LocalFontItem
                    {
                        filePath = file,
                        name = Path.GetFileNameWithoutExtension(file)
                    });
                }

                foreach (string file in otfFiles)
                {
                    localFontList.Add(new LocalFontItem
                    {
                        filePath = file,
                        name = Path.GetFileNameWithoutExtension(file)
                    });
                }

                statusMessage = "Loaded " + localFontList.Count + " local fonts.";
            }
            catch (Exception ex)
            {
                Debug.LogError("WebFontApplierWindow: Error loading local fonts: " + ex.Message);
                statusMessage = "Error loading local fonts. See console for details.";
            }
            finally
            {
                isLoading = false;
            }
        }

        private void DownloadAndApplySelectedFont()
        {
            if (SelectedIndex < 0)
            {
                statusMessage = "No font selected.";
                return;
            }

            if (Selection.gameObjects == null || Selection.gameObjects.Length == 0)
            {
                statusMessage = "No GameObject selected. Select at least one object with text.";
                return;
            }

            try
            {
                isLoading = true;

                if (!Directory.Exists(DownloadFolder))
                {
                    Directory.CreateDirectory(DownloadFolder);
                }

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                if (fontSourceMode == FontSourceMode.WebFonts)
                {
                    ApplyWebFont();
                }
                else
                {
                    ApplyLocalFont();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("WebFontApplierWindow: Error downloading or applying font: " + ex);
                statusMessage = "Error downloading/applying font. See console for details.";
            }
            finally
            {
                isLoading = false;
            }
        }

        private void ApplyWebFont()
        {
            if (SelectedIndex < 0 || SelectedIndex >= webFontList.Count)
            {
                statusMessage = "Invalid web font selection.";
                return;
            }

            var item = webFontList[SelectedIndex];
            if (item == null)
            {
                statusMessage = "Invalid web font selection.";
                return;
            }

            statusMessage = "Downloading web font " + item.family + "...";

            string detailUrl = string.Format(ApiDetailUrlFormat, item.id);
            FontDetail detail;

            using (var client = new WebClient())
            {
                string json = client.DownloadString(detailUrl);
                detail = JsonUtility.FromJson<FontDetail>(json);
            }

            if (detail == null || detail.variants == null || detail.variants.Length == 0)
            {
                statusMessage = "Could not find variants for web font.";
                return;
            }

            FontVariant chosenVariant = null;
            if (!string.IsNullOrEmpty(detail.defVariant))
            {
                foreach (var v in detail.variants)
                {
                    if (v.id == detail.defVariant)
                    {
                        chosenVariant = v;
                        break;
                    }
                }
            }
            if (chosenVariant == null)
            {
                chosenVariant = detail.variants[0];
            }

            if (chosenVariant == null || string.IsNullOrEmpty(chosenVariant.ttf))
            {
                statusMessage = "No TTF url found for selected web font.";
                return;
            }

            string ttfUrl = chosenVariant.ttf;

            byte[] ttfBytes;
            using (var client = new WebClient())
            {
                ttfBytes = client.DownloadData(ttfUrl);
            }

            if (ttfBytes == null || ttfBytes.Length == 0)
            {
                statusMessage = "Downloaded TTF data is empty.";
                return;
            }

            string safeFamily = MakeSafeFileName(detail.family);
            string variantId = string.IsNullOrEmpty(chosenVariant.id) ? "regular" : chosenVariant.id;

            string ttfFileName = safeFamily + "-" + variantId + ".ttf";
            string ttfPath = Path.Combine(DownloadFolder, ttfFileName).Replace("\\", "/");

            File.WriteAllBytes(ttfPath, ttfBytes);
            AssetDatabase.ImportAsset(ttfPath);
            AssetDatabase.Refresh();

            Font uiFont = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (uiFont == null)
            {
                statusMessage = "Imported TTF but failed to load Font asset.";
                return;
            }

            TMP_FontAsset tmpFont = TMP_FontAsset.CreateFontAsset(uiFont);
            string tmpFileName = safeFamily + "-" + variantId + "-SDF.asset";
            string tmpPath = Path.Combine(DownloadFolder, tmpFileName).Replace("\\", "/");

            AssetDatabase.CreateAsset(tmpFont, tmpPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            lastTtfPath = ttfPath;
            lastTmpPath = tmpPath;

            ApplyFontsToSelection(uiFont, tmpFont);

            statusMessage = "Applied web font " + detail.family + " (" + variantId + ") to selection.";
        }

        private void ApplyLocalFont()
        {
            if (SelectedIndex < 0 || SelectedIndex >= localFontList.Count)
            {
                statusMessage = "Invalid local font selection.";
                return;
            }

            var item = localFontList[SelectedIndex];
            if (item == null || string.IsNullOrEmpty(item.filePath))
            {
                statusMessage = "Invalid local font selection.";
                return;
            }

            statusMessage = "Preparing local font " + item.name + "...";

            string sourcePath = item.filePath;
            if (!File.Exists(sourcePath))
            {
                statusMessage = "Local font file does not exist.";
                return;
            }

            string safeName = MakeSafeFileName(item.name);
            string ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrEmpty(ext))
            {
                ext = ".ttf";
            }

            string ttfFileName = safeName + ext;
            string ttfPath = Path.Combine(DownloadFolder, ttfFileName).Replace("\\", "/");

            File.Copy(sourcePath, ttfPath, true);
            AssetDatabase.ImportAsset(ttfPath);
            AssetDatabase.Refresh();

            Font uiFont = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (uiFont == null)
            {
                statusMessage = "Imported local font but failed to load Font asset.";
                return;
            }

            TMP_FontAsset tmpFont = TMP_FontAsset.CreateFontAsset(uiFont);
            string tmpFileName = safeName + "-SDF.asset";
            string tmpPath = Path.Combine(DownloadFolder, tmpFileName).Replace("\\", "/");

            AssetDatabase.CreateAsset(tmpFont, tmpPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            lastTtfPath = ttfPath;
            lastTmpPath = tmpPath;

            ApplyFontsToSelection(uiFont, tmpFont);

            statusMessage = "Applied local font " + item.name + " to selection.";
        }

        private void ApplyFontsToSelection(Font uiFont, TMP_FontAsset tmpFont)
        {
            if (Selection.gameObjects == null) return;

            foreach (var go in Selection.gameObjects)
            {
                if (go == null) continue;
                ApplyFontsToGameObjectRecursive(go, uiFont, tmpFont);
            }
        }

        private void ApplyFontsToGameObjectRecursive(GameObject go, Font uiFont, TMP_FontAsset tmpFont)
        {
            if (go == null) return;

            var uiText = go.GetComponent<Text>();
            if (uiText != null)
            {
                RegisterOriginalFont(uiText, uiText.font);
                Undo.RecordObject(uiText, "Apply Web Font");
                uiText.font = uiFont;
                EditorUtility.SetDirty(uiText);
            }

            var textMesh = go.GetComponent<TextMesh>();
            if (textMesh != null)
            {
                RegisterOriginalFont(textMesh, textMesh.font);
                Undo.RecordObject(textMesh, "Apply Web Font");
                textMesh.font = uiFont;
                EditorUtility.SetDirty(textMesh);
            }

            var tmpText = go.GetComponent<TMP_Text>();
            if (tmpText != null)
            {
                RegisterOriginalFont(tmpText, tmpText.font);
                Undo.RecordObject(tmpText, "Apply Web Font");
                tmpText.font = tmpFont;
                EditorUtility.SetDirty(tmpText);
            }

            foreach (Transform child in go.transform)
            {
                ApplyFontsToGameObjectRecursive(child.gameObject, uiFont, tmpFont);
            }
        }

        private void RegisterOriginalFont(Component component, UnityEngine.Object originalFont)
        {
            if (component == null || originalFont == null) return;

            if (!originalFonts.ContainsKey(component))
            {
                originalFonts.Add(component, originalFont);
            }
        }

        private void RestoreOriginalFonts()
        {
            foreach (var kvp in originalFonts)
            {
                var comp = kvp.Key;
                var original = kvp.Value;
                if (comp == null || original == null) continue;

                Undo.RecordObject(comp, "Restore Original Font");

                if (comp is Text uiText)
                {
                    uiText.font = original as Font;
                    EditorUtility.SetDirty(uiText);
                }
                else if (comp is TextMesh textMesh)
                {
                    textMesh.font = original as Font;
                    EditorUtility.SetDirty(textMesh);
                }
                else if (comp is TMP_Text tmpText)
                {
                    tmpText.font = original as TMP_FontAsset;
                    EditorUtility.SetDirty(tmpText);
                }
            }

            statusMessage = "Restored original fonts for " + originalFonts.Count + " components.";
            originalFonts.Clear();
        }

        private void CopyCurrentFontToProject()
        {
            if (string.IsNullOrEmpty(lastTtfPath) && string.IsNullOrEmpty(lastTmpPath))
            {
                statusMessage = "No downloaded font to copy.";
                return;
            }

            if (!Directory.Exists(FontsFolder))
            {
                Directory.CreateDirectory(FontsFolder);
            }

            bool copiedSomething = false;

            if (!string.IsNullOrEmpty(lastTtfPath) && File.Exists(lastTtfPath))
            {
                string destPath = Path.Combine(FontsFolder, Path.GetFileName(lastTtfPath)).Replace("\\", "/");
                if (destPath != lastTtfPath)
                {
                    bool ok = AssetDatabase.CopyAsset(lastTtfPath, destPath);
                    if (!ok)
                    {
                        Debug.LogWarning("WebFontApplierWindow: Failed to copy TTF to " + destPath);
                    }
                    else
                    {
                        copiedSomething = true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(lastTmpPath) && File.Exists(lastTmpPath))
            {
                string destPath = Path.Combine(FontsFolder, Path.GetFileName(lastTmpPath)).Replace("\\", "/");
                if (destPath != lastTmpPath)
                {
                    bool ok = AssetDatabase.CopyAsset(lastTmpPath, destPath);
                    if (!ok)
                    {
                        Debug.LogWarning("WebFontApplierWindow: Failed to copy TMP asset to " + destPath);
                    }
                    else
                    {
                        copiedSomething = true;
                    }
                }
            }

            AssetDatabase.Refresh();

            statusMessage = copiedSomething
                ? "Copied current font to Assets/Fonts."
                : "Nothing was copied (files already in Assets/Fonts?).";
        }

        private void ClearTempFonts()
        {
            if (!Directory.Exists(DownloadFolder))
            {
                statusMessage = "Temp fonts folder is already empty.";
                return;
            }

            string[] files = Directory.GetFiles(DownloadFolder, "*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                string path = file.Replace("\\", "/");
                int idx = path.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    string assetPath = path.Substring(idx);
                    AssetDatabase.DeleteAsset(assetPath);
                }
            }

            try
            {
                if (Directory.Exists(DownloadFolder))
                {
                    Directory.Delete(DownloadFolder, true);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("WebFontApplierWindow: Could not fully delete temp folder: " + e.Message);
            }

            AssetDatabase.Refresh();

            if (!string.IsNullOrEmpty(lastTtfPath) && lastTtfPath.StartsWith(DownloadFolder, StringComparison.OrdinalIgnoreCase))
                lastTtfPath = null;
            if (!string.IsNullOrEmpty(lastTmpPath) && lastTmpPath.StartsWith(DownloadFolder, StringComparison.OrdinalIgnoreCase))
                lastTmpPath = null;

            statusMessage = "Cleared temp fonts.";
        }

        private static string MakeSafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Font";

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Replace(' ', '_');
        }
    }
}
