using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;

namespace vanhaodev.savemanager.Samples.K_pop_Artists
{
    public partial class KpopArtistsUI : MonoBehaviour
    {
        private KpopArtistsManager _manager = new KpopArtistsManager();
        private enum ModalType { None, ViewImages, EditImages, ViewStats, EditStats, ViewSns, EditSns, EditName }
        private ModalType _modal = ModalType.None;
        
        private ArtistData _targetArtist;
        private Dictionary<string, Texture2D> _imageCache = new Dictionary<string, Texture2D>();
        private Vector2 _mainScroll, _modalScroll;

        private void Start() => _manager.LoadFromDisk();

        private void OnGUI()
        {
            // Cấu hình UI cực to cho Mobile
            GUI.skin.button.fontSize = 38;
            GUI.skin.label.fontSize = 42;
            GUI.skin.textField.fontSize = 45;
            GUI.skin.box.fontSize = 35;

            float p = Screen.width * 0.04f;
            GUILayout.BeginArea(new Rect(p, p, Screen.width - p * 2, Screen.height - p * 2));
            
            GUILayout.Label("<color=#FF007F><b>K-POP ARTIST PRO</b></color>", new GUIStyle(GUI.skin.label){alignment = TextAnchor.MiddleCenter, fontSize = 60});
            GUILayout.Space(30);

            _mainScroll = GUILayout.BeginScrollView(_mainScroll);
            var artists = _manager.Data.Artists;
            for (int i = 0; i < artists.Count; i++) DrawArtistRow(artists[i], i);
            GUILayout.EndScrollView();

            GUI.color = Color.green;
            if (GUILayout.Button("+ NEW ARTIST", GUILayout.Height(150))) {
                _manager.Add(new ArtistData { Name = "New Artist" });
                _manager.SaveToDisk();
            }
            GUI.color = Color.white;
            GUILayout.EndArea();

            if (_modal != ModalType.None && _targetArtist != null) DrawMobileModal();
        }

        private void DrawArtistRow(ArtistData artist, int index)
        {
            GUILayout.BeginVertical(GUI.skin.window);
            
            // Hàng 1: Info + Delete
            GUILayout.BeginHorizontal();
            // Thumbnail to
            Rect imgRect = GUILayoutUtility.GetRect(180, 180, GUILayout.Width(180), GUILayout.Height(180));
            string url = (artist.Images.Count > 0) ? artist.Images[0].Url : "";
            if (!string.IsNullOrEmpty(url) && _imageCache.TryGetValue(url, out Texture2D tex) && tex != null)
                GUI.DrawTexture(imgRect, tex, ScaleMode.ScaleToFit);
            else {
                GUI.Box(imgRect, "IMG");
                if (!string.IsNullOrEmpty(url) && !_imageCache.ContainsKey(url)) StartCoroutine(LoadImage(url));
            }

            GUILayout.BeginVertical();
            GUILayout.Label($"<b>{artist.Name}</b>", GUILayout.Height(80));
            if (GUILayout.Button("✎ RENAME", GUILayout.Width(250), GUILayout.Height(100))) { _targetArtist = artist; _modal = ModalType.EditName; }
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();
            GUI.color = Color.red;
            if (GUILayout.Button("✕", GUILayout.Width(100), GUILayout.Height(100))) { _manager.Delete(index); _manager.SaveToDisk(); return; }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(20);

            // Hàng 2: Grid 6 nút điều hướng cực to
            float btnW = (Screen.width * 0.42f);
            float btnH = 120;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🖼 IMAGE", GUILayout.Width(btnW), GUILayout.Height(btnH))) { _targetArtist = artist; _modal = ModalType.ViewImages; }
            GUI.color = Color.yellow;
            if (GUILayout.Button("EDIT", GUILayout.Width(btnW*0.4f), GUILayout.Height(btnH))) { _targetArtist = artist; _modal = ModalType.EditImages; }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("📊 STATS", GUILayout.Width(btnW), GUILayout.Height(btnH))) { _targetArtist = artist; _modal = ModalType.ViewStats; }
            GUI.color = Color.yellow;
            if (GUILayout.Button("EDIT", GUILayout.Width(btnW*0.4f), GUILayout.Height(btnH))) { _targetArtist = artist; _modal = ModalType.EditStats; }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("📱 SNS", GUILayout.Width(btnW), GUILayout.Height(btnH))) { _targetArtist = artist; _modal = ModalType.ViewSns; }
            GUI.color = Color.yellow;
            if (GUILayout.Button("EDIT", GUILayout.Width(btnW*0.4f), GUILayout.Height(btnH))) { _targetArtist = artist; _modal = ModalType.EditSns; }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.Space(30);
        }

        private void DrawMobileModal()
        {
            // Dim nền đen kịt cho tập trung
            GUI.ModalWindow(1, new Rect(0, 0, Screen.width, Screen.height), (id) => {
                GUILayout.BeginArea(new Rect(40, 40, Screen.width - 80, Screen.height - 80));
                
                GUILayout.Label($"<color=yellow>{_modal.ToString().ToUpper()}</color>", new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter, fontSize=55});
                GUILayout.Space(40);

                _modalScroll = GUILayout.BeginScrollView(_modalScroll);
                switch(_modal) {
                    case ModalType.ViewImages:
                        foreach(var img in _targetArtist.Images) {
                            if (_imageCache.TryGetValue(img.Url, out Texture2D t) && t != null) {
                                float aspect = (float)t.height / t.width;
                                GUILayout.Label(t, GUILayout.Width(Screen.width-150), GUILayout.Height((Screen.width-150)*aspect));
                                GUILayout.Space(20);
                            }
                        }
                        break;
                    case ModalType.EditImages:
                        for(int i=0; i<_targetArtist.Images.Count; i++) {
                            var img = _targetArtist.Images[i];
                            img.Url = GUILayout.TextField(img.Url, GUILayout.Height(100));
                            _targetArtist.Images[i] = img;
                            if (GUILayout.Button("REMOVE", GUILayout.Height(80))) { _targetArtist.Images.RemoveAt(i); break; }
                        }
                        if (GUILayout.Button("+ ADD URL", GUILayout.Height(120))) _targetArtist.Images.Add(new ArtistImage{Url="https://"});
                        break;
                    case ModalType.ViewStats:
                        GUILayout.Label($"<b>HEIGHT:</b> {_targetArtist.Stats.Height} cm");
                        GUILayout.Label($"<b>WEIGHT:</b> {_targetArtist.Stats.Weight}");
                        break;
                    case ModalType.EditStats:
                        GUILayout.Label("HEIGHT (cm):");
                        _targetArtist.Stats.Height = int.Parse(GUILayout.TextField(_targetArtist.Stats.Height.ToString(), GUILayout.Height(100)) ?? "0");
                        GUILayout.Label("WEIGHT:");
                        _targetArtist.Stats.Weight = GUILayout.TextField(_targetArtist.Stats.Weight, GUILayout.Height(100));
                        break;
                    case ModalType.EditName:
                        _targetArtist.Name = GUILayout.TextField(_targetArtist.Name, GUILayout.Height(120));
                        break;
                    case ModalType.ViewSns:
                        foreach(var s in _targetArtist.Sns) GUILayout.Label($"[{s.Type}] {s.Url}");
                        break;
                    case ModalType.EditSns:
                        for(int i=0; i<_targetArtist.Sns.Count; i++) {
                            var s = _targetArtist.Sns[i];
                            s.Url = GUILayout.TextField(s.Url, GUILayout.Height(100));
                            _targetArtist.Sns[i] = s;
                            if (GUILayout.Button("DEL", GUILayout.Width(200))) { _targetArtist.Sns.RemoveAt(i); break; }
                        }
                        if (GUILayout.Button("+ ADD SNS", GUILayout.Height(120))) _targetArtist.Sns.Add(new ArtistSNS());
                        break;
                }
                GUILayout.EndScrollView();

                GUILayout.Space(40);
                GUI.color = Color.cyan;
                if (GUILayout.Button("DONE", GUILayout.Height(160))) {
                    _manager.SaveToDisk(); // Tự động lưu khi bấm Done
                    _modal = ModalType.None;
                }
                GUI.color = Color.white;
                GUILayout.EndArea();
            }, "");
        }

        private IEnumerator LoadImage(string url)
        {
            if (string.IsNullOrEmpty(url) || _imageCache.ContainsKey(url)) yield break;
            _imageCache[url] = null;
            using (UnityWebRequest loader = UnityWebRequestTexture.GetTexture(url)) {
                yield return loader.SendWebRequest();
                if (loader.result == UnityWebRequest.Result.Success)
                    _imageCache[url] = DownloadHandlerTexture.GetContent(loader);
                else _imageCache.Remove(url);
            }
        }
    }
}