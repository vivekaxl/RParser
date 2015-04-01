using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


using NamedArgument = System.Collections.Generic.KeyValuePair<string, object>;



namespace R.Net
{
    public abstract class Expression
    {
        public abstract object Eval(Environment env);
    }



    public class Literal : Expression
    {
        public static Literal NULL = new Literal(Const.NULL);
        public static Literal NA = new Literal(Const.NA);
        public static Literal NAInteger = new Literal(Const.NAInteger);
        public static Literal NAReal = new Literal(Const.NAReal);
        public static Literal NAComplex = new Literal(Const.NAComplex);
        public static Literal NACharacter = new Literal(Const.NACharacter);
        public static Literal True = new Literal(true);
        public static Literal False = new Literal(false);
        public static Literal Inf = new Literal(Double.PositiveInfinity);
        public static Literal NaN = new Literal(Double.NaN);

        public object Value { get; private set; }
        public Literal(object value) { Value = value; }
        public override object Eval(Environment env) { return Vector.Singleton(Value); } // R treats literals as singleton vectors
        public override string ToString() { return Value.ToString(); }
    }


    public class BreakExpression : Expression
    {
        public override object Eval(Environment env) { throw new BreakInterruption(); }
        public override string ToString() { return "break"; }
    }


    public class NextExpression : Expression
    {
        public override object Eval(Environment env) { throw new NextInterruption(); }
        public override string ToString() { return "next"; }
    }


    public class CompoundExpression : Expression
    {
        public List<Expression> Expressions { get; private set; }
        public CompoundExpression(List<Expression> expressions) { Expressions = expressions; }
        public override string ToString() { return "{" + String.Join(", ", Expressions.Select(e => e.ToString())) + "}"; }

        public override object Eval(Environment env)
        {
            object result = null;
            foreach (var e in Expressions) { result = e.Eval(env); }
            return result;
        }
    }


    public class Identifier : Expression
    {
        public string Name { get; private set; }
        public Identifier(string id) { Name = id; }
        public override string ToString() { return Name; }

        public override object Eval(Environment env)
        {
            var val = env[Name];
            var promise = val as Promise;
            return promise != null ? promise.Force() : val;
        }
    }


    public class QualifiedIdentifier : Expression
    {
        public Expression Namespace { get; private set; }
        public Expression Identifier { get; private set; }
        public QualifiedIdentifier(Expression ns, Expression id) { Namespace = ns; Identifier = id; }
        public override object Eval(Environment env) { return null; }
        public override string ToString() { return Namespace + "::" + Identifier; }
    }


    public class Ellipsis : Identifier
    {
        public Ellipsis() : base("...") { }
        public override object Eval(Environment env) { return null; }
        public override string ToString() { return "..."; }
    }


    public class IdentifierWithDefault : Identifier
    {
        public Expression DefaultValue { get; private set; }
        public IdentifierWithDefault(string name, Expression defaultValue) : base(name) { DefaultValue = defaultValue; }
        public override object Eval(Environment env) { return new NamedArgument(Name, DefaultValue.Eval(env)); }
        public override string ToString() { return Name + "=" + DefaultValue; }
    }


    public class NamedFormals : Expression
    {
        List<string> names = new List<string>();
        List<Expression> expressions = new List<Expression>();

        public void Add(IdentifierWithDefault expr)
        {
            names.Add(expr.Name);
            expressions.Add(expr.DefaultValue);
        }

        public override object Eval(Environment env)
        {
            var result = new NamedActuals();
            for (int i = 0; i < names.Count; i++)
            {
                result[names[i]] = expressions[i].Eval(env);
            }
            return result;
        }
    }


    public class FunctionExpression : Expression
    {
        public List<Identifier> Arguments { get; private set; }
        public Expression Body { get; private set; }

        public FunctionExpression(List<Identifier> arguments, Expression body)
        {
            var used = new HashSet<string>();
            foreach (var id in arguments)
            {
                if (used.Contains(id.Name))
                {
                    throw new EvaluationError("Repeated formal argument '" + id.Name + "'");
                }
                used.Add(id.Name);
            }
            Arguments = arguments; Body = body;
        }

        public FunctionExpression(Identifier argument, Expression body)
            : this(new List<Identifier>(new Identifier[] { argument }), body)
        { }

        public override object Eval(Environment env)
        {
            var namedFormals = Arguments.Where(a => a is IdentifierWithDefault);
            return new RFunction((calleeEnv, promiseActuals, namedActuals) =>
            {
                for (int i = 0; i < promiseActuals.Count; i++)
                {
                    calleeEnv.Assign( Arguments[i], promiseActuals[i]);
                }
                foreach (IdentifierWithDefault namedFormal in namedFormals)
                {
                    object actual;
                    if (calleeEnv.Frame.ContainsKey(namedFormal.Name)) { continue; }
                    if (!namedActuals.TryGetValue(namedFormal.Name, out actual))
                    {
                        actual = namedFormal.DefaultValue.Eval(calleeEnv);
                    }
                    calleeEnv[namedFormal.Name] = actual;
                }
                var result = Body.Eval(calleeEnv);
                if (result is Promise) { result = ((Promise)result).Force(); }
                return result;
            });
        }
    }


    public class FunctionCall : Expression
    {
        public Expression Function { get; private set; }
        public Expression[] Arguments { get; private set; }

        public FunctionCall(Expression function, IList<Expression> arguments) { Function = function; Arguments = arguments.ToArray(); }
        public FunctionCall(string functionName, IList<Expression> arguments) : this(new Identifier(functionName), arguments) { }
        public FunctionCall(string functionName, params Expression[] arguments) : this(functionName, (IList<Expression>)arguments) { }

        public override string ToString()
        {
            var buffer = new StringBuilder();
            buffer.Append(Function.ToString());
            buffer.Append("(");
            for (int i = 0; i < Arguments.Length; i++)
            {
                if (i > 0) buffer.Append(", ");
                buffer.Append(Arguments[i].ToString());
            }
            buffer.Append(")");
            return buffer.ToString();
        }

        public static Expression Quote(Expression e)
        {
            return new FunctionCall(new Identifier("quote"), new List<Expression>(new Expression[] { e }));
        }

        public override object Eval(Environment callingEnv)
        {
            var fun = this.Function.Eval(callingEnv);
            if (fun == null) { throw new EvaluationError("could not find function " + Function); }

            var vecFun = fun as Vector;
            if (vecFun != null && vecFun.Count == 1 && vecFun[0] is string)
            {
                fun = callingEnv[(string)vecFun[0]];
            }

            if (!(fun is RFunction)) { throw new EvaluationError("attempt to apply non-function"); }
            var function = (RFunction)fun;

            var positionals = new List<Promise>();
            var namedFormals = new NamedFormals();
            foreach (var arg in this.Arguments)
            {
                var promise = new Promise(callingEnv, arg);
                if (arg is IdentifierWithDefault)
                {
                    namedFormals.Add((IdentifierWithDefault)arg);
                }
                else
                {
                    positionals.Add(new Promise(callingEnv, arg));
                }
            }

            var calleeEnv = new Environment(callingEnv);
            var namedActuals = (NamedActuals)namedFormals.Eval(calleeEnv);

            return function(calleeEnv, positionals, namedActuals);
        }
    }



    public class Promise
    {
        bool forced = false;
        private object memo;
        public Environment Environment { get; private set; }
        public Expression Expression { get; private set; }
        public Promise(Environment env, Expression expr) { Environment = env; Expression = expr; }

        public object Force()
        {
            if (!forced)
            {
                Eval();
                forced = true;
            }
            return memo;
        }

        public object Eval()
        {
            return memo = Expression.Eval(Environment);
        }
    }



    public class ControlInterruption : Exception { }
    public class BreakInterruption : ControlInterruption { }
    public class NextInterruption : ControlInterruption { }



    public class EvaluationError : Exception
    {
        public EvaluationError(string message) : base(message) { }
    }


}
