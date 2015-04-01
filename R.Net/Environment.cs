using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace R.Net
{


    public static class Const
    {
        // FIXME: Figure out later if it's a problem that all these are the same value.
        public static readonly object NULL = null;
        public static readonly object NA = new object();
        public static readonly object NAInteger = new object();
        public static readonly object NAReal = new object();
        public static readonly object NAComplex = new object();
        public static readonly object NACharacter = new object();
    }


    public delegate object RFunction(Environment env, IList<Promise> positionalArguments, NamedActuals namedActuals);

    public delegate RFunction MarshalledFunction(string name);


    public class Frame : Dictionary<string, object> { }


    public class NamedActuals : Dictionary<string, object>
    {
        private T Get<T>(string name, T valueOtherwise, Func<object, T> coerce)
        {
            object value;
            if (this.TryGetValue(name, out value))
            {
                return coerce(value);
            }
            return valueOtherwise;
        }

        public T Get<T>(string name, T valueOtherwise) where T : class
        {
            return Get<T>(name, valueOtherwise, x => x is T ? x as T : valueOtherwise);
        }

        public double GetNumeric(string name, double valueOtherwise)
        {
            return Get(name, valueOtherwise, Util.AsDouble);
        }

        public int GetInteger(string name, int valueOtherwise)
        {
            return Get(name, valueOtherwise, Util.AsInt);
        }

        public bool GetLogical(string name, bool valueOtherwise)
        {
            return Get(name, valueOtherwise, Util.AsBool);
        }
    }




    public class Util
    {
        public static double AsDouble(object obj)
        {
            if (obj is double) { return (double)obj; }
            if (obj is int) { return (double)(int)obj; }
            if (obj is bool) { return (bool)obj ? 1.0 : 0.0; }
            throw new EvaluationError("non-numeric");
        }

        public static int AsInt(object obj)
        {
            if (obj is double) { return (int)(double)obj; }
            if (obj is int) { return (int)obj; }
            if (obj is bool) { return (bool)obj ? 1 : 0; }
            throw new EvaluationError("non-integer");
        }

        public static bool AsBool(object obj)
        {
            if (obj is double) { return (double)obj > 0.0; }
            if (obj is int) { return (int)obj > 0; }
            if (obj is bool) { return (bool)obj; }
            throw new EvaluationError("non-logical");
        }
    }


    public class Environment
    {
        public Frame Frame { get; private set; }
        public Environment Enclosure { get; private set; }

        private Environment() { this.Frame = new Frame(); }
        public Environment(Environment enclosure) : this() { this.Enclosure = enclosure; }

        public object this[string symbol]
        {
            get
            {
                object result = null;
                if (Frame.TryGetValue(symbol, out result))
                {
                    return result;
                }
                if (Enclosure != null)
                {
                    return Enclosure[symbol];
                }
                throw new EvaluationError("object " + symbol + " not found");
            }

            set { Frame[symbol] = value; }
        }


        public object Assign(Expression target, Promise source)
        {
            var sourceValue = source.Force();
            if (sourceValue is ICloneable) { sourceValue = ((ICloneable)sourceValue).Clone(); }

            var ident = target as Identifier;
            if (ident != null)
            {
                this[ident.Name] = sourceValue;
            }
            else
            {
                var targetLiteral = target as Literal;
                if (targetLiteral != null)
                {
                    var targetString = targetLiteral.Eval(this) as string;
                    // Because the targetString appears in quote(...), it's still got quotes around it
                    targetString = targetString.Substring(1, targetString.Length - 2);
                    this[targetString] = sourceValue;
                    return sourceValue;
                }
            }

            return sourceValue;
        }


        public static Environment BaseEnvironment
        {
            get
            {
                var baseEnv = new Environment();

                baseEnv["letters"] = new Vector(new string[] { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z" });
                baseEnv["LETTERS"] = new Vector(new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z" });
                baseEnv["month.abb"] = new Vector(new string[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" });
                baseEnv["month.name"] = new Vector(new string[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" });
                baseEnv["pi"] = Vector.Singleton(Math.PI);

                // Preload the environment with all the methods defined in Primitives
                //
                foreach (var method in typeof(Primitives).GetMethods())
                {
                    if (!method.IsPublic) { continue; }

                    var attributes = method.GetCustomAttributes(false);
                    if (!attributes.Any(a => Equals(a.GetType().Name, "ExportAttribute"))) { continue; }

                    var names = attributes.Where(a => a is ExportAttribute).Select(a => ((ExportAttribute)a).Name).ToArray();
                    if (names.Length == 0) { names = new string[] { method.Name }; }

                    var marshalled = MarshallAsRFunction(method);

                    foreach (string name in names)
                    {
                        baseEnv[name] = marshalled(name);
                    }
                }
                return baseEnv;
            }
        }


        private static bool IsReferenceType(Type paramType)
        {
            return paramType.IsClass || paramType.IsInterface || paramType.IsPointer;
        }


        private static MarshalledFunction MarshallAsRFunction(System.Reflection.MethodInfo method)
        {
            var csParameters = method.GetParameters();
            var stepsToPrepareActuals = new List<Action<List<object>, string, Environment, IList<Promise>, NamedActuals>>();
            int expectedArgCount = 0;
            for (int i = 0; i < csParameters.Length; i++)
            {
                var param = csParameters[i];
                var formalType = param.ParameterType;

                if (Equals(formalType.FullName, "R.Net.Environment"))
                {
                    stepsToPrepareActuals.Add((actuals, name, env, args, namedArgs) => actuals.Add(env));
                }
                else if (Equals(formalType.FullName, "R.Net.NamedActuals"))
                {
                    stepsToPrepareActuals.Add((actuals, name, env, args, namedArgs) => actuals.Add(namedArgs));
                }
                else // the R program is expected to supply this parameter
                {
                    var isMarkedParams = param.CustomAttributes.Any(a => Equals(a.AttributeType.Name, "ParamArrayAttribute"));
                    if (isMarkedParams)
                    {
                        var start = expectedArgCount; // make a copy of this int to capture in the closure
                        stepsToPrepareActuals.Add((actuals, name, env, args, namedArgs) =>
                        {
                            var rest = new List<object>();
                            for (int r = start; r < args.Count; r++) { rest.Add(args[r].Force()); }
                            actuals.Add(rest.ToArray());
                        });
                        expectedArgCount = -1;
                    }
                    else
                    {
                        var index = expectedArgCount; // make a copy of this int to capture in the closure
                        stepsToPrepareActuals.Add((actuals, name, env, args, namedArgs) =>
                        {
                            if (index < args.Count)
                            {
                                var promise = args[index];
                                var actual = IsPromiseType(formalType) ? promise : promise.Force();

                                if (actual == null && IsReferenceType(formalType) ||
                                    actual != null && formalType.IsAssignableFrom(actual.GetType()))
                                {
                                    actuals.Add(actual);
                                }
                                else
                                {
                                    throw new EvaluationError(
                                        String.Format("invalid argument {0} passed to {1}", index, name));
                                }
                            }
                        });
                        expectedArgCount++;
                    }
                }
            }
            if (expectedArgCount >= 0)
            {
                stepsToPrepareActuals.Add((actuals, name, env, args, namedArgs) =>
                {
                    if (actuals.Count != csParameters.Length)
                    {
                        throw new EvaluationError(
                                String.Format("{0} arguments passed to {1} which requires {2}",
                                    args.Count, name, expectedArgCount));
                    }
                });
            }

            return new MarshalledFunction(name => new RFunction((env, args, namedArgs) =>
            {
                var actuals = new List<object>();
                foreach (var step in stepsToPrepareActuals)
                {
                    step(actuals, name, env, args, namedArgs);
                }

                try
                {
                    return method.Invoke(null, actuals.ToArray());
                }
                catch (Exception e)
                {
                    throw e.InnerException;
                }
            }));
        }

        private static bool IsPromiseType(Type type)
        {
            return type.FullName.Equals("R.Net.Promise");
        }
    }



    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class ExportAttribute : Attribute
    {
        public string Name { get; private set; }
        public ExportAttribute(string name) { Name = name; }
    }


    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class ArgCountAttribute : Attribute
    {
        public int MinCount { get; private set; }
        public int MaxCount { get; private set; }
        public ArgCountAttribute(int minCount) { MinCount = minCount; MaxCount = Int32.MaxValue; }
        public ArgCountAttribute(int minCount, int maxCount) { MinCount = minCount; MaxCount = maxCount; }
        public bool Okay(IList<object> args) { return MinCount <= args.Count && args.Count <= MaxCount; }

        public override string ToString()
        {
            return
                MaxCount == Int32.MaxValue ? "at least " + MinCount :
                MaxCount == MinCount ? MinCount.ToString() :
                "between " + MinCount + " and " + MaxCount;
        }
    }


    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class ArgTypeAttribute : Attribute
    {
        public int Index { get; private set; }
        public Type Type { get; private set; }

        public ArgTypeAttribute(int index, Type type) { Index = index; Type = type; }

        public bool Okay(IList<object> args)
        {
            return
                Index < args.Count &&
                (args[Index] == null || Type.IsAssignableFrom(args[Index].GetType()));
        }
    }


}
