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
    using State = Func<ISignal, Response>;

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

        public class Response
        {
            public static Response Handled = new Response(Type.Handled);

            public static Response Parent(State state) => new Response(Type.Parent, state);
            public static Response Transition(State state) => new Response(Type.Transition, state);

            private readonly Type type;

            public readonly State State;

            public bool IsHandled => type == Type.Handled;
            public bool IsParent => type == Type.Parent && State != null;
            public bool IsTransition => type == Type.Transition && State != null;

            private Response(Type type)
            {
                this.type = type;
            }

            private Response(Type type, State state)
            {
                this.type = type;

                State = state;
            }

            private enum Type
            {
                Parent,
                Handled,
                Transition,
            }
        }

        public abstract class StateMachine
        {
            private State current;

            public string CurrentStateName => current.Method.Name;

            public StateMachine()
            {
                current = Initial;

                Handle(Enter.Global);
            }

            public void Handle(ISignal signal)
            {
                Response response;

                var state = current;

                do
                {
                    response = state(signal);

                    if (response.IsParent)
                    {
                        state = response.State;
                    }
                } while (response.IsParent);

                if (response.IsTransition)
                {
                    Transition(response.State);
                }
            }

            private void Transition(State target)
            {
                Response response;

                var targetLiniage = Liniage(target);

                do
                {
                    if (current == target) return;

                    int index = targetLiniage.IndexOf(current);

                    if (index >= 0)
                    {
                        targetLiniage = new List<State>(targetLiniage.Skip(index + 1));

                        break;
                    }

                    response = current(Parent.Global);

                    current(Exit.Global);

                    current = response.State;
                } while (response.IsParent);

                foreach (var state in targetLiniage)
                {
                    state(Enter.Global);
                }

                current = target;
            }

            private List<State> Liniage(State state)
            {
                List<State> stack;

                var response = state(Parent.Global);

                if (response.IsParent)
                {
                    stack = Liniage(response.State);
                }
                else
                {
                    stack = new List<State>();
                }

                stack.Add(state);

                return stack;
            }

            protected abstract Response Initial(ISignal signal);
        }
    }
}
