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

// UdonSharp compiles every script in this assembly, but this component is editor-only and never runs in Udon.
#if !COMPILER_UDONSHARP
using UnityEngine;
using VRC.SDKBase;

namespace SiggiUSTools {
    /// <summary>
    /// Marks a GameObject whose I18n fields must not be changed by the I18n sanity checks when building the world or
    /// entering Play Mode. Removed from the world when it is built.
    /// </summary>
    [AddComponentMenu("Siggi USTools/I18n Lock")]
    [DisallowMultipleComponent]
    public class I18nLock : MonoBehaviour, IEditorOnly
    {
    }
}
#endif
