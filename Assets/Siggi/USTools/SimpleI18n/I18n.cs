/*
 * The MIT License
 *
 * Copyright 2026 Sigurður Jón (siggi.io, github.com/WesternIcelander).
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */

using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon;

namespace SiggiUSTools {
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class I18n : UdonSharpBehaviour
    {
        private bool _started;
        private bool _ready;
        private DataDictionary _fallbackDictionary;
        private DataDictionary _stringsDictionary;
        private DataDictionary _byLanguageCode;
        private DataList _allLanguages;
        private DataList _listeners;
        [SerializeField] private TextAsset[] languagePacks;
        void Start()
        {
            if (_started) return;
            _started = true;

            _byLanguageCode = new DataDictionary();
            _allLanguages = new DataList();
            _listeners = new DataList();
            for (int i = 0; i < languagePacks.Length; i++)
            {
                DataDictionary dictionary = LoadTextAsset(languagePacks[i]);
                if (dictionary == null) continue;
                LoadPack(dictionary);
            }

            if (_fallbackDictionary == null)
            {
                Debug.LogError("No language pack was loaded.");
            }

            string currentLanguage = VRCPlayerApi.GetCurrentLanguage();
            if (currentLanguage != null)
            {
                DoSetLanguage(currentLanguage);
            }

            _ready = true;
        }

        void LoadPack(DataDictionary dictionary)
        {
            if (!dictionary.TryGetValue("languageCode", out DataToken loadingCode)) return;
            string languageCode = loadingCode.String;
            for (int i = 0; i < _allLanguages.Count; i++)
            {
                if (!_allLanguages.TryGetValue(i, out DataToken existingLanguageToken)) continue;
                DataDictionary existingDictionary = existingLanguageToken.DataDictionary;
                if (!existingDictionary.TryGetValue("languageCode", out DataToken existingCodeToken)) continue;
                if (existingCodeToken.String == languageCode)
                {
                    DataList sourceKeys = dictionary.GetKeys();
                    for (int j = 0; j < sourceKeys.Count; j++)
                    {
                        if (!sourceKeys.TryGetValue(j, out DataToken key)) continue;
                        if (!dictionary.TryGetValue(key, out DataToken value)) continue;
                        existingDictionary.SetValue(key, value);
                    }
                    return;
                }
            }
            if (_fallbackDictionary == null) _fallbackDictionary = dictionary;
            _allLanguages.Add(dictionary);
            _byLanguageCode.SetValue(languageCode, dictionary);
        }
        public void AddLanguageChangeListener(UdonSharpBehaviour listener)
        {
            Start();
            if (!_listeners.Contains(listener))
            {
                _listeners.Add(listener);
            }
        }
        void FireLanguageChangeEvent()
        {
            if (!_ready) return;
            for (int i = 0; i < _listeners.Count; i++)
            {
                UdonSharpBehaviour listener = (UdonSharpBehaviour)_listeners[i].Reference;
                if (listener != null)
                {
                    listener.SendCustomEvent("OnLanguageChange");
                }
            }
        }

        DataDictionary LoadTextAsset(TextAsset textAsset)
        {
            if (VRCJson.TryDeserializeFromJson(textAsset.text, out DataToken result))
            {
                DataDictionary dictionary = result.DataDictionary;
                if (!dictionary.TryGetValue("languageCode", out DataToken languageCodeResult))
                {
                    Debug.LogError($"[I18n] A language pack does not have a defined languageCode.");
                    return null;
                }

                return dictionary;
            }
            Debug.LogError($"[I18n] A language pack could not be deserialized from JSON.");
            return null;
        }

        public bool SetLanguage(string rfc5646Code)
        {
            Start();
            return DoSetLanguage(rfc5646Code);
        }

        bool DoSetLanguage(string rfc5646Code)
        {
            DataDictionary forwards = null;
            DataDictionary backwards = null;
            for (int i = 0; i < _allLanguages.Count; i++)
            {
                if (!_allLanguages.TryGetValue(i, out DataToken token)) continue;
                DataDictionary dictionary = token.DataDictionary;
                if (!dictionary.TryGetValue("languageCode", out DataToken languageCodeResult)) continue;
                string languageCode = languageCodeResult.String;
                if (languageCode == rfc5646Code)
                {
                    ApplyLanguagePack(dictionary);
                    return true;
                }
                if (languageCode.StartsWith(rfc5646Code))
                {
                    if (forwards == null) forwards = dictionary;
                }
                if (rfc5646Code.StartsWith(languageCode))
                {
                    if (backwards == null) backwards = dictionary;
                }
            }

            if (forwards != null)
            {
                ApplyLanguagePack(forwards);
                return true;
            }
            if (backwards != null)
            {
                ApplyLanguagePack(backwards);
                return true;
            }

            return false;
        }

        void ApplyLanguagePack(DataDictionary dictionary)
        {
            if (_stringsDictionary == dictionary) return;
            _stringsDictionary = dictionary;
            FireLanguageChangeEvent();
        }

        public string GetCurrentLanguage()
        {
            return _("languageCode");
        }

        public string GetCurrentLanguageName()
        {
            string result = _("languageName");
            return result == "languageName" ? _("languageCode") : result;
        }

        public string[] GetLanguages()
        {
            Start();
            string[] languages = new string[_allLanguages.Count];
            int successfulCount = 0;

            for (int i = 0; i < _allLanguages.Count; i++)
            {
                if (!_allLanguages.TryGetValue(i, out DataToken languageToken)) continue;
                DataDictionary dictionary = languageToken.DataDictionary;
                if (!dictionary.TryGetValue("languageCode", out DataToken languageCodeResult)) continue;
                languages[successfulCount++] = languageCodeResult.String;
            }

            if (successfulCount != languages.Length)
            {
                Debug.LogWarning($"[I18n] Some languages were not enumerated correctly.");
                string[] shortenedArray = new string[successfulCount];
                for (int i = 0; i < shortenedArray.Length; i++) shortenedArray[i] = languages[i];
                return shortenedArray;
            }

            return languages;
        }

        public string GetLanguageName(string languageCode)
        {
            Start();
            if (languageCode == null) return null;
            if (!_byLanguageCode.TryGetValue(languageCode, out DataToken languageDictionaryToken)) return languageCode;
            DataDictionary languagePack = languageDictionaryToken.DataDictionary;
            if (!languagePack.TryGetValue("languageName", out DataToken languageNameToken)) return languageCode;
            string languageName = languageNameToken.String;
            if (languageName == null) return languageCode;
            return languageName;
        }

        public string _(string key)
        {
            Start();
            
            if (_stringsDictionary != null)
            {
                if (_stringsDictionary.TryGetValue(key, out DataToken value))
                {
                    return value.String;
                }
            }

            if (_fallbackDictionary != null)
            {
                if (_fallbackDictionary.TryGetValue(key, out DataToken value))
                {
                    return value.String;
                }
            }
            
            Debug.LogError($"[I18n] No string found for key {key}");

            return key;
        }

        public string _(string key, string[] args)
        {
            string template = _(key);
            if (args == null || args.Length == 0) return template;
            if (args.Length % 2 != 0)
            {
                Debug.LogWarning($"[I18n] Odd number of placeholder args for key {key}; the last one is ignored.");
            }

            string result = "";
            int pos = 0;
            while (true)
            {
                int start = template.IndexOf("${", pos);
                if (start < 0) break;
                int end = template.IndexOf('}', start + 2);
                if (end < 0) break;

                result += template.Substring(pos, start - pos);
                string name = template.Substring(start + 2, end - start - 2);
                int index = FindArg(args, name);
                if (index >= 0)
                {
                    result += args[index + 1];
                }
                else
                {
                    Debug.LogWarning($"[I18n] No value for placeholder {name} in key {key}");
                    result += template.Substring(start, end - start + 1);
                }
                pos = end + 1;
            }
            return result + template.Substring(pos);
        }

        int FindArg(string[] args, string name)
        {
            for (int i = 0; i + 1 < args.Length; i += 2)
            {
                if (args[i] == name) return i;
            }
            return -1;
        }
    }
}
