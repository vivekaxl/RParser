using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace R.Net
{
    public static class Primitives
    {

        [Export("is.na")]
        public static object IsNA(Vector vector)
        {
            return vector.Select(x => x == Const.NA || x is double && Double.IsNaN((double)x));
        }


        [Export("is.nan")]
        public static object IsNan(Vector vector)
        {
            return vector.Select(x => x is double && Double.IsNaN((double)x));
        }

        [Export("is.null")]
        public static object IsNull(Vector vector)
        {
            return vector.Select(x => x = null);
        }


        [Export("c")]
        public static object MakeVector(NamedActuals named, params object[] args)
        {
            bool recursive = named.GetLogical("recursive", false);

            var result = new Vector();
            foreach (var arg in args)
            {
                var v = arg as Vector;
                if (v != null)
                    result.AddRange(recursive ? v.Flatten() : v);
                else
                    result.Add(arg);
            }
            return result.Count == 0 ? Const.NULL : result;
        }


        [Export("seq")]
        public static object Seq(NamedActuals named, params object[] args)
        {
            double from = named.GetNumeric("from", args.Length > 1 ? args[0].AsNumeric() : 1.0);
            double to = named.GetNumeric("to", args.Length == 1 ? args[0].AsNumeric() : args.Length > 1 ? args[1].AsNumeric() : 1.0);
            double length_out = named.GetNumeric("length.out", Math.Abs(to - from) + 1);
            double by = named.GetNumeric("by", (to - from) / (length_out - 1));
            if (from > to && by > 0) { throw new EvaluationError("wrong sign in 'by' argument"); }

            var result = new Vector();
            if (from <= to)
                for (double x = from; x <= to; x += by) { result.Add(x); }
            else
                for (double x = from; x >= to; x += by) { result.Add(x); }
            return result;
        }


        [Export("length")]
        public static object Length(Vector v)
        {
            return Vector.Singleton(v == null ? 0 : v.Count);
        }


        [Export("sum")]
        public static object Sum(NamedActuals namedArgs, params object[] args)
        {
            var all = Vector.Concat(args);
            if (namedArgs.GetLogical("na.rm", false)) { all.RemoveAll(x => x == null); }
            else if (all.Contains(null)) { return Vector.NA; }
            return all.Sum(x => x.AsNumeric());
        }


        [Export("mean")]
        public static object Mean(NamedActuals namedArgs, params object[] args)
        {
            var all = Vector.Concat(args);
            if (namedArgs.GetLogical("na.rm", false)) { all.RemoveAll(x => x == null); }
            else if (all.Contains(null)) { return Vector.NA; }
            return all.Average(x => x.AsNumeric());
        }


        [Export("length<-")]
        public static object LengthAssign(Vector vector, Vector sizeVector)
        {
            int newSize = 0;
            var sizeArg = sizeVector.FirstOrDefault();
            if (sizeArg is int) { newSize = (int)sizeArg; }
            else if (sizeArg is double) { newSize = (int)sizeArg.AsNumeric(); } // truncate, don't round
            else { throw new EvaluationError("invalid value"); }

            if (newSize > vector.Count)
            {
                vector.AddRange(new object[newSize - vector.Count]); // null is our NA
            }
            else if (newSize < vector.Count)
            {
                vector.RemoveRange(newSize, vector.Count - newSize);
            }

            return sizeVector;
        }


        private static IEnumerable<int> ComputeIndices(Vector indexingVector, int sourceCount)
        {
            if (indexingVector.All(x => x is bool))
            {
                var result = new Vector();
                for (int i = 0; i < indexingVector.Count; i++)
                {
                    if ((bool)indexingVector[i]) { yield return i; }
                }
            }
            else if (indexingVector.All(x => x is int || x is double))
            {
                var intOnlyVector = indexingVector.Select(x => x is double ? (int)((double)x) : (int)x); // convert any doubles
                var result = new Vector();
                if (intOnlyVector.All(n => (int)n < 0))
                {
                    for (int i = 0; i < sourceCount; i++)
                    {
                        if (!intOnlyVector.Contains(-i - 1))
                        {
                            yield return i;
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < intOnlyVector.Count; i++)
                    {
                        int index = (int)intOnlyVector[i];
                        if (index > 0)
                        {
                            yield return index - 1;
                        }
                        else if (index < 0)
                        {
                            throw new EvaluationError("only 0's may be mixed with negative subscripts");
                        }
                    }
                }
            }
        }


        [Export("[")]
        public static object IndexGet(Vector sourceVector, Vector indexVector)
        {
            int sourceCount = sourceVector.Count;
            var result = new Vector();
            foreach (int index in ComputeIndices(indexVector, sourceCount))
            {
                result.Add(index < sourceVector.Count ? sourceVector[index] : Const.NA);
            }
            return result;
        }


        [Export("[<-")]
        public static object IndexSet(Vector sourceVector, Vector indexingVector, Vector values)
        {
            var indices = ComputeIndices(indexingVector, sourceVector.Count);
            // Grow vector, if necessary
            for (int i = 0, count = indices.Max() - sourceVector.Count + 1; i < count; i++)
            {
                sourceVector.Add(Const.NA);
            }
            // Overwrite the values
            foreach (var index in indices)
            {
                sourceVector[index] = values[index];
            }
            return sourceVector;
        }


        [Export("ls")]
        public static object Ls(Environment env)
        {
            return new Vector(env.Frame.Keys);
        }


        [Export("eval")]
        public static object Eval(Environment env, object arg)
        {
            var evalString = arg as string;
            if (evalString != null)
            {
                var parser = new Parser(evalString);
                parser.Parse();
                var expr = parser.Expressions.FirstOrDefault();
                if (expr == null) { return ""; }
                return expr.Eval(env);
            }
            var evalExpr = arg as Expression;
            if (evalExpr != null)
            {
                return evalExpr.Eval(env);
            }
            throw new NotImplementedException();
        }


        [Export("assign"), ArgCount(2, 2)]
        public static object Assign(Environment env, NamedActuals named, Promise target, Promise source)
        {
            env = env.Enclosure; // assignments are always done in the calling context, not the local frame for the assign call

            env = named.Get<Environment>("envir", env);

            var funCall = target.Expression as FunctionCall; // attribute assignment e.g. dim(x)<-y, length(x)<-y, etc.
            if (funCall != null)
            {
                var funIdent = funCall.Function as Identifier;
                if (funIdent == null) { throw new EvaluationError("invalid function in complex assignment"); }
                var attributeAssignment = env[funIdent + "<-"] as RFunction;
                if (attributeAssignment == null) { throw new EvaluationError("could not find function \"" + funIdent + "<-\""); }
                var attributeAssignmentActuals = new Promise[funCall.Arguments.Count() + 1];
                for (int i = 0; i < funCall.Arguments.Count(); i++)
                {
                    attributeAssignmentActuals[i] = new Promise(target.Environment, funCall.Arguments[i]);
                }
                attributeAssignmentActuals[funCall.Arguments.Count()] = source;
                return attributeAssignment(env, attributeAssignmentActuals, new NamedActuals());
            }

            return env.Assign(target.Expression, source);
        }


        [Export("<-"), Export("=")]
        public static object LeftAssign(Environment env, Promise target, Promise source)
        {
            return Assign(env, new NamedActuals(), target, source);
        }


        [Export("<<-")]
        public static object LeftEnclosureAssign(Environment env, Promise target, Promise source)
        {
            var newArgs = new NamedActuals();
            if (env.Enclosure != null) { newArgs["envir"] = env.Enclosure; }
            return Assign(env, newArgs, target, source);
        }


        
        [Export("->")]
        public static object RightAssign(Environment env, Promise source, Promise target)
        {
            return Assign(env, new NamedActuals(), target, source);
        }


        [Export("->>")]
        public static object RightEnclosureAssign(Environment env, Promise source, Promise target)
        {
            var newArgs = new NamedActuals();
            if (env.Enclosure != null) { newArgs["envir"] = env.Enclosure; }
            return Assign(env, newArgs, target, source);
        }


        private static object UnaryOrBinary(Func<double, double, double> binary, Vector v1, params object[] rest)
        {
            if (rest.Length == 0)
            {
                return Vector.Singleton(0.0).BinaryOperation(v1, binary); // i.e. 0+x or 0-x
            }
            else if (rest.Length == 1 && rest[0] is Vector)
            {
                var v2 = (Vector)rest[0];
                return v1.BinaryOperation(v2, binary);
            }
            else if (rest.Length > 1)
            {
                throw new EvaluationError("operator needs one or two arguments");
            }
            else
            {
                throw new EvaluationError("invalid argument to unary operator");
            }
        }


        [Export("+")]
        public static object Plus(Vector v1, params object[] rest)
        {
            return UnaryOrBinary((a, b) => a + b, v1, rest);
        }

        [Export("-")]
        public static object Minus(Vector v1, params object[] rest)
        {
            return UnaryOrBinary((a, b) => a - b, v1, rest);
        }

        [Export("*")]
        public static object Times(Vector v1, Vector v2)
        {
            return v1.BinaryOperation(v2, (a, b) => a * b);
        }

        [Export("/")]
        public static object Divide(Vector v1, Vector v2)
        {
            return v1.BinaryOperation(v2, (a, b) => a / b);
        }

        [Export("^")]
        public static object Expo(Vector v1, Vector v2)
        {
            return v1.BinaryOperation(v2, (a, b) => Math.Pow(a, b));
        }

        [Export("<")]
        public static object Less(Vector v1, Vector v2)
        {
            return v1.BinaryOperation(v2, (object a, object b) => a.AsNumeric() < b.AsNumeric());
        }

        [Export("<=")]
        public static object LessEq(Vector v1, Vector v2)
        {
            return v1.BinaryOperation(v2, (object a, object b) => a.AsNumeric() <= b.AsNumeric());
        }

        [Export(">")]
        public static object Gr(Vector v1, Vector v2)
        {
            return v1.BinaryOperation(v2, (object a, object b) => a.AsNumeric() > b.AsNumeric());
        }

        [Export(">=")]
        public static object GrEq(Vector v1, Vector v2)
        {
            return v1.BinaryOperation(v2, (object a, object b) => a.AsNumeric() > b.AsNumeric());
        }

        [Export("!")]
        public static object Neg(Vector v)
        {
            return v.UnaryOperation(x => !x.AsLogical());
        }

        [Export("&")]
        public static object AndWise(Vector v1, Vector v2)
        {
            return v1.BinaryOperation(v2, (a, b) => a & b);
        }

        [Export("|")]
        public static object OrWise(Vector v1, Vector v2)
        {
            return v1.BinaryOperation(v2, (a, b) => a | b);
        }

        [Export("xor")]
        public static object XorWise(Vector v1, Vector v2)
        {
            return v1.BinaryOperation(v2, (a, b) => a ^ b);
        }

        [Export("&&")]
        public static object ShortCircuitAnd(Vector v1, Vector v2)
        {
            if (v1.Count == 0) { throw new EvaluationError("invalid 'x' type in 'x && y'"); }
            if (v2.Count == 0) { throw new EvaluationError("invalid 'y' type in 'x && y'"); }
            return Vector.Singleton(v1[0].AsLogical() && v2[0].AsLogical());
        }

        [Export("||")]
        public static object ShortCircuitOr(Vector v1, Vector v2)
        {
            if (v1.Count == 0) { throw new EvaluationError("invalid 'x' type in 'x || y'"); }
            if (v2.Count == 0) { throw new EvaluationError("invalid 'y' type in 'x || y'"); }
            return Vector.Singleton(v1[0].AsLogical() || v2[0].AsLogical());
        }

        [Export("sqrt")]
        public static object SquareRoot(Vector v)
        {
            return v.UnaryOperation(x => Math.Sqrt(x.AsNumeric()));
        }

        [Export("sin")]
        public static object Sin(Vector v)
        {
            return v.UnaryOperation(x => Math.Sin(x.AsNumeric()));
        }

        [Export("cos")]
        public static object Cos(Vector v)
        {
            return v.UnaryOperation(x => Math.Cos(x.AsNumeric()));
        }

        [Export("tan")]
        public static object Tan(Vector v)
        {
            return v.UnaryOperation(x => Math.Tan(x.AsNumeric()));
        }

        [Export("acos")]
        public static object Acos(Vector v)
        {
            return v.UnaryOperation(x => Math.Acos(x.AsNumeric()));
        }

        [Export("asin")]
        public static object Asin(Vector v)
        {
            return v.UnaryOperation(x => Math.Asin(x.AsNumeric()));
        }

        [Export("atan")]
        public static object Atan(Vector v)
        {
            return v.UnaryOperation(x => Math.Atan(x.AsNumeric()));
        }

        [Export("atan2")]
        public static object Atan2(Vector v1, Vector v2)
        {
            return v1.BinaryOperation(v2, (object x, object y) => Math.Atan2(x.AsNumeric(), y.AsNumeric()));
        }

        [Export("if")]
        public static object If(Promise condition, Promise thenPromise, Promise elsePromise)
        {
            return condition.Force().AsLogical() ? thenPromise.Force() : elsePromise.Force();
        }

        [Export("switch")]
        public static object Switch(object indexObj, params object[] options)
        {
            // FIXME: This is wrong because it causes eager evaluation of the options.
            // We need to allow params of promises.
            int index = (int)indexObj.AsNumeric() - 1;
            if (0 <= index && index < options.Length) { return options[index]; }
            return null;
        }

        [Export("repeat")]
        public static object Repeat(Promise body)
        {
        top: try { while (true) body.Eval(); }
            catch (NextInterruption) { goto top; }
            catch (BreakInterruption) { }
            return null;
        }

        [Export("while")]
        public static object While(Promise cond, Promise body)
        {
        top: try { while (cond.Eval().AsLogical()) { body.Eval(); } }
            catch (NextInterruption) { goto top; }
            catch (BreakInterruption) { }
            return null;
        }

        [Export("for")]
        public static object For(Environment env, Vector values, RFunction body)
        {
            env = env.Enclosure;
            foreach (var val in values)
            {
                body(env, new Promise[] { new Promise(env, new Literal(val)) }, new NamedActuals());
            }
            return null;
        }

        [Export("print")]
        public static object Print(NamedActuals named, object value)
        {
            if (value is Promise) { value = ((Promise)value).Force(); }
            Console.WriteLine(value);
            return value;
        }

    }

}
