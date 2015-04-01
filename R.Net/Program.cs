using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace R.Net
{
    class Program
    {
        static void Main(string[] arg)
        {
            if (arg.Length > 0)
            {
                Scanner scanner = new Scanner(arg[0]);
                Parser parser = new Parser(scanner);
                var env = Environment.BaseEnvironment;
                EvalAndPrint(parser, env);
            }
            else
            {
                string input = "";
                var env = Environment.BaseEnvironment;
                while ((input = PromptAndRead()) != null)
                {
                    var parser = new Parser(input);
                    EvalAndPrint(parser, env);
                }
            }
        }

        private static string PromptAndRead()
        {
            Console.Write("> ");
            return Console.ReadLine();
        }

        private static void EvalAndPrint(Parser parser, Environment env)
        {
            parser.Parse();
            if (parser.errors.count == 0)
            {
                foreach (var expr in parser.Expressions)
                {
                    try
                    {
                        var val = expr.Eval(env);
                        Console.WriteLine(val);
                    }
                    catch (ControlInterruption)
                    {
                        Console.WriteLine("no loop for break/next, jumping to top level");
                    }
                    catch (EvaluationError err)
                    {
                        Console.WriteLine(err.Message);
                    }
                }
            }
        }
    }
}
