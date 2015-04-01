using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace R.Net
{

    public class Vector : List<object>, ICloneable
    {
        public Vector() : base() { }
        public Vector(int size) : base() { while (size-- > 0) Add(Const.NA); }
        public Vector(IEnumerable<Task> items) { AddRange(items); }
        public Vector(System.Collections.IEnumerable items) { foreach (var item in items) Add(item); }

        private Vector(object item) : base() { Add(item); }
        public static Vector Singleton(object x) { return new Vector(x); }
        public static Vector NA = Singleton(null);

        public static Vector Concat(params object[] args)
        {
            Vector all = new Vector();
            foreach (var arg in args)
            {
                if (arg is Vector) { all.AddRange((Vector)arg); }
                else all.Add(arg);
            }
            return all;
        }

        public override string ToString()
        {
            var buffer = new StringBuilder();
            foreach (var x in this) { buffer.Append(x.DisplayLikeR()); buffer.Append(" "); }
            return buffer.ToString();
        }


        public new object this[int i]
        {
            get { return base[i % Count]; }
            set { base[i % Count] = value;  }
        }


        public Vector Select(Func<object, object> func)
        {
            var values = ((IEnumerable<object>)this).Select(func);
            return new Vector(values);
        }


        public Vector Flatten()
        {
            ;
            var result = new Vector();
            foreach (var obj in this)
            {
                if (obj is Vector)
                    result.AddRange(((Vector)obj).Flatten());
                else
                    result.Add(obj);
            }
            return result;
        }


        public double GetNumeric(int index)
        {
            if (this[index] is int) { return (double)((int)this[index]); }
            if (this[index] is double) { return (double)this[index]; }
            throw new EvaluationError("non-numeric argument to binary operator");
        }

        public Vector UnaryOperation(Func<object, object> func)
        {
            var result = new Vector(this.Count);
            for (int i = 0; i < result.Count; i++)
            {
                result[i] = this[i] == Const.NA ? Const.NA : func(this[i]);
            }
            return result;
        }

        public Vector BinaryOperation(Vector that, Func<object, object, object> func)
        {
            var result = new Vector(Math.Max(this.Count, that.Count));
            int thisIndex = 0, thatIndex = 0;
            for (int resultIndex = 0; resultIndex < result.Count; resultIndex++)
            {
                result[resultIndex] =
                    (this[thisIndex] == Const.NA || that[thatIndex] == Const.NA) ? Const.NA :
                    func(this[thisIndex], that[thatIndex]);

                thisIndex = (thisIndex + 1) % this.Count;
                thatIndex = (thatIndex + 1) % that.Count;
            }
            return result;
        }

        public Vector BinaryOperation(Vector that, Func<double, double, double> func)
        {
            return this.BinaryOperation(that, (object a, object b) => func(a.AsNumeric(), b.AsNumeric()));
        }

        public Vector BinaryOperation(Vector that, Func<bool, bool, bool> func)
        {
            return this.BinaryOperation(that, (object a, object b) => func(a.AsLogical(), b.AsLogical()));
        }

        public object Clone()
        {
            return new Vector(this);
        }
    }



    class RArray : Vector
    {
    }





    public static class Extensions
    {
        public static string DisplayLikeR(this object x)
        {
            // ToString mostly matches R's formatting, but there are a few exceptions.
            if (x == Const.NA) { return "NA"; }
            if (x == null) { return "NULL"; }
            if (x is bool) { return ((bool)x) ? "TRUE" : "FALSE"; }
            if (x is double && Double.IsInfinity((double)x)) { return "Inf"; }
            return x.ToString();
        }


        public static bool AsLogical(this object x)
        {
            if (x == Const.NA) { throw new EvaluationError("missing value where TRUE/FALSE needed"); }
            if (x == null) { return false; }
            if (x is bool) { return (bool)x; }
            if (x is int) { return ((int)x) != 0; }
            if (x is double) { return ((double)x) != 0; } // round off?
            if (x is Vector && ((Vector)x).Count > 0) { return ((Vector)x)[0].AsLogical(); }
            return x != null;
        }


        public static double AsNumeric(this object x)
        {
            if (x == null) { return 0; }
            if (x is bool) { return (bool)x ? 1 : 0; }
            if (x is int) { return ((int)x); }
            if (x is double) { return ((double)x); }
            if (x is Vector && ((Vector)x).Count > 0) { return ((Vector)x)[0].AsNumeric(); }
            throw new NotImplementedException();
        }
    }


}
