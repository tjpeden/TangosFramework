using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        class RingBuffer<T> : IEnumerable<T>
        {
            private readonly T[] _buffer;

            private int _size;
            private int _start;
            private int _end;

            public RingBuffer(int capacity)
            {
                _buffer = new T[capacity];
                Clear();
            }

            public int Capacity
            {
                get
                {
                    return _buffer.Length;
                }
            }

            public int Size
            {
                get
                {
                    return _size;
                }
            }

            public bool IsFull
            {
                get
                {
                    return Size == Capacity;
                }
            }

            public bool IsEmpty
            {
                get
                {
                    return Size == 0;
                }
            }

            public void Add(T item)
            {
                _buffer[_end] = item;
                _end = (_end + 1) % Capacity;

                if (IsFull)
                {
                    _start = _end;
                }
                else
                {
                    ++_size;
                }
            }

            public void Clear()
            {
                _size = 0;
                _start = 0;
                _end = 0;
            }

            public IEnumerator<T> GetEnumerator()
            {
                if (!IsEmpty)
                {
                    int startSegmentLength = _start < _end ? _end - _start : Capacity - _start;

                    for (int i = 0; i < startSegmentLength; i++)
                    {
                        yield return _buffer[_start + i];
                    }

                    int endSegmentLength = _start < _end ? 0 : _end;

                    for (int i = 0; i < endSegmentLength; i++)
                    {
                        yield return _buffer[i];
                    }
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
