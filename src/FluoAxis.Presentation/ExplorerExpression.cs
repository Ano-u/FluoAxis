namespace FluoAxis.Presentation;

using System.Globalization;

/// <summary>Resource-bounded row expression DSL. No scripts, reflection, member access, IO, or compiled user code.</summary>
public sealed class ExplorerExpression
{
    private readonly Node root;
    private ExplorerExpression(Node root) { this.root = root; }
    public static ExplorerExpression Parse(string expression, IReadOnlySet<string> columns)
    {
        ArgumentNullException.ThrowIfNull(expression); ArgumentNullException.ThrowIfNull(columns);
        if (expression.Length is < 1 or > 1024) throw new ArgumentException("公式长度须为 1–1024 字符。");
        var parser = new Parser(expression, columns);
        return new ExplorerExpression(parser.Read());
    }
    public double? Evaluate(IReadOnlyDictionary<string, double?[]> columns, int row, ref int remainingOperations)
    {
        double value = root.Evaluate(columns, row, ref remainingOperations);
        return double.IsFinite(value) ? value : null;
    }

    private sealed record Node(string Kind, double Constant, Node[] Children)
    {
        public int Depth { get; } = Children.Length == 0 ? 1 : 1 + Children.Max(child => child.Depth);
        public double Evaluate(IReadOnlyDictionary<string, double?[]> columns, int row, ref int budget)
        {
            if (--budget < 0) throw new InvalidOperationException("EXPLORER_EXPRESSION_BUDGET_EXCEEDED");
            if (Kind == "number") return Constant;
            if (Kind.StartsWith('$')) return columns[Kind[1..]][row] ?? double.NaN;
            Span<double> arguments = stackalloc double[16];
            for (int i = 0; i < Children.Length; i++)
            {
                arguments[i] = Children[i].Evaluate(columns, row, ref budget);
                if (!double.IsFinite(arguments[i])) return double.NaN;
            }
            double a = arguments[0]; double b = Children.Length > 1 ? arguments[1] : 0;
            return Kind switch
            {
                "+" => a + b, "-" => a - b, "*" => a * b, "/" => b == 0 ? double.NaN : a / b,
                "^" or "pow" => Math.Pow(a, b), "negate" => -a, "sqrt" => Math.Sqrt(a), "abs" => Math.Abs(a),
                "ln" => Math.Log(a), "log" => Math.Log10(a), "exp" => Math.Exp(a),
                "min" => Minimum(arguments[..Children.Length]), "max" => Maximum(arguments[..Children.Length]),
                "mean" => Average(arguments[..Children.Length]), "median" => Median(arguments[..Children.Length]),
                "sd" => StandardDeviation(arguments[..Children.Length]), _ => double.NaN,
            };
        }
        private static double Minimum(ReadOnlySpan<double> values) { double result = values[0]; foreach (double value in values) result = Math.Min(result, value); return result; }
        private static double Maximum(ReadOnlySpan<double> values) { double result = values[0]; foreach (double value in values) result = Math.Max(result, value); return result; }
        private static double Average(ReadOnlySpan<double> values) { double sum = 0; foreach (double value in values) sum += value / values.Length; return sum; }
        private static double Median(Span<double> values) { values.Sort(); int middle = values.Length / 2; return values.Length % 2 == 1 ? values[middle] : values[middle - 1] / 2 + values[middle] / 2; }
        private static double StandardDeviation(ReadOnlySpan<double> values)
        {
            if (values.Length < 2) return double.NaN;
            double average = Average(values); double sum = 0;
            foreach (double value in values) { double delta = value - average; sum += delta * delta; }
            return Math.Sqrt(sum / (values.Length - 1));
        }
    }

    private sealed class Parser(string text, IReadOnlySet<string> columns)
    {
        private int position;
        private int nodes;
        private int depth;
        public Node Read() { Node result = Expression(0); Skip(); if (position != text.Length) throw Error(); return result; }
        private Node Expression(int precedence)
        {
            if (++depth > 16) throw new ArgumentException("公式嵌套超过 16 层。");
            Node left;
            Skip();
            if (Take('+')) left = Expression(3);
            else if (Take('-')) left = Make("negate", 0, [Expression(3)]);
            else if (Take('(')) { left = Expression(0); Require(')'); }
            else if (position < text.Length && (char.IsAsciiDigit(text[position]) || text[position] == '.')) left = Number();
            else left = Identifier();
            while (true)
            {
                Skip(); if (position >= text.Length) break;
                char op = text[position]; int priority = op switch { '+' or '-' => 1, '*' or '/' => 2, '^' => 3, _ => -1 };
                if (priority < precedence) break;
                position++;
                Node right = Expression(op == '^' ? priority : priority + 1);
                left = Make(op.ToString(), 0, [left, right]);
            }
            depth--; return left;
        }
        private Node Number()
        {
            int start = position;
            while (position < text.Length && (char.IsAsciiDigit(text[position]) || text[position] == '.')) position++;
            if (position < text.Length && text[position] is 'e' or 'E')
            {
                position++; if (position < text.Length && text[position] is '+' or '-') position++;
                while (position < text.Length && char.IsAsciiDigit(text[position])) position++;
            }
            if (!double.TryParse(text.AsSpan(start, position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number)) throw Error();
            return Make("number", number, []);
        }
        private Node Identifier()
        {
            int start = position;
            if (position >= text.Length || !char.IsAsciiLetter(text[position])) throw Error();
            while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] == '_')) position++;
            string name = text[start..position]; Skip();
            if (!Take('('))
            {
                if (!columns.Contains(name)) throw new ArgumentException("公式引用了未知列。");
                return Make('$' + name, 0, []);
            }
            if (name is not ("sqrt" or "abs" or "min" or "max" or "mean" or "median" or "sd" or "log" or "ln" or "exp" or "pow")) throw new ArgumentException("公式包含未批准的函数。");
            var arguments = new List<Node>();
            do { if (arguments.Count == 16) throw Error(); arguments.Add(Expression(0)); } while (Take(','));
            Require(')');
            int count = arguments.Count;
            if ((name is "sqrt" or "abs" or "log" or "ln" or "exp") && count != 1 || name == "pow" && count != 2 || name == "sd" && count < 2) throw Error();
            return Make(name, 0, arguments.ToArray());
        }
        private Node Make(string kind, double value, Node[] children)
        {
            if (++nodes > 128) throw new ArgumentException("公式节点超过 128。");
            var node = new Node(kind, value, children);
            if (node.Depth > 16) throw new ArgumentException("公式计算树超过 16 层。");
            return node;
        }
        private void Skip() { while (position < text.Length && char.IsWhiteSpace(text[position])) position++; }
        private bool Take(char ch) { Skip(); if (position >= text.Length || text[position] != ch) return false; position++; return true; }
        private void Require(char ch) { if (!Take(ch)) throw Error(); }
        private static ArgumentException Error() => new("公式语法无效。只支持数值、列标识符、白名单函数和 + - * / ^ ( )。");
    }
}
