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
using VRC.SDKBase.Editor.BuildPipeline;

namespace SiggiUSTools.Editor {
    /// <summary>
    /// When a VRChat world build is requested, asks the user whether to fill every I18n field on the scene's
    /// UdonSharpBehaviours that is null or doesn't point at the scene's I18n with the scene's I18n.
    /// </summary>
    public class I18nBuildLinker : IVRCSDKBuildRequestedCallback
    {
        private const string I18nPropertyType = "PPtr<$" + nameof(I18n) + ">";

        // Run after UdonSharp's own build checks (-1) so scripts are compiled.
        public int callbackOrder => 0;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (requestedBuildType != VRCSDKRequestedBuildType.Scene) return true;

            I18n[] instances = Object.FindObjectsOfType<I18n>(true);
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
                        if (!autofixAllowed) return true;
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

            return true;
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
