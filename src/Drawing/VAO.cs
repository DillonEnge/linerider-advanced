﻿//
//  VAO.cs
//
//  Author:
//       Noah Ablaseau <nablaseau@hotmail.com>
//
//  Copyright (c) 2017 
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.

using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using linerider.Rendering;

namespace linerider.Drawing
{
    public class VAO
    {
        #region Fields

        public readonly bool Indexed = false;
        private int[] indices = null;
        public int Texture = 0;
        public bool SetBlend = true;
        public bool Opacity = false;
        private GenericVertex[] vertices = null;
        private int vCount = 0;
        private int iCount = 0;
        private List<byte> alphas = new List<byte>();
        private float _opacity = 1.0f;
        #endregion Fields

        #region Constructors

        public VAO(bool indexed, bool useopacity, int capacity = 500)
        {
            Opacity = useopacity;
            Indexed = indexed;
            vertices = new GenericVertex[capacity];
            indices = new int[capacity];
        }

        #endregion Constructors

        #region Methods

        public void EnsureVertexSize(int size)
        {
            if (size >= vertices.Length)
            {
                Array.Resize(ref vertices, size + (size / 2));
            }
        }
        public void EnsureIndexSize(int size)
        {
            if (Indexed)
            {
                if (size >= indices.Length)
                {
                    Array.Resize(ref indices, size + (size / 2));
                }
            }
        }

        public void SetOpacity(float opacity)
        {
            if (!Opacity)
                throw new InvalidOperationException("Opacity isnt supported in this vao");
            if (_opacity == opacity)
                return;
            if (alphas == null || alphas.Count == 0)
            {
                alphas = new List<byte>(vCount);
                for (int i = 0; i < vCount; i++)
                {
                    alphas.Add(vertices[i].a);
                }
            }
            for (int i = 0; i < vCount; i++)
            {
                var v = vertices[i];
                v.a = (byte)(Math.Min(255, alphas[i] * opacity));
                vertices[i] = v;
            }
            _opacity = opacity;
        }

        public void SetVertex(int index, GenericVertex v)
        {
            vertices[index] = v;
            if (Opacity)
            {
                alphas[index] = v.a;
            }
        }
        public void SetIndex(int index, int index2)
        {
            indices[index] = index2;
        }

        public int GetIndex(int index)
        {
            return indices[index];
        }
        public void SetIndices(List<int> ind)
        {
            if (!Indexed)
                throw new InvalidOperationException("Non indexed VAO");
            if (ind == null)
                ind = new List<int>();
            iCount = 0;
            indices = ind.ToArray();

        }

        public void AddIndex(int index)
        {
            if (!Indexed)
                throw new InvalidOperationException("Non indexed VAO");
            EnsureIndexSize(iCount + 1);
            indices[iCount] = index;
            iCount++;
        }
        public void AddVerticies(List<GenericVertex> v)
        {
            EnsureVertexSize(vCount + v.Count);

            for (int i = 0; i < v.Count; i++)
            {
                vertices[vCount + i] = v[i];
                if (Opacity)
                    alphas.Add(v[i].a);
            }

            vCount += v.Count;

        }

        public int AddVertex(GenericVertex v)
        {
            EnsureVertexSize(vCount + 1);

            vertices[vCount] = v;

            if (Opacity)
                alphas.Add(v.a);
            return vCount++;

        }

        public void ClearIndices()
        {
            iCount = 0;
        }
        public void Clear()
        {
            if (Opacity)
                alphas.Clear();
            if (vertices.Length > 102 * 1500)
            {
                vertices = new GenericVertex[102 * 1500];
            }
            if (indices.Length > 102 * 1500)
            {
                indices = new int[102 * 1500];
            }
            vCount = 0;
            iCount = 0;

        }

        public void Draw(PrimitiveType mode)
        {
            using (new GLEnableCap(EnableCap.Texture2D))
            using (new GLEnableCap(EnableCap.Blend))
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
                GL.EnableClientState(ArrayCap.VertexArray);
                GL.EnableClientState(ArrayCap.ColorArray);
                GL.EnableClientState(ArrayCap.TextureCoordArray);
                if (Texture != 0)
                {
                    GL.BindTexture(TextureTarget.Texture2D, Texture);
                }
                if (vCount != 0 && (!Indexed || iCount != 0))
                {
                    unsafe
                    {
                        fixed (float* ptr1 = &vertices[0].Position.X)
                        fixed (byte* ptr2 = &vertices[0].r)
                        fixed (float* ptr3 = &vertices[0].u)
                        {
                            GL.VertexPointer(2, VertexPointerType.Float, GenericVertex.Size, (IntPtr)ptr1);
                            GL.ColorPointer(4, ColorPointerType.UnsignedByte, GenericVertex.Size, (IntPtr)ptr2);
                            GL.TexCoordPointer(2, TexCoordPointerType.Float, GenericVertex.Size, (IntPtr)ptr3);

                            if (Indexed)
                            {
                                fixed (int* ptr4 = &indices[0])
                                {
                                    GL.DrawElements(mode, iCount, DrawElementsType.UnsignedInt, (IntPtr)ptr4);
                                }
                            }
                            else
                            {
                                GL.DrawArrays(mode, 0, vCount);
                            }
                        }
                    }
                }
                if (Texture != 0)
                {
                    GL.BindTexture(TextureTarget.Texture2D, 0);
                }
                GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);

                GL.DisableClientState(ArrayCap.TextureCoordArray);
                GL.DisableClientState(ArrayCap.ColorArray);
                GL.DisableClientState(ArrayCap.VertexArray);
            }
        }

        #endregion Methods
    }
}