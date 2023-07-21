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
using static IngameScript.Program;

namespace IngameScript
{
    partial class Program
    {
        public interface ISignal { }

        public abstract class Singleton<T> where T : Singleton<T>, new()
        {
            public static T Global = new T();
        }

        public class Enter : Singleton<Enter>, ISignal { }
        public class Exit : Singleton<Exit>, ISignal { }
        public class Parent : Singleton<Parent>, ISignal { }

        public enum Response
        {
            Handled,
            Unhandled,
            Transition,
        }

        public abstract class StateMachine
        {
            private readonly Dictionary<Func<ISignal, Response>, Func<ISignal, Response>> parents = new Dictionary<Func<ISignal, Response>, Func<ISignal, Response>>();

            private Func<ISignal, Response> current;
            private Func<ISignal, Response> next;

            protected readonly Program program;

            protected Dictionary<string, Action<IMyTextSurface>> surfaceTypes;

            public string CurrentStateName => current.Method.Name;

            public StateMachine(Program program)
            {
                this.program = program;
                
                current = Initial;
                next = null;

                Handle(Enter.Global);
            }

            public void Handle(ISignal signal)
            {
                Response response;

                var state = current;

                SendSignal:
                response = state(signal);

                if (response == Response.Unhandled && parents.ContainsKey(state))
                {
                    state = parents[state];

                    goto SendSignal;
                }

                if (response == Response.Transition)
                {
                    Transition();
                }
            }

            protected void RegisterChild(Func<ISignal, Response> parent, Func<ISignal, Response> child)
            {
                parents[child] = parent;
            }

            protected void RegisterChildren(Func<ISignal, Response> parent, List<Func<ISignal, Response>> children)
            {
                foreach (var child in children)
                {
                    RegisterChild(parent, child);
                }
            }

            protected Response TransitionTo(Func<ISignal, Response> next)
            {
                this.next = next;

                return Response.Transition;
            }

            protected void GetSurfaces(IMyTextSurfaceProvider provider)
            {
                var block = provider as IMyTerminalBlock;
                var ini = new MyIni();

                if (ini.TryParse(block.CustomData))
                {
                    for (int i = 0; i < provider.SurfaceCount; i++)
                    {
                        string value = ini.Get(NAME, $"Surface{i}").ToString();

                        if (surfaceTypes.ContainsKey(value))
                        {
                            surfaceTypes[value].Invoke(provider.GetSurface(i));
                        }
                        else
                        {
                            value = "";
                        }

                        ini.Set(NAME, $"Surface{i}", value);
                    }
                }

                ini.AddSection(NAME);
                ini.SetSectionComment(NAME, $"Options: {string.Join(", ", surfaceTypes.Keys)}");

                block.CustomData = ini.ToString() + ini.EndContent;
            }

            protected void HandleError(Exception error)
            {
                Logger.Log($"Error:\n{error.Message}");

                Info();

                program.Runtime.UpdateFrequency = UpdateFrequency.None;
            }

            private void Transition()
            {
                if (next == null)
                {
                    throw new InvalidOperationException("Please call TransitiionTo passing the next state.");
                }

                var targetLiniage = Liniage(next);

                if (current == next) return;

                while (parents.ContainsKey(current))
                {
                    if (current == next) return;

                    int index = targetLiniage.IndexOf(current);

                    if (index >= 0)
                    {
                        targetLiniage = new List<Func<ISignal, Response>>(targetLiniage.Skip(index + 1));

                        break;
                    }

                    current(Exit.Global);

                    current = parents[current];
                }

                foreach (var state in targetLiniage)
                {
                    state(Enter.Global);
                }

                current = next;
                next = null;
            }

            private List<Func<ISignal, Response>> Liniage(Func<ISignal, Response> state)
            {
                List<Func<ISignal, Response>> stack;

                if (parents.ContainsKey(state))
                {
                    stack = Liniage(parents[state]);
                }
                else
                {
                    stack = new List<Func<ISignal, Response>>();
                }

                stack.Add(state);

                return stack;
            }

            protected abstract Response Initial(ISignal signal);
            protected abstract void Info();
        }
    }
}
