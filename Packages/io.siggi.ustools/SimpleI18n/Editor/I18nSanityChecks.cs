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

using System.Collections.Generic;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase.Editor.BuildPipeline;

namespace SiggiUSTools.SimpleI18nEditor {
    /// <summary>
    /// When a VRChat world build is requested or Play Mode is entered, asks the user whether to fill every I18n field
    /// on the scene's UdonSharpBehaviours that is null or doesn't point at the scene's I18n with the scene's I18n.
    /// Fails the build, and blocks entering Play Mode, if an I18n has a language pack that would break it at runtime.
    /// </summary>
    public class I18nSanityChecks : IVRCSDKBuildRequestedCallback
    {
        private const string I18nPropertyType = "PPtr<$" + nameof(I18n) + ">";
        private const int MaxDialogProblems = 10;

        // Run after UdonSharp's own build checks (-1) so scripts are compiled.
        public int callbackOrder => 0;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (requestedBuildType != VRCSDKRequestedBuildType.Scene) return true;

            I18n[] instances = Object.FindObjectsOfType<I18n>(true);
            if (!ValidateLanguagePacks(instances, "The build was cancelled")) return false;
            LinkI18nFields(instances);
            return true;
        }

        [InitializeOnLoadMethod]
        private static void RegisterPlayModeCheck()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.ExitingEditMode) return;
                I18n[] instances = Object.FindObjectsOfType<I18n>(true);
                if (!ValidateLanguagePacks(instances, "Play Mode was not started"))
                {
                    EditorApplication.isPlaying = false;
                    return;
                }
                LinkI18nFields(instances);
            };
        }

        private static void LinkI18nFields(I18n[] instances)
        {
            I18n target = instances.Length == 1 ? instances[0] : null;

            List<UdonSharpBehaviour> pending = new List<UdonSharpBehaviour>();
            int fixedCount = 0;
            bool autofixAllowed = false;
            foreach (UdonSharpBehaviour behaviour in Object.FindObjectsOfType<UdonSharpBehaviour>(true))
            {
                if (behaviour is I18n) continue;

                SerializedObject serializedObject = new SerializedObject(behaviour);
                SerializedProperty property = serializedObject.GetIterator();
                bool changed = false;
                bool needsI18n = false;
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (property.type != I18nPropertyType) continue;
                    if (property.propertyPath.Contains(".Array.data[")) continue;

                    // With exactly one I18n in the scene, any other value is null, missing or points outside the scene.
                    Object current = property.objectReferenceValue;
                    if (target != null ? current == target : IsValid(current, behaviour)) continue;

                    needsI18n = true;
                    if (target == null) continue;
                    if (!autofixAllowed)
                    {
                        autofixAllowed = EditorUtility.DisplayDialog(
                            "I18n",
                            "Some objects are not correctly wired to the I18n object, would you like to autofix this?",
                            "Yes",
                            "No");
                        if (!autofixAllowed) return;
                    }
                    property.objectReferenceValue = target;
                    changed = true;
                    Debug.Log($"[I18n] Assigned {target.name} to {behaviour.GetType().Name}.{property.propertyPath} on {GetPath(behaviour)}", behaviour);
                }

                if (needsI18n && target == null) pending.Add(behaviour);
                if (!changed) continue;

                serializedObject.ApplyModifiedProperties();
                UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
                EditorUtility.SetDirty(behaviour);
                fixedCount++;
            }

            if (instances.Length > 1)
            {
                Debug.LogWarning($"[I18n] Found {instances.Length} I18n behaviours in the scene; expected 1. I18n fields were not auto-filled.");
            }
            else if (instances.Length == 0 && pending.Count > 0)
            {
                Debug.LogWarning($"[I18n] {pending.Count} behaviour(s) have an unset I18n field, but there is no I18n behaviour in the scene.", pending[0]);
            }

            if (fixedCount > 0)
            {
                EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
            }
        }

        // I18n can't recover from these at runtime: an exception halts the behaviour, breaking every translation.
        private static bool ValidateLanguagePacks(I18n[] instances, string consequence)
        {
            List<string> problems = new List<string>();
            foreach (I18n i18n in instances)
            {
                SerializedProperty packs = new SerializedObject(i18n).FindProperty("languagePacks");
                if (packs.arraySize == 0)
                {
                    ReportProblem(problems, $"{GetPath(i18n)} has no language packs.", i18n);
                }
                for (int i = 0; i < packs.arraySize; i++)
                {
                    TextAsset pack = packs.GetArrayElementAtIndex(i).objectReferenceValue as TextAsset;
                    string error = GetPackError(pack);
                    if (error == null) continue;
                    ReportProblem(problems, $"Language pack {i} on {GetPath(i18n)} {error}", pack != null ? (Object)pack : i18n);
                }
            }
            if (problems.Count == 0) return true;

            string message = $"{consequence} because of problems with language packs:\n";
            for (int i = 0; i < problems.Count && i < MaxDialogProblems; i++)
            {
                message += $"\n• {problems[i]}";
            }
            if (problems.Count > MaxDialogProblems)
            {
                message += $"\n\n…and {problems.Count - MaxDialogProblems} more. See the Console for all of them.";
            }
            EditorUtility.DisplayDialog("I18n", message, "OK");
            return false;
        }

        private static void ReportProblem(List<string> problems, string problem, Object context)
        {
            problems.Add(problem);
            Debug.LogError($"[I18n] {problem}", context);
        }

        private static string GetPackError(TextAsset pack)
        {
            if (pack == null) return "is not set.";
            if (!VRCJson.TryDeserializeFromJson(pack.text, out DataToken result)) return $"({pack.name}) is not valid JSON.";
            if (result.TokenType != TokenType.DataDictionary) return $"({pack.name}) is not a JSON object.";
            if (!result.DataDictionary.TryGetValue("languageCode", out DataToken languageCode)) return $"({pack.name}) has no languageCode.";
            if (languageCode.TokenType != TokenType.String) return $"({pack.name}) has a languageCode that is not a string.";
            return null;
        }

        private static bool IsValid(Object current, Component owner)
        {
            return current is I18n i18n && i18n.gameObject.scene == owner.gameObject.scene;
        }

        private static string GetPath(Component component)
        {
            Transform transform = component.transform;
            string path = transform.name;
            while ((transform = transform.parent) != null)
            {
                path = transform.name + "/" + path;
            }
            return path;
        }
    }
}
