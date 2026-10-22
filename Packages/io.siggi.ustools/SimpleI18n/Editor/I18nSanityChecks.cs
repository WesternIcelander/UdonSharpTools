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
using UnityEngine.SceneManagement;
using VRC.SDK3.Data;
using VRC.SDKBase.Editor.BuildPipeline;

namespace SiggiUSTools.SimpleI18nEditor {
    /// <summary>
    /// When a VRChat world build is requested or Play Mode is entered, asks the user whether to point every I18n field
    /// on the scene's UdonSharpBehaviours at the I18n closest to that behaviour, skipping GameObjects with an
    /// <see cref="I18nLock"/>. Fails the build, and blocks entering Play Mode, if an I18n has a language pack that
    /// would break it at runtime, or if the I18n for a field can't be determined.
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
            if (!ValidateMainI18n(instances, "The build was cancelled")) return false;
            LinkMainI18n(instances);
            return LinkI18nFields(instances, "The build was cancelled");
        }

        [InitializeOnLoadMethod]
        private static void RegisterPlayModeCheck()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.ExitingEditMode) return;
                I18n[] instances = Object.FindObjectsOfType<I18n>(true);
                if (!ValidateLanguagePacks(instances, "Play Mode was not started")
                    || !ValidateMainI18n(instances, "Play Mode was not started"))
                {
                    EditorApplication.isPlaying = false;
                    return;
                }
                LinkMainI18n(instances);
                if (!LinkI18nFields(instances, "Play Mode was not started"))
                {
                    EditorApplication.isPlaying = false;
                }
            };
        }

        private class PendingFix
        {
            public UdonSharpBehaviour Behaviour;
            public SerializedObject SerializedObject;
            public List<string> Messages = new List<string>();
        }

        private class SceneFallback
        {
            public I18n I18n;
            public string Problem;
            public string Warning;
        }

        // Returns false if the I18n for a field can't be determined.
        private static bool LinkI18nFields(I18n[] instances, string consequence)
        {
            List<PendingFix> fixes = new List<PendingFix>();
            List<string> problems = new List<string>();
            Dictionary<Scene, SceneFallback> fallbacks = new Dictionary<Scene, SceneFallback>();
            List<UdonSharpBehaviour> unresolved = new List<UdonSharpBehaviour>();
            int fieldCount = 0;
            foreach (UdonSharpBehaviour behaviour in Object.FindObjectsOfType<UdonSharpBehaviour>(true))
            {
                if (behaviour is I18n) continue;
                if (behaviour.GetComponent<I18nLock>() != null) continue;

                SerializedObject serializedObject = new SerializedObject(behaviour);
                SerializedProperty property = serializedObject.GetIterator();
                PendingFix fix = null;
                bool searched = false;
                bool needsI18n = false;
                I18n closest = null;
                string ambiguousLevel = null;
                SceneFallback fallback = null;
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (property.type != I18nPropertyType) continue;
                    if (property.propertyPath.Contains(".Array.data[")) continue;

                    if (!searched)
                    {
                        closest = FindClosest(behaviour.transform, instances, out ambiguousLevel);
                        if (closest == null && ambiguousLevel == null)
                        {
                            Scene scene = behaviour.gameObject.scene;
                            if (!fallbacks.TryGetValue(scene, out fallback))
                            {
                                fallback = FindFallback(scene, instances);
                                fallbacks[scene] = fallback;
                            }
                            closest = fallback.I18n;
                        }
                        searched = true;
                    }

                    Object current = property.objectReferenceValue;
                    if (closest == null)
                    {
                        // Reported once for the whole scene below.
                        if (fallback != null && fallback.Problem != null) continue;
                        if (IsValid(current, behaviour)) continue;
                        if (ambiguousLevel == null)
                        {
                            needsI18n = true;
                            continue;
                        }
                        ReportProblem(problems, $"{behaviour.GetType().Name}.{property.propertyPath} on {GetPath(behaviour)} is not set, and the closest I18n can't be picked because there is more than one among {ambiguousLevel}.", behaviour);
                        continue;
                    }
                    if (current == closest) continue;

                    // Only the SerializedObject changes here; the behaviour is untouched unless the user allows the fix.
                    property.objectReferenceValue = closest;
                    if (fix == null) fix = new PendingFix { Behaviour = behaviour, SerializedObject = serializedObject };
                    fix.Messages.Add($"[I18n] Assigned {GetPath(closest)} to {behaviour.GetType().Name}.{property.propertyPath} on {GetPath(behaviour)}");
                    fieldCount++;
                }

                if (needsI18n) unresolved.Add(behaviour);
                if (fix != null) fixes.Add(fix);
            }

            if (unresolved.Count > 0)
            {
                Debug.LogWarning($"[I18n] {unresolved.Count} behaviour(s) have an unset I18n field, but there is no I18n behaviour in their scene.", unresolved[0]);
            }

            foreach (SceneFallback fallback in fallbacks.Values)
            {
                if (fallback.Problem != null) ReportProblem(problems, fallback.Problem, null);
                if (fallback.Warning != null) Debug.LogWarning($"[I18n] {fallback.Warning}", fallback.I18n);
            }

            if (problems.Count > 0)
            {
                ShowProblems(
                    $"{consequence} because some I18n fields can't be wired automatically:",
                    problems,
                    "Give each object a single I18n among the siblings of itself or its parents, assign unset fields by hand, " +
                    "or set Main I18n on exactly one I18n in the scene.");
                return false;
            }

            if (fixes.Count == 0) return true;
            bool autofixAllowed = EditorUtility.DisplayDialog(
                "I18n",
                $"{fieldCount} I18n field(s) on {fixes.Count} object(s) are not wired to the closest I18n object, would you like to autofix this?\n\n" +
                $"To keep an object's I18n fields as they are, add an {ObjectNames.NicifyVariableName(nameof(I18nLock))} component to it.",
                "Yes",
                "No");
            if (!autofixAllowed) return true;

            foreach (PendingFix fix in fixes)
            {
                fix.SerializedObject.ApplyModifiedProperties();
                UdonSharpEditorUtility.CopyProxyToUdon(fix.Behaviour);
                EditorUtility.SetDirty(fix.Behaviour);
                EditorSceneManager.MarkSceneDirty(fix.Behaviour.gameObject.scene);
                foreach (string message in fix.Messages)
                {
                    Debug.Log(message, fix.Behaviour);
                }
            }
            return true;
        }

        /// <summary>
        /// Finds the I18n closest to <paramref name="transform"/>: one on a sibling of it, else on a sibling of its
        /// parent, and so on up to the scene's root objects. If the first of these levels to have an I18n has more than
        /// one, returns null and sets <paramref name="ambiguousLevel"/> to a description of that level.
        /// </summary>
        private static I18n FindClosest(Transform transform, I18n[] instances, out string ambiguousLevel)
        {
            ambiguousLevel = null;
            Scene scene = transform.gameObject.scene;
            for (Transform node = transform; node != null; node = node.parent)
            {
                Transform level = node.parent;
                I18n found = null;
                int count = 0;
                foreach (I18n i18n in instances)
                {
                    if (i18n.gameObject.scene != scene) continue;
                    if (i18n.transform.parent != level) continue;
                    found = i18n;
                    count++;
                }
                if (count == 1) return found;
                if (count > 1)
                {
                    ambiguousLevel = level != null ? $"the children of \"{GetPath(level)}\"" : $"the root objects of scene \"{scene.name}\"";
                    return null;
                }
            }
            return null;
        }

        /// <summary>
        /// Picks the I18n for objects with no I18n among the siblings of themselves or their parents: the scene's
        /// I18n with Main I18n set (<see cref="ValidateMainI18n"/> ensures there's at most one), else its only I18n
        /// with a warning. Sets a problem if this is ambiguous.
        /// </summary>
        private static SceneFallback FindFallback(Scene scene, I18n[] instances)
        {
            List<I18n> inScene = new List<I18n>();
            I18n main = null;
            foreach (I18n i18n in instances)
            {
                if (i18n.gameObject.scene != scene) continue;
                inScene.Add(i18n);
                if (IsMain(i18n)) main = i18n;
            }

            SceneFallback fallback = new SceneFallback();
            if (main != null)
            {
                fallback.I18n = main;
            }
            else if (inScene.Count == 1)
            {
                fallback.I18n = inScene[0];
                fallback.Warning = $"{GetPath(inScene[0])} is used for objects with no I18n among the siblings of themselves or their parents, but doesn't have Main I18n set.";
            }
            else if (inScene.Count > 1)
            {
                fallback.Problem = $"Some objects in scene \"{scene.name}\" have no I18n among the siblings of themselves or their parents, and none of the scene's {inScene.Count} I18n objects has Main I18n set.";
            }
            return fallback;
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

            ShowProblems($"{consequence} because of problems with language packs:", problems, null);
            return false;
        }

        // A scene may have at most one main I18n, whether or not anything falls back to it.
        private static bool ValidateMainI18n(I18n[] instances, string consequence)
        {
            Dictionary<Scene, List<string>> mainPaths = new Dictionary<Scene, List<string>>();
            foreach (I18n i18n in instances)
            {
                if (!IsMain(i18n)) continue;
                Scene scene = i18n.gameObject.scene;
                if (!mainPaths.TryGetValue(scene, out List<string> paths))
                {
                    paths = new List<string>();
                    mainPaths[scene] = paths;
                }
                paths.Add(GetPath(i18n));
            }

            List<string> problems = new List<string>();
            foreach (KeyValuePair<Scene, List<string>> entry in mainPaths)
            {
                if (entry.Value.Count < 2) continue;
                ReportProblem(problems, $"Scene \"{entry.Key.name}\" has more than one I18n with Main I18n set: {string.Join(", ", entry.Value)}.", null);
            }
            if (problems.Count == 0) return true;

            ShowProblems($"{consequence} because only one I18n per scene may have Main I18n set:", problems, null);
            return false;
        }

        /// <summary>
        /// Points every I18n's <c>main</c> at its scene's main I18n, or null if there is none. Fills the main I18n's
        /// <c>allI18ns</c> with itself followed by the scene's other I18ns in hierarchy order, and empties everyone
        /// else's. These fields are hidden and derived, so they're kept up to date without asking.
        /// </summary>
        private static void LinkMainI18n(I18n[] instances)
        {
            List<I18n> sorted = new List<I18n>(instances);
            sorted.Sort((a, b) => CompareHierarchyOrder(a.transform, b.transform));

            Dictionary<Scene, I18n> mains = new Dictionary<Scene, I18n>();
            foreach (I18n i18n in sorted)
            {
                if (IsMain(i18n)) mains[i18n.gameObject.scene] = i18n;
            }

            HashSet<Scene> warned = new HashSet<Scene>();
            foreach (I18n i18n in sorted)
            {
                Scene scene = i18n.gameObject.scene;
                if (mains.ContainsKey(scene) || !warned.Add(scene)) continue;
                Debug.LogWarning($"[I18n] No I18n in scene \"{scene.name}\" has Main I18n set, so SetLanguageGlobally() won't work. Set Main I18n on one of them.", i18n);
            }

            foreach (I18n i18n in sorted)
            {
                Scene scene = i18n.gameObject.scene;
                mains.TryGetValue(scene, out I18n main);
                List<I18n> all = new List<I18n>();
                if (i18n == main)
                {
                    all.Add(i18n);
                    foreach (I18n other in sorted)
                    {
                        if (other != i18n && other.gameObject.scene == scene) all.Add(other);
                    }
                }

                SerializedObject serializedObject = new SerializedObject(i18n);
                bool changed = false;
                SerializedProperty mainProperty = serializedObject.FindProperty("main");
                if (mainProperty.objectReferenceValue != main)
                {
                    mainProperty.objectReferenceValue = main;
                    changed = true;
                }
                SerializedProperty allProperty = serializedObject.FindProperty("allI18ns");
                if (allProperty.arraySize != all.Count)
                {
                    allProperty.arraySize = all.Count;
                    changed = true;
                }
                for (int i = 0; i < all.Count; i++)
                {
                    SerializedProperty element = allProperty.GetArrayElementAtIndex(i);
                    if (element.objectReferenceValue == all[i]) continue;
                    element.objectReferenceValue = all[i];
                    changed = true;
                }
                if (!changed) continue;

                serializedObject.ApplyModifiedProperties();
                UdonSharpEditorUtility.CopyProxyToUdon(i18n);
                EditorUtility.SetDirty(i18n);
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        private static int CompareHierarchyOrder(Transform a, Transform b)
        {
            List<int> aIndices = GetSiblingIndices(a);
            List<int> bIndices = GetSiblingIndices(b);
            for (int i = 0; i < aIndices.Count && i < bIndices.Count; i++)
            {
                if (aIndices[i] != bIndices[i]) return aIndices[i].CompareTo(bIndices[i]);
            }
            return aIndices.Count.CompareTo(bIndices.Count);
        }

        private static List<int> GetSiblingIndices(Transform transform)
        {
            List<int> indices = new List<int>();
            for (; transform != null; transform = transform.parent)
            {
                indices.Insert(0, transform.GetSiblingIndex());
            }
            return indices;
        }

        private static bool IsMain(I18n i18n)
        {
            return new SerializedObject(i18n).FindProperty("mainI18n").boolValue;
        }

        private static void ShowProblems(string header, List<string> problems, string footer)
        {
            string message = $"{header}\n";
            for (int i = 0; i < problems.Count && i < MaxDialogProblems; i++)
            {
                message += $"\n• {problems[i]}";
            }
            if (problems.Count > MaxDialogProblems)
            {
                message += $"\n\n…and {problems.Count - MaxDialogProblems} more. See the Console for all of them.";
            }
            if (footer != null)
            {
                message += $"\n\n{footer}";
            }
            EditorUtility.DisplayDialog("I18n", message, "OK");
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
